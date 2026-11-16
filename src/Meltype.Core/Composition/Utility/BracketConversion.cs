// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>
/// 片括弧と空の括弧ペアだけの入力に、括弧の候補だけを出す (issue #870。<see cref="BracketCandidates"/>)。
/// 第一候補は入力方式と記号設定を反映した形を優先する。通常の [ ] は設定に従い「」または［］になる。
/// 波括弧・不等号形には専用の幅設定がないため、自動全角化だけを戻し、入力した幅を第一候補にする。
/// かな入力の Raw は物理キーの表記なので、同じ幅の戻し方は適用しない。
/// </summary>
internal sealed class BracketConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        var text = input.Text;
        if (input.Direct || text.Mode is DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric ||
            text.Pending.Length > 0 || text.Units.Count is not (1 or 2) || text.ProtectedSpans().Count > 0) return null;
        var bracket = string.Concat(text.Units.Select(u => !text.KanaInput && u.Raw is "{" or "}" or "<" or ">" ? u.Raw : u.Kana));
        return BracketCandidates.For(bracket) is { } brackets
            ? new(brackets.Candidates, UtilityPlacement.Replace) { Reading = bracket, Notes = brackets.Notes }
            : null;
    }
}
