// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Tests;

internal static class ColorContrastTests
{
    [Test]
    public static void ColorContrast_RejectsColorsThatCannotBeRead()
    {
        Assert.True(ColorContrast.Problem("文字", (255, 255, 255), "背景", (0, 0, 0), ColorContrast.TextMinimum) is null, "白と黒は読める");
        Assert.True(ColorContrast.Problem("文字", (0, 0, 0), "背景", (0x20, 0x20, 0x20), ColorContrast.TextMinimum) is not null, "暗い背景の黒い文字は読めない");
        Assert.True(ColorContrast.Problem("文字", (0xFF, 0xDC, 0x78), "背景", (255, 255, 255), ColorContrast.TextMinimum) is not null, "白い背景のダークの予測の文字は読めない");
        Assert.True(ColorContrast.Problem("文字", (0x77, 0x77, 0x77), "背景", (255, 255, 255), ColorContrast.MarkMinimum) is null, "目印は文字より薄くてもよい");
    }
}
