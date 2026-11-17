// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition;

/// <summary>文字列の終わり・始まりが英語か日本語か (キャレットの前後の文字・変換エンジンに渡す文脈の判断に使う)。</summary>
internal static class TextLanguage
{
    /// <summary>確定済みの文字列の最後の (空白以外の) 文字が英数字なら英語、かな・漢字・全角記号なら日本語。</summary>
    internal static bool? Of(string? text)
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
    internal static bool? OfStart(string? text)
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
}
