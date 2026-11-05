// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition;

/// <summary>
/// 同期でしか答えられない外の変換エンジン (Mac の azooKey・Windows の Microsoft IME) を、1 つのワーカーから呼ぶ。
/// 関数はワーカーだけが呼ぶので、外の変換エンジンはスレッドセーフでなくてよい。
/// </summary>
/// <param name="clauses">(読み, 文脈) → 文節。変換できなければ null。</param>
/// <param name="candidates">読み → 候補の一覧。</param>
/// <param name="convert">読み全体の変換。null なら文節をつなげる。</param>
/// <param name="budget">ほかの変換エンジンと 1 回の入力の待ち時間を共有するときに渡す。</param>
/// <param name="start">ワーカーを動かすスレッドを選ぶ (COM の STA など)。null なら thread pool。</param>
public sealed class BackgroundCallbackConverter(
    Func<string, string?, IReadOnlyList<ConversionClause>?> clauses,
    Func<string, IReadOnlyList<string>> candidates,
    Func<string, string?>? convert = null,
    ConversionBudget? budget = null,
    Action<Action>? start = null) : IKanjiConverter, IBackgroundConverter, IDisposable
{
    // 外の変換エンジンの答えは Zenzai の準備などで変わるので、同じ読みの古い答えを長く持たない
    private const int MaxCache = 64;

    private readonly object _gate = new();
    private readonly Dictionary<Request, Answer> _answers = [];
    private LatestRequestWorker<Request>? _worker;

    private enum Kind { Clauses, Convert, Candidates }

    private readonly record struct Request(Kind Kind, string Reading, string? Context);

    private readonly record struct Answer(IReadOnlyList<ConversionClause>? Clauses, string? Text, IReadOnlyList<string>? Candidates);

    public ConversionBudget Budget { get; } = budget ?? new();

    public event Action? ResultReady;

    public string? Convert(string hiragana) =>
        Ask(new Request(Kind.Convert, hiragana, null))?.Text;

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
        Ask(new Request(Kind.Clauses, hiragana, context))?.Clauses;

    public IReadOnlyList<string> Candidates(string reading) =>
        Ask(new Request(Kind.Candidates, reading, null))?.Candidates ?? [];

    /// <summary>最初の変換の準備 (辞書の読み込み) をワーカーで済ませておく。</summary>
    public void WarmUp() => Worker().Submit(new Request(Kind.Clauses, "にほんご", null));

    public void Dispose()
    {
        lock (_gate) _worker?.Dispose();
    }

    private string? ConvertNow(string hiragana) =>
        convert is not null ? convert(hiragana)
            : clauses(hiragana, null) is { Count: > 0 } parts ? string.Concat(parts.Select(c => c.Text)) : null;

    private LatestRequestWorker<Request> Worker()
    {
        lock (_gate) return _worker ??= new LatestRequestWorker<Request>(Process, start: start);
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
        var answer = request.Kind switch
        {
            Kind.Clauses => new Answer(clauses(request.Reading, request.Context), null, null),
            Kind.Convert => new Answer(null, ConvertNow(request.Reading), null),
            Kind.Candidates => new Answer(null, null, candidates(request.Reading)),
            _ => throw new InvalidOperationException($"不明な要求: {request.Kind}"),
        };
        // 答えが無いのは準備前・一時的な失敗・時間切れ (WinRT) かもしれないので、覚えずに次もまた聞く
        if (answer.Clauses is null && answer.Text is null && answer.Candidates is null or { Count: 0 }) return;
        lock (_gate)
        {
            if (_answers.Count >= MaxCache) _answers.Clear();
            _answers[request] = answer;
        }
        ResultReady?.Invoke();
    }
}
