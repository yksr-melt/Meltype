// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

using Meltype.Dictionary;

namespace Meltype.Composition;

/// <summary>
/// 片括弧と空の括弧ペアの候補を、Meltype の定義だけから生成する。
/// OS・外部変換エンジンの候補は混ぜず、入力した系統だけを、入力形とその幅違いから順に出す。
/// 片括弧の閉じ括弧は、同じ行のキャレットより前に同じ系統の未閉じ開き括弧があれば、その閉じ括弧を第一候補にする。
/// Notes は Windows の幅の表示用。Mac・Linux の候補表示には新しい注記処理を追加しない。
/// </summary>
internal sealed record BracketCandidates(List<string> Candidates, List<CandidateNote> Notes)
{
    private enum Group { Round, Square, Curly, Angle }

    // 幅違いの対応形がある記号だけに注記を付ける。None は「半角」の意味ではなく、注記を表示しない指定。
    private sealed record Entry(string Pair, Group Group, CandidateNote Note = CandidateNote.None, string? OtherWidth = null);

    private static readonly char[] LineBreaks = ['\r', '\n'];

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

    internal static BracketCandidates? For(string input, string? precedingText = null)
    {
        // 本文・空白・不一致の組・複数組は対象外。文章中の括弧の変換には介入しない。
        if (input.Length is not (1 or 2)) return null;
        var first = Entries.FirstOrDefault(e => input.Length == 2 ? e.Pair == input : e.Pair.Contains(input[0]));
        if (first is null) return null;
        var side = input.Length == 2 ? -1 : first.Pair[0] == input[0] ? 0 : 1;
        var preferred = side == 1 ? UnclosedOpener(precedingText, first.Group) ?? first : first;
        var ordered = Entries.Where(e => e.Group == first.Group)
            .OrderBy(e => e == preferred ? 0 : e.Pair == preferred.OtherWidth ? 1 : 2).ToList();
        return new BracketCandidates(
            ordered.Select(e => side < 0 ? e.Pair : e.Pair[side].ToString()).ToList(),
            ordered.Select(e => e.Note).ToList());
    }

    // 同じ行の閉じられていない括弧を探す。
    private static Entry? UnclosedOpener(string? precedingText, Group group)
    {
        if (string.IsNullOrEmpty(precedingText)) return null;
        var start = precedingText.LastIndexOfAny(LineBreaks) + 1;
        var open = new List<Entry>();
        for (var i = start; i < precedingText.Length; i++)
        {
            var c = precedingText[i];
            var entry = Entries.FirstOrDefault(e => e.Group == group && (e.Pair[0] == c || e.Pair[1] == c));
            if (entry is null) continue;
            if (entry.Pair[0] == c)
            {
                open.Add(entry);
            }
            else if (open.LastIndexOf(entry) is >= 0 and var matched)
            {
                // 外側の括弧を閉じたときは内側で閉じられなかった括弧も終わらせる。
                open.RemoveRange(matched, open.Count - matched);
            }
        }
        return open.Count > 0 ? open[^1] : null;
    }
}
