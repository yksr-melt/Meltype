// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

namespace Meltype.Config;

/// <summary>利用者が決めた色の組み合わせが読めるか (WCAG 2 のコントラスト比)。</summary>
internal static class ColorContrast
{
    /// <summary>文字に要る比 (WCAG 2 の AA)。</summary>
    public const double TextMinimum = 4.5;

    /// <summary>2 色のコントラスト比 (1〜21)。</summary>
    public static double Ratio((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>fore が back の上で読みにくければ、利用者に見せる警告。読めれば null。</summary>
    public static string? Warning(string foreName, (int R, int G, int B) fore, string backName, (int R, int G, int B) back) =>
        Ratio(fore, back) >= TextMinimum ? null
            : $"「{foreName}」と「{backName}」の色が近く、文字が見えにくくなります (コントラスト比 {Ratio(fore, back):0.0}。{TextMinimum:0.0} 以上がおすすめです)。";

    private static double Luminance((int R, int G, int B) color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

    private static double Channel(int value)
    {
        var c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
