// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Meltype.Composition;

/// <summary>
/// Mozc (Google 日本語入力のオープンソース版、BSD-3-Clause) の変換エンジン。
/// 同梱の変換ヘルパー (meltype_mozc_helper) を別のプロセスで動かし、標準入出力で変換を頼む。
///
/// やり取り (UTF-8、1 行ずつ) は native/mozc/meltype_mozc_helper.cc の先頭に書いてある。
/// ヘルパーが無い・起動できない・応答しないときは null を返す (呼び出し側はかなのまま見せる)。
/// </summary>
public sealed class MozcConverter : IKanjiConverter, ILearningConverter, IDisposable
{
    private const char UnitSeparator = '\x1f';
    private const char RecordSeparator = '\x1e';
    private const int TimeoutMs = 1500;
    private const int StartTimeoutMs = 10000;

    private readonly string _helperPath;
    private readonly string? _profileDirectory;
    // ヘルパーとのやり取りの順番を守る。応答を待つ間も持つので、入力スレッドでは取らない
    private readonly object _gate = new();
    // 変換の結果の控えと、その回数を守る。ヘルパーの応答を待つ間は持たない (_gate の中で取ってよい。逆は取らない)
    private readonly object _resultsGate = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _candidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Reading, IReadOnlyList<string> Candidates)>?> _conversions = new(StringComparer.Ordinal);
    private readonly LatestRequestWorker<(string, string)> _worker;
    private Process? _process;
    private volatile int _failures;

    // 性能の計測用の回数と時間 (--perf)。ロックの中で数えるだけなので、ふだんの入力は遅くならない
    private long _requests;
    private long _cacheHits;
    private long _candidateRequests;
    private long _candidateCacheHits;
    private long _helperCalls;
    private long _helperTicks;

    // 要る DLL が無くて起動できない (Visual C++ のランタイムが無いなど)。起動し直しても同じなので、もう試さない
    private volatile bool _missingDependency;

    // 終了した。終了のあとで裏の処理 (覚えさせる・WarmUp) が動いても、ヘルパーを起動し直さない
    private volatile bool _disposed;

    // Windows: 子プロセスを起動するあいだ、DLL が見つからないときのエラーの画面を出さない (子プロセスはこの設定を引き継ぐ)。
    // 子プロセスが引き継ぐのはプロセス全体の設定だけなので、SetThreadErrorMode では効かない。起動したらすぐ戻す
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();

    // STATUS_DLL_NOT_FOUND / STATUS_ENTRYPOINT_NOT_FOUND / STATUS_INVALID_IMAGE_FORMAT (起動のときに DLL を読み込めなかった)
    private static bool IsLoaderFailure(int exitCode) => exitCode is unchecked((int)0xC0000135) or unchecked((int)0xC0000139) or unchecked((int)0xC000007B);

    /// <param name="helperPath">meltype_mozc_helper の実行ファイル。</param>
    /// <param name="profileDirectory">Mozc の学習データの保存先 (null なら Mozc の既定)。</param>
    public MozcConverter(string helperPath, string? profileDirectory)
    {
        _helperPath = helperPath;
        _profileDirectory = profileDirectory;
        _worker = new LatestRequestWorker<(string, string)>(ProcessBackground);
    }

    public ConversionBudget Budget { get; } = new();

    public event Action? ResultReady;

    /// <summary>ヘルパーがあるか (起動できるかは使ってみるまで分からない)。</summary>
    public bool IsInstalled => File.Exists(_helperPath);

    /// <summary>使えるか。何度も失敗したら諦める。</summary>
    public bool IsAvailable => IsInstalled && _failures < 3 && !_missingDependency;

    public string? Convert(string hiragana) =>
        ConvertClauses(hiragana) is { Count: > 0 } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
        ConvertWithCandidates(hiragana, context)?.Select(s => s.Clause).ToList();

    /// <summary>
    /// ConvertClauses と同じ文節と、この変換で受け取った文節ごとの候補。
    /// 候補の控え (Candidates) は、ほかの変換や覚えさせたときに捨てられることがあるので使わない。
    /// </summary>
    internal IReadOnlyList<(ConversionClause Clause, IReadOnlyList<string> Candidates)>? ConvertWithCandidates(string hiragana, string? context = null)
    {
        if (Query(context ?? "", hiragana) is not { Count: > 0 } segments) return null;
        return segments.Select(s => (new ConversionClause(s.Reading, (s.Candidates.Count > 0 ? s.Candidates[0] : null) ?? s.Reading), s.Candidates)).ToList();
    }

    /// <summary>読みの変換候補 (多い順)。直前の変換で同じ読みの文節があればその候補、無ければその読みだけで変換する。</summary>
    public IReadOnlyList<string> Candidates(string reading)
    {
        lock (_resultsGate)
        {
            _candidateRequests++;
            if (_candidates.TryGetValue(reading, out var cached))
            {
                _candidateCacheHits++;
                return cached;
            }
        }
        var segments = Query("", reading);
        if (segments is null) return [];
        // 1 文節ならその候補。複数の文節に分かれたなら、最初の候補をつなげたものだけ。
        return segments.Count == 1 ? segments[0].Candidates : [string.Concat(segments.Select(s => (s.Candidates.Count > 0 ? s.Candidates[0] : null) ?? s.Reading))];
    }

    // 学習データを保存する間隔 (覚えさせた回数)。Meltype を終了するときにも保存する。
    private const int SaveEvery = 5;
    private int _learnedSinceSave;

    /// <summary>確定した文節を Mozc に覚えさせる (Mozc の候補に無い文字列の文節があれば覚えない)。</summary>
    public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
    {
        if (clauses.Count == 0 || !IsAvailable) return;
        var records = string.Join(RecordSeparator, clauses.Select(c => Clean(c.Reading).Replace(UnitSeparator, ' ') + UnitSeparator + Clean(c.Text).Replace(UnitSeparator, ' ')));
        lock (_gate)
        {
            if (_disposed) return;
            // 覚えさせると候補の順番が変わるので、候補の控えを捨てる。
            ClearCache();
            var result = Send($"L\t{Clean(context ?? "")}\t{records}");
            if (result == "OK" && ++_learnedSinceSave >= SaveEvery)
            {
                _learnedSinceSave = 0;
                Send("S");
            }
        }
    }

    /// <summary>
    /// 文字列 (漢字かな混じり) の読み (ひらがな)。逆変換できなければ null。
    /// 再変換とユーザー辞書の登録で呼ぶ。キャッシュせず、ヘルパーの応答を待つ。
    /// </summary>
    public string? Reading(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsAvailable) return null;
        lock (_gate)
        {
            if (_disposed) return null;
            return Send($"R\t{Clean(text)}") is { Length: > 0 } reading ? reading : null;
        }
    }

    /// <summary>1 行送って 1 行受け取る (_gate の中で呼ぶ)。失敗したら・起動しているあいだは null。</summary>
    private string? Send(string request)
    {
        try
        {
            var started = Stopwatch.GetTimestamp();
            var process = Start();

            process.StandardInput.Write(request + "\n");
            process.StandardInput.Flush();
            var line = ReadLine(process, TimeoutMs);
            _helperCalls++;
            _helperTicks += Stopwatch.GetTimestamp() - started;

            if (line is null)
            {
                Diagnostics.Log.Warn($"Mozc が応答しませんでした ({TimeoutMs}ms)。");
                Fail();
                return null;
            }

            _failures = 0;
            return line;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"Mozc を使えませんでした: {ex.Message}");
            Fail();
            return null;
        }
    }

    /// <summary>入力スレッドからの問い合わせ。Budget を使っているときは、キャッシュに無ければワーカーに頼み、Budget の間だけ待つ。</summary>
    private List<(string Reading, IReadOnlyList<string> Candidates)>? Query(string context, string reading)
    {
        if (string.IsNullOrEmpty(reading) || !IsAvailable) return null;
        var request = (Clean(context), Clean(reading));
        if (TryCached(request, out var result)) return result;
        _worker.Submit(request);
        return _worker.Wait(request, Budget.RemainingMs) && TryCached(request, out result) ? result : null;
    }

    private bool TryCached((string Context, string Reading) request, out List<(string Reading, IReadOnlyList<string> Candidates)>? result)
    {
        result = null;
        if (_disposed) return false;
        lock (_resultsGate)
        {
            if (!_conversions.TryGetValue(request.Context + "\t" + request.Reading, out result)) return false;
            _requests++;
            _cacheHits++;
            foreach (var (segmentReading, candidates) in result ?? []) _candidates[segmentReading] = candidates;
            return true;
        }
    }

    private void ProcessBackground((string Context, string Reading) request)
    {
        // 読みが空の要求は WarmUp。起動だけして知らせる
        if (request.Reading.Length == 0)
        {
            if (StartNow()) ResultReady?.Invoke();
            return;
        }
        Request(request.Context, request.Reading);
        // 失敗 (キャッシュに入らない) は知らせない。知らせると再描画がまた同じ要求を出して、失敗のたびに繰り返す
        bool cached;
        lock (_resultsGate) cached = _conversions.ContainsKey(request.Context + "\t" + request.Reading);
        if (cached) ResultReady?.Invoke();
    }

    private bool StartNow()
    {
        lock (_gate)
        {
            if (_disposed || !IsAvailable) return false;
            try
            {
                Start();
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"Mozc を使えませんでした: {ex.Message}");
                Fail();
                return false;
            }
        }
    }

    private List<(string Reading, IReadOnlyList<string> Candidates)>? Request(string context, string reading)
    {
        if (string.IsNullOrEmpty(reading) || !IsAvailable) return null;
        context = Clean(context);
        reading = Clean(reading);
        lock (_gate)
        {
            if (_disposed) return null;
            // ライブ変換は 1 キーごとに同じ読みを何度も変換し直すので、同じ頼み方の結果は使い回す (覚えさせたら捨てる)。
            var key = context + "\t" + reading;
            lock (_resultsGate)
            {
                _requests++;
                if (_conversions.TryGetValue(key, out var known))
                {
                    _cacheHits++;
                    // 候補の控えは「直前の変換」のものを使うので、使い回したときも控えを入れ直す
                    foreach (var (segmentReading, candidates) in known ?? []) _candidates[segmentReading] = candidates;
                    return known;
                }
            }
            try
            {
                var line = Send($"C\t{context}\t{reading}");
                if (line is null) return null;
                if (line.Length == 0) return Remember(key, null);
                var segments = new List<(string, IReadOnlyList<string>)>();
                foreach (var record in line.Split(RecordSeparator))
                {
                    var fields = record.Split(UnitSeparator);
                    if (fields[0].Length == 0) continue;
                    var candidates = fields.Skip(1).Where(c => c.Length > 0).Distinct().ToList();
                    segments.Add((fields[0], candidates));
                    lock (_resultsGate)
                    {
                        if (_candidates.Count > 512) _candidates.Clear();
                        _candidates[fields[0]] = candidates;
                    }
                }
                return Remember(key, segments);
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"Mozc を使えませんでした: {ex.Message}");
                Fail();
                return null;
            }
        }
    }

    /// <summary>候補の控えと変換の結果の控えを捨てる (前の入力の控えを次の入力に持ち越さない。テストで文ごとに使う)。</summary>
    public void ClearCache()
    {
        lock (_resultsGate)
        {
            _candidates.Clear();
            _conversions.Clear();
        }
    }

    /// <summary>変換の控えの使い回しと、ヘルパーへの問い合わせの回数・時間 (性能の計測用)。入力した文字は含まない。</summary>
    public MozcStatistics Statistics()
    {
        lock (_gate)
        {
            long workingSet = 0;
            try
            {
                if (_process is { HasExited: false } process)
                {
                    process.Refresh();
                    workingSet = process.WorkingSet64;
                }
            }
            catch
            {
            }
            lock (_resultsGate)
            {
                return new MozcStatistics(_requests, _cacheHits, _candidateRequests, _candidateCacheHits, _helperCalls,
                    Stopwatch.GetElapsedTime(0, _helperTicks), workingSet);
            }
        }
    }

    /// <summary>変換の結果を覚えておく。</summary>
    private List<(string Reading, IReadOnlyList<string> Candidates)>? Remember(string key, List<(string Reading, IReadOnlyList<string> Candidates)>? segments)
    {
        lock (_resultsGate)
        {
            if (_conversions.Count > 512) _conversions.Clear();
            _conversions[key] = segments;
        }
        return segments;
    }

    // タブ・改行は区切りに使うので空白にする。
    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>動いているヘルパー (_gate の中で呼ぶ)。動いていなければ起動して READY を待つ。入力スレッドは _gate を取らないので、入力は止まらない。</summary>
    private Process Start()
    {
        if (_process is { HasExited: false } running) return running;
        _process?.Dispose();
        return _process = Launch();
    }

    private Process Launch()
    {
        var info = new ProcessStartInfo(_helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(_helperPath) ?? "",
        };
        if (_profileDirectory is not null)
        {
            Directory.CreateDirectory(_profileDirectory);
            info.ArgumentList.Add(_profileDirectory);
        }
        Process process;
        // 今の設定に足す (GetErrorMode で読む。SetErrorMode(0) で読むと、一瞬プロセス全体の設定が 0 になる)
        var previousMode = 0u;
        if (OperatingSystem.IsWindows())
        {
            previousMode = GetErrorMode();
            // 戻り値は以前のモード (エラーコードではない)。復元には直前に読んだモードを使う。
            _ = SetErrorMode(previousMode | SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);
        }
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("起動できませんでした");
        }
        finally
        {
            if (OperatingSystem.IsWindows()) _ = SetErrorMode(previousMode);
        }
        // Mozc のログ (標準エラー) は読み捨てる (溜まるとヘルパーが止まる)。
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        var ready = ReadLine(process, StartTimeoutMs);
        if (ready != "READY")
        {
            if (process.WaitForExit(1000) && IsLoaderFailure(process.ExitCode))
            {
                _missingDependency = true;
                Diagnostics.Log.Warn($"Mozc の変換ヘルパーを起動できません (DLL を読み込めない: 0x{process.ExitCode:X8}。同梱の Visual C++ のランタイムが無いか壊れている)。漢字に変換できません。");
            }
            try { process.Kill(); } catch { }
            process.Dispose();
            throw new InvalidOperationException($"起動に失敗しました: {ready ?? "応答なし"}");
        }
        Diagnostics.Log.Info("Mozc の変換エンジンを起動しました。");
        return process;
    }

    private static string? ReadLine(Process process, int timeoutMs)
    {
        var task = process.StandardOutput.ReadLineAsync();
        return task.Wait(timeoutMs) ? task.Result : null;
    }

    private void Fail()
    {
        _failures++;
        try
        {
            if (_process is { HasExited: false }) _process.Kill();
        }
        catch
        {
        }
        _process?.Dispose();
        _process = null;
    }

    /// <summary>起動しておく (最初の変換で待たないように)。</summary>
    public void WarmUp() => _worker.Submit(("", ""));

    public void Dispose()
    {
        _worker.Dispose();
        lock (_gate)
        {
            _disposed = true;
            try
            {
                if (_process is { HasExited: false } process)
                {
                    process.StandardInput.Write("Q\n");
                    process.StandardInput.Flush();
                    if (!process.WaitForExit(1000)) process.Kill();
                }
            }
            catch
            {
            }
            _process?.Dispose();
            _process = null;
        }
    }
}

/// <summary>
/// MozcConverter の計測値 (起動してからの合計)。Requests / CacheHits は変換、CandidateRequests / CandidateCacheHits は候補の一覧、
/// HelperCalls / HelperTime は変換ヘルパーとのやり取り (学習・保存も含む)、HelperWorkingSet は変換ヘルパーの使っているメモリ (起動していなければ 0)。
/// </summary>
public readonly record struct MozcStatistics(long Requests, long CacheHits, long CandidateRequests, long CandidateCacheHits,
    long HelperCalls, TimeSpan HelperTime, long HelperWorkingSet);
