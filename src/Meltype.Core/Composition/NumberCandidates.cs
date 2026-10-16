// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
using System.Text;

namespace Meltype.Composition;

/// <summary>
/// 数字だけの区間を変換するときの候補: 漢数字 (二〇二六・二千二十六・弐千弐拾六)。
/// 西暦に見える 4 桁は元号 (令和8年) も出す。
/// </summary>
public static class NumberCandidates
{
    private const string Digits = "〇一二三四五六七八九";
    private const string FormalDigits = "零壱弐参四五六七八九";
    private static readonly string[] SmallUnits = ["", "十", "百", "千"];
    private static readonly string[] FormalSmallUnits = ["", "拾", "百", "千"];
    private static readonly string[] BigUnits = ["", "万", "億", "兆"];

    /// <summary>数字だけの文字列 (全角でもよい) の候補。数字だけでなければ空。</summary>
    public static List<string> For(string text)
    {
        var half = text.Replace(",", "");
        half = string.Concat(half.Select(c => c is >= '０' and <= '９' ? (char)(c - '０' + '0') : c));
        if (half.Length is 0 or > 16 || !half.All(char.IsAsciiDigit)) return [];
        var result = new List<string> { string.Concat(half.Select(c => Digits[c - '0'])) };
        if (half.Length > 1 && half[0] == '0') return result;
        var value = ulong.Parse(half);
        if (value > 0)
        {
            result.Add(ToKanji(value, Digits, SmallUnits, omitOne: true));
            result.Add(ToKanji(value, FormalDigits, FormalSmallUnits, omitOne: false));
        }
        if (half.Length == 4 && ToEra((int)value) is { } era) result.Add(era);
        return result.Distinct().ToList();
    }

    /// <summary>西暦 → 元号 (明治 〜 令和)。範囲外は null。</summary>
    public static string? ToEra(int year)
    {
        (string Name, int Start)[] eras = [("令和", 2019), ("平成", 1989), ("昭和", 1926), ("大正", 1912), ("明治", 1868)];
        if (year > 2100) return null;
        foreach (var (name, start) in eras)
        {
            if (year < start) continue;
            var n = year - start + 1;
            return $"{name}{(n == 1 ? "元" : n.ToString())}年";
        }
        return null;
    }

    private static string ToKanji(ulong value, string digits, string[] units, bool omitOne)
    {
        var builder = new StringBuilder();
        var groups = new List<ulong>();
        for (var v = value; v > 0; v /= 10000) groups.Add(v % 10000);
        for (var group = groups.Count - 1; group >= 0; group--)
        {
            var g = (int)groups[group];
            if (g == 0) continue;
            for (var place = 3; place >= 0; place--)
            {
                var digit = g / (int)Math.Pow(10, place) % 10;
                if (digit == 0) continue;
                // 十・百・千 の前の 一 は省く (千二十六)。万・億 の前の 一 と一の位は省かない
                if (!(digit == 1 && place > 0 && omitOne)) builder.Append(digits[digit]);
                builder.Append(units[place]);
            }
            builder.Append(BigUnits[group]);
        }
        return builder.ToString();
    }
}
