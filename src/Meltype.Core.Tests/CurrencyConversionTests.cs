// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Composition.Utility;

namespace Meltype.Tests;

/// <summary>通貨の換算 (設定「通貨を換算した候補を出す」。issue #342)。</summary>
internal static class CurrencyConversionTests
{
    private const string Xml = """
        <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
          <Cube><Cube time='2026-11-11'>
            <Cube currency='USD' rate='1.1127'/>
            <Cube currency='JPY' rate='177.18'/>
            <Cube currency='GBP' rate='0.83618'/>
            <Cube currency='KRW' rate='1600.5'/>
          </Cube></Cube>
        </gesmes:Envelope>
        """;

    private static ExchangeRates.Snapshot Rates => ExchangeRates.Parse(Xml, new DateTime(2026, 11, 11, 16, 0, 0, DateTimeKind.Utc))!;

    [Test]
    public static void Parse_ReadsTheEcbReferenceRates()
    {
        var rates = Rates;
        Assert.Equal(new DateOnly(2026, 11, 11), rates.Date);
        Assert.Equal(1m, rates.Rates["EUR"], "ユーロは 1");
        Assert.Equal(177.18m, rates.Rates["JPY"]);
        Assert.True(ExchangeRates.Parse("<html>メンテナンス中</html>", DateTime.UtcNow) is null, "読めないものは使わない");
    }

    [Test]
    public static void Candidates_ConvertWithTheRateDate()
    {
        // 100 ドル = 100 / 1.1127 * 177.18 = 15,923.42… 円
        Assert.Equal("約15,923円（2026/11/11 のレート）|約15,923円",
            string.Join("|", CurrencyConversion.Candidates(100, ("USD", "ドル"), null, Rates)));
        // 円はドルとユーロに
        Assert.Equal("約62.8ドル（2026/11/11 のレート）|約62.8ドル|約56.44ユーロ（2026/11/11 のレート）|約56.44ユーロ",
            string.Join("|", CurrencyConversion.Candidates(10000, ("JPY", "円"), null, Rates)));
        // 換算先を決めたとき
        Assert.Equal("約56.44ユーロ（2026/11/11 のレート）|約56.44ユーロ",
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
    public static void Rates_AreRememberedAndUsedOffline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-rates-{Guid.NewGuid():N}.json");
        try
        {
            var now = new DateTime(2026, 11, 11, 16, 0, 0, DateTimeKind.Utc);
            var online = new ExchangeRates(path, () => Task.FromResult<string?>(Xml), () => now);
            Assert.Equal(177.18m, online.Current(TimeSpan.FromSeconds(5))?.Rates["JPY"], "読めたら使う");
            Assert.True(SpinWait.SpinUntil(() => File.Exists(path), 5000), "ファイルに覚える");

            // 次の日、ネットにつながらない: 最後に読めたレート (日付もそのまま) を使う
            var offline = new ExchangeRates(path, () => throw new HttpRequestException("つながりません"), () => now.AddDays(1));
            var remembered = offline.Current(TimeSpan.FromSeconds(5));
            Assert.Equal(new DateOnly(2026, 11, 11), remembered?.Date, "覚えたレート");
            Assert.Equal(177.18m, remembered?.Rates["JPY"]);

            // 一度も読めていなければ出さない
            var never = new ExchangeRates(null, () => Task.FromResult<string?>(null), () => now);
            Assert.True(never.Current(TimeSpan.FromSeconds(5)) is null, "読めたことが無い");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Conversion_IsOffByDefault()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("100doru ");
        Assert.True(!k.Host.View!.Candidates.Any(c => c.StartsWith('約')), "既定 (OFF) では換算しない");
    }
}
