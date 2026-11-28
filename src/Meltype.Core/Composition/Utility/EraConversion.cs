// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

using System.Globalization;
using System.Text.RegularExpressions;

namespace Meltype.Composition.Utility;

/// <summary>西暦と和暦を換算し、年だけが分かるときは誕生日の前後の年齢を出す。</summary>
internal sealed partial class EraConversion : IUtilityConversion
{
    private static readonly JapaneseCalendar Calendar = new();
    private static readonly DateTimeFormatInfo JapaneseFormat = CreateFormat();

    [GeneratedRegex("^(?<era>めいじ|たいしょう|しょうわ|へいせい|れいわ)?(?<year>[0-9０-９]{1,4}|がん)ねん$")]
    private static partial Regex YearReading();

    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.ReadingAcrossSegments is not { } reading || YearReading().Match(reading) is not { Success: true } match) return null;
        var eraReading = match.Groups["era"].Value;
        var number = match.Groups["year"].Value;
        var year = number == "がん" ? 1 : UtilityConversions.Number(number);
        if (year == 0) return null;
        if (eraReading.Length == 0 && (year < Calendar.MinSupportedDateTime.Year || number == "がん")) return null;

        var result = new List<string>();
        if (eraReading.Length > 0)
        {
            var name = eraReading switch { "めいじ" => "明治", "たいしょう" => "大正", "しょうわ" => "昭和", "へいせい" => "平成", _ => "令和" };
            var era = Calendar.Eras.FirstOrDefault(e => JapaneseFormat.GetEraName(e) == name);
            if (era == 0 || !TryWesternYear(era, year, out year)) return null;
            result.Add(FormattableString.Invariant($"{year}年"));
        }
        else
        {
            // 改元の年には 2 つの元号がある (2019 年は平成 31 年と令和元年)。年末の元号を先に出す。
            var first = new DateTime(year, 1, 1);
            var last = new DateTime(year, 12, 31);
            result.Add(EraYear(last));
            if (first >= Calendar.MinSupportedDateTime && Calendar.GetEra(first) != Calendar.GetEra(last)) result.Add(EraYear(first));
        }
        var age = input.Context.Now.Year - year;
        if (age is >= 0 and <= 150)
            result.Add(age > 0 ? FormattableString.Invariant($"今年{age}歳（{age - 1}〜{age}歳）") : "今年0歳");
        return new(result, UtilityPlacement.Append) { Reading = reading };
    }

    private static bool TryWesternYear(int era, int eraYear, out int year)
    {
        year = 0;
        if (eraYear > 100) return false;
        try { year = Calendar.ToDateTime(eraYear, 1, 1, 0, 0, 0, 0, era).Year; }
        catch (ArgumentOutOfRangeException) when (era == Calendar.GetEra(Calendar.MinSupportedDateTime) && eraYear == 1)
        {
            year = Calendar.MinSupportedDateTime.Year;
        }
        catch (ArgumentOutOfRangeException) { return false; }
        var first = new DateTime(year, 1, 1);
        var last = new DateTime(year, 12, 31);
        return Calendar.GetEra(last) == era || (first >= Calendar.MinSupportedDateTime && Calendar.GetEra(first) == era);
    }

    private static string EraYear(DateTime date)
    {
        var year = Calendar.GetYear(date);
        return JapaneseFormat.GetEraName(Calendar.GetEra(date))
            + (year == 1 ? "元" : year.ToString(CultureInfo.InvariantCulture)) + "年";
    }

    private static DateTimeFormatInfo CreateFormat()
    {
        var culture = new CultureInfo("ja-JP");
        culture.DateTimeFormat.Calendar = Calendar;
        return culture.DateTimeFormat;
    }
}
