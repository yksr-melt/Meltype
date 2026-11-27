// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

using System.Globalization;

namespace Meltype.Composition.Utility;

/// <summary>ISO 8601 と UNIX 時刻を、入力された表記または現在時刻から候補にする。</summary>
internal sealed class TimestampConversion : IUtilityConversion
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] ZonedFormats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];
    private static readonly string[] CalendarFormats = ["yyyy-MM-dd", "yyyyMMdd", "yyyyMMddHHmmss", "yyyy-MM-dd HH:mm:ss"];

    public UtilityCandidates? Convert(UtilityInput input)
    {
        var now = input.Context.Now;
        var raw = CompositionText.Raw(input.Text);
        if (!input.Text.KanaInput && FromFunction(raw, now) is { } formatted)
            return new(formatted, UtilityPlacement.Replace);
        if (input.Reading is "たいむすたんぷ" or "えぽっく" || !input.Text.KanaInput && IsName(raw))
            return new(Format(new DateTimeOffset(now), unixFirst: false), UtilityPlacement.Append);
        if (input.Reading is "いま" or "なう")
            return new([now.ToString("yyyy-MM-dd'T'HH:mm:ss", Invariant)], UtilityPlacement.Append);
        return null;
    }

    internal static IReadOnlyList<string>? FromFunction(string raw, DateTime now)
    {
        var open = raw.IndexOf('(');
        if (open < 0 || !raw.EndsWith(')') || !IsName(raw[..open])) return null;
        var call = raw[..open];
        var input = raw[(open + 1)..^1].Trim();
        if (!TryTimestamp(input, now, out var timestamp, out var numeric)) return null;
        return Format(timestamp, !numeric && !call.Equals("iso", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsName(string raw) =>
        raw.Equals("iso", StringComparison.OrdinalIgnoreCase) ||
        raw.Equals("unix", StringComparison.OrdinalIgnoreCase) ||
        raw.Equals("epoch", StringComparison.OrdinalIgnoreCase);

    private static bool TryTimestamp(string input, DateTime now, out DateTimeOffset result, out bool numeric)
    {
        numeric = false;
        switch (input.ToLowerInvariant())
        {
            case "" or "now": result = new(now); return true;
            case "today": result = new(now.Date); return true;
            case "tomorrow" when now.Date < DateTime.MaxValue.Date: result = new(now.Date.AddDays(1)); return true;
            case "yesterday" when now.Date > DateTime.MinValue: result = new(now.Date.AddDays(-1)); return true;
        }
        if (DateTimeOffset.TryParseExact(input.ToUpperInvariant(), ZonedFormats, Invariant, DateTimeStyles.AssumeLocal, out result)) return true;
        if (DateTime.TryParseExact(input, CalendarFormats, Invariant, DateTimeStyles.None, out var date))
        {
            result = new(date);
            return true;
        }
        if (!long.TryParse(input, NumberStyles.AllowLeadingSign, Invariant, out var value)) return false;
        numeric = true;
        // 8 桁・14 桁の実在する日付は先に扱う。その他は桁数を見て秒・ミリ秒・マイクロ秒・ナノ秒を区別する。
        var digits = input.TrimStart('-', '+').Length;
        try
        {
            result = digits switch
            {
                <= 10 => DateTimeOffset.FromUnixTimeSeconds(value),
                <= 13 => DateTimeOffset.FromUnixTimeMilliseconds(value),
                <= 16 => DateTimeOffset.UnixEpoch.AddTicks(checked(value * 10)),
                _ => DateTimeOffset.UnixEpoch.AddTicks(value / 100),
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> Format(DateTimeOffset timestamp, bool unixFirst)
    {
        var local = timestamp.ToLocalTime();
        var seconds = timestamp.ToUnixTimeSeconds().ToString(Invariant);
        var milliseconds = timestamp.ToUnixTimeMilliseconds().ToString(Invariant);
        var localized = local.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", Invariant);
        var utc = timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", Invariant);
        var plain = local.ToString("yyyy-MM-dd HH:mm:ss", Invariant);
        return unixFirst ? [seconds, milliseconds, localized, utc, plain] : [localized, utc, seconds, milliseconds, plain];
    }
}
