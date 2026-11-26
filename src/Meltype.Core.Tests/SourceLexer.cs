// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;

namespace Meltype.Tests;

internal enum SourceTokenKind { Word, String, Number, Symbol }

internal readonly record struct SourceToken(SourceTokenKind Kind, string Text);

/// <summary>
/// フロントのソース (C++・Python・Kotlin・Swift) を字句に分ける。言語ごとの文法は読まず、どの言語にも共通する字句
/// (名前・文字列・数・記号) だけを取り出し、コメントは捨てる。正規表現でソースを直接読むと、コメントの中や書き方の違いで読み違える。
/// </summary>
internal static class SourceLexer
{
    // 文字列の前に付く印 (C++ の L"x"、Python の f"x" など)。中身の読み方は変わらないので捨てる。
    private static readonly HashSet<string> StringPrefixes = ["L", "u", "U", "u8", "R", "f", "r", "b", "rb", "br", "fr", "rf"];

    public static List<SourceToken> Tokenize(string source)
    {
        var tokens = new List<SourceToken>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c)) i++;
            else if (StartsWith(source, i, "//") || c == '#') i = LineEnd(source, i);
            else if (StartsWith(source, i, "/*")) i = BlockEnd(source, i);
            else if (c is '"' or '\'') i = ReadString(source, i, tokens);
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                var word = source[start..i];
                if (!(i < source.Length && source[i] is '"' or '\'' && StringPrefixes.Contains(word))) tokens.Add(new(SourceTokenKind.Word, word));
            }
            else if (char.IsAsciiDigit(c))
            {
                var start = i;
                while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] is '.' or '_')) i++;
                tokens.Add(new(SourceTokenKind.Number, source[start..i]));
            }
            else
            {
                tokens.Add(new(SourceTokenKind.Symbol, c.ToString()));
                i++;
            }
        }
        return tokens;
    }

    private static bool StartsWith(string source, int i, string text) => string.CompareOrdinal(source, i, text, 0, text.Length) == 0;

    private static int LineEnd(string source, int i)
    {
        var end = source.IndexOf('\n', i);
        return end < 0 ? source.Length : end;
    }

    private static int BlockEnd(string source, int i)
    {
        var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
        return end < 0 ? source.Length : end + 2;
    }

    /// <summary>引用符で始まる文字列を読み、中身 (エスケープを外したもの) を足して、終わりの次の位置を返す。3 つ重ねの引用符 (Python・Kotlin・Swift) も読む。</summary>
    private static int ReadString(string source, int i, List<SourceToken> tokens)
    {
        var quote = source[i];
        var close = StartsWith(source, i, new string(quote, 3)) ? new string(quote, 3) : quote.ToString();
        i += close.Length;
        var text = new StringBuilder();
        while (i < source.Length && !StartsWith(source, i, close))
        {
            // 閉じていない 1 つの引用符の文字列は行で終わる (C++ の文字 '"' を読み違えても、次の行まで巻き込まない)
            if (close.Length == 1 && source[i] == '\n') break;
            if (source[i] == '\\' && i + 1 < source.Length) i++;
            text.Append(source[i++]);
        }
        tokens.Add(new(SourceTokenKind.String, text.ToString()));
        return Math.Min(source.Length, i + close.Length);
    }
}
