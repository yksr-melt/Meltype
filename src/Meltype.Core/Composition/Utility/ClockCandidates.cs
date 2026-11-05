// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition;

/// <summary>時計の候補で共有する、曜日とタイムゾーン。</summary>
internal static class ClockTime
{
    public static readonly string[] WeekDays = ["日", "月", "火", "水", "木", "金", "土"];

    /// <summary>
    /// 設定のタイムゾーン。空・見つからなければ null (この PC のもの)。
    /// IANA (Asia/Tokyo) と Windows (Tokyo Standard Time) のどちらの ID も読む (見つからない ID は .NET が IANA ⇔ Windows に変換して探し直す)。
    /// </summary>
    public static TimeZoneInfo? Zone(string id)
    {
        id = id?.Trim() ?? "";
        if (id.Length == 0) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>設定「時計の候補のタイムゾーン」の今の時刻 (とけい・じこく・いま・なう)。設定が空ならこの PC の時刻。</summary>
    public static DateTime Now(CompositionOptions options) => Zone(options.ClockTimeZone()) is { } zone
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(options.UtcNow(), DateTimeKind.Utc), zone)
        : options.Now();

    /// <summary>日時の候補を選んだか (数字か曜日を含む。読みのままや漢字の候補は学習してよい)。</summary>
    public static bool IsTimeText(string text) => text.Any(char.IsAsciiDigit) || text.EndsWith("曜日", StringComparison.Ordinal);
}

/// <summary>
/// とけい・じこく の文節に、設定のタイムゾーンの今の時刻 (19:21 / 19時21分 / 午後7時21分) を出す (issue #1401)。
/// 夏時間は TimeZoneInfo が決める。この PC と違うタイムゾーンなら、日付とタイムゾーンの名前を付けたものも出す (日付をまたぐことがあるので)。
/// </summary>
internal sealed class ClockCandidateProvider(CompositionOptions options) : IUtilityCandidateProvider
{
    private static readonly HashSet<string> Readings = ["とけい", "じこく"];

    public IEnumerable<string>? Candidates(string reading)
    {
        if (!Readings.Contains(reading)) return null;
        var zone = ClockTime.Zone(options.ClockTimeZone());
        var now = ClockTime.Now(options);
        var clock = $"{now.Hour:D2}:{now.Minute:D2}";
        List<string> result = [clock, $"{now.Hour}時{now.Minute}分", $"{(now.Hour < 12 ? "午前" : "午後")}{now.Hour % 12}時{now.Minute}分"];
        if (zone is not null && !zone.HasSameRules(options.LocalTimeZone())) result.Add($"{now.Month}/{now.Day}({ClockTime.WeekDays[(int)now.DayOfWeek]}) {clock} ({options.ClockTimeZone().Trim()})");
        return result;
    }

    public bool IsVolatile(string reading, string text) => Readings.Contains(reading) && ClockTime.IsTimeText(text);
}

/// <summary>
/// いま・なう の文節に今の日時、きょう の文節に今日の日付を候補として出す (issue #208)。
/// いま → 17:22 / 17時22分 / 午後5時22分 / 2026年10月9日(金) 17時22分 / 10月9日(金) 17:22 …
/// きょう → 2026年10月9日 / 2026年10月9日(金) / 10月9日(金) / 2026/10/09 / 2026/10/09(金) / 10/9(金) / 2026-10-09 / 金曜日 / 20261009 …
/// </summary>
internal sealed class NowCandidateProvider(CompositionOptions options, ICompositionHost host) : IUtilityCandidateProvider
{
    private static readonly HashSet<string> NowReadings = ["いま", "なう"];

    public IEnumerable<string>? Candidates(string reading)
    {
        var isNow = NowReadings.Contains(reading);
        if (!isNow && reading != "きょう") return null;
        // なう: 今再生している曲を、時刻より先に (issue #1400)
        if (reading == "なう" && options.NowPlayingCandidates() && NowPlaying() is { Count: > 0 } songs)
            return [.. songs, .. NowTimes(ClockTime.Now(options))];
        if (isNow) return NowTimes(ClockTime.Now(options));
        var now = options.Now();
        var day = ClockTime.WeekDays[(int)now.DayOfWeek];
        var date = $"{now.Year}年{now.Month}月{now.Day}日";
        var monthDay = $"{now.Month}月{now.Day}日";
        // 区切りの / と : は、Windows の地域の設定に左右されないように数字から組み立てる (DateTime の書式の / : は地域の区切りになる)
        var slashDate = $"{now.Year:D4}/{now.Month:D2}/{now.Day:D2}";
        return [date, $"{date}({day})", monthDay, $"{monthDay}({day})", slashDate, $"{slashDate}({day})", $"{now.Month}/{now.Day}({day})",
            $"{now.Year:D4}-{now.Month:D2}-{now.Day:D2}", $"{day}曜日", $"{now.Year:D4}{now.Month:D2}{now.Day:D2}"];
    }

    /// <summary>
    /// 「なう」は何を選んでも学習しない (再生中の曲は打つたびに変わる。曲名は何でもありうるので、出した曲を覚えておかずに読みだけで決める。issue #1400)。
    /// </summary>
    public bool IsVolatile(string reading, string text) =>
        reading == "なう" || (NowReadings.Contains(reading) || reading == "きょう") && ClockTime.IsTimeText(text);

    private static string[] NowTimes(DateTime now)
    {
        var day = ClockTime.WeekDays[(int)now.DayOfWeek];
        var date = $"{now.Year}年{now.Month}月{now.Day}日";
        var monthDay = $"{now.Month}月{now.Day}日";
        var slashDate = $"{now.Year:D4}/{now.Month:D2}/{now.Day:D2}";
        var clock = $"{now.Hour:D2}:{now.Minute:D2}";
        var time = $"{now.Hour}時{now.Minute}分";
        return [clock, time, $"{(now.Hour < 12 ? "午前" : "午後")}{now.Hour % 12}時{now.Minute}分",
            $"{date}({day}) {time}", $"{monthDay}({day}) {time}", $"{date}({day}) {clock}", $"{slashDate} {clock}"];
    }

    /// <summary>今再生している曲の候補 (曲名 / アーティスト、曲名)。読めなければ空。</summary>
    private List<string> NowPlaying()
    {
        if (host.NowPlaying is not { } song || string.IsNullOrWhiteSpace(song.Title)) return [];
        var title = song.Title.Trim();
        var artist = song.Artist?.Trim() ?? "";
        return artist.Length > 0 ? [$"{title} / {artist}", title, $"#NowPlaying {title} / {artist}"] : [title, $"#NowPlaying {title}"];
    }
}
