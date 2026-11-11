// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>
/// とけい・じこく に、設定のタイムゾーンの今の時刻 (19:21 / 19時21分 / 午後7時21分) を出す (issue #1401)。
/// 夏時間は TimeZoneInfo が決める。この PC と違うタイムゾーンなら、日付とタイムゾーンの名前を付けたものも出す (日付をまたぐことがあるので)。
/// </summary>
internal sealed class ClockConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.Reading is not ("とけい" or "じこく")) return null;
        var setting = input.Context.Settings.ClockTimeZone;
        var now = input.Context.ClockNow;
        var clock = $"{now.Hour:D2}:{now.Minute:D2}";
        List<string> result = [clock, $"{now.Hour}時{now.Minute}分", $"{(now.Hour < 12 ? "午前" : "午後")}{now.Hour % 12}時{now.Minute}分"];
        if (Zone(setting) is { } zone && !zone.HasSameRules(input.Context.LocalTimeZone))
            result.Add($"{now.Month}/{now.Day}({UtilityConversions.WeekDays[(int)now.DayOfWeek]}) {clock} ({setting.Trim()})");
        return new(result, UtilityPlacement.Append);
    }

    /// <summary>
    /// 設定のタイムゾーン。空・見つからなければ null (この PC のもの)。
    /// IANA (Asia/Tokyo) と Windows (Tokyo Standard Time) のどちらの ID も読む (見つからない ID は .NET が IANA ⇔ Windows に変換して探し直す)。
    /// </summary>
    internal static TimeZoneInfo? Zone(string id)
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
}
