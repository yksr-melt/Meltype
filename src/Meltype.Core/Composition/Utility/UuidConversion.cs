// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

namespace Meltype.Composition.Utility;

/// <summary>
/// UUID v4 / v7 生成ユーティリティ。
/// - `uuid` / `guid` / `ゆーゆーあいでぃー`: 通常候補の後ろに新しい UUID 候補を足す (Append)
/// - `uuid()` / `guid()` / `uuid(v4)` / `uuid(v7)` / `uuid(n)`: UUID 候補だけを先頭に出す (関数記法)
/// ランダム生成のため学習には登録しない。
/// </summary>
internal sealed class UuidConversion : IUtilityConversion, IFunctionConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.Reading is "ゆーゆーあいでぃー" ||
            (!input.Text.KanaInput && CompositionText.Raw(input.Text).ToLowerInvariant() is "uuid" or "guid"))
        {
            return new(GenerateAll(input.Context.Now, mode: ""), UtilityPlacement.Append) { Learnable = false };
        }
        return null;
    }

    public Func<DateTime, IReadOnlyList<FunctionCandidate>>? Parse(string name, string args)
    {
        if (name is not ("uuid" or "guid")) return null;
        var mode = ToHalfWidthAscii(args).Trim().ToLowerInvariant();
        return mode is "" or "v4" or "v7" or "n" ? now => GenerateAll(now, mode).Select(c => new FunctionCandidate(c)).ToList() : null;
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
