// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>.moe のドメイン (issue #1356)。</summary>
internal static class DomainSuffixTests
{
    [Test]
    public static void Issue1356_MoeDomain()
    {
        foreach (var (typed, expected) in new[]
        {
            ("meltype.moe", "meltype.moe"), ("github.moe", "github.moe"),
            // 境界: 前がローマ字の日本語 (句点の後ろの もえ)・moe で始まる日本語・一覧に無い ne
            ("sugoku.moe", "すごく。もえ"), ("yoroshiku.moeteru", "よろしく。もえてる"), ("ii.ne", "いい。ね"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }
}
