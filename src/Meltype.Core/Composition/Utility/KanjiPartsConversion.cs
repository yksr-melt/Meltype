// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;

namespace Meltype.Composition.Utility;

/// <summary>
/// 部首・構成要素の呼び名を「に」でつないだ読み (ごんべんにふるとり) に、それらをすべて含む漢字 (誰・讎 …) を出す (#345)。
/// 読みが分からない字や、口で説明された字を打つのに使う。
/// 呼び名は dictionaries/radicals.txt、漢字の構成要素と並び (よく使う字が先) は dictionaries/kanji-parts.txt (KRADFILE・KANJIDIC2)。
/// 左右・上下の配置は KRADFILE に無いので見ない (同じ要素を含む字は並べて出す)。KRADFILE の要素は集合なので、
/// 同じ要素を重ねた字 (林・森) は説明と区別できない。
/// 候補は説明どおりの字 (余分な要素の少ない字) から出す。ほかの要素も含む字 (護 = 言 + 隹 + 又 …) はその後ろ。
/// </summary>
internal sealed class KanjiPartsConversion : IUtilityConversion
{
    // よく使う要素の説明 (さんずいにくさかんむり) は当てはまる字が多い。説明どおりの字から、候補の一覧で選べる数だけ出す
    private const int MaxCandidates = 30;

    private static readonly Lazy<(Dictionary<string, string> Names, (string Kanji, HashSet<string> Parts)[] Kanji)> Data = new(Load);

    public UtilityCandidates? Convert(UtilityInput input)
    {
        if (input.Reading is not { } reading || Find(reading) is not { Count: > 0 } found) return null;
        // 呼び名を「に」でつないだ読みは、ふだんの語・句には無い (readings.txt の語はどれも当てはまらないことをテストで確かめる)。
        // 漢字を最初の候補にし、後ろに変換エンジンの候補 (言偏にふるとり。字の書き方を説明するときに打つ) も残す (Prepend)。
        // 漢字は時刻や設定で変わらないので、選んだら学習してよい
        return new UtilityCandidates(found, UtilityPlacement.Prepend) { Learnable = true };
    }

    /// <summary>呼び名をつないだ読みに当てはまる漢字 (よく使う字から)。当てはまらなければ空。</summary>
    internal static List<string> Find(string reading)
    {
        // 呼び名 2 つ以上。1 つだけ (ごんべん) は、その要素を含む字が多すぎて探す役に立たない
        var (names, kanji) = Data.Value;
        // 先頭は部首にしか使わない名前 (ごんべん・さんずい) だけ。字の名前 (き・ひ・め) や、ふだんの語と同じ読みの名前
        // (かける・こころ・れっか = 劣化) で始まると、「名詞 に 動詞」の句 (きにかける = 気にかける、めにみる) と区別できない。
        // 2 つ目からは字の名前も使える (にんべんにき)
        if (!StartsWithRadicalName(reading, names)) return [];
        var descriptions = Parse(reading, names).Where(parts => parts.Count >= 2).ToList();
        if (descriptions.Count == 0) return [];
        // 説明どおりの字 (説明に無い要素の少ない字) を先に。同じなら kanji-parts.txt の順 (よく使う字が先)。OrderBy は同じ値の順を保つ
        return kanji
            .Select(k => (k.Kanji, Extra: descriptions.Where(parts => parts.All(k.Parts.Contains)).Select(parts => k.Parts.Count(p => !parts.Contains(p))).DefaultIfEmpty(-1).Min()))
            .Where(k => k.Extra >= 0)
            .OrderBy(k => k.Extra)
            .Select(k => k.Kanji).Take(MaxCandidates).ToList();
    }

    /// <summary>読みが、ふだんの語の読み (readings.txt) でない呼び名と「に」で始まるか。</summary>
    private static bool StartsWithRadicalName(string reading, Dictionary<string, string> names)
    {
        for (var length = 1; length < reading.Length; length++)
        {
            if (reading[length] == 'に' && names.ContainsKey(reading[..length]) && !CommonReadings.Words.Contains(reading.AsSpan(0, length))) return true;
        }
        return false;
    }

    /// <summary>
    /// 読みを、呼び名を「に」で区切った並びに分ける。分け方が複数あればすべて (にんべん の「に」は区切りでない)。
    /// 「と」では区切らない。と でつなぐと、ふだんの語 (ひとくち = ひ|と|くち) や句 (かねとひと) と区別できない。
    /// </summary>
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
            if (reading[length] != 'に' || length + 1 == reading.Length) continue;
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
