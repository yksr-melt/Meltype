// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>
/// 変換ボックスの中身のうち、どこを英字のまま見せるかを決める。
///
/// 日本語入力中に google と打って「ごおｇぇ」になるのを防ぐのが目的。英語かどうかは区間ごとに判定するので、
/// 「きょうは google」なら google の部分だけが英字になる。区切り方は ReadingLattice.cs。
/// </summary>
public sealed partial class CompositionDetector
{
    private readonly RomajiDetector _romaji;
    private readonly EnglishDetector _english;
    private readonly ProperNouns _proper;
    private readonly KanaDetector? _kana;

    /// <summary>ユーザーの辞書フォルダーの japanese.txt の語の読み。ラティスの日本語のノードにする (同梱の japanese.txt の語はノードにしない)。</summary>
    private readonly HashSet<string> _userJapanese = new(StringComparer.Ordinal);

    /// <summary>_userJapanese の読みの頭 (さくらざか の さ・さく…)。ラティスが日本語の語を探し続けるかの判定に使う。</summary>
    private readonly HashSet<string> _userJapanesePrefixes = new(StringComparer.Ordinal);

    /// <summary>ローマ字としても読める、よく使う拡張子 (Meltype.ace、setup.ini)。読めない拡張子 (dll、json) は一覧に無くても英字のまま。</summary>
    private static readonly HashSet<string> FileExtensions =
    [
        "a", "ace", "ada", "ado", "ann", "ape", "ase", "dae", "di", "do", "fifo", "fon", "geo", "go", "ifo", "ii", "ini", "ino", "ipa", "iso", "jade", "ko",
        "ma", "metadata", "midi", "mime", "mo", "mobi", "o", "oga", "ora", "oso", "otio", "po", "pyo", "ra", "re", "rego", "rei", "resi", "ru", "sami",
        "so", "suo", "tese", "ufo", "vue",
    ];

    private static readonly HashSet<string> DomainSuffixes = ["ai", "app", "au", "biz", "ca", "cn", "co", "com", "de", "dev", "edu", "eu", "fr", "gg", "gov", "info", "in", "io", "jp", "kr", "me", "net", "org", "uk", "us", "xyz"];

    public CompositionDetector(RomajiDetector romaji, EnglishDetector english, ProperNouns? proper = null, KanaDetector? kana = null,
        IEnumerable<string>? userJapaneseWords = null)
    {
        _kana = kana;
        foreach (var word in userJapaneseWords ?? [])
        {
            if (romaji.AnalyzeWord(word) is not { IsValid: true, Partial: "" } reading) continue;
            _userJapanese.Add(reading.Kana);
            for (var i = 1; i <= reading.Kana.Length; i++) _userJapanesePrefixes.Add(reading.Kana[..i]);
        }
        _romaji = romaji;
        _english = english;
        _proper = proper ?? new ProperNouns();
    }

    public static CompositionDetector CreateDefault(string? userDictionaryDirectory = null)
    {
        var romaji = RomajiDetector.CreateDefault(userDictionaryDirectory);
        var japaneseWords = DictionarySource.Load("japanese.txt", userDictionaryDirectory).ToList();
        var proper = ProperNouns.Load(userDictionaryDirectory);
        var english = new EnglishDetector(DictionarySource.Load("english.txt", userDictionaryDirectory).Concat(proper.LowercaseWords));
        return new CompositionDetector(romaji, english, proper, new KanaDetector(japaneseWords, romaji),
            DictionarySource.LoadUser("japanese.txt", userDictionaryDirectory));
    }

    private static readonly Lazy<WordList> ReadableEnglish = new(() => new WordList(DictionarySource.Load("english-readable.txt", null)));

    /// <summary>english.txt の「日本語由来で英語としても使われる語」(sushi, anime, tofu …)。</summary>
    private static readonly Lazy<HashSet<string>> JapaneseOriginWords = new(() =>
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var inSection = false;
        foreach (var line in DictionarySource.ReadEmbedded("english.txt").Split('\n'))
        {
            if (line.StartsWith("# ---", StringComparison.Ordinal))
            {
                inSection = line.Contains("日本語由来");
                continue;
            }
            if (!inSection || line.StartsWith('#')) continue;
            // 日本語由来の語の後ろに、同じ段落で続けて技術用語などを並べてあるので、空行までを読む
            if (line.Trim().Length == 0)
            {
                inSection = false;
                continue;
            }
            foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) words.Add(word);
        }
        return words;
    });

    /// <summary>日本語由来で英語としても使われる語か (sushi, ramen)。英文の中に出てきても、日本語のつもりのことがある。</summary>
    public static bool IsJapaneseOriginWord(string lower) => JapaneseOriginWords.Value.Contains(lower);

    public RomajiDetector Romaji => _romaji;

    /// <summary>普通の英単語の判定に使う Windows のスペルチェッカー。null なら同梱の辞書だけ。</summary>
    public IWordChecker? SpellChecker { get; set; }

    /// <summary>ユーザーが英字 / かなに直して覚えた語 (自動の判定より優先する)。</summary>
    public LanguageMemory? Memory { get; set; }

    /// <summary>英字の並びが、ローマ字としてよく使う日本語になるか (kyouha = 今日は、tomato = とまと)。無ければ使わない。</summary>
    public Func<string, bool>? IsCommonJapanese { get; set; }

    public ProperNouns ProperNouns => _proper;

    /// <summary>
    /// 英字のすぐ後ろの , . も句読点にするか (設定「英字の後ろの , . も句読点にする」、issue #416)。
    /// null か false なら今までどおり、変換ボックスの中が英字だけなら半角のまま。
    /// </summary>
    public Func<bool>? UseJapanesePunctuationAfterEnglish { get; set; }

    /// <summary>
    /// 英字の後ろの記号 units[i] を、英文の記号 (半角のまま) として扱うか。設定が ON でも、キャレットの前が英語なら英文の続き。
    /// すぐ後ろに英数字が続く . は、ドメイン・拡張子の区切り (tetr.io、MeltypeTip.dll)。
    /// </summary>
    private bool KeepsPunctuationAfterEnglish(IReadOnlyList<CompositionUnit> units, int i, string pending, bool? precedingEnglish)
    {
        if (units[i].Raw is not ("," or ".") || precedingEnglish == true || UseJapanesePunctuationAfterEnglish?.Invoke() != true) return true;
        var next = i + 1 < units.Count ? units[i + 1].Raw : pending;
        return units[i].Raw == "." && next.Length > 0 && char.IsAsciiLetterOrDigit(next[0]);
    }

    /// <summary>
    /// 単位列 (+ 入力途中の子音) を英語区間と日本語区間に分ける。
    /// </summary>
    /// <param name="precedingEnglish">入力欄のキャレットの前の確定済みの文字が英語なら true、日本語なら false、分からなければ null。</param>
    /// <param name="followingEnglish">キャレットの後ろの文字が英語なら true、日本語なら false、分からなければ null。</param>
    /// <param name="level">判定の強さ。手動 (Manual) では Shift で打った大文字始まりの語だけを英語にする。</param>
    /// <param name="englishSentence">キャレットの前が英文 (空白で区切った英単語が 2 語以上続いて空白で終わる: "I want ")。
    /// 日本語の文の中の英単語 (GitHub の) より強い英語の根拠として扱う。</param>
    public IReadOnlyList<CompositionSegment> Segment(IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish = null, bool? followingEnglish = null,
        DetectionLevel level = DetectionLevel.Balanced, bool englishSentence = false, bool kanaInput = false)
    {
        // 区間分けは units[i..j] の生文字列・かなを何度も作る。1 回の Segment 呼び出しの間だけ、
        // 連結した原文と各位置のオフセットを使い回す (内容・順序は同じで、文字列を毎回連結しない)。
        Prepare(units);
        var token = _preparedRaw + pending;
        // Structured Latin tokens are opaque; their components are not Japanese readings.
        if (!kanaInput && token.All(c => c is >= '!' and <= '~') &&
            (token.Contains('@') && token.Any(char.IsAsciiLetter) || token.Contains('_') || token.Contains("://", StringComparison.Ordinal) || IsStylizedName(token)))
            return [new CompositionSegment(true, "", token)];
        return FindSpansByReading(units, pending, precedingEnglish, followingEnglish, level, englishSentence && precedingEnglish == true, kanaInput);
    }

    /// <summary>
    /// 英語の候補を軽くして区切り直した結果 (変換の候補に、日本語にした語の英語を含む区切り midika → midiか を出すのに使う)。
    /// </summary>
    public IReadOnlyList<CompositionSegment> SegmentLeaningEnglish(IReadOnlyList<CompositionUnit> units)
    {
        Prepare(units);
        return FindSpansByReading(units, "", null, null, DetectionLevel.Balanced, false, false, LeaningEnglishBias);
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

    /// <summary>
    /// 英語とも日本語とも読める語か (i, sushi, make, repo): 英単語で、ローマ字としても最後まで読める (母音か ん で終わる)。
    /// 確定した後で前後の文脈と食い違ったら、確定し直す対象になる。
    /// </summary>
    public bool IsAmbiguousWord(string raw)
    {
        if (raw.Length == 0 || !raw.All(char.IsAsciiLetter) || raw.Any(char.IsAsciiLetterUpper)) return false;
        var lower = raw.ToLowerInvariant();
        if (!_english.Words.ContainsWord(lower) || _proper.Contains(lower)) return false;
        var analysis = _romaji.Analyze(lower);
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
        if (_proper.Contains(lower)) return true;
        var analysis = _romaji.Analyze(lower);
        if (!analysis.IsValid) return _english.Words.ContainsWord(lower) || _english.IsPrefix(lower) || lower.Length >= 4;
        // 確定するときに呼ぶので、語は打ち終わっている (it が itai の打ちかけかは気にしない)。
        var word = _english.Words.ContainsWord(lower) || IsSpellWord(lower);
        return word && analysis.Partial.Length > 0 && analysis.Partial != "n";
    }

    /// <summary>
    /// Space を押した時点で、かなにならない子音が残る英単語か (my, by, meeting)。日本語として変換しても子音が残るだけなので、
    /// 英語として確定して空白を入れる。
    /// </summary>
    public bool IsEnglishAtWordEnd(string raw, DetectionLevel level)
    {
        if (level == DetectionLevel.Manual || raw.Length < 2 || !raw.All(char.IsAsciiLetter)) return false;
        var lower = raw.ToLowerInvariant();
        var analysis = _romaji.Analyze(lower);
        if (!analysis.IsValid || analysis.Partial.Length == 0 || analysis.Partial is "n" or "nn") return false;
        return _english.Words.ContainsWord(lower) || IsSpellWord(lower);
    }

    /// <summary>スペルチェッカーが正しいと言う英単語か、よくある打ち間違い (teh、recieve) か。</summary>
    internal bool IsSpellWord(string lower) =>
        // NSSpellChecker は73文字以上の未知語を検査せず「誤りなし」と返す。
        // 長いローマ字文を英単語の根拠にしない。同梱辞書の語は別途判定する。
        lower.Length <= 72 && SpellChecker is { } checker && (checker.IsWord(lower) || checker.AutoCorrection(lower) is not null);

    /// <summary>よくある英語の打ち間違いなら正しい綴り (teh → the)。大文字で始まる語は大文字で始める。</summary>
    public string? EnglishAutoCorrection(string word)
    {
        if (word.Length < 2 || !word.All(char.IsAsciiLetter)) return null;
        // -pedia / -pedias 複合はスペルチェッカーが別綴りに「訂正」してしまうことがあるので触らない。
        if (IsPediaEnglishWord(word.ToLowerInvariant())) return null;
        if (SpellChecker?.AutoCorrection(word.ToLowerInvariant()) is not { } right) return null;
        if (word.All(char.IsAsciiLetterUpper) && word.Length > 1) return right.ToUpperInvariant();
        return char.IsAsciiLetterUpper(word[0]) ? char.ToUpperInvariant(right[0]) + right[1..] : right;
    }

    /// <summary>同梱の英単語の辞書・固有名詞にある語か、ユーザーが英字に直して覚えた語か (ok、github)。スペルチェッカーは使わない。</summary>
    public bool IsListedEnglishWord(string lower) =>
        lower.Length >= 2 && (Memory?.Get(lower) ?? (_english.Words.ContainsWord(lower) || _proper.Contains(lower)));

    /// <summary>
    /// 知っている英単語か (同梱の辞書・固有名詞・ユーザーが英字に直して覚えた語・4 文字以上ならスペルチェッカー)。
    /// python + no の n のように、英単語の最後の n と次の音がくっつくのを防ぐのに使う。
    /// </summary>
    public bool IsKnownEnglishWord(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.Length < 3 || !lower.All(char.IsAsciiLetterLower)) return false;
        if (Memory?.Get(lower) is { } learned) return learned;
        if (_english.Words.ContainsWord(lower) || _proper.Contains(lower) || (lower.Length >= 4 && IsSpellWord(lower))) return true;
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

    /// <summary>英文の中では半角のままにする記号。[ ] は日本語の入力では「」なので含めない (英単語の後ろでも「」: bot「Thinking」)。</summary>
    private static bool IsAsciiSymbol(CompositionUnit unit) =>
        unit.Raw.Length == 1 && unit.Raw[0] is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(unit.Raw[0]) && unit.Raw[0] is not ('[' or ']');

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)。接頭辞 + - + 3 文字以上の英単語なら英語。
    // 1 文字の母音 (o-bun = オーブン) は日本語の長音とまぎらわしいので e と x だけ。
    private static readonly HashSet<string> HyphenPrefixes = ["e", "x", "re", "co", "ex", "non", "anti", "semi", "multi", "pre", "sub", "post", "mid", "self", "well"];

    // 接頭辞の規則では拾えない、- の入ったよく使う英単語 (後ろが 2 文字以下など)
    private static readonly HashSet<string> HyphenatedWords = ["bio-break", "co-op", "re-do", "x-ray", "t-shirt", "wi-fi", "hi-fi", "lo-fi", "sci-fi", "e-book", "e-sports", "k-pop", "j-pop", "j-rock", "j-core", "p-hub", "talk-admin", "r-18", "sub-6", "gpt-6.7", "a-z", "u-turn", "check-in", "log-in", "sign-in", "add-on", "plug-in", "built-in", "follow-up", "set-up", "pop-up", "drop-down", "blu-ray", "binge-watch", "ad-blocker"];
    // 日本語のローマ字の途中を英語の接頭辞と誤認しないよう、日本語に続けて拾うのは明示した英数字表記だけ。
    private static readonly HashSet<string> NumericHyphenatedWords = ["r-18", "sub-6", "gpt-6.7"];

    /// <summary>- を付けて使う英語の接頭辞か (e、re、co …)。英数状態で、- の後を見てから英語か決めるのに使う。</summary>
    public static bool IsHyphenPrefix(string lower) => HyphenPrefixes.Contains(lower);

    /// <summary>- の入った英単語か (e-mail、co-op、re-do、x-ray)。</summary>
    public bool IsHyphenatedEnglishWord(string lower)
    {
        if (HyphenatedWords.Contains(lower)) return true;
        var dash = lower.IndexOf('-');
        if (dash <= 0 || lower.IndexOf('-', dash + 1) >= 0) return false;
        var rest = lower[(dash + 1)..];
        return HyphenPrefixes.Contains(lower[..dash]) && rest.Length >= 3 && rest.All(char.IsAsciiLetterLower) && IsKnownEnglishWord(rest);
    }

    /// <summary>
    /// start から始まるユーザー名の終わり。無ければ -1。
    /// @ の後ろ (Discord・X のメンション @kuraido) と、_ の入った語 (upah_setu、cafely_latte) は、ローマ字として読めても英字のまま。
    /// 英字・数字・_ が続く所までがユーザー名 (@ の後ろは、メールアドレスのドメインの . - も含める)。
    /// </summary>
    private int UserNameEnd(IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        static bool IsNameUnit(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        var n = units.Count;
        if (!IsNameUnit(units[start]) || start > 0 && IsNameUnit(units[start - 1])) return -1;
        var mention = start > 0 && units[start - 1].Raw == "@";
        var end = start;
        while (end < n && IsNameUnit(units[end])) end++;
        // メールアドレスのドメイン (taro@gmail.com) の . - も続けて英字に。
        while (mention && end + 1 < n && units[end].Raw is "." or "-" && IsNameUnit(units[end + 1]))
        {
            end++;
            while (end < n && IsNameUnit(units[end])) end++;
        }
        var name = Raw(units, start, end) + (end == n ? pending : "");
        if (!name.Any(char.IsAsciiLetter)) return -1;
        if (mention || name.Contains('_')) return end;
        // メールアドレスの @ より前 (tanaka@、yamada.taro@): @ を打ったら、その前も英字のまま。
        // @ の後ろのドメインは、上の @ の後ろの決まりで英字になる (たなか@gmail.com になっていた: issue #59)。
        if (start == 0 || units[start - 1].Raw is " " or "<" or "(" or "\"" or "'" or ":" or ",")
        {
            var local = end;
            while (local + 1 < n && units[local].Raw is "." or "-" or "+" && IsNameUnit(units[local + 1]))
            {
                local++;
                while (local < n && IsNameUnit(units[local])) local++;
            }
            // @ の後ろに英字が続いたとき (ドメインを打ち始めた) だけ。あと@3人 (ato@3nin) のような @ は日本語のまま
            var domainStarts = local + 1 < n ? units[local + 1].Raw is [var first, ..] && char.IsAsciiLetter(first) : local + 1 == n && pending is [var p, ..] && char.IsAsciiLetter(p);
            if (local < n && units[local].Raw == "@" && domainStarts) return local + 1;
        }
        return -1;
    }

    /// <summary>
    /// ローマ字として読める英字のすぐ後ろに、読めない拡張子が続くとき (llama.cpp → っぁま。cpp)。
    /// 語の先頭から拡張子までを 1 つの英語区間の候補にする (句点の後ろの日本語 ryoukaidesu.|deha とは重みで比べる)。大文字や知っている英単語 (Meltype.amr) は、拡張子を別区間のままにする。
    /// 数字の前 (3.14desu)・小数 (soudesu.1.5)・最後まで読める語 (Meltype.desu) は対象外。
    /// </summary>
    private int ReadableStemDotSuffixEnd(IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        if (start > 0 && units[start - 1].Raw.Length > 0 && units[start - 1].Raw.All(char.IsAsciiLetter)) return -1;
        var dot = start;
        if (dot >= units.Count || units[dot].Raw.Length == 0 || !units[dot].Raw.All(char.IsAsciiLetter)) return -1;
        if (char.IsAsciiLetterUpper(units[dot].Raw[0])) return -1;
        while (dot < units.Count && units[dot].Raw.Length > 0 && units[dot].Raw.All(char.IsAsciiLetter)) dot++;
        if (dot >= units.Count || units[dot].Raw != ".") return -1;
        var stem = Raw(units, start, dot);
        // 小文字の、日本語の語と同じ綴りの固有名詞 (wakatte) は、区間分けでかなになるので、ここでドメインとしてまとめる (wakatte.tv)
        if (IsKnownEnglishWord(stem) && !IsLowercaseJapaneseProperNoun(stem)) return -1;
        var suffix = DotSuffixEnd(units, dot + 1, pending);
        if (suffix <= dot + 1) return -1;
        // 1.5 のような小数は拡張子ではない (soudesu.1.5 → そうです。1.5)。cpp のような英字の拡張子だけをまとめる。
        var suffixRaw = Raw(units, dot + 1, Math.Min(suffix, units.Count));
        if (suffixRaw.Length > 0 && char.IsAsciiDigit(suffixRaw[0])) return -1;
        return suffix;
    }

    /// <summary>
    /// 入力先頭のドットファイル (.gitignore / .env / .dll) の終わり。無ければ -1 (issue #431)。
    /// 途中の 3.14desu や「です。」の . には使わない (start == 0 のときだけ)。
    /// </summary>
    private int LeadingDotfileEnd(IReadOnlyList<CompositionUnit> units, int start, string pending, DetectionLevel level)
    {
        if (level == DetectionLevel.Manual || start != 0 || units.Count == 0 || units[0].Raw != ".") return -1;
        var end = DotSuffixEnd(units, 1, pending);
        return end > 1 ? end : -1;
    }

    /// <summary>
    /// 英語の語のすぐ後ろの . に続く、ドメイン・拡張子 (tetr.io、MeltypeTip.dll、abi.json、go.mod) の終わり。無ければ -1 (issue #260)。
    /// ドメインは語全体が一致するときだけ (config の co、index の in を英字にして、残りをローマ字にしていた)。
    /// 拡張子は、ローマ字として読めない (dll、aab)・数字や . / を含む (e57、go.mod)・よく使う拡張子 (ace、ini) なら英字のまま。
    /// 最後までローマ字として読める語 (Meltype.desu) は、今までどおり判定する。後ろの助詞からは日本語 (MeltypeTip.dll|wokesu)。
    /// </summary>
    private int DotSuffixEnd(IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        static bool IsPart(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '+');
        var n = units.Count;
        var end = start;
        while (end < n && IsPart(units[end])) end++;
        // . / - で続く所まで 1 つの名前 (abi.json、go.work.sum、cargo/config.toml、db-journal)
        while (end + 1 < n && units[end].Raw is "." or "/" or "-" && IsPart(units[end + 1]))
        {
            end++;
            while (end < n && IsPart(units[end])) end++;
        }
        var name = Raw(units, start, end) + (end == n ? pending : "");
        if (!name.Any(char.IsAsciiLetter)) return -1;
        var lower = name.ToLowerInvariant();
        static bool Known(string label) => DomainSuffixes.Contains(label) || FileExtensions.Contains(label);
        var lastLabel = lower[(lower.LastIndexOfAny(['.', '/']) + 1)..];
        if (Known(lower) || Known(lastLabel)) return end;
        if (lower.Any(c => char.IsAsciiDigit(c) || c is '.' or '/' or '_' or '+')) return end;
        // 打っている途中も、最後の子音までかなになるときだけ読めるとみなす (Meltype.bas を ばs と表示すると、確定で打ち間違いとして ば に直される)
        bool Readable(string text) => _romaji.Analyze(text) is { IsValid: true, Partial: "" or "n" };
        var readable = Readable(lower);
        // 短い語 (拡張子の長さ) の後ろが助詞で始まる日本語なら、そこまで。
        //   ドメイン・拡張子の後ろ: 助詞の後ろが読める・英単語でも分ける (tetr.io|de、github.com|ni|push)。全体が読める語では、今までどおりドメインだけ
        //   読めない語の後ろ: 助詞から最後まで読めるとき (dll|wokesu、json|nokakikata)。長い名前の途中 (gitig|nore)・助詞だけ (gc|no、t|ga) では分けない
        for (var k = start + 1; k < end; k++)
        {
            var stem = Raw(units, start, k).ToLowerInvariant();
            if (stem.Length > 4) break;
            var rest = (Raw(units, k, end) + (end == n ? pending : "")).ToLowerInvariant();
            if (TrailingParticles.FirstOrDefault(p => rest.StartsWith(p, StringComparison.Ordinal)) is not { } particle) continue;
            var afterParticle = rest[particle.Length..];
            // 英単語が続くのは 3 文字以上のときだけ (au|to|mount にしない)
            if ((readable ? DomainSuffixes.Contains(stem) : Known(stem)) &&
                (afterParticle.Length == 0 || Readable(afterParticle) || stem.Length >= 3 && IsKnownEnglishWord(afterParticle))) return k;
            if (!readable && afterParticle.Length > 0 && Readable(rest) && !Readable(stem)) return k;
        }
        // 読めない語の後ろに、(助詞 +) 3 音以上の日本語の語から始まる読める語が続く (.env|fairu、.zshrc|nosettei)。短い語 (.js|on のおん) では分けない
        for (var k = start + 1; !readable && k < end; k++)
        {
            var stem = Raw(units, start, k).ToLowerInvariant();
            if (Readable(stem)) continue;
            var rest = (Raw(units, k, end) + (end == n ? pending : "")).ToLowerInvariant();
            if (!Readable(rest)) continue;
            var particle = TrailingParticles.FirstOrDefault(p => rest.StartsWith(p, StringComparison.Ordinal)) ?? "";
            var kana = _romaji.ConvertLenient(rest[particle.Length..], final: true);
            for (var length = 3; length <= kana.Length; length++)
                if (RomajiTypoCorrector.CommonWordCost(kana.AsSpan(0, length)) is not null || CommonReadings.Katakana.Contains(kana.AsSpan(0, length))) return k;
        }
        // 最後までローマ字として読める語 (Meltype.desu) は、ふつうに判定する
        return readable ? -1 : end;
    }

    /// <summary>
    /// 数字を英字が挟む短い略語 (i18n、l10n、h2o、a11y、k8s) の終わり。無ければ -1。
    /// 末尾が数字だけの語 (win11、ps5) は区切りの重みに任せる。
    /// 日本語の途中の数字 (かいぎは3じ) を略語にしないよう、前後の英字は短く、全体も短いときだけ。
    /// 末尾の子音が打ちかけ (pending の n: i18|n) のときも、数字の後ろの英字として数える。
    /// </summary>
    private int NumeronymEnd(IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        static bool IsLetter(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(char.IsAsciiLetter);
        static bool IsDigit(CompositionUnit unit) => unit.Raw is [var d] && char.IsAsciiDigit(d);
        if (start >= units.Count || !IsLetter(units[start])) return -1;
        var i = start;
        while (i < units.Count && IsLetter(units[i])) i++;
        var headLen = Raw(units, start, i).Length;
        if (headLen is < 1 or > 2 || i >= units.Count || !IsDigit(units[i])) return -1;
        var digitStart = i;
        while (i < units.Count && IsDigit(units[i])) i++;
        var digitLen = Raw(units, digitStart, i).Length;
        if (digitLen is < 1 or > 2) return -1;
        var digitEnd = i;
        var pendingLetters = i == units.Count && pending.Length > 0 && pending.All(char.IsAsciiLetter) ? pending : "";
        if (i < units.Count && IsLetter(units[i]))
        {
            var tailStart = i;
            while (i < units.Count && IsLetter(units[i])) i++;
            // 数字の後ろにまた数字が続く形 (a1b2) は対象外
            if (i < units.Count && IsDigit(units[i])) return -1;
            var tailLen = Raw(units, tailStart, i).Length;
            pendingLetters = i == units.Count && pending.Length > 0 && pending.All(char.IsAsciiLetter) ? pending : "";
            if (tailLen + pendingLetters.Length is < 1 or > 3) return -1;
        }
        else if (pendingLetters.Length is < 1 or > 3) return -1;
        var word = Raw(units, start, i) + pendingLetters;
        if (word.Length is < 3 or > 6) return -1;
        // 数字の前と後ろが、どちらもかなとして最後まで読める (huuso|ku|6|me|-toru の ku と me) なら、
        // 略語 (i18n の n、k8s の s、h2o の h) ではなく、数字を挟んだ日本語 (風速6メートル: issue #1017)。
        // 母音 1 文字 (e2e・o2o の e・o) は、かなとして読めても略語のことが多いので、子音から始まる 2 文字以上のときだけ。
        var head = Raw(units, start, digitStart).ToLowerInvariant();
        var tail = (Raw(units, digitEnd, i) + pendingLetters).ToLowerInvariant();
        if (head.Length >= 2 && tail.Length >= 2 && _romaji.AnalyzeFragment(head) is { IsValid: true, Partial: "" } &&
            _romaji.AnalyzeFragment(tail) is { IsValid: true, Partial: "" }) return -1;
        return i;
    }

    /// <summary>
    /// start から始まる - の入った英単語 (e-mail、co-op) の終わり。無ければ -1。
    /// 後ろに日本語が続いてもよい (e-mail|de) ので、- の後ろは長い方から英単語になる所を探す。
    /// </summary>
    private int HyphenatedWordEnd(IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        var n = units.Count;
        // 日本語を打っている最中のローマ字列では、明示した英数字表記だけを英語として切り出す。
        var afterJapaneseRomaji = start > 0 && units[start - 1].Raw.Length > 0 && units[start - 1].Raw.All(char.IsAsciiLetter) &&
                                  units[start - 1].Kana != units[start - 1].Raw;
        if (start > 0 && units[start - 1].Raw.Length > 0 && units[start - 1].Raw.All(char.IsAsciiLetter) && !afterJapaneseRomaji) return -1;
        var dash = start;
        while (dash < n && units[dash].Raw.Length > 0 && units[dash].Raw.All(char.IsAsciiLetter)) dash++;
        if (dash == start || dash >= n || units[dash].Raw != "-") return -1;
        for (var end = n; end > dash + 1; end--)
        {
            var word = (Raw(units, start, end) + (end == n ? pending : "")).ToLowerInvariant();
            if (afterJapaneseRomaji ? NumericHyphenatedWords.Contains(word) : IsHyphenatedEnglishWord(word)) return end;
        }
        return -1;
    }

    /// <summary>
    /// 版の番号 (1.4.1・1.10.0) の後ろの、- から始まるプレリリースの名前 (-pre・-beta.2・-rc1) の終わり。無ければ -1。
    /// 名前は PrereleaseNames の語 + 数字 + (. 数字) の並び。後ろの英字 (-pre|wodashita) からは日本語。
    /// </summary>
    private static int VersionSuffixEnd(IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        static bool IsDigits(string raw) => raw.Length > 0 && raw.All(char.IsAsciiDigit);
        var n = units.Count;
        if (units[start].Raw != "-") return -1;
        // - の前が x.y.z の 3 つの数字 (1.5-desu・3.14-kai は日本語のまま)
        var before = start;
        for (var part = 0; part < 3; part++)
        {
            var digits = before;
            while (before > 0 && IsDigits(units[before - 1].Raw)) before--;
            if (before == digits || part < 2 && (before == 0 || units[--before].Raw != ".")) return -1;
        }
        // 一番長く一致する名前 (pre|view でなく preview) の終わり。最後まで打っている途中 (-pr) は、名前の頭なら入力の最後まで
        var end = -1;
        var name = "";
        var k = start + 1;
        for (; k < n && PrereleaseNames.Any(p => p.StartsWith(name + units[k].Raw.ToLowerInvariant(), StringComparison.Ordinal)); k++)
        {
            name += units[k].Raw.ToLowerInvariant();
            if (PrereleaseNames.Contains(name)) end = k + 1;
        }
        if (k == n && name.Length + pending.Length > 0 && PrereleaseNames.Any(p => p.StartsWith(name + pending.ToLowerInvariant(), StringComparison.Ordinal))) return n;
        if (end < 0) return -1;
        while (end < n && IsDigits(units[end].Raw)) end++;
        while (end + 1 < n && units[end].Raw == "." && IsDigits(units[end + 1].Raw))
            for (end++; end < n && IsDigits(units[end].Raw); end++) { }
        return end;
    }

    private static readonly string[] PrereleaseNames =
        ["alpha", "beta", "canary", "dev", "edge", "experimental", "final", "hotfix", "insiders", "lts", "next", "nightly", "pre", "preview", "rc", "release",
         "snapshot", "stable", "test", "twilight", "unstable"];

    /// <summary>英語の短縮形 (don't, it's, I'm, you're, we'll, can't)。' の前が英単語か n't の形。</summary>
    private bool IsContraction(string span)
    {
        var apostrophe = span.IndexOf('\'');
        if (apostrophe <= 0 || apostrophe != span.LastIndexOf('\'')) return false;
        var stem = span[..apostrophe].ToLowerInvariant();
        var suffix = span[(apostrophe + 1)..].ToLowerInvariant();
        // 語の最後の g を ' にした書き方 (swingin' = swinging、rockin'): stem + g が英単語なら英語
        if (suffix.Length == 0 && stem.Length >= 3 && stem.EndsWith("in", StringComparison.Ordinal) && stem.All(char.IsAsciiLetterLower)) return IsKnownEnglishWord(stem + "g");
        if (!stem.All(char.IsAsciiLetterLower) || suffix is not ("t" or "s" or "re" or "ve" or "ll" or "d" or "m")) return false;
        if (suffix == "t") return stem.Length >= 2 && stem[^1] == 'n';
        return stem == "i" || _english.Words.ContainsWord(stem) || _proper.Contains(stem);
    }

    private static readonly string[] TrailingParticles = ["kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha", "wa", "ya"];

    /// <summary>
    /// かなのすぐ後ろに続く w の単位 (きた|w|w): 笑いとして w のまま残したもの。ローマ字として読めなかった英字 (zoom の m) とは違うので、
    /// 英単語の根拠にしない (kiyagat|ta|w の ta + w を英単語 taw にしていた)。
    /// </summary>
    private static bool IsLaughter(IReadOnlyList<CompositionUnit> units, int k)
    {
        var i = k;
        while (i >= 0 && units[i].Raw is "w" or "W" && units[i].Kana == units[i].Raw) i--;
        return i < k && i >= 0 && units[i].Kana is [var kana, ..] && kana is >= 'ぁ' and <= 'ヺ' &&
               Enumerable.Range(k + 1, units.Count - k - 1).TakeWhile(j => units[j].Raw.Length > 0 && char.IsAsciiLetter(units[j].Raw[0])).All(j => units[j].Raw is "w" or "W");
    }

    private IReadOnlyList<CompositionUnit>? _preparedUnits;
    // ローマ字として読めなかった英字の単位 (zoom の m) の数の累積
    private int[] _preparedUnreadable = [];
    private string _preparedRaw = "";
    private int[] _preparedRawOffsets = [];
    private string _preparedKana = "";
    private int[] _preparedKanaOffsets = [];

    /// <summary>この Segment 呼び出しの units で、原文・かなの連結と位置を用意する (Segment の先頭で必ず呼ぶ)。</summary>
    private void Prepare(IReadOnlyList<CompositionUnit> units)
    {
        var unreadable = new int[units.Count + 1];
        for (var i = 0; i < units.Count; i++)
            unreadable[i + 1] = unreadable[i] + (units[i] is { Raw.Length: 1 } unit && unit.Kana == unit.Raw &&
                char.IsAsciiLetter(unit.Raw[0]) && !IsLaughter(units, i) ? 1 : 0);
        _preparedUnreadable = unreadable;
        var raw = new StringBuilder();
        var rawOffsets = new int[units.Count + 1];
        var kana = new StringBuilder();
        var kanaOffsets = new int[units.Count + 1];
        for (var i = 0; i < units.Count; i++)
        {
            rawOffsets[i] = raw.Length;
            raw.Append(units[i].Raw);
            kanaOffsets[i] = kana.Length;
            kana.Append(units[i].Kana);
        }
        rawOffsets[units.Count] = raw.Length;
        kanaOffsets[units.Count] = kana.Length;
        _preparedUnits = units;
        _preparedRaw = raw.ToString();
        _preparedRawOffsets = rawOffsets;
        _preparedKana = kana.ToString();
        _preparedKanaOffsets = kanaOffsets;
    }

    private string Kana(IReadOnlyList<CompositionUnit> units, int start, int end)
    {
        if (ReferenceEquals(units, _preparedUnits) && start >= 0 && end < _preparedKanaOffsets.Length)
            return _preparedKana.Substring(_preparedKanaOffsets[start], _preparedKanaOffsets[end] - _preparedKanaOffsets[start]);
        return string.Concat(Enumerable.Range(start, end - start).Select(k => units[k].Kana));
    }

    private string Raw(IReadOnlyList<CompositionUnit> units, int start, int end)
    {
        if (ReferenceEquals(units, _preparedUnits) && start >= 0 && end < _preparedRawOffsets.Length)
            return _preparedRaw.Substring(_preparedRawOffsets[start], _preparedRawOffsets[end] - _preparedRawOffsets[start]);
        return string.Concat(Enumerable.Range(start, end - start).Select(k => units[k].Raw));
    }
}
