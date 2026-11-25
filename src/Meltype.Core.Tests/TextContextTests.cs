// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;
using Meltype.Ime;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>入力欄の文脈 (キャレットの前の文字) で変わる英日の判定。同じ文字なら、確定してからの時間・読めるかによらず同じになる。</summary>
internal static class TextContextTests
{
    /// <summary>確定・通したキーを文字にするアプリ。Shown を入れると、その文字を渡し続ける (確定した文字の反映が遅いアプリ)。</summary>
    private sealed class App
    {
        private readonly TestSession _session;
        private long _now = 100_000;

        public App(string text = "")
        {
            _session = new TestSession(() => new Settings { LiveConversion = false, PredictiveCandidates = false }, timeMs: () => _now);
            Text = text;
        }

        public string Text { get; set; }

        public string? Shown { get; set; }

        public bool Readable { get; set; } = true;

        public string? View { get; private set; }

        private string? Before => Readable ? Shown ?? Text : null;

        public void Wait(int ms) => _now += ms;

        public void MoveCaret() => _session.Send(new ImeEvent.CaretMoved(new ImeCaret { Moved = true, PassedKey = 0 }), Before);

        public App Type(string keys)
        {
            foreach (var c in keys) Key(c switch { '\n' => VirtualKeys.Return, ' ' => VirtualKeys.Space, '\b' => VirtualKeys.Back, _ => 0 }, c);
            return this;
        }

        public App Key(int vk, char ch, bool ctrl = false)
        {
            _now += 30;
            var result = _session.HandleKey(vk, ch >= ' ' ? ch : null, char.IsAsciiLetterUpper(ch), ctrl, false, false, Before);
            foreach (var edit in result.Commits) Text = Text[..^edit.DeleteBefore] + edit.Text;
            View = result.View?.Text;
            if (!result.Consumed && !ctrl) Text = ch == '\b' ? Text[..^1] : Text + ch;
            return this;
        }
    }

    /// <summary>first を打った後、時間・読めるか・キャレットの知らせを変えて second を打ち、どれでも同じ文字が入るのを確かめる。</summary>
    private static void AssertFollows(string first, string second, string expected)
    {
        foreach (var (name, prepare) in new (string, Action<App>)[]
                 {
                     ("直後", _ => { }), ("2 秒後", app => app.Wait(2000)), ("キャレットの知らせの後", app => app.MoveCaret()), ("読めない", app => app.Readable = false),
                     ("読めない 2 秒後", app => { app.Readable = false; app.Wait(2000); }),
                 })
        {
            var app = new App();
            app.Readable = name is not ("読めない" or "読めない 2 秒後");
            app.Type(first);
            var start = app.Text;
            prepare(app);
            app.Type(second);
            Assert.Equal(expected, app.Text[start.Length..], $"{first}{second}: {name}");
        }
    }

    [Test]
    public static void SameText_SameResult()
    {
        // 日本語の文の中の英単語・数字のすぐ後ろは、英語とも日本語とも読める語は日本語、英語の語は英語
        foreach (var first in new[] { "kyouhagoogle\n", "githubnipush\n", "kyouha100\n" })
        {
            AssertFollows(first, "demo\n", "でも");
            AssertFollows(first, "sore\n", "それ");
            AssertFollows(first, ",\n", "、");
            AssertFollows(first, "issuekakuka\n", "issueかくか");
            AssertFollows(first, "ver1.1\n", "ver1.1");
        }
        // Space で英単語を確定したら英語の続き
        AssertFollows("kyouhasample ", "issuekakuka\n", "issueかくか");
        // 英語だけの後は英語
        AssertFollows("google\n", "demo\n", "demo");
    }

    [Test]
    public static void Numbers_AreNotEnglish()
    {
        AssertFollows("100\n", ",\n", "、");
        AssertFollows("kyouha100\n", "demo\n", "でも");
        foreach (var text in new[] { "100", "1,000", "100 " }) Assert.Equal(text + "、", new App(text).Type(",\n").Text, text);
    }

    [Test]
    public static void Issue1542_PreviousLine_IsNotContext()
    {
        Assert.Equal("macOS\n、", new App().Type("macOS\n\n,\n").Text);
        Assert.Equal("macOS\nでも", new App("macOS\n").Type("demo\n").Text);
        Assert.Equal("日本語\nver1.1", new App("日本語\n").Type("ver1.1\n").Text);
    }

    [Test]
    public static void Typing_IsCommittedAsShown_AfterAPause()
    {
        foreach (var first in new[] { "kyouhagoogle\n", "google\n", "100\n" })
        {
            var app = new App().Type(first).Type(",");
            var shown = app.View;
            var start = app.Text;
            app.Wait(3000);
            app.Type("\n");
            Assert.Equal(shown, app.Text[start.Length..], first);
        }
    }

    [Test]
    public static void SlowApp_UsesTheOwnCommit()
    {
        // 確定の前の文字を渡し続けるアプリ (Meltype キーボードは、変換中は変換を始めたときに読んだ文字を渡す)
        foreach (var (before, first, second, expected) in new[] { ("", "google\n", "demo\n", "demo"), ("Hello ", "kyou\n", "demo\n", "でも"), ("今日は", "google\n", ",\n", "、") })
        {
            var app = new App(before) { Shown = before };
            app.Type(first);
            var start = app.Text;
            app.Wait(3000);
            app.Type(second);
            Assert.Equal(expected, app.Text[start.Length..], first + second);
        }
    }

    [Test]
    public static void OwnCommit_IsForgotten_WhenAKeyGoesToTheApp()
    {
        // Enter で送信・全選択して削除。欄は確定の前と同じ空に戻る
        foreach (var send in new Action<App>[] { app => app.Type("\n"), app => app.Key('A', 'a', ctrl: true).Key(0x2E, '\0'), app => app.Type("\b") })
        {
            var app = new App().Type("google\n");
            send(app);
            app.Text = "";
            Assert.Equal("でも", app.Type("demo\n").Text);
        }
    }
}
