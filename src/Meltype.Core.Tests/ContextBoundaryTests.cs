// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Tests;

internal static class ContextBoundaryTests
{
    private static CompositionText Create(string before = "", DetectionLevel level = DetectionLevel.Balanced, LanguageMemory? memory = null)
    {
        var detector = CompositionDetector.CreateDefault();
        detector.SpellChecker = BuiltInWordChecker.Shared;
        detector.Memory = memory;
        detector.IsCommonJapanese = RomajiTypoCorrector.Load(detector.Romaji).IsCommonJapanese;
        return new CompositionText(detector) { PrecedingEnglish = before.Length == 0 ? null : true,
            PrecedingEnglishSentence = before.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2,
            PrecedingEnglishName = CompositionController.IsEnglishNameContext(before),
            Level = () => level };
    }

    [Test]
    public static void EnglishSentence_PreservesUnknownNamesAndRespectsOverrides()
    {
        foreach (var word in new[] { "taro", "jirou", "haruto" })
        {
            var text = Create("my name is ");
            foreach (var c in word) text.Append(c);
            Assert.Equal(word, text.Display(final: true), word);
        }
        var japaneseMemory = new LanguageMemory(null);
        japaneseMemory.Remember("taro", english: false, explicitChoice: true);
        foreach (var text in new[] { Create(), Create("my name is ", DetectionLevel.Manual), Create("my name is ", memory: japaneseMemory) })
        {
            foreach (var c in "taro") text.Append(c);
            Assert.Equal("たろ", text.Display(final: true));
        }
        // 英字に直して覚えた日本語由来の語 (sushi) は英字のまま (tools/test-native-boundaries.py と同じ)
        var englishMemory = new LanguageMemory(null);
        englishMemory.Remember("sushi", english: true, explicitChoice: true);
        englishMemory.Remember("sushi", english: true, explicitChoice: true);
        var learned = Create(memory: englishMemory);
        foreach (var c in "sushi") learned.Append(c);
        Assert.Equal("sushi", learned.Display(final: true), "覚えた sushi");
        var knownJapanese = Create("I study ");
        foreach (var c in "nihongo") knownJapanese.Append(c);
        Assert.Equal("にほんご", knownJapanese.Display(final: true));
    }

    [Test]
    public static void LanguageMemory_SpanLookupMatchesStringIncludingLegacyEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), "meltype-span-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{\"sushi\":{\"English\":true,\"Count\":0},\"api\":{\"English\":false,\"Count\":0}}");
            var memory = new LanguageMemory(path) { IsCommonJapanese = word => word == "sushi", IsReadableRomaji = _ => true };
            Check("missing"); Check("sushi"); Check("api");
            memory.Remember("sushi", true, explicitChoice: true); Check("sushi");
            memory.Remember("go", true); Check("go");
            memory.Remember("go", true); Check("go");
            memory.Remember("no", true, explicitChoice: true); Check("no");
            memory.Remember("taro", false, explicitChoice: true); Check("taro");
            void Check(string word) => Assert.Equal(memory.Get(word), memory.Get(word.AsSpan()), word);
            Assert.True(memory.Get("missing".AsSpan()) is null, "未登録語は null");
            Assert.Equal(false, memory.Get("taro".AsSpan()));
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    [Test]
    public static void ListedTerm_UsesParticleAndEnglishRightContext()
    {
        foreach (var (raw, expected) in new[] { ("apinoerror", "apiのerror"), ("apigabug", "apiがbug"), ("apinitests", "apiにtests") })
        {
            var text = Create();
            foreach (var c in raw) text.Append(c);
            Assert.Equal(expected, text.Display(final: true), raw);
        }
        foreach (var (raw, expected) in new[] { ("api", "api"), ("sushinoerror", "すしのerror"), ("kapibara", "かぴばら") })
        {
            var text = Create();
            foreach (var c in raw) text.Append(c);
            Assert.Equal(expected, text.Display(final: true), raw);
        }
    }
}
