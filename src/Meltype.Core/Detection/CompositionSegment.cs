// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Detection;

/// <summary>
/// 変換ボックス内の 1 単位。ローマ字 1 音 (きょ, っ, ん …)・ローマ字として読めなかった英字 1 文字・記号 1 文字のいずれか。
/// Raw は実際に打った文字 (英語として表示するときに使う)。
/// </summary>
internal readonly record struct CompositionUnit(string Kana, string Raw);

/// <summary>表示上のひとまとまり。英語と判定した区間は英字のまま、それ以外は日本語 (かな/漢字)。</summary>
internal readonly record struct CompositionSegment(bool IsEnglish, string Kana, string Raw)
{
    public bool IsProtected { get; init; }
}
