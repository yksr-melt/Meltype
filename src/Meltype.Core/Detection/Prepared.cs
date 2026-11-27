// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Text;

namespace Meltype.Detection;

/// <summary>
/// Segment 1 回分の units の、原文・かなの連結と各単位の位置。Unreadable は、ローマ字として読めなかった英字の単位 (zoom の m) の数の累積。
/// </summary>
internal sealed record Prepared(int[] Unreadable, string Raw, int[] RawOffsets, string Kana, int[] KanaOffsets)
{
    /// <summary>
    /// かなのすぐ後ろに続く w の単位 (きた|w|w): 笑いとして w のまま残したもの。ローマ字として読めなかった英字 (zoom の m) とは違うので、
    /// 英単語の根拠にしない (kiyagat|ta|w の ta + w を英単語 taw にしていた)。
    /// </summary>
    private static bool IsLaughter(IReadOnlyList<CompositionUnit> units, int k)
    {
        var i = k;
        while (i >= 0 && units[i].Raw is "w" or "W" && units[i].Kana == units[i].Raw) i--;
        return i < k && i >= 0 && units[i].Kana is [var kana, ..] && kana is >= 'ぁ' and <= 'ヺ' &&
               Enumerable.Range(k + 1, units.Count - k - 1).TakeWhile(j => units[j].Raw.Length > 0 && char.IsAsciiLetter(units[j].Raw[0])).All(j => units[j].Raw is "w" or "W");
    }

    /// <summary>この Segment 呼び出しの units で、原文の連結と位置を用意する (Segment の先頭で必ず呼ぶ)。</summary>
    public static Prepared Of(IReadOnlyList<CompositionUnit> units)
    {
        var unreadable = new int[units.Count + 1];
        for (var i = 0; i < units.Count; i++)
            unreadable[i + 1] = unreadable[i] + (units[i] is { Raw.Length: 1 } unit && unit.Kana == unit.Raw &&
                char.IsAsciiLetter(unit.Raw[0]) && !IsLaughter(units, i) ? 1 : 0);
        var raw = new StringBuilder();
        var rawOffsets = new int[units.Count + 1];
        var kana = new StringBuilder();
        var kanaOffsets = new int[units.Count + 1];
        for (var i = 0; i < units.Count; i++)
        {
            rawOffsets[i] = raw.Length;
            raw.Append(units[i].Raw);
            kanaOffsets[i] = kana.Length;
            kana.Append(units[i].Kana);
        }
        rawOffsets[units.Count] = raw.Length;
        kanaOffsets[units.Count] = kana.Length;
        return new Prepared(unreadable, raw.ToString(), rawOffsets, kana.ToString(), kanaOffsets);
    }

    /// <summary>start 番目から end 番目の手前までの単位のかな。</summary>
    public string KanaRange(int start, int end) => Kana[KanaOffsets[start]..KanaOffsets[end]];

    /// <summary>start 番目から end 番目の手前までの単位の原文。</summary>
    public string RawRange(int start, int end) => Raw[RawOffsets[start]..RawOffsets[end]];
}
