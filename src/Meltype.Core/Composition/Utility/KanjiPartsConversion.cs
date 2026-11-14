// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;

namespace Meltype.Composition.Utility;

/// <summary>
/// 部首・構成要素の呼び名を「に」「と」でつないだ読み (ごんべんにふるとり) に、それらをすべて含む漢字 (誰・讎 …) を出す (#345)。
/// 読みが分からない字や、口で説明された字を打つのに使う。
/// 呼び名は dictionaries/radicals.txt、漢字の構成要素と並び (よく使う字が先) は dictionaries/kanji-parts.txt (KRADFILE・KANJIDIC2)。
/// 左右・上下の配置は KRADFILE に無いので見ない (同じ要素を含む字は並べて出す)。
/// </summary>
internal sealed class KanjiPartsConversion : IUtilityConversion
{
    // 要素の少ない説明 (きにひ) は当てはまる字が多い。よく使う字から、候補の一覧で選べる数だけ出す
    private const int MaxCandidates = 30;

    private static readonly Lazy<(Dictionary<string, string> Names, (string Kanji, HashSet<string> Parts)[] Kanji)> Data = new(Load);

    public UtilityCandidates? Convert(UtilityInput input)
    {
        var found = input.Reading is { } reading ? Find(reading) : [];
        // 漢字は時刻や設定で変わらないので、選んだら学習してよい
        return found.Count == 0 ? null : new UtilityCandidates(found, UtilityPlacement.Append) { Learnable = true };
    }

    /// <summary>呼び名をつないだ読みに当てはまる漢字 (よく使う字から)。当てはまらなければ空。</summary>
    internal static List<string> Find(string reading)
    {
        // 呼び名 2 つ以上 (最短で き|に|ひ の 3 文字)。1 つだけ (ごんべん) は、その要素を含む字が多すぎて探す役に立たない
        if (reading.Length < 3) return [];
        var (names, kanji) = Data.Value;
        var descriptions = Parse(reading, names).Where(parts => parts.Count >= 2).ToList();
        if (descriptions.Count == 0) return [];
        return kanji.Where(k => descriptions.Any(parts => parts.All(k.Parts.Contains))).Select(k => k.Kanji).Take(MaxCandidates).ToList();
    }

    /// <summary>読みを、呼び名を「に」「と」で区切った並びに分ける。分け方が複数あればすべて (にんべん の「に」は区切りでない)。</summary>
    internal static IEnumerable<List<string>> Parse(string reading, IReadOnlyDictionary<string, string> names)
    {
        for (var length = 1; length <= reading.Length; length++)
        {
            if (!names.TryGetValue(reading[..length], out var part)) continue;
            if (length == reading.Length)
            {
                yield return [part];
                continue;
            }
            if (reading[length] is not ('に' or 'と') || length + 1 == reading.Length) continue;
            foreach (var rest in Parse(reading[(length + 1)..], names)) yield return [part, .. rest];
        }
    }

    private static (Dictionary<string, string>, (string, HashSet<string>)[]) Load()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in DictionarySource.DataLines(DictionarySource.ReadEmbedded("radicals.txt")))
        {
            var fields = line.Split('\t');
            if (fields.Length == 2) names[fields[0]] = fields[1];
        }
        var kanji = DictionarySource.DataLines(DictionarySource.ReadEmbedded("kanji-parts.txt"))
            .Select(line => line.Split('\t'))
            .Select(fields => (fields[0], fields[1].Split(' ').ToHashSet(StringComparer.Ordinal)))
            .ToArray();
        return (names, kanji);
    }
}
