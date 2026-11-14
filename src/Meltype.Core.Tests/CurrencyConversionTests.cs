// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Composition.Utility;

namespace Meltype.Tests;

/// <summary>通貨の換算 (100どる → 約1.6万円。issue #342)。</summary>
internal static class CurrencyConversionTests
{
    private const string Table = """
        # Meltype 通貨の換算に使うレート (1 ユーロあたりの各通貨の額)
        # 日付: 2026-11-11
        EUR	1
        USD	1.1127
        JPY	177.18
        GBP	0.83618
        KRW	1600.5
        """;

    private static ExchangeRates.Snapshot Rates => ExchangeRates.Parse(Table)!;

    [Test]
    public static void Parse_ReadsTheBundledTable()
    {
        var rates = Rates;
        Assert.Equal(new DateOnly(2026, 11, 11), rates.Date);
        Assert.Equal(1m, rates.Rates["EUR"], "ユーロは 1");
        Assert.Equal(177.18m, rates.Rates["JPY"]);
        Assert.True(ExchangeRates.Parse("EUR\t1\nJPY\t177.18\n") is null, "日付が無ければ使わない");
        // 同梱した表 (tools/make-exchange-rates.mjs で作ったもの) を読める
        Assert.True(ExchangeRates.Bundled is { } bundled && bundled.Rates.ContainsKey("JPY") && bundled.Rates.ContainsKey("USD"), "同梱した表");
    }

    [Test]
    public static void Candidates_ConvertWithTheRateDate()
    {
        // 100 ドル = 100 / 1.1127 * 177.18 = 15,923.42… 円。有効数字 2 桁に丸める
        Assert.Equal("約1.6万円（2026/11/11 のレート）|約1.6万円",
            string.Join("|", CurrencyConversion.Candidates(100, ("USD", "ドル"), null, Rates)));
        // 円はドルとユーロに
        Assert.Equal("約63ドル（2026/11/11 のレート）|約63ドル|約56ユーロ（2026/11/11 のレート）|約56ユーロ",
            string.Join("|", CurrencyConversion.Candidates(10000, ("JPY", "円"), null, Rates)));
        // 換算先を決めたとき
        Assert.Equal("約56ユーロ（2026/11/11 のレート）|約56ユーロ",
            string.Join("|", CurrencyConversion.Candidates(10000, ("JPY", "円"), ("EUR", "ユーロ"), Rates)));
        Assert.Equal(0, CurrencyConversion.Candidates(100, ("THB", "バーツ"), null, Rates).Count, "レートの無い通貨は出さない");
    }

    [Test]
    public static void Parse_ReadsAmountAndCurrencies()
    {
        Assert.True(CurrencyConversion.Parse("100どる") is { Amount: 100, From.Code: "USD", To: null }, "通貨だけ");
        Assert.True(CurrencyConversion.Parse("1まんえんをゆーろ") is { Amount: 10000, From.Code: "JPY", To.Code: "EUR" }, "まん・換算先");
        Assert.True(CurrencyConversion.Parse("10000えんをどるに") is { From.Code: "JPY", To.Code: "USD" }, "〜に");
        Assert.True(CurrencyConversion.Parse("5かなだどる") is { From.Code: "CAD" }, "長い読みを先に");
        Assert.True(CurrencyConversion.Parse("１２．５ぽんど") is { Amount: 12.5m, From.Code: "GBP" }, "全角の数字");
        Assert.True(CurrencyConversion.Parse("100にん") is null, "通貨でない");
        Assert.True(CurrencyConversion.Parse("どる") is null, "金額が無い");
        Assert.True(CurrencyConversion.Parse("100えんをえん") is null, "同じ通貨");
    }

    [Test]
    public static void Conversion_UsesTheBundledRates()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("100doru ");
        var date = ExchangeRates.Bundled!.Date.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(k.Host.View!.Candidates.Any(c => c.StartsWith('約') && c.EndsWith($"円（{date} のレート）", StringComparison.Ordinal)), string.Join(" ", k.Host.View.Candidates));
    }

    [Test]
    public static void Format_RoundsToTwoSignificantDigits()
    {
        // 古いレートの概算なので、精度があるように見せない
        Assert.Equal("1.6万", CurrencyConversion.Format(15923.42m));
        Assert.Equal("63", CurrencyConversion.Format(62.8m));
        Assert.Equal("1,000", CurrencyConversion.Format(999.6m), "繰り上がり");
        Assert.Equal("1,200万", CurrencyConversion.Format(12345678m));
        Assert.Equal("1.2億", CurrencyConversion.Format(123456789m));
        Assert.Equal("1.6兆", CurrencyConversion.Format(1_592_342_000_000m), "100おくどる");
        Assert.Equal("0.063", CurrencyConversion.Format(0.0634m), "1 より小さい額");
    }
}
