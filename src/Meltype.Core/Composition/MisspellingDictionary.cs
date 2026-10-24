// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text;

namespace Meltype.Composition;

/// <summary>読みの中に見つけた書き間違い。Start と Length は読み (カタカナ) の中の位置。</summary>
public readonly record struct Misspelling(int Start, int Length, string Wrong, string Right);

/// <summary>
/// よくある書き間違い (ブレスレッド → ブレスレット、シュミレーション → シミュレーション) の辞書。「もしかして」に使う。
/// misspellings.txt の 1 行は「誤り[Tab]正しい形」か、「正しい形」だけ。どちらも読み (カタカナ・ひらがな) で書く。
/// 正しい形だけの行 (と、誤りの行の正しい形) は、濁点・半濁点・促音 (ッ)・長音 (ー)・小書き文字だけが違う
/// 綴り (ブレスレッド、ブレースレット) を見つけるのにも使う。偶然の一致を避けるため 5 文字以上の語だけ。
/// </summary>
public sealed class MisspellingDictionary
{
    private const int FuzzyMinimumLength = 5;
    private readonly Dictionary<string, string> _wrong = new(StringComparer.Ordinal);
    private readonly HashSet<string> _right = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _rightByShape = new(StringComparer.Ordinal);

    public int Count => _wrong.Count + _right.Count;

    public static MisspellingDictionary Load(string? userDirectory)
    {
        var dictionary = new MisspellingDictionary();
        dictionary.AddText(Detection.DictionarySource.ReadEmbedded("misspellings.txt"));
        if (userDirectory is not null)
        {
            var path = Path.Combine(userDirectory, "misspellings.txt");
            try
            {
                if (File.Exists(path)) dictionary.AddText(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"ユーザーの書き間違い辞書を読めませんでした: {ex.Message}");
            }
        }
        return dictionary;
    }

    public void AddText(string text)
    {
        foreach (var line in Detection.DictionarySource.LinesWithoutComments(text))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1) AddRight(KanaChars.ToKatakana(parts[0]));
            else if (parts.Length >= 2) Add(KanaChars.ToKatakana(parts[0]), KanaChars.ToKatakana(parts[1]));
        }
    }

    public void Add(string wrong, string right)
    {
        if (wrong.Length == 0 || right.Length == 0 || wrong == right) return;
        _wrong[wrong] = right;
        AddRight(right);
    }

    private void AddRight(string right)
    {
        if (right.Length == 0 || !_right.Add(right) || right.Length < FuzzyMinimumLength) return;
        var shape = Shape(right);
        if (!_rightByShape.TryGetValue(shape, out var list)) _rightByShape[shape] = list = [];
        list.Add(right);
    }

    /// <summary>読み (ひらがな・カタカナ) の中から書き間違いを 1 つ探す。無ければ null。</summary>
    public Misspelling? Find(string reading)
    {
        var text = KanaChars.ToKatakana(reading);
        // 1. 辞書に書いてある誤り (長いものを優先)
        Misspelling? best = null;
        foreach (var (wrong, right) in _wrong)
        {
            var index = text.IndexOf(wrong, StringComparison.Ordinal);
            if (index < 0 || best is { } b && b.Length >= wrong.Length) continue;
            // 正しい形の一部として出てきただけ (シミュレーション の中の …) なら誤りではない。
            if (IsInsideRight(text, index, wrong.Length)) continue;
            best = new Misspelling(index, wrong.Length, wrong, right);
        }
        if (best is not null) return best;

        // 2. 濁点・促音・長音・小書き文字だけが違う綴り
        for (var start = 0; start < text.Length; start++)
        {
            for (var length = Math.Min(text.Length - start, 20); length >= FuzzyMinimumLength - 1; length--)
            {
                var part = text.Substring(start, length);
                if (_right.Contains(part)) break; // 正しく打てている
                if (!_rightByShape.TryGetValue(Shape(part), out var candidates)) continue;
                var right = candidates[0];
                if (IsInsideRight(text, start, length)) continue;
                return new Misspelling(start, length, part, right);
            }
        }
        return null;
    }

    private bool IsInsideRight(string text, int start, int length)
    {
        foreach (var right in _right)
        {
            if (right.Length <= length) continue;
            for (var from = Math.Max(0, start + length - right.Length); from <= start && from + right.Length <= text.Length; from++)
            {
                if (string.CompareOrdinal(text, from, right, 0, right.Length) == 0) return true;
            }
        }
        return false;
    }

    /// <summary>濁点・半濁点・促音・長音・小書き文字の違いを無視した形 (ブレスレッド と ブレスレット は同じ)。</summary>
    internal static string Shape(string katakana)
    {
        var builder = new StringBuilder(katakana.Length);
        foreach (var c in katakana)
        {
            var shape = c < 0x80 ? AsciiShapes[c] : c is >= '゠' and <= 'ヿ' ? KatakanaShapes[c - 0x30A0] : null;
            if (shape is null) return NormalizedShape(katakana);
            builder.Append(shape);
        }
        return builder.ToString();
    }

    // 1 文字ずつの形をつなげても、文字列全体を正規化したときと同じになる範囲 (濁点などの結合文字は捨てるので並べ替えが効かない)
    private static readonly string[] AsciiShapes = Enumerable.Range(0, 0x80).Select(c => NormalizedShape(((char)c).ToString())).ToArray();
    private static readonly string[] KatakanaShapes = Enumerable.Range(0x30A0, 0x60).Select(c => NormalizedShape(((char)c).ToString())).ToArray();

    private static string NormalizedShape(string katakana)
    {
        var builder = new StringBuilder(katakana.Length);
        foreach (var c in katakana.Normalize(NormalizationForm.FormD))
        {
            if (c is 'ッ' or 'ー' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(c switch
            {
                'ァ' => 'ア', 'ィ' => 'イ', 'ゥ' => 'ウ', 'ェ' => 'エ', 'ォ' => 'オ',
                'ャ' => 'ヤ', 'ュ' => 'ユ', 'ョ' => 'ヨ', 'ヮ' => 'ワ',
                _ => c,
            });
        }
        return builder.ToString();
    }
}
