// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 Yukishiro

namespace Meltype.Notation;

/// <summary>すべて小文字で打った固有名詞の大文字・小文字 (iphone → iPhone)。当てるのは打ったままの英語の区間 (<see cref="SpanKind.Typed"/>) だけ。</summary>
internal static class ProperNounCase
{
    public static IReadOnlyList<TextSpan> Apply(IReadOnlyList<TextSpan> spans, Func<string, string?> properNoun) =>
        [.. spans.Select((span, i) => span.Kind == SpanKind.Typed ? new TextSpan(SpanKind.English, IsDotConnected(spans, i) ? span.Text : properNoun(span.Text) ?? span.Text) : span)];

    /// <summary>前後の区間と . でつながったドメイン・識別子の一部 (github.com・google.github) か。文末の句点 (github.) や連続する . は含めない。</summary>
    private static bool IsDotConnected(IReadOnlyList<TextSpan> spans, int i) =>
        i >= 2 && spans[i - 1].Text == "." && spans[i - 2].Text is [.., var previous] && char.IsAsciiLetterOrDigit(previous) ||
        i + 2 < spans.Count && spans[i + 1].Text == "." && spans[i + 2].Text is [var next, ..] && char.IsAsciiLetterOrDigit(next);
}
