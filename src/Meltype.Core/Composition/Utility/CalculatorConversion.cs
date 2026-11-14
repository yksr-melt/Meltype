// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>設定「計算式を変換すると結果を候補に出す」: 数字と + - * / ( ) だけの式に、結果と「式=結果」を出す (issue #295)。</summary>
internal sealed class CalculatorConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        var raw = input.Text.Raw;
        if (!input.Context.Settings.CalculatorCandidates || input.Text.KanaInput || input.Text.Mode != DisplayMode.Auto || Calculator.Evaluate(raw) is not { } result) return null;
        return new([raw, result, $"{raw.TrimEnd('=')}={result}"], UtilityPlacement.Replace);
    }
}
