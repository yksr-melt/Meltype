// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

using System.Text;

namespace Meltype.Config;

/// <summary>
/// 同梱したリリースノート (docs/release-notes/&lt;版&gt;.md) を、画面にそのまま出せる文にする。
/// 見出しの #、強調の **、インラインコードの `、リンクの URL を外す。
/// </summary>
internal static class ReleaseNotesText
{
    public static string ToPlainText(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = Inline(Bullet(Heading(lines[i]))).TrimEnd();
        return string.Join("\n", lines).Trim('\n');
    }

    /// <summary>行頭の見出し記号 (# 〜 ######) を外す。#1749 のような語はそのまま。</summary>
    private static string Heading(string line)
    {
        var i = 0;
        while (i < line.Length && line[i] == '#') i++;
        return i is >= 1 and <= 6 && i < line.Length && line[i] == ' ' ? line[(i + 1)..] : line;
    }

    /// <summary>行頭の箇条書き (-・*・+) を「・」にする。入れ子の字下げは残す。</summary>
    private static string Bullet(string line)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] == ' ') indent++;
        return indent + 1 < line.Length && line[indent] is '-' or '*' or '+' && line[indent + 1] == ' '
            ? line[..indent] + "・" + line[(indent + 2)..]
            : line;
    }

    /// <summary>強調の **、インラインコードの `、リンクの [文字](URL) を外す。</summary>
    private static string Inline(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    result.Append(Inline(text[(i + 2)..end]));
                    i = end + 1;
                    continue;
                }
            }
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end >= 0)
                {
                    result.Append(text, i + 1, end - i - 1);
                    i = end;
                    continue;
                }
            }
            if (text[i] == '[')
            {
                var close = text.IndexOf(']', i + 1);
                if (close >= 0 && close + 1 < text.Length && text[close + 1] == '(')
                {
                    var end = text.IndexOf(')', close + 2);
                    if (end >= 0)
                    {
                        result.Append(text, i + 1, close - i - 1);
                        i = end;
                        continue;
                    }
                }
            }
            result.Append(text[i]);
        }
        return result.ToString();
    }
}
