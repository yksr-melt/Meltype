// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

namespace Meltype.Composition;

/// <summary>
/// 片括弧と空の括弧ペアの候補を、Meltype の定義だけから生成する。
/// OS・外部変換エンジンの候補は混ぜず、入力した系統だけを、入力形とその幅違いから順に出す。
/// Notes は Windows の幅の表示用。Mac・Linux の候補表示には新しい注記処理を追加しない。
/// </summary>
internal sealed record BracketCandidates(List<string> Candidates, List<CandidateNote> Notes)
{
    private enum Group { Round, Square, Curly, Angle }

    // 幅違いの対応形がある記号だけに注記を付ける。None は「半角」の意味ではなく、注記を表示しない指定。
    private sealed record Entry(string Pair, Group Group, CandidateNote Note = CandidateNote.None, string? OtherWidth = null);

    // 第一候補とその幅違いを取り出した後の基準順。左右・ペアを別々に管理せず、この定義を共用する。
    private static readonly Entry[] Entries =
    [
        new("「」", Group.Square, CandidateNote.FullWidth, "｢｣"),
        new("『』", Group.Square),
        new("（）", Group.Round, CandidateNote.FullWidth, "()"),
        new("()", Group.Round, CandidateNote.HalfWidth, "（）"),
        new("［］", Group.Square, CandidateNote.FullWidth, "[]"),
        new("[]", Group.Square, CandidateNote.HalfWidth, "［］"),
        new("｛｝", Group.Curly, CandidateNote.FullWidth, "{}"),
        new("{}", Group.Curly, CandidateNote.HalfWidth, "｛｝"),
        new("【】", Group.Square),
        new("〈〉", Group.Angle),
        new("《》", Group.Angle),
        new("〔〕", Group.Square),
        new("｢｣", Group.Square, CandidateNote.HalfWidth, "「」"),
        new("＜＞", Group.Angle, CandidateNote.FullWidth, "<>"),
        new("<>", Group.Angle, CandidateNote.HalfWidth, "＜＞"),
    ];

    internal static BracketCandidates? For(string input)
    {
        // 本文・空白・不一致の組・複数組は対象外。文章中の括弧の変換には介入しない。
        if (input.Length is not (1 or 2)) return null;
        var first = Entries.FirstOrDefault(e => input.Length == 2 ? e.Pair == input : e.Pair.Contains(input[0]));
        if (first is null) return null;
        var side = input.Length == 2 ? -1 : first.Pair[0] == input[0] ? 0 : 1;
        var ordered = Entries.Where(e => e.Group == first.Group)
            .OrderBy(e => e == first ? 0 : e.Pair == first.OtherWidth ? 1 : 2).ToList();
        return new BracketCandidates(
            ordered.Select(e => side < 0 ? e.Pair : e.Pair[side].ToString()).ToList(),
            ordered.Select(e => e.Note).ToList());
    }
}
