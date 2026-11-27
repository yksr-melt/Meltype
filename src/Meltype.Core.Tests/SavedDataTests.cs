// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Learning;

namespace Meltype.Tests;

/// <summary>手で書き換えた・細工したバックアップから戻した学習データを読んでも、入力・変換が止まらない。</summary>
internal static class SavedDataTests
{
    private static void WithFile(string content, Action<string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-saved-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        try { test(path); }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".broken");
        }
    }

    [Test]
    public static void LanguageMemory_NullValue_IsDropped()
    {
        WithFile("{\"sushi\":null,\"api\":{\"English\":true,\"Count\":2,\"Explicit\":true}}", path =>
        {
            var memory = new LanguageMemory(path);
            Assert.True(memory.Get("sushi") is null, "null の語は覚えていない");
            Assert.Equal(true, memory.Get("api"), "ほかの語は読む");
        });
    }

    [Test]
    public static void TranslationHistory_NullValue_IsDropped()
    {
        WithFile("{\"すし\":null,\"りんご\":{\"apple\":2}}", path =>
        {
            var history = new TranslationHistory(path);
            Assert.Equal(0, history.Get("すし").Count);
            Assert.Equal("apple", history.Get("りんご")[0].Word);
        });
    }

    [Test]
    public static void ConversionHistory_NullValue_IsDropped()
    {
        WithFile("{\"きしゃ\":null,\"かんじ\":{\"Text\":null},\"きょう\":{\"Text\":\"今日\"}}", path =>
        {
            var history = new ConversionHistory(path);
            Assert.True(history.Get("きしゃ") is null, "null の読みは覚えていない");
            Assert.True(history.Get("かんじ") is null, "Text が null の読みは覚えていない");
            Assert.Equal("今日", history.Get("きょう"), "ほかの読みは読む");
        });
    }
}
