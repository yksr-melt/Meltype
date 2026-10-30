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
/// やり取り (UTF-8、1 行ずつ): 「C[TAB]前の文字列[TAB]読み」を送ると、文節ごとに「読み[US]候補1[US]候補2…」を [RS] でつないだ 1 行が返る。
/// ヘルパーが無い・起動できない・応答しないときは null を返すので、呼び出し側で別の変換エンジンを使う。
/// </summary>
public sealed class MozcConverter : IKanjiConverter, ILearningConverter, IDisposable
{
    private const char UnitSeparator = '\x1f';
    private const char RecordSeparator = '\x1e';
    private const int TimeoutMs = 1500;
    private const int StartTimeoutMs = 10000;

    private readonly string _helperPath;
    private readonly string? _profileDirectory;
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _candidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Reading, IReadOnlyList<string> Candidates)>?> _conversions = new(StringComparer.Ordinal);
    private Process? _process;
    private int _failures;

    // 性能の計測用の回数と時間 (--perf)。_gate の中で数えるだけなので、ふだんの入力は遅くならない
    private long _requests;
    private long _cacheHits;
    private long _candidateRequests;
    private long _candidateCacheHits;
    private long _helperCalls;
    private long _helperTicks;
    // 入力側がヘルパーを待った時間。裏の学習・保存の時間は含めない (issue #1209)。
    private long _waitCalls;
    private long _waitTicks;
    private long _busySkips;
    // 起動はバックグラウンドで行い、そのあいだの変換は待たずに null を返す (変換は UI スレッドで動くので、READY を待つと入力が止まる)
    private Task? _starting;
    private bool _started;

    // 要る DLL が無くて起動できない (Visual C++ のランタイムが無いなど)。起動し直しても同じなので、もう試さない
    private bool _missingDependency;

    // 終了した。終了のあとで裏の処理 (覚えさせる・WarmUp) が動いても、ヘルパーを起動し直さない (_gate の中で読み書きする)
    private bool _disposed;

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
    }

    /// <summary>ヘルパーがあるか (起動できるかは使ってみるまで分からない)。</summary>
    public bool IsInstalled => File.Exists(_helperPath);

    /// <summary>使えるか。何度も失敗したら諦める (毎回起動し直して入力が遅くなるのを避ける)。</summary>
    public bool IsAvailable => IsInstalled && _failures < 3 && !_missingDependency;

    public string? Convert(string hiragana) =>
        ConvertClauses(hiragana) is { Count: > 0 } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
        ConvertWithCandidates(hiragana, context)?.Select(s => s.Clause).ToList();

    /// <summary>
    /// ConvertClauses と同じ文節と、この変換で受け取った文節ごとの候補。
    /// 候補の控え (Candidates) は、ほかの変換や覚えさせたときに捨てられることがあるので使わない (聞き直すと入力が待たされる)。
    /// </summary>
    internal IReadOnlyList<(ConversionClause Clause, IReadOnlyList<string> Candidates)>? ConvertWithCandidates(string hiragana, string? context = null)
    {
        if (Request(context ?? "", hiragana) is not { Count: > 0 } segments) return null;
        return segments.Select(s => (new ConversionClause(s.Reading, (s.Candidates.Count > 0 ? s.Candidates[0] : null) ?? s.Reading), s.Candidates)).ToList();
    }

    /// <summary>読みの変換候補 (多い順)。直前の変換で同じ読みの文節があればその候補、無ければその読みだけで変換する。</summary>
    public IReadOnlyList<string> Candidates(string reading)
    {
        // 学習・保存や別の変換がヘルパーを使っていても、入力側では終わるのを待たない。
        if (!Monitor.TryEnter(_gate))
        {
            Interlocked.Increment(ref _busySkips);
            return [];
        }
        try
        {
            _candidateRequests++;
            if (_candidates.TryGetValue(reading, out var cached))
            {
                _candidateCacheHits++;
                return cached;
            }
        }
        finally { Monitor.Exit(_gate); }
        var segments = Request("", reading);
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
            if (_disposed || !IsAvailable) return;
            // 覚えさせると候補の順番が変わるので、候補の控えを捨てる。
            _candidates.Clear();
            _conversions.Clear();
            var result = Send($"L\t{Clean(context ?? "")}\t{records}");
            if (result == "OK" && ++_learnedSinceSave >= SaveEvery)
            {
                _learnedSinceSave = 0;
                Send("S");
            }
        }
    }

    /// <summary>1 行送って 1 行受け取る (_gate の中で呼ぶ)。失敗したら・起動しているあいだは null。</summary>
    private string? Send(string request, bool recordWait = false)
    {
        var started = Stopwatch.GetTimestamp();
        var queried = false;
        try
        {
            if (Start() is not { } process) return null;
            queried = true;

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
        finally
        {
            if (recordWait && queried)
            {
                Interlocked.Increment(ref _waitCalls);
                Interlocked.Add(ref _waitTicks, Stopwatch.GetTimestamp() - started);
            }
        }
    }

    private List<(string Reading, IReadOnlyList<string> Candidates)>? Request(string context, string reading)
    {
        if (string.IsNullOrEmpty(reading) || !IsAvailable) return null;
        context = Clean(context);
        reading = Clean(reading);
        if (!Monitor.TryEnter(_gate))
        {
            Interlocked.Increment(ref _busySkips);
            return null;
        }
        try
        {
            if (_disposed || !IsAvailable) return null;
            // ライブ変換は 1 キーごとに同じ読みを何度も変換し直すので、同じ頼み方の結果は使い回す (覚えさせたら捨てる)。
            var key = context + "\t" + reading;
            _requests++;
            if (_conversions.TryGetValue(key, out var known))
            {
                _cacheHits++;
                // 候補の控えは「直前の変換」のものを使うので、使い回したときも控えを入れ直す
                foreach (var (segmentReading, candidates) in known ?? []) _candidates[segmentReading] = candidates;
                return known;
            }
            try
            {
                var line = Send($"C\t{context}\t{reading}", recordWait: true);
                if (line is null) return null;
                if (line.Length == 0) return Remember(key, null);
                var segments = new List<(string, IReadOnlyList<string>)>();
                foreach (var record in line.Split(RecordSeparator))
                {
                    var fields = record.Split(UnitSeparator);
                    if (fields[0].Length == 0) continue;
                    var candidates = fields.Skip(1).Where(c => c.Length > 0).Distinct().ToList();
                    segments.Add((fields[0], candidates));
                    if (_candidates.Count > 512) _candidates.Clear();
                    _candidates[fields[0]] = candidates;
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
        finally { Monitor.Exit(_gate); }
    }

    /// <summary>候補の控えと変換の結果の控えを捨てる (前の入力の控えを次の入力に持ち越さない。テストで文ごとに使う)。</summary>
    public void ClearCache()
    {
        lock (_gate)
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
            return new MozcStatistics(_requests, _cacheHits, _candidateRequests, _candidateCacheHits, _helperCalls,
                Stopwatch.GetElapsedTime(0, _helperTicks), workingSet);
        }
    }

    /// <summary>前に取ってから入力側がヘルパーを待った回数・時間と、使用中のため見送った回数。学習・保存の待ち時間は除く。</summary>
    public (long Calls, long Ms, long BusySkips) TakeWaitStats() =>
        (Interlocked.Exchange(ref _waitCalls, 0), (long)Stopwatch.GetElapsedTime(0, Interlocked.Exchange(ref _waitTicks, 0)).TotalMilliseconds,
            Interlocked.Exchange(ref _busySkips, 0));

    /// <summary>変換の結果を覚えておく (_gate の中で呼ぶ)。</summary>
    private List<(string Reading, IReadOnlyList<string> Candidates)>? Remember(string key, List<(string Reading, IReadOnlyList<string> Candidates)>? segments)
    {
        if (_conversions.Count > 512) _conversions.Clear();
        _conversions[key] = segments;
        return segments;
    }

    // タブ・改行は区切りに使うので空白にする。
    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>動いているヘルパー (_gate の中で呼ぶ)。動いていなければバックグラウンドで起動して null を返す。
    /// WarmUp を呼ばずに初めて変換するとき (テストの道具など) だけは、その場で起動して待つ。</summary>
    private Process? Start()
    {
        if (_process is { HasExited: false } running) return running;
        _process?.Dispose();
        _process = null;
        if (_starting is { IsCompleted: false }) return null;
        if (!_started)
        {
            _started = true;
            return _process = Launch();
        }
        StartInBackground();
        return null;
    }

    /// <summary>バックグラウンドで起動する (_gate の中で呼ぶ)。READY を待つあいだは _gate を持たない。</summary>
    private void StartInBackground()
    {
        _started = true;
        _starting = Task.Run(() =>
        {
            try
            {
                var process = Launch();
                lock (_gate)
                {
                    if (_disposed)
                    {
                        try { process.Kill(); } catch { }
                        process.Dispose();
                        return;
                    }
                    _process = process;
                }
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    Diagnostics.Log.Warn($"Mozc を使えませんでした: {ex.Message}");
                    Fail();
                }
            }
        });
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
                Diagnostics.Log.Warn($"Mozc の変換ヘルパーを起動できません (DLL を読み込めない: 0x{process.ExitCode:X8}。同梱の Visual C++ のランタイムが無いか壊れている)。Microsoft IME だけで変換します。");
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
    public void WarmUp()
    {
        lock (_gate)
        {
            if (!_started && IsAvailable) StartInBackground();
        }
    }

    public void Dispose()
    {
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
