// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using System.Text;

namespace Meltype.Notation;

/// <summary>日本語 (かな・漢字) と英単語・半角数字の間に入れる半角スペース。</summary>
internal static class EnglishSpacing
{
    public static ComposedText Apply(ComposedText text, string? before, string? after, bool english, bool numbers) =>
        english || numbers ? text.WithSpaces(Spaced(text.Text, before, after, english, numbers)) : text;

    /// <summary>
    /// 日本語 (かな・漢字) と英単語の間に半角スペースを入れる (今日はGitHubにpush → 今日は GitHub に push)。
    /// 英字を 1 つ以上含む英数字の並び (iPhone15、C++) を英単語とみなす。数字だけ (3時) と記号 (、。「」) の隣には入れない。
    /// 確定する文字列の端は、入力欄のキャレットの前後の文字 (before・after) との間も見る。
    /// english が false なら英単語には入れない。numbers が true なら、半角数字だけの並び (3回・3,000円 の 3 と 000) の前後にも入れる (issue #958)。
    /// </summary>
    internal static string Spaced(string text, string? before, string? after, bool english = true, bool numbers = false)
    {
        static bool IsJapanese(char c) => c is (>= '\u3040' and <= '\u30FF') or (>= '\u3400' and <= '\u4DBF') or (>= '\u4E00' and <= '\u9FFF') or (>= '\uF900' and <= '\uFAFF');
        static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '#' or '.' or '\'' or '@';
        var previous = string.IsNullOrEmpty(before) ? '\0' : before[^1];
        var next = string.IsNullOrEmpty(after) ? '\0' : after[0];
        var builder = new StringBuilder(text.Length + 8);
        // 英字を含む並び (iPhone15) は english の設定、数字だけの並び (15) は numbers の設定に従う (文字列の中と同じ判定)
        bool SpacedWord(string word) => word.Any(char.IsAsciiLetter) ? english : numbers && word.Any(char.IsAsciiDigit);
        // 前に確定した文字列の最後の語・キャレットの後ろの最初の語で決める (iPhone15 の 5 だけを見て数字にしない)
        var previousWord = new string((before ?? "").Reverse().TakeWhile(IsWordChar).Reverse().ToArray());
        var nextWord = new string((after ?? "").TakeWhile(IsWordChar).ToArray());
        // 前に確定した英単語 (数字) のすぐ後ろに日本語を続けるとき (GitHub|に、3|回)
        if (IsJapanese(text[0]) && char.IsAsciiLetterOrDigit(previous) && SpacedWord(previousWord)) builder.Append(' ');
        var i = 0;
        while (i < text.Length)
        {
            if (!IsWordChar(text[i]))
            {
                builder.Append(text[i++]);
                continue;
            }
            var end = i;
            while (end < text.Length && IsWordChar(text[end])) end++;
            // 語の端の記号 (. ' -) は語に含めない (Hello. の . の後ろに日本語が続いても、. の前に入れない)
            var start = i;
            var stop = end;
            while (stop > start && text[stop - 1] is '.' or '\'' or '-' or '@' or '_') stop--;
            var word = text[start..stop];
            var left = start > 0 ? text[start - 1] : previous;
            var right = stop < text.Length ? text[stop] : next;
            var isWord = SpacedWord(word);
            if (isWord && IsJapanese(left) && (builder.Length == 0 || builder[^1] != ' ')) builder.Append(' ');
            builder.Append(word);
            if (isWord && IsJapanese(right)) builder.Append(' ');
            builder.Append(text, stop, end - stop);
            i = end;
        }
        // 確定した日本語のすぐ後ろが英単語 (数字) のとき (キャレットの後ろの文字)
        if (IsJapanese(text[^1]) && char.IsAsciiLetterOrDigit(next) && SpacedWord(nextWord)) builder.Append(' ');
        return builder.ToString();
    }
}
