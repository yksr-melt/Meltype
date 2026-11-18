// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Tests;

internal static class ColorContrastTests
{
    [Test]
    public static void CustomCompositionColors_WarnOnlyWhenTheTextIsHardToRead()
    {
        Assert.True(new Settings { CompositionTheme = CompositionTheme.Custom }.ColorWarning() is null, "既定のカスタムの色では警告しない");
        foreach (var (back, text) in new[] { ("#1E2A3A", "#F0F0F0"), ("#FFF8E7", "#222222"), ("#000000", "#FFFF00") })
            Assert.True(new Settings { CompositionTheme = CompositionTheme.Custom, CompositionCustomBackColor = back, CompositionCustomTextColor = text }.ColorWarning() is null, $"{back} の上の {text} は読める");
        Assert.True(new Settings { CompositionTheme = CompositionTheme.Custom, CompositionCustomBackColor = "#202020", CompositionCustomTextColor = "#000000" }.ColorWarning() is not null, "暗い背景の黒い文字は警告する");
        Assert.True(new Settings { CompositionTheme = CompositionTheme.Dark, CompositionCustomBackColor = "#202020", CompositionCustomTextColor = "#000000" }.ColorWarning() is null, "カスタムでなければ使わない色なので警告しない");
    }

    [Test]
    public static void CustomCompositionColors_PickTheLightOrDarkThemeFromTheBackground()
    {
        Assert.True(new Settings { CompositionTheme = CompositionTheme.Custom, CompositionCustomBackColor = "#FFF8E7" }.CompositionIsLight(null), "明るい背景はライトの案内の色");
        Assert.True(!new Settings { CompositionTheme = CompositionTheme.Custom, CompositionCustomBackColor = "#1E2A3A" }.CompositionIsLight(true), "暗い背景は Windows がライトでもダークの案内の色");
    }
}
