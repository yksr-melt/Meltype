// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>数字の後ろの母音 1 文字 (GPT-4o の 4o) を、確定のときに型番として英字にする (issue #1417)。</summary>
internal static class ModelNumberTests
{
    [Test]
    public static void Issue1417_VowelAfterDigit()
    {
        foreach (var (typed, expected) in new[]
        {
            ("4o", "4o"), ("10o", "10o"), ("2e", "2e"), ("gpt4o", "gpt4o"), ("gpt-4o", "gpt-4o"),
            // 境界: 順位の 位 (1i)・続けて打つ語 (4oku = 4億)・英字 1 文字 + 数字 + 英字 1 文字の略語 (m3e)
            ("1i", "1い"), ("4oku", "4おく"), ("m3e", "m3e"),
            // 境界: かなの後ろに続けて打った数字 (kono4o) は、前のかなを英字に戻さない
            ("kono4o", "この4お"), ("kore4o", "これ4お"), ("sore4o", "それ4お"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Issue1417_DecidedOnCommitLikeUnits()
    {
        // 単位 (5min) と同じく、打っている間は かな のまま見せ、確定するときに決める (続けて 4oku と打つかもしれない)
        var k = new Keyboard();
        k.Type("4o");
        Assert.Equal("4お", k.Showing, "打っている間");
    }
}
