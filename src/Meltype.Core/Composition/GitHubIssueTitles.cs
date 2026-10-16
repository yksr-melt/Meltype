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
    private readonly ConcurrentDictionary<(string Repository, int Number), Task<string?>> _cache = new();
    private readonly Func<string, int, Task<string?>> _fetch;

    /// <summary>1 回の問い合わせを待つ長さ。</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(800);

    public GitHubIssueTitles(Func<string, int, Task<string?>>? fetch = null) => _fetch = fetch ?? FetchFromGitHub;

    /// <summary>repository は「所有者/名前」。タイトルが分かれば返す (調べて待つのは Timeout まで)。無い・調べられない・時間切れは null。</summary>
    public string? Lookup(string repository, int number)
    {
        if (!IsRepository(repository) || number <= 0) return null;
        var task = _cache.GetOrAdd((repository, number), key => Task.Run(() => _fetch(key.Repository, key.Number)));
        try
        {
            return task.Wait(Timeout) ? task.Result : null;
        }
        catch (Exception)
        {
            // 失敗は覚えない (次の変換でもう一度調べる)
            _cache.TryRemove((repository, number), out _);
            return null;
        }
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
