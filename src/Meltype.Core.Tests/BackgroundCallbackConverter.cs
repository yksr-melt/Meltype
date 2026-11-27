// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Engine;

namespace Meltype.Tests;

/// <summary>
/// 背景で答える変換エンジンの偽物 (BackgroundConversionTests)。同期の関数を 1 つのワーカーから呼び、答えが届いたら知らせる。
/// </summary>
/// <param name="clauses">(読み, 文脈) → 文節。変換できなければ null。</param>
/// <param name="candidates">読み → 候補の一覧。</param>
/// <param name="convert">文節に区切れないときの、読み全体の変換。null なら区切れなければ答えない。</param>
/// <param name="start">ワーカーを動かすスレッドを選ぶ (COM の STA など)。null なら thread pool。</param>
internal sealed class BackgroundCallbackConverter(
    Func<string, string?, IReadOnlyList<ConversionClause>?> clauses,
    Func<string, IReadOnlyList<string>> candidates,
    Func<string, string?>? convert = null,
    Action<Action>? start = null) : IKanjiConverter, IDisposable
{
    // 1 回の表示で要る要求より少ないと、答えが届くたびに作り直しても控えが足りず、再描画が終わらない
    private const int MaxCache = 256;

    private readonly object _gate = new();
    private readonly Dictionary<Request, Answer> _answers = [];
    private LatestRequestWorker<Request>? _worker;

    private enum Kind { Clauses, Candidates }

    private readonly record struct Request(Kind Kind, string Reading, string? Context);

    private readonly record struct Answer(IReadOnlyList<ConversionClause>? Clauses, IReadOnlyList<string>? Candidates);

    public event Action? ResultReady;

    public IReadOnlyList<ConversionClause>? Convert(ConversionRequest request) =>
        Ask(new Request(Kind.Clauses, request.Hiragana, request.Before), ConversionRequest.RemainingMs(request.Deadline))?.Clauses;

    public IReadOnlyList<string> Candidates(string reading, long deadline) =>
        Ask(new Request(Kind.Candidates, reading, null), ConversionRequest.RemainingMs(deadline))?.Candidates ?? [];

    /// <summary>最初の変換の準備 (辞書の読み込み) をワーカーで済ませておく。</summary>
    public void WarmUp() => Worker().Submit(new Request(Kind.Clauses, "にほんご", null));

    public void Dispose()
    {
        lock (_gate) _worker?.Dispose();
    }

    private IReadOnlyList<ConversionClause>? ConvertNow(string hiragana, string? context) =>
        clauses(hiragana, context) ?? (convert?.Invoke(hiragana) is { } text ? [new ConversionClause(hiragana, text)] : null);

    private LatestRequestWorker<Request> Worker()
    {
        lock (_gate) return _worker ??= new LatestRequestWorker<Request>(Process, start: start);
    }

    private Answer? Ask(Request request, int waitMs)
    {
        var worker = Worker();
        lock (_gate)
        {
            if (_answers.TryGetValue(request, out var known))
            {
                // 外の変換エンジンの答えは同じ読みでも変わることがある。
                // 控えを返して裏で聞き直し、変わっていれば Process が知らせる
                worker.SubmitIfIdle(request);
                return known;
            }
        }
        worker.Submit(request);
        if (!worker.Wait(request, waitMs)) return null;
        lock (_gate) return _answers.TryGetValue(request, out var answered) ? answered : null;
    }

    private void Process(Request request)
    {
        var answer = request.Kind switch
        {
            Kind.Clauses => new Answer(ConvertNow(request.Reading, request.Context), null),
            Kind.Candidates => new Answer(null, candidates(request.Reading)),
            _ => throw new InvalidOperationException($"不明な要求: {request.Kind}"),
        };
        // 答えが無いのは準備前・一時的な失敗・時間切れかもしれないので、覚えずに次もまた聞く
        if (answer.Clauses is null && answer.Candidates is null or { Count: 0 }) return;
        lock (_gate)
        {
            // 同じ答えを知らせると、再描画が聞き直しを呼び、終わらない
            if (_answers.TryGetValue(request, out var known) && SameAnswer(known, answer)) return;
            if (_answers.Count >= MaxCache) _answers.Clear();
            _answers[request] = answer;
        }
        ResultReady?.Invoke();
    }

    private static bool SameAnswer(Answer a, Answer b) =>
        (a.Clauses ?? []).SequenceEqual(b.Clauses ?? []) &&
        (a.Candidates ?? []).SequenceEqual(b.Candidates ?? []);
}
