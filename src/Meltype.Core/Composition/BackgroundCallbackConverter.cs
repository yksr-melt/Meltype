// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition;

/// <summary>
/// 同期でしか答えられない外の変換エンジン (Mac の azooKey) を、1 つのワーカーのスレッドから呼ぶ。
/// Budget を使っている間は clauses と candidates をワーカーだけが呼ぶので、外の変換エンジンはスレッドセーフでなくてよい。
/// </summary>
/// <param name="clauses">(読み, 文脈) → 文節。変換できなければ null。</param>
/// <param name="candidates">読み → 候補の一覧。</param>
public sealed class BackgroundCallbackConverter(
    Func<string, string?, IReadOnlyList<ConversionClause>?> clauses,
    Func<string, IReadOnlyList<string>> candidates) : IKanjiConverter, IBackgroundConverter, IDisposable
{
    // 外の変換エンジンの答えは Zenzai の準備などで変わるので、同じ読みの古い答えを長く持たない
    private const int MaxCache = 64;

    private readonly object _gate = new();
    private readonly Dictionary<Request, Answer> _answers = [];
    private LatestRequestWorker<Request>? _worker;

    private readonly record struct Request(bool IsCandidates, string Reading, string? Context);

    private readonly record struct Answer(IReadOnlyList<ConversionClause>? Clauses, IReadOnlyList<string>? Candidates);

    public ConversionBudget Budget { get; } = new();

    public event Action? ResultReady;

    public string? Convert(string hiragana) =>
        ConvertClauses(hiragana) is { Count: > 0 } parts ? string.Concat(parts.Select(c => c.Text)) : null;

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
        Budget.Limited ? Ask(new Request(false, hiragana, context))?.Clauses : clauses(hiragana, context);

    public IReadOnlyList<string> Candidates(string reading) =>
        Budget.Limited ? Ask(new Request(true, reading, null))?.Candidates ?? [] : candidates(reading);

    /// <summary>最初の変換の準備 (辞書の読み込み) をワーカーで済ませておく。</summary>
    public void WarmUp() => Worker().Submit(new Request(false, "にほんご", null));

    public void Dispose()
    {
        lock (_gate) _worker?.Dispose();
    }

    private LatestRequestWorker<Request> Worker()
    {
        lock (_gate) return _worker ??= new LatestRequestWorker<Request>(Process);
    }

    private Answer? Ask(Request request)
    {
        lock (_gate)
        {
            if (_answers.TryGetValue(request, out var known)) return known;
        }
        var worker = Worker();
        worker.Submit(request);
        if (!worker.Wait(request, Budget.RemainingMs)) return null;
        lock (_gate) return _answers.TryGetValue(request, out var answered) ? answered : null;
    }

    private void Process(Request request)
    {
        var answer = request.IsCandidates
            ? new Answer(null, candidates(request.Reading))
            : new Answer(clauses(request.Reading, request.Context), null);
        lock (_gate)
        {
            if (_answers.Count >= MaxCache) _answers.Clear();
            _answers[request] = answer;
        }
        ResultReady?.Invoke();
    }
}
