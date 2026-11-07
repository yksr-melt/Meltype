// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Clause = Meltype.Composition.CompositionController.Clause;

namespace Meltype.Composition;

/// <summary>日本語の区間 1 つの変換の要求。Kana は区間全体の読み。Raw は打った文字全体 (入力全体を見るときだけ。それ以外は空)。</summary>
/// <param name="CandidatesFor">(読み, 文の中での変換結果) → いつもの候補の一覧。文節をまとめ直すときに使う。</param>
internal sealed record ConversionRequest(string Kana, string Raw, Func<string, string?, List<string>> CandidatesFor);

internal delegate List<Clause> ConversionStep(ConversionRequest request);

/// <summary>
/// 変換の前後に処理を挟むミドルウェア。next を呼ぶと、次のミドルウェアと変換エンジンが動く。next を呼ばずに返せば、変換エンジンを使わない。
/// 足すときは、このインターフェース (読みに候補を足すだけなら <see cref="ReadingCandidateMiddleware"/>) を実装して <see cref="UtilityCandidates"/> の一覧に入れる。
/// </summary>
internal interface IConversionMiddleware
{
    List<Clause> Convert(ConversionRequest request, ConversionStep next);
}

/// <summary>
/// 日本語の区間全体の読みが一致したら、候補を足す。区間の途中の語には足さない (きょうのひづけ の 今日 に日付を出さない)。
/// 変換エンジンが文節を分けても (10がつ|9にち) 1 つの文節にまとめる。足した候補は選んでも学習しない (打つたびに変わる)。
/// </summary>
internal abstract class ReadingCandidateMiddleware : IConversionMiddleware
{
    /// <summary>読みに対する候補。対象の読みでなければ空。</summary>
    protected abstract IEnumerable<string> Candidates(string reading);

    public List<Clause> Convert(ConversionRequest request, ConversionStep next)
    {
        var clauses = next(request);
        if (clauses.Count == 0 || request.Kana.Length == 0) return clauses;
        var extra = Candidates(request.Kana).ToList();
        if (extra.Count == 0) return clauses;
        var clause = clauses.Count == 1 ? clauses[0]
            : new Clause(request.Kana, false, request.CandidatesFor(request.Kana, string.Concat(clauses.Select(c => c.Text))));
        foreach (var text in extra)
        {
            if (!clause.Candidates.Contains(text)) clause.Candidates.Add(text);
            clause.Generated.Add(text);
        }
        return [clause];
    }
}

/// <summary>ユーティリティ変換のミドルウェアの一覧。先に書いたものが外側になり、候補は内側のものから順に足される。</summary>
internal sealed class UtilityCandidates(CompositionOptions options, ICompositionHost host)
{
    private readonly IConversionMiddleware[] _middlewares =
    [
        new NowCandidateProvider(options, host),
        new ClockCandidateProvider(options),
        new ClipboardCandidateProvider(options, host),
        new TraditionalUnitProvider(),
        new EraYearProvider(),
        new SeasonalGreetingProvider(options),
        new TypedDateCandidateProvider(options),
        new RawExpressionProvider(options),
    ];

    public List<Clause> Convert(ConversionRequest request, ConversionStep engine)
    {
        var next = engine;
        foreach (var middleware in Enumerable.Reverse(_middlewares))
        {
            var inner = next;
            next = r => middleware.Convert(r, inner);
        }
        return next(request);
    }
}
