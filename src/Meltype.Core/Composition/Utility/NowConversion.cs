// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>
/// いま・なう に今の日時を出す (issue #208)。17:22 / 17時22分 / 午後5時22分 / 2026年10月9日(金) 17時22分 / 10月9日(金) 17:22 …
/// 時刻は設定「時計の候補のタイムゾーン」のもの。なう には、今再生している曲を時刻より先に出す (issue #1400)。
/// </summary>
internal sealed class NowConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.Reading is not ("いま" or "なう")) return null;
        var times = Times(input.Context.ClockNow);
        if (input.Reading == "なう" && input.Context.Settings.NowPlayingCandidates && Songs(input.Context.NowPlaying()) is { Count: > 0 } songs)
            return new([.. songs, .. times], UtilityPlacement.Append);
        return new(times, UtilityPlacement.Append);
    }

    private static string[] Times(DateTime now)
    {
        var day = UtilityConversions.WeekDays[(int)now.DayOfWeek];
        var date = $"{now.Year}年{now.Month}月{now.Day}日";
        var monthDay = $"{now.Month}月{now.Day}日";
        var slashDate = $"{now.Year:D4}/{now.Month:D2}/{now.Day:D2}";
        var clock = $"{now.Hour:D2}:{now.Minute:D2}";
        var time = $"{now.Hour}時{now.Minute}分";
        return [clock, time, $"{(now.Hour < 12 ? "午前" : "午後")}{now.Hour % 12}時{now.Minute}分",
            $"{date}({day}) {time}", $"{monthDay}({day}) {time}", $"{date}({day}) {clock}", $"{slashDate} {clock}"];
    }

    /// <summary>今再生している曲の候補 (曲名 / アーティスト、曲名)。読めなければ空。</summary>
    private static List<string> Songs((string Title, string Artist)? playing)
    {
        if (playing is not { } song || string.IsNullOrWhiteSpace(song.Title)) return [];
        var title = song.Title.Trim();
        var artist = song.Artist?.Trim() ?? "";
        return artist.Length > 0 ? [$"{title} / {artist}", title, $"#NowPlaying {title} / {artist}"] : [title, $"#NowPlaying {title}"];
    }
}
