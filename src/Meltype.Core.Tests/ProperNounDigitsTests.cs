// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>数字を含む固有名詞 (yachi8000) を、数字の前で区切ってかなにしない (issue #1013)。</summary>
internal static class ProperNounDigitsTests
{
    [Test]
    public static void Issue1013_ProperNounWithDigits()
    {
        foreach (var (typed, expected) in new[]
        {
            ("yachi8000", "yachi8000"), ("yachi8000desu", "yachi8000です"),
            // 数字の無い固有名詞 (neovide) と同じく、文の途中でも英字
            ("korehayachi8000desu", "これはyachi8000です"),
            // 境界: 数字が違う (yachi800)・一覧に無い語 + 数字 (heisei31nenn) は今までどおり
            ("yachi800", "やち800"), ("heisei31nenn", "へいせい31ねん"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Issue1013_ManualLevelKeepsKana()
    {
        // 自動判定が OFF なら、ほかの固有名詞 (neovide) と同じく英字にしない
        var k = new Keyboard { Manual = true };
        k.Type("yachi8000\n");
        Assert.Equal("やち8000", k.Host.Document, "手動では英字にしない");
    }
}
