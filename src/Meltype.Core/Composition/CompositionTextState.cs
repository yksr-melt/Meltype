// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;
using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>状態 (データだけ)。処理は <see cref="CompositionText"/> の関数が受け取って書き換える。</summary>
internal sealed class CompositionTextState
{
    public readonly List<CompositionUnit> Units = [];

    public readonly StringBuilder Pending = new();

    public DisplayMode Mode = DisplayMode.Auto;

    /// <summary>英字 (F9 / F10) で見せるときの大文字・小文字。確定・取り消しで戻る。</summary>
    public LetterCase Case = LetterCase.AsTyped;

    /// <summary>この入力だけ自動判定を使う (自動判定を使わないときに Tab で提案を受け入れた)。確定・取り消しで戻る。</summary>
    public bool SuggestionAccepted;

    /// <summary>かな入力 (JIS) か。かな入力では <see cref="AppendKana"/> で 1 キー 1 文字ずつ入れる。</summary>
    public bool KanaInput;

    /// <summary>
    /// この入力の打ち始めの / を中黒 (・) にするか (文章の入力欄で打ち始めた: 本体が入力を始めるときに決める)。
    /// 入力の間は変えないので、BackSpace で原文を打ち直しても同じ結果になる。入力が空になったら false に戻す。
    /// </summary>
    public bool SlashAtStart;

    public (string Key, LanguageMemory? Memory, IReadOnlyList<CompositionSegment> Segments, List<Func<string>> Reasons)? SegmentsCache;

    // ---- 保護範囲 (メンション・URL・メール・パス) ----
    public string? ProtectedCacheRaw;

    public IReadOnlyList<ProtectedSpan>? ProtectedCache;

    /// <summary>( ) を全角にするか (設定「カッコを全角で入力」)。</summary>
    public bool FullWidthParentheses;

    /// <summary>[ ] を「」ではなく［］にするか (設定)。</summary>
    public bool SquareBrackets;

    /// <summary>1 つのかなの音の後ろの w を笑いにするか (設定)。</summary>
    public bool LaughterAfterKana;
}
