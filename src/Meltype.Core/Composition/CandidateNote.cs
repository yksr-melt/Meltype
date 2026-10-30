// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

namespace Meltype.Composition;

/// <summary>候補に付ける幅の注記の種類。</summary>
internal enum CandidateNote
{
    None,
    HalfWidth,
    FullWidth,
}

internal static class CandidateNoteExtensions
{
    /// <summary>注記を表示用の文字列へ変換する。None は null を返す。</summary>
    internal static string? ToDisplayText(this CandidateNote note) => note switch
    {
        CandidateNote.None => null,
        CandidateNote.HalfWidth => "半角",
        CandidateNote.FullWidth => "全角",
        _ => throw new ArgumentOutOfRangeException(nameof(note), note, "候補の注記が不正です。"),
    };
}
