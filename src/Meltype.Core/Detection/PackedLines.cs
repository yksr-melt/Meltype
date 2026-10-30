// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Eisenferik

namespace Meltype.Detection;

/// <summary>
/// タブ区切りの辞書を、本文と行の位置で保持する。行ごとの文字列は作らず、見出しをハッシュ表で引いて必要な欄を取り出す。
/// </summary>
internal sealed class PackedLines
{
    private readonly string _text;
    // 行の始まり (上位 24 ビット) と見出しの長さ (下位 8 ビット)。見出しの順に並べ、同じ見出しの行は本文の順。
    // 見出しを切り出すたびにタブを探さずに済む。
    private readonly int[] _lines;
    private readonly HashIndex<int> _index; // 見出しごとの最初の行の番号 + 1

    private PackedLines(string text, int[] lines)
    {
        (_text, _lines) = (text, lines);
        var firsts = new List<int>(); // 見出しごとの最初の行
        for (var k = 0; k < lines.Length; k += GroupAt(k).Count)
        {
            firsts.Add(k);
            MaxKeyLength = Math.Max(MaxKeyLength, Key(k).Length);
        }
        Count = firsts.Count;
        _index = new(Count);
        foreach (var k in firsts) _index.Add(HashIndex.Of(Key(k)), k + 1);
    }

    /// <summary>
    /// 意味・英訳辞書の本文から作る。空行と # で始まる行、タブ区切りで 3 欄に満たない行は飛ばす。
    /// </summary>
    public static PackedLines FromText(string text)
    {
        var lines = new List<(int Start, int KeyLength)>();
        for (var at = 0; at < text.Length;)
        {
            var end = text.IndexOf('\n', at);
            if (end < 0) end = text.Length;
            var line = text.AsSpan(at, end - at).TrimEnd('\r');
            if (line.Length > 0 && line[0] != '#' && line.Count('\t') >= 2) lines.Add((at, line.IndexOf('\t')));
            at = end + 1;
        }
        ReadOnlySpan<char> Key((int Start, int KeyLength) line) => text.AsSpan(line.Start, line.KeyLength);
        int Compare((int Start, int KeyLength) a, (int Start, int KeyLength) b)
        {
            var order = Key(a).SequenceCompareTo(Key(b));
            if (order != 0) return order;
            // 辞書に書かれた候補の優先順位を保つ
            return a.Start.CompareTo(b.Start);
        }
        lines.Sort(Compare);
        if (text.Length >= 1 << 24 || lines.Exists(line => line.KeyLength >= 1 << 8)) throw new ArgumentException("本文か見出しが長すぎます。", nameof(text));
        return new PackedLines(text, [.. lines.Select(line => line.Start << 8 | line.KeyLength)]);
    }

    /// <summary>見出しの数 (同じ見出しの行はまとめて 1 つ)。</summary>
    public int Count { get; }

    public int MaxKeyLength { get; }

    public int LineCount => _lines.Length;

    /// <summary>見出しの順で index 番目の行の見出し。</summary>
    public ReadOnlySpan<char> Key(int index) => _text.AsSpan(_lines[index] >>> 8, _lines[index] & 0xFF);

    /// <summary>index 番目の行が本文の何文字目から始まるか (行が本文のどちらに先に書いてあるかを比べる)。</summary>
    public int Position(int index) => _lines[index] >>> 8;

    /// <summary>見出しが key の行の範囲 (見出しの順での最初の番号と数)。無ければ数が 0。</summary>
    public (int First, int Count) Find(ReadOnlySpan<char> key)
    {
        foreach (var k in _index.Candidates(HashIndex.Of(key)))
        {
            if (Key(k - 1).SequenceEqual(key)) return GroupAt(k - 1);
        }
        return default;
    }

    /// <summary>同じ見出しの先頭行 first から、その見出しの行の範囲を返す。</summary>
    public (int First, int Count) GroupAt(int first)
    {
        var key = Key(first);
        var last = first + 1;
        while (last < _lines.Length && Key(last).SequenceEqual(key)) last++;
        return (first, last - first);
    }

    /// <summary>
    /// 見出しの順で index 番目の行の、タブで区切った n 番目 (1 から。見出しの次が 1) の欄 (行末の \r を除く)。無ければ空。
    /// 見出しの後から読むので、手前の欄だけを見るなら行の終わりまでは探さない。
    /// </summary>
    public ReadOnlySpan<char> Field(int index, int n)
    {
        // 見出しの後ろには必ずタブがある (3 欄に満たない行は FromText で飛ばしている)
        var rest = _text.AsSpan(Position(index) + Key(index).Length + 1);
        for (; n > 1; n--)
        {
            var end = rest.IndexOfAny('\t', '\n');
            if (end < 0 || rest[end] == '\n') return [];
            rest = rest[(end + 1)..];
        }
        var stop = rest.IndexOfAny('\t', '\n');
        return (stop < 0 ? rest : rest[..stop]).TrimEnd('\r');
    }
}
