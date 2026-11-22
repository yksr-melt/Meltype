// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Libraries.Lexicon;

namespace Meltype.Detection;

/// <summary>単語集合と、その全 prefix の集合。判定はフックのスレッドで走るので O(1) で引けるようにしておく。</summary>
public sealed class WordList
{
    private readonly HashSet<string> _words = new(StringComparer.Ordinal);
    private readonly Dictionary<char, List<string>> _byFirstLetter = [];
    private readonly List<string> _wordsByIndex = [];
    // prefix の文字列を複製せず、元の語の番号と長さで引く。
    // 上位 32 ビットが _wordsByIndex の番号、下位 32 ビットが長さ (1 以上なので 0 にはならない)。
    private readonly HashIndex<long> _prefixes;

    public WordList() : this([])
    {
    }

    /// <summary>語をまとめて入れる。頭の数を先に数えて、頭の表を 1 回で作る (1 語ずつ Add すると、表を倍々に作り直す)。</summary>
    public WordList(IEnumerable<string> words)
    {
        foreach (var word in words) AddWord(word);
        _prefixes = new(CountPrefixes());
        for (var k = 0; k < _wordsByIndex.Count; k++) AddPrefixes(k);
    }

    public int Count => _words.Count;
    public IEnumerable<string> Words => _words;

    public void Add(string word)
    {
        if (AddWord(word)) AddPrefixes(_wordsByIndex.Count - 1);
    }

    private bool AddWord(string word)
    {
        if (word.Length == 0 || !_words.Add(word)) return false;
        _wordsByIndex.Add(word);
        if (!_byFirstLetter.TryGetValue(word[0], out var bucket)) _byFirstLetter[word[0]] = bucket = [];
        bucket.Add(word);
        return true;
    }

    private void AddPrefixes(int index)
    {
        var word = _wordsByIndex[index];
        for (var i = 1; i <= word.Length; i++)
        {
            var prefix = word.AsSpan(0, i);
            var hash = HashIndex.Of(prefix);
            if (HasPrefix(prefix, hash)) continue;
            if (_prefixes.IsFull) _prefixes.Grow(entry => HashIndex.Of(PrefixAt(entry)));
            _prefixes.Add(hash, (long)index << 32 | (uint)i);
        }
    }

    /// <summary>頭 (語そのものを含む) の数。並べると、ある語で初めて出る頭は、直前の語と共通する長さより長いものだけ。</summary>
    private int CountPrefixes()
    {
        var sorted = new List<string>(_wordsByIndex);
        sorted.Sort(StringComparer.Ordinal);
        var count = 0;
        for (var k = 0; k < sorted.Count; k++) count += sorted[k].Length - (k == 0 ? 0 : sorted[k].AsSpan().CommonPrefixLength(sorted[k - 1]));
        return count;
    }

    public bool ContainsWord(string text) => _words.Contains(text);
    internal bool ContainsWord(ReadOnlySpan<char> text) => _words.GetAlternateLookup<ReadOnlySpan<char>>().Contains(text);

    /// <summary>text で始まる単語があるか (text 自体が単語の場合も true)。</summary>
    public bool HasPrefix(string text) => HasPrefix(text.AsSpan());
    internal bool HasPrefix(ReadOnlySpan<char> text) => HasPrefix(text, HashIndex.Of(text));

    private bool HasPrefix(ReadOnlySpan<char> text, int hash)
    {
        foreach (var entry in _prefixes.Candidates(hash))
        {
            if (PrefixAt(entry).SequenceEqual(text)) return true;
        }
        return false;
    }

    private ReadOnlySpan<char> PrefixAt(long entry) => _wordsByIndex[(int)(entry >> 32)].AsSpan(0, (int)(uint)entry);

    public IReadOnlyList<string> WordsStartingWith(char first) =>
        _byFirstLetter.TryGetValue(first, out var bucket) ? bucket : [];
}
