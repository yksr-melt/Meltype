// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;

namespace Meltype.Composition.Utility;

/// <summary>
/// 数字だけの入力に、漢数字 (二〇二六・二千二十六・弐千弐拾六) を出す (issue #344)。
/// Mozc も同じ候補を出すが、Mac の azooKey は打った数字を漢数字にしないので、どの OS でも出るようにここで出す。
/// 選んだ漢数字は学習する (1 → 一 を選んだら次も 一: issue #672)。
/// </summary>
internal sealed class KanjiNumberConversion : IUtilityConversion
{
    private const string Digits = "〇一二三四五六七八九";
    private const string FormalDigits = "零壱弐参四五六七八九";
    private static readonly string[] SmallUnits = ["", "十", "百", "千"];
    private static readonly string[] FormalSmallUnits = ["", "拾", "百", "千"];
    private static readonly string[] BigUnits = ["", "万", "億", "兆"];

    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading is { } reading && Candidates(reading) is { Count: > 0 } candidates
            ? new(candidates, UtilityPlacement.Append) { Learnable = true }
            : null;

    /// <summary>数字だけの文字列 (全角・3 桁ごとの , があってもよい) の漢数字。数字だけでなければ空。</summary>
    internal static List<string> Candidates(string text)
    {
        var half = string.Concat(text.Replace(",", "").Select(c => c is >= '０' and <= '９' ? (char)(c - '０' + '0') : c));
        if (half.Length is 0 or > 16 || !half.All(char.IsAsciiDigit)) return [];
        var result = new List<string> { string.Concat(half.Select(c => Digits[c - '0'])) };
        // 先頭が 0 の数字 (001) は位取りしない
        if (half.Length > 1 && half[0] == '0') return result;
        var value = ulong.Parse(half, CultureInfo.InvariantCulture);
        if (value > 0)
        {
            result.Add(ToKanji(value, Digits, SmallUnits, omitOne: true));
            result.Add(ToKanji(value, FormalDigits, FormalSmallUnits, omitOne: false));
        }
        return result.Distinct().ToList();
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
