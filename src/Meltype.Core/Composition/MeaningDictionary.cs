// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 候補の日本語の意味 (dictionaries/meanings.txt。ウィクショナリー日本語版から作った表、CC BY-SA 4.0)。
/// 変換中に候補で止まったとき、その意味を出す (箸 → 食物を…二本一組で用いられる棒)。
/// </summary>
public sealed class MeaningDictionary
{
    private readonly Dictionary<string, List<(string Reading, string Gloss)>> _entries = new(StringComparer.Ordinal);
    // 活用する語の語幹 (持つ → 持、美しい → 美し) → 書き方。持って・美しかった を引くのに使う。
    private readonly Dictionary<string, string> _stems = new(StringComparer.Ordinal);
    private int _maxLength;

    /// <summary>一度に出す意味の数。</summary>
    private const int MaxSenses = 2;

    public int Count => _entries.Count;

    public static MeaningDictionary Load() => Parse(Detection.DictionarySource.ReadEmbedded("meanings.txt"));

    public static MeaningDictionary Parse(string text)
    {
        var dictionary = new MeaningDictionary();
        foreach (var line in Detection.DictionarySource.DataLines(text))
        {
            var fields = line.Split('\t');
            if (fields.Length < 3) continue;
            var word = fields[0];
            if (!dictionary._entries.TryGetValue(word, out var list)) dictionary._entries[word] = list = [];
            list.Add((fields[1], fields[2]));
            dictionary._maxLength = Math.Max(dictionary._maxLength, word.Length);
            // 動詞 (う段で終わる)・い形容詞 (い で終わる) は語幹でも引けるようにする。語幹に漢字が入るものだけ。
            if (word.Length >= 2 && "うくぐすつぬぶむるい".Contains(word[^1]) && word[..^1].Any(KanaChars.IsKanji)) dictionary._stems.TryAdd(word[..^1], word);
        }
        return dictionary;
    }

    /// <summary>
    /// 候補 (text) の意味。reading はその文節の読み (橋 を はし と読んだのか きょう と読んだのかで意味を選ぶ)。
    /// 候補の先頭から一番長く一致する語を引き、残りはひらがな (助詞・送り仮名) だけのときに返す。無ければ null。
    /// </summary>
    public string? Lookup(string text, string? reading)
    {
        if (!text.Any(KanaChars.IsKanjiOrKatakana)) return null;
        for (var length = Math.Min(_maxLength, text.Length); length >= 1; length--)
        {
            var head = text[..length];
            if (!_entries.TryGetValue(head, out var senses))
            {
                if (!_stems.TryGetValue(head, out var word) || !_entries.TryGetValue(word, out senses)) continue;
            }
            if (text[length..].Any(KanaChars.IsKanjiOrKatakana)) return null;
            return Format(Choose(senses, reading));
        }
        return null;
    }

    /// <summary>読みの印の付いた意味 (（はし）…) は、読みが合うものを先に。合うものが無ければ印の無いもの、それも無ければ全部。</summary>
    private static List<string> Choose(List<(string Reading, string Gloss)> senses, string? reading)
    {
        var matching = reading is null ? [] : senses.Where(s => s.Reading.Length > 0 && reading.StartsWith(s.Reading, StringComparison.Ordinal)).ToList();
        if (matching.Count > 0) return matching.Concat(senses.Where(s => s.Reading.Length == 0)).Select(s => s.Gloss).ToList();
        var unmarked = senses.Where(s => s.Reading.Length == 0).ToList();
        return (unmarked.Count > 0 ? unmarked : senses).Select(s => s.Gloss).ToList();
    }

    private static string Format(List<string> glosses)
    {
        var shown = glosses.Take(MaxSenses).ToList();
        if (shown.Count == 1) return shown[0];
        return string.Join("\n", shown.Select((g, i) => $"{(char)('①' + i)} {g}"));
    }
}
