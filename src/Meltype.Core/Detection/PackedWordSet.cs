// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Eisenferik

namespace Meltype.Detection;

/// <summary>
/// 語ごとの文字列を保持せず、連結した文字列と語の位置を持つ読み取り専用の集合。
/// 語は並べた順に持つ (重複を隣どうしで除き、HasPrefix で頭を引くため)。語そのものは、打鍵ごとに引くので語の番号をハッシュ表で引く。
/// </summary>
internal sealed class PackedWordSet
{
    private readonly string _chars;
    private readonly int[] _starts; // 語 k は _chars[_starts[k].._starts[k + 1]]
    private readonly HashIndex<int> _index; // 語の番号 + 1

    private PackedWordSet(string chars, int[] starts)
    {
        (_chars, _starts) = (chars, starts);
        _index = new(Count);
        for (var k = 0; k < Count; k++) _index.Add(HashIndex.Of(Word(k)), k + 1);
    }

    private ReadOnlySpan<char> Word(int k) => _chars.AsSpan(_starts[k], _starts[k + 1] - _starts[k]);

    /// <summary>
    /// 1 行に 1 語の辞書の本文から作る。空行と # で始まる行は飛ばす。語ごとの文字列は作らない。
    /// firstField なら、行のタブより前 (readings.txt の「読み [TAB] 活用の種類」の読み) を語にする。
    /// </summary>
    public static PackedWordSet FromLines(string text, bool firstField = false)
    {
        var words = new List<(int Start, int Length)>();
        for (var at = 0; at < text.Length;)
        {
            var end = text.IndexOf('\n', at);
            if (end < 0) end = text.Length;
            var line = text.AsSpan(at, end - at);
            if (firstField)
            {
                var tab = line.IndexOf('\t');
                if (tab >= 0) line = line[..tab];
            }
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && trimmed[0] != '#')
            {
                var leading = line.Length - line.TrimStart().Length;
                words.Add((at + leading, trimmed.Length));
            }
            at = end + 1;
        }
        return Pack(text, words);
    }

    /// <summary>語の一覧から作る (活用した形など、作って足す語)。</summary>
    public static PackedWordSet FromWords(IEnumerable<string> words)
    {
        // 作った語は並んでいないので、文字列のまま並べてから詰める (Pack で位置を並べ替えるより速い。順序はどちらも UTF-16 の符号単位の順)
        var sorted = new List<string>(words);
        sorted.Sort(StringComparer.Ordinal);
        var text = new System.Text.StringBuilder();
        var positions = new List<(int Start, int Length)>();
        foreach (var word in sorted)
        {
            positions.Add((text.Length, word.Length));
            text.Append(word);
        }
        return Pack(text.ToString(), positions);
    }

    /// <summary>text の中の語 (始まりと長さ) を並べ、重複を除いて詰める。</summary>
    private static PackedWordSet Pack(string text, List<(int Start, int Length)> words)
    {
        int Compare((int Start, int Length) a, (int Start, int Length) b) =>
            text.AsSpan(a.Start, a.Length).SequenceCompareTo(text.AsSpan(b.Start, b.Length));
        // 辞書の本文はたいてい並べて書いてある。Sort は並んだ列でも n log n 回比べるので、並んでいれば並べ直さない。
        var sorted = true;
        for (var k = 1; k < words.Count && sorted; k++) sorted = Compare(words[k - 1], words[k]) <= 0;
        if (!sorted) words.Sort(Compare);
        var buffer = new char[words.Sum(w => w.Length)];
        var starts = new List<int>(words.Count + 1) { 0 };
        var length = 0;
        foreach (var (start, count) in words)
        {
            var word = text.AsSpan(start, count);
            // 並べたので、重複は直前に入れた語と比べれば除ける
            if (starts.Count > 1 && buffer.AsSpan(starts[^2], length - starts[^2]).SequenceEqual(word)) continue;
            word.CopyTo(buffer.AsSpan(length));
            length += word.Length;
            starts.Add(length);
        }
        return new PackedWordSet(new string(buffer, 0, length), [.. starts]);
    }

    public int Count => _starts.Length - 1;

    public int MaxLength
    {
        get
        {
            var max = 0;
            for (var k = 0; k < Count; k++) max = Math.Max(max, Word(k).Length);
            return max;
        }
    }

    public bool Contains(ReadOnlySpan<char> word)
    {
        foreach (var k in _index.Candidates(HashIndex.Of(word)))
        {
            if (Word(k - 1).SequenceEqual(word)) return true;
        }
        return false;
    }

    /// <summary>prefix で始まる語があるか (prefix 自体が語の場合も true)。語は並べた順なので、prefix 以上の最初の語を見る。</summary>
    public bool HasPrefix(ReadOnlySpan<char> prefix)
    {
        var (low, high) = (0, Count);
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (Word(middle).SequenceCompareTo(prefix) < 0) low = middle + 1;
            else high = middle;
        }
        return low < Count && Word(low).StartsWith(prefix);
    }
}
