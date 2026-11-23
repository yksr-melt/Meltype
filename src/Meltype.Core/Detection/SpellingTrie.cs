// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>
/// 綴り → かな の表を、1 文字ずつ進む状態遷移 (トライ木) にしたもの。作った後は変わらない。
/// 部分文字列を切り出して表を引く代わりに文字を 1 つずつ辿るので、最長一致と「綴りの打ちかけか」が同じ 1 回の走査で分かる。
/// </summary>
internal sealed class SpellingTrie
{
    private sealed class Node
    {
        public readonly Dictionary<char, Node> Next = [];
        public string? Kana;
    }

    private readonly Node _root = new();

    public SpellingTrie(IEnumerable<KeyValuePair<string, string>> spellings)
    {
        foreach (var (spelling, kana) in spellings)
        {
            var node = _root;
            foreach (var c in spelling)
            {
                if (!node.Next.TryGetValue(c, out var next)) node.Next[c] = next = new Node();
                node = next;
            }
            node.Kana = kana;
        }
    }

    /// <summary>text の start から始まる、いちばん長い綴りの長さとかな。無ければ長さ 0。</summary>
    public (int Length, string? Kana) Longest(string text, int start)
    {
        var (length, kana) = (0, (string?)null);
        var node = _root;
        for (var i = start; i < text.Length && node.Next.TryGetValue(text[i], out node); i++)
        {
            if (node.Kana is not null) (length, kana) = (i - start + 1, node.Kana);
        }
        return (length, kana);
    }

    /// <summary>text の start から末尾までが、ある綴りの途中まで (それより長い綴りがある) か。綴りとして完成していても続きがあれば true。</summary>
    public bool IsPrefix(string text, int start) => Walk(text, start) is { Next.Count: > 0 };

    /// <summary>text の start から末尾までが綴りの途中までで、それ自体は綴りでない (続きを待つしかない) か。</summary>
    public bool IsOnlyPrefix(string text, int start) => Walk(text, start) is { Next.Count: > 0, Kana: null };

    private Node? Walk(string text, int start)
    {
        var node = _root;
        for (var i = start; i < text.Length; i++)
        {
            if (!node.Next.TryGetValue(text[i], out node)) return null;
        }
        return node;
    }
}
