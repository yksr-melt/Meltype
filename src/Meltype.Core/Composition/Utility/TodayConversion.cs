// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>
/// きょう に今日の日付を出す (issue #208)。
/// 2026年10月9日 / 2026年10月9日(金) / 10月9日(金) / 2026/10/09 / 2026/10/09(金) / 10/9(金) / 2026-10-09 / 金曜日 / 20261009 …
/// 区切りの / と : は、Windows の地域の設定に左右されないように数字から組み立てる (DateTime の書式の / : は地域の区切りになる)。
/// </summary>
internal sealed class TodayConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.Reading != "きょう") return null;
        var now = input.Context.Now;
        var day = UtilityConversions.WeekDays[(int)now.DayOfWeek];
        var date = $"{now.Year}年{now.Month}月{now.Day}日";
        var monthDay = $"{now.Month}月{now.Day}日";
        var slashDate = $"{now.Year:D4}/{now.Month:D2}/{now.Day:D2}";
        return new([date, $"{date}({day})", monthDay, $"{monthDay}({day})", slashDate, $"{slashDate}({day})", $"{now.Month}/{now.Day}({day})",
            $"{now.Year:D4}-{now.Month:D2}-{now.Day:D2}", $"{day}曜日", $"{now.Year:D4}{now.Month:D2}{now.Day:D2}"], UtilityPlacement.Append);
    }
}
