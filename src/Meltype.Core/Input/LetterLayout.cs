// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Input;

/// <summary>
/// 英字の位置を入れ替えるローマ字の配列 (Dvorak など): QWERTY の位置で打った文字を、その配列の文字にする。
/// 入れ替えるのは英字の 3 段 (Q の段・A の段・Z の段の 10 キーずつ) だけで、数字と記号のキーはそのまま。
/// 表は QWERTY の qwertyuiop / asdfghjkl; / zxcvbnm,./ の位置に、その配列の文字を並べたもの。
/// </summary>
public static class LetterLayout
{
    private const string Qwerty = "qwertyuiopasdfghjkl;zxcvbnm,./";

    private static readonly Dictionary<RomajiLayout, string> Tables = new()
    {
        [RomajiLayout.Dvorak] = "',.pyfgcrlaoeuidhtns;qjkxbmwvz",
        [RomajiLayout.Colemak] = "qwfpgjluy;arstdhneiozxcvbkm,./",
        [RomajiLayout.Workman] = "qdrwbjfup;ashtgyneoizxmcvkl,./",
        [RomajiLayout.Ohnishi] = "qlu,.fwrypeiao-ktnshzxcv;gdmjb",
        [RomajiLayout.Eucalyn] = "qw,.;mrdypaoeiugtksnzxcvfbhjl/",
    };

    /// <summary>QWERTY の位置で打った文字 ch を、その配列の文字にする。入れ替えない文字なら null。</summary>
    public static char? Map(RomajiLayout layout, char ch)
    {
        if (!Tables.TryGetValue(layout, out var table)) return null;
        var index = Qwerty.IndexOf(char.ToLowerInvariant(ch));
        if (index < 0) return null;
        var mapped = table[index];
        if (char.IsAsciiLetter(mapped)) return char.IsAsciiLetterUpper(ch) ? char.ToUpperInvariant(mapped) : mapped;
        // Shift で打った記号の位置 (< > ? :) は入れ替えない (英字の位置の Shift だけを大文字にする)
        return char.IsAsciiLetter(ch) ? mapped : null;
    }

    /// <summary>
    /// アプリから届いたキーを、その配列の文字のキーにする。日本語のローマ字入力のときだけで、
    /// 英数状態とショートカット (Ctrl・Alt・Command) のキーはアプリにそのまま届くので入れ替えない。
    /// 仮想キーは省き、入れ替えた文字から決めさせる (<see cref="Ime.ImeKey.Vk"/>)。
    /// </summary>
    public static Ime.ImeKey Apply(Ime.ImeKey key, Config.Settings settings, bool direct) =>
        !direct && settings.InputStyle != Config.InputStyle.Kana && !key.Ctrl && !key.Alt && !key.Command &&
        key.Ch is > 0 and < 0x80 && Map(settings.RomajiLayout, (char)key.Ch) is { } mapped
            ? key with { Vk = 0, Ch = mapped }
            : key;
}
