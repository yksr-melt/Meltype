// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>ローマ字の打ち間違いを直した結果。Start と Length は直す前の英字の並びの中の位置。</summary>
public readonly record struct RomajiFix(string Wrong, string Right, string Kana);

/// <summary>
/// ローマ字の打ち間違い (onegaishimsu → onegaishimasu、arigatpu → arigatou、sumimasne → sumimasen) を直す。
/// ローマ字として読めない子音が残ったところだけを見て、その近くを 1 文字だけ直した綴り
/// (隣のキーとの打ち間違い・入れ替わり・抜け・余計な 1 文字) のうち、よく使う語の読みでいちばん自然に区切れるものを選ぶ。
/// 直したところが知っている語 (2 文字以上か、ます・です・て などの付属語) に入らなければ直さない。
/// </summary>
public sealed class RomajiTypoCorrector
{
    private readonly RomajiDetector _romaji;
    // よく使う語の読み (Load で作ったときは、かな入力の判定と同じ CommonReadings を共有する) と、付属語・活用した形
    private readonly PackedWordSet _readings;
    private readonly PackedWordSet _forms;
    private readonly int _maxLength;

    public RomajiTypoCorrector(RomajiDetector romaji, string readings) : this(romaji, PackedWordSet.FromLines(readings, firstField: true), readings)
    {
    }

    private RomajiTypoCorrector(RomajiDetector romaji, PackedWordSet readings, string readingsText) : this(romaji, readings, BuildForms(readingsText, readings))
    {
    }

    private RomajiTypoCorrector(RomajiDetector romaji, PackedWordSet readings, PackedWordSet forms)
    {
        _romaji = romaji;
        _readings = readings;
        _forms = forms;
        _maxLength = Math.Max(_readings.MaxLength, _forms.MaxLength);
        Count = _readings.Count + _forms.Count;
    }

    private static PackedWordSet BuildForms(string readingsText, PackedWordSet readings)
    {
        var forms = new List<string>(FunctionWords);
        foreach (var line in Detection.DictionarySource.DataLines(readingsText))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            // 同じ読みで活用の違う語 (くる = 繰る v5r・来る vk) は , でつないである
            foreach (var kind in parts[1].Split(',')) AddConjugations(parts[0], kind, forms);
        }
        return PackedWordSet.FromWords(forms.Where(form => !readings.Contains(form)));
    }

    // 同梱の読み (CommonReadings) から作った付属語・活用した形。Load で作る打ち間違い直しと、日英の区切り (CompositionDetector) で共有する
    private static readonly Lazy<PackedWordSet> CommonForms = new(() => BuildForms(CommonReadings.ReadText(), CommonReadings.Words));

    public static RomajiTypoCorrector Load(RomajiDetector romaji) => new(romaji, CommonReadings.Words, CommonForms.Value);

    public int Count { get; }

    /// <summary>読みがよく使う語 (または付属語・活用した形) か。</summary>
    public bool IsWord(string hiragana) => IsWord(hiragana.AsSpan());

    private bool IsWord(ReadOnlySpan<char> hiragana) => _readings.Contains(hiragana) || _forms.Contains(hiragana);

    /// <summary>同梱の読みで、kana を 1 語として読むときの重み (WordCost)。語でなければ null。</summary>
    internal static double? CommonWordCost(ReadOnlySpan<char> kana) =>
        CommonReadings.Words.Contains(kana) || CommonForms.Value.Contains(kana) ? WordCost(kana) : null;

    /// <summary>kana が、同梱の読み・付属語・活用した形のどれかの頭か。</summary>
    internal static bool CommonHasPrefix(ReadOnlySpan<char> kana) => CommonReadings.Words.HasPrefix(kana) || CommonForms.Value.HasPrefix(kana);

    // 語の生起コスト。日英の区切り (ReadingLattice.cs) と共有する
    // 付属語 (が・を・て)
    private const double FunctionWordCost = 0.8;
    // 2 文字以上の語
    private const double WordCostPerWord = 1;
    // 1 文字の語 (め・き)
    private const double SingleKanaWordCost = 1.5;

    private static double WordCost(ReadOnlySpan<char> word) => Function.Contains(word) ? FunctionWordCost : word.Length >= 2 ? WordCostPerWord : SingleKanaWordCost;

    // 助詞・助動詞・よく付く語。1 文字でも語として数える。
    private static readonly string[] FunctionWords =
    [
        "は", "が", "を", "に", "で", "と", "も", "へ", "の", "や", "か", "ね", "よ", "な", "わ", "さ", "ぞ", "ぜ",
        "だ", "です", "でし", "でしょ", "ます", "まし", "ませ", "ましょ", "た", "て", "で", "だっ", "ない", "なかっ", "なく", "なけれ",
        "たい", "たく", "たかっ", "れる", "れ", "られ", "られる", "せる", "せ", "させ", "させる", "ば", "う", "よう", "ん",
        "し", "する", "さ", "しろ", "すれ", "して", "した", "しない", "こと", "もの", "ので", "から", "まで", "より", "けど", "けれど",
        "ながら", "って", "という", "でも", "ください", "くださ", "ちゃ", "じゃ", "じゃない", "ちゃう", "いる", "い", "いた", "いて",
        "ある", "あり", "あっ", "おり", "おる", "ござい", "ございます", "だけ", "しか", "ほど", "くらい", "ぐらい", "など", "なら",
        "たら", "だら", "たり", "だり", "ずつ", "そう", "らしい", "みたい", "ほう", "とき", "ところ", "ため", "はず", "わけ",
        "ました", "ません", "ましょう", "ませんでした", "でした", "でしょう", "ている", "ていた", "てい", "てる", "てた", "ちゃった", "なかった", "たかった",
        "ないで", "なくて", "られた", "れた", "ください", "でしょうか", "ですか", "ますか", "ですね", "ますね", "ですよ", "ますよ",
    ];

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> Function =
        new HashSet<string>(FunctionWords, StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>活用する語の、付属語が続く形 (たべ、かき・かか・かい、はやく・はやかっ …)。</summary>
    private static void AddConjugations(string word, string kind, List<string> forms)
    {
        if (word.Length < 2) return;
        var stem = word[..^1];
        switch (kind)
        {
            case "v1":
                if (word[^1] != 'る') break;
                // て・た 形も 1 語にする: 語幹が 1 文字 (み・ね) だと 1 文字の語の重みになり、み|て が英単語 mite に負ける
                foreach (var ending in (string[])["", "て", "た"]) forms.Add(stem + ending);
                break;
            case "i":
                if (word[^1] != 'い') break;
                foreach (var ending in (string[])["く", "かっ", "けれ", "さ", "そう"]) forms.Add(stem + ending);
                if (word == "いい") foreach (var ending in (string[])["よく", "よかっ", "よけれ"]) forms.Add(ending);
                break;
            case "vk":
                if (word.EndsWith("くる", StringComparison.Ordinal)) foreach (var form in (string[])["き", "きて", "きた", "こ", "く", "くれ"]) forms.Add(word[..^2] + form);
                break;
            case "vs":
                if (word.EndsWith("する", StringComparison.Ordinal)) foreach (var form in (string[])["し", "さ", "せ", "すれ", "しろ"]) forms.Add(word[..^2] + form);
                break;
            default:
                if (!kind.StartsWith("v5", StringComparison.Ordinal) || !Rows.TryGetValue(word[^1], out var row)) break;
                foreach (var kana in row) forms.Add(stem + kana);
                // 音便 (かい-た、およい-だ、かっ-た、よん-だ、いっ-た)
                var onbin = kind switch
                {
                    "v5k" => "い",
                    "v5g" => "い",
                    "v5k-s" or "v5t" or "v5r" or "v5u" or "v5aru" or "v5r-i" or "v5u-s" => "っ",
                    "v5b" or "v5m" or "v5n" => "ん",
                    _ => null,
                };
                if (onbin is not null) forms.Add(stem + onbin);
                // ござる・いらっしゃる・なさる は ます の前が い (ございます)
                if (kind == "v5aru") forms.Add(stem + "い");
                break;
        }
    }

    // 五段の語尾 → あ段・い段・え段・お段
    private static readonly Dictionary<char, string> Rows = new()
    {
        ['う'] = "わいえお", ['く'] = "かきけこ", ['ぐ'] = "がぎげご", ['す'] = "さしせそ", ['つ'] = "たちてと",
        ['ぬ'] = "なにねの", ['ぶ'] = "ばびべぼ", ['む'] = "まみめも", ['る'] = "らりれろ",
    };

    // QWERTY の隣のキー
    private static readonly Dictionary<char, string> Neighbors = new()
    {
        ['q'] = "wa", ['w'] = "qeas", ['e'] = "wrsd", ['r'] = "etdf", ['t'] = "ryfg", ['y'] = "tugh", ['u'] = "yihj", ['i'] = "uojk", ['o'] = "ipkl", ['p'] = "ol",
        ['a'] = "qwsz", ['s'] = "weadzx", ['d'] = "erfsxc", ['f'] = "rtdgcv", ['g'] = "tyfhvb", ['h'] = "yugjbn", ['j'] = "uihknm", ['k'] = "iojlm", ['l'] = "opk",
        ['z'] = "asx", ['x'] = "zsdc", ['c'] = "xdfv", ['v'] = "cfgb", ['b'] = "vghn", ['n'] = "bhjm", ['m'] = "njk",
    };

    private static bool IsVowel(char c) => c is 'a' or 'i' or 'u' or 'e' or 'o';

    /// <summary>
    /// 小文字の英字の並び letters (ローマ字として読めない子音を含む) を直す。直せなければ null。
    /// final でなければ、まだ打っている途中 (語末の子音はそのまま残してよい)。
    /// </summary>
    public RomajiFix? Fix(string letters, bool final)
    {
        var first = FirstUnreadable(letters, final);
        // 語頭の打ち間違いはまれで、英字 1 文字 + 日本語 (sだけが) のこともあるので見ない。
        if (first <= 0) return null;
        // 読めない子音の後ろに 2 文字以上打つまでは待つ (続きを打つと読めることがある: shimsu の s の後の u)。
        if (!final && letters.Length - first - 1 < 2) return null;
        // 語末に子音が 1 つ残っただけ (yoroshikuy) なら、隣のキーを一緒に押したときだけ消す
        // (teh を ては のように母音を足して直すと、英語の打ち間違いまで日本語にしてしまう)。
        if (first == letters.Length - 1 && _romaji.AnalyzeFragment(letters).IsValid)
        {
            var extra = letters[^1];
            if (!(Neighbors.TryGetValue(extra, out var around) && around.Contains(letters[^2]) || extra == letters[^2])) return null;
            var trimmed = letters[..^1];
            var kana = _romaji.ConvertLenient(trimmed, final: true);
            return Segment(kana, kana.Length - 1).Known ? new RomajiFix(letters, trimmed, kana) : null;
        }

        var candidates = new Dictionary<string, (double Cost, int Edit)>(StringComparer.Ordinal);
        void Consider(string fixedLetters, int edit, double prior)
        {
            if (FirstUnreadable(fixedLetters, final) >= 0) return;
            if (candidates.TryGetValue(fixedLetters, out var existing) && existing.Cost <= prior) return;
            candidates[fixedLetters] = (prior, edit);
        }
        for (var k = Math.Max(1, first - 2); k <= Math.Min(letters.Length, first + 2); k++)
        {
            if (k < letters.Length)
            {
                var c = letters[k];
                // 隣のキーを押した (arigatpu → arigatou)
                if (Neighbors.TryGetValue(c, out var near))
                {
                    foreach (var n in near) Consider(letters[..k] + n + letters[(k + 1)..], k, 0.3);
                }
                // 余計な 1 文字 (隣のキーを一緒に押した・同じキーを 2 回押した)
                var previous = letters[k - 1];
                var next = k + 1 < letters.Length ? letters[k + 1] : '\0';
                if (c == previous || near?.Contains(previous) == true || near?.Contains(next) == true)
                {
                    Consider(letters[..k] + letters[(k + 1)..], k, 0.5);
                }
                // 入れ替わり (sumimasne → sumimasen)
                if (k + 1 < letters.Length && letters[k] != letters[k + 1])
                {
                    Consider(letters[..k] + letters[k + 1] + letters[k] + letters[(k + 2)..], k, 0.3);
                }
            }
            // 1 文字抜けた (onegaishimsu → onegaishimasu)
            for (var n = 'a'; n <= 'z'; n++)
            {
                Consider(letters[..k] + n + letters[k..], k, IsVowel(n) ? 0.3 : 0.8);
            }
        }
        if (candidates.Count == 0) return null;

        var ranked = new List<(string Letters, string Kana, double Cost)>();
        foreach (var (candidate, (prior, edit)) in candidates)
        {
            var analysis = _romaji.AnalyzeFragment(candidate);
            var kana = analysis.Kana + (final && analysis.Partial == "n" ? "ん" : "");
            // 直したところのかなの位置
            var position = 0;
            var kanaPosition = 0;
            var editKana = -1;
            foreach (var token in analysis.Tokens)
            {
                if (edit < position + token.Romaji.Length)
                {
                    editKana = kanaPosition;
                    break;
                }
                position += token.Romaji.Length;
                kanaPosition += token.Kana.Length;
            }
            if (editKana < 0) editKana = kana.Length - 1;
            var (cost, known) = Segment(kana, editKana);
            if (!known) continue;
            ranked.Add((candidate, kana, cost + prior));
        }
        if (ranked.Count == 0) return null;
        ranked.Sort((a, b) => a.Cost.CompareTo(b.Cost));
        // 同じくらい自然な直し方が 2 つあるなら、どちらか分からないので直さない。
        if (ranked.Count > 1 && ranked[1].Cost - ranked[0].Cost < 0.2 && ranked[1].Kana != ranked[0].Kana) return null;
        // 直す前より自然でなければ直さない (読めない子音を英字 1 文字として数える)。
        var (before, _) = Segment(_romaji.ConvertLenient(letters, final), -1);
        if (ranked[0].Cost >= before) return null;
        return new RomajiFix(letters, ranked[0].Letters, ranked[0].Kana);
    }

    /// <summary>
    /// 英字の並びが、ローマ字としてよく使う日本語になるか (kyouha = きょう|は、sushi = すし)。
    /// 最後まで読めて、よく使う語の読みで区切ったとき、ほぼ 2 文字以上の語か付属語だけでできているもの。
    /// </summary>
    public bool IsCommonJapanese(string letters)
    {
        var lower = letters.ToLowerInvariant();
        var analysis = _romaji.AnalyzeFragment(lower);
        if (!analysis.IsValid || analysis.Partial is { Length: > 0 } and not "n") return false;
        var kana = analysis.Kana + (analysis.Partial == "n" ? "ん" : "");
        if (kana.Length < 2) return false;
        var (cost, _) = Segment(kana, -1);
        return cost <= kana.Length / 2.0;
    }

    /// <summary>ローマ字として読めない最初の文字の位置 (無ければ -1)。final でなければ語末の途中の子音は読めるとみなす。</summary>
    public int FirstUnreadable(string letters, bool final)
    {
        var analysis = _romaji.AnalyzeFragment(letters);
        if (analysis.IsValid) return final && analysis.Partial is { Length: > 0 } partial && partial != "n" ? letters.Length - partial.Length : -1;
        return analysis.Tokens.Sum(t => t.Romaji.Length);
    }

    /// <summary>
    /// かなをよく使う語の読みで区切ったときの不自然さ (区切りの数。知らない文字は 1 文字 3)。
    /// あわせて、at の位置のかなが知っている語 (2 文字以上か付属語) に入っているかを返す。
    /// </summary>
    private (double Cost, bool Known) Segment(string kana, int at)
    {
        var n = kana.Length;
        var cost = new double[n + 1];
        var from = new int[n + 1];
        for (var i = 1; i <= n; i++)
        {
            cost[i] = double.MaxValue;
            for (var length = Math.Min(i, _maxLength); length >= 1; length--)
            {
                var piece = kana.AsSpan(i - length, length);
                double step;
                if (IsWord(piece)) step = WordCost(piece);
                else if (length == 1) step = piece is "ー" or "っ" ? 0.5 : 3;
                else continue;
                if (cost[i - length] + step < cost[i])
                {
                    cost[i] = cost[i - length] + step;
                    from[i] = i - length;
                }
            }
        }
        var known = at < 0;
        for (var i = n; i > 0 && !known; i = from[i])
        {
            if (from[i] > at || i <= at) continue;
            var piece = kana.AsSpan(from[i], i - from[i]);
            known = IsWord(piece) && (piece.Length >= 2 || Function.Contains(piece));
            break;
        }
        return (cost[n], known);
    }
}
