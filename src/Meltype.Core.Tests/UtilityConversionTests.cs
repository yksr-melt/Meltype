// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Composition.Utility;
using Meltype.Engine;
using Meltype.Input;
using Meltype.Learning;
using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

internal static class UtilityConversionTests
{
    private static readonly DateTime Tuesday = new(2026, 11, 10, 20, 5, 0);

    /// <summary>決めた文節に区切る変換エンジン。</summary>
    private sealed class SplitConverter(Dictionary<string, ConversionClause[]> clauses) : TestConverter
    {
        private readonly FakeConverter _inner = new();
        public override string? Convert(string hiragana, string? before) => ConvertClauses(hiragana, before) is { } parts ? string.Concat(parts.Select(c => c.Text)) : _inner.Convert(hiragana, before);
        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) =>
            clauses.TryGetValue(hiragana, out var result) ? result : _inner.ConvertClauses(hiragana, before);

    }

    /// <summary>変換して、候補の一覧を広げてから text を選んで確定する。</summary>
    private static void Choose(Keyboard k, string typed, string text)
    {
        k.Type(typed + " ");
        k.Press(VirtualKeys.Space);
        var index = k.Host.View!.Candidates.ToList().IndexOf(text);
        Assert.True(index >= 0, $"{typed} の候補に {text}: " + string.Join(",", k.Host.View.Candidates));
        k.SelectCandidate(index);
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
        Assert.Equal("〇〇一|(001)|（001）", string.Join("|", KanjiNumberConversion.Candidates("001")), "先頭が 0 の数字は位取りしない");
        var numbered = new Keyboard();
        numbered.Type("1 ");
        Assert.True(numbered.Host.View!.Candidates.Contains("(1)") && numbered.Host.View!.Candidates.Contains("（1）"), "箇条書きの番号 (issue #2256)");
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
                Assert.Equal(!kept, StaleWords.IsStale(reading, text), $"{reading} → {text}");
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
            var words = Libraries.Lexicon.DictionarySource.DataLines(Libraries.Lexicon.DictionarySource.ReadEmbedded(name)).Select(l => l.Split('\t')[0]);
            Assert.Equal("", string.Join(",", words.Where(w => Kanji(w).Count > 0).Take(10)), name);
        }
    }

    private static List<string> Kanji(string reading) => KanjiPartsConversion.Find(reading);

    [Test]
    public static void Issue1868_VersionShowsCommitFrontAndZenzai()
    {
        // リリースは版だけ、タグの無い自己ビルドはコミット ID も付ける。どれも OS とフロント・Zenzai の設定を付ける
        Assert.Equal("Meltype-1.5.1 (Windows / Meltype IME、Zenzai: ON)", VersionConversion.Text("1.5.1", "Windows / Meltype IME", zenzai: true, otherTipVersion: null));
        Assert.Equal("Meltype-1.5.1+abc1234 (Linux / fcitx5、Zenzai: OFF)", VersionConversion.Text("1.5.1+abc1234", "Linux / fcitx5", zenzai: false, otherTipVersion: null));
        Assert.True(Predictor.IsVersionText(VersionConversion.Text("1.5.1+abc1234", "Linux", zenzai: true, otherTipVersion: null)), "学習・予測には使わない");
        var k = new CompositionTests.Keyboard();
        k.Settings.UseZenzai = false;
        k.Type("ba-jonn ");
        Assert.True(k.Host.View!.Candidates.Contains(k.VersionCandidate), string.Join(" ", k.Host.View.Candidates));
        Assert.True(k.VersionCandidate.StartsWith($"Meltype-{Meltype.Config.ProjectInfo.WithCommit(Meltype.Config.ProjectInfo.CoreVersion)} (", StringComparison.Ordinal) &&
            k.VersionCandidate.EndsWith("、Zenzai: OFF)", StringComparison.Ordinal), k.VersionCandidate);
    }

    [Test]
    public static void Issue1868_VersionShowsOtherTipVersionOnlyWhenItDiffers()
    {
        // 登録済みの Meltype IME が別のビルドのときだけ、そのことと、その Meltype IME を同梱した Meltype の版を付ける
        Assert.Equal("Meltype-1.6.0 (Windows / Meltype キーボード、Zenzai: ON、Meltype IME は別の版: 1.5.1)",
            VersionConversion.Text("1.6.0", "Windows / Meltype キーボード", zenzai: true, otherTipVersion: "1.5.1"));
        var stale = new CompositionTests.Keyboard { OtherTipVersion = "1.5.1+abc1234" };
        stale.Type("ba-jonn ");
        Assert.True(stale.Host.View!.Candidates.Any(c => c.EndsWith("、Meltype IME は別の版: 1.5.1+abc1234)", StringComparison.Ordinal)), string.Join(" ", stale.Host.View.Candidates));
        var same = new CompositionTests.Keyboard();
        same.Type("ba-jonn ");
        Assert.True(!same.Host.View!.Candidates.Any(c => c.Contains("別の版", StringComparison.Ordinal)), "同じビルドなら付けない");
    }

    [Test]
    public static void Issue1868_LinuxFrontFromCommandLine()
    {
        // Linux のフロントは、Core を読み込んだプロセスのコマンドラインで分かる
        Assert.Equal("fcitx5", VersionConversion.LinuxFrontOf(["/usr/bin/fcitx5", "-d"]));
        Assert.Equal("IBus", VersionConversion.LinuxFrontOf(["python3", "/opt/meltype/ibus-engine-meltype", "--ibus"]));
        Assert.Equal("端末", VersionConversion.LinuxFrontOf(["/usr/bin/python3", "/home/u/Meltype-tty/meltype-tty"]));
        Assert.Equal(null, VersionConversion.LinuxFrontOf(["python3", "src/Meltype.Core.CApi/tests/test-native-boundaries.py", "libMeltypeNative.so"]));
    }

    [Test]
    public static void ColorConversion_ConvertsHexRgbAndHsl()
    {
        Assert.Equal("#3b82f6|rgb(59, 130, 246)|hsl(217, 91%, 60%)", string.Join("|", ColorConversion.Candidates("#3b82f6")));
        Assert.Equal("#f00|#ff0000|rgb(255, 0, 0)|hsl(0, 100%, 50%)", string.Join("|", ColorConversion.Candidates("#f00")));
        Assert.Equal("#f008|#ff000088|rgba(255, 0, 0, 0.533)|hsla(0, 100%, 50%, 0.533)", string.Join("|", ColorConversion.Candidates("#f008")));
        Assert.Equal("rgb(59, 130, 246)|#3b82f6|#3B82F6|hsl(217, 91%, 60%)", string.Join("|", ColorConversion.Candidates("rgb(59, 130, 246)")));
        Assert.Equal("hsl(217, 91%, 60%)|#3c83f6|#3C83F6|rgb(60, 131, 246)", string.Join("|", ColorConversion.Candidates("hsl(217, 91%, 60%)")));
        Assert.Equal("rgba(255, 0, 0, 0.5)|#ff000080|#FF000080|hsla(0, 100%, 50%, 0.5)", string.Join("|", ColorConversion.Candidates("rgba(255, 0, 0, 0.5)")));
        Assert.Equal(0, ColorConversion.Candidates("rgb(256, 0, 0)").Count);
        Assert.Equal(0, ColorConversion.Candidates("#12345").Count);

        var k = new Keyboard();
        k.Settings.PredictiveCandidates = true;
        k.Type("#3b82f6");
        Assert.Equal("rgb(59, 130, 246)|hsl(217, 91%, 60%)", string.Join("|", k.Host.View!.Predictions!));
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Return);
        Assert.Equal("rgb(59, 130, 246)", k.Host.Document);

        var convert = new Keyboard();
        convert.Type("#3b82f6");
        convert.Press(VirtualKeys.Convert);
        Assert.Equal("#3b82f6", convert.Host.View!.Candidates[0]);
        Assert.True(convert.Host.View.Candidates.Contains("rgb(59, 130, 246)") && convert.Host.View.Candidates.Contains("hsl(217, 91%, 60%)"), string.Join(",", convert.Host.View.Candidates));

        // Space は英数字の確定＋空白のまま邪魔しない
        var space = new Keyboard();
        space.Type("#3b82f6 ");
        Assert.Equal("#3b82f6 ", space.Host.Document);

        // 色見本用の RGBA パース
        Assert.True(ColorConversion.TryParseRgba("#3b82f6", out var hexRgba) && hexRgba == (59, 130, 246, 255), "HEX を RGBA にパースできる");
        Assert.True(ColorConversion.TryParseRgba("rgba(255, 0, 0, 0.5)", out var alphaRgba) && alphaRgba == (255, 0, 0, 128), "RGBA をパースできる");
        Assert.True(ColorConversion.TryParseRgba("hsl(0, 100%, 50%)", out var hslRgba) && hslRgba == (255, 0, 0, 255), "HSL を RGBA にパースできる");
        Assert.True(!ColorConversion.TryParseRgba("#3b82f6 Issueタイトル", out _), "カラーコード以外の文字が混ざる候補はパースしない");

        // #123456 は GitHubIssueConversion の後ろに共存し、学習には残らない
        var issueHistory = new ConversionHistory(null);
        var issuePhrases = new PhraseHistory(null);
        var titles = new GitHubIssueTitles((repo, number) => Task.FromResult<string?>(repo == "yksr-melt/Meltype" && number == 123456 ? "テストIssue" : null));
        var sixDigits = new Keyboard(history: issueHistory, predictor: new Predictor(issuePhrases, null, issueHistory)) { GitHubRepository = "yksr-melt/Meltype", GitHubTitles = titles };
        sixDigits.Type("#123456");
        sixDigits.Press(VirtualKeys.Convert);
        Assert.Equal("#123456", sixDigits.Host.View!.Candidates[0], "#123456 の先頭は打ったまま");
        Assert.True(sixDigits.Host.View.Candidates.Contains("#123456 テストIssue"), "GitHub Issue 候補を維持する");
        Assert.True(sixDigits.Host.View.Candidates.Contains("rgb(18, 52, 86)"), "後ろに色変換も出す");
        var learnCheck = new Keyboard(history: issueHistory, predictor: new Predictor(issuePhrases, null, issueHistory));
        learnCheck.Type("#3b82f6");
        learnCheck.Press(VirtualKeys.Convert);
        learnCheck.Press(VirtualKeys.Return);
        Assert.Equal("#3b82f6", learnCheck.Host.Document);
        Assert.True(issueHistory.Get("#3b82f6") is null && !issuePhrases.StartingWith("#3b82f6").Any(), "カラーコード変換は学習しない");
    }
}
