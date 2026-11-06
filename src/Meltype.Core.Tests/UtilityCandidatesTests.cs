// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Composition;
using Meltype.Input;
using static Meltype.Tests.CompositionTests;
using Clause = Meltype.Composition.CompositionController.Clause;

namespace Meltype.Tests;

/// <summary>ユーティリティ変換のミドルウェア。日本語の区間全体の読みが一致したら候補を足す。</summary>
internal static class UtilityCandidatesTests
{
    private sealed class SplittingConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => hiragana switch
        {
            "きょうのひづけ" => [new("きょう", "今日"), new("の", "の"), new("ひづけ", "日付")],
            "じこうのあいさつ" => [new("じこうの", "時候の"), new("あいさつ", "挨拶")],
            "10がつ9にち" => [new("10がつ", "10月"), new("9にち", "9日")],
            "10がつ9にちのよてい" => [new("10がつ", "10月"), new("9にちの", "9日の"), new("よてい", "予定")],
            _ => [new(hiragana, hiragana)],
        };
    }

    private sealed class Words(string reading, params string[] words) : ReadingCandidateMiddleware
    {
        protected override IEnumerable<string> Candidates(string text) => text == reading ? words : [];
    }

    private static List<string> Candidates(Keyboard k) => k.Host.View!.Candidates.ToList();

    [Test]
    public static void WholeSegment_OffersDate()
    {
        var k = new Keyboard(converter: new SplittingConverter());
        k.Type("kyou ");
        Assert.True(Candidates(k).Any(c => c.Contains('年')), string.Join(",", Candidates(k)));
    }

    [Test]
    public static void InsideASentence_DoesNotOfferDate()
    {
        var k = new Keyboard(converter: new SplittingConverter());
        k.Type("kyounohiduke ");
        Assert.True(!Candidates(k).Any(c => c.Contains('年')), "文の途中の 今日 に日付を出さない: " + string.Join(",", Candidates(k)));

        var typed = new Keyboard(converter: new SplittingConverter());
        typed.Type("10gatu9nitinoyotei ");
        Assert.True(!Candidates(typed).Contains("10/9"), "文の途中の日付には足さない: " + string.Join(",", Candidates(typed)));
    }

    [Test]
    public static void EngineSplit_IsJoinedIntoOneClause()
    {
        var greeting = new Keyboard(converter: new SplittingConverter());
        greeting.Type("jikounoaisatu ");
        Assert.True(greeting.Host.View!.Clauses is null or { Count: 1 }, "時候の|挨拶 を 1 つの文節にする");
        Assert.True(Candidates(greeting).Any(c => c.EndsWith("の候", StringComparison.Ordinal)), string.Join(",", Candidates(greeting)));

        var date = new Keyboard(converter: new SplittingConverter());
        date.Type("10gatu9niti ");
        Assert.True(Candidates(date).Contains("10/9"), string.Join(",", Candidates(date)));
    }

    [Test]
    public static void OnlyGeneratedCandidates_AreNotLearned()
    {
        // 数字を含むふつうの候補 (1間) は学習してよい。足した候補だけが学習の対象外
        var clause = new Clause("いま", false, ["1間"]);
        var result = new Words("いま", "17:22").Convert(new ConversionRequest("いま", "", (_, _) => []), _ => [clause]);
        Assert.Equal("1間,17:22", string.Join(",", result[0].Candidates));
        Assert.True(result[0].Generated.Contains("17:22"), "足した候補");
        Assert.True(!result[0].Generated.Contains("1間"), "変換エンジンの候補");
    }

    [Test]
    public static void GeneratedText_IsNotLearnedEvenIfTheEngineAlsoOffersIt()
    {
        // Mozc も きょう に今日の日付を出す。学習すると、次の日からも古い日付が先に出る
        var clause = new Clause("きょう", false, ["今日", "2026/11/06"]);
        var result = new Words("きょう", "2026/11/06").Convert(new ConversionRequest("きょう", "", (_, _) => []), _ => [clause]);
        Assert.True(result[0].Generated.Contains("2026/11/06"), "変換エンジンも出す日付");
    }

    [Test]
    public static void SeveralMiddlewares_AllAddCandidates()
    {
        // 同じ読みに複数のユーティリティ変換が答えたら、どれも出す
        var request = new ConversionRequest("よみ", "", (_, _) => []);
        var result = new Words("よみ", "外側").Convert(request,
            r => new Words("よみ", "内側", "外側").Convert(r, _ => [new Clause("よみ", false, ["よみ"])]));
        Assert.Equal("よみ,内側,外側", string.Join(",", result[0].Candidates));
    }

    [Test]
    public static void NoMatch_ReturnsTheEngineClauses()
    {
        var clauses = new List<Clause> { new("あ", false, ["あ"]), new("い", false, ["い"]) };
        var result = new Words("よみ", "候補").Convert(new ConversionRequest("あい", "", (_, _) => []), _ => clauses);
        Assert.True(ReferenceEquals(result, clauses), "文節をまとめない");
    }
}
