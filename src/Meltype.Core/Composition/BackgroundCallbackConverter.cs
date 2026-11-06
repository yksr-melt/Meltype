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
    // 1 回の表示で要る要求より少ないと、答えが届くたびに作り直しても控えが足りず、再描画が終わらない
    private const int MaxCache = 256;

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
        var worker = Worker();
        lock (_gate)
        {
            if (_answers.TryGetValue(request, out var known))
            {
                // 外の変換エンジンの答えは同じ読みでも変わる (Zenzai の準備中・処理中は azooKey の答え、Microsoft IME は単語の登録)。
                // 控えを返して裏で聞き直し、変わっていれば Process が知らせる
                worker.SubmitIfIdle(request);
                return known;
            }
        }
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
            // 同じ答えを知らせると、再描画が聞き直しを呼び、終わらない
            if (_answers.TryGetValue(request, out var known) && SameAnswer(known, answer)) return;
            if (_answers.Count >= MaxCache) _answers.Clear();
            _answers[request] = answer;
        }
        ResultReady?.Invoke();
    }

    private static bool SameAnswer(Answer a, Answer b) =>
        a.Text == b.Text &&
        (a.Clauses ?? []).SequenceEqual(b.Clauses ?? []) &&
        (a.Candidates ?? []).SequenceEqual(b.Candidates ?? []);
}
