// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Libraries.Chars;

/// <summary>文字列の言語 (英語か日本語か) と、半角・全角の文字の扱い。</summary>
internal static class TextChars
{
    /// <summary>確定済みの文字列の最後の (空白以外の) 文字が英数字なら英語、かな・漢字・全角記号なら日本語。</summary>
    public static bool? LanguageOf(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (Classify(text[i]) is { } english) return english;
            if (text[i] >= 0x80 && !char.IsWhiteSpace(text[i]) && text[i] != '　') return null;
        }
        return null;
    }

    /// <summary>キャレットの後ろの文字列の最初の (空白以外の) 文字で判断する。</summary>
    public static bool? LanguageOfStart(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var c in text)
        {
            if (c is '\r' or '\n') return null; // 次の行は別の文
            if (Classify(c) is { } english) return english;
            if (c >= 0x80 && !char.IsWhiteSpace(c) && c != '　') return null;
        }
        return null;
    }

    /// <summary>
    /// 英数字なら英語 (true)、かな・漢字・全角文字・日本語の文で使う記号 (○ ※ ★ 「」 など U+2000 以降) なら日本語 (false)、
    /// 空白や英文の記号なら判断しない (null)。
    /// </summary>
    private static bool? Classify(char c)
    {
        if (char.IsAsciiLetterOrDigit(c)) return true;
        if (c >= '\u2000' && !char.IsWhiteSpace(c) && c != '\u3000') return false;
        return null;
    }

    /// <summary>
    /// 変換エンジンは読みの中の英数字を全角にする (2025ねん → ２０２５年、sだけが → ｓだけが) ので、
    /// 半角で打った数字・英字は半角に戻す。読みに半角の英数字が無ければ (全角を選んだ候補など) そのまま。
    /// </summary>
    public static string NormalizeHalfWidth(string converted, string reading)
    {
        var digits = reading.Any(char.IsAsciiDigit);
        var letters = reading.Any(char.IsAsciiLetter);
        if (!digits && !letters) return converted;
        var chars = converted.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (digits && chars[i] is >= '０' and <= '９') chars[i] = (char)(chars[i] - 0xFEE0);
            else if (letters && chars[i] is >= 'Ａ' and <= 'Ｚ' or >= 'ａ' and <= 'ｚ') chars[i] = (char)(chars[i] - 0xFEE0);
        }
        return new string(chars);
    }
}
