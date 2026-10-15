// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>かな・カタカナ・漢字の文字種の判定と、ひらがな ⇔ カタカナの変換。複数の場所に同じ式を書かないためにまとめた。</summary>
internal static class KanaChars
{
    private const int KatakanaOffset = 0x60;

    public static bool IsHiragana(char c) => c is >= 'ぁ' and <= 'ゖ';

    /// <summary>ひらがな、または長音符 (ー)。読みとして使える文字。</summary>
    public static bool IsHiraganaOrLongMark(char c) => c is >= 'ぁ' and <= 'ゖ' or 'ー';

    public static bool IsKatakana(char c) => c is >= 'ァ' and <= 'ヶ';

    public static bool IsKanji(char c) => c is >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or '々';

    /// <summary>漢字かカタカナ。かな (助詞・送り仮名) だけでない語かを見るのに使う。</summary>
    public static bool IsKanjiOrKatakana(char c) => IsKanji(c) || IsKatakana(c);

    public static string ToHiragana(string text) => new(text.Select(c => IsKatakana(c) ? (char)(c - KatakanaOffset) : c).ToArray());

    public static string ToKatakana(string text) => new(text.Select(c => IsHiragana(c) ? (char)(c + KatakanaOffset) : c).ToArray());
}
