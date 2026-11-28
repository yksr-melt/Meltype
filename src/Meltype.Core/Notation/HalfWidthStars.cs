// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 Yukishiro

namespace Meltype.Notation;

/// <summary>
/// 表示の ＊ ＋ を打った半角にする。* が 2 個以上 (強調 **bold**・*italic*) と、* か + だけ (検索欄のワイルドカード・演算子。#2224)。
/// * + だけの Space は Markdown の記号と空白で確定するので、空白なしの半角は Enter で確定する。
/// </summary>
internal static class HalfWidthStars
{
    public static ComposedText Apply(ComposedText text, string typed) => typed switch
    {
        "*" or "+" => text.MapChars(chars => chars.Replace('＊', '*').Replace('＋', '+')),
        _ when typed.Count(c => c == '*') >= 2 => text.MapChars(chars => chars.Replace('＊', '*')),
        _ => text,
    };
}
