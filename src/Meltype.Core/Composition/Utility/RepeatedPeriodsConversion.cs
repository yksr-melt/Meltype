// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

using Meltype.Notation;

namespace Meltype.Composition.Utility;

/// <summary>
/// ピリオドが続くときはその数だけ設定の句点を候補に出す。少数のピリオドは通常の句読点として扱う。
/// 候補は確定するときに句読点の設定を当てないので、ここで設定の句点にする。
/// </summary>
internal sealed class RepeatedPeriodsConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading is { Length: >= 3 } reading && reading.All(c => c == '.')
            ? new([new string(Punctuation.Marks(input.Context.Settings.Punctuation).Period, reading.Length)], UtilityPlacement.Append) : null;
}
