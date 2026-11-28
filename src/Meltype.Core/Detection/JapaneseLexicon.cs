// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;
using Meltype.Libraries.Lexicon;

namespace Meltype.Detection;

/// <summary>
/// よく使う語の読み・付属語・活用した形の一覧と、語の重み。日英の区切り (ReadingLattice) の語の生起コストと、
/// 英字の並びがよく使う日本語になるか (学習の IsCommonJapanese) に使う。
/// </summary>
internal sealed class JapaneseLexicon
{
    private readonly RomajiDetector _romaji;
    // よく使う語の読み (Load で作ったときは、かな入力の判定と同じ CommonReadings を共有する) と、付属語・活用した形
    private readonly PackedWordSet _readings;
    private readonly PackedWordSet _forms;
    private readonly int _maxLength;

    public JapaneseLexicon(RomajiDetector romaji, string readings) : this(romaji, PackedWordSet.FromLines(readings, firstField: true), readings)
    {
    }

    private JapaneseLexicon(RomajiDetector romaji, PackedWordSet readings, string readingsText) : this(romaji, readings, BuildForms(readingsText, readings))
    {
    }

    private JapaneseLexicon(RomajiDetector romaji, PackedWordSet readings, PackedWordSet forms)
    {
        _romaji = romaji;
        _readings = readings;
        _forms = forms;
        _maxLength = Math.Max(_readings.MaxLength, _forms.MaxLength);
        Count = _readings.Count + _forms.Count;
    }

    private static PackedWordSet BuildForms(string readingsText, PackedWordSet readings) =>
        PackedWordSet.FromWords(FunctionWords.Concat(Conjugations(readingsText)).Where(form => !readings.Contains(form)));

    private static List<string> Conjugations(string readingsText)
    {
        var forms = new List<string>();
        foreach (var line in Libraries.Lexicon.DictionarySource.DataLines(readingsText))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            // 同じ読みで活用の違う語 (くる = 繰る v5r・来る vk) は , でつないである
            foreach (var kind in parts[1].Split(',')) AddConjugations(parts[0], kind, forms);
        }
        return forms;
    }

    // 同梱の読み (CommonReadings) から作った付属語・活用した形。Load で作る一覧と、日英の区切り (ReadingLattice) で共有する
    private static readonly Lazy<PackedWordSet> CommonForms = new(() => BuildForms(CommonReadings.ReadText(), CommonReadings.Words));

    // readings-rare.txt の語の活用した形 (かんれんづけ)
    private static readonly Lazy<PackedWordSet> RareForms = new(() =>
        // 短い読みを足しても、そこから 2 文字以下の活用形 (くべる → くべ = cube) は作らない。
        PackedWordSet.FromWords(Conjugations(CommonReadings.RareText()).Where(form => form.Length >= 3 && !CommonReadings.Rare.Contains(form))));

    // 同梱の読み (readings-rare.txt も含む) のうち活用する語の、終止形・連体形 (たべる・はやい)・活用した形 (たべ・かい・まい)・未然形 (たべ・かか・こ・さ)。
    // 活用した形は、よく使う語の読みと同じ綴り (まい = 舞う の連用形・枚) も含む
    private static readonly Lazy<(PackedWordSet Inflecting, PackedWordSet Inflected, PackedWordSet Irrealis)> Inflections = new(() =>
    {
        List<string> inflecting = [], inflected = [], irrealis = [];
        foreach (var line in Libraries.Lexicon.DictionarySource.DataLines(CommonReadings.ReadText() + "\n" + CommonReadings.RareText()))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            inflecting.Add(parts[0]);
            foreach (var kind in parts[1].Split(','))
            {
                AddConjugations(parts[0], kind, inflected);
                irrealis.AddRange(Irrealis(parts[0], kind));
            }
        }
        return (PackedWordSet.FromWords(inflecting), PackedWordSet.FromWords(inflected), PackedWordSet.FromWords(irrealis));
    });

    // 未然形 (ない・ん が続く形)
    private static string[] Irrealis(string word, string kind) => word.Length < 2 ? [] : kind switch
    {
        "v1" when word[^1] == 'る' => [word[..^1]],
        "vk" when word.EndsWith("くる", StringComparison.Ordinal) => [word[..^2] + "こ"],
        "vs" when word.EndsWith("する", StringComparison.Ordinal) => [word[..^2] + "さ", word[..^2] + "せ"],
        _ when kind.StartsWith("v5", StringComparison.Ordinal) && Rows.TryGetValue(word[^1], out var row) => [word[..^1] + row[0]],
        _ => [],
    };

    // 動詞の て形の後ろにしか付かない付属語
    private static readonly HashSet<string> TeFormAuxiliaries = ["てる", "てた", "ている", "てい", "ていた", "ちゃう", "ちゃった"];

    /// <summary>
    /// 付属語 kana を、直前の語 previous (英単語なら null) に付けられないか。ん は未然形・連体形の後ろ (いかん・いくん)、
    /// てる・ちゃう は活用した形の後ろにしか付かないので、英単語と活用しない語の後ろ (main|ん・まい|ん・のち|てる) では付属語として読まない。
    /// 直前が付属語 (ない|ん・じゃ|ん) か、ー で終わる感動詞 (あー|ん) なら付けられる。
    /// </summary>
    internal static bool CannotFollow(string? previous, string kana)
    {
        if (kana != "ん" && !TeFormAuxiliaries.Contains(kana)) return false;
        if (previous is null) return true;
        if (IsFunctionWord(previous) || previous.EndsWith('ー')) return false;
        var (inflecting, inflected, irrealis) = Inflections.Value;
        return kana == "ん" ? !inflecting.Contains(previous) && !irrealis.Contains(previous) : !inflected.Contains(previous);
    }

    public static JapaneseLexicon Load(RomajiDetector romaji) => new(romaji, CommonReadings.Words, CommonForms.Value);

    public int Count { get; }

    /// <summary>読みがよく使う語 (または付属語・活用した形) か。</summary>
    public bool IsWord(string hiragana) => IsWord(hiragana.AsSpan());

    private bool IsWord(ReadOnlySpan<char> hiragana) => _readings.Contains(hiragana) || _forms.Contains(hiragana);

    /// <summary>同梱の読みで、kana を 1 語として読むときの重み (WordCost)。語でなければ null。</summary>
    internal static double? CommonWordCost(ReadOnlySpan<char> kana) =>
        CommonReadings.Words.Contains(kana) || CommonForms.Value.Contains(kana) ? WordCost(kana) : null;

    /// <summary>kana が、同梱の読み・付属語・活用した形のどれかの頭か。</summary>
    internal static bool CommonHasPrefix(ReadOnlySpan<char> kana) => CommonReadings.Words.HasPrefix(kana) || CommonForms.Value.HasPrefix(kana);

    /// <summary>kana が、よく使う語でない語の読み (readings-rare.txt) かその活用した形か。</summary>
    internal static bool IsRareWord(ReadOnlySpan<char> kana) => CommonReadings.Rare.Contains(kana) || RareForms.Value.Contains(kana);

    /// <summary>kana が、よく使う語でない語の読みかその活用した形の頭か。</summary>
    internal static bool RareHasPrefix(ReadOnlySpan<char> kana) => CommonReadings.Rare.HasPrefix(kana) || RareForms.Value.HasPrefix(kana);

    // 語の生起コスト。日英の区切り (ReadingLattice.cs) と共有する
    // 付属語 (が・を・て)
    private const double FunctionWordCost = 0.8;
    // 2 文字以上の語
    private const double WordCostPerWord = 1;
    // 1 文字の語 (め・き)
    private const double SingleKanaWordCost = 1.5;

    private static double WordCost(ReadOnlySpan<char> word) => Function.Contains(word) ? FunctionWordCost : word.Length >= 2 ? WordCostPerWord : SingleKanaWordCost;

    /// <summary>読みが付属語・助動詞か (が・を・て・ので)。</summary>
    internal static bool IsFunctionWord(ReadOnlySpan<char> kana) => Function.Contains(kana);

    // 助詞・助動詞・よく付く語。1 文字でも語として数える。
    private static readonly string[] FunctionWords =
    [
        "は", "が", "を", "に", "で", "と", "も", "へ", "の", "や", "か", "ね", "よ", "な", "わ", "さ", "ぞ", "ぜ",
        "だ", "です", "でし", "でしょ", "ます", "まし", "ませ", "ましょ", "た", "て", "で", "だっ", "ない", "なかっ", "なく", "なけれ",
        "たい", "たく", "たかっ", "れる", "れ", "られ", "られる", "せる", "せ", "させ", "させる", "ば", "う", "よう", "ん",
        "し", "する", "さ", "しろ", "すれ", "して", "した", "しない", "こと", "もの", "ので", "から", "まで", "より", "けど", "けれど",
        "ながら", "って", "という", "では", "でも", "ください", "くださ", "ちゃ", "じゃ", "じゃない", "ちゃう", "いる", "い", "いた", "いて",
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
                // て・た 形 (と て の縮まった ちゃ・ちゃった の ちゃっ) も 1 語にする: 語幹が 1 文字 (み・き) だと 1 文字の語の重みになり、
                // み|て が英単語 mite に、き|ちゃ が kit + や に負ける
                // 命令形 (みろ・たべろ) も、ろ が 1 文字の語にならないよう 1 語にする (みろ が英単語 miro に負ける)
                // て形 + る (おちてる) も 1 語にする: おち|てる の 2 語だと、英単語の後ろ (main|おちてる) で まい|の… に負ける
                foreach (var ending in (string[])["", "て", "た", "てる", "ちゃ", "ちゃっ", "ろ"]) forms.Add(stem + ending);
                break;
            case "i":
                if (word[^1] != 'い') break;
                foreach (var ending in (string[])["く", "かっ", "けれ", "さ", "そう"]) forms.Add(stem + ending);
                if (word == "いい") foreach (var ending in (string[])["よく", "よかっ", "よけれ"]) forms.Add(ending);
                break;
            case "vk":
                if (word.EndsWith("くる", StringComparison.Ordinal)) foreach (var form in (string[])["き", "きて", "きた", "きちゃ", "きちゃっ", "こ", "く", "くれ"]) forms.Add(word[..^2] + form);
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
                if (onbin is not null)
                {
                    forms.Add(stem + onbin);
                    // 音便の て・た 形と て形 + る (まって・よんで・まってる) も 1 語にする: まっ|て に分けると、英単語 (matte) 1 語と同じ重みにしかならない
                    var voiced = kind is "v5g" or "v5b" or "v5m" or "v5n";
                    forms.Add(stem + onbin + (voiced ? "で" : "て"));
                    forms.Add(stem + onbin + (voiced ? "だ" : "た"));
                    forms.Add(stem + onbin + (voiced ? "でる" : "てる"));
                }
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
        return Segment(kana) <= kana.Length / 2.0;
    }

    /// <summary>かなをよく使う語の読みで区切ったときの不自然さ (区切りの数。知らない文字は 1 文字 3)。</summary>
    private double Segment(string kana)
    {
        var n = kana.Length;
        var cost = new double[n + 1];
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
                cost[i] = Math.Min(cost[i], cost[i - length] + step);
            }
        }
        return cost[n];
    }
}
