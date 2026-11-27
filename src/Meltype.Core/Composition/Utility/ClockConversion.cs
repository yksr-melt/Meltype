// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>とけい・じこく に、この PC の今の時刻 (19:21 / 19時21分 / 午後7時21分) を出す。</summary>
internal sealed class ClockConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.Reading is not ("とけい" or "じこく")) return null;
        var now = input.Context.Now;
        return UtilityCandidates.Rankable("clock", [$"{now.Hour:D2}:{now.Minute:D2}", $"{now.Hour}時{now.Minute}分", $"{(now.Hour < 12 ? "午前" : "午後")}{now.Hour % 12}時{now.Minute}分"]);
    }
}
