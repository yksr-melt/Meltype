// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Composition;
using Meltype.Composition.Utility;
using Meltype.Input;
using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

internal static class UtilityConversionTests
{
    private static readonly DateTime Tuesday = new(2026, 11, 10, 20, 5, 0);

    /// <summary>決めた文節に区切る変換エンジン。</summary>
    private sealed class SplitConverter(Dictionary<string, ConversionClause[]> clauses) : IKanjiConverter, ILearningConverter
    {
        private readonly FakeConverter _inner = new();
        public List<string> Learned { get; } = [];
        public string? Convert(string hiragana) => ConvertClauses(hiragana) is { } parts ? string.Concat(parts.Select(c => c.Text)) : _inner.Convert(hiragana);
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            clauses.TryGetValue(hiragana, out var result) ? result : _inner.ConvertClauses(hiragana, context);

        public void Learn(string? context, IReadOnlyList<ConversionClause> learned)
        {
            lock (Learned) Learned.Add(string.Join("|", learned.Select(c => $"{c.Reading}={c.Text}")));
        }
    }

    /// <summary>変換して、候補の一覧を広げてから text を選んで確定する。</summary>
    private static void Choose(Keyboard k, string typed, string text)
    {
        k.Type(typed + " ");
        k.Press(VirtualKeys.Space);
        var index = k.Host.View!.Candidates.ToList().IndexOf(text);
        Assert.True(index >= 0, $"{typed} の候補に {text}: " + string.Join(",", k.Host.View.Candidates));
        k.Controller.SelectCandidate(index);
        k.Press(VirtualKeys.Return);
    }

    [Test]
    public static void EveryConversionIsListed()
    {
        var types = typeof(IUtilityConversion).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(IUtilityConversion).IsAssignableFrom(t));
        foreach (var type in types)
            Assert.True(UtilityConversions.All.Any(c => c.GetType() == type), $"{type.Name} が UtilityConversions.All に無い");
    }

    [Test]
    public static void Today_IsOfferedOnlyForTheWholeInput()
    {
        var k = new Keyboard(now: () => Tuesday);
        k.Type("kyou ");
        Assert.Equal("今日", k.Host.View!.Candidates[0], "普通の候補が先");
        Assert.True(k.Host.View.Candidates.Contains("2026年11月10日"), string.Join(",", k.Host.View.Candidates));
        var sentence = new Keyboard(now: () => Tuesday);
        sentence.Type("kyouha ");
        Assert.True(!sentence.Host.View!.Candidates.Contains("2026年11月10日"), "文の一部の きょう には日付を出さない: " + string.Join(",", sentence.Host.View.Candidates));
    }

    [Test]
    public static void UtilityClause_JoinsWhatTheEngineSplit()
    {
        var converter = new SplitConverter(new() { ["じこうのあいさつ"] = [new("じこうの", "時候の"), new("あいさつ", "挨拶")] });
        var k = new Keyboard(converter: converter, now: () => Tuesday);
        k.Type("jikounoaisatu ");
        Assert.Equal("時候の挨拶", string.Join("|", k.Host.View!.Clauses!), "1 つの文節にまとめる");
        Assert.Equal("時候の挨拶", k.Host.View.Candidates[0], "普通の候補が先");
        Assert.True(k.Host.View.Candidates.Contains("晩秋の候"), string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void UtilityCandidates_AreNotLearned()
    {
        var history = new ConversionHistory(null);
        var phrases = new PhraseHistory(null);
        var k = new Keyboard(history: history, predictor: new Predictor(phrases, null, history), now: () => Tuesday);
        Choose(k, "jikou", "晩秋の候");
        Assert.Equal("晩秋の候", k.Host.Document);
        Assert.True(history.Get("じこう") is null, "時候の挨拶を覚えない");
        Assert.True(!phrases.StartingWith("じこう").Any(), "予測変換にも覚えない");
        Choose(k, "3syaku", "約90.91cm");
        Assert.True(history.Get("3しゃく") is null, "尺貫法を覚えない");
    }

    [Test]
    public static void EngineDateAndVersion_AreNotLearned()
    {
        // 変換エンジン (Mozc) が今日の日時・版から作る候補 (あした → 2026/11/11、ことし → 令和八年) も覚えない
        var history = new ConversionHistory(null);
        var phrases = new PhraseHistory(null);
        IReadOnlyList<string> More(string reading) => reading switch
        {
            "あした" => ["明日", "2026/11/11", "水曜日"],
            "ことし" => ["今年", "令和八年"],
            "ばーじょん" => ["バージョン", "Mozc-2.31.5851.102"],
            _ => [],
        };
        foreach (var (typed, reading, text) in new[] { ("asita", "あした", "2026/11/11"), ("asita", "あした", "水曜日"), ("kotosi", "ことし", "令和八年"), ("ba-jonn", "ばーじょん", "Mozc-2.31.5851.102") })
        {
            var k = new Keyboard(history: history, predictor: new Predictor(phrases, null, history), moreCandidates: More, now: () => Tuesday);
            Choose(k, typed, text);
            Assert.True(history.Get(reading) is null, $"{reading} → {text} を覚えない");
        }
        Assert.True(!phrases.StartingWith("ことし").Any(), "予測変換にも覚えない");
        var ordinary = new Keyboard(history: history, moreCandidates: More, now: () => Tuesday);
        Choose(ordinary, "asita", "明日");
        Assert.Equal("明日", history.Get("あした"), "普通の語は覚える");
        var weekday = new Keyboard(history: history, moreCandidates: reading => reading == "かようび" ? ["火曜日"] : [], now: () => Tuesday);
        Choose(weekday, "kayoubi", "火曜日");
        Assert.Equal("火曜日", history.Get("かようび"), "読みに ようび がある 火曜日 は覚える");
    }

    [Test]
    public static void DigitsNotInTheReading_AreLeftToTheEngineToLearn()
    {
        // いちばん → 1番 は Meltype の変換履歴には覚えないが、Mozc には覚えさせる (Mozc は日時の候補を自分で学習しない)
        var history = new ConversionHistory(null);
        var converter = new SplitConverter([]);
        var k = new Keyboard(converter: converter, history: history, moreCandidates: reading => reading == "いちばん" ? ["一番", "1番"] : []);
        Choose(k, "itiban", "1番");
        Assert.True(history.Get("いちばん") is null, "Meltype の変換履歴には覚えない");
        for (var i = 0; i < 100 && converter.Learned.Count == 0; i++) Thread.Sleep(10);
        lock (converter.Learned) Assert.Equal("いちばん=1番", converter.Learned.SingleOrDefault() ?? "(なし)");
    }
}
