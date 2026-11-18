// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>re- + 英単語の決まりで、ローマ字として読める語 (tori) を英単語にしない (issue #705)。</summary>
internal static class HyphenRomajiTests
{
    [Test]
    public static void Issue705_HyphenPrefixDoesNotCutInsideRomaji()
    {
        foreach (var (typed, expected) in new[]
        {
            // - の後ろが読める語で、英字が続く: 長音を挟んだ日本語
            ("re-torimitto", "れーとりみっと"),
            // 同梱の一覧にもある語 (make・take・name・age・made) + 名詞・付属語は今までどおり英語
            ("re-makeban", "re-makeばん"), ("re-takesatsuei", "re-takeさつえい"), ("anti-agekea", "anti-ageけあ"),
            ("pre-madekitto", "pre-madeきっと"), ("re-takegahitsuyou", "re-takeがひつよう"), ("lo-fiwokiku", "lo-fiをきく"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }
}
