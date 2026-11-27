// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

using System.Globalization;
using System.Text.RegularExpressions;

namespace Meltype.Composition.Utility;

/// <summary>
/// 関数形式のカラー表記 (hex(dadada)・rgb(59,130,246)・hsl(217,91%,60%)) を相互変換して候補に出す。
/// 未確定の原文全体が 関数名(引数) に一致し、引数を色として解釈できたときだけ反応する。
/// 各候補の色見本 (#rrggbbaa) も Core で決めて返す。
/// </summary>
internal sealed partial class ColorConversion : IUtilityConversion
{
    [GeneratedRegex(@"^#?(?<hex>[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexArgsPattern();

    [GeneratedRegex(@"^(?<r>\d{1,3})\s*(?:,\s*|\s+)(?<g>\d{1,3})\s*(?:,\s*|\s+)(?<b>\d{1,3})(?:\s*(?:,|/)\s*(?<a>\d+(?:\.\d+)?%?|\.\d+%?))?$")]
    private static partial Regex RgbArgsPattern();

    [GeneratedRegex(@"^(?<h>-?\d+(?:\.\d+)?)\s*(?:,\s*|\s+)(?<s>\d+(?:\.\d+)?)%\s*(?:,\s*|\s+)(?<l>\d+(?:\.\d+)?)%(?:\s*(?:,|/)\s*(?<a>\d+(?:\.\d+)?%?|\.\d+%?))?$")]
    private static partial Regex HslArgsPattern();

    public UtilityCandidates? Convert(UtilityInput input)
    {
        var raw = CompositionText.Raw(input.Text);
        if (input.Text.KanaInput || input.Text.Mode != DisplayMode.Auto) return null;
        var pairs = CandidatesWithColors(raw);
        if (pairs.Count == 0) return null;
        var candidates = pairs.Select(p => p.Text).ToList();
        var colors = pairs.Select(p => (string?)p.Color).ToList();
        if (!candidates.Contains(raw))
        {
            candidates.Add(raw);
            colors.Add(null);
        }
        return new(candidates, UtilityPlacement.Replace) { Colors = colors };
    }

    /// <summary>原文全体が有効な色変換の関数呼び出しに一致するか。</summary>
    internal static bool Matches(string raw) => CandidatesWithColors(raw).Count > 0;

    internal static List<string> Candidates(string raw) =>
        CandidatesWithColors(raw).Select(p => p.Text).ToList();

    internal static List<(string Text, string Color)> CandidatesWithColors(string raw)
    {
        if (!TrySplitFunctionCall(raw, out var name, out var args)) return [];

        switch (name)
        {
            case "hex" when TryParseHexArgs(args, out var hexColor):
                {
                    var results = new List<(string Text, string Color)>();
                    var exactColor = FormatRgbaHex(hexColor.R, hexColor.G, hexColor.B, ToAlphaByte(hexColor.Alpha));
                    if (hexColor.RawHex.Length is 3 or 4)
                        AddUnique(results, "#" + hexColor.RawHex.ToLowerInvariant(), exactColor);
                    AddFormattedCandidates(results, hexColor.R, hexColor.G, hexColor.B, hexColor.Alpha, includeHex: true, includeRgb: true, includeHsl: true);
                    return results;
                }
            case "rgb" or "rgba" when TryParseRgbArgs(args, out var rgbColor):
                {
                    var results = new List<(string Text, string Color)>();
                    AddFormattedCandidates(results, rgbColor.R, rgbColor.G, rgbColor.B, rgbColor.Alpha, includeHex: true, includeRgb: true, includeHsl: true);
                    return results;
                }
            case "hsl" or "hsla" when TryParseHslArgs(args, out var hslColor):
                {
                    var (r, g, b) = HslToRgb(hslColor.H, hslColor.S, hslColor.L);
                    var results = new List<(string Text, string Color)>();
                    AddFormattedCandidates(results, r, g, b, hslColor.Alpha, includeHex: true, includeRgb: true, includeHsl: true);
                    return results;
                }
            default:
                return [];
        }
    }

    private static bool TrySplitFunctionCall(string raw, out string name, out string args)
    {
        name = "";
        args = "";
        if (raw.Length < 4 || !raw.EndsWith(')')) return false;
        var open = raw.IndexOf('(');
        if (open <= 0) return false;
        for (var i = 0; i < open; i++)
        {
            if (!char.IsAsciiLetter(raw[i])) return false;
        }
        name = raw[..open].ToLowerInvariant();
        args = raw[(open + 1)..^1].Trim();
        return args.Length > 0;
    }

    private static void AddFormattedCandidates(List<(string Text, string Color)> results, byte r, byte g, byte b, double? alpha, bool includeHex, bool includeRgb, bool includeHsl)
    {
        var alphaByte = ToAlphaByte(alpha);
        var exactColor = FormatRgbaHex(r, g, b, alphaByte);

        if (alpha is { } a)
        {
            if (includeHex)
            {
                AddUnique(results, FormatHex(r, g, b, alphaByte, lowercase: true), exactColor);
                AddUnique(results, FormatHex(r, g, b, alphaByte, lowercase: false), exactColor);
            }
            if (includeRgb) AddUnique(results, FormatRgba(r, g, b, a), exactColor);
            if (includeHsl)
            {
                var (h, s, l) = RgbToHsl(r, g, b);
                var (hr, hg, hb) = HslToRgb(h, s, l);
                AddUnique(results, FormatHsla(h, s, l, a), FormatRgbaHex(hr, hg, hb, alphaByte));
            }
            return;
        }

        if (includeHex)
        {
            AddUnique(results, FormatHex(r, g, b, lowercase: true), exactColor);
            AddUnique(results, FormatHex(r, g, b, lowercase: false), exactColor);
        }
        if (includeRgb) AddUnique(results, FormatRgb(r, g, b), exactColor);
        if (includeHsl)
        {
            var (h, s, l) = RgbToHsl(r, g, b);
            var (hr, hg, hb) = HslToRgb(h, s, l);
            AddUnique(results, FormatHsl(h, s, l), FormatRgbaHex(hr, hg, hb, 255));
        }
    }

    private static void AddUnique(List<(string Text, string Color)> results, string text, string color)
    {
        if (!results.Any(r => r.Text == text))
            results.Add((text, color));
    }

    private static byte ToAlphaByte(double? alpha) =>
        alpha is { } a ? (byte)Math.Round(a * 255.0, MidpointRounding.AwayFromZero) : (byte)255;

    private static bool TryParseHexArgs(string args, out (string RawHex, byte R, byte G, byte B, double? Alpha) color)
    {
        color = default;
        if (HexArgsPattern().Match(args) is not { Success: true } match) return false;
        var hex = match.Groups["hex"].Value;
        switch (hex.Length)
        {
            case 3:
                color = (hex, ExpandNibble(hex[0]), ExpandNibble(hex[1]), ExpandNibble(hex[2]), null);
                return true;
            case 4:
                color = (hex, ExpandNibble(hex[0]), ExpandNibble(hex[1]), ExpandNibble(hex[2]), ExpandNibble(hex[3]) / 255.0);
                return true;
            case 6:
                color = (hex, ParseHexByte(hex[..2]), ParseHexByte(hex[2..4]), ParseHexByte(hex[4..6]), null);
                return true;
            case 8:
                color = (hex, ParseHexByte(hex[..2]), ParseHexByte(hex[2..4]), ParseHexByte(hex[4..6]), ParseHexByte(hex[6..8]) / 255.0);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseRgbArgs(string args, out (byte R, byte G, byte B, double? Alpha) color)
    {
        color = default;
        if (RgbArgsPattern().Match(args) is not { Success: true } match) return false;
        if (!int.TryParse(match.Groups["r"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var r) || r > 255 ||
            !int.TryParse(match.Groups["g"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var g) || g > 255 ||
            !int.TryParse(match.Groups["b"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var b) || b > 255)
            return false;

        double? alpha = null;
        if (match.Groups["a"].Success)
        {
            if (!TryParseAlpha(match.Groups["a"].Value, out var parsedAlpha)) return false;
            alpha = parsedAlpha;
        }

        color = ((byte)r, (byte)g, (byte)b, alpha);
        return true;
    }

    private static bool TryParseHslArgs(string args, out (double H, double S, double L, double? Alpha) color)
    {
        color = default;
        if (HslArgsPattern().Match(args) is not { Success: true } match) return false;
        if (!double.TryParse(match.Groups["h"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ||
            !double.TryParse(match.Groups["s"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) || s is < 0 or > 100 ||
            !double.TryParse(match.Groups["l"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) || l is < 0 or > 100)
            return false;

        double? alpha = null;
        if (match.Groups["a"].Success)
        {
            if (!TryParseAlpha(match.Groups["a"].Value, out var parsedAlpha)) return false;
            alpha = parsedAlpha;
        }

        color = (h, s, l, alpha);
        return true;
    }

    private static bool TryParseAlpha(string text, out double alpha)
    {
        if (text.EndsWith('%'))
        {
            if (double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) && pct is >= 0 and <= 100)
            {
                alpha = pct / 100.0;
                return true;
            }
            alpha = 0;
            return false;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 1)
        {
            alpha = value;
            return true;
        }

        alpha = 0;
        return false;
    }

    private static byte ExpandNibble(char c)
    {
        var n = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => c - 'A' + 10,
        };
        return (byte)((n << 4) | n);
    }

    private static byte ParseHexByte(string s) =>
        byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    internal static (int H, int S, int L) RgbToHsl(byte rByte, byte gByte, byte bByte)
    {
        var r = rByte / 255.0;
        var g = gByte / 255.0;
        var b = bByte / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var l = (max + min) / 2.0;

        if (d == 0)
            return (0, 0, (int)Math.Round(l * 100.0, MidpointRounding.AwayFromZero));

        var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (max == r)
            h = ((g - b) / d + (g < b ? 6.0 : 0.0)) * 60.0;
        else if (max == g)
            h = ((b - r) / d + 2.0) * 60.0;
        else
            h = ((r - g) / d + 4.0) * 60.0;

        var hInt = ((int)Math.Round(h, MidpointRounding.AwayFromZero) % 360 + 360) % 360;
        var sInt = (int)Math.Round(s * 100.0, MidpointRounding.AwayFromZero);
        var lInt = (int)Math.Round(l * 100.0, MidpointRounding.AwayFromZero);
        return (hInt, sInt, lInt);
    }

    internal static (byte R, byte G, byte B) HslToRgb(double hDeg, double sPct, double lPct)
    {
        var h = ((hDeg % 360.0) + 360.0) % 360.0 / 360.0;
        var s = sPct / 100.0;
        var l = lPct / 100.0;

        if (s == 0)
        {
            var gray = (byte)Math.Round(l * 255.0, MidpointRounding.AwayFromZero);
            return (gray, gray, gray);
        }

        var q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        var p = 2.0 * l - q;
        var r = (byte)Math.Round(HueToRgb(p, q, h + 1.0 / 3.0) * 255.0, MidpointRounding.AwayFromZero);
        var g = (byte)Math.Round(HueToRgb(p, q, h) * 255.0, MidpointRounding.AwayFromZero);
        var b = (byte)Math.Round(HueToRgb(p, q, h - 1.0 / 3.0) * 255.0, MidpointRounding.AwayFromZero);
        return (r, g, b);
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1.0;
        if (t > 1) t -= 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }

    private static string FormatRgbaHex(byte r, byte g, byte b, byte a) => $"#{r:x2}{g:x2}{b:x2}{a:x2}";

    private static string FormatHex(byte r, byte g, byte b, bool lowercase) =>
        lowercase ? $"#{r:x2}{g:x2}{b:x2}" : $"#{r:X2}{g:X2}{b:X2}";

    private static string FormatHex(byte r, byte g, byte b, byte a, bool lowercase) =>
        lowercase ? $"#{r:x2}{g:x2}{b:x2}{a:x2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";

    private static string FormatRgb(byte r, byte g, byte b) => $"rgb({r}, {g}, {b})";

    private static string FormatRgba(byte r, byte g, byte b, double a) => $"rgba({r}, {g}, {b}, {FormatAlpha(a)})";

    private static string FormatHsl(int h, int s, int l) => $"hsl({h}, {s}%, {l}%)";

    private static string FormatHsla(int h, int s, int l, double a) => $"hsla({h}, {s}%, {l}%, {FormatAlpha(a)})";

    private static string FormatAlpha(double a) =>
        Math.Round(a, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture);
}
