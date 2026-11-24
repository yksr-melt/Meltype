// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;

namespace Meltype.Engine;

/// <summary>
/// Mozc (Google 日本語入力のオープンソース版、BSD-3-Clause) の変換エンジン。変換ヘルパー (<see cref="IMozcHelper"/>) に 1 行ずつ変換を頼む。
/// ヘルパーが無い・起動できない・応答しないときは null を返す (呼び出し側はかなのまま見せる)。
/// </summary>
internal sealed class MozcConverter : IKanjiConverter, IDisposable
{
    private const char UnitSeparator = '\x1f';
    private const char RecordSeparator = '\x1e';
    // 変換の答えで、ユーザー辞書の語を含む候補の頭に付く印 (GS)
    private const char UserDictionaryMark = '\x1d';

    private readonly IMozcHelper _helper;
    // ヘルパーとのやり取りの順番を守る。応答を待つ間も持つので、入力スレッドでは取らない
    private readonly object _gate = new();
    // 変換の結果の控えと、その回数を守る。ヘルパーの応答を待つ間は持たない (_gate の中で取ってよい。逆は取らない)
    private readonly object _resultsGate = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _candidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Reading, IReadOnlyList<string> Candidates, bool UserWord)>?> _conversions = new(StringComparer.Ordinal);
    private readonly LatestRequestWorker<(string, string)> _worker;
    private volatile int _failures;
    // 諦めてから、もう一度ヘルパーを使ってみる時刻 (Environment.TickCount64)
    private long _retryAt;
    // 一時的に答えない (重い・ウイルス対策ソフトが調べている) だけで、再起動までかなのままにしない (issue #2269)。
    // 変換は Mozc が頼りなので、Zenzai (ZenzaiHolder.RetryAfter の 5 分) より早く試し直す。試して失敗したら、また同じだけ待つ
    private const int DefaultRetryAfterMs = 60_000;
    // 送りたいユーザー辞書の要求 (D) と、それを受け取ったヘルパーの起動の回数 (受け取っていなければ -1)。_gate の中で読み書きする
    private string? _dictionary;
    private int _dictionaryLaunch = -1;

    // 性能の計測用の回数と時間 (--perf)。ロックの中で数えるだけなので、ふだんの入力は遅くならない
    private long _requests;
    private long _cacheHits;
    private long _candidateRequests;
    private long _candidateCacheHits;
    private long _helperCalls;
    private long _helperTicks;

    // 終了した。終了のあとで裏の処理 (覚えさせる・WarmUp) が動いても、ヘルパーを起動し直さない
    private volatile bool _disposed;

    /// <param name="helper">変換ヘルパー。この MozcConverter が Dispose する。</param>
    public MozcConverter(IMozcHelper helper)
    {
        _helper = helper;
        _worker = new LatestRequestWorker<(string, string)>(ProcessBackground);
    }

    public event Action? ResultReady;

    /// <summary>何度も失敗して諦めてから、もう一度使ってみるまでの時間 (テストでは縮める)。</summary>
    public int RetryAfterMs { get; init; } = DefaultRetryAfterMs;

    /// <summary>ヘルパーがあるか (起動できるかは使ってみるまで分からない)。</summary>
    public bool IsInstalled => _helper.IsInstalled;

    /// <summary>使えるか。何度も失敗したら、しばらく諦める。</summary>
    public bool IsAvailable => IsInstalled && (_failures < 3 || Environment.TickCount64 >= Interlocked.Read(ref _retryAt));

    public IReadOnlyList<ConversionClause>? Convert(ConversionRequest request) => ConvertWithCandidates(request)?.Select(s => s.Clause).ToList();

    /// <summary>
    /// Convert と同じ文節と、この変換で受け取った文節ごとの候補。
    /// 候補の控え (Candidates) は、ほかの変換や覚えさせたときに捨てられることがあるので使わない。
    /// 文脈は同じ文の日本語 (<see cref="EngineContext.Sentence"/>)。文脈付きと文脈なしの両方で変換し、文節の区切りが同じなら文脈付き
    /// (漢字の選び方だけが文脈で変わる: この本は + あつい → 厚い)、区切りまで変わるなら文脈なしを使う。前に同じ語があると、
    /// Mozc は文脈に引きずられて区切りを変えてしまうため (「記号等」含め、 + きごうとう → き|ごうとう → 気強盗)。
    /// </summary>
    internal IReadOnlyList<(ConversionClause Clause, IReadOnlyList<string> Candidates)>? ConvertWithCandidates(ConversionRequest request) =>
        WithContext(request.Hiragana, request.Before, (context, reading) => Query(context, reading, request.Deadline));

    /// <summary>ConvertWithCandidates と同じ (前の文脈なし)。ヘルパーが答えるまで待つので、入力スレッドでは呼ばない。</summary>
    internal IReadOnlyList<(ConversionClause Clause, IReadOnlyList<string> Candidates)>? ConvertWithCandidatesNow(string hiragana) =>
        WithContext(hiragana, null, Request);

    /// <summary>before から Mozc に渡す文脈を決めて、request (待つ・待たない) で変換する。</summary>
    private static List<(ConversionClause Clause, IReadOnlyList<string> Candidates)>? WithContext(string hiragana, string? before,
        Func<string, string, List<(string Reading, IReadOnlyList<string> Candidates, bool UserWord)>?> request)
    {
        if (EngineContext.Sentence(before) is not { } sentence) return WithCandidates(request("", hiragana));
        var withContext = request(sentence, hiragana);
        var plain = request("", hiragana);
        if (withContext is null || plain is null) return WithCandidates(withContext ?? plain);
        return WithCandidates(withContext.Select(s => s.Reading).SequenceEqual(plain.Select(s => s.Reading)) ? withContext : plain);
    }

    private static List<(ConversionClause Clause, IReadOnlyList<string> Candidates)>? WithCandidates(List<(string Reading, IReadOnlyList<string> Candidates, bool UserWord)>? segments) =>
        segments is { Count: > 0 }
            ? segments.Select(s => (new ConversionClause(s.Reading, (s.Candidates.Count > 0 ? s.Candidates[0] : null) ?? s.Reading) { UserWord = s.UserWord }, s.Candidates)).ToList()
            : null;

    /// <summary>読みの変換候補 (多い順)。直前の変換で同じ読みの文節があればその候補、無ければその読みだけで変換する (deadline まで待つ)。</summary>
    public IReadOnlyList<string> Candidates(string reading, long deadline)
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
        var segments = Query("", reading, deadline);
        if (segments is null) return [];
        // 1 文節ならその候補。複数の文節に分かれたなら、最初の候補をつなげたものだけ。
        return segments.Count == 1 ? segments[0].Candidates : [string.Concat(segments.Select(s => (s.Candidates.Count > 0 ? s.Candidates[0] : null) ?? s.Reading))];
    }

    // 学習データを保存する間隔 (覚えさせた回数)。Meltype を終了するときにも保存する。
    private const int SaveEvery = 5;
    private int _learnedSinceSave;

    /// <summary>確定した文節を Mozc に覚えさせる (Mozc の候補に無い文字列の文節があれば覚えない)。</summary>
    public void Learn(string? before, IReadOnlyList<ConversionClause> clauses)
    {
        if (clauses.Count == 0 || !IsAvailable) return;
        var records = string.Join(RecordSeparator, clauses.Select(c => Clean(c.Reading).Replace(UnitSeparator, ' ') + UnitSeparator + Clean(c.Text).Replace(UnitSeparator, ' ')));
        lock (_gate)
        {
            if (_disposed) return;
            // 覚えさせると候補の順番が変わるので、候補の控えを捨てる。
            ClearCache();
            var result = Send($"L\t{Clean(EngineContext.Sentence(before) ?? "")}\t{records}");
            if (result == "OK" && ++_learnedSinceSave >= SaveEvery)
            {
                _learnedSinceSave = 0;
                Send("S");
            }
        }
    }

    /// <summary>
    /// Mozc のユーザー辞書を、words の語 (読み・語・Mozc の品詞名) で入れ替える。入れ替えて変換に使えるようになったら true。
    /// 受け取っていないヘルパー (送るのに失敗した・起動し直した) には、次の要求の前に送る。
    /// ヘルパーの応答を待つので、入力スレッドでは呼ばない。
    /// </summary>
    public bool SetUserDictionary(IReadOnlyList<(string Reading, string Word, string Pos)> words)
    {
        var request = "D\t" + string.Join(RecordSeparator, words.Select(w => string.Join(UnitSeparator, Field(w.Reading), Field(w.Word), Field(w.Pos))));
        lock (_gate)
        {
            if (_disposed) return false;
            _dictionary = request;
            _dictionaryLaunch = -1;
            // 前の語での結果を控えから使わない (送るのに失敗しても、次の変換で送り直す)
            ClearCache();
            if (!IsAvailable) return false;
            if (_helper.Start()) return DeliverDictionary();
            Fail();
            return false;
        }

        static string Field(string text) => Clean(text).Replace(UnitSeparator, ' ').Replace(RecordSeparator, ' ');
    }

    // _dictionary を、まだ受け取っていない動いているヘルパーに送る (_gate の中で呼ぶ)。受け取ったら true
    private bool DeliverDictionary()
    {
        if (_dictionary is not { } dictionary) return false;
        if (_dictionaryLaunch == _helper.Launches) return true;
        switch (_helper.Send(dictionary))
        {
            case "OK":
                _dictionaryLaunch = _helper.Launches;
                _failures = 0;
                // 登録した語で候補が変わるので、控えを捨てる
                ClearCache();
                return true;
            case null:
                // 答えなかった。次の要求の前に送り直す
                Fail();
                return false;
            case var answer:
                // 要求を知らない前の版のヘルパー (空行)・受け取れない語 (NG)。送り直しても同じなので送らない
                Diagnostics.Log.Warn($"Mozc にユーザー辞書を渡せませんでした ({(answer.Length == 0 ? "要求を知らない前の版のヘルパー" : answer)})。登録した語が変換に出ません。");
                _dictionary = null;
                return false;
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
        if (!_helper.Start())
        {
            Fail();
            return null;
        }
        // 受け取っていないヘルパーには、先にユーザー辞書を送る。答えなかったら要求は送らない (辞書の無い結果を控えない。次の要求で送り直す)
        if (_dictionary is not null && !DeliverDictionary() && _dictionary is not null) return null;
        var started = Stopwatch.GetTimestamp();
        var line = _helper.Send(request);
        _helperCalls++;
        _helperTicks += Stopwatch.GetTimestamp() - started;
        if (line is null) Fail();
        else _failures = 0;
        return line;
    }

    /// <summary>入力スレッドからの問い合わせ。キャッシュに無ければワーカーに頼み、deadline (Environment.TickCount64) まで待つ。</summary>
    private List<(string Reading, IReadOnlyList<string> Candidates, bool UserWord)>? Query(string context, string reading, long deadline)
    {
        if (string.IsNullOrEmpty(reading) || !IsAvailable) return null;
        var request = (Clean(context), Clean(reading));
        if (TryCached(request, out var result)) return result;
        _worker.Submit(request);
        return _worker.Wait(request, ConversionRequest.RemainingMs(deadline)) && TryCached(request, out result) ? result : null;
    }

    private bool TryCached((string Context, string Reading) request, out List<(string Reading, IReadOnlyList<string> Candidates, bool UserWord)>? result)
    {
        result = null;
        if (_disposed) return false;
        lock (_resultsGate)
        {
            if (!_conversions.TryGetValue(request.Context + "\t" + request.Reading, out result)) return false;
            _requests++;
            _cacheHits++;
            foreach (var (segmentReading, candidates, _) in result ?? []) _candidates[segmentReading] = candidates;
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
            if (_helper.Start()) return true;
            Fail();
            return false;
        }
    }

    private List<(string Reading, IReadOnlyList<string> Candidates, bool UserWord)>? Request(string context, string reading)
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
                    foreach (var (segmentReading, candidates, _) in known ?? []) _candidates[segmentReading] = candidates;
                    return known;
                }
            }
            var line = Send($"C\t{context}\t{reading}");
            if (line is null) return null;
            if (line.Length == 0) return Remember(key, null);
            var segments = new List<(string, IReadOnlyList<string>, bool)>();
            foreach (var record in line.Split(RecordSeparator))
            {
                var fields = record.Split(UnitSeparator);
                if (fields[0].Length == 0) continue;
                var candidates = fields.Skip(1).Select(c => c.TrimStart(UserDictionaryMark)).Where(c => c.Length > 0).Distinct().ToList();
                segments.Add((fields[0], candidates, fields.Length > 1 && fields[1].StartsWith(UserDictionaryMark)));
                lock (_resultsGate)
                {
                    if (_candidates.Count > 512) _candidates.Clear();
                    _candidates[fields[0]] = candidates;
                }
            }
            return Remember(key, segments);
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
    internal MozcStatistics Statistics()
    {
        lock (_gate)
        {
            var workingSet = _helper.WorkingSet;
            lock (_resultsGate)
            {
                return new MozcStatistics(_requests, _cacheHits, _candidateRequests, _candidateCacheHits, _helperCalls,
                    Stopwatch.GetElapsedTime(0, _helperTicks), workingSet);
            }
        }
    }

    /// <summary>変換の結果を覚えておく。</summary>
    private List<(string Reading, IReadOnlyList<string> Candidates, bool UserWord)>? Remember(string key, List<(string Reading, IReadOnlyList<string> Candidates, bool UserWord)>? segments)
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

    private void Fail()
    {
        if (++_failures >= 3) Interlocked.Exchange(ref _retryAt, Environment.TickCount64 + RetryAfterMs);
        _helper.Reset();
    }

    /// <summary>起動しておく (最初の変換で待たないように)。</summary>
    public void WarmUp() => _worker.Submit(("", ""));

    public void Dispose()
    {
        _worker.Dispose();
        lock (_gate)
        {
            _disposed = true;
            _helper.Dispose();
        }
    }
}

/// <summary>
/// MozcConverter の計測値 (起動してからの合計)。Requests / CacheHits は変換、CandidateRequests / CandidateCacheHits は候補の一覧、
/// HelperCalls / HelperTime は変換ヘルパーとのやり取り (学習・保存も含む)、HelperWorkingSet は変換ヘルパーの使っているメモリ (起動していなければ 0)。
/// </summary>
internal readonly record struct MozcStatistics(long Requests, long CacheHits, long CandidateRequests, long CandidateCacheHits,
    long HelperCalls, TimeSpan HelperTime, long HelperWorkingSet);
