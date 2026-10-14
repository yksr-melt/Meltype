// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

using Meltype.Composition;

namespace Meltype.Tests;

internal static class MemoryPersistenceTests
{
    [Test]
    public static void MemoryFiles_ReadLegacySchemasAndPreserveEntriesAfterSaving()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-memory-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var languagesPath = Path.Combine(directory, "languages.json");
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            // Count / Explicit の無い旧版の学習データも受け入れる。
            File.WriteAllText(languagesPath, "{\"sushi\":{\"English\":true,\"Used\":\"2026-10-07T00:00:00Z\"}}");
            File.WriteAllText(conversionsPath, "{\"すし\":{\"Text\":\"鮨\",\"Used\":\"2026-10-07T00:00:00Z\"}}");
            File.WriteAllText(translationsPath, "{\"すし\":{\"sushi\":2}}");
            var languages = new LanguageMemory(languagesPath);
            var conversions = new ConversionHistory(conversionsPath);
            var translations = new TranslationHistory(translationsPath);
            Assert.Equal(true, languages.Get("sushi"));
            Assert.Equal("鮨", conversions.Get("すし"));
            Assert.Equal(2, translations.Get("すし").Single().Count);

            languages.Remember("cafe", english: false, explicitChoice: true);
            conversions.Remember("くらいど", "クラウド");
            translations.Remember("すし", "sushi");

            var restoredLanguages = new LanguageMemory(languagesPath);
            var restoredConversions = new ConversionHistory(conversionsPath);
            Assert.Equal(true, restoredLanguages.Get("sushi"));
            Assert.Equal(false, restoredLanguages.Get("cafe"));
            Assert.Equal("鮨", restoredConversions.Get("すし"));
            Assert.Equal("クラウド", restoredConversions.Get("くらいど"));
            Assert.Equal(3, new TranslationHistory(translationsPath).Get("すし").Single().Count);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void BrokenMemoryFiles_AreCopiedToBroken_AndKeptAfterSaving()
    {
        // 読めないファイルは .broken に残す。読めないまま空で動き、次の保存で上書きしても元の中身は .broken に残る。
        var directory = Path.Combine(Path.GetTempPath(), "meltype-memory-broken-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string broken = "{\"sig\":\"--\\n雪代\",";
            var snippetsPath = Path.Combine(directory, "snippets.json");
            var languagesPath = Path.Combine(directory, "languages.json");
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            foreach (var path in new[] { snippetsPath, languagesPath, conversionsPath, translationsPath }) File.WriteAllText(path, broken);

            var snippets = new SnippetStore(snippetsPath);
            var languages = new LanguageMemory(languagesPath);
            var conversions = new ConversionHistory(conversionsPath);
            var translations = new TranslationHistory(translationsPath);
            snippets.Set("ty", "ありがとうございます。");
            languages.Remember("cafe", english: false, explicitChoice: true);
            conversions.Remember("くらいど", "クラウド");
            translations.Remember("すし", "sushi");

            foreach (var path in new[] { snippetsPath, languagesPath, conversionsPath, translationsPath })
            {
                Assert.True(File.Exists(path + ".broken"), $"{Path.GetFileName(path)} を .broken に残す");
                Assert.Equal(broken, File.ReadAllText(path + ".broken"));
            }
            Assert.Equal("ありがとうございます。", new SnippetStore(snippetsPath).Get("ty"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void ForgetSince_RemovesOnlyRecentEntries_AndTheyStayGoneAfterReload()
    {
        // #277: 最近覚えたものだけを忘れる。忘れたものは読み込み直しても戻らない (再利用されない)
        var directory = Path.Combine(Path.GetTempPath(), "meltype-forget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var languagesPath = Path.Combine(directory, "languages.json");
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var phrasesPath = Path.Combine(directory, "phrases.txt");
            var translationsPath = Path.Combine(directory, "translations.json");
            // 昨日覚えたもの
            File.WriteAllText(languagesPath, "{\"api\":{\"English\":true,\"Used\":\"2026-10-01T00:00:00Z\",\"Count\":2,\"Explicit\":true}}");
            File.WriteAllText(conversionsPath, "{\"はし\":{\"Text\":\"箸\",\"Used\":\"2026-10-01T00:00:00Z\"}}");
            File.WriteAllText(phrasesPath, $"きょうは\t今日は\t1\t{new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).Ticks}\n");
            var languages = new LanguageMemory(languagesPath);
            var conversions = new ConversionHistory(conversionsPath);
            var phrases = new PhraseHistory(phrasesPath);
            var since = DateTime.UtcNow.AddMinutes(-1);
            // 今覚えたもの
            languages.Remember("ok", english: true, explicitChoice: true);
            conversions.Remember("かみ", "紙");
            phrases.Remember("あしたは", "明日は");

            Assert.Equal(1, languages.ForgetSince(since));
            Assert.Equal(1, conversions.ForgetSince(since));
            Assert.Equal(1, phrases.ForgetSince(since));
            Assert.Equal(0, conversions.ForgetSince(since), "2 回目は何も無い");

            var reloadedLanguages = new LanguageMemory(languagesPath);
            var reloadedConversions = new ConversionHistory(conversionsPath);
            var reloadedPhrases = new PhraseHistory(phrasesPath);
            Assert.Equal(null, reloadedLanguages.Get("ok"), "最近の語は戻らない");
            Assert.Equal(null, reloadedConversions.Get("かみ"));
            Assert.True(!reloadedPhrases.StartingWith("あし").Any(), "最近の語句は戻らない");
            Assert.Equal(true, reloadedLanguages.Get("api"), "前に覚えたものは残す");
            Assert.Equal("箸", reloadedConversions.Get("はし"));
            Assert.True(reloadedPhrases.StartingWith("きょ").Contains("今日は"), "前に覚えた語句は残す");

            // 英訳の記録も、リセットで消して読み込み直しても戻らない
            var translations = new TranslationHistory(translationsPath);
            translations.Remember("すし", "sushi");
            translations.Clear();
            Assert.True(new TranslationHistory(translationsPath).Get("すし").Count == 0, "英訳の記録を消す");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
