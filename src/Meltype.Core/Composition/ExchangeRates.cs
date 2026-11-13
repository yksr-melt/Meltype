// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Meltype.Composition;

/// <summary>
/// 通貨の換算に使う為替レート (issue #342)。欧州中央銀行 (ECB) の参照レートを使う。
/// 同梱した表 (リリースのときに tools/make-exchange-rates.mjs で作る) と、ECB から取って保存した表のうち、日付の新しいほうを使う。
/// ECB から取るのは、設定「為替レートを毎回取得する」が ON のとき (変換のときに古ければ取る) と、手動で更新したとき (<see cref="UpdateAsync"/>) だけ。
/// </summary>
public sealed partial class ExchangeRates
{
    /// <summary>レートの表。Date は ECB のレートの日付、Rates は 1 ユーロあたりの各通貨の額 (EUR は 1)。</summary>
    public sealed record Snapshot(DateOnly Date, IReadOnlyDictionary<string, decimal> Rates)
    {
        /// <summary>amount (from の通貨) を to の通貨にした額。どちらかのレートが無ければ null。</summary>
        public decimal? Convert(decimal amount, string from, string to) =>
            Rates.TryGetValue(from, out var f) && Rates.TryGetValue(to, out var t) && f > 0 ? amount / f * t : null;

        /// <summary>exchange-rates.txt の形 (<see cref="Parse"/> で読める形) にする。</summary>
        public string Format() =>
            $"# Meltype 通貨の換算に使うレート (1 ユーロあたりの各通貨の額)\n# 出典: European Central Bank ({EcbUrl})\n# 日付: {Date:yyyy-MM-dd}\n"
            + string.Concat(Rates.Select(r => $"{r.Key}\t{r.Value.ToString(CultureInfo.InvariantCulture)}\n"));
    }

    private const string EcbUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";

    private static readonly Lazy<Snapshot?> SharedBundled = new(() => Parse(Detection.DictionarySource.ReadEmbedded("exchange-rates.txt")));

    /// <summary>同梱したレート。読めなければ null。</summary>
    public static Snapshot? Bundled => SharedBundled.Value;

    private readonly string? _path;
    private readonly Func<Task<string>> _fetch;
    private readonly Func<long> _now;
    private readonly Lazy<Snapshot?> _loaded;
    private readonly object _lock = new();
    private Snapshot? _fetched;
    private Task? _task;
    private long _started;
    private bool _waited;

    /// <summary>ECB はレートを 1 日 1 回更新するので、取ってからこの時間は取り直さない。</summary>
    public TimeSpan RefreshAfter { get; init; } = TimeSpan.FromHours(1);

    /// <summary>取りに行ったときに変換を待たせる長さ。待つのは取り始めた最初の 1 回だけで、間に合わなければ手元の表で出す。</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <param name="path">取った表を保存するファイル。null なら保存しない (テスト)。</param>
    /// <param name="fetch">ECB の eurofxref-daily.xml を返す。null なら ECB から取る。</param>
    public ExchangeRates(string? path, Func<Task<string>>? fetch = null, Func<long>? now = null)
    {
        _path = path;
        _fetch = fetch ?? FetchFromEcb;
        _now = now ?? (() => Environment.TickCount64);
        _loaded = new(() => _path is not null && File.Exists(_path) ? Parse(File.ReadAllText(_path)) : null);
    }

    /// <summary>今使うレート (同梱・保存した表の新しいほう)。online なら、古ければ ECB から取り直す。</summary>
    public Snapshot? Current(bool online)
    {
        if (online)
        {
            Task task;
            lock (_lock)
            {
                if (_task is null || (_task.IsCompleted && _now() - _started >= RefreshAfter.TotalMilliseconds))
                {
                    _task = Task.Run(() => UpdateAsync());
                    _started = _now();
                    _waited = false;
                }
                task = _task;
                if (task.IsCompleted || _waited) task = Task.CompletedTask;
                _waited = true;
            }
            // 取れなくても (オフライン・ECB の不調) 手元の表で換算する。失敗は次に古くなったときに取り直す
            try { task.Wait(Timeout); }
            catch (AggregateException e) when (e.InnerException is HttpRequestException or TaskCanceledException or FormatException) { }
        }
        return Newest(Newest(Bundled, _loaded.Value), Volatile.Read(ref _fetched));
    }

    /// <summary>ECB からレートを取って保存する (手動の更新・ON のときの取得)。取れなければ例外 (HttpRequestException など)。</summary>
    public async Task<Snapshot> UpdateAsync()
    {
        var snapshot = ParseEcb(await _fetch().ConfigureAwait(false)) ?? throw new FormatException("ECB のレートを読めません");
        Volatile.Write(ref _fetched, snapshot);
        if (_path is not null) File.WriteAllText(_path, snapshot.Format());
        return snapshot;
    }

    private static Snapshot? Newest(Snapshot? a, Snapshot? b) => a is null ? b : b is null || a.Date >= b.Date ? a : b;

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

    [GeneratedRegex(@"time=['""](\d{4}-\d{2}-\d{2})['""]")]
    private static partial Regex EcbDate();

    [GeneratedRegex(@"currency=['""]([A-Z]{3})['""]\s+rate=['""]([0-9.]+)['""]")]
    private static partial Regex EcbRate();

    /// <summary>ECB の eurofxref-daily.xml を読む (tools/make-exchange-rates.mjs と同じ読み方)。読めなければ null。</summary>
    public static Snapshot? ParseEcb(string xml)
    {
        if (EcbDate().Match(xml) is not { Success: true } date) return null;
        var lines = EcbRate().Matches(xml).Select(m => $"{m.Groups[1].Value}\t{m.Groups[2].Value}");
        return Parse($"# 日付: {date.Groups[1].Value}\nEUR\t1\n{string.Join("\n", lines)}") is { } s && s.Rates.ContainsKey("JPY") ? s : null;
    }

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Meltype", "1"));
        return client;
    }

    private static Task<string> FetchFromEcb() => Client.GetStringAsync(EcbUrl);
}
