// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;

namespace Meltype.Composition;

/// <summary>
/// 変換ボックスの表示と確定の文字。打った句読点 (読みの 、。) の区間を区別して持ち、設定の句読点は <see cref="CompositionOutput"/> がその区間にだけ当てる。
/// 辞書の語・顔文字に入っている 、。 (モーニング娘。) は句読点の区間にしない。
/// </summary>
internal sealed class ComposedText
{
    private readonly string _text;
    // _text の文字ごとに、打った句読点か
    private readonly bool[] _punctuation;

    private ComposedText(string text, bool[] punctuation)
    {
        _text = text;
        _punctuation = punctuation;
    }

    public static readonly ComposedText Empty = Literal("");

    /// <summary>打った句読点を含まない文字 (英字・打ったままの原文・変換エンジンを通らない記号)。</summary>
    public static ComposedText Literal(string text) => new(text, new bool[text.Length]);

    /// <summary>読みのまま見せる文字。読みの 、。 はすべて打った句読点。</summary>
    public static ComposedText Kana(string kana) => new(kana, kana.Select(IsMark).ToArray());

    /// <summary>
    /// 読み reading を変換した文字 text。読みの 、。 を後ろから順に text の同じ文字に対応させ、対応した文字を打った句読点にする。
    /// 読みに無い 、。 (もーにんぐむすめ → モーニング娘。) は句読点にしない。
    /// </summary>
    public static ComposedText Converted(string reading, string text)
    {
        var punctuation = new bool[text.Length];
        var at = text.Length;
        for (var r = reading.Length - 1; r >= 0; r--)
        {
            if (!IsMark(reading[r])) continue;
            var found = at > 0 ? text.LastIndexOf(reading[r], at - 1) : -1;
            if (found < 0) break;
            punctuation[found] = true;
            at = found;
        }
        return new(text, punctuation);
    }

    private static bool IsMark(char c) => c is '、' or '。';

    /// <summary>読み・変換・学習で使う文字 (句読点は 、。)。</summary>
    public string Text => _text;

    public int Length => _text.Length;

    public ComposedText Slice(int start, int length) => new(_text.Substring(start, length), _punctuation[start..(start + length)]);

    public static ComposedText operator +(ComposedText left, ComposedText right) => new(left._text + right._text, [.. left._punctuation, .. right._punctuation]);

    public static ComposedText Concat(IEnumerable<ComposedText> texts) => texts.Aggregate(Empty, (all, text) => all + text);

    /// <summary>文字を 1 対 1 で置き換える (句読点の区間はそのまま)。</summary>
    public ComposedText Replace(char from, char to) => new(_text.Replace(from, to), _punctuation);

    /// <summary>
    /// 空白を足した spaced (英単語の前後の空白) に、句読点の区間を移す。spaced が _text に空白を足しただけのものでなければ例外。
    /// </summary>
    public ComposedText WithSpaces(string spaced)
    {
        var punctuation = new bool[spaced.Length];
        var i = 0;
        for (var s = 0; s < spaced.Length; s++)
        {
            if (i < _text.Length && spaced[s] == _text[i]) punctuation[s] = _punctuation[i++];
            else if (spaced[s] != ' ') throw new ArgumentException($"空白を足しただけの文字ではない: {_text} → {spaced}", nameof(spaced));
        }
        if (i != _text.Length) throw new ArgumentException($"空白を足しただけの文字ではない: {_text} → {spaced}", nameof(spaced));
        return new(spaced, punctuation);
    }

    /// <summary>打った句読点の 、。 を comma・period にした文字。</summary>
    public string Render(char comma, char period)
    {
        var builder = new StringBuilder(_text);
        for (var i = 0; i < builder.Length; i++)
        {
            if (_punctuation[i]) builder[i] = builder[i] == '、' ? comma : period;
        }
        return builder.ToString();
    }
}
