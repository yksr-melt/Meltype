// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text.RegularExpressions;

namespace Meltype.Composition;

/// <summary>
/// 打った日付・時刻の文節に、/ と : の形を候補として出す (#294)。いつもの候補 (10月9日) の後ろに足す。
/// 10がつ9にち → 10/9 / 10/09 / 10月9日 / 10/9(金) … (#935)、3じ15ふん → 3:15 / 03:15。
/// ありえない日付・時刻 (13がつ、2がつ30にち、25じ) なら候補なし。日付・時刻でなければ null。
/// </summary>
internal sealed class TypedDateCandidateProvider(CompositionOptions options) : IUtilityCandidateProvider
{
    /// <summary>打った日付 (10がつ9にち) と時刻 (3じ15ふん) の読み。数字は全角でもよい。</summary>
    private static readonly Regex DateReading = new("^(?<m>[0-9０-９]{1,2})がつ(?<d>[0-9０-９]{1,2})にち$");
    private static readonly Regex ClockReading = new("^(?<h>[0-9０-９]{1,2})じ(?<m>[0-9０-９]{1,2})(?:ふん|ぷん)$");
    private static readonly Regex SlashReading = new("^(?<m>[0-9]{1,2})/(?<d>[0-9]{1,2})$");

    public IEnumerable<string>? Candidates(string reading)
    {
        static int Number(string digits) => int.Parse(new string(digits.Select(c => c is >= '０' and <= '９' ? (char)(c - '０' + '0') : c).ToArray()), CultureInfo.InvariantCulture);
        if (DateReading.Match(reading) is { Success: true } date)
        {
            return DateCandidates(Number(date.Groups["m"].Value), Number(date.Groups["d"].Value)) ?? [];
        }
        if (ClockReading.Match(reading) is { Success: true } clock)
        {
            var (hour, minute) = (Number(clock.Groups["h"].Value), Number(clock.Groups["m"].Value));
            if (hour > 23 || minute > 59) return [];
            return [$"{hour}:{minute:D2}", $"{hour:D2}:{minute:D2}"];
        }
        return null;
    }

    /// <summary>打った日付・時刻 (10がつ20にち・3じ15ふん・10/20) の文節は、その読みから作る日付・時刻の候補を選んだか (書き方の形は見ない)。</summary>
    public bool IsVolatile(string reading, string text) => (SlashDate(reading) ?? Candidates(reading))?.Contains(text) == true;

    /// <summary>月/日 (10/20) の日付の候補。月/日でなければ null。</summary>
    public string[]? SlashDate(string raw) => SlashReading.Match(raw) is { Success: true } date
        ? DateCandidates(int.Parse(date.Groups["m"].Value, CultureInfo.InvariantCulture), int.Parse(date.Groups["d"].Value, CultureInfo.InvariantCulture))
        : null;

    /// <summary>
    /// 月と日の書き方の候補。10がつ20にち と 10/20 (変換) で同じものを出す。
    /// 10/20 / 10/20 / 10月20日 / 10/20(火) / 10月20日(火) / 2026/10/20 / 2026/10/20(火) / 2026年10月20日。ありえない日付なら null。
    /// 年は先の日付を優先する (予定を打つことが多いので)。今年のその日が今日以降なら今年、過ぎていれば来年。
    /// ただし過ぎてから 30 日以内ならその日 (少し前のことを書くとき。6 月 15 日に打つ 6/1 は今年、1/5 は来年、1 月 5 日に打つ 12/28 は去年)。
    /// </summary>
    private string[]? DateCandidates(int month, int day)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31) return null;
        var today = options.Now().Date;
        DateTime? best = null;
        for (var year = today.Year - 1; year <= today.Year + 4; year++) // 去年から (1 月に打つ 12/28)。2/29 は次のうるう年まで
        {
            if (day > DateTime.DaysInMonth(year, month)) continue;
            var date = new DateTime(year, month, day);
            if (date >= today.AddDays(-30)) { best = date; break; }
        }
        if (best is not { } chosen) return null;
        var week = ClockTime.WeekDays[(int)chosen.DayOfWeek];
        var y = chosen.Year;
        return [$"{month}/{day}", $"{month:D2}/{day:D2}", $"{month}月{day}日", $"{month}/{day}({week})", $"{month}月{day}日({week})",
            $"{y:D4}/{month:D2}/{day:D2}", $"{y:D4}/{month:D2}/{day:D2}({week})", $"{y}年{month}月{day}日"];
    }

    /// <summary>
    /// 変換エンジンが打った日付・時刻を 2 つ以上の文節に分けたら (10がつ|9にち)、1 つの文節にまとめる (/ の形の候補を出せるように)。
    /// </summary>
    public static IReadOnlyList<ConversionClause> JoinClauses(IReadOnlyList<ConversionClause> parts)
    {
        if (parts.Count < 2 || !parts.Any(p => p.Reading.Contains("がつ") || p.Reading.Contains('じ'))) return parts;
        var joined = new List<ConversionClause>();
        for (var i = 0; i < parts.Count; i++)
        {
            var end = -1;
            for (var j = Math.Min(parts.Count, i + 4); j > i + 1 && end < 0; j--)
                if (IsDateOrTime(string.Concat(parts.Skip(i).Take(j - i).Select(p => p.Reading)))) end = j;
            if (end < 0)
            {
                joined.Add(parts[i]);
                continue;
            }
            var run = parts.Skip(i).Take(end - i).ToList();
            joined.Add(new ConversionClause(string.Concat(run.Select(p => p.Reading)), string.Concat(run.Select(p => p.Text))));
            i = end - 1;
        }
        return joined;
    }

    private static bool IsDateOrTime(string reading) => DateReading.IsMatch(reading) || ClockReading.IsMatch(reading);
}
