// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>
/// じこう・じこうのあいさつ に、今の月に合う時候の挨拶 (秋冷の候 / 拝啓　秋冷の候、…) を出す (issue #339)。
/// 今の月を先に、続けて前後の月のものも出す。
/// </summary>
internal sealed class SeasonalGreetingConversion : IUtilityConversion
{
    /// <summary>月ごとの時候の挨拶 (「〜の候」の〜)。手紙・メールの書き出しに使う。</summary>
    private static readonly string[][] Terms =
    [
        ["新春", "初春", "厳寒", "大寒"],
        ["余寒", "立春", "春寒", "梅花"],
        ["早春", "春分", "春暖", "浅春"],
        ["桜花", "陽春", "春暖", "晩春"],
        ["新緑", "薫風", "若葉", "立夏"],
        ["入梅", "梅雨", "向暑", "初夏"],
        ["盛夏", "猛暑", "大暑", "酷暑"],
        ["晩夏", "残暑", "立秋", "暮夏"],
        ["初秋", "新秋", "秋晴", "野分"],
        ["秋冷", "紅葉", "秋涼", "寒露"],
        ["晩秋", "霜降", "向寒", "初霜"],
        ["初冬", "師走", "寒冷", "歳晩"],
    ];

    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading is "じこう" or "じこうのあいさつ" ? new([.. Greetings(input.Context.Now.Month)], UtilityPlacement.Append) : null;

    internal static IEnumerable<string> Greetings(int month)
    {
        var months = new[] { month, (month + 10) % 12 + 1, month % 12 + 1 };
        var terms = months.SelectMany(m => Terms[m - 1]).Distinct().ToList();
        return terms.Select(t => $"{t}の候").Concat([$"拝啓　{terms[0]}の候、"]);
    }
}
