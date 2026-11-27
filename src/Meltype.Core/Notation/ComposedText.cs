// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

namespace Meltype.Notation;

/// <summary>変換ボックスの表示と確定の文字の区間の種類。</summary>
internal enum SpanKind
{
    /// <summary>かな・変換した語・候補から選んだ文字 (語の中の 、。 も含む)。</summary>
    Japanese,

    /// <summary>英語と判定した、打ったままの文字。表記を決める前で、<see cref="NotationRules.Typed"/> を通すと <see cref="English"/> になる。</summary>
    Typed,

    /// <summary>英字・打ったままの文字。</summary>
    English,

    /// <summary>保護区間 (URL・メンション・メール・パス) の原文。</summary>
    Protected,

    /// <summary>かなと一緒に打った句読点 (読みの 、。)。</summary>
    Punctuation,
}

internal readonly record struct TextSpan(SpanKind Kind, string Text);

/// <summary>
/// 変換ボックスの表示と確定の文字を、区間の種類の並びのまま持つ。文字列にするのは <see cref="NotationRules.Write"/> だけ (設定の句読点を当てる)。
/// <see cref="Text"/> は句読点を 、。 のままつないだ文字で、学習・文脈・ログに使う。
/// </summary>
internal sealed class ComposedText
{
    private ComposedText(IReadOnlyList<TextSpan> spans)
    {
        Spans = spans;
        Text = string.Concat(spans.Select(s => s.Text));
    }

    public static readonly ComposedText Empty = new([]);

    public IReadOnlyList<TextSpan> Spans { get; }

    public string Text { get; }

    public int Length => Text.Length;

    public static ComposedText Of(SpanKind kind, string text) => text.Length == 0 ? Empty : new([new TextSpan(kind, text)]);

    public static ComposedText English(string text) => Of(SpanKind.English, text);

    /// <summary>読みのまま見せるかな。読みの 、。 は句読点の区間にする。</summary>
    public static ComposedText Kana(string kana)
    {
        var spans = new List<TextSpan>();
        var start = 0;
        for (var i = 0; i <= kana.Length; i++)
        {
            if (i < kana.Length && !IsMark(kana[i])) continue;
            if (i > start) spans.Add(new TextSpan(SpanKind.Japanese, kana[start..i]));
            if (i < kana.Length) spans.Add(new TextSpan(SpanKind.Punctuation, kana[i].ToString()));
            start = i + 1;
        }
        return new(spans);
    }

    /// <summary>
    /// 読み reading の文節の文字 text。打った句読点は読みが句読点だけの文節になる (Composition の SplitAtMarks) ので、
    /// 読みのままの文字なら読みの 、。 を句読点の区間にし、変換した文字の 、。 (モーニング娘。・顔文字) は語の文字のままにする。
    /// </summary>
    public static ComposedText Converted(string reading, string text) => text == reading ? Kana(text) : Of(SpanKind.Japanese, text);

    /// <summary>読みの句読点か (読みでは設定によらず 、。)。</summary>
    public static bool IsMark(char c) => c is '、' or '。';

    public static ComposedText operator +(ComposedText left, ComposedText right) => new([.. left.Spans, .. right.Spans]);

    public static ComposedText Concat(IEnumerable<ComposedText> texts) => new([.. texts.SelectMany(t => t.Spans)]);

    /// <summary>区間ごとに、長さを変えずに文字を置き換える (＊ → *)。</summary>
    public ComposedText MapChars(Func<string, string> map) => new([.. Spans.Select(s => s with { Text = map(s.Text) })]);

    public override string ToString() => Text;
}
