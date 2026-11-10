// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Meltype.Composition;

/// <summary>
/// GitHub の Issue・Pull Request のタイトルを調べる (#123 を変換したときの候補。issue #291)。
/// 調べるのは「リポジトリ + 番号」だけで、打った文字は送らない。結果はこのセッションの間だけ覚え、ファイルには書かない。
/// 変換をあまり待たせないよう、待つのは <see cref="Timeout"/> まで。非公開のリポジトリは環境変数 MELTYPE_GITHUB_TOKEN のトークンで読む。
/// </summary>
public sealed class GitHubIssueTitles
{
    private sealed class Entry(Task<string?> task, long started)
    {
        public Task<string?> Task { get; } = task;
        public long Started { get; } = started;

        /// <summary>もう待ったか。待つのは最初の 1 回だけ (UI スレッドで、打鍵のたびに待たない)。</summary>
        public bool Waited { get; set; }
    }

    private readonly ConcurrentDictionary<(string Repository, int Number), Entry> _cache = new();
    private readonly Func<long> _now;

    /// <summary>調べられなかった (404・レート制限・ネットワークの失敗) 番号を、もう一度調べるまでの時間。ずっと覚えて二度と調べない、ということはしない。</summary>
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromSeconds(60);
    private readonly Func<string, int, Task<string?>> _fetch;

    /// <summary>1 回の問い合わせを待つ長さ。</summary>
    /// 最初の問い合わせは HTTPS の接続から始まり 300ms では間に合わないことが多いので、少し長めに待つ (issue #866)。
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(1500);

    public GitHubIssueTitles(Func<string, int, Task<string?>>? fetch = null, Func<long>? now = null)
    {
        _fetch = fetch ?? FetchFromGitHub;
        _now = now ?? (() => Environment.TickCount64);
    }

    /// <summary>repository は「所有者/名前」。タイトルが分かれば返す (調べて待つのは Timeout まで)。無い・調べられない・時間切れは null。</summary>
    public string? Lookup(string repository, int number)
    {
        repository = Normalize(repository);
        if (!IsRepository(repository) || number <= 0) return null;
        var key = (repository, number);
        var entry = _cache.GetOrAdd(key, k => new Entry(Task.Run(() => _fetch(k.Repository, k.Number)), _now()));
        try
        {
            if (entry.Task.IsCompleted)
            {
                if (!entry.Task.IsFaulted && entry.Task.Result is { } title) return title;
                // 調べられなかった番号は、しばらくしたらもう一度調べる (レート制限や一時的な失敗を、ずっと覚えない)
                if (_now() - entry.Started >= RetryAfter.TotalMilliseconds) _cache.TryRemove(new KeyValuePair<(string, int), Entry>(key, entry));
                return null;
            }
            // まだ調べている途中: 最初の 1 回だけ Timeout まで待つ。次からは待たずに null (終わっていれば、次の変換で出る)
            if (entry.Waited) return null;
            entry.Waited = true;
            return entry.Task.Wait(Timeout) && !entry.Task.IsFaulted ? entry.Task.Result : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 設定に書かれたリポジトリを「所有者/名前」にそろえる (issue #866)。前後の空白、
    /// https://github.com/所有者/名前 の URL、github.com/所有者/名前、末尾の / や .git も受け付ける。
    /// </summary>
    public static string Normalize(string? text)
    {
        var value = (text ?? "").Trim().TrimEnd('/');
        foreach (var prefix in (string[])["https://", "http://"])
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) value = value[prefix.Length..];
        }
        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) value = value[4..];
        if (value.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)) value = value["github.com/".Length..];
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        // URL の続き (/issues/123 など) は捨てる
        return value.Split('/') is [var owner, var name, ..] ? $"{owner.Trim()}/{name.Trim()}" : value;
    }

    /// <summary>「所有者/名前」の形か (英数字と - _ . だけ)。</summary>
    public static bool IsRepository(string text) =>
        text.Split('/') is [{ Length: > 0 } owner, { Length: > 0 } name] && (owner + name).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Meltype", "1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static async Task<string?> FetchFromGitHub(string repository, int number)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/issues/{number}");
        if (Environment.GetEnvironmentVariable("MELTYPE_GITHUB_TOKEN") is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Client.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
    }
}
