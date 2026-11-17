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
    public static void ChoiceInAJoinedClause_IsLearned()
    {
        // 変換エンジンが分けた文節 (じこうの|あいさつ) を 1 つにまとめても、選び直した普通の候補は変換エンジンに覚えさせる (#1716 のレビュー)
        var converter = new SplitConverter(new() { ["じこうのあいさつ"] = [new("じこうの", "時候の"), new("あいさつ", "挨拶")] });
        IReadOnlyList<string> More(string reading) => reading == "じこうのあいさつ" ? ["時候のあいさつ"] : [];
        Choose(new Keyboard(converter: converter, moreCandidates: More, now: () => Tuesday), "jikounoaisatu", "時候のあいさつ");
        for (var i = 0; i < 100 && converter.Learned.Count == 0; i++) Thread.Sleep(10);
        lock (converter.Learned) Assert.Equal("じこうのあいさつ=時候のあいさつ", converter.Learned.SingleOrDefault() ?? "(なし)");
    }

    [Test]
    public static void UtilityCandidates_AreNotLearned()
    {
        var converter = new LearningConverter();
        var phrases = new PhraseHistory(null);
        var k = new Keyboard(converter: converter, predictor: new Predictor(phrases, null), now: () => Tuesday);
        Choose(k, "jikou", "晩秋の候");
        Assert.Equal("晩秋の候", k.Host.Document);
        Assert.True(!converter.HasLearned("じこう", "晩秋の候", expected: false), "時候の挨拶を覚えない");
        Assert.True(!phrases.StartingWith("じこう").Any(), "予測変換にも覚えない");
        Choose(k, "3syaku", "約90.91cm");
        Assert.True(!converter.HasLearned("3しゃく", "約90.91cm", expected: false), "尺貫法を覚えない");
    }

    [Test]
    public static void EngineDateAndVersion_AreNotPredicted()
    {
        // 変換エンジン (Mozc) が今日の日時・版から作る候補 (あした → 2026/11/11、ことし → 令和八年) は、予測変換の語句に覚えない
        var phrases = new PhraseHistory(null);
        IReadOnlyList<string> More(string reading) => reading switch
        {
            "あした" => ["明日", "2026/11/11", "水曜日"],
            "ことし" => ["今年", "令和八年"],
            "ばーじょん" => ["バージョン", "Mozc-2.31.5851.102"],
            _ => [],
        };
        foreach (var (typed, text) in new[] { ("asita", "2026/11/11"), ("asita", "水曜日"), ("kotosi", "令和八年"), ("ba-jonn", "Mozc-2.31.5851.102") })
        {
            var k = new Keyboard(predictor: new Predictor(phrases, null), moreCandidates: More, now: () => Tuesday);
            Choose(k, typed, text);
        }
        Assert.Equal(0, phrases.Count, string.Join(",", phrases.Entries()));
    }

    [Test]
    public static void DigitsNotInTheReading_AreLeftToTheEngineToLearn()
    {
        // いちばん → 1番 も Mozc に覚えさせる (Mozc は日時の候補を自分で学習しない)
        var converter = new SplitConverter([]);
        var k = new Keyboard(converter: converter, moreCandidates: reading => reading == "いちばん" ? ["一番", "1番"] : []);
        Choose(k, "itiban", "1番");
        for (var i = 0; i < 100 && converter.Learned.Count == 0; i++) Thread.Sleep(10);
        lock (converter.Learned) Assert.Equal("いちばん=1番", converter.Learned.SingleOrDefault() ?? "(なし)");
    }

    [Test]
    public static void KanjiNumbers_AreOfferedForDigitsOnly()
    {
        // 変換エンジンによっては打った数字を漢数字にしないので、Meltype が出す (issue #344)
        var k = new Keyboard();
        k.Type("2026 ");
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Contains("二〇二六") && candidates.Contains("二千二十六") && candidates.Contains("弐千弐拾六"), string.Join(",", candidates));
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
            foreach (var (reading, text, kept) in new[]
            {
                ("あした", "2026/11/11", false), ("きのう", "月曜日", false), ("ことし", "令和八年", false), ("ばーじょん", "Meltype-1.4.1", false),
                ("じこう", "晩秋の候", false), ("くりっぷぼーど", "パスワード123", false), ("ほかの", "！\uFFFC他のメッセージオプション", false),
                ("きょう", "京", true), ("かようび", "火曜日", true), ("じこう", "時効", true), ("れいわ8ねん", "2026年", true), ("くりっぷぼーど", "クリップボード", true),
            })
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
        // 画面の文字 (U+FFFC を含む) が混ざった候補は、選んでも予測変換の語句に覚えない (issue #1537)
        var phrases = new PhraseHistory(null);
        var k = new Keyboard(predictor: new Predictor(phrases, null), moreCandidates: reading => reading == "ほかの" ? ["他の", "！\uFFFC他のメッセージオプション"] : []);
        Choose(k, "hokano", "！\uFFFC他のメッセージオプション");
        Assert.Equal(0, phrases.Count, "覚えない");
        var ordinary = new Keyboard(predictor: new Predictor(phrases, null), moreCandidates: reading => reading == "ほかの" ? ["他の", "外の"] : []);
        Choose(ordinary, "hokano", "外の");
        Assert.Equal("外の", string.Join(",", phrases.Entries().Select(e => e.Text)), "普通の語は覚える");
    }

    [Test]
    public static void Issue345_KanjiFromRadicalNames()
    {
        // #345: 部首の呼び名を「に」でつなぐと、それらを含む漢字を、説明どおりの字から最初の候補に出す。
        // 後ろに変換エンジンの候補 (言偏にふるとり。字の書き方を説明するときに打つ) も残す
        var converter = new SplitConverter(new() { ["ごんべんにふるとり"] = [new("ごんべんに", "言偏に"), new("ふるとり", "ふるとり")] });
        var k = new Keyboard(converter: converter, now: () => Tuesday);
        k.Type("gonbennnifurutori ");
        var shown = k.Host.View!.Candidates.ToList();
        Assert.Equal("誰", k.Host.View!.Candidates[k.Host.View.SelectedIndex], string.Join(",", shown));
        Assert.Equal("誰", shown[0], string.Join(",", shown));
        Assert.True(shown.Contains("言偏にふるとり") && shown.Contains("ごんべんにふるとり"), "変換エンジンの候補とかなも残す: " + string.Join(",", shown));

        // 候補の一覧を広げても、変換エンジンの候補は漢字の後ろに入る (漢字の間に入らない)
        IReadOnlyList<string> More(string reading) => reading == "ごんべんにふるとり" ? ["言偏にふるとり", "ごんべんにふるとり"] : [];
        var expanded = new Keyboard(converter: converter, moreCandidates: More, now: () => Tuesday);
        expanded.Type("gonbennnifurutori ");
        var all = expanded.Host.View!.Candidates.ToList();
        Assert.True(all.IndexOf("言偏にふるとり") > all.IndexOf("讎"), string.Join(",", all));

        // 説明どおりの字 (余分な要素の無い字) が先。ほかの要素も含む字 (護・体・深) はその後ろ
        Assert.Equal("誰", Kanji("ごんべんにふるとり")[0], string.Join(",", Kanji("ごんべんにふるとり")));
        Assert.Equal("休", Kanji("にんべんにき")[0], string.Join(",", Kanji("にんべんにき")));
        Assert.Equal("沐", Kanji("さんずいにき")[0], string.Join(",", Kanji("さんずいにき")));
        // 呼び名の揺れ (ことばへん)。呼び名の中の「に」(にんべん) は区切りでない
        Assert.Equal("誰", Kanji("ことばへんにふるとり")[0]);
    }

    [Test]
    public static void Issue345_EverydayWordsAreNotRadicalNames()
    {
        // 逆向き: 呼び名 1 つだけ・「と」でつないだもの・呼び名でない語は出さない
        Assert.True(Kanji("ごんべん").Count == 0, "1 つだけ");
        Assert.True(Kanji("ごんべんとふるとり").Count == 0, "と ではつながない");
        // 「名詞 に 動詞」の句 (きにかける = 気にかける)。先頭は部首にしか使わない名前だけにする
        foreach (var phrase in new[] { "ひとくち", "ひとつき", "ひととき", "ひとめ", "かねとひと", "いしとき", "にほんにいく", "きのうにいった",
            "きにかける", "ひにかける", "めにみる", "てにいれる", "みみにいれる", "こころにきめる", "かいにいく", "やまにのぼる", "かわにいく", "いしにかく" })
            Assert.True(Kanji(phrase).Count == 0, phrase);
        // よく使う語 (readings.txt) とカタカナ語は、どれも呼び名の並びにならない。なれば、普通の変換が漢字の候補に置き換わる
        foreach (var name in new[] { "readings.txt", "readings-extra.txt", "katakana.txt" })
        {
            var words = Meltype.Detection.DictionarySource.DataLines(Meltype.Detection.DictionarySource.ReadEmbedded(name)).Select(l => l.Split('\t')[0]);
            Assert.Equal("", string.Join(",", words.Where(w => Kanji(w).Count > 0).Take(10)), name);
        }
    }

    private static List<string> Kanji(string reading) => KanjiPartsConversion.Find(reading);

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
