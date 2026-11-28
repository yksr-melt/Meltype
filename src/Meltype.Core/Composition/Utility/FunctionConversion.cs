// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>
/// 関数記法 (名前(引数)) のユーティリティ変換 (hex(dadada) → #dadada、uuid() → 新しい UUID)。
/// 打った英字の全体が関数記法のとき、英語と判定した入力でも Space・Tab で変換を始める。
/// 足すときは、このファイルの隣に 1 つ実装して <see cref="UtilityConversions.Functions"/> に入れる。
/// </summary>
internal interface IFunctionConversion
{
    /// <summary>
    /// name は小文字、args は前後の空白を除いたもの。この変換の名前で引数を解釈できれば、変換を始めた時刻から空でない候補を作る関数。そうでなければ null。
    /// Space・Tab で変換を始めるかは解釈だけで決めるので、候補 (uuid() の UUID など) は返した関数の中で作る。
    /// </summary>
    Func<DateTime, IReadOnlyList<FunctionCandidate>>? Parse(string name, string args);
}

/// <summary>関数記法の変換の候補。Color は色見本 (#rrggbbaa)。</summary>
internal sealed record FunctionCandidate(string Text, string? Color = null);

/// <summary><see cref="UtilityConversions.Functions"/> を、変換を始めたときの候補にする。打った関数記法そのものも最後の候補に残す。</summary>
internal sealed class FunctionConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.Text.KanaInput || input.Text.Mode != DisplayMode.Auto) return null;
        var raw = CompositionText.Raw(input.Text);
        if (UtilityConversions.Parse(raw) is not { } candidatesAt) return null;
        var candidates = candidatesAt(input.Context.Now).ToList();
        if (!candidates.Any(c => c.Text == raw)) candidates.Add(new(raw));
        return new(candidates.Select(c => c.Text).ToList(), UtilityPlacement.Replace)
        {
            Colors = candidates.Any(c => c.Color is not null) ? candidates.Select(c => c.Color).ToList() : null,
        };
    }
}
