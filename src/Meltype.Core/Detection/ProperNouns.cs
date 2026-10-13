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

    public void AddText(string text)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            foreach (var word in line.Split([' ', '\t', '\r', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!word.All(char.IsAsciiLetterOrDigit)) continue;
                var lower = word.ToLowerInvariant();
                _canonical.TryAdd(lower, word);
                _words.Add(lower);
            }
        }
        Version++;
    }

    /// <summary>語を足すたびに増える (区間の判定の控えを捨てる)。</summary>
    internal int Version { get; private set; }

    /// <summary>小文字の綴り (英語辞書に足す用)。</summary>
    public IEnumerable<string> LowercaseWords => _canonical.Keys;

    public bool Contains(string lower) => _canonical.ContainsKey(lower);
    internal bool Contains(ReadOnlySpan<char> lower) => _canonical.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(lower);

    public bool HasPrefix(string lower) => _words.HasPrefix(lower);
    internal bool HasPrefix(ReadOnlySpan<char> lower) => _words.HasPrefix(lower);

    /// <summary>正しい大文字小文字の形。固有名詞でなければ null。</summary>
    public string? Canonical(string lower) => _canonical.TryGetValue(lower, out var word) ? word : null;
}
