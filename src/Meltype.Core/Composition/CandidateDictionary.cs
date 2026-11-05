// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 変換候補の補助辞書 (読み → 候補)。Microsoft IME の変換エンジンは最有力の 1 候補しか返さないので、
/// 同音異義語 (はし → 橋/箸/端、とうてん → 当店/読点) や医療用語はここから出す。
/// 形式は 1 行に「読み 候補 候補 …」。# 以降はコメント。
/// 組み込みの dictionaries/candidates.txt・medical.txt と %LOCALAPPDATA%\Meltype\dictionaries\candidates.txt を読む。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "日本語入力の辞書を表す既存の公開名。コレクション型を表すものではない。")]
public sealed class CandidateDictionary
{
    private readonly Dictionary<string, string[]> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _medicalEntries = new(StringComparer.Ordinal);

    // 絵文字・顔文字 (emoji.txt・emoji-cldr.txt)。変換の候補では最後にまとめて出すので、ほかの候補と分けて持つ。
    private readonly Dictionary<string, string[]> _emoji = new(StringComparer.Ordinal);

    public int Count => _entries.Count
        + _medicalEntries.Keys.Count(k => !_entries.ContainsKey(k))
        + _emoji.Keys.Count(k => !_entries.ContainsKey(k) && !_medicalEntries.ContainsKey(k));

    public static CandidateDictionary Load(string? userDirectory)
    {
        var dictionary = new CandidateDictionary();
        dictionary.AddText(Detection.DictionarySource.ReadEmbedded("candidates.txt"));
        dictionary.AddTabText(Detection.DictionarySource.ReadEmbedded("emoji.txt"));
        // Unicode CLDR の日本語の名前・キーワードから作った絵文字 (手で書いた emoji.txt の後に並ぶ)
        dictionary.AddTabText(Detection.DictionarySource.ReadEmbedded("emoji-cldr.txt"));
        // 英字で書く語 (リナックス → Linux)。JMdict から作ったもの
        dictionary.AddText(Detection.DictionarySource.ReadEmbedded("loanwords.txt"));
        // JMdict に無い社名・サービス名 (しゃおみ → Xiaomi)。手で書いたもの
        dictionary.AddText(Detection.DictionarySource.ReadEmbedded("brands.txt"));
        foreach (var entry in MedicalTermDictionary.Readings)
            Add(dictionary._medicalEntries, entry.Text, entry.Words);
        // 実行中の版を出す (issue #959)。辞書に固定すると、更新後も古い版が候補に残ってしまう。
        dictionary.Add("ばーじょん", [$"Meltype-{Config.ProjectInfo.CoreVersion}"]);
        if (userDirectory is not null)
        {
            var emoji = Path.Combine(userDirectory, "emoji.txt");
            try
            {
                if (File.Exists(emoji)) dictionary.AddTabText(File.ReadAllText(emoji));
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"ユーザーの絵文字辞書を読めませんでした: {ex.Message}");
            }
            var path = Path.Combine(userDirectory, "candidates.txt");
            try
            {
                if (File.Exists(path)) dictionary.AddText(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"ユーザーの候補辞書を読めませんでした: {ex.Message}");
            }
        }
        return dictionary;
    }

    public void AddText(string text)
    {
        foreach (var line in Detection.DictionarySource.LinesWithoutComments(text))
        {
            var parts = line.Split([' ', '\t', '\r', '　'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            Add(parts[0], parts.Skip(1));
        }
    }

    /// <summary>
    /// 絵文字・顔文字の辞書 (emoji.txt)。顔文字には空白や # が入るので、区切りはタブだけ、コメントは行頭の # だけ。
    /// </summary>
    public void AddTabText(string text)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.TrimStart().StartsWith('#')) continue;
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;
            Add(_emoji, parts[0], parts.Skip(1));
        }
    }

    /// <summary>読みそのものが辞書 (医療用語・絵文字を含む) にあるか (先頭一致ではなく)。</summary>
    public bool Contains(string reading) => _entries.ContainsKey(reading) || _medicalEntries.ContainsKey(reading) || _emoji.ContainsKey(reading);

    public void Add(string reading, IEnumerable<string> words) => Add(_entries, reading, words);

    private static void Add(Dictionary<string, string[]> entries, string reading, IEnumerable<string> words)
    {
        var existing = entries.GetValueOrDefault(reading) ?? [];
        entries[reading] = existing.Concat(words).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 文節の読みに対する候補。文節は助詞などを含む (はしを) ので、辞書にある最長の先頭部分を置き換え、
    /// 残りはかなのまま付ける (箸を / 端を)。
    /// </summary>
    // 語の後ろに付いていてよい助詞・「だ」など
    private static readonly HashSet<string> Endings =
        ["", "を", "が", "は", "に", "で", "と", "も", "へ", "の", "や", "な", "だ", "です", "から", "まで", "より", "って", "とか", "さ", "ね", "よ"];

    /// <summary>候補 (同音異義語・医療用語の後ろに絵文字・顔文字)。</summary>
    public IReadOnlyList<string> Lookup(string reading) =>
        LookupWords(reading).Concat(LookupMedicalWords(reading)).Concat(LookupEmoji(reading)).Distinct().ToList();

    /// <summary>医療用語以外の候補 (同音異義語・英字で書く語・社名)。</summary>
    public IReadOnlyList<string> LookupWords(string reading) => Lookup(_entries, reading);

    /// <summary>医療用語辞書の候補。</summary>
    public IReadOnlyList<string> LookupMedicalWords(string reading) => Lookup(_medicalEntries, reading);

    /// <summary>絵文字・顔文字の候補 (手で書いた emoji.txt の順 → CLDR の順)。</summary>
    public IReadOnlyList<string> LookupEmoji(string reading) => Lookup(_emoji, reading);

    private static List<string> Lookup(Dictionary<string, string[]> entries, string reading)
    {
        for (var length = reading.Length; length >= 1; length--)
        {
            // 1 文字の読み (い → 位 胃 …) は、その読みだけの文節のときだけ (いきを → 位きを にしない)。
            if (length == 1 && reading.Length > 1) break;
            // 残りが助詞など (はし|を) のときだけ。語の途中 (ふく|ざつな → 服ざつな) では使わない。
            if (!Endings.Contains(reading[length..])) continue;
            if (entries.TryGetValue(reading[..length], out var words))
            {
                var rest = reading[length..];
                return words.Select(w => w + rest).ToList();
            }
        }
        return [];
    }
}

internal static class MedicalTermDictionary
{
    internal readonly record struct Reading(string Text, string[] Words, int Order);

    private static readonly Lazy<Reading[]> s_readings = new(LoadReadings);
    private static readonly Lazy<Dictionary<string, Reading[]>> s_byPrefix = new(() =>
        s_readings.Value
            .Where(entry => entry.Text.Length >= 2)
            .GroupBy(entry => entry.Text[..2], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(entry => entry.Text.Length).ThenBy(entry => entry.Order).ToArray(),
                StringComparer.Ordinal));

    internal static IReadOnlyList<Reading> Readings => s_readings.Value;

    internal static IEnumerable<string> Predict(string prefix)
    {
        if (prefix.Length < 2 || !s_byPrefix.Value.TryGetValue(prefix[..2], out var entries)) return [];
        return entries
            .Where(entry => entry.Text.Length > prefix.Length && entry.Text.StartsWith(prefix, StringComparison.Ordinal))
            .SelectMany(entry => entry.Words);
    }

    private static Reading[] LoadReadings()
    {
        var entries = Detection.DictionarySource.DataLines(Detection.DictionarySource.ReadEmbedded("medical.txt"))
            .Select((line, order) => (Fields: line.TrimEnd('\r').Split('\t'), Order: order))
            .Where(row => row.Fields.Length >= 2 && row.Fields[0].Length > 0 && row.Fields[1].Length > 0);
        return entries
            .GroupBy(row => row.Fields[0], StringComparer.Ordinal)
            .Select(group => new Reading(
                group.Key,
                group.Select(row => row.Fields[1]).Distinct(StringComparer.Ordinal).ToArray(),
                group.Min(row => row.Order)))
            .ToArray();
    }
}
