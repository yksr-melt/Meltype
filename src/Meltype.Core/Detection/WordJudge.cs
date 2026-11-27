// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Libraries.Chars;
using Meltype.Libraries.Lexicon;

namespace Meltype.Detection;

/// <summary>語の判定 (英単語・固有名詞・日本人の名前・日本語の語・する の活用・- の入った英単語)。</summary>
internal sealed class WordJudge
{
    private readonly EnglishDetector _english;
    private readonly KanaDetector? _kana;

    public WordJudge(RomajiDetector romaji, EnglishDetector english, ProperNouns? proper, KanaDetector? kana, IEnumerable<string>? userJapaneseWords)
    {
        var userJapanese = new HashSet<string>(StringComparer.Ordinal);
        var userJapanesePrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in userJapaneseWords ?? [])
        {
            if (romaji.AnalyzeWord(word) is not { IsValid: true, Partial: "" } reading) continue;
            userJapanese.Add(reading.Kana);
            for (var i = 1; i <= reading.Kana.Length; i++) userJapanesePrefixes.Add(reading.Kana[..i]);
        }
        Romaji = romaji;
        _english = english;
        ProperNouns = proper ?? new ProperNouns();
        _kana = kana;
        UserJapanese = userJapanese;
        UserJapanesePrefixes = userJapanesePrefixes;
    }

    public RomajiDetector Romaji { get; }

    public ProperNouns ProperNouns { get; }

    /// <summary>ユーザーの辞書フォルダーの japanese.txt の語の読み。ラティスの日本語のノードにする (同梱の japanese.txt の語はノードにしない)。</summary>
    internal IReadOnlySet<string> UserJapanese { get; }

    /// <summary><see cref="UserJapanese"/> の読みの頭 (さくらざか の さ・さく…)。ラティスが日本語の語を探し続けるかの判定に使う。</summary>
    internal IReadOnlySet<string> UserJapanesePrefixes { get; }

    private static readonly Lazy<WordList> ReadableEnglish = new(() => new WordList(DictionarySource.Load("english-readable.txt", null)));

    /// <summary>english.txt の見出し (# --- 技術用語 ---) から、次の空行までの語。</summary>
    private static HashSet<string> EnglishSection(string marker)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var inSection = false;
        foreach (var line in DictionarySource.ReadEmbedded("english.txt").Split('\n'))
        {
            if (line.StartsWith("# ---", StringComparison.Ordinal))
            {
                inSection = line.Contains(marker);
                continue;
            }
            if (!inSection || line.StartsWith('#')) continue;
            // 見出しの中に、同じ段落で続けて別の種類の語を並べてあるので、空行までを読む
            if (line.Trim().Length == 0)
            {
                inSection = false;
                continue;
            }
            foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) words.Add(word);
        }
        return words;
    }

    /// <summary>english.txt の「日本語由来で英語としても使われる語」(sushi, anime, tofu …)。</summary>
    private static readonly Lazy<HashSet<string>> JapaneseOriginWords = new(() => EnglishSection("日本語由来"));

    /// <summary>english.txt の「技術用語」(github, push, share …)。日本語の文の中でも英字のまま書くことの多い語。</summary>
    private static readonly Lazy<HashSet<string>> TechnicalEnglishWords = new(() => EnglishSection("技術用語"));

    /// <summary>日本語由来で英語としても使われる語か (sushi, ramen)。英文の中に出てきても、日本語のつもりのことがある。</summary>
    public static bool IsJapaneseOriginWord(string lower) => JapaneseOriginWords.Value.Contains(lower);

    /// <summary>日本語の文の中でも英字のまま書くことの多い技術用語か (github, push, share)。</summary>
    public static bool IsTechnicalEnglishWord(string lower) => TechnicalEnglishWords.Value.Contains(lower);

    /// <summary>
    /// 英語とも日本語とも読める語か (i, sushi, make, repo): 英単語で、ローマ字としても最後まで読める (母音か ん で終わる)。
    /// 確定した後で前後の文脈と食い違ったら、確定し直す対象になる。
    /// </summary>
    public bool IsAmbiguousWord(string raw)
    {
        if (raw.Length == 0 || !raw.All(char.IsAsciiLetter) || raw.Any(char.IsAsciiLetterUpper)) return false;
        var lower = raw.ToLowerInvariant();
        if (!_english.Words.ContainsWord(lower) || ProperNouns.Contains(lower)) return false;
        var analysis = Romaji.Analyze(lower);
        return analysis.IsValid && analysis.Partial is "" or "n";
    }

    /// <summary>
    /// 確実に英語の語か (want, google, Tokyo): ローマ字として読めない・子音で終わる英単語・固有名詞・大文字で始まる。
    /// 前後の文脈にかかわらず英語なので、直前に確定した語を確定し直す根拠にできる。
    /// </summary>
    public bool IsDefinitelyEnglish(string raw)
    {
        if (raw.Length == 0 || !raw.All(char.IsAsciiLetter)) return false;
        if (char.IsAsciiLetterUpper(raw[0])) return true;
        var lower = raw.ToLowerInvariant();
        if (ProperNouns.Contains(lower)) return true;
        var analysis = Romaji.Analyze(lower);
        if (!analysis.IsValid) return _english.Words.ContainsWord(lower) || _english.IsPrefix(lower) || lower.Length >= 4;
        // 確定するときに呼ぶので、語は打ち終わっている (it が itai の打ちかけかは気にしない)。
        var word = _english.Words.ContainsWord(lower) || IsCommonEnglishWord(lower);
        return word && analysis.Partial.Length > 0 && analysis.Partial != "n";
    }

    /// <summary>
    /// Space を押した時点で、かなにならない子音が残る英単語か (my, by, meeting)。日本語として変換しても子音が残るだけなので、
    /// 英語として確定して空白を入れる。
    /// </summary>
    public bool IsEnglishAtWordEnd(string raw, bool manual)
    {
        if (manual || raw.Length < 2 || !raw.All(char.IsAsciiLetter)) return false;
        var lower = raw.ToLowerInvariant();
        var analysis = Romaji.Analyze(lower);
        if (!analysis.IsValid || analysis.Partial.Length == 0 || analysis.Partial is "n" or "nn") return false;
        return _english.Words.ContainsWord(lower) || IsCommonEnglishWord(lower);
    }

    /// <summary>同梱のよく使う英単語の一覧 (english-words.txt) にある語か。</summary>
    internal static bool IsCommonEnglishWord(string lower) => BuiltInWordChecker.Shared.IsWord(lower);

    /// <summary>同梱の英単語の辞書・固有名詞にある語か、ユーザーが英字に直して覚えた語か (ok、github)。よく使う英単語の一覧は使わない。</summary>
    public bool IsListedEnglishWord(LanguageLearning learning, string lower) =>
        lower.Length >= 2 && (learning.Get(lower) ?? (_english.Words.ContainsWord(lower) || ProperNouns.Contains(lower)));

    /// <summary>
    /// 知っている英単語か (同梱の辞書・固有名詞・ユーザーが英字に直して覚えた語・4 文字以上ならよく使う英単語の一覧)。
    /// python + no の n のように、英単語の最後の n と次の音がくっつくのを防ぐのに使う。
    /// </summary>
    public bool IsKnownEnglishWord(LanguageLearning learning, string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.Length < 3 || !lower.All(char.IsAsciiLetterLower)) return false;
        if (learning.Get(lower) is { } learned) return learned;
        if (_english.Words.ContainsWord(lower) || ProperNouns.Contains(lower) || (lower.Length >= 4 && IsCommonEnglishWord(lower))) return true;
        // english-readable.txt / -pedia。打ち間違い直しが英字の語を崩さないようにする。
        if (lower.Length >= 4 && ReadableEnglish.Value.ContainsWord(lower)) return true;
        return IsPediaEnglishWord(lower);
    }

    /// <summary>
    /// -pedia / -pedias 複合 (pedia / protopedia / wikipedias …)。
    /// ローマ字だと ぺぢあ になり、途中の to が助詞「と」に割れる。複数の s が付いても同じ。
    /// 接頭が無い pedia(s) か、3 文字以上の接頭 + pedia(s)。
    /// </summary>
    private static bool IsPediaEnglishWord(ReadOnlySpan<char> lower)
    {
        var stem = lower.EndsWith("pedias", StringComparison.Ordinal) ? 6
            : lower.EndsWith("pedia", StringComparison.Ordinal) ? 5 : 0;
        if (stem == 0 || lower.Length < stem) return false;
        return lower.Length == stem || lower.Length - stem >= 3;
    }

    /// <summary>する の活用 (した・して・しない・しよう・したい …) で始まる読み。</summary>
    internal static readonly string[] SuruForms = ["する", "すれ", "した", "して", "しま", "しな", "しよ", "しと", "しちゃ", "しろ", "され", "させ", "せず"];

    /// <summary>する の活用 (して・した・します …) だけでできたかなか。</summary>
    internal static bool IsSuruForm(string kana) => SuruForms.Any(kana.StartsWith) && kana.All(KanaChars.IsHiraganaOrLongMark);

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)。接頭辞 + - + 3 文字以上の英単語なら英語。
    // 1 文字の母音 (o-bun = オーブン) は日本語の長音とまぎらわしいので e と x だけ。
    private static readonly HashSet<string> HyphenPrefixes = ["e", "x", "re", "co", "ex", "non", "anti", "semi", "multi", "pre", "sub", "post", "mid", "self", "well"];

    // 接頭辞の規則では拾えない、- の入ったよく使う英単語 (後ろが 2 文字以下など)
    private static readonly HashSet<string> HyphenatedWords = ["bio-break", "co-op", "re-do", "x-ray", "t-shirt", "wi-fi", "hi-fi", "lo-fi", "sci-fi", "e-book", "e-sports", "k-pop", "j-pop", "j-rock", "j-core", "p-hub", "talk-admin", "r-18", "sub-6", "gpt-6.7", "a-z", "u-turn", "check-in", "log-in", "sign-in", "add-on", "plug-in", "built-in", "follow-up", "set-up", "pop-up", "drop-down", "blu-ray", "binge-watch", "ad-blocker"];
    // 日本語のローマ字の途中を英語の接頭辞と誤認しないよう、日本語に続けて拾うのは明示した英数字表記だけ。
    internal static readonly HashSet<string> NumericHyphenatedWords = ["r-18", "sub-6", "gpt-6.7"];

    /// <summary>- を付けて使う英語の接頭辞か (e、re、co …)。英数状態で、- の後を見てから英語か決めるのに使う。</summary>
    public static bool IsHyphenPrefix(string lower) => HyphenPrefixes.Contains(lower);

    /// <summary>- の入った英単語か (e-mail、co-op、re-do、x-ray)。</summary>
    public bool IsHyphenatedEnglishWord(LanguageLearning learning, string lower)
    {
        if (HyphenatedWords.Contains(lower)) return true;
        var dash = lower.IndexOf('-');
        if (dash <= 0 || lower.IndexOf('-', dash + 1) >= 0) return false;
        var rest = lower[(dash + 1)..];
        // - の後ろの語は、区間分けと同じ基準で英単語か見る (re-torimitto を re-tori|みっと にしない。issue #705)
        if (!HyphenPrefixes.Contains(lower[..dash]) || rest.Length < 3 || !rest.All(char.IsAsciiLetterLower) || !IsLatticeEnglishWord(learning, rest)) return false;
        // - の後ろがローマ字として最後まで読める語は、- が長音の日本語 (re-toga = れーとが) と区別できないので、
        // よく使う英単語の一覧 (toga) にあるだけでは英語にしない。同梱の英単語の辞書・固有名詞・覚えた語 (re-make) なら英語
        return Romaji.AnalyzeFragment(rest) is not { IsValid: true, Partial: "" } || IsListedEnglishWord(learning, rest);
    }

    /// <summary>大文字 1 文字の後ろが小文字だけで、ローマ字で読むと日本人の姓・名の読み (dictionaries/names.txt) になるか (Takahashi・Sato)。</summary>
    internal bool IsJapaneseName(string span) =>
        span.Skip(1).All(char.IsAsciiLetterLower) && Romaji.ConvertLenient(span.ToLowerInvariant(), final: true) is var reading &&
        reading.All(KanaChars.IsHiragana) && CommonReadings.Names.Contains(CommonReadings.WithoutLongVowels(reading));

    /// <summary>英単語の書き方の大文字・小文字か (its・Its・ITS と、固有名詞の書き方 GitHub)。iTs は its の書き方ではない。</summary>
    internal bool IsWordCasing(string span) =>
        !span.Skip(1).Any(char.IsAsciiLetterUpper) || span.All(char.IsAsciiLetterUpper) || ProperNouns.Canonical(span.ToLowerInvariant()) == span;

    /// <summary>
    /// 小文字で打った、japanese.txt の語と同じ綴りの固有名詞か (wakatte は わかって、Wakatte.TV は固有名詞)。
    /// 英単語の辞書には固有名詞の小文字も入っているので、固有名詞で見分ける。
    /// </summary>
    internal bool IsLowercaseJapaneseProperNoun(LanguageLearning learning, string span) =>
        span.All(char.IsAsciiLetterLower) && learning.Get(span) is null && ProperNouns.Contains(span) &&
        _kana?.IsJapaneseWord(Romaji.ConvertLenient(span, final: true)) == true;

    /// <summary>
    /// 知っている英単語か (同梱の辞書・固有名詞・よく使う英単語の一覧・english-readable.txt・-pedia・ユーザーが英字に直して覚えた語)。
    /// 1 文字は a・i と、チャットの略し方の u (you)・r (are) だけ。
    /// </summary>
    /// <remarks>
    /// english.txt の語の -s の形 (deploys) も語にする。english-words.txt は deploy を持たず ploys を持つので、deploys を de|ploys に割ってしまう。
    /// </remarks>
    internal bool IsLatticeEnglishWord(LanguageLearning learning, string lower)
    {
        if (learning.Get(lower) is { } learned) return learned;
        if (lower.Length == 1) return lower is "a" or "i" or "u" or "r";
        return _english.Words.ContainsWord(lower) || ProperNouns.Contains(lower) || IsCommonEnglishWord(lower) || ReadableEnglish.Value.ContainsWord(lower) ||
            lower.Length >= 4 && lower[^1] == 's' && _english.Words.ContainsWord(lower[..^1]) ||
            IsPediaEnglishWord(lower);
    }

    /// <summary>同梱の英単語の辞書 (english.txt と固有名詞の小文字) の語か。</summary>
    internal bool IsDictionaryWord(string lower) => _english.Words.ContainsWord(lower);

    /// <summary>同梱の英単語の辞書の語の頭か (打ちかけの英単語)。</summary>
    internal bool IsEnglishPrefix(string span) => _english.IsPrefix(span);

    /// <summary>english-readable.txt (ローマ字として読めても英単語として読むことが多い語) の語か。</summary>
    internal static bool IsReadableEnglish(string lower) => ReadableEnglish.Value.ContainsWord(lower);
}
