// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Notation;

/// <summary>打った * が 2 個以上あるときだけ、表示の ＊ を打った半角の * にする (強調 **bold**・*italic*)。</summary>
internal static class HalfWidthStars
{
    public static ComposedText Apply(ComposedText text, string typed) =>
        typed.Count(c => c == '*') >= 2 ? text.MapChars(chars => chars.Replace('＊', '*')) : text;
}
