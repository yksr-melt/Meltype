// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>
/// 関数記法 (名前(引数)) のユーティリティ変換 (hex(dadada) → #dadada、uuid() → 新しい UUID)。
/// 打った英字の全体が関数記法のとき、英語と判定した入力でも Space で変換を始め、<see cref="Predictable"/> なら Tab の予測候補にも出す。
/// 足すときは、このファイルの隣に 1 つ実装して <see cref="UtilityConversions.Functions"/> に入れる。
/// </summary>
internal interface IFunctionConversion
{
    /// <summary>name は小文字、args は前後の空白を除いたもの。この変換の名前でないか、引数を解釈できなければ空。</summary>
    IReadOnlyList<FunctionCandidate> Call(string name, string args, DateTime now);

    /// <summary>予測の候補は打った内容が変わるたびに作り直すので、同じ呼び出しに同じ候補を返す変換だけ true にする。</summary>
    bool Predictable { get; }
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
        if (UtilityConversions.Call(raw, input.Context.Now) is not { } call) return null;
        var candidates = call.Candidates.ToList();
        if (!candidates.Any(c => c.Text == raw)) candidates.Add(new(raw));
        return new(candidates.Select(c => c.Text).ToList(), UtilityPlacement.Replace)
        {
            Colors = candidates.Any(c => c.Color is not null) ? candidates.Select(c => c.Color).ToList() : null,
        };
    }
}
