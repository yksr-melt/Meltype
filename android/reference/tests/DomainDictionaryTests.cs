// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;

internal static class DomainDictionaryTests
{
    private sealed class Plain : IKanjiConverter
    {
        public string? Convert(string hiragana) => null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => null;
    }

    private sealed class SaikiConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana == "さいき" ? "再起" : null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            hiragana == "さいき" ? [new("さいき", "再起")] : null;
    }

    public static void Run()
    {
        var dictionary = CandidateDictionary.Load(null);
        void Has(string reading, string word)
        {
            if (!dictionary.LookupDomain(reading).Contains(word))
                throw new Exception($"分野辞書の候補がありません: {reading} → {word}");
        }
        Has("せんたんきょだいしょう", "先端巨大症");
        Has("いくせいしゃけん", "育成者権");
        Has("あいづかのうえき", "会津加納駅");
        Has("せんたんきょだいしょうの", "先端巨大症の");
        Has("どくにんせい", "独任制");
        Has("ちゅうえつぱるぷ", "中越パルプ");
        Has("あまみぐんとうしんこうかいはつとくべつそちほう", "奄美群島振興開発特別措置法");
        if (dictionary.LookupDomain("きょう").Count != 0 || dictionary.LookupDomain("あい").Count != 0)
            throw new Exception("よく使う読みに分野辞書の語が入っています");
        if (dictionary.Contains("せんたんきょだいしょう"))
            throw new Exception("分野辞書が文節区切り判定に使われています");
        Console.WriteLine("PASS: domain dictionaries provide terms without affecting clause segmentation");

        var shown = new AndroidInputSession(new Plain(), _ => [], (_, _, _) => { });
        foreach (var c in "sentankyodaishou") shown.Character(c);
        shown.Space(false);
        var candidates = shown.View!.Candidates.ToList();
        var first = string.Join(",", candidates.Take(4));
        if (first != "せんたんきょだいしょう,センタンキョダイショウ,ｾﾝﾀﾝｷｮﾀﾞｲｼｮｳ,先端巨大症")
            throw new Exception("分野辞書の候補順が違います: " + first + " / " + string.Join(" ", candidates));
        Console.WriteLine("PASS: domain terms come after everyday candidates");

        foreach (var (keys, expected) in new[]
        {
            ("ff\buronto\n", "ふろんと"),
            ("kk\bakkou\n", "かっこう"),
            ("nk\ba\n", "な"),
            ("kank\bi\n", "かに"),
        })
        {
            var output = "";
            var session = new AndroidInputSession(new Plain(), _ => [],
                (op, text, _) => { if (op == 1) output += text; });
            foreach (var c in keys)
            {
                if (c == '\b') session.Key(0x08);
                else if (c == '\n') session.Key(0x0D);
                else session.Character(c);
            }
            if (output != expected) throw new Exception($"BackSpace 後の再判定が違います: {keys.Replace("\b", "<BS>")} → {output} (期待 {expected})");
        }
        Console.WriteLine("PASS: BackSpace re-evaluates doubled consonants and n");

        var path = Path.Combine(Path.GetTempPath(), "meltype-domain-test-" + Guid.NewGuid().ToString("N") + ".json");
        var user = new AndroidUserDictionary(path);
        user.Add("さいき", "PSYQUI");
        user.Add("さいき", "サイキ");
        var ordered = new AndroidInputSession(new SaikiConverter(),
            reading => reading == "さいき" ? ["再帰", "最期", "才気", "佐伯", "催奇"] : [],
            (_, _, _) => { }, userDictionary: user);
        foreach (var c in "saiki") ordered.Character(c);
        ordered.Space(false);
        var list = ordered.View!.Candidates.ToList();
        // Android では先頭がエンジンの答え (再起) のまま。その後ろの辞書語が 2 次候補より前に残れば #1681 は満たす。
        if (list.Count < 4 || list[0] != "再起" || list[1] != "サイキ" || list[2] != "PSYQUI" || list[3] != "再帰")
            throw new Exception("辞書語とエンジン候補の順が違います: " + string.Join(" ", list));
        Console.WriteLine("PASS: dictionary words precede engine candidates");
    }
}
