// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>
/// 英語の固有名詞 (dictionaries/propernouns.txt)。小文字で引き、正しい大文字小文字の形 (GitHub, iPhone) を返す。
/// ローマ字としても読めてしまう固有名詞 (amazon, adobe) を英語として扱うのに使う。
/// </summary>
public sealed class ProperNouns
{
    private readonly Dictionary<string, string> _canonical = new(StringComparer.Ordinal);
    private readonly WordList _words = new();

    public static ProperNouns Load(string? userDirectory)
    {
        var nouns = new ProperNouns();
        nouns.AddText(DictionarySource.ReadEmbedded("propernouns.txt"));
        // Unicode CLDR の国・地域名の語 (tools/make-countries.mjs で作る。#1615)。
        // ワークアラウンド (#1869): ローマ字として最後まで読める語 (Oman・San・Mayotte) は入れない。
        // 固有名詞を語の頭でも英語と判定するので、日本語の語の頭とぶつかる (omanjuu → oman中)。
        // #1869 で判定を直したら、この絞り込みを外して国名の語をすべて入れる (#1615 の方針)。
        // それまで英語にしたい国名は、確かめて propernouns.txt に書く (Benin・Panama)
        var romaji = new RomajiDetector();
        nouns.AddText(DictionarySource.ReadEmbedded("countries.txt"), word => romaji.Analyze(word) is not { IsValid: true, Partial: "" or "n" });
        if (userDirectory is not null)
        {
            var path = Path.Combine(userDirectory, "propernouns.txt");
            try
            {
                if (File.Exists(path)) nouns.AddText(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"ユーザーの固有名詞辞書を読めませんでした: {ex.Message}");
            }
        }
        return nouns;
    }

    /// <param name="accept">小文字の語を入れるか。null ならすべて入れる。</param>
    public void AddText(string text, Func<string, bool>? accept = null)
    {
        foreach (var line in DictionarySource.LinesWithoutComments(text))
        {
            foreach (var word in line.Split([' ', '\t', '\r', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!word.All(char.IsAsciiLetterOrDigit)) continue;
                var lower = word.ToLowerInvariant();
                if (accept?.Invoke(lower) == false) continue;
                _canonical.TryAdd(lower, word);
                _words.Add(lower);
            }
        }
    }

    /// <summary>小文字の綴り (英語辞書に足す用)。</summary>
    public IEnumerable<string> LowercaseWords => _canonical.Keys;

    public bool Contains(string lower) => _canonical.ContainsKey(lower);
    internal bool Contains(ReadOnlySpan<char> lower) => _canonical.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(lower);

    public bool HasPrefix(string lower) => _words.HasPrefix(lower);
    internal bool HasPrefix(ReadOnlySpan<char> lower) => _words.HasPrefix(lower);

    /// <summary>正しい大文字小文字の形。固有名詞でなければ null。</summary>
    public string? Canonical(string lower) => _canonical.TryGetValue(lower, out var word) ? word : null;
}
