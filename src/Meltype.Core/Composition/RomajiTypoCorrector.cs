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
    private readonly HashSet<string> _words = new(StringComparer.Ordinal);
    private int _maxLength;

    public RomajiTypoCorrector(RomajiDetector romaji, string readings)
    {
        _romaji = romaji;
        foreach (var word in FunctionWords) Add(word);
        foreach (var line in Detection.DictionarySource.DataLines(readings))
        {
            var parts = line.Split('\t');
            Add(parts[0]);
            if (parts.Length > 1) AddConjugations(parts[0], parts[1]);
        }
    }

    public static RomajiTypoCorrector Load(RomajiDetector romaji) => new(romaji, DictionarySource.ReadEmbedded("readings.txt"));

    public int Count => _words.Count;

    /// <summary>読みがよく使う語 (または付属語・活用した形) か。</summary>
    public bool IsWord(string hiragana) => _words.Contains(hiragana);

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

    private static readonly HashSet<string> Function = new(FunctionWords, StringComparer.Ordinal);

    private void Add(string word)
    {
        if (word.Length == 0 || !_words.Add(word)) return;
        _maxLength = Math.Max(_maxLength, word.Length);
    }

    /// <summary>活用する語の、付属語が続く形 (たべ、かき・かか・かい、はやく・はやかっ …)。</summary>
    private void AddConjugations(string word, string kind)
    {
        if (word.Length < 2) return;
        var stem = word[..^1];
        switch (kind)
        {
            case "v1":
                if (word[^1] == 'る') Add(stem);
                break;
            case "i":
                if (word[^1] != 'い') break;
                foreach (var ending in (string[])["く", "かっ", "けれ", "さ", "そう"]) Add(stem + ending);
                if (word == "いい") foreach (var ending in (string[])["よく", "よかっ", "よけれ"]) Add(ending);
                break;
            case "vk":
                if (word.EndsWith("くる")) foreach (var form in (string[])["き", "こ", "く", "くれ"]) Add(word[..^2] + form);
                break;
            case "vs":
                if (word.EndsWith("する")) foreach (var form in (string[])["し", "さ", "せ", "すれ", "しろ"]) Add(word[..^2] + form);
                break;
            default:
                if (!kind.StartsWith("v5") || !Rows.TryGetValue(word[^1], out var row)) break;
                foreach (var kana in row) Add(stem + kana);
                // 音便 (かい-た、およい-だ、かっ-た、よん-だ、いっ-た)
                var onbin = kind switch
                {
                    "v5k" => "い",
                    "v5g" => "い",
                    "v5k-s" or "v5t" or "v5r" or "v5u" or "v5aru" or "v5r-i" or "v5u-s" => "っ",
                    "v5b" or "v5m" or "v5n" => "ん",
                    _ => null,
                };
                if (onbin is not null) Add(stem + onbin);
                // ござる・いらっしゃる・なさる は ます の前が い (ございます)
                if (kind == "v5aru") Add(stem + "い");
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
                var piece = kana.Substring(i - length, length);
                double step;
                if (_words.Contains(piece)) step = Function.Contains(piece) ? 0.8 : length >= 2 ? 1 : 1.5;
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
            var piece = kana[from[i]..i];
            known = _words.Contains(piece) && (piece.Length >= 2 || Function.Contains(piece));
            break;
        }
        return (cost[n], known);
    }
}
