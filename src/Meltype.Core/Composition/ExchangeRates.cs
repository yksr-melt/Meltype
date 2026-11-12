// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Meltype.Composition;

/// <summary>
/// 通貨の換算に使う為替レート (設定「通貨を換算した候補を出す」。issue #342)。欧州中央銀行 (ECB) が毎日公開している参照レート
/// (eurofxref-daily.xml。ユーロに対するレート) を読み、ファイルに覚える。打った文字は送らない (ファイルを読むだけ)。
/// ネットにつながらない・読めないときは、最後に読めたレートを使う。読むのは、覚えたレートが古いとき (12 時間) だけ。
/// </summary>
public sealed partial class ExchangeRates
{
    /// <summary>レートを読み直すまでの時間 (ECB は営業日ごとに 1 回更新する)。</summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(12);

    /// <summary>読めなかったとき、次に読みに行くまでの時間 (ネットにつながらない間、変換のたびに読みに行かない)。</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    /// <summary>レートを覚えたもの。Date は ECB のレートの日付、Rates は 1 ユーロあたりの各通貨の額 (EUR は 1)。</summary>
    public sealed record Snapshot(DateOnly Date, DateTime FetchedUtc, IReadOnlyDictionary<string, decimal> Rates)
    {
        /// <summary>amount (from の通貨) を to の通貨にした額。どちらかのレートが無ければ null。</summary>
        public decimal? Convert(decimal amount, string from, string to) =>
            Rates.TryGetValue(from, out var f) && Rates.TryGetValue(to, out var t) && f > 0 ? amount / f * t : null;
    }

    private readonly string? _path;
    private readonly Func<Task<string?>> _fetch;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private Snapshot? _current;
    private bool _loaded;
    private Task? _refreshing;
    private DateTime _nextTry = DateTime.MinValue;

    /// <param name="path">覚えるファイル (null なら覚えない)。</param>
    /// <param name="fetch">eurofxref-daily.xml の中身を読む (テスト用。null なら ECB から読む)。読めなければ null。</param>
    public ExchangeRates(string? path, Func<Task<string?>>? fetch = null, Func<DateTime>? utcNow = null)
    {
        _path = path;
        _fetch = fetch ?? FetchFromEcb;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>覚えた (か、最初に読めるまで少し待った) レート。古ければ裏で読み直す。一度も読めていなければ null。</summary>
    public Snapshot? Current(TimeSpan wait = default)
    {
        Task? refreshing;
        lock (_gate)
        {
            if (!_loaded)
            {
                _loaded = true;
                _current = Load();
            }
            var now = _utcNow();
            if ((_current is null || now - _current.FetchedUtc >= RefreshAfter) && now >= _nextTry && _refreshing is null)
            {
                _nextTry = now + RetryAfter;
                _refreshing = Task.Run(Refresh);
            }
            refreshing = _refreshing;
            if (_current is not null || refreshing is null || wait <= TimeSpan.Zero) return _current;
        }
        // 一度も読めていないときだけ、少し待つ (変換をあまり待たせない)
        try { refreshing.Wait(wait); } catch (AggregateException) { }
        lock (_gate) return _current;
    }

    private async Task Refresh()
    {
        try
        {
            if (await _fetch().ConfigureAwait(false) is { } xml && Parse(xml, _utcNow()) is { } snapshot)
            {
                lock (_gate) _current = snapshot;
                Save(snapshot);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log.Info($"為替レートを読めませんでした (覚えたレートを使います): {ex.Message}");
        }
        finally
        {
            lock (_gate) _refreshing = null;
        }
    }

    [GeneratedRegex(@"time=['""](?<date>\d{4}-\d{2}-\d{2})['""]")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"currency=['""](?<code>[A-Z]{3})['""]\s+rate=['""](?<rate>[0-9.]+)['""]")]
    private static partial Regex RatePattern();

    /// <summary>eurofxref-daily.xml を読む。読めなければ null。</summary>
    internal static Snapshot? Parse(string xml, DateTime fetchedUtc)
    {
        if (DatePattern().Match(xml) is not { Success: true } date || !DateOnly.TryParseExact(date.Groups["date"].Value, "yyyy-MM-dd", out var day)) return null;
        var rates = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["EUR"] = 1 };
        foreach (Match match in RatePattern().Matches(xml))
        {
            if (decimal.TryParse(match.Groups["rate"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) && rate > 0)
                rates[match.Groups["code"].Value] = rate;
        }
        return rates.Count > 1 ? new Snapshot(day, fetchedUtc, rates) : null;
    }

    private sealed record Stored(string Date, DateTime FetchedUtc, Dictionary<string, decimal> Rates);

    private Snapshot? Load()
    {
        if (_path is null || !File.Exists(_path)) return null;
        try
        {
            var stored = JsonSerializer.Deserialize(File.ReadAllText(_path), ExchangeRatesJsonContext.Default.Stored);
            return stored is not null && DateOnly.TryParseExact(stored.Date, "yyyy-MM-dd", out var day) && stored.Rates.Count > 1
                ? new Snapshot(day, DateTime.SpecifyKind(stored.FetchedUtc, DateTimeKind.Utc), stored.Rates) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log.Warn($"覚えた為替レートを読めませんでした: {ex.Message}");
            return null;
        }
    }

    private void Save(Snapshot snapshot)
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var stored = new Stored(snapshot.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), snapshot.FetchedUtc, new(snapshot.Rates));
            File.WriteAllText(_path, JsonSerializer.Serialize(stored, ExchangeRatesJsonContext.Default.Stored));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log.Warn($"為替レートを覚えられませんでした: {ex.Message}");
        }
    }

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Meltype", "1"));
        return client;
    }

    private static async Task<string?> FetchFromEcb()
    {
        using var response = await Client.GetAsync(new Uri("https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml")).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(Stored))]
    private sealed partial class ExchangeRatesJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
}
