// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;

namespace Meltype.Composition.Utility;

/// <summary>
/// ユーティリティ変換 (きょう → 2026年11月10日、1+1 → 2 など)。変換を始めたとき、入力の全体が一致したときだけ候補を出す。
/// 足すときは、このファイルの隣に 1 つ実装して <see cref="UtilityConversions.All"/> に入れる。
/// </summary>
internal interface IUtilityConversion
{
    /// <summary>入力の全体が一致しなければ null。</summary>
    UtilityCandidates? Convert(UtilityInput input);
}

/// <summary>
/// 変換を始めたときの入力。Reading は入力の全体が日本語の区間 1 つのときの読み (それ以外は null)。
/// 打った英字は Text.Raw (かな入力では物理キーの表記)。
/// </summary>
internal sealed record UtilityInput(CompositionText Text, string? Reading, bool Direct, UtilityContext Context);

/// <summary>変換を始めたときの設定と、候補に使う外の値。</summary>
internal sealed record UtilityContext(
    Settings Settings,
    DateTime Now,
    DateTime UtcNow,
    TimeZoneInfo LocalTimeZone,
    Func<string?> Clipboard,
    Func<(string Title, string Artist)?> NowPlaying,
    GitHubIssueTitles? GitHubTitles)
{
    /// <summary>設定「時計の候補のタイムゾーン」の今の時刻。設定が空ならこの PC の時刻。</summary>
    public DateTime ClockNow => ClockConversion.Zone(Settings.ClockTimeZone) is { } zone
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc), zone)
        : Now;
}

internal enum UtilityPlacement
{
    /// <summary>普通の変換の候補の後ろに足す (きょう → 今日・京 … 2026年11月10日)。</summary>
    Append,

    /// <summary>ユーティリティの候補だけを出す。変換エンジンの候補を足さず、文節の区切りも動かさない (1+1 → 1+1・2・1+1=2)。</summary>
    Replace,
}

/// <summary>
/// ユーティリティ変換の候補。
/// Reading は Replace の文節の読み (null なら打った英字の英語の文節)。Notes は Replace の候補と同じ順・件数の注記。
/// </summary>
internal sealed record UtilityCandidates(IReadOnlyList<string> Candidates, UtilityPlacement Placement)
{
    private readonly IReadOnlyList<string>? _notLearned;

    public string? Reading { get; init; }

    public IReadOnlyList<CandidateNote>? Notes { get; init; }

    /// <summary>選んだら学習してよいか。時刻・設定・外の値で変わらない候補 (漢数字) だけ true にする。</summary>
    public bool Learnable { get; init; }

    /// <summary>選んでも学習しない候補。</summary>
    public IReadOnlyList<string> NotLearned
    {
        get => _notLearned ?? (Learnable ? [] : Candidates);
        init => _notLearned = value;
    }
}

internal static class UtilityConversions
{
    /// <summary>ユーティリティ変換の一覧。いくつかが一致したら、この順に候補を並べる。</summary>
    internal static readonly IUtilityConversion[] All =
    [
        new BracketConversion(),
        new CalculatorConversion(),
        new SlashDateConversion(),
        new GitHubIssueConversion(),
        new TypedDateConversion(),
        new KanjiNumberConversion(),
        new TodayConversion(),
        new NowConversion(),
        new ClockConversion(),
        new SeasonalGreetingConversion(),
        new TraditionalUnitConversion(),
        new CurrencyConversion(),
        new ClipboardConversion(),
        new VersionConversion(),
    ];

    /// <summary>一致したすべての変換の候補。1 つでも Replace なら Replace にする。どれも一致しなければ null。</summary>
    internal static UtilityCandidates? Convert(UtilityInput input)
    {
        var matches = All.Select(c => c.Convert(input)).OfType<UtilityCandidates>().Where(m => m.Candidates.Count > 0).ToList();
        if (matches.Count == 0) return null;
        if (matches is [var only]) return only;
        var replace = matches.FirstOrDefault(m => m.Placement == UtilityPlacement.Replace);
        return new UtilityCandidates(matches.SelectMany(m => m.Candidates).Distinct(StringComparer.Ordinal).ToList(), replace?.Placement ?? UtilityPlacement.Append)
        {
            Reading = replace?.Reading,
            NotLearned = matches.SelectMany(m => m.NotLearned).ToList(),
        };
    }

    internal static readonly string[] WeekDays = ["日", "月", "火", "水", "木", "金", "土"];

    /// <summary>全角の数字を半角にして数にする。</summary>
    internal static int Number(string digits) =>
        int.Parse(new string(digits.Select(c => c is >= '０' and <= '９' ? (char)(c - '０' + '0') : c).ToArray()), System.Globalization.CultureInfo.InvariantCulture);
}
