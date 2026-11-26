// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

using System.Text;

namespace Meltype.Config;

/// <summary>
/// 同梱したリリースノート (docs/release-notes/&lt;版&gt;.md) を、画面に出すための行に分ける。
/// 見出しの #、強調の **、インラインコードの `、リンクの URL を外す (リンクは文字だけ残す)。
/// </summary>
public static class ReleaseNotesMarkdown
{
    public enum LineKind
    {
        Text,
        Heading,
        Bullet,
    }

    /// <summary>1 行の中で、同じ書式が続く文字。</summary>
    public readonly record struct Span(string Text, bool Bold = false, bool Code = false);

    /// <summary>1 行。見出しの大きさは HeadingLevel (1〜6)、箇条書きの字下げは Indent (空白の数)。</summary>
    public sealed record Line(LineKind Kind, int HeadingLevel, int Indent, IReadOnlyList<Span> Spans);

    public static IReadOnlyList<Line> Parse(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var result = new List<Line>(lines.Length);
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (Heading(line) is { } heading)
                result.Add(new(LineKind.Heading, heading.Level, 0, Inline(heading.Text)));
            else if (Bullet(line) is { } bullet)
                result.Add(new(LineKind.Bullet, 0, bullet.Indent, Inline(bullet.Text)));
            else
                result.Add(new(LineKind.Text, 0, 0, Inline(line)));
        }
        // 先頭と末尾の空行は出さない。途中の空行はそのまま残す。
        while (result.Count > 0 && result[^1].Spans.Count == 0) result.RemoveAt(result.Count - 1);
        while (result.Count > 0 && result[0].Spans.Count == 0) result.RemoveAt(0);
        return result;
    }

    /// <summary>行頭の見出し記号 (# 〜 ######) を外す。#1749 のような語はそのまま。</summary>
    private static (int Level, string Text)? Heading(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#') level++;
        return level is >= 1 and <= 6 && level < line.Length && line[level] == ' '
            ? (level, line[(level + 1)..])
            : null;
    }

    /// <summary>行頭の箇条書き (-・*・+) を外し、字下げ (空白の数) と本文を返す。</summary>
    private static (int Indent, string Text)? Bullet(string line)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] == ' ') indent++;
        return indent + 1 < line.Length && line[indent] is '-' or '*' or '+' && line[indent + 1] == ' '
            ? (indent, line[(indent + 2)..])
            : null;
    }

    /// <summary>強調の **、インラインコードの `、リンクの [文字](URL) を、書式付きの文字に分ける。</summary>
    private static List<Span> Inline(string text)
    {
        var spans = new List<Span>();
        var plain = new StringBuilder();

        void Flush()
        {
            if (plain.Length > 0) Add(new Span(plain.ToString()));
            plain.Clear();
        }

        void Add(Span span)
        {
            if (span.Text.Length == 0) return;
            if (spans.Count > 0 && spans[^1] is { Bold: var bold, Code: var code } last && bold == span.Bold && code == span.Code)
                spans[^1] = last with { Text = last.Text + span.Text };
            else
                spans.Add(span);
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    Flush();
                    foreach (var span in Inline(text[(i + 2)..end])) Add(span with { Bold = true });
                    i = end + 1;
                    continue;
                }
            }
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end >= 0)
                {
                    Flush();
                    Add(new Span(text[(i + 1)..end], Code: true));
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
                        Flush();
                        foreach (var span in Inline(text[(i + 1)..close])) Add(span);
                        i = end;
                        continue;
                    }
                }
            }
            plain.Append(text[i]);
        }
        Flush();
        return spans;
    }
}
