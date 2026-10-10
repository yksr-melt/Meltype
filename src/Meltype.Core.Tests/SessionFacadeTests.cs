// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>Mac 版・Linux 版から使う入力の本体 (MeltypeSession) のテスト。OS がキーを 1 つずつ渡し、使ったかをその場で返す。</summary>
internal static class SessionFacadeTests
{
    private static MeltypeSession Create() => new(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => new Settings());

    /// <summary>文字を 1 つずつ打つ (英字は大文字なら Shift 付き)。</summary>
    private static List<SessionResult> Type(MeltypeSession session, string text, string? before = null)
    {
        var results = new List<SessionResult>();
        foreach (var c in text)
        {
            var vk = c switch
            {
                ' ' => VirtualKeys.Space,
                '\n' => VirtualKeys.Return,
                '\b' => VirtualKeys.Back,
                ',' => VirtualKeys.OemComma,
                '.' => VirtualKeys.OemPeriod,
                '-' => VirtualKeys.OemMinus,
                '"' or '\'' => 0xDE,
                '#' => '3',
                '/' => VirtualKeys.Oem2,
                _ when char.IsAsciiLetter(c) => char.ToUpperInvariant(c),
                _ => c,
            };
            char? ch = c is ' ' or '\n' or '\b' ? null : c;
            results.Add(session.HandleKey(vk, ch, char.IsAsciiLetterUpper(c), false, false, false, before));
        }
        return results;
    }

    [Test]
    public static void MacMixedInputRegression()
    {
        foreach (var (input, expected) in new[] {
            ("wsldeshell", "wslでshell"),
            ("kyouhagoogledekensaku", "今日はgoogleで検索"),
            ("seeyouagain", "see you again"),
            ("I want to go to the park", "I want to go to the park"),
            ("Zorgblax", "Zorgblax"),
            ("stackoverflow", "stackoverflow"),
            ("https://example.com", "https://example.com"),
            ("alice@example.com", "alice@example.com"),
            ("getUserName", "getUserName"),
            ("google  shell", "google  shell"),
            ("google\u3000shell", "google\u3000shell"),
            ("get_user_name", "get_user_name") })
        {
            var session = new MeltypeSession(CompositionDetector.CreateDefault(), new CompositionTests.FakeConverter(),
                new CompositionOptions { LiveConversion = () => true, AutomaticEnglishSpacing = () => true }, () => new Settings());
            var results = Type(session, input + "\n");
            var output = "";
            for (var i = 0; i < results.Count; i++)
            {
                foreach (var edit in results[i].Commits)
                    output = output[..Math.Max(0, output.Length - edit.DeleteBefore)] + edit.Text;
                if (!results[i].Consumed) output += (input + "\n")[i];
            }
            Assert.Equal(expected, output, input);
        }
    }

    [Test]
    public static void Romaji_ComposesAndEnterCommits()
    {
        var session = Create();
        var results = Type(session, "kyouha");
        Assert.True(results.All(r => r.Consumed), "打った英字はアプリに渡さない");
        Assert.Equal("きょうは", results[^1].View?.Text);
        var enter = Type(session, "\n")[0];
        Assert.True(enter.Consumed, "Enter は確定に使う");
        Assert.Equal("きょうは", enter.Commits.Single().Text);
        Assert.True(enter.View is null, "確定したら変換ボックスを閉じる");
    }

    [Test]
    public static void EnglishWord_SpaceCommitsWithSpace()
    {
        var session = Create();
        var results = Type(session, "google ");
        Assert.Equal("google ", results[^1].Commits.Single().Text);
    }

    [Test]
    public static void KeysOutsideComposition_GoToTheApp()
    {
        var session = Create();
        Assert.True(!session.HandleKey(VirtualKeys.Left, null, false, false, false, false).Consumed, "変換ボックスが空なら矢印はアプリへ");
        Assert.True(!session.HandleKey('C', 'c', false, false, false, true).Consumed, "Command + C はアプリの操作");
        Assert.True(!Type(session, " ")[0].Consumed, "空白はアプリへ");
        session.Direct = true;
        Assert.True(!Type(session, "a")[0].Consumed, "英数 (直接入力) ならすべてアプリへ");
    }

    [Test]
    public static void AutoCorrect_ReplacesPreviousWord()
    {
        // i を確定したあと、want で英文と分かったら i を確定し直す (前の文字を消して入れ直す)
        var session = Create();
        Type(session, "i ");
        var results = Type(session, "want ");
        Assert.True(results.SelectMany(r => r.Commits).Any(c => c.DeleteBefore > 0), "前の語を確定し直す");
    }

    [Test]
    public static void AutoCorrect_TellsWhatToDelete()
    {
        // 確定し直すときは、消す文字 (前に確定した文字) も渡す。DLL は入力欄の文字が同じときだけ消す
        var session = Create();
        // i の確定 (Space で変換したものは、次の w で確定する) → want の確定のときに確定し直す
        var commits = Type(session, "i want ").SelectMany(r => r.Commits).ToList();
        var index = commits.FindIndex(c => c.DeleteBefore > 0);
        Assert.True(index > 0, "前の語を確定し直す");
        var first = commits[0].Text;
        var correction = commits[index];
        Assert.Equal(first, correction.Expect);
        Assert.Equal(first.Length, correction.DeleteBefore);
        Assert.True(new SessionResult(true, [correction], null).ToJson().Contains("\"expect\":"), "JSON にも入れる");
    }

    [Test]
    public static void Commits_WithoutDeleteHaveNoExpect()
    {
        var session = Create();
        var commit = Type(session, "kyouha\n")[^1].Commits.Single();
        Assert.True(commit.Expect is null, "消さない確定には付けない");
        Assert.True(!new SessionResult(true, [commit], null).ToJson().Contains("expect"), "JSON にも入れない");
    }

    [Test]
    public static void AutoCorrect_NotAfterKeyPassedToApp()
    {
        // 確定したあと、アプリに渡したキー (矢印など) でキャレットが動いたかもしれない: 消す位置がずれるので確定し直さない
        // Enter で確定したあとも、次の語で確定し直す (動かさなければ)
        var control = Create();
        Type(control, "i\n");
        Assert.True(Type(control, "want ").SelectMany(r => r.Commits).Any(c => c.DeleteBefore > 0), "動かさなければ確定し直す");

        var session = Create();
        Type(session, "i\n");
        var left = session.HandleKey(VirtualKeys.Left, null, false, false, false, false);
        Assert.True(!left.Consumed, "変換していないときの矢印はアプリに渡す");
        var results = Type(session, "want ");
        Assert.True(!results.SelectMany(r => r.Commits).Any(c => c.DeleteBefore > 0), "関係ない文字を消さない");
    }

    [Test]
    public static void AutoCorrect_NotAfterCaretMovedOutside()
    {
        // OS の IME がアプリに通したキー・クリックで動いたと知らせてきた (Meltype IME の "moved")
        var session = Create();
        Type(session, "i\n");
        session.ForgetLastCommit();
        var results = Type(session, "want ");
        Assert.True(!results.SelectMany(r => r.Commits).Any(c => c.DeleteBefore > 0), "関係ない文字を消さない");
    }

    [Test]
    public static void ShortcutWhileComposing_CommitsThenPassesTheKey()
    {
        var session = Create();
        Type(session, "abc");
        var result = session.HandleKey('S', 's', false, false, false, true);
        Assert.True(!result.Consumed, "Command + S はアプリへ");
        Assert.True(result.Commits.Count == 1, "その前に変換ボックスの内容を確定する");
    }

    [Test]
    public static void CtrlHWhileComposing_IsBackSpace()
    {
        // #150: Meltype IME・Mac・Linux も、入力中の Ctrl+H は確定せずに 1 音消す
        var session = Create();
        Type(session, "aiueo");
        var result = session.HandleKey('H', '\b', false, true, false, false);
        Assert.True(result.Consumed && result.Commits.Count == 0, "確定しない");
        Assert.Equal("あいうえ", result.View?.Text);
    }

    [Test]
    public static void ArrowWhileComposing_SelectsClauses()
    {
        var session = Create();
        Type(session, "kyouha");
        var result = session.HandleKey(VirtualKeys.Right, null, false, false, false, false);
        Assert.True(result.Consumed && result.View is { Converting: true }, "変換前の矢印は文節の選択に使う");
    }

    [Test]
    public static void Candidates_CanBeSelectedByIndex()
    {
        var session = Create();
        Type(session, "api ");
        var view = session.SelectCandidate(2).View!;
        Assert.Equal(2, view.SelectedIndex);
        var commit = Type(session, "\n")[0];
        Assert.Equal(view.Candidates[2], commit.Commits.Single().Text);
    }

    [Test]
    public static void Json_IsEscaped()
    {
        var result = new SessionResult(true, [new TextEdit(2, "a\"b\\c\n")], new CompositionView("x", ["y"], 0, true, "h", ["x"], 0));
        const string expected = """{"consumed":true,"commits":[{"deleteBefore":2,"text":"a\"b\\c\n"}],"view":{"text":"x","converting":true,"selectedIndex":0,"selectedClause":0,"hint":"h","candidates":["y"],"clauses":["x"],"suggestion":null,"meaning":null,"notes":[null]}}""";
        Assert.Equal(expected, result.ToJson());
    }

    [Test]
    public static void CodeInput_PassesCodeButComposesCommentsAndStrings()
    {
        var session = Create();
        session.CodeInput = true;
        Assert.True(Type(session, "kyouha", "const value = ").All(r => !r.Consumed), "コードは英数のまま");
        Assert.True(Type(session, "hello").All(r => !r.Consumed), "周辺文字列が取得できない場合も英数");
        Assert.Equal("きょうは", Type(session, "kyouha", "// ")[^1].View?.Text);
        Type(session, "\n");
        Assert.Equal("きょうは", Type(session, "kyouha", "const text = \"")[^1].View?.Text);
        Type(session, "\n");
        Assert.Equal("きょうは", Type(session, "kyouha", "/* comment\n")[^1].View?.Text);
    }

    [Test]
    public static void MixedInput_ConvertsJapaneseAndKeepsEnglish()
    {
        var session = Create();
        Type(session, "kyouhagoogledekensaku ");
        var result = Type(session, "\n")[0];
        Assert.Equal("今日はgoogleで検索", result.Commits.Single().Text);
    }

    [Test]
    public static void CodeInput_TracksTerminalQuotesWithoutSurroundingText()
    {
        var session = Create();
        session.CodeInput = true;
        Assert.True(Type(session, "echo \"").All(r => !r.Consumed), "シェルのコマンドは直接入力");
        Assert.Equal("きょうは", Type(session, "kyouha")[^1].View?.Text);
        var close = Type(session, "\"")[0];
        Assert.True(!close.Consumed && close.Commits.Single().Text == "きょうは", "閉じ引用符は英数のまま通し、日本語は確定");
        Assert.True(Type(session, "hello").All(r => !r.Consumed), "文字列の外では英数に戻す");
        session.CodeInput = true;
        Type(session, "# ");
        Assert.Equal("きょうは", Type(session, "kyouha")[^1].View?.Text);
    }

    [Test]
    public static void CodeInput_TracksCommentSlashesWithSigilWordsEnabled()
    {
        var session = Create();
        session.CodeInput = true;
        Assert.True(Type(session, "// ").All(r => !r.Consumed), "コメントの開始は直接入力");
        Assert.Equal("きょうは", Type(session, "kyouha")[^1].View?.Text);
    }

    [Test]
    public static void EnglishSentence_RemainsEnglish()
    {
        var session = Create();
        var results = Type(session, "I want to go to the park\n");
        Assert.Equal("I want to go to the park", string.Concat(results.SelectMany(r => r.Commits).Select(e => e.Text)));
    }

    [Test]
    public static void JoinedFarewell_RemainsEnglish()
    {
        var session = Create();
        var results = Type(session, "seeyouagain\n");
        Assert.Equal("seeyouagain", string.Concat(results.SelectMany(r => r.Commits).Select(e => e.Text)));
    }

    [Test]
    public static void Go_OffersEnglishAndHiraganaWhileTyping()
    {
        foreach (var (index, expected) in new[] { (0, "go"), (1, "ご") })
        {
            var session = Create();
            var view = Type(session, "go")[^1].View!;
            Assert.True(view.Candidates.SequenceEqual(new[] { "go", "ご" }), "入力中に両方の候補を返す");
            Assert.Equal(expected, session.SelectCandidate(index).View?.Text);
            Assert.Equal(expected, Type(session, "\n")[0].Commits.Single().Text);
        }
        var cycling = Create();
        Type(cycling, "go");
        Assert.Equal("go", Type(cycling, " ")[0].View?.Text);
        Assert.Equal("ご", Type(cycling, " ")[0].View?.Text);
    }

    [Test]
    public static void GoPreview_IsLimitedToExactAutomaticToken()
    {
        foreach (var input in new[] { "g", "goo", "google", "gohan", "kyouhago" })
        {
            var session = Create();
            var view = Type(session, input)[^1].View!;
            Assert.True(view.Candidates.Count == 0, input + ": go preview must not replace other candidates");
            Assert.True(!session.SelectCandidate(0).View!.Converting, input + ": no preview selection");
        }
        var forced = Create();
        Type(forced, "go");
        var kana = forced.HandleKey(VirtualKeys.F6, null, false, false, false, false).View!;
        Assert.Equal("ご", kana.Text);
        Assert.Equal(0, kana.Candidates.Count);
        var invalid = Create();
        Type(invalid, "go");
        Assert.True(!invalid.SelectCandidate(2).View!.Converting, "invalid preview index leaves composition unchanged");
    }

    [Test]
    public static void EnglishVerb_ConjugationDoesNotCrossRomajiTokens()
    {
        foreach (var (input, expected) in new[] {
            ("reflectsareta", "reflectされた"), ("reflectsita", "reflectした"),
            ("reflectsaseru", "reflectさせる"), ("reflected", "reflected"), ("reflects", "reflects") })
        {
            var detector = CompositionDetector.CreateDefault();
            detector.SpellChecker = Meltype.Detection.BuiltInWordChecker.Shared;
            var session = new MeltypeSession(detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => new Settings());
            var results = Type(session, input + "\n");
            Assert.Equal(expected, string.Concat(results.SelectMany(r => r.Commits).Select(e => e.Text)));
        }
    }

    [Test]
    public static void Misspelling_IsExposedAndCorrected()
    {
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { Misspellings = MisspellingDictionary.Load(null) }, () => new Settings());
        Assert.True(Type(session, "buresureddo")[^1].View?.Suggestion?.Contains("ブレスレット") == true, "Mac に修正候補を返す");
        var result = session.HandleKey(VirtualKeys.Tab, null, false, false, false, false);
        Assert.Equal("ぶれすれっと", result.View?.Text);
    }

    [Test]
    public static void SigilWord_PassesToTheAppUntilSpace()
    {
        // #193: 先頭の /review は打つたびにアプリへ渡し (補完を選べるように)、空白の後は日本語に戻る。
        var session = Create();
        Assert.True(Type(session, "/review").All(r => !r.Consumed && r.View is null), "/review はそのままアプリへ");
        Assert.True(!Type(session, " ")[0].Consumed, "空白もアプリへ");
        Assert.Equal("きょう", Type(session, "kyou")[^1].View?.Text);
        // google を確定した後の空白に続く @ も。
        session = Create();
        Type(session, "google ");
        Assert.True(Type(session, "@file").All(r => !r.Consumed), "空白の後の @file はアプリへ");
    }

    [Test]
    public static void SigilWord_UsesTextBeforeCaret()
    {
        // キャレットの前の文字を教えてもらえば、それで決める (taro@ は対象外、"> " の後は対象)。
        var session = Create();
        Assert.True(Type(session, "@", before: "taro")[0].Consumed, "前が英字なら @ は変換ボックスへ");
        session = Create();
        session.HandleKey(VirtualKeys.Left, null, false, false, false, false);
        Assert.True(!Type(session, "/", before: "> ")[0].Consumed, "前が空白なら / はアプリへ");
        // 前の文字を打ったのを見ていれば、空 (前の文字を読めないアプリ) より自分の記録を信じる。
        session = Create();
        session.Direct = true;
        Type(session, "taro");
        session.Direct = false;
        Assert.True(Type(session, "@", before: "")[0].Consumed, "taro と打った後の @ は変換ボックスへ");
    }
}
