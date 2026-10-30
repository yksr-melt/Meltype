// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 候補の日本語の意味 (dictionaries/meanings.txt。ウィクショナリー日本語版から作った表、CC BY-SA 4.0)。
/// 変換中に候補で止まったとき、その意味を出す (箸 → 食物を…二本一組で用いられる棒)。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "日本語入力の辞書を表す既存の公開名。コレクション型を表すものではない。")]
public sealed class MeaningDictionary
{
    // 書き方 [TAB] 読みの印 [TAB] 意味
    private readonly Detection.PackedLines _lines;
    // 活用する語の語幹 (持つ → 持、美しい → 美し) で、その語の見出しの最初の行の番号 + 1 を引く。
    private readonly Detection.HashIndex<int> _stems;

    /// <summary>一度に出す意味の数。</summary>
    private const int MaxSenses = 2;

    private MeaningDictionary(Detection.PackedLines lines)
    {
        _lines = lines;
        var words = new List<int>();
        for (var k = 0; k < lines.LineCount; k += lines.GroupAt(k).Count)
        {
            // 動詞 (う段で終わる)・い形容詞 (い で終わる) は語幹でも引けるようにする。語幹に漢字が入るものだけ。
            var word = lines.Key(k);
            if (word.Length >= 2 && "うくぐすつぬぶむるい".Contains(word[^1]) && HasKanji(word[..^1])) words.Add(k);
        }
        // 本文で先に書いてある語から入れ、同じ語幹の語は後から入れない
        words.Sort((a, b) => lines.Position(a).CompareTo(lines.Position(b)));
        _stems = new(words.Count);
        foreach (var k in words)
        {
            if (FindStem(Stem(k)).Count == 0) _stems.Add(Detection.HashIndex.Of(Stem(k)), k + 1);
        }
    }

    private static bool HasKanji(ReadOnlySpan<char> text)
    {
        foreach (var c in text) if (KanaChars.IsKanji(c)) return true;
        return false;
    }

    private ReadOnlySpan<char> Stem(int line) => _lines.Key(line)[..^1];

    public int Count => _lines.Count;

    public static MeaningDictionary Load() => Parse(Detection.DictionarySource.ReadEmbedded("meanings.txt"));

    public static MeaningDictionary Parse(string text) => new(Detection.PackedLines.FromText(text));

    /// <summary>語幹が stem の語の行の範囲。無ければ数が 0。</summary>
    private (int First, int Count) FindStem(ReadOnlySpan<char> stem)
    {
        foreach (var k in _stems.Candidates(Detection.HashIndex.Of(stem)))
        {
            if (Stem(k - 1).SequenceEqual(stem)) return _lines.GroupAt(k - 1);
        }
        return default;
    }

    /// <summary>
    /// 候補 (text) の意味。reading はその文節の読み (橋 を はし と読んだのか きょう と読んだのかで意味を選ぶ)。
    /// 候補の先頭から一番長く一致する語を引き、残りはひらがな (助詞・送り仮名) だけのときに返す。無ければ null。
    /// </summary>
    public string? Lookup(string text, string? reading)
    {
        var minLength = text.Length;
        while (minLength > 0 && !KanaChars.IsKanjiOrKatakana(text[minLength - 1])) minLength--;
        if (minLength == 0) return null;
        for (var length = Math.Min(_lines.MaxKeyLength, text.Length); length >= minLength; length--)
        {
            var head = text.AsSpan(0, length);
            var (first, count) = _lines.Find(head);
            if (count == 0)
            {
                // 辞書の語の形でなければ、活用する語の語幹として引く (持って の 持 → 持つ)
                (first, count) = FindStem(head);
            }
            if (count == 0) continue;
            Span<int> senses = stackalloc int[MaxSenses];
            return Format(senses[..Choose(first, count, reading, senses)]);
        }
        return null;
    }

    /// <summary>
    /// 読みが合う印付きの意味、印なしの意味の順に、senses が埋まるまで行を選ぶ。どちらも無ければ本文の順に選ぶ。選んだ数を返す。
    /// </summary>
    private int Choose(int first, int count, string? reading, Span<int> senses)
    {
        var n = 0;
        if (reading is not null)
        {
            for (var k = first; k < first + count && n < senses.Length; k++)
            {
                var mark = _lines.Field(k, 1);
                if (!mark.IsEmpty && reading.AsSpan().StartsWith(mark, StringComparison.Ordinal)) senses[n++] = k;
            }
        }
        for (var k = first; k < first + count && n < senses.Length; k++)
        {
            if (_lines.Field(k, 1).IsEmpty) senses[n++] = k;
        }
        if (n == 0)
        {
            for (var k = first; k < first + count && n < senses.Length; k++) senses[n++] = k;
        }
        return n;
    }

    private string Format(ReadOnlySpan<int> senses)
    {
        if (senses.Length == 1) return _lines.Field(senses[0], 2).ToString();
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < senses.Length; i++)
        {
            if (i > 0) text.Append('\n');
            text.Append((char)('①' + i)).Append(' ').Append(_lines.Field(senses[i], 2));
        }
        return text.ToString();
    }
}
