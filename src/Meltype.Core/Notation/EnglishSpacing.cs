// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

namespace Meltype.Notation;

/// <summary>
/// 日本語 (かな・漢字) と英単語・半角数字の間に入れる半角スペース (今日はGitHubにpush → 今日は GitHub に push)。
/// 英語・保護区間の区間は 1 語として前後だけを見て、区間の中には入れない。日本語の区間の中 (変換した 3回・iPhone15を) は、英数字の並びを 1 語とみなす。
/// 英字を含む語は english、数字だけの語は numbers の設定に従う (issue #958)。記号 (、。「」) の隣と、語の端の記号 (Hello. の .) の後ろには入れない。
/// 確定する文字の端は、入力欄のキャレットの前後の文字 (before・after) との間も見る。
/// </summary>
internal static class EnglishSpacing
{
    public static ComposedText Apply(ComposedText text, string? before, string? after, bool english, bool numbers)
    {
        if (!english && !numbers || text.Length == 0) return text;
        var words = Words(text);
        // 空白を入れる位置 (text.Text の文字の位置。その位置の文字の前に入れる)
        var spaces = new SortedSet<int>();
        var previous = string.IsNullOrEmpty(before) ? '\0' : before[^1];
        var next = string.IsNullOrEmpty(after) ? '\0' : after[0];
        foreach (var word in words)
        {
            if (!Spaced(word.Text, english, numbers)) continue;
            var left = word.Start > 0 ? text.Text[word.Start - 1] : previous;
            var right = word.End < text.Length ? text.Text[word.End] : next;
            if (IsWordChar(word.Text[0]) && IsJapanese(left)) spaces.Add(word.Start);
            if (!IsTrailingMark(word.Text[^1]) && IsWordChar(word.Text[^1]) && IsJapanese(right)) spaces.Add(word.End);
        }
        // 前に確定した英単語 (数字) のすぐ後ろに日本語を続けるとき (GitHub|に、3|回)・確定した日本語のすぐ後ろが英単語 (数字) のとき
        if (IsJapanese(text.Text[0]) && char.IsAsciiLetterOrDigit(previous) && Spaced(LastWord(before!), english, numbers)) spaces.Add(0);
        if (IsJapanese(text.Text[^1]) && char.IsAsciiLetterOrDigit(next) && Spaced(FirstWord(after!), english, numbers)) spaces.Add(text.Length);
        return spaces.Count == 0 ? text : Insert(text, spaces);
    }

    private readonly record struct Word(int Start, int End, string Text);

    /// <summary>
    /// 空白を前後に入れるか決める語。保護区間は区間ごとに 1 語にし、ほかの語とつなげない。英語の区間の中では語を切らない。
    /// 区間の境目は、両側が語の文字 (英数字と _ - + # . ' @) ならつなぐ (1.|GitHub・#|GitHub・GitHub|3かい の GitHub3)。
    /// 区切りの記号 (GitHub|,|3かい の ,・/・!) の前後ではつながず、3 は数字だけの語になる。
    /// </summary>
    private static List<Word> Words(ComposedText text)
    {
        var words = new List<Word>();
        var offset = 0;
        var start = -1;
        void Close(int end)
        {
            if (start >= 0) words.Add(new(start, end, text.Text[start..end]));
            start = -1;
        }
        foreach (var span in text.Spans)
        {
            if (span.Kind == SpanKind.Protected)
            {
                Close(offset);
                words.Add(new(offset, offset + span.Text.Length, span.Text));
            }
            else
            {
                var english = span.Kind is SpanKind.English or SpanKind.Typed;
                if (start >= 0 && !(IsWordChar(text.Text[offset - 1]) && IsWordChar(span.Text[0]))) Close(offset);
                for (var i = 0; i < span.Text.Length; i++)
                {
                    if (english || IsWordChar(span.Text[i])) start = start < 0 ? offset + i : start;
                    else Close(offset + i);
                }
            }
            offset += span.Text.Length;
        }
        Close(offset);
        return words;
    }

    /// <summary>text の positions の文字の前に空白を入れる。足した空白は前の文字の区間に入れる (先頭なら英字の区間)。</summary>
    private static ComposedText Insert(ComposedText text, SortedSet<int> positions)
    {
        var spans = new List<ComposedText>();
        if (positions.Contains(0)) spans.Add(ComposedText.English(" "));
        var offset = 0;
        foreach (var span in text.Spans)
        {
            var spaced = span.Text;
            // 後ろから入れて、前の位置をずらさない。区間の始まり (offset) の空白は前の区間の終わりに入れる
            foreach (var at in positions.GetViewBetween(offset + 1, offset + span.Text.Length).Reverse()) spaced = spaced.Insert(at - offset, " ");
            spans.Add(ComposedText.Of(span.Kind, spaced));
            offset += span.Text.Length;
        }
        return ComposedText.Concat(spans);
    }

    /// <summary>英字を含む語は english、数字だけの語は numbers の設定に従う (iPhone15 は英単語)。</summary>
    private static bool Spaced(string word, bool english, bool numbers) => word.Any(char.IsAsciiLetter) ? english : numbers && word.Any(char.IsAsciiDigit);

    /// <summary>前に確定した文字列の最後の語・キャレットの後ろの最初の語 (iPhone15 の 5 だけを見て数字にしない)。</summary>
    private static string LastWord(string before) => new([.. before.Reverse().TakeWhile(IsWordChar).Reverse()]);

    private static string FirstWord(string after) => new([.. after.TakeWhile(IsWordChar)]);

    /// <summary>空白を入れる相手の日本語の文字 (かな・漢字・互換漢字)。</summary>
    internal static bool IsJapanese(char c) => c is (>= '\u3040' and <= '\u30FF') or (>= '\u3400' and <= '\u4DBF') or (>= '\u4E00' and <= '\u9FFF') or (>= '\uF900' and <= '\uFAFF');

    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '#' or '.' or '\'' or '@';

    /// <summary>語の端の記号。語に含めず、後ろに空白を入れない (Hello. の . の後ろに日本語が続いても入れない)。</summary>
    private static bool IsTrailingMark(char c) => c is '.' or '\'' or '-' or '@' or '_';
}
