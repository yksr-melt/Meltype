// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.RegularExpressions;

namespace Meltype.Composition.Utility;

/// <summary>
/// 打った日付・時刻 (10がつ9にち・3じ15ふん) に、/ と : の形を出す (#294)。普通の候補 (10月9日) の後ろに足す。
/// 10がつ9にち → 10/9 / 10/09 / 10月9日 / 10/9(金) … (<see cref="Dates"/>)、3じ15ふん → 3:15 / 03:15。ありえない日付・時刻 (13がつ、25じ) には出さない。
/// </summary>
internal sealed partial class TypedDateConversion : IUtilityConversion
{
    [GeneratedRegex("^(?<m>[0-9０-９]{1,2})がつ(?<d>[0-9０-９]{1,2})にち$")]
    private static partial Regex DateReading();

    [GeneratedRegex("^(?<h>[0-9０-９]{1,2})じ(?<m>[0-9０-９]{1,2})(?:ふん|ぷん)$")]
    private static partial Regex ClockReading();

    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.Reading is not { } reading) return null;
        if (DateReading().Match(reading) is { Success: true } date)
            return Dates(UtilityConversions.Number(date.Groups["m"].Value), UtilityConversions.Number(date.Groups["d"].Value), input.Context.Now) is { } dates
                ? new(dates, UtilityPlacement.Append) : null;
        if (ClockReading().Match(reading) is not { Success: true } clock) return null;
        var (hour, minute) = (UtilityConversions.Number(clock.Groups["h"].Value), UtilityConversions.Number(clock.Groups["m"].Value));
        if (hour > 23 || minute > 59) return null;
        return new([$"{hour}:{minute:D2}", $"{hour:D2}:{minute:D2}"], UtilityPlacement.Append);
    }

    /// <summary>
    /// 月と日の書き方の候補。10がつ20にち と 10/20 (<see cref="SlashDateConversion"/>) で同じものを出す。
    /// 10/20 / 10/20 / 10月20日 / 10/20(火) / 10月20日(火) / 2026/10/20 / 2026/10/20(火) / 2026年10月20日。ありえない日付なら null。
    /// 年は先の日付を優先する (予定を打つことが多いので)。今年のその日が今日以降なら今年、過ぎていれば来年。
    /// ただし過ぎてから 30 日以内ならその日 (少し前のことを書くとき。6 月 15 日に打つ 6/1 は今年、1/5 は来年、1 月 5 日に打つ 12/28 は去年)。
    /// </summary>
    internal static string[]? Dates(int month, int day, DateTime now)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31) return null;
        var today = now.Date;
        DateTime? best = null;
        for (var year = today.Year - 1; year <= today.Year + 4; year++) // 去年から (1 月に打つ 12/28)。2/29 は次のうるう年まで
        {
            if (day > DateTime.DaysInMonth(year, month)) continue;
            var date = new DateTime(year, month, day);
            if (date >= today.AddDays(-30)) { best = date; break; }
        }
        if (best is not { } chosen) return null;
        var week = UtilityConversions.WeekDays[(int)chosen.DayOfWeek];
        var y = chosen.Year;
        return [$"{month}/{day}", $"{month:D2}/{day:D2}", $"{month}月{day}日", $"{month}/{day}({week})", $"{month}月{day}日({week})",
            $"{y:D4}/{month:D2}/{day:D2}", $"{y:D4}/{month:D2}/{day:D2}({week})", $"{y}年{month}月{day}日"];
    }
}
