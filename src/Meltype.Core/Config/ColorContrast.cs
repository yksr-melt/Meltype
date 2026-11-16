// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>
/// 利用者が決めた色の組み合わせが読めるか (WCAG 2 のコントラスト比)。
/// 背景と文字が近い色だと文字が見えなくなるので、設定画面はその組み合わせを保存しない。
/// </summary>
public static class ColorContrast
{
    /// <summary>文字に要る比 (WCAG 2 の AA)。</summary>
    public const double TextMinimum = 4.5;

    /// <summary>文字でない目印 (選んでいる候補の枠・下線・スイッチ) に要る比 (WCAG 2 の非テキスト)。</summary>
    public const double MarkMinimum = 3.0;

    /// <summary>2 色のコントラスト比 (1〜21)。</summary>
    public static double Ratio((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var (light, dark) = (Luminance(a), Luminance(b)) is var (x, y) && x >= y ? (x, y) : (y, x);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>fore が back の上で見えなければ、利用者に見せる理由。見えれば null。</summary>
    public static string? Problem(string foreName, (int R, int G, int B) fore, string backName, (int R, int G, int B) back, double minimum) =>
        Ratio(fore, back) >= minimum ? null
            : $"「{foreName}」と「{backName}」の色が近く、見えにくくなります (コントラスト比 {Ratio(fore, back):0.0}。{minimum:0.0} 以上にしてください)。";

    private static double Luminance((int R, int G, int B) color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

    private static double Channel(int value)
    {
        var c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
