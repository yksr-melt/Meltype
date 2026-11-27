// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

using System.Globalization;
using System.Text.RegularExpressions;

namespace Meltype.Composition.Utility;

/// <summary>予定の言い方 (3日後、来週の金曜日) から日付や時刻を出す。</summary>
internal sealed partial class RelativeDateConversion : IUtilityConversion
{
    [GeneratedRegex("^(?<amount>[0-9０-９]{1,3})(?<unit>にち|しゅうかん|かげつ)(?<direction>ご|まえ)$")]
    private static partial Regex CalendarOffset();

    [GeneratedRegex("^(?<week>せんしゅう|こんしゅう|らいしゅう|さらいしゅう)の(?<day>げつ|か|すい|もく|きん|ど|にち)ようび$")]
    private static partial Regex Weekday();

    [GeneratedRegex("^(?:(?<hours>[0-9０-９]{1,3})じかん(?:(?<half>はん)|(?<minutes>[0-9０-９]{1,3})(?:ふん|ぷん))?|(?<minutes>[0-9０-９]{1,3})(?:ふん|ぷん))(?<direction>ご|まえ)$")]
    private static partial Regex ClockOffset();

    private static readonly string[] DaysFromMonday = ["げつ", "か", "すい", "もく", "きん", "ど", "にち"];

    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.ReadingAcrossSegments is not { } reading) return null;
        var now = input.Context.Now;
        try
        {
            if (ClockOffset().Match(reading) is { Success: true } clock)
            {
                var hours = clock.Groups["hours"].Success ? UtilityConversions.Number(clock.Groups["hours"].Value) : 0;
                var minutes = clock.Groups["half"].Success ? 30 : clock.Groups["minutes"].Success ? UtilityConversions.Number(clock.Groups["minutes"].Value) : 0;
                var time = now.AddMinutes((hours * 60 + minutes) * (clock.Groups["direction"].Value == "ご" ? 1 : -1));
                var invariant = CultureInfo.InvariantCulture;
                var shortTime = time.ToString("HH:mm", invariant);
                var japanese = time.ToString("H'時'mm'分'", invariant);
                return new(time.Date == now.Date ? [shortTime, japanese]
                    : [time.ToString("yyyy-MM-dd", invariant) + " " + shortTime, DateText(time) + japanese], UtilityPlacement.Append) { Reading = reading };
            }
            if (Date(reading, now.Date) is not { } date) return null;
            var text = DateText(date);
            return new([$"{text}({UtilityConversions.WeekDays[(int)date.DayOfWeek]})", text,
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)], UtilityPlacement.Append) { Reading = reading };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTime? Date(string reading, DateTime today)
    {
        // 「来週」の週は月曜日から始まる。
        if (Weekday().Match(reading) is { Success: true } weekday)
        {
            var week = weekday.Groups["week"].Value switch { "せんしゅう" => -1, "らいしゅう" => 1, "さらいしゅう" => 2, _ => 0 };
            var todayIndex = ((int)today.DayOfWeek + 6) % 7;
            return today.AddDays(week * 7 + Array.IndexOf(DaysFromMonday, weekday.Groups["day"].Value) - todayIndex);
        }
        if (CalendarOffset().Match(reading) is { Success: true } offset)
        {
            var amount = UtilityConversions.Number(offset.Groups["amount"].Value) * (offset.Groups["direction"].Value == "ご" ? 1 : -1);
            return offset.Groups["unit"].Value switch { "かげつ" => today.AddMonths(amount), "しゅうかん" => today.AddDays(7 * amount), _ => today.AddDays(amount) };
        }
        return reading switch
        {
            "きのう" => today.AddDays(-1),
            "あした" or "あす" => today.AddDays(1),
            "あさって" => today.AddDays(2),
            "しあさって" or "みっかご" => today.AddDays(3),
            "せんげつまつ" => MonthEnd(today.AddMonths(-1)),
            "こんげつまつ" => MonthEnd(today),
            "らいげつまつ" => MonthEnd(today.AddMonths(1)),
            _ => null,
        };
    }

    private static DateTime MonthEnd(DateTime date) => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static string DateText(DateTime date) => FormattableString.Invariant($"{date.Year}年{date.Month}月{date.Day}日");
}
