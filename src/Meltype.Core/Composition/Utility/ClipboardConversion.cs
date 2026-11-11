// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition.Utility;

/// <summary>設定「クリップボードの文字を変換候補に出す」: くりっぷぼーど・ぺーすと に、クリップボードの文字 (1 行) を出す。</summary>
internal sealed class ClipboardConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading is "くりっぷぼーど" or "ぺーすと" && input.Context.Settings.ClipboardCandidates && Line(input.Context.Clipboard()) is { } line
            ? new([line], UtilityPlacement.Append)
            : null;

    /// <summary>クリップボードの文字が、候補に出してよい 1 行 (200 文字まで・制御文字なし) なら、その文字。前後の空白は除く。</summary>
    internal static string? Line(string? text)
    {
        var line = text?.Trim();
        return line is { Length: > 0 and <= 200 } && !line.Any(char.IsControl) ? line : null;
    }
}
