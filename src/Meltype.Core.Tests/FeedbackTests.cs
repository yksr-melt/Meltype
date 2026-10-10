// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>協力者のテストで報告された問題 (2026-09) と、判定の強さ (Aggressive / Balanced / Conservative / Manual)。</summary>
internal static class FeedbackTests
{
    private static string Showing(string typed, string? before = null, DetectionLevel level = DetectionLevel.Balanced)
    {
        var k = new CompositionTests.Keyboard { Level = level };
        k.Host.PrecedingText = before;
        k.Type(typed);
        return k.Showing ?? "";
    }

    /// <summary>Windows のスペルチェッカーを使って打つ (使えない環境では null)。</summary>
    private static string? TypeWithSpellChecker(string typed, DetectionLevel level = DetectionLevel.Balanced)
    {
        if (TestSupport.WordChecker is not { IsAvailable: true } checker) return null;
        CompositionTests.Detector.SpellChecker = checker;
        try
        {
            var k = new CompositionTests.Keyboard { Level = level };
            k.Type(typed);
            return k.Host.Document;
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void SmallKana_WithXAndL()
    {
        Assert.Equal("ぁ", Showing("xa"));
        Assert.Equal("ぁ", Showing("la"));
        Assert.Equal("まぁ", Showing("mala"), "mala は英語ではなく まぁ");
    }

    [Test]
    public static void ShiftedSingleConsonant_StaysUppercase()
    {
        foreach (var c in "WRTYPSDGHKLZXVBNM")
        {
            Assert.Equal(c.ToString(), Showing(c.ToString()), $"Shift+{c}");
        }
        Assert.Equal("かW", Showing("kaW"));
    }

    /// <summary>短い英単語だけを知っているスペルチェッカー (Windows のスペルチェッカーの代わり)。</summary>
    private sealed class FewWordsChecker(params string[] words) : Detection.IWordChecker
    {
        public bool IsAvailable => true;
        public bool IsWord(string lower) => words.Contains(lower);
    }

    [Test]
    public static void Laughter_IsNotTakenAsWordFoundMidRomaji()
    {
        // Windows のスペルチェッカーは taw (ビー玉) を知っている。kiyagat|taw の taw で、笑いの w を英字にしていた (きやがっtaw)
        var saved = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = new FewWordsChecker("taw", "new", "show");
        try
        {
            foreach (var (typed, expected) in new[] { ("kiyagattaw", "きやがったw"), ("kitaw!", "きたw！"), ("new", "new"), ("show", "show") })
            {
                var k = new CompositionTests.Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = saved;
        }
    }

    [Test]
    public static void ShiftedSymbols_StartComposition()
    {
        Assert.Equal("！", Showing("!"));
        Assert.Equal("？", Showing("?"));
        Assert.Equal(":", Showing(":"));
        Assert.Equal("～", Showing("~"));
        Assert.Equal("!", Showing("!", before: "Hello"), "英文の後は半角");
    }

    [Test]
    public static void TripleSlash_IsEllipsis()
    {
        // 報告 (Discord のリスト #4): /// を … にできるようにする。URL (file:///) はそのまま。
        // 入力欄の先頭の /// は /command と同じくそのままアプリへ渡す (#193)。設定で OFF にすれば … にできる。
        var k = new CompositionTests.Keyboard { SigilWords = false };
        k.Type("///");
        Assert.Equal("…", k.Showing);
        Assert.Equal("それで…", Showing("sorede///"));
        Assert.Equal("file:///", Showing("file:///"));
    }

    [Test]
    public static void ShortParticles_AfterEnglishWord_AreJapanese()
    {
        foreach (var (typed, expected) in new[] { ("no", "の"), ("to", "と"), ("ga", "が") })
        {
            Assert.Equal(expected, Showing(typed, before: "GitHub"), $"GitHub + {typed}");
            Assert.Equal(expected, Showing(typed, before: "今日は GitHub "), $"今日は GitHub + {typed}");
        }
        Assert.Equal("no", Showing("no", before: "GitHub", level: DetectionLevel.Aggressive), "積極的なら英語");
    }

    [Test]
    public static void ShortWords_InEnglishSentence_AreEnglish()
    {
        Assert.Equal("to", Showing("to", before: "I want "));
        Assert.Equal("a", Showing("a", before: "this is "));
        Assert.Equal("is", Showing("is", before: "this "), "子音で終わる語は片側が英語なら英語");
    }

    [Test]
    public static void Levels_ChangeHowEagerlyEnglishIsShown()
    {
        Assert.Equal("sushi", Showing("sushi", before: "I like "), "英文の後");
        Assert.Equal("すし", Showing("sushi", before: "I ", level: DetectionLevel.Conservative), "慎重: 1 語だけの文脈では英語にしない");
        Assert.Equal("google", Showing("google", level: DetectionLevel.Conservative), "慎重でもローマ字として読めない英単語は英語");
        Assert.Equal("あまぞn", Showing("amazon", level: DetectionLevel.Conservative), "慎重: ローマ字として読める固有名詞は日本語のまま");
    }

    [Test]
    public static void Manual_OnlySuggests_TabAccepts()
    {
        var k = new CompositionTests.Keyboard { Level = DetectionLevel.Manual };
        k.Type("google");
        Assert.Equal("ごおgぇ", k.Showing, "手動: 自動では英字にしない");
        Assert.True(k.Host.View!.Hint.Contains("Tab → google"), $"提案を表示: {k.Host.View.Hint}");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("google", k.Showing, "Tab で提案どおり英字");
        k.Type("\n");
        Assert.Equal("google", k.Host.Document);

        Assert.Equal("Google", Showing("Google", level: DetectionLevel.Manual), "Shift で打った大文字始まりは手動でも英語");
    }

    [Test]
    public static void EnglishSentences_WithSpellChecker()
    {
        foreach (var sentence in new[] { "I want to go to the park", "make sure you have time", "the meeting is at nine", "my name is taro", "this is a pen" })
        {
            if (TypeWithSpellChecker(sentence + "\n") is not { } result) return;
            Assert.Equal(sentence, result, "英文");
        }
    }

    [Test]
    public static void JapaneseSentences_WithSpellChecker()
    {
        foreach (var (typed, expected) in new[]
        {
            ("kore ha pen desu", "これはぺんです"),
            ("GitHub no repo", "GitHub のれぽ"),
            ("Google to Apple", "Google とApple"),
            ("watashi ha sushi ga suki", "わたしはすしがすき"),
            ("mala", "まぁ"),
        })
        {
            if (TypeWithSpellChecker(typed + "\n") is not { } result) return;
            Assert.Equal(expected, result, "日本語の文");
        }
    }
}

internal static class KanaInputTests
{
    private static CompositionTests.Keyboard Kana(DetectionLevel level = DetectionLevel.Balanced) => new() { Kana = true, Level = level };

    [Test]
    public static void KanaKeys_ProduceKana()
    {
        var k = Kana();
        k.TypeKanaKeys("tu");
        Assert.Equal("かな", k.Showing, "T = か, U = な");
        k.TypeKanaKeys("t@");
        Assert.Equal("かなが", k.Showing, "濁点は直前のかなに付く");
        k.Press(VirtualKeys.Back);
        Assert.Equal("かな", k.Showing, "BackSpace で が を 1 文字消す");
    }

    [Test]
    public static void KanaKeys_ShiftGivesSmallKanaAndPunctuation()
    {
        var k = Kana();
        k.TypeKeys((0x45, true), (0x5A, true), (0xBC, true), (0xBE, true), (0x33, false), (0x33, true));
        Assert.Equal("ぃっ、。あぁ", k.Showing);
        k.TypeKeys((0xBC, false));
        Assert.Equal("ぃっ、。あぁね", k.Showing, "Shift なしの , は ね");
    }

    [Test]
    public static void KanaInput_EnglishWordsAreShownAsEnglish()
    {
        var k = Kana();
        k.TypeKanaKeys("google");
        Assert.Equal("google", k.Showing, "打ったキーが英単語で、かなとしては日本語にならない");
        k.Type("\n");
        Assert.Equal("google", k.Host.Document);

        var japanese = Kana();
        japanese.TypeKanaKeys("byiaf");
        Assert.Equal("こんにちは", japanese.Showing);
    }

    [Test]
    public static void KanaInput_Punctuation_FollowsSetting()
    {
        // かな入力の 、 (Shift+ね) と 。 (Shift+る) も句読点の設定に合わせる
        var k = Kana();
        k.Punctuation = PunctuationStyle.FullWidthCommaPeriod;
        k.TypeKeys(KanaQualityTests.KeysFor("はい、はい。"));
        k.Type("\n");
        Assert.Equal("はい，はい．", k.Host.Document);
    }

    [Test]
    public static void KanaInput_SpaceAroundEnglish()
    {
        // かな入力でも、確定した英単語の前後に半角スペースが入る
        var k = Kana();
        k.SpaceAroundEnglish = true;
        k.TypeKanaKeys("google");
        k.Type("\n");
        k.TypeKanaKeys("byiaf");
        k.Type("\n");
        Assert.Equal("google こんにちは", k.Host.Document);
    }

    [Test]
    public static void KanaInput_ShiftSmallKana_IsNotCapitalLetter()
    {
        // っ (Shift+Z)・ぃ (Shift+E) は、大文字で打った英語ではない (たのしかった が たのしかZq になっていた)
        var k = Kana();
        k.TypeKeys(KanaQualityTests.KeysFor("たのしかった"));
        k.Type("\n");
        Assert.Equal("たのしかった", k.Host.Document);
    }

    [Test]
    public static void KanaInput_EnglishAfterJapanese()
    {
        // 日本語のすぐ後ろの英単語も英字にする (きょうは + google が きららきりい になっていた)
        var k = Kana();
        k.TypeKeys(KanaQualityTests.KeysFor("きょうは"));
        k.TypeKanaKeys("google");
        k.TypeKeys(KanaQualityTests.KeysFor("でけんさく"));
        k.Type("\n");
        Assert.Equal("きょうはgoogleでけんさく", k.Host.Document);

        var after = Kana();
        after.Host.PrecedingText = "今日は";
        after.TypeKanaKeys("google");
        after.Type("\n");
        Assert.Equal("google", after.Host.Document, "確定済みの日本語の後ろ");
    }

    [Test]
    public static void KanaInput_FollowsLevels()
    {
        var manual = Kana(DetectionLevel.Manual);
        manual.TypeKanaKeys("google");
        Assert.Equal("きららきりい", manual.Showing, "手動: 自動では英字にしない");
        Assert.True(manual.Host.View!.Hint.Contains("Tab → google"), manual.Host.View.Hint);

        var shift = Kana(DetectionLevel.Manual);
        shift.TypeKanaKeys("Google");
        Assert.Equal("Google", shift.Showing, "Shift で打った大文字始まりは英語");
    }
}

internal static class MisspellingTests
{
    [Test]
    public static void Misspelling_IsSuggestedAndFixedWithTab()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("buresureddo");
        Assert.Equal("ぶれすれっど", k.Showing);
        Assert.Equal("もしかして: ブレスレット　<Tab>で修正", k.Host.View!.Suggestion, "変換ボックスの「もしかして」の行");
        Assert.True(!k.Host.View.Hint.Contains("もしかして"), "案内の行には出さない");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("ぶれすれっと", k.Showing, "Tab で正しい読みに直す");
        Assert.True(!k.Host.View!.Hint.Contains("もしかして"), "直した後は出ない");
        k.Type("\n");
        Assert.Equal("ぶれすれっと", k.Host.Document);
    }

    [Test]
    public static void Misspelling_ListedPairsAndSpellingVariants()
    {
        foreach (var (typed, right) in new[]
        {
            ("shumire-shon", "シミュレーション"),
            ("komyunike-shon", null),
            ("kominyuke-shon", "コミュニケーション"),
            ("bure-suretto", "ブレスレット"),
            ("figiasuke-to", "フィギュア"),
            ("kyouhaiitenki", null),
            ("buresurettowokau", null),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            var suggestion = k.Host.View!.Suggestion ?? "";
            if (right is null) Assert.True(suggestion.Length == 0, $"{typed}: 誤りではない ({suggestion})");
            else Assert.True(suggestion.Contains($"もしかして: {right}"), $"{typed}: {suggestion}");
        }
    }

    [Test]
    public static void Misspelling_TabWhileConverting_Reconverts()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("buresureddo ");
        Assert.True(k.Host.View!.Converting, "Space で変換中");
        Assert.True(k.Host.View.Suggestion?.Contains("もしかして: ブレスレット") == true, k.Host.View.Suggestion ?? "");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View!.Converting, "直して変換し直す");
        Assert.Equal("ぶれすれっと", k.Showing);
    }
}

internal static class CodeProfileTests
{
    [Test]
    public static void LineTracker_FollowsTypingAndResets()
    {
        var line = new LineTracker();
        Assert.True(line.Text is null, "最初は分からない");
        line.NewLine();
        line.Append("x = 1 ");
        Assert.Equal(LineKind.Code, LineContext.Classify(line.Text!));
        line.Append("// ");
        Assert.Equal(LineKind.Comment, LineContext.Classify(line.Text!), "// の後はコメント");
        line.Backspace();
        line.Backspace();
        line.Backspace();
        Assert.Equal("x = 1 ", line.Text);
        line.NewLine();
        Assert.Equal("x = 1 \n", line.Text, "改行で新しい行 (前の行も持っておく)");
        Assert.Equal(LineKind.Code, LineContext.ClassifyText(line.Text!));
        line.Invalidate();
        Assert.True(line.Text is null, "キャレットが動いたら分からない");
        line.SetFromText("first line\r\n    print(\"こん");
        Assert.Equal(LineKind.String, LineContext.ClassifyText(line.Text!), "UI Automation で読んだ文字列の、今の行で調べる");
    }

    [Test]
    public static void LineContext_MultiLineStringsAndComments()
    {
        // Python の """ の中の行 (本文) で日本語が打てなかった
        Assert.Equal(LineKind.String, LineContext.ClassifyText("def f():\n    \"\"\"\n    "));
        Assert.Equal(LineKind.String, LineContext.ClassifyText("x = '''\nほん"));
        Assert.Equal(LineKind.Comment, LineContext.ClassifyText("/*\n  説明"));
        Assert.Equal(LineKind.Comment, LineContext.ClassifyText("<!--\n"));
        // 閉じた後はコード
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("\"\"\"\ndoc\n\"\"\"\nx = "));
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("/* a */\nint x = "));
        // コメント・1 行の文字列の中の """ /* は数えない
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("# \"\"\" in comment\nx = "));
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("s = \"/*\"\nx = "));
    }

    [Test]
    public static void Settings_CodeAppsAreCode()
    {
        var settings = new Settings();
        Assert.Equal(AppProfile.Code, settings.ProfileFor("Code.exe"));
        Assert.Equal(AppProfile.Code, settings.ProfileFor("windowsterminal.exe"), "大文字小文字は区別しない");
        Assert.Equal(AppProfile.General, settings.ProfileFor("chrome.exe"));
        Assert.Equal(AppProfile.Code, settings.Clone().ProfileFor("Code.exe"), "複製しても種類を保つ");

        // v3 の設定ファイル (種類が無い) を読み込むと、コードエディター・ターミナルが「コード」になる。ユーザーが OFF にしたものは OFF のまま。
        var old = new Settings { SettingsVersion = 3, AppRules = [new AppRule { Process = "Code.exe", Enabled = false }, new AppRule { Process = "chrome.exe" }] };
        old.Migrate();
        Assert.Equal(AppProfile.Code, old.ProfileFor("Code.exe"));
        Assert.True(!old.IsAppEnabled("Code.exe"), "OFF のまま");
        Assert.Equal(AppProfile.Code, old.ProfileFor("pwsh.exe"), "足りないコードアプリを追加");
        Assert.Equal(AppProfile.General, old.ProfileFor("chrome.exe"));
    }

    [Test]
    public static void Settings_CustomAppKinds()
    {
        // 報告: アプリ別設定の種類を、一般とコードだけでなく自分で作れるように。
        var settings = new Settings
        {
            DetectionLevel = DetectionLevel.Balanced,
            AppKinds = [new AppKind { Name = "チャット", Base = AppProfile.General, DetectionLevel = DetectionLevel.Aggressive, LiveConversion = false, StartInEnglish = true }],
            AppRules = [new AppRule { Process = "Discord.exe", Kind = "チャット" }, new AppRule { Process = "Code.exe", Profile = AppProfile.Code }],
        };
        Assert.Equal(DetectionLevel.Aggressive, settings.ForApp("discord.exe").DetectionLevel, "独自の種類の判定の強さ");
        Assert.True(!settings.ForApp("Discord.exe").LiveConversion, "独自の種類のライブ変換");
        Assert.Equal(DetectionLevel.Balanced, settings.ForApp("chrome.exe").DetectionLevel, "ほかのアプリは全体の設定");
        Assert.True(settings.KindFor("Discord.exe")?.StartInEnglish == true, "最初は英数");
        Assert.Equal(AppProfile.Code, settings.ProfileFor("Code.exe"));

        var path = Path.Combine(Path.GetTempPath(), $"meltype-kinds-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal("チャット", loaded.KindFor("Discord.exe")?.Name, "保存して読み込んでも種類を保つ");
            Assert.Equal(DetectionLevel.Aggressive, loaded.Clone().ForApp("Discord.exe").DetectionLevel, "複製しても保つ");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

internal static class LanguageLearningTests
{
    [Test]
    public static void CommonJapanese_NeedsTwoTimesToBecomeEnglish()
    {
        // 一度 kyouha を英字で確定しただけで、ずっと kyouha朝から… になっていた。
        var typos = RomajiTypoCorrector.Load(CompositionTests.Detector.Romaji);
        Assert.True(typos.IsCommonJapanese("kyouha") && typos.IsCommonJapanese("sushi"), "よく使う日本語の読み");
        Assert.True(!typos.IsCommonJapanese("api") && !typos.IsCommonJapanese("github") && !typos.IsCommonJapanese("tao"), "日本語の語にならない");
        var memory = new LanguageMemory(null) { IsCommonJapanese = typos.IsCommonJapanese };
        memory.Remember("kyouha", english: true);
        Assert.True(memory.Get("kyouha") is null, "1 回では英語にしない");
        memory.Remember("kyouha", english: true);
        Assert.Equal(true, memory.Get("kyouha") ?? false, "2 回で英語");
        memory.Remember("api", english: true);
        Assert.Equal(true, memory.Get("api") ?? false, "ふつうの語は 1 回で英語");
    }

    [Test]
    public static void F10_TeachesEnglish_ThenUsedInContext()
    {
        var memory = new LanguageMemory(null);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("api");
            Assert.Equal("あぴ", k.Showing, "最初は日本語");
            k.Press(VirtualKeys.F10);
            k.Type("\n");
            Assert.Equal("api", k.Host.Document);
            Assert.Equal(true, memory.Get("api"), "F10 で英字にして確定したので覚える");

            var next = new CompositionTests.Keyboard(languages: memory);
            next.Type("apinoerror\n");
            Assert.Equal("apiのerror", next.Host.Document, "次からは文の中でも英字");
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }

    [Test]
    public static void ShortLearnedWord_DoesNotSplitJapanese()
    {
        // 一度 go を英字で確定したら、日本語 (nihongo) が にほんgo になっていた。
        var memory = new LanguageMemory(null);
        memory.Remember("go", english: true, explicitChoice: true);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("nihongo\n");
            Assert.Equal("にほんご", k.Host.Document, "日本語のすぐ後ろでは、覚えた短い英単語を使わない");
            k = new CompositionTests.Keyboard(languages: memory);
            k.Type("go\n");
            Assert.Equal("go", k.Host.Document, "単独なら覚えたとおり英字");
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }



    [Test]
    public static void EnglishWordEndingInNg_BeforeParticle()
    {
        // kyouhameetinggaarimasu が きょうはめえちんっがあります になっていた (meeting の g が が とつながって っが)。
        // また、表示では きょうはmeetingです なのに、確定したら打ち間違いとして g を直されて めえちんがです になっていた。
        // meeting を英単語と知るのに、スペルチェッカー (Windows) か同梱の英単語の一覧 (Mac・Linux) を使う。
        CompositionTests.Detector.SpellChecker = TestSupport.WordChecker is { IsAvailable: true } checker ? checker : Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("meetingga", "meetingが"), ("shoppinggasuki", "shoppingがすき"), ("sanngatsu", "さんがつ"),
                ("kyouhameetinggaarimasu", "きょうはmeetingがあります"), ("kyouhameetingdesu", "きょうはmeetingです"), ("onegaishimsu", "おねがいします"),
            })
            {
                var k = new CompositionTests.Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }


    [Test]
    public static void SymbolCandidates_ShowHalfOrFullWidth()
    {
        // 変換の候補で、記号が半角か全角か分からなかった (@ と ＠)。両方あるときは右に「半角」「全角」と出す。
        var k = new CompositionTests.Keyboard { SigilWords = false };
        k.Type("@ ");
        var view = k.Host.View!;
        Assert.True(view.Converting, "変換中");
        var notes = view.Notes ?? [];
        Assert.Equal("半角", notes.ElementAtOrDefault(view.Candidates.ToList().IndexOf("@")), string.Join(" ", view.Candidates));
        Assert.Equal("全角", notes.ElementAtOrDefault(view.Candidates.ToList().IndexOf("＠")), string.Join(" ", view.Candidates));
    }

  [Test]
  public static void Brand_TeamsFromChiimusu()
  {
    // ちーむす でも Teams を出す (issue #47。ちーむず だけだった)
    var candidates = CandidateDictionary.Load(null);
    Assert.True(candidates.Lookup("ちーむす").Contains("Teams"), string.Join(" ", candidates.Lookup("ちーむす")));
    Assert.True(candidates.Lookup("ちーむず").Contains("Teams"), "ちーむず も今までどおり");
  }

  [Test]
  public static void Phrase_AgeashiWoToru()
  {
    // 揚げ足取るな が 揚げ足とルナ になっていた (issue #147)。同梱の語句で 揚げ足取る を 1 つの文節にする
    var k = new CompositionTests.Keyboard(userDictionary: new UserDictionary(null));
    k.Type("ageashitoruna ");
    var view = k.Host.View!;
    Assert.True(view.Converting, "変換中");
    Assert.Equal("揚げ足取る", view.Clauses![0], string.Join("|", view.Clauses));
  }

    [Test]
    public static void TesterNames_AreCandidates()
    {
        // 協力してくださった方々の名前を変換しやすくする (issue #156)
        var candidates = CandidateDictionary.Load(null);
        foreach (var (reading, name) in new[] { ("くらいど", "くらいど！"), ("ことね", "琴音"), ("ことねりんく", "琴音Link"), ("れい", "Ray") })
            Assert.True(candidates.Lookup(reading).Contains(name), reading + ": " + string.Join(" ", candidates.Lookup(reading)));
    }

    [Test]
    public static void CompositionSize_LargerChoicesAreSaved()
    {
        // 変換ボックスの文字をもっと大きくしたい (issue #164): 特大・最大 を選べて、保存しても残る
        var path = Path.Combine(Path.GetTempPath(), $"meltype-size-{Guid.NewGuid():N}.json");
        try
        {
            foreach (var size in new[] { CompositionSize.ExtraLarge, CompositionSize.Huge })
            {
                new Settings { CompositionSize = size }.Save(path);
                Assert.Equal(size, Settings.Load(path).CompositionSize);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void CompositionFont_IsSaved()
    {
        // 変換ボックスのフォントを変えたい (issue #165): 既定は空 (Yu Gothic UI)、選んだフォントは保存しても残る
        Assert.Equal("", new Settings().CompositionFont);
        var path = Path.Combine(Path.GetTempPath(), $"meltype-font-{Guid.NewGuid():N}.json");
        try
        {
            new Settings { CompositionFont = "Meiryo UI" }.Save(path);
            Assert.Equal("Meiryo UI", Settings.Load(path).CompositionFont);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void CompositionAppearance_IsSavedByName()
    {
        // 変換ボックスの色 (ライト / Windows に合わせる)・不透明度・カーソルの上に出す (issue #39): 既定は今までどおりで、選んだ値は名前で保存して残る
        var defaults = new Settings();
        Assert.Equal(CompositionTheme.Dark, defaults.CompositionTheme);
        Assert.Equal(CompositionOpacity.Opaque, defaults.CompositionOpacity);
        Assert.Equal(1.0, defaults.CompositionOpacityValue);
        var path = Path.Combine(Path.GetTempPath(), $"meltype-theme-{Guid.NewGuid():N}.json");
        try
        {
            new Settings { CompositionTheme = CompositionTheme.System, CompositionOpacity = CompositionOpacity.Percent80, CompositionPlacement = CompositionPlacement.AboveCaret }.Save(path);
            var json = File.ReadAllText(path);
            Assert.True(json.Contains("\"CompositionTheme\": \"System\"") && json.Contains("\"CompositionOpacity\": \"Percent80\"") && json.Contains("\"AboveCaret\""), json);
            var loaded = Settings.Load(path);
            Assert.Equal(CompositionTheme.System, loaded.CompositionTheme);
            Assert.Equal(CompositionOpacity.Percent80, loaded.CompositionOpacity);
            Assert.Equal(0.8, loaded.CompositionOpacityValue);
            Assert.Equal(CompositionPlacement.AboveCaret, loaded.CompositionPlacement);
            foreach (var (theme, opacity) in new[] { (CompositionTheme.Light, CompositionOpacity.Percent90), (CompositionTheme.Dark, CompositionOpacity.Percent70) })
            {
                new Settings { CompositionTheme = theme, CompositionOpacity = opacity }.Save(path);
                Assert.Equal(theme, Settings.Load(path).CompositionTheme);
                Assert.Equal(opacity, Settings.Load(path).CompositionOpacity);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void CompositionTheme_FollowsWindowsOnlyWhenAsked()
    {
        // 「Windows の設定に合わせる」は Windows のアプリ モードどおり (読めなければダーク)。ライト / ダークは Windows の設定によらない
        Assert.True(!new Settings().CompositionIsLight(true), "既定はダーク");
        Assert.True(new Settings { CompositionTheme = CompositionTheme.Light }.CompositionIsLight(false), "ライト");
        var system = new Settings { CompositionTheme = CompositionTheme.System };
        Assert.True(system.CompositionIsLight(true), "Windows がライト");
        Assert.True(!system.CompositionIsLight(false), "Windows がダーク");
        Assert.True(!system.CompositionIsLight(null), "読めなければダーク");
    }

    [Test]
    public static void DoubledUnitAfterNumber_ShowsLettersWhileTyping()
    {
        // 50cc を打っている途中に 50っc と出ていた (issue #130)。確定した結果は直っていたが、途中の表示も 50cc にする
        var k = new CompositionTests.Keyboard();
        k.Type("50cc");
        Assert.Equal("50cc", k.Showing);
        k.Type("genntuki\n");
        Assert.Equal("50ccげんつき", k.Host.Document);
        // mm は mmol の打ちかけかもしれないので、今までどおり続きを待つ
        k = new CompositionTests.Keyboard();
        k.Type("2mmol\n");
        Assert.Equal("2mmol", k.Host.Document);
        // mm の後ろに日本語が続けば、確定した結果は単位の mm
        k = new CompositionTests.Keyboard();
        k.Type("10mmdesu");
        Assert.Equal("10mmです", k.Showing);
        // c 1 つは単位の打ちかけとして英字のまま
        k = new CompositionTests.Keyboard();
        k.Type("5c");
        Assert.Equal("5c", k.Showing);
    }

    [Test]
    public static void AcronymThenRomaji_IsJapanese()
    {
        // 大文字の略語の後ろのローマ字 (AInituite → AIについて: issue #129)。
        // AIde を英単語 aide、AInit を init と読んで、後ろまで英字にしていた
        foreach (var (typed, expected) in new[]
        {
            ("AInituite", "AIについて"), ("AInitsuite", "AIについて"), ("AIdekiru", "AIできる"), ("GPTnituite", "GPTについて"),
            ("AIde", "AIで"), ("iOSdekiru", "iOSできる"),
            // 略語に英単語が続くもの・英文の中の略語は英語のまま
            ("HTTPserver", "HTTPserver"), ("GPT is great", "GPT is great"), ("use HTTPS for login", "use HTTPS for login"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void ShortHeadThenCapital_IsName()
    {
        // #266: McAfeeto が McA|feeto で切れて McAふぇえと になっていた。Mc・Le の後ろの大文字で終わる頭 (McA・LeB) は略語ではなく名前の途中
        foreach (var (typed, expected) in new[]
        {
            ("McAfeeto", "McAfeeと"), ("McAfeewo", "McAfeeを"), ("McAfeede", "McAfeeで"), ("LeBronde", "LeBronで"),
            // 英単語 + 大文字 1 文字・大文字が続く略語は今までどおり区切る
            ("PlanBdeiku", "PlanBでいく"), ("TypeAnohou", "TypeAのほう"), ("OrpCde", "OrpCで"), ("oRPCde", "oRPCで"), ("ORPCde", "ORPCで"),
            ("OCRwoshi", "OCRをし"), ("DeNAde", "DeNAで"), ("OpenAIno", "OpenAIの"), ("Anisiyouka", "Aにしようか"), ("MrXde", "MrXで"),
            ("McAfee", "McAfee"), ("McKinseyde", "McKinseyで"), ("McDonaldsde", "McDonaldsで"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }

        var typing = new CompositionTests.Keyboard();
        typing.Type("McAfeeto");
        Assert.Equal("McAfeeと", typing.Showing);
    }

    [Test]
    public static void AllowInjectedInput_IsSharedAndOffByDefault()
    {
        // 遠隔操作 (AnyDesk・VNC) のキーも処理する設定 (issue #110)。既定は OFF、保存して残り、全プロファイル共通
        var path = Path.Combine(Path.GetTempPath(), $"meltype-injected-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}");
            Assert.True(!Settings.Load(path).AllowInjectedInput, "既存の設定ファイルでは OFF");
            var settings = new Settings { AllowInjectedInput = true };
            settings.Clone().Normalize().Save(path);
            Assert.True(Settings.Load(path).AllowInjectedInput, "保存・複製・読み込みで設定が残る");
            var profile = settings.Normalize().AddProfile("仕事用")!;
            profile.AllowInjectedInput = false;
            Assert.True(!profile.SwitchProfile(Settings.DefaultProfileName).AllowInjectedInput, "全プロファイル共通の設定");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public static void JsonFiles_ReadWithGeneratedMetadata()
    {
        // Mac・Linux (NativeAOT) で config.json・languages.json・conversions.json が読めず、上書きで消えていた (issue #151)。
        // 読み書きをビルド時に作った型の情報 (ソース生成) に変えたので、今までの形式のファイルがそのまま読めることを確かめる
        var dir = Path.Combine(Path.GetTempPath(), $"meltype-json-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var config = Path.Combine(dir, "config.json");
            File.WriteAllText(config, "{\n  // コメント\n  \"FileLog\": true,\n  \"Mode\": \"Keyboard\",\n  \"DetectionLevel\": \"Conservative\",\n  \"AppRules\": [{ \"Process\": \"code.exe\", \"Profile\": \"Code\" }],\n}");
            var settings = Settings.Load(config);
            Assert.True(settings.FileLog, "FileLog");
            Assert.Equal(DetectionLevel.Conservative, settings.DetectionLevel);
            Assert.Equal(AppProfile.Code, settings.ProfileFor("code.exe"));
            Assert.True(!File.Exists(config + ".broken"), "壊れたとみなさない");
            settings.Save(config);
            Assert.True(File.ReadAllText(config).Contains("\"DetectionLevel\": \"Conservative\""), "列挙型は名前で保存する");

            var languages = Path.Combine(dir, "languages.json");
            File.WriteAllText(languages, "{\"emoji\":{\"English\":true,\"Used\":\"2026-10-01T00:00:00Z\",\"Count\":3,\"Explicit\":true}}");
            Assert.Equal(true, new LanguageMemory(languages).Get("emoji"));

            var conversions = Path.Combine(dir, "conversions.json");
            File.WriteAllText(conversions, "{\"ごかん\":{\"Text\":\"互換\",\"Used\":\"2026-10-01T00:00:00Z\"}}");
            var history = new ConversionHistory(conversions);
            Assert.Equal("互換", history.Get("ごかん"));
            history.Remember("きごう", "記号");
            Assert.Equal("互換", new ConversionHistory(conversions).Get("ごかん"), "保存しても前の学習が残る");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public static void UnitsAfterNumbers_StayLetters()
    {
        // 単位 (mm、min) が打ちにくく、日本語になることがあった (10mmで → 10っまで、5min → 5みん)。
        foreach (var (typed, expected) in new[]
        {
            ("10mmde", "10mmで"), ("kyouha10mmdesu", "きょうは10mmです"), ("5min", "5min"), ("3mol", "3mol"), ("2mmol", "2mmol"),
            ("100mlnomizu", "100mlのみず"), ("3nin", "3にん"), ("1man", "1まん"), ("10mina", "10みな"), ("5ko", "5こ"),
            // cc も 1 語の単位 (50cc原付 が 50っc原付 になっていた: issue #130)。ppm・ppb・ppt も同じく促音になっていた
            ("50cc", "50cc"), ("50ccgenntuki", "50ccげんつき"), ("150ccdattara", "150ccだったら"), ("50ccwokatta", "50ccをかった"),
            ("100ppm", "100ppm"), ("50ppbhikaku", "50ppbひかく"),
            // 数字の後ろでない っ (ccha・tchi 系) は今までどおりかな
            ("cchau", "っちゃう"), ("yacchatta", "やっちゃった"), ("50ccha", "50ccは"), ("50ccchan", "50ccちゃん"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }


    [Test]
    public static void TesterReports_20261003()
    {
        // テスターの報告 (2026-10-03): これあれか、開きの記号の全角、Ah!、swingin'、a / u / r
        CompositionTests.Detector.SpellChecker = TestSupport.WordChecker is { IsAvailable: true } checker ? checker : Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("koreareka", "これあれか"), ("koreade", "koreaで"),
                ("\"uchiagebanashihaGo Through!\"", "\"うちあげばなしはGo Through!\""), ("(Ooh Let's Think wow...) ", "(Ooh Let's Think wow...) "),
                ("(chuui)", "(ちゅうい)"), ("\"kyouhaGo!\"tteitta", "\"きょうはGo!\"っていった"),
                ("1. Ah! Ah!", "1. Ah! Ah!"), ("ahoka", "あほか"),
                ("Swingin'", "Swingin'"), ("rockin' and rollin'", "rockin' and rollin'"),
                ("A fool a fool a, for u", "A fool a fool a, for u"), ("how r u", "how r u"), ("kyouhau", "きょうはう"),
                ("atarashiiPCwokatta", "あたらしいPCをかった"), ("iPhonewokatta", "iPhoneをかった"),
                ("toshiteOCRwoshi,amerikagonaruEnglishwohappyni", "としてOCRをし、あめりかごなるEnglishをhappyに"), ("Tokyonisumu", "Tokyoにすむ"),
                ("motometeSNSde", "もとめてSNSで"), ("shilyowhugaltsucowhu", "しょうがっこう"), ("dochiramouserga", "どちらもuserが"), ("macOSnoupdate", "macOSのupdate"),
                ("tabetemitaidesupart1026beta", "たべてみたいですpart1026beta"), ("kaibunsyorta2026kotosimo", "かいぶんしょrta2026ことしも"), ("PS5wokaitai", "PS5をかいたい"),
                ("cyiisai", "ちぃいさい"), ("yiu", "いう"), ("wu", "う"), ("ceito", "せいと"),
                ("translatebot[Thinking is thinking]", "translatebot「Thinking is thinking」"), ("fuwafuwanacornyorifuwafuwanachocolatenohougayum", "ふわふわなcornよりふわふわなchocolateのほうがyum"),
                ("hey yo say!", "hey yo say!"), ("korosuzobot", "ころすぞbot"),
                ("appuruulottitukereba", "あっぷるうぉっちつければ"), ("JapanesetoEnglishwohodohodonimazetahougagoodkamoshirenai", "JapaneseとEnglishをほどほどにまぜたほうがgoodかもしれない"), ("JavaScriptwokaku", "JavaScriptをかく"), ("iPaddeiisonnnadekakunakute", "iPadでいいそんなでかくなくて"), ("atohahelppe-jitoka", "あとはhelpぺーじとか"), ("HHireta", "HHいれた"), ("grokga", "grokが"), ("grokniyoruto", "grokによると"), ("guri-nnshanottara51kmijoukukan", "ぐりーんしゃのったら51kmいじょうくかん"), ("shitaraavgadete", "したらavがでて"), ("jimotogeoguessershitetara", "じもとgeoguesserしてたら"), ("jiketsurtashite", "じけつrtaして"),
                // summary.json (2026-10-05): 音の途中から始まる英単語 (kara|na|l の anal、da|me|x の amex) で小書き文字が崩れていた
                ("yakaranala", "やからなぁ"), ("damexe", "だめぇ"), ("hotelya", "hotelや"),
                // Issue #104: 英単語 (moral) の最後の l + tu が っ にならなかった。英単語 + つ (hotel|tukau) は今までどおり
                ("moraltute", "もらって"), ("hoteltukau", "hotelつかう"), ("hoteltsukau", "hotelつかう"), ("mailtukau", "mailつかう"), ("realtukau", "realつかう"), ("tooltukau", "toolつかう"), ("sukilltute", "すkillつて"), ("Moraltute", "Moralつて"),
                // Issue #218: 英単語 (hotel・total) の後ろでわざわざ打った ltu が、l の重なり (te|ll = tell) で英字になっていた
                ("hotelltu", "hotelっ"), ("hotelltsu", "hotelっ"), ("totalltute", "totalって"), ("hotellya", "hotellや"), ("welltuned", "welltuned"),
                // Issue #220: 表示では tabで なのに、確定すると打ち間違いとして たべ に直されていた
                ("tabde", "tabで"), ("tabga", "tabが"), ("kyouhatabdeyaru", "きょうはtabでやる"), ("tabete", "たべて"), ("onegaishimsu", "おねがいします"),
                // 伸ばす音の う + について (そう|u|ni|t の unit) で、t と s が つ にならず英単語 unit になっていた。英単語 + する (commit|する) は今までどおり
                ("sounitsuite", "そうについて"), ("hontounitsukareta", "ほんとうにつかれた"), ("kaijounitsukuto", "かいじょうにつくと"), ("kyouhacommitsuru", "きょうはcommitする"), ("editsuru", "editする"),
                // 笑いの w (文末の w が消えていた、ww が っw になっていた)。英単語の最後の w はそのまま
                ("kiyagattaw", "きやがったw"), ("daneww", "だねww"), ("toottawwwww", "とおったwwwww"), ("wwww", "wwww"), ("kitaww!", "きたww！"), ("new", "new"), ("aww", "aww"),
                // 読めない略語の最後の c + は・や (vrc|ya が vr|ちゃ になっていた)。読める語 (まち) と大文字の略語の後ろ (EDちゃう) は分けない
                ("vrcyaranai", "vrcやらない"), ("vchairu", "vcはいる"), ("pcha", "pcは"), ("machi", "まち"), ("ochaire", "おちゃいれ"), ("EDchau", "EDちゃう"),
                ("everyonenohatsugen", "everyoneのはつげん"),
                // 英語の歌詞: 行の始めの I'll の後ろ (I'll ご になっていた)、riverside, (river しで、 になっていた)
                ("I'll go to see you again tomorrow", "I'll go to see you again tomorrow"), ("by the riverside, I'm sitting", "by the riverside, I'm sitting"),
                ("shirigaru teenage girl", "しりがるteenage girl"), ("bakudannnihanarenai oh no!", "ばくだんにはなれないoh no!"), ("supeaman woah", "すぺあまんwoah"),
                // 英文の中の短い語 + 記号 (let's go! の go が ご になっていた)。日本語の後ろの記号は今までどおり全角
                ("let's go!", "let's go!"), ("I said no?", "I said no?"), ("sorena!", "それな！"), ("nande?", "なんで？"),
                // 記号の前・確定時の笑いの w (きたw！ の w が確定で消えていた)
                ("kitaw!", "きたw！"), ("hontow?w", "ほんとw？w"),
                // 略語 + 日本語 + 記号は今までどおり (BE|かな？ が BEkana？ になっていた)
                ("tougouhandakaraBEkana?", "とうごうはんだからBEかな？"), ("fubusangaXshisuginadakenanda!!", "ふぶさんがXしすぎなだけなんだ！！"), ("tsubemyunorevancedtsukatteru", "つべみゅのrevancedつかってる"),
                // テスターの報告 (2026-10-05): 英語のユーザー名が打てない。@ の後ろ (メンション)・_ の入った語は英字のまま
                ("@kuraido", "@kuraido"), ("@una08142009 arigatou", "@una08142009 ありがとう"), ("upah_setu", "upah_setu"), ("cafely_latte", "cafely_latte"),
                // メールアドレスは @ の前も英字のまま (issue #59。前は たろ@... だった。例には example.com を使う)
                ("taro@example.com", "taro@example.com"), ("@akisamesan", "@akisamesan"),
                // Issue #12: ローマ字として読めてしまう英単語 (feature → ふぇあつれ)。日本語の中でも英字
                ("feature", "feature"), ("future", "future"), ("nature", "nature"), ("remote", "remote"), ("online", "online"),
                ("atarashiifeaturewotsuika", "あたらしいfeatureをついか"), ("kyouharemotedesu", "きょうはremoteです"),
            })
            {
                // 先頭の @ の語 (#193) はそのままアプリへ渡すので、ここは変換ボックスに入れたときの扱いを確かめる。
                var k = new CompositionTests.Keyboard { SigilWords = false };
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void ShortWord_ChosenFromCandidates_IsLearnedOnSecondTime()
    {
        // 変換の候補から go を英字で選んだだけで覚えると、日本語の中まで英字になりやすい (にほんgo)。2 文字の語は 2 回で覚える。
        var memory = new LanguageMemory(null) { IsReadableRomaji = _ => true };
        memory.Remember("go", english: true);
        Assert.Equal(null, memory.Get("go"), "1 回目はまだ覚えない");
        Assert.True(!memory.Entries().Single().Active, "一覧では「2 回目を待っている」");
        memory.Remember("go", english: true);
        Assert.Equal(true, memory.Get("go"), "2 回目で英語として覚える");
        memory.Remember("to", english: true, explicitChoice: true);
        Assert.Equal(true, memory.Get("to"), "F10 ではっきり直したら 1 回で覚える");
        memory.Remember("api", english: true);
        Assert.Equal(true, memory.Get("api"), "3 文字以上は今までどおり 1 回で覚える");
        memory.Remove(["go"]);
        Assert.Equal(null, memory.Get("go"), "一覧から消したら忘れる");
    }

    [Test]
    public static void ShiftSpace_ConvertsEnglishWordAsRomaji()
    {
        // 英字と判定された語も変換できるように: Shift+Space でローマ字として読んで変換する。
        var memory = new LanguageMemory(null);
        memory.Remember("go", english: true, explicitChoice: true);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("go");
            k.TypeKeys((VirtualKeys.Space, true));
            k.Type("\n");
            Assert.Equal("ご", k.Host.Document, "Shift+Space で日本語の候補が先頭");
            Assert.Equal(false, memory.Get("go"), "日本語で確定したので、次から日本語");
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }

    [Test]
    public static void F6_TeachesJapanese()
    {
        var memory = new LanguageMemory(null);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("google");
            k.Press(VirtualKeys.F6);
            k.Type("\n");
            Assert.Equal("ごおgぇ", k.Host.Document);
            Assert.Equal(false, memory.Get("google"), "F6 でかなにして確定したので覚える");
            var next = new CompositionTests.Keyboard(languages: memory);
            next.Type("google");
            Assert.Equal("ごおgぇ", next.Showing);
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }
}

internal static class RawCandidateTests
{
    [Test]
    public static void Conversion_OffersTypedLetters_AndLearns()
    {
        var memory = new LanguageMemory(null);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("api ");
            var candidates = k.Host.View!.Candidates;
            Assert.True(candidates.Contains("api") && candidates.Contains("ａｐｉ"), $"候補に打ったままの英字と全角の英字: {string.Join(" ", candidates)}");
            // Space を連打して英字まで送る
            for (var i = 0; i < 10 && k.Host.View!.Candidates[k.Host.View.SelectedIndex] != "api"; i++) k.Press(VirtualKeys.Space);
            k.Type("\n");
            Assert.Equal("api", k.Host.Document);
            Assert.Equal(true, memory.Get("api"), "英字を選んで確定したので覚える");
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }

    [Test]
    public static void EnglishWordEndingInN_BeforeParticle()
    {
        foreach (var (typed, expected) in new[] { ("pythonnobug", "pythonのbug"), ("kotlinnihenkou", "kotlinにへんこう"), ("kannji", "かんじ"), ("konnnichiha", "こんにちは") })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, typed);
        }
    }
}

internal static class RomajiTypoTests
{
    [Test]
    public static void RomajiTypos_AreFixedOnCommit()
    {
        foreach (var (typed, expected) in new[]
        {
            ("onegaishimsu", "おねがいします"),   // 抜け
            ("arigatpu", "ありがとう"),           // 隣のキー
            ("arigtou", "ありがとう"),
            ("sumimasne", "すみません"),          // 入れ替わり
            ("shitmeasu", "してます"),
            ("gozaimsu", "ございます"),
            ("yorosikuy", "よろしく"),            // 余計な 1 文字
            ("kinouhaamegafuttemshita", "きのうはあめがふってました"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void RomajiTypos_LeaveEnglishAndAbbreviations()
    {
        foreach (var (typed, expected) in new[]
        {
            ("oknotasuku", "okのたすく"),
            ("googlede", "googleで"),
            ("kyouhamtgdesu", "きょうはmtgです"),
            ("sdakega", "sだけが"),
            ("htmlnokaisetu", "htmlのかいせつ"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void RomajiTypos_CanBeTurnedOff()
    {
        var k = new CompositionTests.Keyboard();
        k.CorrectTypos = false;
        k.Type("onegaishimsu\n");
        Assert.Equal("おねがいしmす", k.Host.Document);
    }

    [Test]
    public static void CamelCaseWord_AfterJapanese_SplitsOffJapanese()
    {
        foreach (var (typed, expected) in new[]
        {
            ("kyouhaGitHubni", "きょうはGitHubに"),
            ("kyouhaGitHubnipushshita", "きょうはGitHubにpushした"),
            ("camelCaseName", "camelCaseName"),         // 識別子はそのまま英字
            ("getElementById", "getElementById"),
            ("dataSetName", "dataSetName"),
            ("sakuraTreeNode", "sakuraTreeNode"),       // 助詞で終わらないローマ字の名前も識別子
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }
}
