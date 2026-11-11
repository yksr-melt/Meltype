// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>ばーじょん に、実行中の Meltype の版 (Meltype-1.5.0) を出す (issue #959)。</summary>
internal sealed class VersionConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading == "ばーじょん" ? new([$"Meltype-{Config.ProjectInfo.CoreVersion}"], UtilityPlacement.Append) : null;
}
