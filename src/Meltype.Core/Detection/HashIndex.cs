// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Eisenferik

using System.Numerics;

namespace Meltype.Detection;

internal static class HashIndex
{
    /// <summary>文字列のハッシュ。UTF-16 コード単位で FNV-1a を計算し、上位ビットを下位にも混ぜる。</summary>
    public static int Of(ReadOnlySpan<char> text)
    {
        var hash = 2166136261u;
        foreach (var c in text) hash = (hash ^ c) * 16777619u;
        return (int)(hash ^ (hash >> 15));
    }
}

/// <summary>
/// 文字列を表す値 (語の番号など。default は空き) を、その文字列のハッシュで引く表。表は文字列を持たず、
/// 値から文字列を取り出して比べるのは呼ぶ側。線形探索で衝突を解決し、検索の終端になる空きを必ず残す。
/// </summary>
internal sealed class HashIndex<T> where T : unmanaged, IEquatable<T>
{
    private T[] _slots;
    private int _count;

    /// <summary>count 個入れても 3/4 までしか埋まらない大きさで作る。</summary>
    public HashIndex(int count) => _slots = new T[(int)Math.Max(4, BitOperations.RoundUpToPowerOf2((uint)(count + count / 3 + 1)))];

    /// <summary>もう 1 つ入れると 3/4 を超えるか。超えるなら Add の前に Grow する。</summary>
    public bool IsFull => (_count + 1) * 4 > _slots.Length * 3;

    public void Add(int hash, T value)
    {
        Place(_slots, hash, value);
        _count++;
    }

    /// <summary>表を倍の大きさにする。hashOf は値が表す文字列のハッシュ。</summary>
    public void Grow(Func<T, int> hashOf)
    {
        var slots = new T[_slots.Length * 2];
        foreach (var value in _slots) if (!value.Equals(default)) Place(slots, hashOf(value), value);
        _slots = slots;
    }

    private static void Place(T[] slots, int hash, T value)
    {
        var mask = slots.Length - 1;
        var slot = hash & mask;
        while (!slots[slot].Equals(default)) slot = (slot + 1) & mask;
        slots[slot] = value;
    }

    /// <summary>ハッシュが hash の文字列を表すかもしれない値 (ハッシュの位置から空きまで)。</summary>
    public Enumerator Candidates(int hash) => new(_slots, hash);

    public ref struct Enumerator(T[] slots, int hash)
    {
        private readonly int _mask = slots.Length - 1;
        private int _slot = (hash - 1) & (slots.Length - 1);

        public readonly T Current => slots[_slot];

        public bool MoveNext()
        {
            _slot = (_slot + 1) & _mask;
            return !slots[_slot].Equals(default);
        }

        public readonly Enumerator GetEnumerator() => this;
    }
}
