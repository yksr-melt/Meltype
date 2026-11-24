// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;
using Meltype.Engine;
using Meltype.Input;
using Meltype.Learning;

namespace Meltype.Tests;

/// <summary>設定の句読点は打った句読点にだけ当て、語の中の 、。 (モーニング娘。・顔文字) は変えない (#1735)。</summary>
internal static class PunctuationSpanTests
{
    private const PunctuationStyle Period = PunctuationStyle.FullWidthCommaPeriod;

    [Test]
    public static void WordMarks_StayWithTheSetting()
    {
        var k = new CompositionTests.Keyboard(converter: new MorningConverter()) { Punctuation = Period };
        k.Type("mo-ningumusume ");
        Assert.Equal("モーニング娘。", k.Showing, "Space で変換");
        Assert.True(k.Host.View!.Candidates.Contains("モーニング娘。"), "候補: " + string.Join(" ", k.Host.View.Candidates));
        k.Type("\n");
        Assert.Equal("モーニング娘。", k.Host.Output.Single(), "確定");

        var live = new CompositionTests.Keyboard(live: true, converter: new MorningConverter()) { Punctuation = Period };
        live.Type("mo-ningumusume");
        Assert.Equal("モーニング娘。", live.Showing, "ライブ変換");
    }

    [Test]
    public static void TypedMarks_FollowTheSetting_NextToWordMarks()
    {
        var k = new CompositionTests.Keyboard(converter: new MorningConverter()) { Punctuation = Period };
        k.Type("mo-ningumusumeha,suki. ");
        Assert.Equal("モーニング娘。は，好き．", k.Showing, "打った 、。 だけが設定の句読点");
        k.Type("\n");
        Assert.Equal("モーニング娘。は，好き．", k.Host.Output.Single());

        var live = new CompositionTests.Keyboard(live: true, converter: new MorningConverter()) { Punctuation = Period };
        live.Type("mo-ningumusumeha,suki.");
        Assert.Equal("モーニング娘。は，好き．", live.Showing, "ライブ変換");

        // 読みの末尾の 。 に、変換エンジンが 。 を足した答え (娘。。) は、末尾の 。 だけが打った句読点
        var doubled = new CompositionTests.Keyboard(converter: new MorningConverter()) { Punctuation = Period };
        doubled.Type("mo-ningumusume. ");
        Assert.Equal("モーニング娘。．", doubled.Showing);
    }

    [Test]
    public static void KaomojiCandidate_KeepsItsMarks()
    {
        var k = new CompositionTests.Keyboard(converter: new MorningConverter(), moreCandidates: reading => reading == "かおもじ" ? ["(。・ω・。)"] : []) { Punctuation = Period };
        k.Type("kaomoji ");
        for (var i = 0; i < 20 && k.Host.View!.Candidates[k.Host.View.SelectedIndex] != "(。・ω・。)"; i++) k.Press(VirtualKeys.Space);
        Assert.Equal("(。・ω・。)", k.Showing, "候補の一覧: " + string.Join(" ", k.Host.View!.Candidates));
        k.Press(VirtualKeys.Return);
        Assert.Equal("(。・ω・。)", k.Host.Output.Single());
    }

    [Test]
    public static void PredictedPhrase_TypedMarksFollowTheSetting()
    {
        // 予測変換の語句は、確定した文字 (設定の句読点・語の中の 。) のまま覚えて出す
        var predictor = new Predictor(new PhraseHistory(null), null, null);
        var k = new CompositionTests.Keyboard(converter: new MorningConverter(), predictor: predictor) { Punctuation = Period };
        k.Type("mo-ningumusumeha,suki ");
        k.Press(VirtualKeys.Return);
        k.Type("mo-ningumusumeha");
        Assert.True(k.Host.View!.Predictions?.Contains("モーニング娘。は，好き") == true, "予測の候補: " + string.Join(" / ", k.Host.View.Predictions ?? []));
        k.Press(VirtualKeys.Tab);
        Assert.Equal("モーニング娘。は，好き", k.Showing);
        k.Press(VirtualKeys.Return);
        Assert.Equal("モーニング娘。は，好きモーニング娘。は，好き", k.Host.Document);
    }

    [Test]
    public static void ShiftEnter_RestKeepsTypedMarks()
    {
        var k = new CompositionTests.Keyboard(converter: new MorningConverter()) { PartialCommit = true, Punctuation = Period };
        k.Type("suki,suki ");
        Assert.Equal("好き|，|好き", string.Join("|", k.Host.View!.Clauses!));
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Return);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
        Assert.Equal("好き", k.Host.Document, "選んでいる文節までを確定");
        Assert.Equal("，すき", k.Showing, "残した文節の打った 、 も設定の句読点");
    }

    [Test]
    public static void WordWithCommaMark_CanBeCorrected()
    {
        // 語の中の ， (胃，) は入力欄にそのまま入るので、確定し直すときは入力欄の文字 (設定の句読点を戻す前) と比べる
        var k = new CompositionTests.Keyboard(converter: new CommaConverter()) { Punctuation = Period };
        k.Type("i want ");
        Assert.Equal("I want ", k.Host.Document);
    }

    private sealed class CommaConverter : TestConverter
    {
        public override string? Convert(string hiragana, string? before) => hiragana == "い" ? "胃，" : null;
    }

    [Test]
    public static void EngineAnswers_AreSplitAtTypedMarks()
    {
        static string Split(string reading, string text, bool userWord = false) =>
            string.Join("|", Composition.CompositionConversion.SplitAtMarks(new ConversionClause(reading, text) { UserWord = userWord }).Select(c => c.Reading + ":" + c.Text));
        Assert.Equal("わたしは:私は|、:、|りんご:林檎|。:。", Split("わたしは、りんご。", "私は、林檎。"));
        Assert.Equal("。。。:。。。", Split("。。。", "。。。"), "続いた句読点は 1 つの文節");
        Assert.Equal("むすめ:娘。", Split("むすめ", "娘。"), "読みに無い 。 は語の文字");
        Assert.Equal("むすめ。:娘。。", Split("むすめ。", "娘。。"), "句読点が合わない文節は分けない");
        Assert.Equal("おつ、:お疲れ様です、", Split("おつ、", "お疲れ様です、", userWord: true), "ユーザー辞書の語は分けない");
    }

    [Test]
    public static void WholeAnswer_TypedMarksFollowTheSetting()
    {
        // 読み全体で 1 つの文節の答え (Zenzai) も、打った句読点で分ける
        foreach (var live in new[] { false, true })
        {
            var k = new CompositionTests.Keyboard(live: live, converter: new WholeConverter()) { Punctuation = Period };
            k.Type(live ? "watasiha,ringowo,tabeta." : "watasiha,ringowo,tabeta. ");
            Assert.Equal("私は，林檎を，食べた．", k.Showing, live ? "ライブ変換" : "Space で変換");
            if (!live) Assert.Equal("私は|，|林檎を|，|食べた|．", string.Join("|", k.Host.View!.Clauses!));
            k.Type("\n");
            Assert.Equal("私は，林檎を，食べた．", k.Host.Document, "確定");
        }
    }

    private sealed class WholeConverter : TestConverter
    {
        public override string? Convert(string hiragana, string? before) => hiragana == "わたしは、りんごを、たべた。" ? "私は、林檎を、食べた。" : null;
    }

    [Test]
    public static void SegmentationAlternatives_ShowWhatIsCommitted()
    {
        // 英日の区切りの別案があると文全体が 1 つの文節になる。文の途中の打った 。 (娘。 の後ろ) も、確定と同じ文字を見せる
        var k = new CompositionTests.Keyboard(converter: new MorningConverter()) { Punctuation = Period };
        k.Type("tanniwotoru,mo-ningumusume.suki. ");
        Assert.Equal(1, k.Host.View!.Clauses!.Count, "文全体の 1 文節: " + string.Join("|", k.Host.View.Clauses));
        var shown = k.Showing;
        k.Type("\n");
        Assert.Equal(k.Host.Document, shown, "表示と確定が同じ");
        Assert.True(shown!.EndsWith("モーニング娘。．好き．", StringComparison.Ordinal), shown);
    }

    /// <summary>読みの 、。 で文節を区切り、モーニング娘。 のように 。 を含む語を返す変換エンジン。</summary>
    private sealed class MorningConverter : TestConverter
    {
        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) =>
            System.Text.RegularExpressions.Regex.Matches(hiragana, "[^、。]+|[、。]").Select(m => new ConversionClause(m.Value, Map(m.Value))).ToList();

        private static string Map(string reading) => reading switch
        {
            "もーにんぐむすめ" => "モーニング娘。",
            "もーにんぐむすめは" => "モーニング娘。は",
            "すき" => "好き",
            "もーにんぐむすめは、すき" => "モーニング娘。は、好き",
            "かおもじ" => "顔文字",
            _ => reading,
        };
    }
}
