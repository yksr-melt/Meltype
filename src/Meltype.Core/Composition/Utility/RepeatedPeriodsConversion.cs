// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

namespace Meltype.Composition.Utility;

/// <summary>ピリオドが続くときはその数だけ句点を候補に出す。少数のピリオドは通常の句読点として扱う。</summary>
internal sealed class RepeatedPeriodsConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading is { Length: >= 3 } reading && reading.All(c => c == '.')
            ? new([new string('。', reading.Length)], UtilityPlacement.Append) : null;
}
