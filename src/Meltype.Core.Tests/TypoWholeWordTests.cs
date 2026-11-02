// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>語の途中に偶然できた短い英単語があっても、直すと 1 語になる打ち間違いは直す (issue #971)。</summary>
internal static class TypoWholeWordTests
{
    /// <summary>Windows のスペルチェッカーのように、同梱の一覧に無い語 (kas) も知っている。</summary>
    private sealed class WiderChecker(params string[] extra) : Detection.IWordChecker
    {
        public bool IsAvailable => true;
        public bool IsWord(string lower) => extra.Contains(lower) || Detection.BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void Issue971_TypoInsideWordWithShortEnglishWord()
    {
        Detector.SpellChecker = new WiderChecker("kas", "mas");
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("sumimasne", "すみません"), ("henkasnn", "へんかん"), ("henkasnsuru", "へんかんする"),
                // 境界: 直しても 1 語にならない (tab|で を たべ にしない、issue #220)。保護範囲の中は ProtectionTests で確かめている
                ("tabde", "tabで"), ("kyouhatabdeyaru", "きょうはtabでやる"),
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
