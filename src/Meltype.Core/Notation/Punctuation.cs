// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;

namespace Meltype.Notation;

/// <summary>
/// 設定の句読点。読み・変換・学習では設定によらず 、。 で持つ。
/// 当てるのは打った句読点の区間 (<see cref="SpanKind.Punctuation"/>) だけで、語の中の 、。 (モーニング娘。) は変えない。
/// </summary>
internal static class Punctuation
{
    /// <summary>文字列にする。</summary>
    public static string Write(ComposedText text, PunctuationStyle style)
    {
        var (comma, period) = Marks(style);
        return string.Concat(text.Spans.Select(span => span.Kind switch
        {
            SpanKind.Punctuation => span.Text.Replace('、', comma).Replace('。', period),
            SpanKind.Japanese or SpanKind.English or SpanKind.Protected => span.Text,
            SpanKind.Typed => throw new InvalidOperationException("表記を決めていない区間を文字列にした (NotationRules.Typed を通していない)"),
            _ => throw new InvalidOperationException($"区間の種類 {span.Kind} の書き方が無い"),
        }));
    }

    /// <summary>入力欄から読んだ文字の、設定の句読点を 、。 に戻す。</summary>
    public static string Read(string text, PunctuationStyle style)
    {
        var (comma, period) = Marks(style);
        return text.Replace(comma, '、').Replace(period, '。');
    }

    private static (char Comma, char Period) Marks(PunctuationStyle style) => style switch
    {
        PunctuationStyle.Japanese => ('、', '。'),
        PunctuationStyle.FullWidthCommaPeriod => ('，', '．'),
        PunctuationStyle.FullWidthCommaKuten => ('，', '。'),
        PunctuationStyle.ToutenFullWidthPeriod => ('、', '．'),
        _ => throw new InvalidOperationException($"句読点の設定 {style} の文字が無い"),
    };
}
