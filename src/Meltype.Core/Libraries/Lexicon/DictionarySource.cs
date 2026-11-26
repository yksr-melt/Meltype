// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Libraries.Lexicon;

/// <summary>組み込み辞書 (dictionaries/*.txt を埋め込んだもの) とユーザー辞書を読む。</summary>
internal static class DictionarySource
{
    // 英単語の判定と予測は同じ一覧を引くため、本文と索引は一度だけ用意する。
    private static readonly Lazy<PackedWordSet> EnglishWordList = new(() => PackedWordSet.FromLines(ReadEmbedded("english-words.txt")));

    internal static PackedWordSet EnglishWords => EnglishWordList.Value;

    public static string ReadEmbedded(string name)
    {
        var assembly = typeof(DictionarySource).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Meltype.dictionaries.{name}")
            ?? throw new InvalidOperationException($"組み込み辞書 {name} が見つかりません。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>組み込み辞書のうち、名前が prefix で始まるもの (domain- の分野の辞書) の名前。名前の順。</summary>
    public static IEnumerable<string> EmbeddedNames(string prefix)
    {
        const string resource = "Meltype.dictionaries.";
        return typeof(DictionarySource).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(resource + prefix, StringComparison.Ordinal))
            .Select(n => n[resource.Length..])
            .Order(StringComparer.Ordinal);
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

    public static IEnumerable<string> Load(string name, string? userDirectory) =>
        ParseWords(ReadEmbedded(name)).Concat(LoadUser(name, userDirectory));

    /// <summary>ユーザーの辞書フォルダーに置いた name の語だけ。</summary>
    public static IEnumerable<string> LoadUser(string name, string? userDirectory)
    {
        if (userDirectory is null) yield break;
        var userFile = Path.Combine(userDirectory, name);
        string text;
        try
        {
            if (!File.Exists(userFile)) yield break;
            text = File.ReadAllText(userFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 読めないユーザーの一覧は無いものとして、同梱の一覧だけを使う
            yield break;
        }
        foreach (var word in ParseWords(text)) yield return word;
    }

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
