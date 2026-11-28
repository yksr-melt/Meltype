// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Libraries.Lexicon;

namespace Meltype.Detection;

/// <summary>
/// 変換ボックスの中身のうち、どこを英字のまま見せるかを決める。
///
/// 日本語入力中に google と打って「ごおｇぇ」になるのを防ぐのが目的。英語かどうかは区間ごとに判定するので、
/// 「きょうは google」なら google の部分だけが英字になる。区切り方は <see cref="ReadingLattice"/>。
/// </summary>
internal sealed class CompositionDetector
{
    public CompositionDetector(RomajiDetector romaji, EnglishDetector english, ProperNouns? proper = null, KanaDetector? kana = null,
        IEnumerable<string>? userJapaneseWords = null, LinkWeights? weights = null)
    {
        Words = new WordJudge(romaji, english, proper, kana, userJapaneseWords);
        Lattice = new ReadingLattice(Words, weights ?? LinkWeights.Default);
    }

    /// <summary>語の判定。</summary>
    internal WordJudge Words { get; }

    /// <summary>日英の区切りのラティス。</summary>
    internal ReadingLattice Lattice { get; }

    public static CompositionDetector CreateDefault(string? userDictionaryDirectory = null) => CreateDefault(userDictionaryDirectory, LinkWeights.Default);

    /// <summary>辺をつなぐ重みを変えて作る (重みを探す道具 --tune-weights が使う)。</summary>
    internal static CompositionDetector CreateDefault(string? userDictionaryDirectory, LinkWeights weights)
    {
        var romaji = RomajiDetector.CreateDefault(userDictionaryDirectory);
        var japaneseWords = DictionarySource.Load("japanese.txt", userDictionaryDirectory).ToList();
        var proper = ProperNouns.Load(userDictionaryDirectory);
        var english = new EnglishDetector(DictionarySource.Load("english.txt", userDictionaryDirectory).Concat(proper.LowercaseWords));
        return new CompositionDetector(romaji, english, proper, new KanaDetector(japaneseWords, romaji),
            DictionarySource.LoadUser("japanese.txt", userDictionaryDirectory), weights);
    }

    public RomajiDetector Romaji => Words.Romaji;

    public ProperNouns ProperNouns => Words.ProperNouns;

    /// <summary>
    /// 単位列 (+ 入力途中の子音) を英語区間と日本語区間に分ける。
    /// </summary>
    /// <param name="precedingEnglish">入力欄のキャレットの前の確定済みの文字が英語なら true、日本語なら false、分からなければ null。</param>
    /// <param name="followingEnglish">キャレットの後ろの文字が英語なら true、日本語なら false、分からなければ null。</param>
    /// <param name="manual">自動判定を使わない (設定 AutoDetection が OFF)。Shift で打った大文字始まりの語だけを英語にする。</param>
    /// <param name="englishSentence">キャレットの前が英文 (空白で区切った英単語が 2 語以上続いて空白で終わる: "I want ")。
    /// 日本語の文の中の英単語 (GitHub の) より強い英語の根拠として扱う。</param>
    /// <param name="punctuateAfterEnglish">英単語のすぐ後ろの , . を句読点にする。false なら、英字だけの入力の , . は半角のまま。</param>
    /// <param name="reasons">渡したら、区切りを選んだ理由を足す (確定のログ用。issue #1644)。文字列にするのはログに書くときだけ。</param>
    internal IReadOnlyList<CompositionSegment> Segment(LanguageLearning learning, IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish = null, bool? followingEnglish = null,
        bool manual = false, bool englishSentence = false, bool kanaInput = false, bool punctuateAfterEnglish = false, List<Func<string>>? reasons = null) =>
        SegmentAlternatives(learning, units, pending, precedingEnglish, followingEnglish, manual, englishSentence, kanaInput, punctuateAfterEnglish, 1, reasons)[0];

    /// <summary>
    /// <see cref="Segment"/> の区切りと、その別案 (軽い順に count 案まで)。先頭は <see cref="Segment"/> と同じ。
    /// 別案は、ラティスの上位案と、打った英字をすべてローマ字として読んだ案 (botto → ぼっと)。記号の入った語は別案を出さない。
    /// </summary>
    internal IReadOnlyList<IReadOnlyList<CompositionSegment>> SegmentAlternatives(LanguageLearning learning, IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish,
        bool? followingEnglish, bool manual, bool englishSentence, bool kanaInput, bool punctuateAfterEnglish, int count, List<Func<string>>? reasons = null)
    {
        var prepared = Prepared.Of(units);
        var token = prepared.Raw + pending;
        if (IsOpaqueToken(token, kanaInput))
        {
            reasons?.Add(() => "記号の入った語 (メールアドレス・URL・_ の入った名前など) なので英字のまま");
            return [[new CompositionSegment(true, "", token)]];
        }
        var alternatives = Lattice.FindSpanAlternatives(prepared, learning, units, pending, precedingEnglish, followingEnglish, manual, englishSentence && precedingEnglish == true, kanaInput, punctuateAfterEnglish, 0, count, reasons);
        // すべてローマ字として読んだ案。入力途中の子音は含めない (日本語の区間と同じく、変換するときに足す)。
        // ローマ字として読めない英字が残る読み (google → ごおgぇ) は、日本語の案にならないので出さない
        if (alternatives.Count < count && !kanaInput && string.Concat(units.Select(u => u.Kana)) is var kana &&
            !(kana + Words.Romaji.ConvertLenient(pending.ToLowerInvariant(), final: true)).Any(char.IsAsciiLetter) &&
            new CompositionSegment(false, kana, token) is var japanese && !alternatives.Any(a => a.SequenceEqual([japanese])))
            alternatives.Add([japanese]);
        return alternatives;
    }

    // Structured Latin tokens are opaque; their components are not Japanese readings.
    private static bool IsOpaqueToken(string token, bool kanaInput) =>
        !kanaInput && token.All(c => c is >= '!' and <= '~') &&
        (token.Contains('@') && token.Any(char.IsAsciiLetter) || token.Contains('_') || token.Contains("://", StringComparison.Ordinal) || IsStylizedName(token) || IsHexColor(token));

    /// <summary>
    /// 16 進の色 (#30b0e8・#fff0)。英字の後ろに数字が続くものだけ: 英字だけ (#cafe) はハッシュタグの日本語 (#かふぇ)、
    /// 数字の後ろに英字だけが続くもの (#2392de) は Issue の番号 + 日本語 (#2392で) と区別できない。
    /// </summary>
    private static bool IsHexColor(string token) => token.Length is 4 or 5 or 7 or 9 && token[0] == '#' && token.Skip(1).All(char.IsAsciiHexDigit) &&
        token.Skip(1).SkipWhile(char.IsAsciiDigit).Any(char.IsAsciiDigit);

    /// <summary>
    /// 英語の候補を軽くして区切り直した結果 (変換の候補に、日本語にした語の英語を含む区切り midika → midiか を出すのに使う)。
    /// </summary>
    internal IReadOnlyList<CompositionSegment> SegmentLeaningEnglish(LanguageLearning learning, IReadOnlyList<CompositionUnit> units, bool punctuateAfterEnglish)
    {
        var prepared = Prepared.Of(units);
        return Lattice.FindSpanAlternatives(prepared, learning, units, "", null, null, false, false, false, punctuateAfterEnglish, ReadingLattice.LeaningEnglishBias, 1)[0];
    }

    /// <summary>
    /// 記号を混ぜて書く名前・略語か (#303)。英字 1 文字ずつを . で区切った大文字を含む略語 (B.o.B、U.S.A.) と、
    /// 大文字で始まり、英字のすぐ後ろに $ が入った名前 (Chri$tian、Ke$ha、A$AP)。日本語の打ちかけではない。
    /// </summary>
    private static bool IsStylizedName(string token) =>
        token.Any(char.IsAsciiLetterUpper) && DottedLetters.IsMatch(token) ||
        token.Length > 0 && char.IsAsciiLetterUpper(token[0]) && DollarName.IsMatch(token);

    private static readonly System.Text.RegularExpressions.Regex DottedLetters = new(@"^(?:[A-Za-z]\.)+[A-Za-z]\.?$");
    private static readonly System.Text.RegularExpressions.Regex DollarName = new(@"^[A-Za-z]+(?:\$[A-Za-z]*)+$");
}
