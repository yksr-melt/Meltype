// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>影響範囲の確認に使う、読みのローマ字 (issue #1196)。</summary>
internal static class ImpactTests
{
    [Test]
    public static void Readings_BecomeTypableRomaji()
    {
        foreach (var (reading, romaji) in new[]
                 {
                     ("みぢか", "midika"), ("こんにちは", "konnnichiha"), ("しんぶん", "shinbunn"), ("きっぷ", "kippu"),
                     ("しゃしん", "shashinn"), ("きょう", "kyou"), ("ほんや", "honnya"), ("まっちゃ", "maccha"),
                 })
            Assert.Equal(romaji, Impact.ToRomaji(reading), reading);
        // 打ち方が決めにくい読みは入れない
        foreach (var reading in new[] { "らーめん", "ふぁん", "あっ", "ゔぁ" })
            Assert.Equal(null, Impact.ToRomaji(reading), reading);
    }
}
