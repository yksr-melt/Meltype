// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Libraries.Lexicon;

namespace Meltype.Detection;

/// <summary>
/// 英語の固有名詞 (dictionaries/propernouns.txt)。小文字で引き、正しい大文字小文字の形 (GitHub, iPhone) を返す。
/// ローマ字としても読めてしまう固有名詞 (amazon, adobe) を英語として扱うのに使う。
/// </summary>
internal sealed class ProperNouns
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
        Config.DataFile.LoadUserText(userDirectory, "propernouns.txt", "ユーザーの固有名詞辞書", text => nouns.AddText(text));
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
                _canonical[lower] = word;
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

    /// <summary>
    /// すべて小文字で打った語を自動で正しい大文字小文字に直す形 (iphone → iPhone、iphone's → iPhone's)。直さないなら null。
    /// 直すのは、語の途中で Shift を押し直す手間がかかり、文章では小文字で書くことのない語 (iPhone・GitHub・macOS):
    /// 大文字・小文字が混在し、2 文字目以降に大文字を含む固有名詞。
    /// 先頭だけ大文字 (Google) や全大文字 (LINE) の語は、小文字で書く人もいるので直さない。
    /// 自分で大文字を混ぜて打った語 (IPHONE) は、打ったままにする。
    /// </summary>
    public string? AutoCanonical(string raw)
    {
        if (raw.EndsWith("'s", StringComparison.Ordinal))
            return AutoCanonical(raw[..^2]) is { } stem ? stem + "'s" : null;
        return _canonical.TryGetValue(raw, out var word) && IsMixedInternalUpper(word) ? word : null;
    }

    private static bool IsMixedInternalUpper(string word)
    {
        var hasLower = false;
        var hasInternalUpper = false;
        for (var i = 0; i < word.Length; i++)
        {
            if (char.IsAsciiLetterLower(word[i])) hasLower = true;
            else if (i > 0 && char.IsAsciiLetterUpper(word[i])) hasInternalUpper = true;
        }
        return hasLower && hasInternalUpper;
    }
}
