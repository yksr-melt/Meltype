// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Detection;
using static Meltype.Tests.TestSupport;

namespace Meltype.Tests;

internal static class DetectionTests
{
    private static readonly ScoreEngine Engine = CreateEngine();

    private static void ExpectJapanese(params string[] words)
    {
        foreach (var word in words)
        {
            var result = Classify(Engine, word);
            Assert.True(result.Verdict == Verdict.Japanese, $"「{word}」は日本語と判定されるべき: {result.Describe()}");
        }
    }

    private static void ExpectNotJapanese(params string[] words)
    {
        foreach (var word in words)
        {
            var result = Classify(Engine, word);
            Assert.True(result.Verdict != Verdict.Japanese, $"「{word}」を日本語と誤判定した: {result.Describe()}");
        }
    }

    [Test] public static void Design_Japanese() => ExpectJapanese("konnichiwa", "arigatou", "ohayou", "watashi", "ashita");

    [Test] public static void Design_Typo() => ExpectJapanese("konnitiwa", "konnichia", "arigatouu");

    [Test] public static void ParticleThenWord_AndCRow() => ExpectJapanese("notasuku", "gamenwo", "fucarete");

    [Test] public static void Design_English() => ExpectNotJapanese("hello", "github", "typescript", "javascript", "server", "terminal");

    [Test]
    public static void Design_TechnicalInput()
    {
        foreach (var line in new[] { "npm install", "git commit", "git push", "localhost", "http://" })
        {
            foreach (var word in line.Split(' ')) ExpectNotJapanese(word);
        }
    }

    [Test] public static void Design_UnknownStaysUnchanged() => ExpectNotJapanese("test");

    [Test] public static void Design_RomajiReadableEnglishIsNotEnough() => ExpectNotJapanese("kana", "sushi", "radio");

    [Test]
    public static void MoreJapanese() => ExpectJapanese(
        "kyou", "desu", "masu", "sugoi", "tabemasu", "shigoto", "tomodachi", "daijoubu", "yoroshiku", "otsukare",
        "sumimasen", "onegaishimasu", "hontou", "mainichi", "nihongo", "kaigi", "shiryou", "kakunin", "ryoukai",
        "wakarimashita", "itadakimasu", "gomennasai", "zenzen", "chotto", "tanoshii", "kanashii", "benri");

    [Test]
    public static void ReportedMisses() => ExpectJapanese("himadana", "onakagasuita", "nemui", "tsukareta", "oishii", "ganbatte", "yukkuri");

    [Test]
    public static void NewDictionaryWordsDoNotCatchEnglish() => ExpectNotJapanese(
        "haha", "hair", "suitable", "semantic", "madonna", "naked", "nerd", "hotel", "music", "seminar", "motive", "kidney");

    [Test]
    public static void KunreiSpellingsAreJapanese() => ExpectJapanese("sigoto", "tomodati", "otukare", "arigatou", "siryou", "konniti");

    [Test]
    public static void CommonEnglishIsNotJapanese() => ExpectNotJapanese(
        "the", "and", "you", "make", "take", "same", "home", "time", "name", "made", "note", "open", "done",
        "tomorrow", "anatomy", "monday", "should", "shirt", "kitten", "matter", "item", "setting", "remote",
        "minute", "banana", "hamburger", "karate", "karaoke", "tsunami", "anime", "manga", "ninja", "samurai",
        "tofu", "kimono", "sake", "sumo", "tokyo", "kyoto", "honda", "toyota", "nintendo", "pokemon", "kawaii",
        "sensei", "senpai", "matcha", "wasabi", "tempura", "emoji", "sudoku", "demo", "node", "none", "kite");

    [Test]
    public static void EnglishSentencesNeverSwitch()
    {
        const string text =
            "The quick brown fox jumps over the lazy dog. Please review my pull request before the meeting tomorrow. " +
            "I think we need to update the documentation and fix the remaining bugs. Can you send me the latest " +
            "build? The server returned an error when I tried to deploy the new version. Let me know if you have " +
            "any questions about the design. We should make a decision on the database migration by Monday. " +
            "Our team will take care of the performance issue this week. Thanks for your help, see you soon. " +
            "Remote work is going well and the new monitor is great. Open the terminal and run the tests again. " +
            "Maybe we can meet at noon near the station. Take a look at the image I attached to the ticket.";
        var words = text.Split([' ', '.', ',', '?', '!'], StringSplitOptions.RemoveEmptyEntries);
        ExpectNotJapanese(words);
    }

    [Test]
    public static void ObviousEnglishIsDecidedOnFirstKey()
    {
        foreach (var letter in "lqvx")
        {
            var result = Engine.Evaluate(new DetectionInput(letter.ToString(), [char.ToUpperInvariant(letter)], false));
            Assert.Equal(Verdict.English, result.Verdict, $"'{letter}'");
        }
    }

    [Test]
    public static void ObviousEnglishKeysRemainKanaInKanaInput()
    {
        var expectedKana = new Dictionary<int, string>
        {
            [0x51] = "た", // Q
            [0x58] = "さ", // X
            [0x56] = "ひ", // V
            [0x4C] = "り", // L
        };

        var settings = DefaultSettings();
        settings.InputStyle = InputStyle.Kana;
        var engine = CreateEngine(settings);

        foreach (var (key, kana) in expectedKana)
        {
            var letter = char.ToLowerInvariant((char)key).ToString();
            var result = engine.Evaluate(new DetectionInput(letter, [key], true));
            Assert.True(result.Verdict != Verdict.English, $"{letter.ToUpperInvariant()} は JIS かな入力の {kana} として扱う: {result.Describe()}");
            Assert.Equal(kana, KanaDetector.ToKana([key]));
        }
    }

    [Test]
    public static void MaxPendingKeys_EndsWithoutWaitingForMore()
    {
        // 上限の文字数まで判定できなければ、打ち終わる前でも結論 (判断できない) を出す (英数状態の検知が終わらないままにならない)
        var input = new DetectionInput("tab", "TAB".Select(c => (int)c).ToArray(), false);
        Assert.Equal(Verdict.Undecided, Engine.Evaluate(input).Verdict, "上限より短いうちは待つ");
        var settings = DefaultSettings();
        settings.MaxPendingKeys = 3;
        Assert.Equal(Verdict.Unknown, CreateEngine(settings).Evaluate(input).Verdict, "上限の文字数で結論を出す");
    }

    [Test]
    public static void EnglishIsDecidedEarly()
    {
        Assert.Equal("hel", Classify(Engine, "hello").Text);
        // the → てぇ は CompositionTable のみ。th は英語辞書の接頭辞で EN+3 となり、th 時点で英語確定する。
        Assert.Equal("th", Classify(Engine, "the").Text);
        Assert.Equal("np", Classify(Engine, "npm").Text);
    }

    [Test]
    public static void JapaneseIsDecidedWithinFourLetters()
    {
        Assert.Equal("konn", Classify(Engine, "konnichiwa").Text);
        Assert.Equal("wata", Classify(Engine, "watashi").Text);
        Assert.Equal("kyou", Classify(Engine, "kyouha").Text);
    }

    [Test]
    public static void HigherThresholdIsMoreConservative()
    {
        var strict = DefaultSettings();
        strict.JapaneseThreshold = 12;
        var engine = CreateEngine(strict);
        Assert.True(Classify(engine, "konnichiwa").Verdict != Verdict.Japanese, "閾値を上げたら切り替えない");
    }

    [Test]
    public static void KanaInputStyle()
    {
        var settings = DefaultSettings();
        settings.InputStyle = InputStyle.Kana;
        var engine = CreateEngine(settings);
        int[] keys = [0x42, 0x59, 0x49, 0x41, 0x46];
        DetectionResult? result = null;
        for (var i = 1; i <= keys.Length; i++)
        {
            var letters = new string(keys[..i].Select(k => (char)('a' + k - 0x41)).ToArray());
            result = engine.Evaluate(new DetectionInput(letters, keys[..i], i == keys.Length));
            if (result.Verdict != Verdict.Undecided) break;
        }
        Assert.Equal(Verdict.Japanese, result!.Verdict, result.Describe());
        Assert.Equal("こんにちは", KanaDetector.ToKana(keys));
        Assert.Equal("が", KanaDetector.ToKana([0x54, 0xC0]));
    }

    [Test]
    public static void Romaji_Analyze()
    {
        var romaji = new RomajiDetector();
        Assert.Equal("こんにちわ", romaji.Analyze("konnichiwa").Kana);
        Assert.Equal("こんにちは", romaji.Analyze("konnnichiha").Kana);
        Assert.Equal("しんぶん", romaji.AnalyzeWord("shinbun").Kana);
        Assert.Equal("きって", romaji.Analyze("kitte").Kana);
        Assert.Equal("まっちゃ", romaji.Analyze("matcha").Kana);
        Assert.Equal("しゅ", romaji.Analyze("syu").Kana);
        Assert.Equal("し", romaji.AnalyzeFragment("ci").Kana);
        Assert.True(!romaji.Analyze("ci").IsValid, "ci は変換ボックス専用");
        Assert.True(romaji.Analyze("ky").IsValid, "入力途中の子音は有効");
        Assert.True(romaji.Analyze("th").IsValid, "th は有効な部分ローマ字 (tha/thu/tho の前置詞)");
        Assert.True(!romaji.Analyze("np").IsValid, "語頭の ん は不正");
        Assert.True(!romaji.Analyze("kkk").IsValid, "語頭の っ は不正");
        Assert.True(romaji.Analyze("kyou").StrongYouon == 1, "拗音");
    }

    [Test]
    public static void Romaji_NewConversions()
    {
        var romaji = new RomajiDetector();
        // Issue #92: 判定用 Table のペア
        Assert.Equal("てゃ", romaji.Analyze("tha").Kana);
        Assert.Equal("てゅ", romaji.Analyze("thu").Kana);
        Assert.Equal("てょ", romaji.Analyze("tho").Kana);
        Assert.Equal("でゃ", romaji.Analyze("dha").Kana);
        Assert.Equal("でゅ", romaji.Analyze("dhu").Kana);
        Assert.Equal("でょ", romaji.Analyze("dho").Kana);
        Assert.Equal("にぃ", romaji.Analyze("nyi").Kana);
        Assert.Equal("にぇ", romaji.Analyze("nye").Kana);
        Assert.Equal("とぁ", romaji.Analyze("twa").Kana);
        Assert.Equal("とぃ", romaji.Analyze("twi").Kana);
        Assert.Equal("とぇ", romaji.Analyze("twe").Kana);
        Assert.Equal("とぅ", romaji.Analyze("twu").Kana);
        Assert.Equal("どぁ", romaji.Analyze("dwa").Kana);
        Assert.Equal("どぃ", romaji.Analyze("dwi").Kana);
        Assert.Equal("どぇ", romaji.Analyze("dwe").Kana);
        Assert.Equal("どぅ", romaji.Analyze("dwu").Kana);
        // CompositionTable のみ (the は英語最頻出、q は1キーで英語確定するため判定用 Table には入れない)
        Assert.Equal("てぇ", romaji.AnalyzeFragment("the").Kana);
        Assert.Equal("でぇ", romaji.AnalyzeFragment("dhe").Kana);
        Assert.Equal("くぃ", romaji.AnalyzeFragment("qi").Kana);
        Assert.Equal("くぇ", romaji.AnalyzeFragment("qe").Kana);
        Assert.Equal("くぉ", romaji.AnalyzeFragment("qo").Kana);
    }

    [Test]
    public static void Romaji_SpellingVariants()
    {
        var variants = new RomajiDetector().SpellingVariants("shigoto");
        Assert.True(variants.Contains("sigoto"), string.Join(",", variants));
        variants = new RomajiDetector().SpellingVariants("konnichiwa");
        Assert.True(variants.Contains("konnitiwa") && variants.Contains("konnnichiwa"), string.Join(",", variants));
        variants = new RomajiDetector().SpellingVariants("shinbun");
        Assert.True(variants.Contains("shinnbunn") && variants.Contains("sinbun"), string.Join(",", variants));
    }

    [Test]
    public static void Typo_Levenshtein()
    {
        Assert.Equal(0, TypoDetector.Levenshtein("abc", "abc"));
        Assert.Equal(1, TypoDetector.Levenshtein("konnichia", "konnichiwa"));
        Assert.Equal(2, TypoDetector.Levenshtein("kitten", "sitting", 1), "上限を超えたら max+1");
    }

    [Test]
    public static void Typo_AloneDoesNotSwitch()
    {
        var withoutTypo = DefaultSettings();
        withoutTypo.TypoEnabled = true;
        var engine = CreateEngine(withoutTypo);
        var result = engine.Evaluate(new DetectionInput("konic", [], false));
        var typo = result.Contributions.Where(c => c.Source == "Typo").Sum(c => c.Japanese);
        Assert.True(typo > 0 && typo < withoutTypo.JapaneseThreshold, $"Typo の加点は閾値未満であるべき: {result.Describe()}");
    }

    [Test]
    public static void Describe_HidesTypedText_UnlessRecordTextIsOn()
    {
        // ログに出す判定の説明に、打った文字を出さない (入力した文字をログに出す設定が OFF のとき)
        var result = Engine.Evaluate(new DetectionInput("kyouha", "KYOUHA".Select(c => (int)c).ToArray(), true));
        var before = Diagnostics.Log.RecordText;
        try
        {
            Diagnostics.Log.RecordText = false;
            var hidden = result.Describe();
            Assert.True(!hidden.Contains("kyou") && hidden.Contains("(6 文字)"), $"打った文字を出さない: {hidden}");
            Diagnostics.Log.RecordText = true;
            Assert.True(result.Describe().Contains("kyouha"), "ON なら出す");
        }
        finally
        {
            Diagnostics.Log.RecordText = before;
        }
    }

    [Test]
    public static void CustomRomajiTable_ExtendsSpellings()
    {
        // 拡張ローマ字 (AZIK) をユーザーのローマ字の表 (romaji.txt) で足せる (issue #125)
        var table = RomajiDetector.ParseCustomTable("# AZIK の一部\nkz\tかん\nkk\tきん\nkq\tかい\nsh すう\nbad;\tだめ\n");
        Assert.True(!table.ContainsKey("bad;"), "英字以外を含む綴りは読まない");
        var romaji = new RomajiDetector(table);
        Assert.Equal("かんじ", romaji.AnalyzeFragment("kzji").Kana);
        Assert.Equal("きんし", romaji.AnalyzeFragment("kksi").Kana + romaji.AnalyzeFragment("kksi").Partial, "kk は っ ではなく表の きん");
        Assert.Equal("かいしゃ", romaji.AnalyzeFragment("kqsya").Kana);
        var partial = romaji.AnalyzeFragment("k");
        Assert.True(partial.IsValid && partial.Partial == "k", "表の綴りの打ちかけは続きを待つ");
        // 表に無い綴りは今までどおり
        Assert.Equal("かった", romaji.AnalyzeFragment("katta").Kana);
        Assert.Equal("きっく", new RomajiDetector().AnalyzeFragment("kikku").Kana, "表が無ければ kk は っ");
        var plain = new RomajiDetector();
        Assert.Equal("しんぶん", plain.AnalyzeFragment("shinnbunn").Kana, "表が無ければ sh・nn も今までどおり");
        Assert.Equal("ん", plain.AnalyzeFragment("nn").Kana);
    }

    [Test]
    public static void CustomRomajiTable_PrefixWaitsForLongerSpelling()
    {
        // issue #979: 短い定義とそれを先頭に含む長い定義があるとき、打ちかけは ん・っ・組み込みの綴りで読まずに続きを待つ
        var table = RomajiDetector.ParseCustomTable("ry\tらい\nryp\tりょう\nngh\tにゅう\n");
        var romaji = new RomajiDetector(table);
        // 完成した入力はファイルの行順ではなく最長一致
        Assert.Equal("らい", romaji.ConvertLenient("ry", final: true));
        Assert.Equal("りょう", romaji.ConvertLenient("ryp", final: true));
        Assert.Equal("らいt", romaji.ConvertLenient("ryt", final: true));
        Assert.Equal("にゅう", romaji.ConvertLenient("ngh", final: true));
        // 打ちかけは続きを待つ (以前は ng → んg、kk → っk と読んでいた)
        var mid = romaji.AnalyzeFragment("ng");
        Assert.True(mid.IsValid && mid.Tokens.Count == 0 && mid.Partial == "ng", "ng は にゅう の打ちかけとして続きを待つ");
        Assert.Equal("ng", romaji.ConvertLenient("ng", final: false));
        var kkq = new RomajiDetector(RomajiDetector.ParseCustomTable("kkq\tきゅん\n"));
        Assert.Equal("kk", kkq.ConvertLenient("kk", final: false));
        Assert.Equal("きゅん", kkq.ConvertLenient("kkq", final: true));
        // 前置詞から外れたら今までどおり組み込みの読みに戻る
        Assert.Equal("んが", romaji.AnalyzeFragment("nga").Kana);
        // 確定のときは打ちかけを待たずに組み込みの読みに戻す (ng → んg、kk → っk)
        Assert.Equal("んg", romaji.ConvertLenient("ng", final: true));
        Assert.Equal("っk", kkq.ConvertLenient("kk", final: true));
    }

    [Test]
    public static void CustomRomajiTable_StrictStartDoesNotWait()
    {
        // issue #979 レビュー: 語頭の ん・っ をはじく確認 (strictStart) は、表の打ちかけを待つ処理より先。
        // 表に ngh・kkq を登録していても、語頭の ng・kk はローマ字として正しいと判定されない
        // (英字かローマ字かの判定に使うので、nginx のような英語の語頭で今までと結果が変わらない)。
        var romaji = new RomajiDetector(RomajiDetector.ParseCustomTable("ngh\tにゅう\nkkq\tきゅん\n"));
        Assert.True(!romaji.Analyze("ng").IsValid, "語頭の ng は待たずに語頭の「ん」としてはじく");
        Assert.True(!romaji.Analyze("kk").IsValid, "語頭の kk は待たずに語頭の「っ」としてはじく");
        // 語中や打ちかけの表示 (strictStart ではない判定) では、これまでどおり続きを待つ
        Assert.True(romaji.AnalyzeFragment("ng").IsValid, "打ちかけの ng は にゅう の打ちかけとして待つ");
        var mid = romaji.AnalyzeFragment("ang");
        Assert.True(mid.IsValid && mid.Partial == "ng", "語中の ng も続きを待つ");
    }

    [Test]
    public static void CustomRomajiTable_CompleteBuiltinSpellingDoesNotWait()
    {
        // issue #979 レビュー: 組み込みの綴りとして完成している語 (ka など) は、それを先頭に含む
        // ユーザーの綴り (kaa) があっても待たない。待つのは組み込みの読みが ん・っ になる場合だけ。
        var romaji = new RomajiDetector(RomajiDetector.ParseCustomTable("kaa\tかあ\n"));
        Assert.Equal("か", romaji.ConvertLenient("ka", final: false));
        Assert.Equal("か", romaji.ConvertLenient("ka", final: true));
        // 完成したユーザーの綴りはこれまでどおり読める
        Assert.Equal("かあ", romaji.ConvertLenient("kaa", final: true));
        var mid = romaji.AnalyzeFragment("ka");
        Assert.True(mid.IsValid && mid.Kana == "か" && mid.Partial.Length == 0, "ka は か と読む (かあ の打ちかけとして待たない)");
    }

    [Test]
    public static void CustomRomajiTable_AzikSampleKeepsCommonSpellings()
    {
        // 同梱の AZIK の例 (docs/romaji-azik-sample.txt) を読んでも、ふつうのローマ字の語は今までどおり読める (AZIK では sh は すう なので し は si)
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docs", "romaji-azik-sample.txt"))) dir = Path.GetDirectoryName(dir);
        Assert.True(dir is not null, "docs/romaji-azik-sample.txt が見つからない");
        var table = RomajiDetector.ParseCustomTable(File.ReadAllText(Path.Combine(dir!, "docs", "romaji-azik-sample.txt")));
        Assert.True(table.Count >= 50, "例の綴りを読む: " + table.Count);
        var romaji = new RomajiDetector(table);
        foreach (var (typed, kana) in new[] { ("arigatou", "ありがとう"), ("nihongo", "にほんご"), ("watasi", "わたし"), ("katta", "かった"), ("kz", "かん") })
            Assert.Equal(kana, romaji.AnalyzeFragment(typed).Kana, typed);
    }

    [Test]
    public static void CustomRomajiTable_LoadsFromUserDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"meltype-romaji-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "romaji.txt"), "kz\tかん\n");
            var detector = Composition.CompositionDetector.CreateDefault(dir);
            var text = new Composition.CompositionText(detector);
            foreach (var c in "kzji") text.Append(c);
            Assert.Equal("かんじ", text.Display(final: true));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
