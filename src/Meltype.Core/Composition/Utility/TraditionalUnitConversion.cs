// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text.RegularExpressions;

namespace Meltype.Composition.Utility;

/// <summary>数字 + 尺貫法の単位の読み (3しゃく) に、単位の字 (3尺) と、メートル法に直した大きさ (約90.9cm) を出す (issue #340)。</summary>
internal sealed partial class TraditionalUnitConversion : IUtilityConversion
{
    /// <summary>尺貫法の単位 (読み → 単位の字、メートル法での大きさ、メートル法の単位)。</summary>
    private static readonly (string Reading, string Unit, double Size, string Metric)[] Units =
    [
        ("しゃく", "尺", 100.0 / 3.3, "cm"), ("すん", "寸", 10.0 / 3.3, "cm"), ("けん", "間", 200.0 / 110.0, "m"),
        ("じょう", "丈", 100.0 / 33.0, "m"), ("つぼ", "坪", 400.0 / 121.0, "㎡"),
        ("ごう", "合", 180.39, "mL"), ("しょう", "升", 1.8039, "L"), ("と", "斗", 18.039, "L"),
        ("かん", "貫", 3.75, "kg"), ("もんめ", "匁", 3.75, "g"), ("きん", "斤", 600, "g"), ("りょう", "両", 37.5, "g"),
    ];

    [GeneratedRegex("^(?<n>[0-9０-９]{1,6}(?:[.．][0-9０-９]{1,3})?)(?<u>しゃく|すん|けん|じょう|つぼ|ごう|しょう|と|かん|もんめ|きん|りょう)$")]
    private static partial Regex UnitReading();

    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading is { } reading && Candidates(reading) is { } candidates ? new(candidates, UtilityPlacement.Append) : null;

    /// <summary>尺貫法の読みでなければ null。</summary>
    internal static string[]? Candidates(string reading)
    {
        if (UnitReading().Match(reading) is not { Success: true } match) return null;
        var digits = new string(match.Groups["n"].Value.Select(c => c switch { >= '０' and <= '９' => (char)(c - '０' + '0'), '．' => '.', _ => c }).ToArray());
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
        var unit = Units.First(u => u.Reading == match.Groups["u"].Value);
        var size = value * unit.Size;
        var text = size >= 100 ? Math.Round(size).ToString("0", CultureInfo.InvariantCulture)
            : Math.Round(size, 2).ToString("0.##", CultureInfo.InvariantCulture);
        return [$"{digits}{unit.Unit}", $"約{text}{unit.Metric}"];
    }
}
