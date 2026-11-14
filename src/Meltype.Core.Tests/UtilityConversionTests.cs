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
    public static void ChoiceInAJoinedClause_IsUsedNextTime()
    {
        // 変換エンジンが分けた文節 (じこうの|あいさつ) を 1 つにまとめても、選び直した普通の候補は次の変換で先頭になる (#1716 のレビュー)
        var converter = new SplitConverter(new() { ["じこうのあいさつ"] = [new("じこうの", "時候の"), new("あいさつ", "挨拶")] });
        var history = new ConversionHistory(null);
        IReadOnlyList<string> More(string reading) => reading == "じこうのあいさつ" ? ["時候のあいさつ"] : [];
        Choose(new Keyboard(converter: converter, history: history, moreCandidates: More, now: () => Tuesday), "jikounoaisatu", "時候のあいさつ");
        Assert.Equal("時候のあいさつ", history.Get("じこうのあいさつ"), "普通の候補は覚える");
        var next = new Keyboard(converter: converter, history: history, moreCandidates: More, now: () => Tuesday);
        next.Type("jikounoaisatu ");
        Assert.Equal("時候のあいさつ", next.Host.View!.Candidates[next.Host.View.SelectedIndex], string.Join(",", next.Host.View.Candidates));
        Assert.True(next.Host.View.Candidates.Contains("晩秋の候"), "ユーティリティの候補も出る");
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

    [Test]
    public static void KanjiNumbers_AreOfferedForDigitsOnly()
    {
        // 変換エンジンによっては打った数字を漢数字にしないので、Meltype が出す (issue #344)
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history);
        k.Type("2026 ");
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Contains("二〇二六") && candidates.Contains("二千二十六") && candidates.Contains("弐千弐拾六"), string.Join(",", candidates));
        Choose(new Keyboard(history: history), "12345", "一万二千三百四十五");
        Assert.Equal("一万二千三百四十五", history.Get("12345"), "選んだ漢数字は覚える (issue #672)");
        Assert.Equal("〇〇一", string.Join("|", KanjiNumberConversion.Candidates("001")), "先頭が 0 の数字は位取りしない");
        Assert.Equal(0, KanjiNumberConversion.Candidates("12a").Count, "数字だけでなければ出さない");
        var sentence = new Keyboard();
        sentence.Type("2026nen ");
        Assert.True(!sentence.Host.View!.Candidates.Contains("二〇二六"), "文の途中の数字には出さない");
    }

    [Test]
    public static void StaleLearning_IsDroppedWhenLoaded()
    {
        // 前の版が覚えてしまった日時・版・時候の挨拶・クリップボードの文字は、読み込むときに捨てる。普通の語は残す
        var directory = Path.Combine(Path.GetTempPath(), $"meltype-stale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var conversions = Path.Combine(directory, "conversions.json");
            string[][] entries =
            [
                ["あした", "2026/11/11"], ["きのう", "月曜日"], ["ことし", "令和八年"], ["ばーじょん", "Meltype-1.4.1"],
                ["じこう", "晩秋の候"], ["くりっぷぼーど", "パスワード123"], ["ほかの", "！\uFFFC他のメッセージオプション"],
                ["きょう", "京"], ["かようび", "火曜日"], ["じこう", "時効"], ["れいわ8ねん", "2026年"], ["くりっぷぼーど", "クリップボード"],
            ];
            File.WriteAllText(conversions, "{" + string.Join(",", entries.Take(9).Select(e => $"\"{e[0]}\":{{\"Text\":\"{e[1]}\",\"Used\":\"2026-11-01T00:00:00Z\"}}")) + "}");
            var history = new ConversionHistory(conversions);
            foreach (var reading in new[] { "あした", "きのう", "ことし", "ばーじょん", "ほかの" }) Assert.True(history.Get(reading) is null, $"{reading} は捨てる");
            Assert.Equal("京", history.Get("きょう"));
            Assert.Equal("火曜日", history.Get("かようび"), "読みに ようび がある語は残す");
            foreach (var (reading, text, kept) in new[] { ("じこう", "晩秋の候", false), ("くりっぷぼーど", "パスワード123", false), ("じこう", "時効", true), ("れいわ8ねん", "2026年", true), ("くりっぷぼーど", "クリップボード", true) })
                Assert.Equal(!kept, CompositionLearning.IsStale(reading, text), $"{reading} → {text}");
            var phrases = Path.Combine(directory, "phrases.txt");
            File.WriteAllLines(phrases, ["きょうは\t2026/11/10は\t3\t0", "きょうは\t今日は\t3\t0", "きょうの\t今日の！\uFFFC他のメッセージオプション\t3\t0"]);
            Assert.Equal("今日は", string.Join(",", new PhraseHistory(phrases).StartingWith("きょ")), "予測変換の語句も捨てる");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void ScreenTextInACandidate_IsNotLearned()
    {
        // 画面の文字 (U+FFFC を含む) が混ざった候補は、選んでも覚えない (issue #1537)
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history, moreCandidates: reading => reading == "ほかの" ? ["他の", "！\uFFFC他のメッセージオプション"] : []);
        Choose(k, "hokano", "！\uFFFC他のメッセージオプション");
        Assert.True(history.Get("ほかの") is null, "変換履歴に覚えない");
        var ordinary = new Keyboard(history: history, moreCandidates: reading => reading == "ほかの" ? ["他の", "外の"] : []);
        Choose(ordinary, "hokano", "外の");
        Assert.Equal("外の", history.Get("ほかの"), "普通の語は覚える");
    }

    [Test]
    public static void Issue1868_VersionShowsCommitAndFront()
    {
        // リリースは版だけ、タグの無い自己ビルドはコミット ID も付ける。どちらも OS とフロントを付ける
        Assert.Equal("Meltype-1.5.1 (Windows / Meltype IME)", VersionConversion.Text("1.5.1", "Windows / Meltype IME"));
        Assert.Equal("Meltype-1.5.1+abc1234 (Linux)", VersionConversion.Text("1.5.1+abc1234", "Linux"));
        Assert.True(Predictor.IsVersionText(VersionConversion.Text("1.5.1+abc1234", "Linux")), "学習・予測には使わない");
        var candidate = VersionConversion.Candidate(new Meltype.Config.Settings());
        Assert.True(candidate.StartsWith($"Meltype-{Meltype.Config.ProjectInfo.WithCommit(Meltype.Config.ProjectInfo.CoreVersion)} (", StringComparison.Ordinal) && candidate.EndsWith(')'), candidate);
    }
}
