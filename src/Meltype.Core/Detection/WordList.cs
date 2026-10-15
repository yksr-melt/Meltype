// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>単語集合と、その全 prefix の集合。判定はフックのスレッドで走るので O(1) で引けるようにしておく。</summary>
public sealed class WordList
{
    private readonly HashSet<string> _words = new(StringComparer.Ordinal);
    private readonly HashSet<string> _prefixes = new(StringComparer.Ordinal);
    private readonly Dictionary<char, List<string>> _byFirstLetter = [];

    public int Count => _words.Count;
    public IEnumerable<string> Words => _words;

    public void Add(string word)
    {
        if (word.Length == 0 || !_words.Add(word)) return;
        for (var i = 1; i <= word.Length; i++) _prefixes.Add(word[..i]);
        if (!_byFirstLetter.TryGetValue(word[0], out var bucket)) _byFirstLetter[word[0]] = bucket = [];
        bucket.Add(word);
    }

    public bool ContainsWord(string text) => _words.Contains(text);
    internal bool ContainsWord(ReadOnlySpan<char> text) => _words.GetAlternateLookup<ReadOnlySpan<char>>().Contains(text);

    /// <summary>text で始まる単語があるか (text 自体が単語の場合も true)。</summary>
    public bool HasPrefix(string text) => _prefixes.Contains(text);
    internal bool HasPrefix(ReadOnlySpan<char> text) => _prefixes.GetAlternateLookup<ReadOnlySpan<char>>().Contains(text);

    public IReadOnlyList<string> WordsStartingWith(char first) =>
        _byFirstLetter.TryGetValue(first, out var bucket) ? bucket : [];

    /// <summary>空白・改行区切り。# 以降はコメント。英小文字以外を含む語は無視する。</summary>
    public static IEnumerable<string> ParseWords(string text)
    {
        foreach (var line in DictionarySource.LinesWithoutComments(text))
        {
            foreach (var token in line.Split([' ', '\t', '\r', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                var word = token.Trim().ToLowerInvariant();
                if (word.Length > 0 && word.All(c => c is >= 'a' and <= 'z')) yield return word;
            }
        }
    }
}

/// <summary>組み込み辞書 (dictionaries/*.txt を埋め込んだもの) とユーザー辞書を読む。</summary>
public static class DictionarySource
{
    public static string ReadEmbedded(string name)
    {
        var assembly = typeof(DictionarySource).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Meltype.dictionaries.{name}")
            ?? throw new InvalidOperationException($"組み込み辞書 {name} が見つかりません。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>辞書の本文を行に分ける。行末の \r を除き、空行と # で始まるコメント行は飛ばす。</summary>
    public static IEnumerable<string> DataLines(string text)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            yield return line;
        }
    }

    /// <summary>辞書の本文を行に分け、# から行末までのコメントを除いた行を返す (行末の \r や空行はそのまま)。</summary>
    public static IEnumerable<string> LinesWithoutComments(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var hash = line.IndexOf('#');
            yield return hash >= 0 ? line[..hash] : line;
        }
    }

    public static IEnumerable<string> Load(string name, string? userDirectory)
    {
        foreach (var word in WordList.ParseWords(ReadEmbedded(name))) yield return word;
        if (userDirectory is null) yield break;
        var userFile = Path.Combine(userDirectory, name);
        string text;
        try
        {
            if (!File.Exists(userFile)) yield break;
            text = File.ReadAllText(userFile);
        }
        catch
        {
            yield break;
        }
        foreach (var word in WordList.ParseWords(text)) yield return word;
    }
}
