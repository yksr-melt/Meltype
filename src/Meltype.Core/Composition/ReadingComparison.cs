// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 souta

using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// 日英の区切りを、入力全体を「日本語として読んだ場合」と「英語として読んだ場合」の点で比べて決める
/// (issue #838 の提案の実装)。
///
/// 今までの FindSpans は、区間ごとに「英語にしてよいか」を規則で決めていて、入力全体を日本語として読んだ場合と
/// 比べてはいない。そのため、ローマ字としても読める英単語 (image・pure・main dot …) は、前後に手がかりが無いと
/// 日本語に倒れていた。ここでは、入力全体を
///   日本語の語 (辞書にある)・日本語の語でないかな・知っている英単語・知らない英字の並び
/// に分ける分け方を全部比べ、点が一番低い (自然な) 分け方を選ぶ。
///
/// 点 (issue #838 の案と同じ):
///   - 日本語の語: 1 (1 文字の語は 1.5、助詞は 0.8)
///   - 日本語の語でないかな: 1 文字 3 (っ・ー・小書き文字は 0.5)
///   - 知っている英単語: 1
///   - 知らない英字の並び: 1 文字 1.5 (大文字は 1)
///   - 日本語と英語の切り替え: 1 (日本語側が助詞・する のときは 0)
///   - 英単語が続く (英字の並びを 2 語に分ける): 1
/// 同点なら日本語。大文字を含む区間は日本語にしない。
/// </summary>
public sealed partial class CompositionDetector
{
    /// <summary>この決め方を使うか (設定・環境変数 MELTYPE_READING で切り替える)。</summary>
    public Func<bool>? UseReadingComparison { get; set; }

    internal static readonly bool ReadingByEnvironment = Environment.GetEnvironmentVariable("MELTYPE_READING") == "1";

    private bool ReadingComparison => ReadingByEnvironment || UseReadingComparison?.Invoke() == true;

    // 打鍵のたびに同じ部分文字列 (print・te・en …) を計算し直さないよう、呼び出しをまたいで覚えておく。
    // 英単語の点は学習 (LanguageMemory) で変わるので、学習の版が変わったら捨てる。
    private readonly Dictionary<string, double> _readingEnglishCosts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (bool Usable, string Kana, ReadingKind Kind, double Cost)> _readingJapaneseTokens =
        new(StringComparer.Ordinal);
    private int _readingCacheVersion = -1;
    /// <summary>控えの件数の上限 (学習しない設定で長く使っても増え続けないように、超えたら捨てる)。</summary>
    private const int ReadingCacheLimit = 512;

    /// <summary>この決め方が有効か (区間の控えのキーに使う)。</summary>
    internal bool ReadingComparisonEnabled => ReadingComparison;

    /// <summary>
    /// 点数で区切り直す値打ちがあるか (入力のたびに DP を走らせないための安い判定)。
    /// 英語の区間が既にある (単位をまたぐ英単語を直せる) か、日本語の読みに英字が残っている (pりんてんv・どおm) ときだけ。
    /// </summary>
    private static bool ShouldTryReadingComparison(List<CompositionSegment> segments)
    {
        // 既に大半が英語なら、点数で直せるものは無い (長い英文を打つたびに DP を走らせない)
        var total = segments.Sum(segment => segment.Raw.Length);
        var english = segments.Where(segment => segment.IsEnglish).Sum(segment => segment.Raw.Length);
        if (total > 0 && english * 10 >= total * 8) return false;
        // 英語の区間がある (単位をまたぐ英単語を直せる) か、日本語の読みに英字が残っている (pりんてんv・どおm)
        return segments.Any(segment => segment.IsEnglish || segment.Kana.Any(char.IsAsciiLetter));
    }

    // 点の重み (issue #838 の案)。比べるときに環境変数で変えられるようにしておく。
    private static double ReadingWeight(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable("MELTYPE_READING_" + name),
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static readonly double JapaneseWordCost = ReadingWeight("WORD", 1.0);
    private static readonly double JapaneseSingleCharCost = ReadingWeight("CHAR", 1.5);
    private static readonly double JapaneseParticleCost = ReadingWeight("PARTICLE", 0.8);
    private static readonly double JapaneseKanaCost = ReadingWeight("KANA", 3.0);
    private static readonly double JapaneseSmallKanaCost = ReadingWeight("SMALLKANA", 0.5);
    private static readonly double EnglishWordCost = ReadingWeight("ENGLISH", 1.0);
    private static readonly double EnglishUnknownCost = ReadingWeight("UNKNOWN", 1.5);
    private static readonly double EnglishUpperCost = ReadingWeight("UPPER", 1.0);
    private static readonly double ReadingSwitchCost = ReadingWeight("SWITCH", 1.0);
    private static readonly double EnglishAdjacentCost = ReadingWeight("ADJACENT", 1.0);
    /// <summary>前が英語 (英文の続き) のときの英語の後押し。</summary>
    private static readonly double ReadingContextBonus = ReadingWeight("CONTEXT", 1.0);
    /// <summary>1 つの区間として見る英字の長さの上限 (長い英文を 1 語として扱わない)。</summary>
    private const int MaxEnglishSpan = 24;

    /// <summary>区切りの種類。点を比べるときの「直前が何だったか」。</summary>
    private enum ReadingKind
    {
        Start = 0,
        JapaneseWord = 1,
        JapaneseParticle = 2,
        English = 3,
    }

    /// <summary>かな 1 文字の点。っ・ー・小書き文字は、日本語の語でなくても自然なので安くする。</summary>
    private static double KanaCost(string kana)
    {
        var total = 0.0;
        foreach (var c in kana)
            total += c is 'っ' or 'ー' or 'ゃ' or 'ゅ' or 'ょ' or 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ' or 'ゎ'
                ? JapaneseSmallKanaCost
                : JapaneseKanaCost;
        return total;
    }

    /// <summary>英字の並びの点。知っている英単語なら 1、知らなければ 1 文字 1.5 (大文字は 1)。</summary>
    private double EnglishCost(string lower, string raw)
    {
        // 大文字小文字が混ざった語 (AIde・McAfe) は、小文字の語 (aide・cafe) の点にしない
        var mixedCase = raw != lower && !raw.All(c => !c.Equals(char.ToLowerInvariant(c))) && _proper.Canonical(lower) != raw;
        // 日本語由来で英語としても使われる語 (sushi・suzuki・anime) は、英語の根拠にしない (japanese.txt の語と同じ扱い)
        if (JapaneseOriginWords.Value.Contains(lower)) return EnglishUnknownCost * raw.Length;
        if (!mixedCase && IsKnownEnglishWord(lower))
        {
            // english-readable は「ローマ字として読めても日本語の語にならない」契約なので、少し強く英語にする
            if (ReadableEnglish.Value.ContainsWord(lower)) return EnglishWordCost - 0.5;
            // 同梱の辞書・固有名詞・-pedia・学習した語は 1 点。
            if (IsListedEnglishWord(lower) || _proper.Contains(lower) ||
                IsPediaEnglishWord(lower) || Memory?.Get(lower) is not null) return EnglishWordCost;
            // 複数形 (invites) だけが辞書にあるとき、語幹 (invite) も同じ扱いにする
            if (lower.Length >= 4 && lower.EndsWith('s') &&
                (IsListedEnglishWord(lower[..^1]) || _proper.Contains(lower[..^1]))) return EnglishWordCost;
            // スペルチェッカーだけが知っている語 (repo・make・thin) は、ローマ字としても読めるなら弱く扱う。
            // 日本語の語 (まけ・てぃん) とどちらにも読めるので、前後の文脈が無ければ日本語にする (issue #838 の案の重み)。
            if (_romaji.AnalyzeFragment(lower).IsValid) return EnglishUnknownCost * raw.Length;
            return EnglishWordCost;
        }
        var total = 0.0;
        for (var i = 0; i < raw.Length; i++)
            total += char.IsAsciiLetterUpper(raw[i]) ? EnglishUpperCost : EnglishUnknownCost;
        return total;
    }

    /// <summary>区切りを移るときの点。日本語側が助詞・する のときは、英語とつながって自然なので 0。</summary>
    private static double SwitchCost(ReadingKind from, ReadingKind to)
    {
        if (from == ReadingKind.Start || to == ReadingKind.Start) return 0;
        var fromEnglish = from == ReadingKind.English;
        var toEnglish = to == ReadingKind.English;
        if (!fromEnglish && !toEnglish) return 0;
        if (fromEnglish && toEnglish) return EnglishAdjacentCost;
        // 日本語 → 英語: 日本語側が助詞なら 0
        if (!fromEnglish && from == ReadingKind.JapaneseParticle) return 0;
        // 英語 → 日本語: 日本語側が助詞なら 0
        if (fromEnglish && to == ReadingKind.JapaneseParticle) return 0;
        return ReadingSwitchCost;
    }

    /// <summary>
    /// 入力全体を読んだ点数で区切る。入力が英字だけのときだけ使う (記号・数字は今までの決まりに任せる)。
    /// 使わないときは null。
    ///
    /// 単位 (キーを打ったまとまり) ではなく 1 文字ずつで区切る。単位は ん (nn) のように語の切れ目と
    /// 一致しないことがあり、main + に (mai|nn|i) を単位では表せないため。
    /// </summary>
    private List<CompositionSegment>? FindSpansReading(IReadOnlyList<CompositionUnit> units, string pending, DetectionLevel level, bool final, bool? precedingEnglish, bool? followingEnglish, bool englishSentence)
    {
        if (level == DetectionLevel.Manual) return null;
        if (units.Count == 0) return null;
        var letters = Raw(units, 0, units.Count) + pending;
        if (letters.Length < 2 || !letters.All(char.IsAsciiLetter)) return null;
        var memoryVersion = Memory?.Version ?? -1;
        if (memoryVersion != _readingCacheVersion)
        {
            _readingCacheVersion = memoryVersion;
            _readingEnglishCosts.Clear();
            _readingJapaneseTokens.Clear();
        }

        var length = letters.Length;
        // 4 文字以下で、ローマ字として最後まで読めて、英単語として知られていない綴りは日本語 (fa → ふぁ、je → じぇ、kwo → くぉ)
        if (length <= 4)
        {
            var wholeLower = letters.ToLowerInvariant();
            var wholeAnalysis = _romaji.AnalyzeFragment(wholeLower);
            if (wholeAnalysis.IsValid && wholeAnalysis.Partial.Length == 0 && wholeAnalysis.Kana.Length > 0 &&
                !IsListedEnglishWord(wholeLower) && !_proper.Contains(wholeLower) &&
                !ReadableEnglish.Value.ContainsWord(wholeLower) && !IsPediaEnglishWord(wholeLower) &&
                Memory?.Get(wholeLower) is null && !IsSpellWord(wholeLower))
                return [new CompositionSegment(false, wholeAnalysis.Kana, letters)];
        }
        // 前が英語 (英文の続き) なら、英語の語を優先する。前が日本語なら、日本語を優先する。
        var contextBonus = precedingEnglish switch { true => ReadingContextBonus, false => -ReadingContextBonus, _ => 0 };
        // 後ろが英語 (英文の続き) なら、末尾の語も英語にしやすい。日本語が続くなら日本語にしやすい。
        var endingBonus = englishSentence || followingEnglish == true ? ReadingContextBonus
            : followingEnglish == false ? -ReadingContextBonus : 0;
        var englishScores = new Dictionary<(int Start, int End), double>();
        var japaneseTokens = new Dictionary<(int Start, int End), (bool Usable, string Kana, ReadingKind Kind, double Cost)>();
        var best = new double[length + 1, 4];
        var from = new (int Position, ReadingKind Kind)[length + 1, 4];
        var kana = new string?[length + 1, 4];
        for (var i = 0; i <= length; i++)
            for (var k = 0; k < 4; k++)
            {
                best[i, k] = double.PositiveInfinity;
                from[i, k] = (-1, ReadingKind.Start);
            }
        best[0, (int)ReadingKind.Start] = 0;

        void Offer(int i, int kind, int j, ReadingKind next, double cost, string? tokenKana)
        {
            var candidate = best[i, kind] + cost;
            var e = (int)next;
            // 同点なら先に試したもの (日本語) を残す
            if (candidate >= best[j, e]) return;
            best[j, e] = candidate;
            from[j, e] = (i, (ReadingKind)kind);
            kana[j, e] = tokenKana;
        }

        for (var i = 0; i < length; i++)
        {
            for (var kind = 0; kind < 4; kind++)
            {
                if (double.IsPositiveInfinity(best[i, kind])) continue;
                for (var j = i + 1; j <= length; j++)
                {
                    var raw = letters[i..j];
                    var lower = raw.ToLowerInvariant();
                    // 大文字を含む区間は日本語にしない (英単語・固有名詞・略語)
                    if (!raw.Any(char.IsAsciiLetterUpper))
                    {
                        if (!japaneseTokens.TryGetValue((i, j), out var token))
                        {
                            if (_readingJapaneseTokens.TryGetValue(lower, out var cachedToken))
                            {
                                // 位置に依らない区間だけを覚えている (語末の ん は位置で変わるので入れない)
                                token = cachedToken;
                            }
                            else
                            {
                                // 語末の ん (zenzen・hon) は、ローマ字の解析では Partial が "n" になる。
                                // 1 つの日本語の語として読めるようにする (でないと zen が英単語として扱われる)。
                                var analysis = _romaji.AnalyzeFragment(lower);
                                var partialN = analysis.IsValid && analysis.Partial == "n" && analysis.Kana.Length > 0 &&
                                    (j == length || (letters[j] is not ('a' or 'i' or 'u' or 'e' or 'o' or 'y') &&
                                                     _romaji.Analyze(letters[j..]).IsValid));
                                token = (false, "", ReadingKind.JapaneseWord, 0);
                                if (analysis.IsValid && analysis.Kana.Length > 0 && (analysis.Partial.Length == 0 || partialN))
                                {
                                    var fullKana = partialN ? analysis.Kana + "ん" : analysis.Kana;
                                    var isParticle = Meltype.Detection.DictionaryDetector.StartsWithParticle(lower) == lower;
                                    var isSuru = IsSuruForm(fullKana);
                                    // 日本語の語か: ローマ字見出しの辞書 (japanese.txt) と、よく使う語の読み (readings.txt、約 2 万語)
                                    var isWord = _japanese.Words.ContainsWord(lower) || CommonReadings.Words.Contains(fullKana);
                                    var next = isParticle || isSuru ? ReadingKind.JapaneseParticle : ReadingKind.JapaneseWord;
                                    var cost = isParticle ? JapaneseParticleCost
                                        : isSuru && fullKana.Length <= 4 ? JapaneseParticleCost
                                        : isWord ? (fullKana.Length == 1 ? JapaneseSingleCharCost : JapaneseWordCost)
                                        : KanaCost(fullKana);
                                    // 末尾の ん は表示側 (RenderSegments) が付けるので、語の読みからは外す
                                    token = (true, partialN && j == length ? analysis.Kana : fullKana, next, cost);
                                    // 次の打鍵でも使い回せるのは、位置に依らない区間だけ
                                    if (!partialN)
                                    {
                                        _readingJapaneseTokens[lower] = token;
                                        if (_readingJapaneseTokens.Count > ReadingCacheLimit) _readingJapaneseTokens.Clear();
                                    }
                                }
                            }
                            japaneseTokens[(i, j)] = token;
                        }
                        if (token.Usable)
                            Offer(i, kind, j, token.Kind, token.Cost + SwitchCost((ReadingKind)kind, token.Kind), token.Kana);
                    }
                    // 長すぎる英字の並びは 1 語として扱わない (長い英文で候補が増えすぎるのを防ぐ)
                    if (j - i > MaxEnglishSpan) continue;
                    if (!englishScores.TryGetValue((i, j), out var englishBase))
                    {
                        if (!_readingEnglishCosts.TryGetValue(lower, out englishBase))
                            _readingEnglishCosts[lower] = englishBase = EnglishCost(lower, raw);
                        if (_readingEnglishCosts.Count > ReadingCacheLimit) _readingEnglishCosts.Clear();
                        englishScores[(i, j)] = englishBase;
                    }
                    var englishCost = englishBase + SwitchCost((ReadingKind)kind, ReadingKind.English)
                        - (i == 0 ? contextBonus : 0) - (j == length ? endingBonus : 0);
                    Offer(i, kind, j, ReadingKind.English, englishCost, null);
                }
            }
        }

        var endKind = -1;
        for (var kind = 0; kind < 4; kind++)
            if (!double.IsPositiveInfinity(best[length, kind]) && (endKind < 0 || best[length, kind] < best[length, endKind])) endKind = kind;
        if (endKind < 0) return null;

        // 区間を組み立てる (後ろから辿る)
        var tokens = new List<(bool English, string Kana, string Raw)>();
        var position = length;
        var current = endKind;
        while (position > 0)
        {
            var (previousPosition, previousKind) = from[position, current];
            if (previousPosition < 0) return null;
            var tokenRaw = letters[previousPosition..position];
            var isEnglish = current == (int)ReadingKind.English;
            tokens.Add((isEnglish, isEnglish ? "" : kana[position, current] ?? tokenRaw, tokenRaw));
            position = previousPosition;
            current = (int)previousKind;
        }
        tokens.Reverse();
        // 結合する前に、頼りにならない英語の区間が無いか確かめる (結合すると daneww のように 1 語に見えてしまう)
        foreach (var token in tokens)
        {
            if (!token.English) continue;
            var tokenLower = token.Raw.ToLowerInvariant();
            if (token.Raw.Length == 1 && tokenLower is not ("w" or "a" or "i" or "u" or "r")) return null;
            if (token.Raw.Any(char.IsAsciiLetterUpper) && !token.Raw.All(char.IsAsciiLetterUpper) &&
                !IsListedEnglishWord(tokenLower) && !_proper.Contains(tokenLower)) return null;
        }

        // 日本語どうし・英語どうしは 1 つの区間にする
        var segments = new List<CompositionSegment>();
        foreach (var token in tokens)
        {
            if (segments.Count > 0 && segments[^1].IsEnglish == token.English)
                segments[^1] = new CompositionSegment(token.English,
                    segments[^1].Kana + token.Kana, segments[^1].Raw + token.Raw);
            else
                segments.Add(new CompositionSegment(token.English, token.Kana, token.Raw));
        }
        // 末尾の打ちかけの子音 (sumimasen の n) は、表示側 (RenderSegments) が最後の日本語の区間に付ける。
        // ここでも持っていると「すみませんん」になるので、日本語の区間の読みからは外す。
        if (pending.Length > 0 && segments.Count > 0 && !segments[^1].IsEnglish)
        {
            var pendingKana = _romaji.ConvertLenient(pending.ToLowerInvariant(), final);
            if (pendingKana.Length > 0 && segments[^1].Kana.EndsWith(pendingKana, StringComparison.Ordinal))
                segments[^1] = segments[^1] with { Kana = segments[^1].Kana[..^pendingKana.Length] };
        }
        if (segments.Count == 0) return null;
        // 頼りにならない英語の区間ができていたら使わない:
        //   - 小文字 1 文字 (kiyagattaw の g、cyiisai の c)。w などの笑いだけは残す
        //   - 大文字を含むのに、同梱の辞書の語でも固有名詞でもない (Tokyo を Tok + よ に割らない)
        foreach (var segment in segments)
        {
            if (!segment.IsEnglish) continue;
            var raw = segment.Raw;
            var lower = raw.ToLowerInvariant();
            // 1 文字の英字は、英文の中の a / i / u / r と笑いの w だけ (Hello の H を ひらがなから切り離さない)
            if (raw.Length == 1 && lower is not ("w" or "a" or "i" or "u" or "r")) return null;
            if (raw.Any(char.IsAsciiLetterUpper) && !raw.All(char.IsAsciiLetterUpper) &&
                !IsListedEnglishWord(lower) && !_proper.Contains(lower)) return null;
        }
        return segments;
    }

    /// <summary>区間を表示したときに英字のまま残る文字の数。</summary>
    private static int RenderedLetters(List<CompositionSegment> segments) =>
        segments.Sum(segment => (segment.IsEnglish ? segment.Raw : segment.Kana).Count(char.IsAsciiLetter));

    /// <summary>
    /// この決め方の結果が、今までの決まりの結果より良いか。
    /// 次のどちらかのときだけ使う (既に正しい区切りを崩さないため):
    ///   - 今までの決まりが、日本語の区間の中に英字を残している (pりんてんv・どおm のように、明らかに崩れている)
    ///   - 新しい区切りが、キーの単位 (1 かな) の途中で分かれている (main + に が nn の途中)。今までの決まりでは表せない。
    /// </summary>
    private bool ReadingComparisonIsBetter(List<CompositionSegment> reading, List<CompositionSegment> baseline,
        IReadOnlyList<CompositionUnit> units, string pending, DetectionLevel level)
    {
        // 慎重・手動では、今までの決まりの判断を変えない (設定の意味を保つ)
        if (level is DetectionLevel.Conservative or DetectionLevel.Manual) return false;
        // 明示的に日本語として覚えた語 (ユーザーが英字をかなに直した) は、点数で英語にしない
        foreach (var segment in reading.Where(segment => segment.IsEnglish))
            if (Memory?.Get(segment.Raw.ToLowerInvariant()) == false) return false;
        if (reading.Count == 0 || !reading.Any(segment => segment.IsEnglish)) return false;
        // 今までの決まりより英字をかなに変える区切りは採らない
        // (trackutalkko を trackうtalkこ にしない。英字のまま残る文字の数で比べる)
        if (RenderedLetters(reading) < RenderedLetters(baseline)) return false;
        // 今までの決まりが、日本語の区間の中に英字を残している = 明らかに崩れている。
        // ただし、それだけで新しい決め方を使うと、大文字で始まる語 (Tokyo) のような
        // わざと英字を残す区切りまで崩すので、入力全体が 1 つの英単語になる場合だけにする。
        var wholeEnglish = reading.All(segment => segment.IsEnglish);
        if (wholeEnglish && reading[0].Raw.Length >= 5 &&
            baseline.Any(segment => !segment.IsEnglish && segment.Kana.Any(char.IsAsciiLetter)))
        {
            // 既定で既に大半が英語なら、その区切り (rds と redshift) のほうが自然なので触らない
            var total = baseline.Sum(segment => segment.Raw.Length);
            var english = baseline.Where(segment => segment.IsEnglish).Sum(segment => segment.Raw.Length);
            if (total == 0 || english * 10 < total * 8)
            {
                // 入力の始まりと終わりが「かなとして読めない英字」のときだけ、全体を英語にする。
                // 始まりがかな (aprintenv) や、終わりがかな (printenva) なら、その日本語を英語に吸収しない。
                var japanese = baseline.Where(segment => !segment.IsEnglish).Select(segment => segment.Kana).Where(kana => kana.Length > 0).ToList();
                // 末尾の打ちかけの子音 (printenv の v) は区間に入らず pending に残るので、そちらも見る
                var lastChar = pending.Length > 0 ? pending[^1] : japanese.Count > 0 ? japanese[^1][^1] : '\0';
                if (japanese.Count > 0 && char.IsAsciiLetter(japanese[0][0]) && char.IsAsciiLetter(lastChar)) return true;
            }
        }
        // キーの単位をまたいで分かれている = 今までの決まりでは表せない区切り
        var boundaries = new HashSet<int> { 0 };
        var offset = 0;
        foreach (var unit in units)
        {
            offset += unit.Raw.Length;
            boundaries.Add(offset);
        }
        if (pending.Length > 0) boundaries.Add(offset + pending.Length);
        var consumed = 0;
        foreach (var segment in reading)
        {
            consumed += segment.Raw.Length;
            if (boundaries.Contains(consumed)) continue;
            // 単位をまたいでいる英字の区間が、知っている英単語 (4 文字以上) で、
            // 既定ではその位置が「日本語の語になっていないかな」のときだけ、直す値打ちがある。
            var lower = segment.Raw.ToLowerInvariant();
            if (!segment.IsEnglish || lower.Length < 4 || !IsKnownEnglishWord(lower)) continue;
            var covered = 0;
            foreach (var japanese in baseline)
            {
                if (covered >= consumed - segment.Raw.Length && covered < consumed && !japanese.IsEnglish &&
                    !CommonReadings.Words.Contains(japanese.Kana)) return true;
                covered += japanese.Raw.Length;
            }
        }
        return false;
    }
}
