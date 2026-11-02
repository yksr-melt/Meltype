// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>re- + 英単語の決まりで、語の途中で切らない (issue #705)。</summary>
internal static class HyphenRomajiTests
{
    /// <summary>Windows のスペルチェッカーのように、同梱の一覧に無い語 (tori) も知っている。</summary>
    private sealed class WiderChecker(params string[] extra) : Detection.IWordChecker
    {
        public bool IsAvailable => true;
        public bool IsWord(string lower) => extra.Contains(lower) || Detection.BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void Issue705_HyphenPrefixDoesNotCutInsideRomaji()
    {
        Detector.SpellChecker = new WiderChecker("tori", "fi");
        try
        {
            foreach (var (typed, expected) in new[]
            {
                // - の後ろが読める語で、英字が続く: 長音を挟んだ日本語
                ("re-torimitto", "れーとりみっと"),
                // 境界: 英字が続かない・後ろが付属語・- の後ろが読めない語
                ("re-tori", "re-tori"), ("lo-fiwokiku", "lo-fiをきく"), ("re-installsuru", "re-installする"),
            })
            {
                var k = new Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
        }
        finally
        {
            Detector.SpellChecker = null;
        }
    }
}
