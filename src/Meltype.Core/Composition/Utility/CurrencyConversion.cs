// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text.RegularExpressions;

namespace Meltype.Composition.Utility;

/// <summary>
/// 金額と通貨の読み (100どる、1まんえんをゆーろ) に、別の通貨に換算した額を出す (issue #342)。
/// レートは同梱した ECB の参照レート (ExchangeRates。リリースのときのもの) で、今のレートに見えないように、候補にレートの日付を付ける。
/// 通貨だけ (100どる) なら円に、円 (1000えん) ならドルとユーロにする。「〜を〜」で換算先を決められる。
/// </summary>
internal sealed partial class CurrencyConversion : IUtilityConversion
{
    /// <summary>通貨の読み → 通貨コード・書き方。長い読みを先に (かなだどる を どる より先に)。</summary>
    internal static readonly (string Reading, string Code, string Unit)[] Currencies =
    [
        ("おーすとらりあどる", "AUD", "豪ドル"), ("しんがぽーるどる", "SGD", "シンガポールドル"), ("じんみんげん", "CNY", "人民元"),
        ("ほんこんどる", "HKD", "香港ドル"), ("すいすふらん", "CHF", "スイスフラン"), ("かなだどる", "CAD", "カナダドル"),
        ("ごうどる", "AUD", "豪ドル"), ("べいどる", "USD", "米ドル"), ("ゆーろ", "EUR", "ユーロ"), ("ぽんど", "GBP", "ポンド"),
        ("うぉん", "KRW", "ウォン"), ("ばーつ", "THB", "バーツ"), ("るぴー", "INR", "ルピー"), ("どる", "USD", "ドル"),
        ("げん", "CNY", "元"), ("えん", "JPY", "円"),
    ];

    private static readonly string Names = string.Join("|", Currencies.Select(c => c.Reading));

    [GeneratedRegex(@"^(?<n>[0-9０-９]{1,12}(?:[.．][0-9０-９]{1,4})?)(?<man>まん|おく)?(?<from>[ぁ-んー]+?)(?:を(?<to>[ぁ-んー]+?)(?:に)?)?$")]
    private static partial Regex AmountReading();

    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.Reading is not { } reading || Parse(reading) is not { } parsed) return null;
        if (ExchangeRates.Bundled is not { } rates) return null;
        var candidates = Candidates(parsed.Amount, parsed.From, parsed.To, rates);
        return candidates.Count > 0 ? new(candidates, UtilityPlacement.Append) : null;
    }

    /// <summary>金額と通貨の読みなら、金額・換算元・換算先 (決めていなければ null)。読みが合わなければ null。</summary>
    internal static (decimal Amount, (string Code, string Unit) From, (string Code, string Unit)? To)? Parse(string reading)
    {
        if (AmountReading().Match(reading) is not { Success: true } match) return null;
        if (Currency(match.Groups["from"].Value) is not { } from) return null;
        (string, string)? to = null;
        if (match.Groups["to"].Success)
        {
            if (Currency(match.Groups["to"].Value) is not { } target || target.Code == from.Code) return null;
            to = target;
        }
        var digits = new string(match.Groups["n"].Value.Select(c => c switch { >= '０' and <= '９' => (char)(c - '０' + '0'), '．' => '.', _ => c }).ToArray());
        if (!decimal.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) return null;
        amount *= match.Groups["man"].Value switch { "まん" => 10_000m, "おく" => 100_000_000m, _ => 1m };
        return (amount, from, to);
    }

    private static (string Code, string Unit)? Currency(string reading) =>
        Currencies.FirstOrDefault(c => c.Reading == reading) is { Code: not null } c ? (c.Code, c.Unit) : null;

    /// <summary>換算した額の候補 (レートの日付つきと、額だけ)。換算先を決めていなければ、円以外は円に、円はドルとユーロに。</summary>
    internal static List<string> Candidates(decimal amount, (string Code, string Unit) from, (string Code, string Unit)? to, ExchangeRates.Snapshot rates)
    {
        var targets = to is { } target ? [target] : from.Code == "JPY" ? new[] { ("USD", "ドル"), ("EUR", "ユーロ") } : [("JPY", "円")];
        var date = rates.Date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var candidates = new List<string>();
        foreach (var (code, unit) in targets)
        {
            if (rates.Convert(amount, from.Code, code) is not { } value) continue;
            var text = $"約{Format(value)}{unit}";
            candidates.Add($"{text}（{date} のレート）");
            candidates.Add(text);
        }
        return candidates;
    }

    /// <summary>
    /// 額を有効数字 2 桁に丸め、1 万以上は 万・億・兆 で書く (15,923 → 1.6万、62.8 → 63)。
    /// 古いレートの概算なので、1 の位まで出して精度があるように見せない (#1871)。
    /// </summary>
    internal static string Format(decimal value)
    {
        if (value <= 0) return "0";
        var drop = (int)Math.Floor(Math.Log10((double)value)) + 1 - SignificantDigits;
        var rounded = drop <= 0
            ? Math.Round(value, -drop, MidpointRounding.AwayFromZero)
            : Math.Round(value / Pow10(drop), MidpointRounding.AwayFromZero) * Pow10(drop);
        var (size, suffix) = LargeUnits.FirstOrDefault(u => rounded >= u.Size, (Size: 1m, Suffix: ""));
        return (rounded / size).ToString("#,0.##########", CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>大きい額の単位。大きい順。</summary>
    private static readonly (decimal Size, string Suffix)[] LargeUnits =
        [(1_000_000_000_000m, "兆"), (100_000_000m, "億"), (10_000m, "万")];

    private const int SignificantDigits = 2;

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++) result *= 10;
        return result;
    }
}
