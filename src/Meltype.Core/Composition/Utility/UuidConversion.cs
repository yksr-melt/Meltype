// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

namespace Meltype.Composition.Utility;

/// <summary>
/// UUID v4 / v7 生成ユーティリティ。
/// - `uuid` / `guid` / `ゆーゆーあいでぃー`: 通常候補の後ろに新しい UUID 候補を足す (Append)
/// - `uuid()` / `guid()` / `uuid(v4)` / `uuid(v7)` / `uuid(n)`: UUID 候補だけを先頭に出す (Replace)
/// ランダム生成のため学習には登録しない。
/// </summary>
internal sealed class UuidConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        var raw = CompositionText.Raw(input.Text);
        if (!input.Text.KanaInput && TryGenerate(raw, input.Context.Now) is { Count: > 0 } funcCandidates)
            return new(funcCandidates, UtilityPlacement.Replace) { Learnable = false };

        if (input.Reading is "ゆーゆーあいでぃー" ||
            (!input.Text.KanaInput && raw.ToLowerInvariant() is "uuid" or "guid"))
        {
            return new(GenerateAll(input.Context.Now, mode: ""), UtilityPlacement.Append) { Learnable = false };
        }
        return null;
    }

    /// <summary>関数記法に一致するか (Space での変換開始判定用)。</summary>
    internal static bool Matches(string raw) =>
        ParseFunction(raw) is not null;

    private static List<string>? TryGenerate(string raw, DateTime now) =>
        ParseFunction(raw) is { } mode ? GenerateAll(now, mode) : null;

    private static string? ParseFunction(string raw)
    {
        var trimmed = raw.Trim();
        var open = trimmed.IndexOf('(');
        if (open <= 0 || !trimmed.EndsWith(')')) return null;
        var name = trimmed[..open].ToLowerInvariant();
        if (name is not ("uuid" or "guid")) return null;
        var arg = ToHalfWidthAscii(trimmed[(open + 1)..^1]).Trim().ToLowerInvariant();
        return arg is "" or "v4" or "v7" or "n" ? arg : null;
    }

    private static List<string> GenerateAll(DateTime now, string mode)
    {
        var v4 = Guid.NewGuid().ToString("D");
        var v7 = Guid.CreateVersion7(ToDateTimeOffset(now)).ToString("D");
        var upper = v4.ToUpperInvariant();
        var compact = v4.Replace("-", "", StringComparison.Ordinal);

        return mode switch
        {
            "v7" => [v7, v7.ToUpperInvariant(), v7.Replace("-", "", StringComparison.Ordinal)],
            "v4" => [v4, upper, compact],
            "n" => [compact, v7.Replace("-", "", StringComparison.Ordinal), v4, upper],
            _ => [v4, v7, upper, compact],
        };
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime now) => now.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(now, TimeSpan.Zero),
        DateTimeKind.Local => new DateTimeOffset(now),
        _ => new DateTimeOffset(now, TimeZoneInfo.Local.GetUtcOffset(now)),
    };

    private static string ToHalfWidthAscii(string s) =>
        new(s.Select(c => c is >= '！' and <= '～' ? (char)(c - 0xFEE0) : c == '　' ? ' ' : c).ToArray());
}
