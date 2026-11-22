// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 AoneSenbongi
//
// CandidateDictionary.cs から分野辞書のロード・候補順・文節区切り判定だけを抜き出したもの。
// 単体ではビルドできない。

public sealed class CandidateDictionary
{
    private readonly Dictionary<string, List<string>> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _emoji = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _domain = new(StringComparer.Ordinal);

    public static CandidateDictionary Load(string? userDirectory)
    {
        var dictionary = new CandidateDictionary();
        dictionary.AddText(Detection.DictionarySource.ReadEmbedded("candidates.txt"));
        // 元のファイルでは、ここで絵文字・英字で書く語・社名の辞書も ReadEmbedded で読む
        foreach (var name in Detection.DictionarySource.EmbeddedNames("domain-"))
            dictionary.AddDomainText(Detection.DictionarySource.ReadEmbedded(name));
        if (userDirectory is not null)
        {
            var path = Path.Combine(userDirectory, "candidates.txt");
            if (File.Exists(path)) dictionary.AddText(File.ReadAllText(path));
        }
        return dictionary;
    }

    public void AddText(string text)
    {
        foreach (var line in Detection.DictionarySource.LinesWithoutComments(text))
        {
            var parts = line.Split([' ', '\t', '\r', '　'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            Add(_entries, parts[0], parts.Skip(1));
        }
    }

    /// <summary>絵文字・顔文字の辞書。顔文字には空白や # が入るので、区切りはタブだけ、コメントは行頭の # だけ。</summary>
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

    /// <summary>分野の辞書 (domain-*.txt)。形式は「読み<Tab>表記<Tab>表記 …」、行頭が # の行はコメント。</summary>
    public void AddDomainText(string text)
    {
        foreach (var line in Detection.DictionarySource.DataLines(text))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) Add(_domain, parts[0], parts.Skip(1));
        }
    }

    /// <summary>
    /// 読みそのものが辞書にあるか (先頭一致ではなく)。文節の区切りを決めるのに使う。
    /// 分野の辞書は含めない。読みが多いので、ふだんの文 (えかく → 絵|描く) が駅名 (永覚) に合わさって
    /// 1 つの文節になってしまうのを避ける。
    /// </summary>
    public bool Contains(string reading) => _entries.ContainsKey(reading) || _emoji.ContainsKey(reading);

    public void Add(string reading, IEnumerable<string> words) => Add(_entries, reading, words);

    private static void Add(Dictionary<string, List<string>> entries, string reading, IEnumerable<string> words)
    {
        if (!entries.TryGetValue(reading, out var list)) entries[reading] = list = [];
        foreach (var word in words)
        {
            if (!list.Contains(word)) list.Add(word);
        }
    }

    /// <summary>候補 (同音異義語などの後ろに絵文字・顔文字)。</summary>
    public IReadOnlyList<string> Lookup(string reading) =>
        LookupWords(reading).Concat(LookupEmoji(reading)).Distinct().ToList();

    /// <summary>絵文字・顔文字以外の候補 (同音異義語・英字で書く語・社名)。</summary>
    public IReadOnlyList<string> LookupWords(string reading) => Lookup(_entries, reading);

    /// <summary>分野の辞書の候補 (病名・駅名・人名など)。</summary>
    public IReadOnlyList<string> LookupDomain(string reading) => Lookup(_domain, reading);

    /// <summary>絵文字・顔文字の候補。</summary>
    public IReadOnlyList<string> LookupEmoji(string reading) => Lookup(_emoji, reading);

    // 語の後ろに付いていてよい助詞・「だ」など
    private static readonly HashSet<string> Endings =
    [
        "", "を", "が", "は", "に", "で", "と", "も", "へ", "の", "や", "な", "だ",
        "です", "から", "まで", "より", "って", "とか", "さ", "ね", "よ",
    ];

    /// <summary>
    /// 文節の読みに対する候補。文節は助詞などを含む (はしを) ので、辞書にある最長の先頭部分を置き換え、
    /// 残りはかなのまま付ける (箸を / 端を)。
    /// </summary>
    private static IReadOnlyList<string> Lookup(Dictionary<string, List<string>> entries, string reading)
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
