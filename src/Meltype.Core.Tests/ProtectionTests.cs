// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// MT-001 / MT-002: メンション・URL・メール・パスの保護範囲と、確定操作ごとの契約 (acceptance/acceptance_cases.json)。
/// </summary>
internal static class ProtectionTests
{
    // ---- scanner: 保護する ----
    [Test]
    public static void Scanner_FindsUrlEmailPathMention()
    {
        foreach (var (raw, kind) in new[]
                 {
                     ("https://example.com/path?q=nihongo&x=a%2Fb#konnichiwa", ProtectedKind.Url),
                     ("file:///home/taro/main.ts", ProtectedKind.Url),
                     ("https://example.com/", ProtectedKind.Url),
                     ("https://example.com/a(b)c", ProtectedKind.Url),
                     ("taro.yamada+tag@example.com", ProtectedKind.Email),
                     ("taro.yamada@", ProtectedKind.Email),
                     ("../src/main.ts", ProtectedKind.PosixPath),
                     ("~/project/main.ts", ProtectedKind.PosixPath),
                     ("./src/", ProtectedKind.PosixPath),
                     ("C:/Users/taro/project", ProtectedKind.WindowsPath),
                     ("\\\\server\\share\\nihongo.txt", ProtectedKind.WindowsPath),
                     ("@User_123", ProtectedKind.Mention),
                     ("/home", ProtectedKind.PosixPath),
                     ("/tmp", ProtectedKind.PosixPath),
                     ("/etc", ProtectedKind.PosixPath),
                     ("/usr", ProtectedKind.PosixPath),
                 })
        {
            var spans = ProtectedSpanScanner.Scan(raw);
            Assert.True(spans.Count == 1, $"1 つの保護区間: {raw} (実際 {spans.Count})");
            Assert.Equal(kind, spans[0].Kind, raw);
            Assert.Equal(0, spans[0].Start, raw);
            Assert.Equal(raw.Length, spans[0].End, raw);
            Assert.True(ProtectedSpanScanner.CoversWhole(raw), $"全体を保護: {raw}");
        }
    }

    // ---- scanner: 保護しない ----
    [Test]
    public static void Scanner_LeavesOrdinaryTextAlone()
    {
        foreach (var raw in new[]
                 {
                     "a/b", "1/2", "///", "@", "/", ":", "kyouhagithubnipushshita",
                     "konnichiwa.", "OPENAI_API_KEY", "camelCase", "2026-10-05", "hello", "upah_setu",
                     "/nihongo", "/ashita",
                 })
        {
            var spans = ProtectedSpanScanner.Scan(raw);
            Assert.Equal(0, spans.Count, $"保護しない: {raw}");
        }
    }

    [Test]
    public static void Scanner_ReanalyseAfterEdit()
    {
        // @ を消すと保護が外れる (保護フラグだけが残らない)。
        Assert.True(ProtectedSpanScanner.CoversWhole("@kuraido"), "@kuraido は保護する");
        Assert.Equal(0, ProtectedSpanScanner.Scan("@").Count, "@ だけは保護しない");
        // ドメインを打ち始めるまでも暫定保護する。
        Assert.True(ProtectedSpanScanner.CoversWhole("taro.yamada@"), "taro.yamada@ は暫定保護する");
        Assert.Equal(0, ProtectedSpanScanner.Scan("taro.yamada").Count, "taro.yamada だけは保護しない");
    }

    [Test]
    public static void Scanner_DoesNotReusePreviouslyProtectedText()
    {
        // 不完全な隣接トークンは前の保護区間を優先する。後の @ から前の文字をメールとして再利用しない。
        foreach (var (raw, expected, kind) in new[]
                 {
                     ("@a@b", "@a", ProtectedKind.Mention),
                     ("a@b@c", "a@b", ProtectedKind.Email),
                     ("/a@b", "/a@b", ProtectedKind.PosixPath),
                 })
        {
            var span = ProtectedSpanScanner.Scan(raw).Single();
            Assert.Equal(kind, span.Kind, raw);
            Assert.Equal(expected, raw.Substring(span.Start, span.Length), raw);
        }
        var separate = ProtectedSpanScanner.Scan("@user taro@example.com");
        Assert.Equal(2, separate.Count, "区切られたメンションとメールの両方を保護する");
        Assert.Equal(ProtectedKind.Mention, separate[0].Kind);
        Assert.Equal(ProtectedKind.Email, separate[1].Kind);
        Assert.Equal(6, separate[1].Start);
    }

    [Test]
    public static void Protected_ReconversionReplacesSelection()
    {
        var keyboard = new CompositionTests.Keyboard();
        keyboard.Host.Selection = new ReconversionSelection("@user", "@user");
        keyboard.Press(VirtualKeys.Convert);
        keyboard.Press(VirtualKeys.Return);
        Assert.Equal("@user", keyboard.Host.Document);
        Assert.Equal(0, keyboard.Host.Output.Count, "選択を置換し、別の確定入力を足さない");
        Assert.True(keyboard.Showing is null, "再変換の未確定状態を消す");
    }

    // ---- 確定契約 (MeltypeSession は Linux/Mac アダプターと同じ経路) ----

    private static MeltypeSession Create(LanguageMemory? languages = null, bool spaceAroundEnglish = false, bool slashAsMiddleDot = false)
    {
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        // 製品の CreateDefault と同じく、候補辞書・誤字補正・文脈辞書を入れる (実経路と同じ条件で検査する)。
        var options = new CompositionOptions
        {
            Candidates = CandidateDictionary.Load(null),
            ContextRules = ContextRules.Load(null),
            History = new ConversionHistory(null),
            Misspellings = MisspellingDictionary.Load(null),
            RomajiTypos = RomajiTypoCorrector.Load(CompositionTests.Detector.Romaji),
            Languages = languages,
            SpaceAroundEnglish = () => spaceAroundEnglish,
            SlashAsMiddleDot = () => slashAsMiddleDot,
        };
        // 行頭の @・/ (#193 の「そのままアプリへ渡す」) を切り、変換ボックスの中の保護区間の扱いを確かめる。
        return new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), options, () => new Settings { SigilWordsDirect = false, SlashAsMiddleDot = slashAsMiddleDot });
    }

    private static SessionResult TypeOne(MeltypeSession session, char c)
    {
        // Linux ラッパー (ibus-engine-meltype) と同じく、英字は A-Z の仮想キー、他の文字は他文字キー (7)。
        var vk = c switch
        {
            ' ' => VirtualKeys.Space,
            '\n' => VirtualKeys.Return,
            _ when char.IsAsciiLetter(c) => char.ToUpperInvariant(c),
            _ when char.IsAsciiDigit(c) => c,
            _ => 0x07,
        };
        return session.HandleKey(vk, c is ' ' or '\n' ? null : c, char.IsAsciiLetterUpper(c), false, false, false);
    }

    private static void TypeText(MeltypeSession session, string text)
    {
        foreach (var c in text) TypeOne(session, c);
    }

    [Test]
    public static void Protected_CommitsIgnoreAutomaticEnglishSpacing()
    {
        foreach (var raw in new[] { "@user", "taro@example.com", "https://example.com", "./src/main.ts", @"C:\Users\taro" })
        foreach (var operation in new[] { VirtualKeys.Return, VirtualKeys.Space, VirtualKeys.Tab, 0 })
        {
            var session = Create(spaceAroundEnglish: true);
            foreach (var c in raw)
                session.HandleKey(char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : 0x07,
                    c, char.IsAsciiLetterUpper(c), false, false, false, "今日は", "です");
            var result = operation == 0 ? session.CommitPending() :
                session.HandleKey(operation, null, false, false, false, false);
            Assert.Equal(raw + (operation == VirtualKeys.Space ? " " : ""), result.Commits.Single().Text, raw);
            Assert.Equal(operation != VirtualKeys.Tab, result.Consumed, "Tab だけをアプリへ通す");
            Assert.True(result.View is null && !session.IsComposing, "確定後の未確定状態を消す");
        }
    }

    [Test]
    public static void Protected_MentionSpaceCommitsRawPlusHalfWidthSpace()
    {
        var session = Create();
        TypeText(session, "@kuraido");
        var space = TypeOne(session, ' ');
        Assert.True(space.Consumed, "Space は IME が消費する (アプリへ送らない)");
        Assert.Equal("@kuraido ", space.Commits.Single().Text);
        Assert.True(space.View is null, "確定したら変換ボックスを閉じる");
    }

    [Test]
    public static void Protected_EnterCommitsRawWithoutNewline()
    {
        var session = Create();
        TypeText(session, "https://example.com?q=konnichiwa");
        var enter = TypeOne(session, '\n');
        Assert.True(enter.Consumed, "Enter は IME が消費する (改行を入れない)");
        Assert.Equal("https://example.com?q=konnichiwa", enter.Commits.Single().Text);
    }

    [Test]
    public static void Protected_TabCommitsRawAndPassesThrough()
    {
        var session = Create();
        TypeText(session, "taro.yamada@example.com");
        // Linux/Mac は Tab を文字なし (ch=0) で渡す。
        var tab = session.HandleKey(VirtualKeys.Tab, null, false, false, false, false);
        Assert.True(!tab.Consumed, "Tab はアプリへ 1 回だけ渡す");
        Assert.Equal("taro.yamada@example.com", tab.Commits.Single().Text);
    }

    [Test]
    public static void Protected_TabDoesNotApplyKanaMisspellingToPath()
    {
        foreach (var raw in new[] { "./shumire-shon", "/tmp/buresureddo", "https://example.com/shumire-shon" })
        {
            var session = Create();
            TypeText(session, raw);
            var tab = session.HandleKey(VirtualKeys.Tab, null, false, false, false, false);
            Assert.True(!tab.Consumed, $"保護原文の Tab は誤字補正に使わずアプリへ通す: {raw}");
            Assert.Equal(raw, tab.Commits.Single().Text);
            Assert.True(tab.View is null, "確定後は変換ボックスを閉じる");
        }
    }

    [Test]
    public static void Protected_ExplicitConversionCommitsSelectedCandidate()
    {
        foreach (var operation in new[] { VirtualKeys.Return, VirtualKeys.Tab, 0 })
        {
            var session = Create();
            TypeText(session, "@kuraido");
            var conversion = session.HandleKey(VirtualKeys.Space, null, true, false, false, false);
            Assert.True(conversion.View is { Converting: true }, "Shift+Space は明示的な変換");
            var candidates = conversion.View!.Candidates;
            var index = Enumerable.Range(0, candidates.Count).First(i => candidates[i].Contains('＠'));
            var selected = session.SelectCandidate(index).View!.Text;
            Assert.True(selected != "@kuraido", "原文と異なる候補を選んでいる");
            var result = operation == 0 ? session.CommitPending() :
                session.HandleKey(operation, null, false, false, false, false);
            Assert.Equal(selected, result.Commits.Single().Text, "明示的に選んだ候補を確定する");
            Assert.Equal(operation != VirtualKeys.Tab, result.Consumed);
        }
    }

    [Test]
    public static void Protected_FocusCommitsRaw()
    {
        var session = Create();
        TypeText(session, "/home/taro/project");
        var focus = session.CommitPending();
        Assert.Equal("/home/taro/project", focus.Commits.Single().Text);
    }

    [Test]
    public static void Protected_WindowsPathAndEmailKeepBackslashAndAscii()
    {
        var session = Create();
        TypeText(session, "C:\\Users\\taro\\project");
        Assert.Equal("C:\\Users\\taro\\project", session.HandleKey(VirtualKeys.Return, null, false, false, false, false).Commits.Single().Text);
    }

    [Test]
    public static void Protected_BackspaceDeletesRawCharacters()
    {
        var session = Create();
        TypeText(session, "@kuraido");
        for (var i = 0; i < 7; i++) session.HandleKey(VirtualKeys.Back, null, false, false, false, false);
        var view = session.HandleKey(VirtualKeys.Right, null, false, false, false, false).View;
        Assert.True(view is null || view.Text == "@", $"7 回の Backspace で @ だけ残る (実際 {view?.Text})");
        Assert.Equal("@", session.HandleKey(VirtualKeys.Return, null, false, false, false, false).Commits.Single().Text);
    }

    [Test]
    public static void NormalJapanese_StillConverts()
    {
        var session = Create();
        TypeText(session, "kyouha");
        var space = TypeOne(session, ' ');
        Assert.Equal("今日は", space.View?.Text, "普通の日本語の Space は変換のまま");
    }

    [Test]
    public static void MixedProtectedSpans_KeepRawWhileSurroundingJapaneseConverts()
    {
        foreach (var token in new[] { "@kuraido", "https://example.com/nihongo", "taro@example.com", "./shumire-shon", @"C:\Users\taro" })
        {
            var text = new CompositionText(CompositionTests.Detector);
            foreach (var c in "kyouha「" + token + "」ashita") text.Append(c);
            var calls = new List<string>();
            var shown = text.Display(final: true, convert: kana =>
            {
                calls.Add(kana);
                return kana.Replace("きょうは", "今日は").Replace("あした", "明日");
            });
            Assert.Equal("今日は「" + token + "」明日", shown, token);
            Assert.True(calls.All(kana => !kana.Contains(token)), "保護原文を漢字変換へ渡さない");
            Assert.Equal(token, text.ConversionSegments().Single(s => s.IsProtected).Raw);
        }
    }

    [Test]
    public static void MixedProtectedSpans_CommitAndBackspaceKeepRaw()
    {
        foreach (var token in new[] { "@kuraido", "https://example.com/nihongo", "taro@example.com", "./shumire-shon" })
        foreach (var operation in new[] { VirtualKeys.Return, VirtualKeys.Space, VirtualKeys.Tab, 0 })
        {
            var session = Create();
            TypeText(session, "kyouha「" + token + "」ashita");
            var result = operation == 0 ? session.CommitPending() : session.HandleKey(operation, null, false, false, false, false);
            if (operation == VirtualKeys.Space)
                result = session.HandleKey(VirtualKeys.Return, null, false, false, false, false);
            Assert.Equal("きょうは「" + token + "」あした", result.Commits.Single().Text, token);
            Assert.True(result.View is null, "確定後は表示を消す");
        }
        var pending = Create();
        TypeText(pending, "kyouha「@kuraido");
        var erased = pending.HandleKey(VirtualKeys.Back, null, false, false, false, false);
        Assert.Equal("きょうは「@kuraid", erased.View!.Text);
        Assert.Equal("きょうは「@kuraid", pending.CommitPending().Commits.Single().Text);
    }

    [Test]
    public static void MixedProtectedSpans_DoNotAutocorrectRomajiTypos()
    {
        foreach (var token in new[] { "@onegaishimsu", "https://example.com/onegaishimsu", "./onegaishimsu" })
        foreach (var action in new[] { VirtualKeys.Return, VirtualKeys.Space })
        {
            var session = Create();
            TypeText(session, "kyouha「" + token + "」ashita");
            var result = session.HandleKey(action, null, false, false, false, false);
            if (action == VirtualKeys.Space) result = session.HandleKey(VirtualKeys.Return, null, false, false, false, false);
            Assert.Equal("きょうは「" + token + "」あした", result.Commits.Single().Text, token);
        }
    }

    [Test]
    public static void Backspace_PreservesProtectedPartOfCrossingUnit()
    {
        foreach (var raw in new[] { "./z]", "./z[", "@z]", "@z[" })
        {
            var session = Create();
            TypeText(session, raw);
            var removed = session.HandleKey(VirtualKeys.Back, null, false, false, false, false);
            Assert.Equal(raw[..^1], removed.View!.Text, raw);
            Assert.Equal(raw[..^1], session.CommitPending().Commits.Single().Text, raw);
        }
    }

    [Test]
    public static void ExplicitOverride_WinsOverProtection()
    {
        // F10 (半角英数) を明示したら、保護していてもその指定を優先する。
        // 英字で見せている語に F10 を押すと、大文字・小文字の次の段 (すべて大文字) へ進む (main の F10 の動き)。
        var session = Create();
        TypeText(session, "@kuraido");
        session.HandleKey(VirtualKeys.F10, null, false, false, false, false);
        Assert.Equal("@KURAIDO ", TypeOne(session, ' ').Commits.Single().Text);
        // F9 (全角英数) も同様。次の token に指定が漏れない。
        var session2 = Create();
        TypeText(session2, "@kuraido");
        session2.HandleKey(VirtualKeys.F9, null, false, false, false, false);
        var committed = TypeOne(session2, '\n').Commits.Single().Text;
        Assert.Equal(CompositionText.ToFullWidth("@kuraido"), committed);
    }

    [Test]
    public static void Protected_IsNotLearnedOrCorrected()
    {
        var languages = new LanguageMemory(null);
        var session = Create(languages);
        TypeText(session, "https://example.com/?token=synthetic_test_only");
        TypeOne(session, '\n');
        // 保護した原文は学習記憶に残さない。
        Assert.True(languages.Get("https://example.com/?token=synthetic_test_only") is null, "保護原文を学習しない");
    }

    [Test]
    public static void SingleSlashBeforeRomaji_DoesNotLockIntoProtectedPosixPath()
    {
        // SigilWordsDirect = false のときに /nihongo や 、/ashita と打っても PosixPath 保護区間に固定されず日本語になる。
        var session = Create();
        TypeText(session, "/nihongo");
        Assert.Equal("/にほんご", TypeOne(session, '\n').Commits.Single().Text);

        var session2 = Create();
        TypeText(session2, "kyouha,/ashita");
        Assert.Equal("きょうは、/あした", TypeOne(session2, '\n').Commits.Single().Text);

        // /Linux などルートディレクトリ以外の英単語も、1 階層パスとして誤保護されない。
        Assert.Equal(0, ProtectedSpanScanner.Scan("/Linux").Count, "/Linux はルートディレクトリではないので保護されない");
    }

    [Test]
    public static void SingleSlashBeforeRootPath_ProtectsPosixPath()
    {
        // /home, /tmp, /data などの代表的なルートディレクトリは保護区間として保護され、ローマ字かな化 (/ほめ 等) されない。
        var session = Create();
        TypeText(session, "/home");
        Assert.Equal("/home", TypeOne(session, '\n').Commits.Single().Text);

        var session2 = Create();
        TypeText(session2, "/tmp");
        Assert.Equal("/tmp", TypeOne(session2, '\n').Commits.Single().Text);

        var session3 = Create();
        TypeText(session3, "/data");
        Assert.Equal("/data", TypeOne(session3, '\n').Commits.Single().Text);
    }

    [Test]
    public static void SingleSlashBeforeRootPath_WithSlashAsMiddleDot_ProtectsPosixPath()
    {
        // SlashAsMiddleDot = true のときでも、/home や /tmp などは ・ほめ にならず /home として保護される。
        var session = Create(slashAsMiddleDot: true);
        TypeText(session, "/home");
        Assert.Equal("/home", TypeOne(session, '\n').Commits.Single().Text);

        var session2 = Create(slashAsMiddleDot: true);
        TypeText(session2, "/tmp");
        Assert.Equal("/tmp", TypeOne(session2, '\n').Commits.Single().Text);

        // /nihongo は中黒のまま ・にほんご になる。
        var session3 = Create(slashAsMiddleDot: true);
        TypeText(session3, "/nihongo");
        Assert.Equal("・にほんご", TypeOne(session3, '\n').Commits.Single().Text);
    }
}
