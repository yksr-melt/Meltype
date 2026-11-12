// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>入力欄の外の言語を判定の手がかりにする (実験的。issue #287)。</summary>
internal static class PageLanguageTests
{
    [Test]
    public static void EnglishRatio_CountsLettersAndJapanese()
    {
        Assert.True(PageLanguage.EnglishRatio("Sure, I will check the logs tomorrow and get back to you.") >= 0.8, "英語のスレッド");
        Assert.True(PageLanguage.EnglishRatio("明日ログを確認して、また連絡しますね。よろしくお願いします。") <= 0.2, "日本語のスレッド");
        Assert.True(PageLanguage.EnglishRatio("ok") is null, "文字が少なければ分からない");
        Assert.True(PageLanguage.EnglishRatio(null) is null, "読めない");
    }

    private static string Typed(string word, double? ratio, bool on = true, string? before = null)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var k = new CompositionTests.Keyboard(pageEnglishRatio: () => ratio);
            k.Settings.PageLanguageHint = on;
            if (before is not null) k.Type(before);
            k.Type(word + "\n");
            return k.Host.Document;
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = previous;
        }
    }

    [Test]
    public static void FirstWord_LeansEnglishOnAnEnglishPage()
    {
        // 英語のスレッドで返信を打ち始める: 英語とも日本語とも読める語は英語のまま
        foreach (var word in new[] { "sure", "made", "sushi" })
        {
            Assert.Equal(word, Typed(word, 0.95), "英語の画面: " + word);
            Assert.True(Typed(word, null) != word, "画面の言語が分からなければ今までどおり日本語: " + word);
            Assert.True(Typed(word, 0.95, on: false) != word, "設定が OFF なら今までどおり: " + word);
        }
        // ローマ字としてしか読めない語は、英語の画面でも日本語
        Assert.Equal("また", Typed("mata", 0.95));
        Assert.Equal("これ", Typed("kore", 0.95));
        // 日本語の画面では寄せない (はっきりした英単語までかなにしない)
        Assert.Equal("nice", Typed("nice", 0.05));
        Assert.Equal("すれ", Typed("sure", 0.05));
    }

    [Test]
    public static void PrecedingText_WinsOverThePage()
    {
        // 入力欄の前に日本語を確定していれば、画面の言語は見ない
        Assert.Equal("きょうはすれ", Typed("sure", 0.95, before: "kyouha\n"));
    }
}
