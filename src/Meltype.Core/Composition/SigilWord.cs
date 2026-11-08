// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// AI エージェントの入力欄などで打つ /command・$skill・@ファイル名 を見分ける (#193)。
/// 入力欄・行の先頭か空白の直後に打った / $ @ と、続く名前 (空白まで) は変換せず、打つたびにそのままアプリへ渡す
/// (変換ボックスに溜めると、アプリ側の補完の候補が出ない・選べない)。名前の後に空白を打てば普通の入力に戻る。
/// taro@example.com の @ は前が英字なので対象にしない。どれもキャレットの前の文字 (before) だけで決める。
/// </summary>
public static class SigilWord
{
    /// <summary>名前の前に付ける記号。</summary>
    public static bool IsSigil(char c) => c is '/' or '$' or '@';

    /// <summary>名前に使う文字: 空白以外の半角の英数字・記号 (review、skill-name、src/app.ts、plugin:cmd)。</summary>
    public static bool IsNameChar(char c) => c is > ' ' and <= '~';

    /// <summary>
    /// キャレットの前の文字列 before の後に打った c を、変換せずにそのままアプリへ渡すか (状態を持たない版)。
    /// 先頭か空白の直後の記号と、記号から続く名前の文字。before が分からなければ渡さない。
    /// </summary>
    public static bool PassesAfter(string? before, char c) =>
        before is not null && (InNameAfter(before) ? IsNameChar(c) : IsSigil(c) && LastWord(before).IsEmpty);

    /// <summary>before の後で、記号から始めた名前を打っている途中か。</summary>
    public static bool InNameAfter(string? before)
    {
        if (before is null) return false;
        var word = LastWord(before);
        if (word.IsEmpty || !IsSigil(word[0])) return false;
        foreach (var c in word)
        {
            if (!IsNameChar(c)) return false;
        }
        return true;
    }

    // キャレットの前の、最後の空白より後ろ。Discord は、空の入力欄でもキャレットの前に見えない文字 (U+FEFF) を返すので除く
    private static ReadOnlySpan<char> LastWord(string before)
    {
        var text = before.AsSpan().TrimEnd("﻿​");
        var start = text.Length;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        return text[start..];
    }
}
