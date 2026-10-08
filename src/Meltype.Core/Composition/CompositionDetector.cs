// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>
/// 変換ボックスの中身のうち、どこを英字のまま見せるかを決める。
///
/// 日本語入力中に google と打って「ごおｇぇ」になるのを防ぐのが目的。英語かどうかは区間ごとに判定するので、
/// 「きょうは google」なら google の部分だけが英字になる。区間の区切りはかな 1 音の境目だけ。
/// 未確定のうちは何度でも表示を作り直せるので、ここでの判定は IME 自動切替より積極的でよいが、
/// 既定は日本語で、英語と判断できる根拠があるときだけ英字にする。
/// </summary>
public sealed class CompositionDetector
{
    private readonly RomajiDetector _romaji;
    private readonly DictionaryDetector _japanese;
    private readonly EnglishDetector _english;
    private readonly TypoDetector _typo;
    private readonly ProperNouns _proper;
    private readonly KanaDetector? _kana;

    private static readonly HashSet<string> DomainSuffixes = ["ai", "app", "au", "biz", "ca", "cn", "co", "com", "de", "dev", "edu", "eu", "fr", "gg", "gov", "info", "in", "io", "jp", "kr", "me", "net", "org", "uk", "us", "xyz"];

    public CompositionDetector(RomajiDetector romaji, DictionaryDetector japanese, EnglishDetector english, TypoDetector typo, ProperNouns? proper = null,
        KanaDetector? kana = null)
    {
        _kana = kana;
        _romaji = romaji;
        _japanese = japanese;
        _english = english;
        _typo = typo;
        _proper = proper ?? new ProperNouns();
    }

    public static CompositionDetector CreateDefault(string? userDictionaryDirectory = null)
    {
        var romaji = RomajiDetector.CreateDefault(userDictionaryDirectory);
        var japaneseWords = DictionarySource.Load("japanese.txt", userDictionaryDirectory).ToList();
        var japanese = new DictionaryDetector(japaneseWords, romaji);
        var proper = ProperNouns.Load(userDictionaryDirectory);
        var english = new EnglishDetector(DictionarySource.Load("english.txt", userDictionaryDirectory).Concat(proper.LowercaseWords));
        return new CompositionDetector(romaji, japanese, english, new TypoDetector(japanese.Words), proper, new KanaDetector(japaneseWords, romaji));
    }

    private static readonly Lazy<WordList> ReadableEnglish = new(() =>
    {
        var list = new WordList();
        foreach (var word in DictionarySource.Load("english-readable.txt", null)) list.Add(word);
        return list;
    });

    public RomajiDetector Romaji => _romaji;

    /// <summary>普通の英単語の判定に使う Windows のスペルチェッカー。null なら同梱の辞書だけ。</summary>
    public IWordChecker? SpellChecker { get; set; }

    /// <summary>ユーザーが英字 / かなに直して覚えた語 (自動の判定より優先する)。</summary>
    public LanguageMemory? Memory { get; set; }

    /// <summary>英字の並びが、ローマ字としてよく使う日本語になるか (kyouha = 今日は、tomato = とまと)。無ければ使わない。</summary>
    public Func<string, bool>? IsCommonJapanese { get; set; }

    public ProperNouns ProperNouns => _proper;

    /// <summary>
    /// 単位列 (+ 入力途中の子音) を英語区間と日本語区間に分ける。
    /// 先頭から見て、ある単位から始まる最長の「英語と言える」区間があればそこを英語にする。
    /// </summary>
    /// <param name="precedingEnglish">入力欄のキャレットの前の確定済みの文字が英語なら true、日本語なら false、分からなければ null。</param>
    /// <param name="followingEnglish">キャレットの後ろの文字が英語なら true、日本語なら false、分からなければ null。</param>
    /// <param name="level">判定の強さ。手動 (Manual) では Shift で打った大文字始まりの語だけを英語にする。</param>
    /// <param name="englishSentence">キャレットの前が英文 (空白で区切った英単語が 2 語以上続いて空白で終わる: "I want ")。
    /// 日本語の文の中の英単語 (GitHub の) より強い英語の根拠として扱う。</param>
    public IReadOnlyList<CompositionSegment> Segment(IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish = null, bool? followingEnglish = null,
        DetectionLevel level = DetectionLevel.Balanced, bool englishSentence = false, bool kanaInput = false, bool final = false)
    {
        var segments = FindSpans(units, pending, precedingEnglish, followingEnglish, level, englishSentence && precedingEnglish == true, kanaInput, final);
        if (kanaInput) return segments;
        // 辞書にない英単語 (stackoverflow など) を最初から打っているなら全体を英語にする。
        // 途中の区間 (… flow) だけを英語にすると「sたcこvえrflow」のようになってしまう。
        // ただし先頭が辞書の英単語として区切れている (github に push) ならその区切りを使う。
        // 助詞 + 既知の英単語 (ya|python) も FindSpans の区切りを優先する (やpython が yapython になるのを防ぐ)。
        var whole = Raw(units, 0, units.Count) + pending;
        if (UnknownWordThenJapanese(units, pending, segments, level, whole) is { } split) return split;
        if (level != DetectionLevel.Manual && !segments[0].IsEnglish && Memory?.Get(whole.ToLowerInvariant()) != false && IsUnknownEnglishWord(whole))
        {
            var lowerWhole = whole.ToLowerInvariant();
            if (Detection.DictionaryDetector.StartsWithParticle(lowerWhole) is not { } leading ||
                lowerWhole.Length - leading.Length < 3 ||
                !IsKnownEnglishWord(lowerWhole[leading.Length..]))
            {
                return [new CompositionSegment(true, "", whole)];
            }
        }
        return segments;
    }

    private List<CompositionSegment>? UnknownWordThenJapanese(IReadOnlyList<CompositionUnit> units, string pending, List<CompositionSegment> segments, DetectionLevel level, string whole)
    {
        // 知らない英字の語 + 助詞で始まる日本語 (grokga、grokniyoruto) を最初から打っているなら、語は英字・後ろは日本語 (grokが)。
        // 語は、ローマ字の打ちかけとしても読めない (gr) もの。後ろは最後までローマ字として読めるもの。
        if (level != DetectionLevel.Manual && !segments[0].IsEnglish && whole.All(char.IsAsciiLetter) && !IsKnownEnglishWord(whole))
        {
            for (var k = 1; k < units.Count; k++)
            {
                var stem = Raw(units, 0, k).ToLowerInvariant();
                if (stem.Length < 3 || _romaji.AnalyzeFragment(stem).IsValid || IsKnownEnglishWord(stem) && stem.Length >= 5) continue;
                // 語の頭から読めない (gr): 日本語の後ろの英単語 (kyouha|google) ではない。
                // 読めない 1 文字の後ろが最後まで読める (s|dake) なら、英字 1 文字 + 日本語。
                var readable = _romaji.Analyze(stem).Tokens.Sum(t => t.Romaji.Length);
                if (readable >= 2 || _romaji.Analyze(stem[(readable + 1)..]) is { IsValid: true, Partial: "" }) continue;
                var rest = (Raw(units, k, units.Count) + pending).ToLowerInvariant();
                if (!TrailingParticles.Any(p => rest.StartsWith(p, StringComparison.Ordinal)) || stem[^1] == rest[0] ||
                    _romaji.Analyze(rest) is not { IsValid: true, Partial: "" or "n" }) continue;
                return [new CompositionSegment(true, "", Raw(units, 0, k)), Japanese(units, k, units.Count, pending)];
            }
        }
        return null;
    }

    private List<CompositionSegment> FindSpans(IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish, bool? followingEnglish, DetectionLevel level, bool englishSentence, bool kanaInput, bool final)
    {
        var n = units.Count;
        var segments = new List<CompositionSegment>();
        var japaneseStart = 0;
        // 区間の前が英語か: 先頭なら入力欄の確定済みの文字、途中なら直前の区間 (英語区間の直後なら英語、それ以外は日本語)。
        bool? PrecededByEnglish(int start) => start == 0 ? precedingEnglish : segments.Count > 0 && segments[^1].IsEnglish && japaneseStart == start;
        // 前の文脈の点数: 英文の続きなら +2、英語なら +1、日本語なら -1、分からなければ 0。
        int BeforeScore(int start) => start == 0 && englishSentence ? 2 : Score(PrecededByEnglish(start));
        var i = 0;
        while (i < n)
        {
            var found = -1;
            // ドメインの . の後ろは、短い国別・用途別トップレベルドメインでも英字のままにする (tetr.io、Wakatte.TV)。
            if (!kanaInput && level != DetectionLevel.Manual && i > 0 && units[i - 1].Raw == "." && PrecededByEnglish(i) == true)
            {
                for (var j = n; j > i; j--)
                {
                    var domainLabel = (Raw(units, i, j) + (j == n ? pending : "")).ToLowerInvariant();
                    if (DomainSuffixes.Contains(domainLabel) || !final && DomainSuffixes.Any(tld => tld.StartsWith(domainLabel, StringComparison.Ordinal)))
                    {
                        found = j;
                        break;
                    }
                }
            }
            // 英文の中の記号 (, . ! ? -) は読点・句点にせず半角のまま。日本語の文の中の英単語の後 (今日はgoogle、) は日本語の記号。
            // (かな入力では 、。 も かなのキーなので対象外)
            if (!kanaInput && IsAsciiSymbol(units[i]) && PrecededByEnglish(i) == true && segments.All(s => s.IsEnglish || !s.Raw.Any(char.IsAsciiLetter))) found = i + 1;
            if (!kanaInput && found < 0) found = UserNameEnd(units, i, pending);
            if (!kanaInput && found < 0 && level != DetectionLevel.Manual) found = CapitalizedWordEnd(units, i, pending, final);
            if (!kanaInput && found < 0 && level != DetectionLevel.Manual) found = HyphenatedWordEnd(units, i, pending);
            // 英語の語 + 数字のすぐ後ろの英単語 (part1026|beta、win11|pro) は、ローマ字として読めても英語 (ベタ にしない)。
            // 助詞で始まるなら日本語 (PS5|wokaitai)。
            if (!kanaInput && found < 0 && level != DetectionLevel.Manual && AlphanumericSuffixEnd(units, i, pending, segments, japaneseStart) is var suffix and > 0) found = suffix;
            for (var j = n; j > i && found < 0; j--)
            {
                // 区間の後ろ: 末尾まで打っているならキャレットの後ろの文字、途中なら続きの日本語。
                // 後ろが記号だけ (let's go! の !) なら、語はそこで打ち終わっている: Enter で確定するときと同じく末尾の語として見る
                // (記号を日本語の続きとみなして、英文の中の go・no を ご・の にしていた)。
                // (入力が 1 語 + 記号だけのとき。途中の区間 (BE|kana|?) の後ろの記号は、今までどおり日本語の続きとみなす)
                var symbolsAfter = i == 0 && j < n && pending.Length == 0 && Enumerable.Range(j, n - j).All(k => IsAsciiSymbol(units[k]));
                var after = j == n || symbolsAfter ? followingEnglish : false;
                // 英単語のすぐ後ろの する の活用 (push + site = して、commit + sita = した) は、英単語 (site) でも日本語
                // (末尾だと pushsite 全体が英字になっていた)。
                if (!kanaInput && PrecededByEnglish(i) == true && IsSuruForm(Kana(units, i, j))) continue;
                var english = kanaInput
                    ? IsEnglishSpanKana(Raw(units, i, j), Kana(units, i, j), atEnd: j == n, BeforeScore(i), after, level, final, Kana(units, j, Math.Min(n, j + 2)))
                    : IsEnglishSpan(Raw(units, i, j) + (j == n ? pending : ""), atEnd: j == n, BeforeScore(i), after, startOfInput: i == 0, level, final, endsWord: symbolsAfter,
                        unreadable: HasUnreadable(units, i, j) || EndsWithLoneSokuon(units, j), next: j < n ? units[j].Raw + (j + 1 == n ? pending : "") : null);
                if (english)
                {
                    found = j;
                    break;
                }
            }
            // 途中で終わる英語の区間 (te + al… の teal) より、少し後ろから末尾まで続く長い英単語 (alcoholic) があれば、そちらを取る
            // (sometealcoholic → 染めて + alcoholic。teal を取ると残りの coholic がローマ字になってしまう)。
            if (found > i && found < n && !kanaInput && !IsAsciiSymbol(units[i]))
            {
                // 後ろに日本語が続いてもよい (motte|school|he → mottes を取ると chool が ちょおl になる。持って + school + へ)。
                var foundLength = Raw(units, i, found).Length;
                for (var k = i + 1; k < found && found >= 0; k++)
                {
                    for (var e = n; e > found; e--)
                    {
                        var word = Raw(units, k, e) + (e == n ? pending : "");
                        if (word.Length >= foundLength && IsLongEnglishWord(word))
                        {
                            found = -1;
                            break;
                        }
                    }
                }
            }
            // 短い英単語 (yap) が助詞 (ya) + 後ろの英単語 (play) の頭を食っているなら、助詞側を優先する
            // (yap|lay → ya|play。スペルチェッカーが yap / lay を知っているとやplay が yaplay になる)。
            if (found > i && !kanaInput && !IsAsciiSymbol(units[i]))
            {
                var head = Raw(units, i, found).ToLowerInvariant();
                if (Detection.DictionaryDetector.StartsWithParticle(head) is { } leading && head.Length > leading.Length)
                {
                    var covered = 0;
                    var afterParticle = i;
                    while (afterParticle < found && covered < leading.Length) covered += units[afterParticle++].Raw.Length;
                    if (covered == leading.Length)
                    {
                        var rest = Raw(units, afterParticle, n) + pending;
                        if (rest.Length >= 3 && IsKnownEnglishWord(rest)) found = -1;
                    }
                }
            }
            if (found < 0)
            {
                i++;
                continue;
            }
            if (i > japaneseStart) segments.Add(Japanese(units, japaneseStart, i, ""));
            segments.Add(new CompositionSegment(true, "", Raw(units, i, found) + (found == n ? pending : "")));
            i = found;
            japaneseStart = found;
            if (found == n) return segments;
        }

        // 打ちかけの 1 文字だけ (how r u の r): 1 文字の語の決まり (前が英語なら r・u は英字) で見る。
        if (n == 0 && !kanaInput && pending.Length == 1 && char.IsAsciiLetterLower(pending[0]) &&
            IsEnglishSpan(pending, atEnd: true, BeforeScore(0), followingEnglish, startOfInput: true, level, final))
        {
            return [new CompositionSegment(true, "", pending)];
        }
        // Shift を押して打った入力途中の子音 (W, K) は大文字のまま英字で見せる (かなの読み途中として小文字にしない)。
        if (pending.Length > 0 && char.IsAsciiLetterUpper(pending[0]))
        {
            if (japaneseStart < n) segments.Add(Japanese(units, japaneseStart, n, ""));
            segments.Add(new CompositionSegment(true, "", pending));
            return segments;
        }
        if (japaneseStart < n || pending.Length > 0 || segments.Count == 0)
        {
            segments.Add(Japanese(units, japaneseStart, n, pending));
        }
        return segments;
    }

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
    private bool IsSpellWord(string lower) => SpellChecker is { } checker && (checker.IsWord(lower) || checker.AutoCorrection(lower) is not null);

    /// <summary>よくある英語の打ち間違いなら正しい綴り (teh → the)。大文字で始まる語は大文字で始める。</summary>
    public string? EnglishAutoCorrection(string word)
    {
        if (word.Length < 2 || !word.All(char.IsAsciiLetter)) return null;
        // -pedia 複合 (conservapedia など) はスペルチェッカーが別綴りに「訂正」してしまうことがあるので触らない。
        if (word.EndsWith("pedia", StringComparison.OrdinalIgnoreCase) && word.Length >= 5) return null;
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
        // english-readable.txt / -pedia (IsEnglishSpan と同じ根拠)。打ち間違い直しが英字の語を崩さないようにする。
        if (lower.Length >= 4 && ReadableEnglish.Value.ContainsWord(lower)) return true;
        return lower.EndsWith("pedia", StringComparison.Ordinal) && lower.Length >= 5 &&
               (lower.Length == 5 || lower.Length - 5 >= 3);
    }

    /// <summary>よく使う語の読み (readings.txt、3 文字以上)。かな入力で、英単語のキーが日本語の語を打っていないかを見る。</summary>
    private static readonly Lazy<HashSet<string>> Readings = new(() =>
        DictionarySource.ReadEmbedded("readings.txt").Split('\n')
            .Select(line => line.Split('\t')[0].Trim())
            .Where(reading => reading.Length >= 3 && !reading.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// かなの大部分 (3/4 以上) を占める、3 文字以上のよく使う語があるか。語は後ろのかな (kanaAfter) にはみ出してもよい
    /// (てにはい + る = 手に入る)。
    /// </summary>
    private static bool ContainsJapaneseReading(string kana, string kanaAfter = "")
    {
        var text = kana + kanaAfter;
        for (var start = 0; start < kana.Length; start++)
            for (var length = 3; start + length <= text.Length; length++)
                if (Math.Min(start + length, kana.Length) - start is var inside && inside * 100 >= kana.Length * 75 &&
                    Readings.Value.Contains(text.Substring(start, length))) return true;
        return false;
    }

    private static readonly string[] SuruForms = ["する", "すれ", "した", "して", "しま", "しな", "しよ", "しと", "しちゃ", "しろ", "され", "させ", "せず"];

    /// <summary>する の活用 (して・した・します …) だけでできたかなか。</summary>
    private static bool IsSuruForm(string kana) => SuruForms.Any(kana.StartsWith) && kana.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー');

    private static int Score(bool? english) => english switch { true => 1, false => -1, null => 0 };

    /// <summary>英文の中では半角のままにする記号。[ ] は日本語の入力では「」なので含めない (英単語の後ろでも「」: bot「Thinking」)。</summary>
    private static bool IsAsciiSymbol(CompositionUnit unit) =>
        unit.Raw.Length == 1 && unit.Raw[0] is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(unit.Raw[0]) && unit.Raw[0] is not ('[' or ']');


    /// <param name="final">打ち終わった (Space・Enter)。末尾の区間でも、英単語の打ちかけ (amaz) は英語の根拠にしない。</param>
    /// <param name="unreadable">区間にローマ字として読めなかった英字がある (zoom + de の m、bug + wo の g)。</param>
    /// <param name="endsWord">区間の後ろが記号だけ (let's go! の go)。語はそこで打ち終わっているので、短い語も末尾の語と同じく見る。</param>
    private bool IsEnglishSpan(string span, bool atEnd, int before, bool? after, bool startOfInput, DetectionLevel level, bool final = false, bool unreadable = false, string? next = null, bool endsWord = false)
    {
        // まだ続きを打つかもしれない末尾の区間 (打ちかけの英単語を英語と見てよい)。
        var growing = atEnd && !final;
        if (IsContraction(span)) return level != DetectionLevel.Manual || char.IsAsciiLetterUpper(span[0]);
        if (span.Length == 0 || !span.All(char.IsAsciiLetter)) return false;
        var lower = span.ToLowerInvariant();
        // 小文字で始まって途中に大文字がある区間 (iPC、meteSNS) は、固有名詞の書き方 (iPhone・eBay・macOS) でなければ 1 語ではない:
        // 小文字の部分は前の日本語の続き (atarashi|i|PC → あたらしいPC、motome|te|SNS → 求めてSNS)。
        if (char.IsAsciiLetterLower(span[0]) && span.Skip(1).Any(char.IsAsciiLetterUpper) && _proper.Canonical(lower) != span) return false;
        // 短い区間の最後の子音が、次の音と合わせて っ になる (u|lo|t + ti = ぉっち) なら、英単語 (lot) ではなく日本語の途中
        if (lower.Length <= 4 && next is [var following, ..] && char.ToLowerInvariant(following) == lower[^1] && lower[^1] is not ('a' or 'i' or 'u' or 'e' or 'o' or 'n') &&
            _romaji.AnalyzeFragment(lower[..^1]) is { IsValid: true, Partial: "" })
        {
            return false;
        }
        // 数字のすぐ前の、ローマ字として読めない子音で始まる短い英字 (kaibunsho|rta|2026、ps5): 略語。日本語の打ちかけではない
        if (next is [var digit, ..] && char.IsAsciiDigit(digit) && span.Length is >= 2 and <= 6 && span.All(char.IsAsciiLetter) &&
            _romaji.AnalyzeFragment(lower[..2]) is { IsValid: false })
        {
            return true;
        }
        // 日本語のすぐ後ろで、助詞 + 英単語 (dochira|mo|user の mouser) は、スペルチェッカーが 1 語と言っても 助詞 + 英単語
        // (同梱の英語の辞書の語は除く)。後ろの英単語の区間は、この後で別に見る。
        if (before < 0 && Detection.DictionaryDetector.StartsWithParticle(lower) is { } leading && lower.Length - leading.Length >= 3 &&
            !_english.Words.ContainsWord(lower) && IsKnownEnglishWord(lower[leading.Length..]))
        {
            return false;
        }

        var inDictionary = _english.Words.ContainsWord(lower);
        // 知らない英字の語 + 助詞 (grok|ga、figma|de) は、助詞までを 1 語にしない。語の部分だけの区間はこの後で別に見る
        if (EndsWithParticleAfterUnknownWord(lower)) return false;
        // Windows のスペルチェッカーの英単語 (meeting, name …)。ローマ字の語 (kore, sore) まで含む緩いものなので、
        // ローマ字として読めない語か、前後の文脈で英語と分かるときだけ使う (同梱の辞書の語より弱い)。
        var conservative = level == DetectionLevel.Conservative;
        // 小書き文字の綴り (mala = まぁ, xtu = っ) で最後まで読める語は、日本語をわざわざ打っている。同梱の辞書の英単語以外は日本語。
        // (6 文字以上のスペルチェッカーの英単語は除く: chocolate の la = ぁ でも英語)
        var smallKanaSpelling = !_romaji.Analyze(lower).IsValid && _romaji.AnalyzeFragment(lower) is { IsValid: true, Partial: "" } && !(lower.Length >= 6 && IsSpellWord(lower));
        var spellWord = !inDictionary && !smallKanaSpelling && IsSpellWord(lower);
        var exact = inDictionary || (spellWord && !_romaji.Analyze(lower).IsValid);
        var prefix = growing && lower.Length >= 4 && !conservative && !smallKanaSpelling && _english.IsPrefix(lower);

        // 大文字で始まる語 (Shift を押して打った) は固有名詞や英文。1 文字 (I) でも、末尾まで打っている途中でも英語。
        // 手動でもこれだけは英語にする (Shift を押したのはユーザーの明示的な指定)。
        // 大文字で始まる語の後ろに記号が続く (Ah! Ooh, Wow.) なら、その語で終わっている: スペルチェッカーの語でも英語。
        if (char.IsAsciiLetterUpper(span[0]) && (exact || prefix || atEnd || (spellWord && next is [var symbol, ..] && !char.IsAsciiLetterOrDigit(symbol)))) return true;
        if (level == DetectionLevel.Manual) return false;
        // ユーザーが英字 / かなに直して覚えた語。ただし短くてローマ字として読める語 (go、no) は、日本語のすぐ後ろ
        // (nihon|go) では使わない (一度 go を英字で確定しただけで、日本語 が にほんgo になっていた)。
        if (Memory?.Get(lower) is { } learned && !(learned && before < 0 && lower.Length <= 3 && _romaji.AnalyzeFragment(lower) is { IsValid: true, Partial: "" })) return learned;
        // 5 文字以上の英単語で、ローマ字としても読めるもの:
        // - c 行の綴り (camera、coffee、class) は英語。日本語を打つときは k を使う (カメラ は kamera)。
        // - ローマ字として読むと ぢ・づ になる綴り (radio = らぢお、studio、audio) で、ふつうの日本語の語にならないなら英語。
        //   (sake・tokyo・suzuki のような日本由来の語は、ふつうのかなになるので、ここでは英語にしない)
        //   日本語の語にもなるもの (tomato = とまと、piano = ぴあの、anime) は、今までどおり前後の文脈で決める。
        // 日本語の辞書の語 (suzuki) と、慎重なときは使わない。続きと合わせて日本語の語になる (sense + i = せんせい) ときも使わない。
        if (lower.Length >= 5 && !conservative && (inDictionary || IsSpellWord(lower)) && !_japanese.IsPrefix(lower) &&
            !_proper.Contains(lower) && _romaji.AnalyzeFragment(lower) is { IsValid: true } &&
            !(next is { Length: > 0 } && IsCommonJapanese is { } common && !common(lower) && common(lower + next.ToLowerInvariant())))
        {
            if (lower.Contains('c') && !lower.Contains("ch")) return true;
            if (_romaji.AnalyzeFragment(lower).Kana.IndexOfAny(['ぢ', 'づ']) >= 0 && IsCommonJapanese?.Invoke(lower) != true) return true;
        }
        // ローマ字として最後まで読めても、日本語の語にならない英単語 (feature = ふぇあつれ、remote = れもて。dictionaries/english-readable.txt、#12)。
        // 日本語の語の始まりにもならない語だけを入れているので、後ろに日本語が続いても (feature|wo) 英語。
        if (lower.Length >= 4 && ReadableEnglish.Value.ContainsWord(lower)) return true;
        // -pedia (pedia / protopedia …)。ローマ字だと ぺぢあ になり、途中の to が助詞「と」に割れる (pro|to|pedia)。
        // wikipedia はスペルチェッカーで足りるが、pedia 単体や未登録の複合はここですくう。
        if (lower.EndsWith("pedia", StringComparison.Ordinal) && lower.Length >= 5 &&
            (lower.Length == 5 || lower.Length - 5 >= 3) && IsCommonJapanese?.Invoke(lower) != true)
        {
            return true;
        }
        // c 行の綴りで読める語 (care = かれ、can = かん) が日本語の途中にあるなら、日本語を打っている (fucarete → ふかれて、shoucanshi → しょうかんし)。
        // 入力全体がその語だけのときは英語。
        if (!(startOfInput && atEnd))
        {
            if (lower.Contains('c') && _romaji.Analyze(RomajiDetector.ReadCRow(lower)) is { IsValid: true, Partial: "" or "n" }) return false;
            // v 行 (va = ゔぁ): 辞書の英単語 (video) でなければ日本語 (vanpaia → ゔぁんぱいあ → ヴァンパイア)。
            // スペルチェッカーの 5 文字以上の英単語 (invite、private) は、ゔぃ と読める綴りでも英語 (いんviteしました になっていた: issue #69)。
            if (lower.Contains('v') && !lower.Contains('l') && !lower.Contains('x') && !inDictionary && !_proper.Contains(lower) &&
                !(lower.Length >= 5 && IsSpellWord(lower) && IsCommonJapanese?.Invoke(lower) != true) &&
                _romaji.AnalyzeFragment(lower) is { IsValid: true, Partial: "" or "n" })
            {
                return false;
            }
        }
        // 1 文字は英文の中の a / i だけ。
        // 1 文字は英文の中の a / i と、チャットの略し方の u (you)・r (are) だけ。前が英語の語なら英字 (A fool a fool a, for u / how r u)。
        // 語として独立している (後ろが空白・記号・終わり) ときだけ。後ろにかなが続く (HH|i|reta = HH いれた) なら日本語の 1 音。
        if (span.Length < 2) return lower is "a" or "i" or "u" or "r" && before >= 1 && !(next is { Length: > 0 } && char.IsAsciiLetter(next[0]));

        // 英語の固有名詞 (amazon, adobe, netflix) は、ローマ字として読めても英語。日本語の語と同じ綴りなら除く。
        // 短い名前 (ben, tom) の偶然の一致 (にほんごの|ben|きょう) を避けるため 4 文字以上。文の途中の区間なら
        // 5 文字以上 (きょうは|amazon|で) か、ローマ字として読めないもの。末尾の 4 文字の語は、日本語のすぐ後ろでなければ
        // (ある程度は の teido|ha を tei|doha = Doha にしない)。慎重なら、ローマ字として読めないものだけ。
        // ただし、ローマ字として最後まで読める固有名詞 (korea = これあ) の後ろに、助詞でない日本語が続くなら、日本語の語の途中
        // (korea|reka → これあれか。Korea|reka にしない)。助詞が続くなら固有名詞 (korea|de → Koreaで)。
        if (lower.Length >= 4 && _proper.Contains(lower) && next is { Length: > 0 } && char.IsAsciiLetter(next[0]) &&
            _romaji.Analyze(lower) is { IsValid: true, Partial: "" } && Detection.DictionaryDetector.StartsWithParticle(next.ToLowerInvariant()) is null)
        {
            return false;
        }
        if (lower.Length >= 4 && _proper.Contains(lower) && !_japanese.Words.ContainsWord(lower) &&
            (conservative ? !_romaji.Analyze(lower).IsValid : (atEnd && before >= 0) || lower.Length >= 5 || !_romaji.Analyze(lower).IsValid))
        {
            return true;
        }
        if (growing && lower.Length >= 4 && !conservative && !smallKanaSpelling && _proper.HasPrefix(lower) && !_japanese.IsPrefix(lower)) return true;

        // 英単語で、ローマ字として読めない英字を含む (zoom + でかいぎ → m が読めない)。日本語の文の途中でも英語。
        // 日本語の後ろで、助詞 + 読めない英字 (の + ts: jissainotsyu) は、英単語 (not) ではなく 助詞 + 英字。
        // 助詞の後ろがローマ字の打ちかけ (ts = つ) のときだけ。ローマ字にならない (he + lp: help) なら英単語。
        if (unreadable && before < 0 && Detection.DictionaryDetector.StartsWithParticle(lower) is { } particle && lower.Length - particle.Length <= 2 &&
            _romaji.AnalyzeFragment(lower[particle.Length..]).IsValid) return false;
        // 同梱の辞書の英単語で、ローマ字としては促音 (っ) を使わないと読めない語 (issue = いっすえ, apple) は英語。
        // 日本語の語 (の先頭) なら除く。
        if (inDictionary && lower.Length >= 4 && _romaji.Analyze(lower) is { IsValid: true, Sokuon: > 0 } && !_japanese.IsPrefix(lower)) return true;
        // 2 文字でも、同梱の辞書の語で読めない英字がある (ok + notasuku の k) なら英語。
        if (unreadable && (exact || spellWord) && (lower.Length >= 3 || inDictionary)) return true;

        // 英語とも日本語とも読める語 (sushi, repo, make) は前後の両方で決める。前が英語なら +1・日本語なら -1、
        // 後ろも同じように数え、合計が必要な点数に届けば英語 (どちらも分からない・食い違うときは日本語)。
        // 入力の先頭で末尾まで打っている途中なら英単語の先頭でも数える (英文の続きを打っている途中を英字で見せる)。
        // 変換ボックス内の英語区間の続きは、偶然の一致 (google + de) を避けるため 3 文字以上の英単語そのものだけ。
        // 助詞などと同じ 2 文字の語 (no, to, ga) は、前後の両方が英語のときだけ (標準)。
        var ambiguous = level switch
        {
            // 積極的でも、助詞と同じ形の 2 文字 (ni, ga) は英単語の先頭というだけでは英語にしない。
            DetectionLevel.Aggressive => startOfInput && atEnd ? exact || spellWord || (!final && lower.Length >= 3 && _english.IsPrefix(lower)) : exact || spellWord,
            DetectionLevel.Conservative => (exact || spellWord) && (lower.Length >= 3 || before >= 2),
            _ => startOfInput && endsWord ? exact || spellWord :
                startOfInput && atEnd ? exact || spellWord || lower.Length == 1 || (!final && !smallKanaSpelling && _english.IsPrefix(lower)) : (exact || spellWord) && lower.Length >= 3,
        };
        var needed = level switch
        {
            DetectionLevel.Aggressive => 1,
            DetectionLevel.Conservative => 2,
            // 助詞と同じ形の 2 文字の語 (no, to, ga) は両側が必要。子音で終わる語 (is, at, my) は日本語の語にならないので片側でよい。
            _ => lower.Length <= 2 && _romaji.Analyze(lower) is { IsValid: true, Partial: "" or "n" } ? 2 : 1,
        };
        // スペルチェッカーだけが知っている、最後までローマ字として読める語 (shite, kore) は、前の英単語 1 つ (push) では足りない
        // (pushshite → pushして)。英文の続き (+2) か、前後の両方が英語のときだけ。
        if (spellWord && !inDictionary && _romaji.Analyze(lower) is { IsValid: true, Partial: "" }) needed = Math.Max(needed, 2);
        // 前が英文でも、後ろが日本語なら英文の強さは数えない (I love |sushi| が好き → 食い違うので日本語)。
        if (ambiguous && (after == false ? Math.Min(before, 1) : before) + Score(after) >= needed) return true;
        var analysis = _romaji.Analyze(lower);
        if (!exact && !prefix && !spellWord) return false;

        if (!analysis.IsValid)
        {
            if (!exact && !prefix) return false;
            // 日本語のすぐ後ろの短い英単語 (thin) が、変換ボックスの綴り (thi = てぃ) では最後まで読めて、続き (gu) とも読めるなら、
            // 外来語のカタカナ (hosu|thin|gu = ホスティング) を打っている途中。英単語にしない (ほすthinぐ になっていた: issue #153)。
            if (before < 0 && !atEnd && lower.Length <= 4 && next is { Length: > 0 } && char.IsAsciiLetter(next[0]) &&
                _romaji.AnalyzeFragment(lower) is { IsValid: true, Partial: "" or "n" } &&
                _romaji.AnalyzeFragment(lower + next.ToLowerInvariant()).IsValid)
            {
                return false;
            }
            if (!exact && smallKanaSpelling) return false;
            // 日本語のすぐ後ろの 2 文字の語で、変換ボックスでは読める綴り (こ + we = こうぇ、wi = うぃ) は日本語。
            if (smallKanaSpelling && lower.Length <= 2 && before < 0) return false;
            // ローマ字として読めない英単語。途中の区間は 3 文字以上だけ (短い語の偶然の一致を避ける)。
            return (atEnd && (exact || !conservative)) || span.Length >= 3;
        }
        // ローマ字として読めても、末尾が子音の英単語 (git, zoom, about) で、日本語の語の途中でもないなら英語。
        // スペルチェッカーだけが知っている語 (meeting, my) は、前が日本語でないときだけ。
        // 打ち終わっていれば、日本語の語の打ちかけ (it → itai) かどうかは気にしなくてよい。
        var notJapanesePrefix = final || !_japanese.IsPrefix(lower);
        if (spellWord && atEnd && before >= 0 && analysis.Partial.Length > 0 && analysis.Partial != "n" && notJapanesePrefix) return true;
        return atEnd && exact && (!conservative || lower.Length >= 3) && analysis.Partial.Length > 0 && analysis.Partial != "n" && notJapanesePrefix;
    }

    /// <summary>
    /// かな入力 (JIS) の区間が英語か。打ったキーの英字 (Raw) が英単語で、かなとしては日本語の語にならないなら英語。
    /// かなとしても日本語の語 (の先頭) になるなら、ローマ字入力の「英語とも日本語とも読める語」と同じく前後の文脈で決める。
    /// </summary>
    private bool IsEnglishSpanKana(string span, string kana, bool atEnd, int before, bool? after, DetectionLevel level, bool final = false, string kanaAfter = "")
    {
        if (span.Length == 0 || !span.All(char.IsAsciiLetter)) return false;
        var lower = span.ToLowerInvariant();
        var inDictionary = _english.Words.ContainsWord(lower) || (lower.Length >= 4 && _proper.Contains(lower));
        // キー列が偶然スペルチェッカーの語になることがあるので、スペルチェッカーの語は 4 文字以上だけ。
        var word = inDictionary || (lower.Length >= 4 && IsSpellWord(lower));
        var prefix = atEnd && !final && level == DetectionLevel.Aggressive && lower.Length >= 4 && _english.IsPrefix(lower);

        // Shift を押して打った大文字で始まる語は英語 (手動でも)。
        if (char.IsAsciiLetterUpper(span[0]) && (word || prefix || atEnd)) return true;
        if (level == DetectionLevel.Manual) return false;
        if (Memory?.Get(lower) is { } learned) return learned;
        if (!(word || prefix) || lower.Length < 2) return false;

        var japanese = _kana?.IsJapaneseWordOrPrefix(kana) == true;
        var context = (after == false ? Math.Min(before, 1) : before) + Score(after);
        if (context >= (level == DetectionLevel.Conservative ? 2 : 1)) return true;
        if (japanese) return false;
        // 前が日本語 (きょうは + google) でも、辞書の英単語 (4 文字以上) で、かなとしては日本語にならないなら英語。
        // かなのキーで打った日本語が、たまたま辞書の英単語のキーと同じになることは少ない (ローマ字と違い、1 キーが 1 文字)。
        // ただし、そのキーのかなに 3 文字以上の日本語の語が入っているなら、日本語を打っている (にかいも = item、かんせい = type)。
        if (context < 0) return (inDictionary && lower.Length >= 4 || word && lower.Length >= 6) && !ContainsJapaneseReading(kana, kanaAfter);
        var minimum = level switch { DetectionLevel.Aggressive => 2, DetectionLevel.Conservative => 4, _ => 3 };
        return lower.Length >= minimum;
    }

    /// <summary>
    /// 大文字で始まる語 (Shift を押して打った) の後ろに日本語が続いているなら、その語の終わり (単位の位置)。無ければ -1。
    /// 大文字で始まる区間は末尾まで英語になるので、そのままでは AutoIMEnotesuto → 全部英字 になってしまう。
    /// 語は、知っている英単語 (Github) か、大文字で終わる語 (AutoIME, OK, NHK)。後ろは 3 文字以上の小文字で、ローマ字として読めるもの。
    /// </summary>
    private int CapitalizedWordEnd(IReadOnlyList<CompositionUnit> units, int start, string pending, bool final)
    {
        // 見るのは、次の記号・数字・空白まで (長い文の OCR|woshi, … では、後ろの , までの woshi を見る)
        var n = start;
        while (n < units.Count && units[n].Raw.All(char.IsAsciiLetter) && units[n].Raw.Length > 0) n++;
        if (n < units.Count) pending = "";
        var whole = Raw(units, start, n) + pending;
        // 2 文字目が大文字の語 (iPad、iPhone、eSports) も、知っている語なら同じように区切る (iPad|deii → iPad でいい)
        var lowerStart = whole.Length >= 2 && char.IsAsciiLetterLower(whole[0]) && char.IsAsciiLetterUpper(whole[1]);
        if (whole.Length == 0 || !char.IsAsciiLetterUpper(whole[0]) && !lowerStart) return -1;
        // 全体が英単語・固有名詞 (Tokyo, Github) なら区切らない。
        if (IsKnownCapitalizedWord(whole)) return -1;
        for (var k = n - 1; k > start; k--)
        {
            var head = Raw(units, start, k);
            if (!head.All(char.IsAsciiLetter)) continue;
            if (lowerStart && !(head.Length >= 3 && IsKnownCapitalizedWord(head))) continue;
            // 大文字の略語に小文字が続いた形 (AIde、AIni) は語ではない。略語 (AI) の後ろがローマ字 (dekiru) と見る (issue #129)
            if (IsAcronymWithLowerTail(head)) continue;
            // 後ろは小文字のローマ字 (長音の - を含んでもよい: TSyu-za- の yu-za-)。
            var rest = Raw(units, k, n) + pending;
            // 後ろが助詞 1 つだけ (OCR|wo、English|ga) なら 2 文字でもよい
            if ((rest.Length < 3 && Detection.DictionaryDetector.StartsWithParticle(rest) != rest) || !rest.All(c => char.IsAsciiLetterLower(c) || c == '-') || !char.IsAsciiLetterLower(rest[0])) continue;
            // 知っている英単語・略語 (English、OCR) の後ろが助詞で始まるなら、その後ろに英単語が続いても (English|wo|happy) 区切る
            var knownHead = head.Length >= 2 && char.IsAsciiLetterUpper(head[^1]) || head.Length >= 3 && IsKnownCapitalizedWord(head) || head.Length >= 4 && IsSpellWord(head.ToLowerInvariant());
            if (knownHead && Detection.DictionaryDetector.StartsWithParticle(rest) is not null) return k;
            var analysis = _romaji.AnalyzeFragment(rest.Replace("-", ""));
            if (!analysis.IsValid || (final && analysis.Partial.Length > 0 && analysis.Partial != "n")) continue;
            // 大文字で終わる略語 (OCR)、知っている語 (Tokyo)、スペルチェッカーの 4 文字以上の語 (English)
            if (head.Length >= 2 && char.IsAsciiLetterUpper(head[^1]) || head.Length >= 3 && IsKnownCapitalizedWord(head) || head.Length >= 4 && IsSpellWord(head.ToLowerInvariant())) return k;
            // 大文字 1 文字 + 助詞で始まるローマ字 (A|nisiyouka → Aにしようか、B|noan → Bの案)。
            // 名前 (Tanaka、Hanako) を区切らないよう、後ろが助詞で始まるときだけ。
            if (head.Length == 1 && char.IsAsciiLetterUpper(head[0]) && Detection.DictionaryDetector.StartsWithParticle(rest.Replace("-", "")) is not null) return k;
        }
        // 後ろに大文字で始まる語が続く (Japanese|to|English…) と、後ろが小文字だけにならず上では区切れない。
        // 次の大文字の前までを見て、知っている語 + 助詞で始まるローマ字 (Japanese|to) なら区切る。
        var camel = start + 1;
        while (camel < n && !units[camel].Raw.Any(char.IsAsciiLetterUpper)) camel++;
        for (var k = camel - 1; camel < n && k > start; k--)
        {
            var head = Raw(units, start, k);
            var rest = Raw(units, k, camel);
            if (!rest.All(char.IsAsciiLetterLower) || Detection.DictionaryDetector.StartsWithParticle(rest) is null || !_romaji.AnalyzeFragment(rest).IsValid) continue;
            if (head.Length >= 3 && IsKnownCapitalizedWord(head) || head.Length >= 4 && IsSpellWord(head.ToLowerInvariant())) return k;
        }
        return -1;
    }

    /// <summary>
    /// 大文字 2 文字以上の後ろに小文字が続く (AIde・GPTni)。iOS・IDEs のような知っている書き方でなければ、
    /// 1 つの語ではなく、略語 + ローマ字の打ち始め。
    /// </summary>
    private bool IsAcronymWithLowerTail(string head)
    {
        var upper = 0;
        while (upper < head.Length && char.IsAsciiLetterUpper(head[upper])) upper++;
        return upper >= 2 && upper < head.Length && head[upper..].All(char.IsAsciiLetterLower) && !IsKnownCapitalizedWord(head);
    }

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)。接頭辞 + - + 3 文字以上の英単語なら英語。
    // 1 文字の母音 (o-bun = オーブン) は日本語の長音とまぎらわしいので e と x だけ。
    private static readonly HashSet<string> HyphenPrefixes = ["e", "x", "re", "co", "ex", "non", "anti", "semi", "multi", "pre", "sub", "post", "mid", "self", "well"];

    // 接頭辞の規則では拾えない、- の入ったよく使う英単語 (後ろが 2 文字以下など)
    private static readonly HashSet<string> HyphenatedWords = ["co-op", "re-do", "x-ray", "t-shirt", "wi-fi", "hi-fi", "e-book", "e-sports", "k-pop", "j-pop", "j-rock", "j-core", "p-hub", "talk-admin", "r-18", "sub-6", "gpt-6.7", "a-z", "u-turn", "check-in", "log-in", "sign-in", "add-on", "plug-in", "built-in", "follow-up", "set-up", "pop-up", "drop-down"];
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
    private static int UserNameEnd(IReadOnlyList<CompositionUnit> units, int start, string pending)
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

    /// <summary>ローマ字として読めない、5 文字以上の英単語 (alcoholic, pressure)。</summary>
    private bool IsLongEnglishWord(string raw)
    {
        if (raw.Length < 5 || !raw.All(char.IsAsciiLetter)) return false;
        var lower = raw.ToLowerInvariant();
        if (_romaji.Analyze(lower).IsValid) return false;
        return _english.Words.ContainsWord(lower) || _proper.Contains(lower) || IsSpellWord(lower);
    }

    private static readonly string[] TrailingParticles = ["kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha", "wa", "ya"];

    /// <summary>
    /// 知らない英字の語 + 助詞 (grokga = grok + が) か。語の部分は 3 文字以上でローマ字として読めないもの、全体は辞書に無いもの。
    /// 語の最後の子音と助詞の頭が同じ (lot + to = ろっと) なら っ の綴りなので除く。
    /// </summary>
    private bool EndsWithParticleAfterUnknownWord(string lower)
    {
        if (_english.Words.ContainsWord(lower) || _proper.Contains(lower) || Memory?.Get(lower) == true || IsSpellWord(lower)) return false;
        foreach (var particle in TrailingParticles)
        {
            if (!lower.EndsWith(particle, StringComparison.Ordinal)) continue;
            var stem = lower[..^particle.Length];
            if (stem.Length < 3 || stem[^1] == particle[0]) return false;
            return !_romaji.Analyze(stem).IsValid;
        }
        return false;
    }

    private bool IsKnownCapitalizedWord(string word)
    {
        var lower = word.ToLowerInvariant();
        return Memory?.Get(lower) == true || _english.Words.ContainsWord(lower) || _proper.Contains(lower);
    }

    /// <summary>最初の 3 文字以内でローマ字として読めなくなる、4 文字以上の語 (日本語の打ち間違いでもないもの)。</summary>
    private bool IsUnknownEnglishWord(string raw)
    {
        if (raw.Length < 4 || !raw.All(char.IsAsciiLetter)) return false;
        var lower = raw.ToLowerInvariant();
        var analysis = _romaji.Analyze(lower);
        if (analysis.IsValid) return false;
        // 小書き文字などの綴り (kaxnji, ulo) まで含めれば読めるなら、日本語を打っている。
        if (_romaji.AnalyzeFragment(lower).IsValid) return false;
        var firstInvalid = analysis.Tokens.Sum(t => t.Romaji.Length);
        if (firstInvalid >= 3) return false;
        // 読めない文字の後ろが普通のローマ字なら、英字 1 文字 + 日本語 (sだけが, Xがわかる) を打っている。
        var rest = lower[(firstInvalid + 1)..];
        if (rest.Length >= 3 && _romaji.AnalyzeFragment(rest).IsValid) return false;
        var typo = new List<Contribution>();
        _typo.Evaluate(lower, typo);
        return typo.Count == 0;
    }

    private static CompositionSegment Japanese(IReadOnlyList<CompositionUnit> units, int start, int end, string pending)
    {
        var kana = string.Concat(Enumerable.Range(start, end - start).Select(k => units[k].Kana));
        return new CompositionSegment(false, kana, Raw(units, start, end) + pending);
    }

    /// <summary>単位 [start, end) に、ローマ字として読めなかった英字 (かなにならなかった 1 文字) があるか。</summary>
    /// <summary>
    /// 英語の区間 + 数字のすぐ後ろ (part1026|beta) から始まる英字の並びが英単語なら、その終わり。違えば -1。
    /// segments・japaneseStart は FindSpans の途中の状態 (数字が、直前の英語の区間のすぐ後ろにあるかを見る)。
    /// </summary>
    private int AlphanumericSuffixEnd(IReadOnlyList<CompositionUnit> units, int start, string pending, List<CompositionSegment> segments, int japaneseStart)
    {
        static bool IsDigit(CompositionUnit unit) => unit.Raw is [var d] && char.IsAsciiDigit(d);
        if (start == 0 || !IsDigit(units[start - 1]) || units[start].Raw.Length == 0 || !char.IsAsciiLetter(units[start].Raw[0])) return -1;
        var k = start - 1;
        while (k >= 0 && IsDigit(units[k])) k--;
        if (k < 0 || segments.Count == 0 || !segments[^1].IsEnglish || japaneseStart != k + 1) return -1;
        var end = start;
        while (end < units.Count && units[end].Raw.Length > 0 && units[end].Raw.All(char.IsAsciiLetter)) end++;
        var word = Raw(units, start, end) + (end == units.Count ? pending : "");
        if (Detection.DictionaryDetector.StartsWithParticle(word.ToLowerInvariant()) is not null || !IsKnownEnglishWord(word)) return -1;
        return end;
    }

    /// <summary>
    /// 区間 [.., end) が、ん の後の っ (1 文字の子音) で終わるか (meeting|ga の g = っ)。区間だけを見るとこの子音は読めない
    /// (英単語の最後の子音) ので、読めない英字を含む区間と同じに扱う。
    /// </summary>
    private static bool EndsWithLoneSokuon(IReadOnlyList<CompositionUnit> units, int end) =>
        end >= 2 && end < units.Count && units[end - 1] is { Kana: "っ", Raw.Length: 1 } && units[end - 2].Kana == "ん";

    private static bool HasUnreadable(IReadOnlyList<CompositionUnit> units, int start, int end)
    {
        for (var k = start; k < end; k++)
        {
            if (units[k] is { Raw.Length: 1 } unit && unit.Kana == unit.Raw && char.IsAsciiLetter(unit.Raw[0]) && !IsLaughter(units, k)) return true;
        }
        return false;
    }

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

    private static string Kana(IReadOnlyList<CompositionUnit> units, int start, int end) =>
        string.Concat(Enumerable.Range(start, end - start).Select(k => units[k].Kana));

    private static string Raw(IReadOnlyList<CompositionUnit> units, int start, int end) =>
        string.Concat(Enumerable.Range(start, end - start).Select(k => units[k].Raw));
}
