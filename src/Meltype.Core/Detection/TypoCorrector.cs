// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>
/// 英語 / 日本語 (ローマ字) の打ち間違いを直す。直し間違い (FP) を減らすのを最優先にする。
///   入力 → 正規化 → 辞書にそのままあれば終わり → 候補 (SymSpell、削除 2 回まで) → 点数 → 1 位と 2 位の差が θ 以上なら直す、でなければそのまま。
/// 点数 S(c, l) = −α·D(入力, c) + log P₄(c | l) + log P(l | 直前の言語)。
/// D は QWERTY の距離で重みを付けた Damerau-Levenshtein (隣のキーの置換 .4、入れ替え .5、同じ文字の重複 .3、挿入・削除 .7)。
/// </summary>
public sealed class TypoCorrector
{
    public const int MaxDeletes = 2;
    public const int MinLength = 3;

    public enum Language { Japanese, English }

    public readonly record struct Candidate(string Word, Language Language, double Distance, double Score);

    public readonly record struct Result(string Input, string Output, Language Language, bool Corrected, double Margin, IReadOnlyList<Candidate> Candidates);

    /// <summary>点数の距離の重み α。</summary>
    public double Alpha { get; set; } = 6.0;

    /// <summary>直すのに要る、1 位と 2 位 (2 位が無ければ、入力そのまま) の点数の差 θ。</summary>
    public double Theta { get; set; } = 2.0;

    /// <summary>直前と同じ言語の確率 (日本語の後なら P(日本語) = .8。英語も同じ)。</summary>
    public double SameLanguagePrior { get; set; } = 0.8;

    private readonly HashSet<string> _japanese = new(StringComparer.Ordinal);
    private readonly HashSet<string> _english = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _deletes = new(StringComparer.Ordinal);
    private readonly NGramModel _japaneseModel;
    private readonly NGramModel _englishModel;
    private readonly Dictionary<(char Typed, char Intended), double> _learned = [];

    private readonly HashSet<string> _protected = new(StringComparer.Ordinal);

    /// <param name="protectedWords">辞書に無くても直さない語 (固有名詞・製品名など)。</param>
    public TypoCorrector(IEnumerable<string> japaneseRomaji, IEnumerable<string> englishWords, IEnumerable<string>? protectedWords = null)
    {
        foreach (var w in protectedWords ?? []) _protected.Add(w.ToLowerInvariant());
        foreach (var w in japaneseRomaji) AddTo(_japanese, Normalize(w));
        foreach (var w in englishWords) AddTo(_english, w.ToLowerInvariant());
        _japaneseModel = new NGramModel(_japanese);
        _englishModel = new NGramModel(_english);
    }

    /// <summary>同梱の辞書 (japanese.txt のローマ字、english-words.txt) で作る。</summary>
    /// 日本語は japanese.txt のローマ字に、readings.txt (JMdict のよく使う語の読み、約 2 万語) をローマ字にしたものを足す。
    /// 固有名詞 (propernouns.txt)・製品名 (brands.txt)・カタカナ語の英語の綴り (loanwords.txt) は直さない語にする。
    public static TypoCorrector FromBuiltIn()
    {
        var readings = DictionarySource.LinesWithoutComments(DictionarySource.ReadEmbedded("readings.txt"))
            .Select(l => l.Split('\t')[0].Trim()).Where(l => l.Length > 0).Select(KanaToRomaji).OfType<string>();
        var protectedWords = DictionarySource.Load("propernouns.txt", null)
            .Concat(KanaLineSpellings("brands.txt")).Concat(KanaLineSpellings("loanwords.txt"));
        return new(DictionarySource.Load("japanese.txt", null).Concat(readings),
            WordList.ParseWords(DictionarySource.ReadEmbedded("english-words.txt")), protectedWords);
    }

    /// <summary>「かな 綴り 綴り …」の行の、かな以外の綴り。</summary>
    private static IEnumerable<string> KanaLineSpellings(string name) =>
        DictionarySource.LinesWithoutComments(DictionarySource.ReadEmbedded(name))
            .SelectMany(l => l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Skip(1));

    private static readonly Dictionary<char, string> KanaTable = BuildKanaTable();

    private static Dictionary<char, string> BuildKanaTable()
    {
        const string kana = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをがぎぐげござじずぜぞだぢづでどばびぶべぼぱぴぷぺぽ";
        string[] romaji = ["a","i","u","e","o","ka","ki","ku","ke","ko","sa","shi","su","se","so","ta","chi","tsu","te","to","na","ni","nu","ne","no",
            "ha","hi","fu","he","ho","ma","mi","mu","me","mo","ya","yu","yo","ra","ri","ru","re","ro","wa","wo",
            "ga","gi","gu","ge","go","za","ji","zu","ze","zo","da","ji","zu","de","do","ba","bi","bu","be","bo","pa","pi","pu","pe","po"];
        var table = new Dictionary<char, string>();
        for (var i = 0; i < kana.Length; i++)
        {
            table[kana[i]] = romaji[i];
            table[(char)(kana[i] + 0x60)] = romaji[i]; // カタカナ
        }
        return table;
    }

    /// <summary>かなをヘボン式のローマ字にする (きゃ → kya、しゃ → sha、っ → 子音を重ねる、ん → n / 母音・y の前は nn)。長音 (ー) など、打ち方が決まらないものを含む語は null。</summary>
    public static string? KanaToRomaji(string kana)
    {
        var sb = new System.Text.StringBuilder();
        var doubleNext = false;
        for (var i = 0; i < kana.Length; i++)
        {
            var c = kana[i] is >= 'ァ' and <= 'ヶ' ? (char)(kana[i] - 0x60) : kana[i];
            if (c == 'っ') { doubleNext = true; continue; }
            if (c == 'ん')
            {
                var next = i + 1 < kana.Length && KanaTable.TryGetValue(kana[i + 1] is >= 'ァ' and <= 'ヶ' ? (char)(kana[i + 1] - 0x60) : kana[i + 1], out var n) ? n : "";
                sb.Append(next.Length == 0 || "aiueoy".Contains(next[0]) ? "nn" : "n");
                continue;
            }
            if (!KanaTable.TryGetValue(c, out var r)) return null;
            var small = i + 1 < kana.Length ? kana[i + 1] : '\0';
            if (small is >= 'ァ' and <= 'ヶ') small = (char)(small - 0x60);
            if (small is 'ゃ' or 'ゅ' or 'ょ' && r.EndsWith('i'))
            {
                var y = small == 'ゃ' ? "a" : small == 'ゅ' ? "u" : "o";
                r = r is "shi" or "chi" or "ji" ? r[..^1] + y : r[..^1] + "y" + y;
                i++;
            }
            if (doubleNext) { sb.Append(r.StartsWith("ch") ? 't' : r[0]); doubleNext = false; }
            sb.Append(r);
        }
        return doubleNext || sb.Length == 0 ? null : sb.ToString();
    }

    private void AddTo(HashSet<string> set, string word)
    {
        if (word.Length < 2 || !word.All(c => char.IsAsciiLetterLower(c) || c == 'N') || !set.Add(word)) return;
        if (_japanese.Contains(word) && _english.Contains(word)) return; // 両方にある語は 1 回だけ索引に入れる
        foreach (var d in Deletes(word))
        {
            if (!_deletes.TryGetValue(d, out var list)) _deletes[d] = list = [];
            list.Add(word);
        }
    }

    /// <summary>小文字にし、訓令式を揃える (si → shi、tu → tsu、ti → chi、hu → fu、n' / nn → N)。</summary>
    public static string Normalize(string input)
    {
        var s = input.ToLowerInvariant();
        // 拗音の訓令式 (tya → cha、sya → sha、zya・jya → ja) と、っち の綴り (cchi・ttyo → tchi・tcho) を揃える
        s = System.Text.RegularExpressions.Regex.Replace(s, "ty([auo])", "ch$1");
        s = System.Text.RegularExpressions.Regex.Replace(s, "sy([auo])", "sh$1");
        s = System.Text.RegularExpressions.Regex.Replace(s, "(?:zy|jy)([auo])", "j$1");
        s = s.Replace("cch", "tch");
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            var prev = i > 0 ? s[i - 1] : '\0';
            if (s[i] == 'n' && (next == '\'' || next == 'n')) { sb.Append('N'); i++; continue; }
            // 子音の前と語の終わりの n も ん (shinkansen = shinkansenn)
            if (s[i] == 'n' && !"aiueoy".Contains(next) && next != 'N') { sb.Append('N'); continue; }
            if (next == 'i' && s[i] == 's' && prev != 's' || next == 'i' && s[i] == 't' && prev != 't' && prev != 'c')
            {
                sb.Append(s[i] == 's' ? "sh" : "ch");
                continue;
            }
            if (s[i] == 't' && next == 'u' && prev != 't') { sb.Append("ts"); continue; }
            if (s[i] == 'h' && next == 'u' && prev is not ('s' or 'c' or 'f')) { sb.Append('f'); continue; }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    public Result Correct(string input, Language? previous = null)
    {
        var lower = input.ToLowerInvariant();
        var romaji = Normalize(input);
        if (_english.Contains(lower) || _protected.Contains(lower)) return Keep(input, Language.English);
        if (_japanese.Contains(romaji)) return Keep(input, Language.Japanese);
        if (lower.Length < MinLength || !lower.All(char.IsAsciiLetterLower)) return Keep(input, previous ?? Language.English);
        // 日本語の文 (kyouhaame) は語に区切れればそのまま、区切れなければ 1 文字の打ち間違いを直して区切れるかを見る (kyouahame → kyouhaame)
        // (語の直しで直らなかったときだけ。下の TryPhrase)
        var phraseAllowed = previous != Language.English && lower.Length >= PhraseMinLength;
        if (phraseAllowed && SegmentCost(romaji) <= PhraseMaxCost(romaji)) return Keep(input, Language.Japanese);
        var word = CorrectWord(input, lower, romaji, previous);
        // 語の直しに近い候補 (距離 1 未満) があって (差が足りずに) そのままにしたものは、文として区切り直さない (1 語の打ち間違いを 2 語に割ってしまう)
        if (word.Corrected || !phraseAllowed || word.Candidates.Any(c => c.Distance < 1.0)) return OnlyLongVowelsUnlessJapanese(word, previous);
        return CorrectPhrase(input) is { Corrected: true } phrase ? OnlyLongVowelsUnlessJapanese(phrase, previous) : word;
    }

    /// <summary>
    /// 長音の u を足すだけの直し (hokkaido → hokkaidou) は、日本語を打っている途中だけにする。
    /// 英語やローマ字の名前 (Hokkaido・wagyu) として打ったのかもしれないので、言語が分からなければそのまま。
    /// </summary>
    private static Result OnlyLongVowelsUnlessJapanese(Result r, Language? previous)
    {
        if (!r.Corrected || previous == Language.Japanese) return r;
        return StripLongVowels(Normalize(r.Output)) == StripLongVowels(Normalize(r.Input)) ? r with { Output = r.Input, Corrected = false } : r;
    }

    private static string StripLongVowels(string s) => System.Text.RegularExpressions.Regex.Replace(s, "(?<=[ou])u", "");

    private Result CorrectWord(string input, string lower, string romaji, Language? previous)
    {

        var candidates = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, lang) in new[] { (lower, Language.English), (romaji, Language.Japanese) })
        {
            foreach (var d in Deletes(key).Append(key))
            {
                if (!_deletes.TryGetValue(d, out var words) && !Has(d)) continue;
                foreach (var w in words is null ? [d] : words.Append(d))
                {
                    if (!seen.Add(w + "|" + lang)) continue;
                    var inJa = _japanese.Contains(w);
                    var inEn = _english.Contains(w);
                    if (lang == Language.Japanese ? !inJa : !inEn) continue;
                    var dist = Distance(key, w, lang == Language.Japanese);
                    if (dist > MaxDeletes) continue;
                    candidates.Add(new Candidate(w, lang, dist, Score(dist, w, lang, previous)));
                }
            }
        }
        if (candidates.Count == 0) return Keep(input, previous ?? Language.English);
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        var top = candidates[0];
        // 2 位が無ければ「入力のまま」を対抗にする (辞書に無い語を打った可能性)。距離 0 で、言語モデルの点だけ。
        var keepScore = Math.Max(Score(0, lower, Language.English, previous), Score(0, romaji, Language.Japanese, previous)) - Alpha * 0.9;
        var second = candidates.Count > 1 ? Math.Max(candidates[1].Score, keepScore) : keepScore;
        var margin = top.Score - second;
        // 辞書に無い語と打ち間違いは見分けにくい。短い語と、ローマ字としてそのまま読める語 (genshin・odakyu) は、
        // 知らない正しい語のことが多いので、直すのに大きな差を求める (日本語の打ち間違いは、たいていローマ字として読めなくなる)。
        // 日本語を打っている途中で、ローマ字として読めない入力 (kuas の s) は打ち間違いがはっきりしているので、短くても直してよい
        var brokenJapanese = previous == Language.Japanese && top.Language == Language.Japanese && !IsRomaji(romaji);
        var theta = Theta + (lower.Length <= 4 && !brokenJapanese ? ShortWordExtra : 0) + (top.Language == Language.Japanese && IsRomaji(romaji) ? ValidRomajiExtra : 0)
            + (top.Distance >= 0.7 ? CostlyEditExtra : 0);
        return margin >= theta
            ? new Result(input, top.Word.Replace("N", "nn"), top.Language, true, margin, candidates)
            : new Result(input, input, top.Language, false, margin, candidates);
    }

    public const int PhraseMinLength = 6;
    public const int PhraseMaxLength = 40;

    /// <summary>助詞 (語の間に来る 1〜2 音の語。区切るときに安くする)。</summary>
    private static readonly HashSet<string> Particles = new(StringComparer.Ordinal)
        { "ha", "ga", "wo", "ni", "de", "to", "mo", "no", "he", "ya", "ka", "ne", "yo", "kara", "made", "yori", "dake", "desu", "da", "masu", "ta", "te",
          // 語の後に付く語 (出張中・田中さん・私たち)
          "chuu", "san", "sama", "kun", "chan", "tachi" };

    /// <summary>文の途中によく来る助詞 (は・が・を・に・で・と・も・の・から・まで)。ほかの助詞 (か・ね・よ など) は少し高くする。</summary>
    private static readonly HashSet<string> CommonParticles = new(StringComparer.Ordinal) { "ha", "ga", "wo", "ni", "de", "to", "mo", "no", "kara", "made" };

    /// <summary>
    /// ローマ字を辞書の語と助詞に区切る、いちばん安い区切り方の点 (区切れなければ +∞)。
    /// 語 1 つで 1、よく使う助詞は .5 (ほかの助詞は 1)、2 文字以下の助詞でない語 (あ・め など、何にでも当てはまる) は 3。
    /// </summary>
    public double SegmentCost(string normalized)
    {
        if (normalized.Length > PhraseMaxLength) return double.PositiveInfinity;
        // best[i, k]: i 文字目までの区切りの点。k = 1 なら最後が語 (助詞でない)。語のすぐ後の語 (kyou|hame) は +1 (助詞を挟む文を選ぶ)
        var best = new double[normalized.Length + 1, 2];
        for (var i = 0; i <= normalized.Length; i++) best[i, 0] = best[i, 1] = double.PositiveInfinity;
        best[0, 0] = 0;
        for (var i = 0; i < normalized.Length; i++)
        {
            for (var k = 0; k < 2; k++)
            {
                if (double.IsPositiveInfinity(best[i, k])) continue;
                for (var j = i + 1; j <= Math.Min(normalized.Length, i + 24); j++)
                {
                    var w = normalized[i..j];
                    if (Particles.Contains(w)) best[j, 0] = Math.Min(best[j, 0], best[i, k] + (CommonParticles.Contains(w) ? 0.5 : 1.0));
                    else if (_japanese.Contains(w)) best[j, 1] = Math.Min(best[j, 1], best[i, k] + (w.Length <= 2 ? 3 : 1) + k * AdjacentWordCost);
                }
            }
        }
        return Math.Min(best[normalized.Length, 0], best[normalized.Length, 1]);
    }

    public double AdjacentWordCost { get; set; } = 1.0;

    /// <summary>区切れたとみなす点の上限 (だいたい 3 文字に 1 区切りまで)。</summary>
    private static double PhraseMaxCost(string normalized) => Math.Max(2, normalized.Length / 3.0);

    /// <summary>文単位の直しで、区切りの点の重み (距離の重み α と比べる)。</summary>
    public double PhraseSegmentWeight { get; set; } = 2.0;

    /// <summary>文単位の直しの距離の重み。</summary>
    public double PhraseAlpha { get; set; } = 6.0;

    /// <summary>文単位の直しに要る、1 位と 2 位の点数の差。</summary>
    public double PhraseTheta { get; set; } = 1.0;

    /// <summary>
    /// 日本語の文の打ち間違いを、1 か所の編集 (入れ替え・隣のキー・余分な文字) で直す。
    /// 直した結果が語と助詞に区切れて、2 番目によい直しより θ 以上よいときだけ直す。
    /// </summary>
    public Result CorrectPhrase(string input)
    {
        var n = Normalize(input);
        if (n.Length < PhraseMinLength || n.Length > PhraseMaxLength || !double.IsPositiveInfinity(SegmentCost(n)) && SegmentCost(n) <= PhraseMaxCost(n))
            return Keep(input, Language.Japanese);
        var scored = new List<Candidate>();
        var longVowels = LongVowelInsertions(n).ToHashSet();
        foreach (var v in SingleEdits(n).Concat(longVowels).Distinct())
        {
            var seg = SegmentCost(v);
            // 1 語に収まる直し (odakyu → odaku) は語の直しに任せる。ここは 2 語以上に区切れる文だけ
            if (seg > PhraseMaxCost(v) || _japanese.Contains(v)) continue;
            var dist = Distance(n, v, japanese: true);
            scored.Add(new Candidate(v, Language.Japanese, dist, -PhraseAlpha * dist - PhraseSegmentWeight * seg));
        }
        if (scored.Count == 0) return Keep(input, Language.Japanese);
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        var margin = scored.Count > 1 ? scored[0].Score - scored[1].Score : double.PositiveInfinity;
        if (margin < PhraseTheta) return new Result(input, input, Language.Japanese, false, margin, scored);
        // 抜けた文字を足す直しは、候補として比べるが選ばない。足す文字の候補が多く、どこにでも語が作れてしまい、直し間違いが一番多かった (抜け: 直し間違い 13%)
        // 余分な文字を消す直し (重複を除く) も、同じ距離の「抜けた文字を足す」直しと見分けられないので選ばない。
        // 選ぶのは、隣のキー (.4)・入れ替え (.5)・重複 (.3) で説明できるものだけ。
        // 長音の u を足す直し (2 か所まで) は選んでよい。打ち間違いでなく、打ち方の癖なので見分けやすい
        var chosen = scored[0].Word;
        var isLongVowel = longVowels.Contains(chosen) || longVowels.Any(l => l.Length == chosen.Length && Distance(l, chosen, true) < InsertCost);
        if (!isLongVowel && (chosen.Length > n.Length || scored[0].Distance >= InsertCost)) return new Result(input, input, Language.Japanese, false, margin, scored);
        var output = scored[0].Word.Replace("N", "nn");
        return new Result(input, output, Language.Japanese, true, margin, scored);
    }

    /// <summary>o・u の後に長音の u を 1〜2 か所足したもの (shutchochu → shutchouchuu)。</summary>
    private static IEnumerable<string> LongVowelInsertions(string s)
    {
        var spots = Enumerable.Range(1, s.Length).Where(i => s[i - 1] is 'o' or 'u' && (i == s.Length || s[i] != 'u')).ToList();
        foreach (var a in spots)
        {
            yield return s.Insert(a, "u");
            foreach (var b in spots.Where(b => b > a)) yield return s.Insert(b, "u").Insert(a, "u");
        }
    }

    private static IEnumerable<string> SingleEdits(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            yield return s.Remove(i, 1);
            if (i + 1 < s.Length) yield return s[..i] + s[i + 1] + s[i] + s[(i + 2)..];
            foreach (var k in Qwerty.Neighbors(s[i])) yield return s[..i] + k + s[(i + 1)..];
        }
        for (var i = 0; i <= s.Length; i++)
            foreach (var k in "aiueokstnhmyrwgzdbpjfcN")
                yield return s[..i] + k + s[i..];
    }

    public double ShortWordExtra { get; set; } = 1.5;
    public double ValidRomajiExtra { get; set; } = 1.5;
    /// <summary>隣のキー・入れ替え・重複のどれでもない直し (距離 .7 以上) に足す差。辞書に無い語 (mlops → mops) を守る。</summary>
    public double CostlyEditExtra { get; set; } = 0.5;

    private static readonly HashSet<string> Syllables = BuildSyllables();

    private static HashSet<string> BuildSyllables()
    {
        var set = new HashSet<string>(KanaTable.Values, StringComparer.Ordinal) { "N" };
        foreach (var r in KanaTable.Values.Where(r => r.EndsWith('i') && r.Length >= 2).ToList())
            foreach (var v in "auo")
                set.Add(r is "shi" or "chi" or "ji" ? r[..^1] + v : r[..^1] + "y" + v);
        return set;
    }

    /// <summary>正規化したローマ字が、残らずかなに読めるか (促音の重ねた子音を含む)。</summary>
    public static bool IsRomaji(string normalized)
    {
        var i = 0;
        while (i < normalized.Length)
        {
            if (i + 1 < normalized.Length && normalized[i] == normalized[i + 1] && !"aiueoN".Contains(normalized[i])) { i++; continue; }
            if (i + 2 < normalized.Length && normalized[i] == 't' && normalized[i + 1] == 'c' && normalized[i + 2] == 'h') { i++; continue; }
            var matched = 0;
            for (var len = 3; len >= 1 && matched == 0; len--)
                if (i + len <= normalized.Length && Syllables.Contains(normalized.Substring(i, len))) matched = len;
            if (matched == 0) return false;
            i += matched;
        }
        return true;
    }

    private bool Has(string w) => _japanese.Contains(w) || _english.Contains(w);

    private static Result Keep(string input, Language language) => new(input, input, language, false, double.PositiveInfinity, []);

    private double Score(double distance, string word, Language language, Language? previous)
    {
        var prior = previous is null ? 0.5 : previous == language ? SameLanguagePrior : 1 - SameLanguagePrior;
        var model = language == Language.Japanese ? _japaneseModel : _englishModel;
        return -Alpha * distance + model.LogProbabilityPerChar(word) * 2 + Math.Log(prior);
    }

    /// <summary>確定した直し (入力 → 確定) を覚え、その置換を安くする。</summary>
    public void Learn(string typed, string confirmed) => AdjustPairs(typed, confirmed, 0.8);

    /// <summary>直した直後に BackSpace で戻された直し (入力 → 直した語) は、その置換を高くし、入力をそれからは直さない語にする。</summary>
    public void Reject(string typed, string proposed)
    {
        AdjustPairs(typed, proposed, 1.5);
        _protected.Add(typed.ToLowerInvariant());
    }

    private void AdjustPairs(string typed, string target, double factor)
    {
        var a = Normalize(typed);
        var b = Normalize(target);
        if (a.Length != b.Length) return;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            var pair = (a[i], b[i]);
            var current = _learned.GetValueOrDefault(pair, 1.0);
            _learned[pair] = Math.Clamp(current * factor, 0.3, 3.0);
        }
    }

    /// <summary>重み付きの Damerau-Levenshtein (隣り合う入れ替えだけを許す、制限付きの版)。</summary>
    public double Distance(string a, string b, bool japanese = false)
    {
        var d = new double[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i == 0 ? 0 : (i - 1 + FirstCharFactor) * InsertCost;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j * DeleteCost * FirstCharFactor;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                // 入力 a に余分な文字がある (挿入の打ち間違い)。直前と同じ文字の重複は安い。
                // 先頭の文字の打ち間違いはまれなので、先頭を変える直しは高くする (iphone → phone、emoji → moji を防ぐ)。
                var first = i == 1 ? FirstCharFactor : 1;
                var extra = a[i - 1] == (i >= 2 ? a[i - 2] : '\0') ? DuplicateCost : InsertCost * first;
                // 日本語の長音の u の抜け (shucchou を shuccho、chuu を chu と打つ) は安い
                var missing = japanese && b[j - 1] == 'u' && j >= 2 && b[j - 2] is 'o' or 'u' ? LongVowelCost : DeleteCost;
                var v = Math.Min(d[i - 1, j] + extra, d[i, j - 1] + missing);
                v = Math.Min(v, d[i - 1, j - 1] + SubstituteCost(a[i - 1], b[j - 1]) * first);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    v = Math.Min(v, d[i - 2, j - 2] + TransposeCost);
                d[i, j] = v;
            }
        }
        return d[a.Length, b.Length];
    }

    public const double TransposeCost = 0.5;
    public const double DuplicateCost = 0.3;
    public const double InsertCost = 0.7;
    public const double DeleteCost = 0.7;
    public const double FirstCharFactor = 2.0;
    public const double LongVowelCost = 0.2;

    public double SubstituteCost(char typed, char intended)
    {
        if (typed == intended) return 0;
        var cost = Qwerty.Distance(typed, intended) <= 1.2 ? 0.4 : 1.0;
        return cost * _learned.GetValueOrDefault((typed, intended), 1.0);
    }

    /// <summary>削除を 0 〜 2 回した文字列 (SymSpell の前もって作る索引と、引くときの両方に使う)。</summary>
    public static IEnumerable<string> Deletes(string word)
    {
        var level = new HashSet<string>(StringComparer.Ordinal) { word };
        var all = new HashSet<string>(StringComparer.Ordinal);
        for (var n = 0; n < MaxDeletes; n++)
        {
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in level)
            {
                if (w.Length <= 1) continue;
                for (var i = 0; i < w.Length; i++)
                {
                    var d = w.Remove(i, 1);
                    if (all.Add(d)) next.Add(d);
                }
            }
            level = next;
        }
        return all;
    }

    /// <summary>QWERTY のキーの位置。行ごとのずれは 0 / .25 / .75。</summary>
    public static class Qwerty
    {
        private static readonly string[] Rows = ["qwertyuiop", "asdfghjkl", "zxcvbnm"];
        private static readonly double[] Offsets = [0, 0.25, 0.75];

        public static double Distance(char a, char b)
        {
            if (!TryPosition(char.ToLowerInvariant(a), out var pa) || !TryPosition(char.ToLowerInvariant(b), out var pb)) return double.MaxValue;
            var dx = pa.X - pb.X;
            var dy = pa.Y - pb.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static bool TryPosition(char c, out (double X, double Y) position)
        {
            for (var r = 0; r < Rows.Length; r++)
            {
                var i = Rows[r].IndexOf(c);
                if (i >= 0) { position = (i + Offsets[r], r); return true; }
            }
            position = default;
            return false;
        }

        /// <summary>隣のキー (距離 1.2 以下) のどれか。評価の雑音作りに使う。</summary>
        public static IEnumerable<char> Neighbors(char c)
        {
            foreach (var row in Rows)
                foreach (var k in row)
                    if (k != c && Distance(c, k) <= 1.2) yield return k;
        }
    }

    /// <summary>文字 4-gram (Witten-Bell ではなく、加算 1 の後ろに引く簡単な版)。語の頭と終わりに印を付ける。</summary>
    private sealed class NGramModel
    {
        private const int N = 4;
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _contexts = new(StringComparer.Ordinal);
        private const int Vocabulary = 29;

        public NGramModel(IEnumerable<string> words)
        {
            foreach (var w in words)
            {
                var s = new string('^', N - 1) + w + "$";
                for (var i = N - 1; i < s.Length; i++)
                    for (var k = 1; k <= N; k++)
                    {
                        var gram = s.Substring(i - k + 1, k);
                        _counts[gram] = _counts.GetValueOrDefault(gram) + 1;
                        _contexts[gram[..^1]] = _contexts.GetValueOrDefault(gram[..^1]) + 1;
                    }
            }
        }

        public double LogProbabilityPerChar(string word)
        {
            var s = new string('^', N - 1) + word + "$";
            var total = 0.0;
            for (var i = N - 1; i < s.Length; i++)
            {
                // 長い文脈から順に、見たことのあるものを使う (後ろに引くたびに log .4 の罰)。
                var p = 0.0;
                var penalty = 0.0;
                for (var k = N; k >= 1; k--)
                {
                    var gram = s.Substring(i - k + 1, k);
                    var c = _counts.GetValueOrDefault(gram);
                    if (c > 0 || k == 1)
                    {
                        p = (c + 0.01) / (_contexts.GetValueOrDefault(gram[..^1]) + 0.01 * Vocabulary);
                        break;
                    }
                    penalty += Math.Log(0.4);
                }
                total += Math.Log(p) + penalty;
            }
            return total / (word.Length + 1);
        }
    }
}
