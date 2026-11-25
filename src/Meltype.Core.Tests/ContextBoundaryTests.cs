// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Tests;

internal static class ContextBoundaryTests
{
    private static (CompositionTextState Text, CompositionEnv Env) Create(string before = "", bool manual = false, LanguageMemory? memory = null)
    {
        var detector = CompositionDetector.CreateDefault();
        detector.Memory = memory;
        detector.IsCommonJapanese = RomajiTypoCorrector.Load(detector.Romaji).IsCommonJapanese;
        var context = TextContext.None with
        {
            PrecedingEnglish = before.Length == 0 ? null : true,
            PrecedingEnglishSentence = before.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2,
            PrecedingEnglishName = CompositionContext.IsEnglishNameContext(before),
        };
        return (new CompositionTextState(), TestSupport.Env(detector, new Settings { AutoDetection = !manual }) with { Context = () => context });
    }

    [Test]
    public static void EnglishSentence_PreservesUnknownNamesAndRespectsOverrides()
    {
        foreach (var word in new[] { "taro", "jirou", "haruto" })
        {
            var (text, env) = Create("my name is ");
            foreach (var c in word) CompositionText.Append(text, env, c);
            Assert.Equal(word, CompositionText.Display(text, env, final: true), word);
        }
        var japaneseMemory = new LanguageMemory(null);
        japaneseMemory.Remember("taro", english: false, explicitChoice: true);
        foreach (var (text, env) in new[] { Create(), Create("my name is ", manual: true), Create("my name is ", memory: japaneseMemory) })
        {
            foreach (var c in "taro") CompositionText.Append(text, env, c);
            Assert.Equal("たろ", CompositionText.Display(text, env, final: true));
        }
        // 英字に直して覚えた日本語由来の語 (sushi) は英字のまま (tools/test-native-boundaries.py と同じ)
        var englishMemory = new LanguageMemory(null);
        englishMemory.Remember("sushi", english: true, explicitChoice: true);
        englishMemory.Remember("sushi", english: true, explicitChoice: true);
        var (learned, learnedEnv) = Create(memory: englishMemory);
        foreach (var c in "sushi") CompositionText.Append(learned, learnedEnv, c);
        Assert.Equal("sushi", CompositionText.Display(learned, learnedEnv, final: true), "覚えた sushi");
        var (knownJapanese, knownEnv) = Create("I study ");
        foreach (var c in "nihongo") CompositionText.Append(knownJapanese, knownEnv, c);
        Assert.Equal("にほんご", CompositionText.Display(knownJapanese, knownEnv, final: true));
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
            var (text, env) = Create();
            foreach (var c in raw) CompositionText.Append(text, env, c);
            Assert.Equal(expected, CompositionText.Display(text, env, final: true), raw);
        }
        foreach (var (raw, expected) in new[] { ("api", "api"), ("sushinoerror", "すしのerror"), ("kapibara", "かぴばら") })
        {
            var (text, env) = Create();
            foreach (var c in raw) CompositionText.Append(text, env, c);
            Assert.Equal(expected, CompositionText.Display(text, env, final: true), raw);
        }
    }
}
