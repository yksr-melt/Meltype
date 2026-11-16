// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;

namespace Meltype.Composition;

/// <summary>
/// 通貨の換算に使う為替レート (issue #342)。欧州中央銀行 (ECB) の参照レートを、
/// リリースのときに tools/make-exchange-rates.mjs で dictionaries/exchange-rates.txt に書いて同梱する。使うときにネットにはつながない。
/// </summary>
public static class ExchangeRates
{
    /// <summary>レートの表。Date は ECB のレートの日付、Rates は 1 ユーロあたりの各通貨の額 (EUR は 1)。</summary>
    public sealed record Snapshot(DateOnly Date, IReadOnlyDictionary<string, decimal> Rates)
    {
        /// <summary>amount (from の通貨) を to の通貨にした額。どちらかのレートが無ければ null。</summary>
        public decimal? Convert(decimal amount, string from, string to) =>
            Rates.TryGetValue(from, out var f) && Rates.TryGetValue(to, out var t) && f > 0 ? amount / f * t : null;
    }

    private static readonly Lazy<Snapshot?> SharedBundled = new(() => Parse(Detection.DictionarySource.ReadEmbedded("exchange-rates.txt")));

    /// <summary>同梱したレート。読めなければ null。</summary>
    public static Snapshot? Bundled => SharedBundled.Value;

    /// <summary>exchange-rates.txt (「# 日付: yyyy-MM-dd」の行と、「通貨コード&lt;Tab&gt;レート」の行) を読む。読めなければ null。</summary>
    public static Snapshot? Parse(string text)
    {
        DateOnly? date = null;
        var rates = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("# 日付:", StringComparison.Ordinal))
            {
                if (DateOnly.TryParseExact(line["# 日付:".Length..].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) date = day;
                continue;
            }
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.Split('\t') is [{ Length: 3 } code, var value] && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) && rate > 0)
                rates[code] = rate;
        }
        return date is { } d && rates.Count > 1 ? new Snapshot(d, rates) : null;
    }
}
