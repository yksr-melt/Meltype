// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text.RegularExpressions;

namespace Meltype.Composition.Utility;

/// <summary>
/// 月/日 (10/20) に日付の書き方の候補を出す (issue #935)。分数・割り算 (1/2) かもしれないので、打ったまま・計算の結果より後ろに出し、
/// 全角 (１０／２０) を最後に出す。Space は今までどおり確定して空白 (SpaceCommitsAlphanumeric)。
/// </summary>
internal sealed partial class SlashDateConversion : IUtilityConversion
{
    [GeneratedRegex("^(?<m>[0-9]{1,2})/(?<d>[0-9]{1,2})$")]
    private static partial Regex SlashDate();

    public UtilityCandidates? Convert(UtilityInput input)
    {
        var raw = input.Text.Raw;
        if (input.Text.KanaInput || input.Text.Mode != DisplayMode.Auto || SlashDate().Match(raw) is not { Success: true } date) return null;
        var dates = TypedDateConversion.Dates(int.Parse(date.Groups["m"].Value, CultureInfo.InvariantCulture), int.Parse(date.Groups["d"].Value, CultureInfo.InvariantCulture), input.Context.Now);
        return dates is null ? null : new([raw, .. dates, CompositionText.ToFullWidth(raw)], UtilityPlacement.Replace);
    }
}
