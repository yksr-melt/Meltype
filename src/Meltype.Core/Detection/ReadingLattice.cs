// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Libraries.Chars;

namespace Meltype.Detection;

/// <summary>
/// 日英の区切り。日本語の語・英単語・未知語をノードにしたラティスを作り、Viterbi で最小コスト経路を選ぶ。
/// 日本語の語の生起コストは RomajiTypoCorrector.WordCost と共有する。
///
/// コストで比べない決まり:
/// - 大文字を含む区間は日本語のノードにしない。
/// - 綴りを名指しする条件は持たない。
/// - 知っている英単語は、英単語の書き方の大文字・小文字 (its・Its・ITS・GitHub) のときだけノードにする (iTs は its にしない)。
/// - 1 文字の英単語 (a・i・u・r) は、前後を英字以外で区切ったときだけノードにする。
/// - 自動判定を使わないなら、Shift を押して打った大文字で始まる語だけを英語のノードにする。
/// - 記号・数字の決まり (ドメイン・拡張子・ユーザー名・\ + 英字・- の入った語・数字を挟んだ略語・英文の中の記号) は、コストで比べずにその区間を英語にする。
/// - 技術用語の綴りのすぐ後ろが する の活用 (share|して・make|します) なら、その綴りを日本語の語 (しゃれ・まけ) として読まない (しゃれする・まけします は日本語でない)。ただし一段動詞の使役形 (make|させた) は日本語のまま。
/// - ローマ字入力では 1 文字ずつを区切りの位置にする (単位の途中で区切れる: get|Element、medal|s|ha)。かな入力では打った単位の境目だけ。
/// </summary>
internal sealed class ReadingLattice(WordJudge words, LinkWeights weights)
{
    // ---- 日本語のノードの生起コスト ----
    // 1 文字の語 1.5・付属語 0.8 は RomajiTypoCorrector.WordCost
    // 日本語の語 (readings.txt・readings-extra.txt と活用した形)・同梱の語句の読み (phrases.txt の どしたの)・
    // ユーザーがかなに直して覚えた語・english.txt の日本語由来の語 (sushi) をローマ字で読んだかな
    private const double JapaneseWordCost = 1;
    // katakana.txt の語 (ほすてぃんぐ) と brands.txt の読み (ぎっとはぶ)。katakana.txt は英単語をカタカナにした語 (アマゾン) が多いので、同じ綴りの英単語 (amazon) に譲る
    private const double KatakanaWordCost = 1.1;
    // readings-rare.txt の語 (JMdict のよく使う語でない語の 3 文字以上の読み: ろんぱ・かんれんづけ・くるまどめ)。
    // 英単語への切り替え (関連|duke = 1 + 1 + 1) より軽く、よく使う語どうしの区切りを割らない重みにする。
    // 1.5 では きょうはい|まげ が きょうは|image より軽くなり、3 では 関連づけ が 関連|duke と同じ重みになる
    private const double RareWordCost = 2.5;
    // readings-rare.txt の 4 文字以下の読み (活用形も含む) は、英単語の一部に重なりやすい (ぜん|ぶち|めにする)。
    // 日本語の前後に打った英単語 (ぜんぶ|time|にする) より軽くせず、未知音 + 英語 (うち|dome = 3) よりは軽くする。
    private const double ShortRareWordCost = 2.9;
    // 語にならないかな 1 音、日本語の中に残る読めない英字 1 文字
    internal const double UnknownKanaCost = 3;
    // 語にならない っ・ー、x で打った小書きの母音が語の 1 音目 (だめ|ぇ)
    internal const double LightKanaCost = 0.5;

    // ---- 英語のノードの生起コスト ----
    // 知っている英単語 (english.txt・固有名詞・english-words.txt)
    private const double KnownEnglishCost = 1;
    // english-readable.txt の語 (open・feature): ローマ字としても読めるが、英語で打つことの多い語
    private const double ReadableEnglishCost = 0.7;
    // ユーザーが英字に直して覚えた語。同じ読みの日本語の語 (sushi = すし、重み 1) より軽くする
    private const double LearnedEnglishCost = 0.7;
    // 小文字の 2 文字以下の知っている英単語 (be・pa・on) に足す。ローマ字の 1 音と同じ綴りなので、辞書にない日本語の語 (すべ・ろんぱ) が
    // 語にならないかな + 短い英単語 + 語にならないかな (す|be|を・ろん|pa) に割れていた。
    // 大文字を含む語 (AI・Go) は日本語のノードと取り合わないので足さない。覚えた語にも足さない
    private const double ShortEnglishCost = 1;
    // 知らない英字 (ローマ字として読めない所: pwd) 1 文字
    private const double UnknownLetterCost = 1.5;
    // 入力の最後の、知っている英単語の頭 (insta)。英単語 + 日本語 1 音 (ins + た) より軽く、日本語の語より重い
    private const double EnglishPrefixCost = 2.5;
    // ローマ字として読める知らない語 + . + ドメイン・拡張子 (hoge.io・llama.cpp) をまとめた区間は、知っている英単語の重み + . の前の 1 文字ごとにこの重み
    // (前が長いほど、句点の後ろの日本語 junbigadekita.|deha と読む方が自然になる)
    private const double ReadableDomainLetterCost = 0.25;
    // 知らない語の大文字 1 文字
    private const double UnknownUpperCost = 1;
    // 大文字で始まり 2 文字目が小文字の 3 文字以上の知らない語 (Tanaka・McKinsey) の小文字 1 文字
    private const double NameLetterCost = 0.5;
    // かな入力の 4 文字以上の知っている英単語に足す: 英字のキーで打ったかな (さっき + かく|ちか = that) が日本語の語に読める分を打ち消す。
    // 3 文字以下 (eh・at・id) は日本語の文の一部が英単語になりやすいので付けない
    private const double KanaInputEnglishBias = -1;
    // 変換の候補に出す英語寄りの区切りを作るとき英語のノードごとに足す: 英単語 + 切り替え + 付属語 (midi + か = 2.8) が日本語の語 1 つ (みぢか = 1) より軽くなる
    internal const double LeaningEnglishBias = -2;

    // ---- 連接コスト (重みは LinkWeights) ----
    // 日本語の中の c 行の綴り (ca・ci・cu・ce・co。ch は除く) 1 つ: 日本語を打つときは k を使う
    private const double CRowCost = 0.5;
    // 同じコストなら日本語にする
    private const double EnglishTieBreak = 1e-6;
    // 1 つの候補として見る長さの上限 (ローマ字の文字数)
    private const int MaxSpan = 48;

    // ---- 変換の候補に出す区切りの別案 ----
    // 最も軽い案からこれより重い案は出さない
    private const double AlternativeMargin = 1;
    // (位置, 状態) ごとに残す道の数。日本語の語の区切りだけが違う道 (ぼ|っと・ぼっと) が同じ案になるので、出す案の数より多く残す
    private const int PathsPerState = 8;

    private enum Side : byte { None, Japanese, English, Sentence }

    /// <summary>区切りの 1 つの候補。Kana は日本語の候補の読み。Letters は英字を含む日本語の候補か。Neutral は記号・数字・空白 (言語を切り替えない)。</summary>
    private readonly record struct LatticeEdge(int Start, int End, bool English, string Kana, bool Letters, bool Particle, bool Neutral = false, bool Name = false);

    private sealed record LatticeNode(double Cost, LatticeNode? Previous, LatticeEdge Edge);

    /// <summary>
    /// 状態: 直前の言語 (Side)・直前の日本語の候補が名詞に付く付属語か・ここまで英字の日本語が無いか・直前の候補が英語か・直前の候補が っ で終わるか・
    /// 前の英語の文脈 (hello ) からの切り替えをまだ数えていないか。記号・数字の候補は直前の言語を変えない。
    /// </summary>
    private readonly record struct LatticeState(Side Side, bool Particle, bool AllEnglish, bool PreviousEnglish, bool Sokuon = false, bool ContextSwitch = false)
    {
        public const int Count = 128;
        public int Key => (int)Side | (Particle ? 4 : 0) | (AllEnglish ? 8 : 0) | (PreviousEnglish ? 16 : 0) | (Sokuon ? 32 : 0) | (ContextSwitch ? 64 : 0);
        public static LatticeState FromKey(int key) => new((Side)(key & 3), (key & 4) != 0, (key & 8) != 0, (key & 16) != 0, (key & 32) != 0, (key & 64) != 0);
        public bool IsEnglish => Side is Side.English or Side.Sentence;
    }

    /// <summary>コストで比べずに、その区間を英語にする記号・数字の決まり (このクラスの説明の「コストで比べない決まり」)。</summary>
    private enum LexemeRule { None, Dotfile, UserName, Escape, HyphenatedWord, Numeronym, ProperNounWithDigits, VersionSuffix }

    /// <summary>
    /// 1 つの位置から始まる字句。Rule が None でなければ、End (打った文字の位置) までを英語にする。
    /// DomainEnd はローマ字として読める語 + ドメイン・拡張子の終わり (重みで比べる)、Technical は技術用語の綴り + する の活用の頭。
    /// </summary>
    private readonly record struct Lexeme(LexemeRule Rule, int End, int DomainEnd, bool Technical);

    /// <summary>辺をつなぐときに数える特徴の、1 回ごとの重み。</summary>
    internal LinkWeights Weights { get; } = weights;
    private double LinkCost(ReadOnlySpan<int> counts)
    {
        var cost = 0.0;
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] != 0) cost += counts[i] * Weights[(LinkFeature)i];
        }
        return cost;
    }

    /// <summary>調べる用 (--segments): 区間分けのたびに、最後の状態ごとの一番軽い道と、候補ごとの重み (+) を 1 行ずつ渡す。* が選んだ道。</summary>
    internal Action<string>? LatticeTrace { get; set; }

    /// <summary>
    /// 区切りの上位 count 案を、軽い順に返す。区間の並びが同じ道 (日本語の語の区切りだけが違う道) は 1 案にまとめる。
    /// count が 2 以上なら、(位置, 状態) ごとに軽い道を <see cref="PathsPerState"/> 個ずつ残す (k-best Viterbi)。最も軽い案から <see cref="AlternativeMargin"/> より重い案は返さない。
    /// </summary>
    /// <param name="reasons">渡したら、選んだ道と、言語の割り当てが違う一番軽い道 (次点) を書く関数を足す (確定のログ用。issue #1644)。
    /// キーを打つたびに呼ばれるので、次点を探して文字列にするのは、ログに書くときだけにする。</param>
    internal List<List<CompositionSegment>> FindSpanAlternatives(Prepared prepared, LanguageLearning learning, IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish, bool? followingEnglish,
        bool manual, bool englishSentence, bool kanaInput, bool punctuateAfterEnglish, double englishBias, int count, List<Func<string>>? reasons = null)
    {
        var text = prepared.Raw + pending;
        var length = text.Length;
        if (length == 0) return [[new CompositionSegment(false, "", "")]];
        var keep = count > 1 ? PathsPerState : 1;
        var unitsEnd = prepared.Raw.Length;
        // 文字の位置 → その位置から始まる単位 (単位の境目でなければ -1)
        var unitAt = new int[length + 1];
        Array.Fill(unitAt, -1);
        for (var k = 0; k < units.Count; k++) unitAt[prepared.RawOffsets[k]] = k;
        unitAt[unitsEnd] = units.Count;
        // 文字の位置 → その文字を含む単位
        var unitOf = new int[unitsEnd];
        for (var k = 0; k < units.Count; k++)
            for (var p = prepared.RawOffsets[k]; p < prepared.RawOffsets[k + 1]; p++) unitOf[p] = k;
        bool IsBoundary(int position) => !kanaInput || unitAt[position] >= 0 || position == length;

        // 知らない英字として英語にできる文字 (ローマ字として読めない英字・大文字・打ち終わって読めない入力途中の子音) の数の累積
        var unknown = new int[length + 1];
        // 入力途中の子音は、入力中も Enter のときと同じ読みで重みを付ける (表示と確定の区切りを揃える)
        var finalPending = words.Romaji.ConvertLenient(pending.ToLowerInvariant(), final: true);
        for (var p = 0; p < length; p++)
        {
            var c = text[p];
            var unreadable = char.IsAsciiLetterUpper(c) ||
                (p < unitsEnd ? unitAt[p] is var k and >= 0 && prepared.Unreadable[k + 1] > prepared.Unreadable[k] : finalPending.Any(char.IsAsciiLetter));
            unknown[p + 1] = unknown[p] + (char.IsAsciiLetter(c) && unreadable ? 1 : 0);
        }

        // [位置, 状態, 軽い順]
        var best = new LatticeNode?[length + 1, LatticeState.Count, keep];
        var start = new LatticeState(precedingEnglish switch { true => englishSentence ? Side.Sentence : Side.English, false => Side.Japanese, null => Side.None },
            false, true, precedingEnglish == true, ContextSwitch: precedingEnglish == true);
        best[0, start.Key, 0] = new LatticeNode(0, null, default);

        void Offer(int position, LatticeState state, LatticeNode node)
        {
            var key = state.Key;
            var at = keep;
            while (at > 0 && (best[position, key, at - 1] is not { } worse || node.Cost < worse.Cost)) at--;
            if (at == keep) return;
            for (var k = keep - 1; k > at; k--) best[position, key, k] = best[position, key, k - 1];
            best[position, key, at] = node;
        }

        // [start, end) の中の、ltu・xtu・ltsu・xtsu で打った っ のうち、後ろに入力が続くものの数
        int SpelledSokuons(int start, int end)
        {
            var count = 0;
            for (var i = start; i + 3 <= end; i++)
            {
                if (char.ToLowerInvariant(text[i]) is not ('l' or 'x') || char.ToLowerInvariant(text[i + 1]) != 't') continue;
                var tail = char.ToLowerInvariant(text[i + 2]) == 's' ? i + 4 : i + 3;
                if (tail <= end && char.ToLowerInvariant(text[tail - 1]) == 'u' && tail < length) count++;
            }
            return count;
        }

        // position の英字が、次の文字と同じ子音を重ねたもの (tt の t = っ)
        bool DoubledAt(int position) => position + 1 < length && char.IsAsciiLetter(text[position]) && char.ToLowerInvariant(text[position + 1]) == char.ToLowerInvariant(text[position]);

        // 辺をつなぐときに当てはまる特徴を数える。重みは LinkWeight の表だけが持つ (ここでは値を足さない)
        void CountLinks(LatticeNode from, LatticeState state, LatticeEdge edge, Span<int> counts)
        {
            var japanese = !edge.English && !edge.Neutral;
            if (japanese)
            {
                // 子音を重ねて打った っ は、前後をかなに挟まれているときだけ軽い (excelとっpt・cwっで にしない。ltu・xtu で打った っ と、Goって の って は除く)
                if (state.PreviousEnglish && edge.Start > 0 && char.IsAsciiLetterLower(text[edge.Start - 1]) && edge.Kana is [var head, ..] &&
                    ("ぁぃぅぇぉ".Contains(head) || "あいうえお".Contains(head) && !"aeiou".Contains(text[edge.Start - 1]))) counts[(int)LinkFeature.VowelAfterEnglish]++;
                if (!edge.Particle && from.Edge.Name && from.Edge.End == edge.Start && edge.Start < length && char.IsAsciiLetterLower(text[edge.Start])) counts[(int)LinkFeature.NameSplit]++;
                counts[(int)LinkFeature.SpelledSokuon] += SpelledSokuons(edge.Start, edge.End);
                if (edge.Kana == "っ" && DoubledAt(edge.Start) && state.Side != Side.Japanese) counts[(int)LinkFeature.DanglingSokuon]++;
                // 語の終わりの っ (ぶっ・まっ) の後ろに、和語では っ の後ろに来ない音 (濁音・な行・あ行など) で始まる付属語が続く (ぶっ|が):
                // 英単語の最後の子音を重ねて読んだ切り間違い (bug|が)。付属語でない語 (せっ|ど・ぶっ|ぢずむ) は、読みが辞書に無い外来語の
                // 途中のことがあるので数えない。付属語の だ が続く外来語 (ぶっだ) は、読みを辞書に入れて 1 語にする
                if (edge.Particle && from.Edge is { English: false, Neutral: false, Kana: [_, .., 'っ'] } && from.Edge.End == edge.Start && edge.Kana is [var after, ..] &&
                    NoSokuonBefore.Contains(after)) counts[(int)LinkFeature.DanglingSokuon]++;
            }
            else if (state.Sokuon) counts[(int)LinkFeature.DanglingSokuon]++;

            var betweenLetters = edge.Start > 0 && char.IsAsciiLetter(text[edge.Start - 1]) && char.IsAsciiLetter(text[edge.Start]);
            if (edge.English)
            {
                if (state.Side == Side.Japanese && betweenLetters && IsSandwiched(from)) counts[(int)LinkFeature.Sandwich]++;
                if (state.Side == Side.Japanese && !state.Particle) counts[(int)LinkFeature.Switch]++;
                if (state.IsEnglish && betweenLetters) counts[(int)LinkFeature.AdjacentEnglish]++;
            }
            else if (japanese)
            {
                if (state.Side == Side.Sentence) counts[(int)LinkFeature.SentenceSwitch]++;
                // 3 文字以上の英単語の後ろの、助詞と同じかなで始まる語 (java|のこ・java|でかく) も、助詞からの切り替えと同じ重みにする
                // (日本語の道だけが の|こ を のこ にまとめられて、英語が負ける。so|ですか・ma|のなか のような 2 文字の語は除く)
                else if (state.IsEnglish && !edge.Particle &&
                         !(edge.Kana.Length > 1 && NounParticles.Contains(edge.Kana[..1]) && from.Edge.English && from.Edge.End - from.Edge.Start >= 3)) counts[(int)LinkFeature.Switch]++;
                // 前の英語の文脈からの切り替えは、英語の語から始めた道も最初の日本語で数える (hello + sushi|が を すしが と同じ重さの差で比べる)
                if (state.ContextSwitch && edge.Start > 0) counts[(int)(englishSentence ? LinkFeature.SentenceSwitch : LinkFeature.Switch)]++;
            }
        }

        // 英字 → 日本語 → 英字 と、英字のあいだで区切りなしに戻る (procras|ち|cle)。あいだが付属語を含むとき (excel|と|ppt) は除く
        bool IsSandwiched(LatticeNode from)
        {
            var run = from;
            var particle = run.Edge.Particle;
            while (run.Previous is { } previous && !run.Edge.English && !run.Edge.Neutral)
            {
                if (previous.Edge.English || previous.Edge.Neutral) break;
                run = previous;
                particle |= run.Edge.Particle;
            }
            return !particle && run.Previous is { Edge.English: true } before && before.Edge.End == run.Edge.Start && run.Edge.Start > 0 && char.IsAsciiLetter(text[run.Edge.Start - 1]);
        }

        // 辺を通った後の状態 (重みにはかかわらない)。記号・数字の辺は直前の言語を変えない
        LatticeState Next(LatticeState state, LatticeEdge edge)
        {
            var japanese = !edge.English && !edge.Neutral;
            var next = edge.English
                // 英文の続きの強さは、最初の英語の語までで使い終わる (英文の中の記号では使い終わらない)
                ? new LatticeState(state.Side == Side.Sentence && !char.IsAsciiLetter(text[edge.Start]) ? Side.Sentence : Side.English, false, state.AllEnglish, true, ContextSwitch: state.ContextSwitch)
                : edge.Neutral ? state with { PreviousEnglish = false }
                : new LatticeState(Side.Japanese, edge.Particle && !NeedsInflection(edge.Kana), state.AllEnglish && !edge.Letters, false);
            return next with { Sokuon = japanese && edge.Kana.EndsWith('っ') && DoubledAt(edge.End - 1) };
        }

        void OfferEdge(LatticeNode from, LatticeState state, LatticeEdge edge, double cost)
        {
            // 活用する語の後ろにしか付かない 1 文字の付属語 (う・れ) は、英単語の後ろと、入力の頭で後ろに続きがあるときは、語にならないかなとして読む。
            // 付属語の軽い重み (0.8) のままだと、ローマ字の途中で英単語を切り出した残り (get|う・hit|う・れ|pro) が、語にならないかな 2 音 (げ|つ) より軽くなる。
            // 付属語だけを打った入力 (re) は、前に確定した語の続きなので付属語のまま
            if (!edge.English && !edge.Neutral && (state.IsEnglish || state.Side == Side.None && edge.End < length) && VerbAuxiliaries.Contains(edge.Kana)) cost = UnknownKanaCost;
            if (edge.English) cost += EnglishTieBreak + englishBias;
            Span<int> counts = stackalloc int[LinkWeights.Count];
            CountLinks(from, state, edge, counts);
            Offer(edge.End, Next(state, edge), new LatticeNode(from.Cost + cost + LinkCost(counts), from, edge));
        }

        var englishLevel = !manual;

        // 字句の段階: 記号・数字の決まりのうち状態によらないもの (どの道から来ても同じ) を、Viterbi の前に位置ごとに 1 回だけ決める
        Lexeme Lex(int p)
        {
            var unit = p < unitsEnd ? unitAt[p] : -1;
            // 技術用語の綴り + する の活用 (share|して・make|します): 日本語の語 (しゃれ・まけ) には する を付けられないので、日本語の候補を出さない
            var technical = !kanaInput && englishLevel && IsTechnicalWordBeforeSuru(text, p);
            // ローマ字として読める語 + . + ドメイン・拡張子 (llama.cpp・hoge.io): 句点の後ろの日本語 (ryoukaidesu.|deha) と重みで比べる
            var domain = unit >= 0 && !kanaInput && englishLevel ? ReadableStemDotSuffixEnd(prepared, learning, units, unit, pending) : -1;
            if (unit < 0 || kanaInput) return new(LexemeRule.None, -1, domain, technical);
            // 上から順に、先に当てはまった決まりを使う
            if (LeadingDotfileEnd(prepared, learning, units, unit, pending, manual) is >= 0 and var dotfile) return new(LexemeRule.Dotfile, RawEnd(dotfile), domain, technical);
            if (UserNameEnd(prepared, units, unit, pending) is >= 0 and var user) return new(LexemeRule.UserName, RawEnd(user), domain, technical);
            if (!englishLevel) return new(LexemeRule.None, -1, domain, technical);
            if (EscapeEnd(learning, text, p) is >= 0 and var escape) return new(LexemeRule.Escape, escape, domain, technical);
            if (HyphenatedWordEnd(prepared, learning, units, unit, pending) is >= 0 and var hyphenated) return new(LexemeRule.HyphenatedWord, RawEnd(hyphenated), domain, technical);
            if (NumeronymEnd(prepared, units, unit, pending) is >= 0 and var numeronym) return new(LexemeRule.Numeronym, RawEnd(numeronym), domain, technical);
            if (ProperNounWithDigitsEnd(prepared, units, unit, pending) is >= 0 and var proper) return new(LexemeRule.ProperNounWithDigits, RawEnd(proper), domain, technical);
            if (VersionSuffixEnd(units, unit, pending) is >= 0 and var version) return new(LexemeRule.VersionSuffix, RawEnd(version), domain, technical);
            return new(LexemeRule.None, -1, domain, technical);
        }
        // 単位の番号 end までの、打った文字の位置 (最後の単位までなら、入力途中の子音も含める)
        int RawEnd(int end) => end == units.Count ? length : prepared.RawOffsets[end];
        var lexemes = new Lexeme[length];
        for (var p = 0; p < length; p++)
            if (IsBoundary(p)) lexemes[p] = Lex(p);

        for (var p = 0; p < length; p++)
        {
            if (!IsBoundary(p)) continue;
            var japanese = JapaneseEdges(prepared, units, text, p, unitOf, unitsEnd, pending, kanaInput);
            var english = EnglishEdges(prepared, learning, text, p, unknown, kanaInput, manual, IsBoundary, precedingEnglish == true);
            var unit = p < unitsEnd ? unitAt[p] : -1;
            // 記号・数字・空白・z の記号 (zh = ←) の単位: 言語を切り替えない
            LatticeEdge? neutral = unit >= 0 && !IsKanaUnit(units[unit]) && !IsLetterUnit(units[unit])
                ? new LatticeEdge(p, prepared.RawOffsets[unit + 1], false, units[unit].Kana, false, false, true)
                : null;
            var (_, forced, domain, technical) = lexemes[p];

            for (var slot = 0; slot < LatticeState.Count * keep; slot++)
            {
                var key = slot / keep;
                if (best[p, key, slot % keep] is not { } node) continue;
                var state = LatticeState.FromKey(key);
                var to = forced;
                // ドメイン・拡張子の . の後ろ (tetr.io、MeltypeTip.dll)
                if (to < 0 && unit > 0 && englishLevel && !kanaInput && units[unit - 1].Raw == "." && state.PreviousEnglish &&
                    DotSuffixEnd(prepared, learning, units, unit, pending) is var suffix && suffix > unit) to = RawEnd(suffix);
                if (to > p)
                {
                    OfferEdge(node, state, new LatticeEdge(p, to, true, "", false, false), KnownEnglishCost);
                    continue;
                }
                if (technical)
                {
                    // 日本語の候補だけを出さない。英語の候補はそのまま使う (open は english-readable.txt の重み 0.7 のまま)
                    foreach (var (edge, cost) in english) OfferEdge(node, state, edge, cost);
                    continue;
                }
                if (domain > unit)
                    OfferEdge(node, state, new LatticeEdge(p, domain == units.Count ? length : prepared.RawOffsets[domain], true, "", false, false),
                        KnownEnglishCost + ReadableDomainLetterCost * (text.IndexOf('.', p) - p));
                // 英文の中の記号 (, . ! ? -) は半角のまま
                if (unit >= 0 && !kanaInput && IsAsciiSymbol(units[unit]) && state.PreviousEnglish && state.AllEnglish &&
                    KeepsPunctuationAfterEnglish(units, unit, pending, punctuateAfterEnglish))
                {
                    OfferEdge(node, state, new LatticeEdge(p, p + 1, true, "", false, false), 0);
                    continue;
                }
                if (neutral is { } symbol) OfferEdge(node, state, symbol, 0);
                foreach (var (edge, cost) in japanese) OfferEdge(node, state, edge, cost);
                foreach (var (edge, cost) in english) OfferEdge(node, state, edge, cost);
            }
        }

        LatticeNode? chosen = null;
        var chosenCost = double.MaxValue;
        var finals = new List<(double Cost, LatticeState State, LatticeNode Node)>();
        for (var slot = 0; slot < LatticeState.Count * keep; slot++)
        {
            var key = slot / keep;
            if (best[length, key, slot % keep] is not { } node) continue;
            var state = LatticeState.FromKey(key);
            var cost = node.Cost;
            if (state.Sokuon) cost += Weights[LinkFeature.DanglingSokuon];
            if (followingEnglish == true && state.Side == Side.Japanese && !state.Particle) cost += Weights[LinkFeature.Switch];
            if (followingEnglish == false && state.IsEnglish && node.Edge.English) cost += Weights[LinkFeature.Switch];
            finals.Add((cost, state, node));
            if (cost < chosenCost)
            {
                chosen = node;
                chosenCost = cost;
            }
        }
        if (LatticeTrace is { } trace)
        {
            trace($"lattice {text} (manual {manual}, preceding {precedingEnglish?.ToString() ?? "-"}, following {followingEnglish?.ToString() ?? "-"}, bias {englishBias})");
            for (var p = 0; p < length; p++)
                if (lexemes[p].Rule != LexemeRule.None) trace($"  字句 {p}: {lexemes[p].Rule} ({lexemes[p].End} 文字目まで英語)");
            foreach (var (cost, state, node) in finals.OrderBy(f => f.Cost))
                trace($"  {(node == chosen ? "*" : " ")} {cost:0.00} {state.Side}{(state.Particle ? " particle" : "")}{(state.Sokuon ? " sokuon" : "")}: {DescribePath(node, text, true)}");
        }
        if (reasons is not null && chosen is not null) reasons.Add(() => Explain(chosen, chosenCost, finals, text));
        var alternatives = new List<List<CompositionSegment>> { Path(chosen) };
        foreach (var (cost, _, node) in finals.OrderBy(f => f.Cost))
        {
            if (alternatives.Count >= count || cost > chosenCost + AlternativeMargin) break;
            var segments = Path(node);
            if (!alternatives.Any(a => a.SequenceEqual(segments))) alternatives.Add(segments);
        }
        return alternatives;

        List<CompositionSegment> Path(LatticeNode? last)
        {
            var edges = new List<LatticeEdge>();
            for (var node = last; node?.Previous is not null; node = node.Previous) edges.Add(node.Edge);
            edges.Reverse();
            return ToSegments(prepared, text, unitAt, unitsEnd, edges);
        }
    }

    /// <summary>選んだ道と次点の道。次点は、文字ごとの言語 (英語 / 日本語) が選んだ道と違う道のうち一番軽いもの (同じ区切りで状態だけ違う道は比べても意味がない)。</summary>
    private static string Explain(LatticeNode chosen, double chosenCost, List<(double Cost, LatticeState State, LatticeNode Node)> finals, string text)
    {
        var languages = Languages(chosen, text.Length);
        var (runnerCost, _, runner) = finals.Where(f => Languages(f.Node, text.Length) != languages).DefaultIfEmpty().MinBy(f => f.Node is null ? double.MaxValue : f.Cost);
        return $"区切り {DescribePath(chosen, text, Diagnostics.Log.RecordText)} 計 {chosenCost:0.00}" +
            (runner is null ? "、ほかの区切りなし" : $"、次点 {DescribePath(runner, text, Diagnostics.Log.RecordText)} 計 {runnerCost:0.00} (差 {runnerCost - chosenCost:0.00})");
    }

    /// <summary>
    /// 道の候補ごとに 言語:打った文字=読み+重み を並べる (EN:google+1 JA:kyouha=きょうは+2)。
    /// 打った文字を出さないなら、文字の代わりに文字数を出す (JA(6)+2)。
    /// </summary>
    private static string DescribePath(LatticeNode node, string text, bool withText)
    {
        var path = new List<string>();
        for (var at = node; at.Previous is { } previous; at = previous)
        {
            var raw = text[at.Edge.Start..at.Edge.End];
            var label = at.Edge.English ? "EN" : at.Edge.Neutral ? "--" : "JA";
            var shown = withText ? $"{label}:{raw}{(at.Edge.Kana.Length > 0 && at.Edge.Kana != raw ? "=" + at.Edge.Kana : "")}" : $"{label}({raw.Length})";
            path.Add($"{shown}+{at.Cost - previous.Cost:0.##}");
        }
        path.Reverse();
        return string.Join(" ", path);
    }

    /// <summary>道が文字ごとに英語 (E) か日本語 (J) か。記号・数字 (-) は言語を変えないので比べない。</summary>
    private static string Languages(LatticeNode node, int length)
    {
        var languages = new char[length];
        for (var at = node; at.Previous is not null; at = at.Previous)
            Array.Fill(languages, at.Edge.English ? 'E' : at.Edge.Neutral ? '-' : 'J', at.Edge.Start, at.Edge.End - at.Edge.Start);
        return new string(languages);
    }

    /// <summary>名詞に付く付属語か (が・を・に・で・する …)。英単語との切り替えの重みを 0 にする。</summary>
    private static bool IsNounParticle(string kana) => NounParticles.Contains(kana) || WordJudge.IsSuruForm(kana) || kana is "し" or "さ" or "せ";

    /// <summary>
    /// 名詞に付く付属語のうち、後ろに活用の続き (る・た・て) が要る形か (させ・られ・してい)。名詞の後ろには付くが、
    /// 後ろに英単語は来ないので、英語への切り替えを軽くしない (させ|taken・してい|run)。
    /// </summary>
    private static bool NeedsInflection(string kana) => kana.Length >= 2 && (kana.EndsWith("させ", StringComparison.Ordinal) || kana.EndsWith("られ", StringComparison.Ordinal) ||
        kana.EndsWith("てい", StringComparison.Ordinal) || kana.EndsWith("でい", StringComparison.Ordinal));

    /// <summary>和語で っ の後ろに来ない音 (濁音・な行・あ行など)。</summary>
    private const string NoSokuonBefore = "あいうえおがぎぐげござじずぜぞだぢづでどなにぬねのばびぶべぼまみむめもやゆよらりるれろわをん";

    private static readonly HashSet<string> NounParticles = ["が", "を", "に", "で", "と", "の", "は", "も", "へ", "や", "から", "まで", "より", "って", "だ", "です", "では"];

    /// <summary>
    /// 動詞・形容詞の活用形の後ろにしか付かない、1 文字の付属語 (名詞や英単語の後ろには付かない)。
    /// 2 文字以上の付属語 (ます・ません) は、英単語を切り出した残りにはまずならず、なっても日本語の根拠として十分なので入れない。
    /// </summary>
    private static readonly HashSet<string> VerbAuxiliaries = ["う", "た", "て", "ば", "れ"];

    /// <summary>
    /// start から始まる技術用語の綴り (share・make) のすぐ後ろが する の活用 (して・します) か。
    /// その綴りに付属語でない日本語の読み (しゃれ・まけ) があり、日本語由来の語 (ninja) でなければ、
    /// その区間を日本語の語として読まない (issue #1899)。「しゃれする」「まけします」は日本語として成り立たないため。
    /// ただし させ は一段動詞の使役形 (負けさせた・褒めさせて) とも読めるので、読みが一段動詞の語幹なら日本語のまま。
    /// </summary>
    private bool IsTechnicalWordBeforeSuru(string text, int start)
    {
        var runEnd = start;
        while (runEnd < text.Length && char.IsAsciiLetter(text[runEnd])) runEnd++;
        for (var end = start + 1; end < runEnd && end - start <= MaxSpan; end++)
        {
            var word = text[start..end];
            // 日本語由来の語 (ninja) は、日本語の読み (にんじゃ) のまま使うことがある
            if (!word.All(char.IsAsciiLetterLower) || !WordJudge.IsTechnicalEnglishWord(word) || WordJudge.IsJapaneseOriginWord(word)) continue;
            // 日本語の読みがある語だけ (github のように読めない語は、日本語の道がもともと重い)
            if (words.Romaji.ConvertLenient(word, final: true) is not { Length: > 0 } reading || !reading.All(KanaChars.IsHiraganaOrLongMark) ||
                RomajiTypoCorrector.IsFunctionWord(reading)) continue;
            // すぐ後ろが する の活用か (して・します・した …)。し だけは日本語の語の終わり (恩師・分子・如し) と紛れるので除く
            if (words.Romaji.ConvertLenient(text[end..runEnd].ToLowerInvariant(), final: true) is var next && next.All(KanaChars.IsHiraganaOrLongMark) &&
                WordJudge.IsSuruForm(next))
            {
                // 一段動詞の使役形 (負けさせた・褒めさせて) は する の使役形と同じかな。日本語の候補を残す
                if (next.StartsWith("させ", StringComparison.Ordinal) && CommonReadings.IchidanStems.Contains(reading)) continue;
                return true;
            }
        }
        return false;
    }

    /// <summary>かなを、語にならないかなとして読むときの重み (1 音 3、っ・ー は 0.5。小書きのかな ゃ・ぃ は前のかなと 1 音)。</summary>
    private static double UnknownKana(string kana) =>
        kana.Select((c, i) => c is 'っ' or 'ー' ? LightKanaCost : i > 0 && KanaChars.IsSmallKana(c) ? 0 : UnknownKanaCost).Sum();

    /// <summary>単位の読みがかな (ひらがな・ー) か。</summary>
    private static bool IsKanaUnit(CompositionUnit unit) => unit.Kana.Length > 0 && unit.Kana.All(KanaChars.IsHiraganaOrLongMark);

    /// <summary>ローマ字として読めなかった英字の単位か (zoom の m)。</summary>
    private static bool IsLetterUnit(CompositionUnit unit) => unit.Kana == unit.Raw && unit.Raw.Length > 0 && unit.Raw.All(char.IsAsciiLetter);

    /// <summary>[start, end) の中の、c 行の綴り (ca・ci・cu・ce・co。ch は除く) の数。</summary>
    private static int CRowSpellings(string text, int start, int end)
    {
        var count = 0;
        for (var i = start; i < end; i++)
            if (text[i] == 'c' && (i + 1 >= text.Length || text[i + 1] != 'h')) count++;
        return count;
    }

    private (LatticeEdge Edge, double Cost) JapaneseEdge(int start, int end, string kana)
    {
        var particle = IsNounParticle(kana);
        if (RomajiTypoCorrector.CommonWordCost(kana) is { } cost) return (new LatticeEdge(start, end, false, kana, true, particle), cost);
        if (CommonReadings.Phrases.Contains(kana) || words.UserJapanese.Contains(kana)) return (new LatticeEdge(start, end, false, kana, true, false), JapaneseWordCost);
        if (CommonReadings.Katakana.Contains(kana) || CommonReadings.Brands.Contains(kana)) return (new LatticeEdge(start, end, false, kana, true, false), KatakanaWordCost);
        // readings-rare.txt の読みは katakana.txt とも重なる (おねしゃす) ので、ほかの一覧の軽い重みを先に使う
        if (RomajiTypoCorrector.IsRareWord(kana)) return (new LatticeEdge(start, end, false, kana, true, false), kana.Length < 5 ? ShortRareWordCost : RareWordCost);
        return (new LatticeEdge(start, end, false, kana, true, false), double.NaN);
    }

    /// <summary>
    /// start から始まる日本語の候補 (語・語にならないかな 1 音・読めない英字 1 文字)。読みは単位の読み (変換ボックスの表示と同じ) を使う。
    /// ローマ字入力では、英語の候補が単位の途中で終わった所 (medal|s|ha の ha) は、単位の残りをローマ字として読み直す。
    /// 日本語の語は単位の途中で終わってもよい (単位の頭をローマ字として読み直す)。大文字を含む単位は日本語にしない (かな入力を除く)。
    /// </summary>
    private List<(LatticeEdge, double)> JapaneseEdges(Prepared prepared, IReadOnlyList<CompositionUnit> units, string text, int start, int[] unitOf, int unitsEnd, string pending,
        bool kanaInput)
    {
        var edges = new List<(LatticeEdge, double)>();
        var length = text.Length;
        var first = true;

        void Add(int end, string reading, bool wordOnly = false)
        {
            var (edge, cost) = JapaneseEdge(start, end, reading);
            if (first && !wordOnly)
            {
                // 1 音目は、語にならなくても読める (打っている途中の子音だけなら重み 0)
                // x で打った小書きの母音 (だめ|xe = ぇ) は伸ばす音なので ー と同じ
                var unknownCost = reading.Length == 0 ? 0 : reading is "ぁ" or "ぃ" or "ぅ" or "ぇ" or "ぉ" && text[start] == 'x' ? LightKanaCost : UnknownKana(reading);
                cost = double.IsNaN(cost) ? unknownCost : Math.Min(cost, unknownCost);
                first = false;
            }
            if (!double.IsNaN(cost)) edges.Add((edge, cost + CRowCost * CRowSpellings(text, start, end)));
        }

        bool LetterEdge(int at)
        {
            if (!char.IsAsciiLetterLower(text[at])) return false;
            // かなか ? ! の後ろで語の終わりまで続く w は笑い (きた|w・ほんとw？|w)。知らない英字と同じ重みで、日本語のまま置ける
            var laughter = at;
            while (laughter < length && text[laughter] == 'w') laughter++;
            if (laughter > at && at > 0 && (char.IsAsciiLetterLower(text[at - 1]) || text[at - 1] is '?' or '!') &&
                (laughter == length || !char.IsAsciiLetter(text[laughter])))
                edges.Add((new LatticeEdge(at, laughter, false, text[at..laughter], true, false), UnknownLetterCost));
            edges.Add((new LatticeEdge(at, at + 1, false, text[at].ToString(), true, false), UnknownKanaCost));
            return true;
        }

        // 入力途中の子音 (pending) から始まる
        if (start >= unitsEnd)
        {
            // Shift を押して打った子音 (W) は日本語にしない
            if (text[start..].Any(char.IsAsciiLetterUpper)) return edges;
            if (words.Romaji.ConvertLenient(text[start..].ToLowerInvariant(), final: true) is var tail && tail.All(KanaChars.IsHiraganaOrLongMark)) Add(length, tail);
            else LetterEdge(start);
            return edges;
        }
        var u = unitOf[start];
        var unitStart = prepared.RawOffsets[u];
        var kana = "";
        if (start == unitStart)
        {
            if (IsLetterUnit(units[u]))
            {
                // 読めない英字 (zoom の m・grok の g) は、英字のまま日本語に残せる
                LetterEdge(start);
                return edges;
            }
        }
        else
        {
            // 単位の途中から: 残りをローマ字として読み直す。読み切れなければ、次の単位 (英字だけのもの) まで続けて読む
            // (bacon|n + i の ni、quiz|k + a の ka)
            Detection.RomajiAnalysis? reading = null;
            var next = u + 1;
            for (; !kanaInput && next <= Math.Min(units.Count, u + 3); next++)
            {
                if (next > u + 1 && units[next - 1].Raw is var raw && (raw.Length == 0 || !raw.All(char.IsAsciiLetterLower))) break;
                var rest = text[start..prepared.RawOffsets[next]];
                if (rest.Any(char.IsAsciiLetterUpper)) break;
                if (words.Romaji.AnalyzeFragment(rest) is { IsValid: true, Partial: "" } complete)
                {
                    reading = complete;
                    break;
                }
            }
            if (reading is null)
            {
                LetterEdge(start);
                return edges;
            }
            kana = reading.Kana;
            Add(prepared.RawOffsets[next], kana);
            if (next == units.Count) AddPending();
            u = next;
        }
        for (var k = u; k < units.Count; k++)
        {
            // 語の頭でなくなったら、それより長い語は無い (1 音目は語にならなくても読む)
            if (!first && !RomajiTypoCorrector.CommonHasPrefix(kana) && !RomajiTypoCorrector.RareHasPrefix(kana) && !CommonReadings.Katakana.HasPrefix(kana) && !CommonReadings.Brands.HasPrefix(kana) &&
                !CommonReadings.Phrases.HasPrefix(kana) && !words.UserJapanesePrefixes.Contains(kana)) break;
            var unit = units[k];
            if (!IsKanaUnit(unit) || !kanaInput && unit.Raw.Any(char.IsAsciiLetterUpper)) break;
            var from = prepared.RawOffsets[k];
            // 語が単位の途中で終わる (日本語の後ろの英語が単位の途中から始まる)
            for (var end = from + 1; !kanaInput && !first && end < prepared.RawOffsets[k + 1]; end++)
                if (words.Romaji.AnalyzeFragment(text[from..end].ToLowerInvariant()) is { IsValid: true, Partial: "" } head) Add(end, kana + head.Kana, wordOnly: true);
            kana += unit.Kana;
            Add(prepared.RawOffsets[k + 1], kana);
            if (k + 1 == units.Count) AddPending();
        }
        return edges;

        // 入力の最後まで: 後ろの入力途中の子音 (pending) と、する の活用を打っている途中 (commit|す・test|す|r。名詞に付く付属語)
        void AddPending()
        {
            if (unitsEnd == length)
            {
                if (kana.Length > 0 && WordJudge.SuruForms.Any(form => form.Length > kana.Length && form.StartsWith(kana, StringComparison.Ordinal)))
                    edges.Add((new LatticeEdge(start, length, false, kana, true, true), JapaneseWordCost));
                return;
            }
            if (pending.Any(char.IsAsciiLetterUpper)) return;
            if (words.Romaji.ConvertLenient(pending.ToLowerInvariant(), final: true) is var tail && tail.All(KanaChars.IsHiraganaOrLongMark)) Add(length, kana + tail);
            else if (IsPendingSuruForm(kana, pending)) edges.Add((new LatticeEdge(start, length, false, kana, true, true), JapaneseWordCost));
        }
    }

    /// <summary>kana の後ろに入力途中の子音 (す|r・し|t) を打っている所で、母音を足すと する の活用になるか。</summary>
    private bool IsPendingSuruForm(string kana, string pending) =>
        pending.All(char.IsAsciiLetterLower) &&
        "aiueo".Any(vowel => words.Romaji.ConvertLenient(pending + vowel, final: true) is var next && WordJudge.IsSuruForm(kana + next));

    /// <summary>
    /// start から始まる、綴りで決まる候補: 英語 (知っている英単語・知らない英字・短縮形) と、
    /// ユーザーがかなに直して覚えた語 (日本語の語として重み 1)。
    /// </summary>
    private List<(LatticeEdge, double)> EnglishEdges(Prepared prepared, LanguageLearning learning, string text, int start, int[] unknown, bool kanaInput, bool manual, Func<int, bool> isBoundary, bool afterEnglish)
    {
        var edges = new List<(LatticeEdge, double)>();
        if (!char.IsAsciiLetter(text[start])) return edges;
        // 自動判定を使わないときは、Shift を押して打った大文字で始まる語だけ
        if (manual && !char.IsAsciiLetterUpper(text[start])) return edges;
        var length = text.Length;
        for (var end = start + 1; end <= length && end - start <= MaxSpan; end++)
        {
            var c = text[end - 1];
            if (!char.IsAsciiLetter(c) && c != '\'') break;
            if (!isBoundary(end)) continue;
            var span = text[start..end];
            double cost;
            if (span.Contains('\''))
            {
                if (!IsContraction(learning, span)) continue;
                cost = KnownEnglishCost;
            }
            else if (words.IsWordCasing(span) && words.IsLatticeEnglishWord(learning, span.ToLowerInvariant()) && (span.Length > 1 || Standalone(text, start, end, afterEnglish)) &&
                     !words.IsLowercaseJapaneseProperNoun(learning, span))
            {
                cost = learning.Get(span.ToLowerInvariant()) == true ? LearnedEnglishCost
                    : (WordJudge.IsReadableEnglish(span.ToLowerInvariant()) ? ReadableEnglishCost : KnownEnglishCost) + (span.Length <= 2 && span.All(char.IsAsciiLetterLower) ? ShortEnglishCost : 0);
                if (kanaInput && span.Length >= 4) cost += KanaInputEnglishBias;
                // 日本語由来の語 (suzuki・sushi) は、ローマ字で読んだかなも日本語の語として並べる
                if (span.All(char.IsAsciiLetterLower) && WordJudge.IsJapaneseOriginWord(span) &&
                    words.Romaji.ConvertLenient(span, final: true) is var reading && reading.All(KanaChars.IsHiraganaOrLongMark))
                    edges.Add((new LatticeEdge(start, end, false, reading, true, false), JapaneseWordCost));
            }
            else if (learning.Get(span.ToLowerInvariant()) == false)
            {
                // かなに直して覚えた語 (google を ごおgぇ に) は、日本語の語として読む
                edges.Add((new LatticeEdge(start, end, false, words.Romaji.ConvertLenient(span.ToLowerInvariant(), final: true), true, false), JapaneseWordCost));
                continue;
            }
            else if (!kanaInput && end == length && span.Length >= 4 && span.All(char.IsAsciiLetterLower) &&
                     unknown[Math.Max(start, Math.Min(end, prepared.Raw.Length))] > unknown[start] && words.IsEnglishPrefix(span))
                // 入力の最後の、英単語の頭 (insta|ll)。入力途中の子音より前に、ローマ字で読めない英字があるときだけ (mala は まぁ、mits は みつ の途中)
                cost = EnglishPrefixCost;
            else if (!kanaInput && unknown[end] > unknown[start])
            {
                var name = span.Length >= 3 && char.IsAsciiLetterUpper(span[0]) && char.IsAsciiLetterLower(span[1]);
                // 日本人名 (Takahashi) は長さによらず知っている英単語と同じ重みにする。小文字 1 文字ごとの重みでは、長い名前ほど頭で切って日本語を続けた道 (T|赤橋・Toyo|だ) が軽くなる (#1744)。
                // 名前の頭で切る道 (Ai|かわ) には、知らない語と同じく NameSplit の重みを足すので、Name の印は残す
                cost = name && words.IsJapaneseName(span) ? KnownEnglishCost
                    : span.Sum(ch => char.IsAsciiLetterUpper(ch) ? UnknownUpperCost : name ? NameLetterCost : UnknownLetterCost);
                edges.Add((new LatticeEdge(start, end, true, "", false, false, Name: name), cost));
                continue;
            }
            else continue;
            edges.Add((new LatticeEdge(start, end, true, "", false, false), cost));
        }
        return edges;
    }

    /// <summary>
    /// [start, end) の前後が英字以外で区切られているか (1 文字の英単語 a・i は、区切ったときだけ語)。
    /// 入力の頭は、前が英語のときだけ区切りとして数える (a だけを打ったら あ)。
    /// </summary>
    private static bool Standalone(string text, int start, int end, bool afterEnglish) =>
        (start == 0 ? afterEnglish : !char.IsAsciiLetter(text[start - 1])) && (end == text.Length || !char.IsAsciiLetter(text[end]));

    /// <summary>選んだ候補の並びを、英語・日本語の区間の並びにする (続いた日本語の候補は 1 つの区間にする。英語の候補は 1 つずつの区間)。</summary>
    private List<CompositionSegment> ToSegments(Prepared prepared, string text, int[] unitAt, int unitsEnd, List<LatticeEdge> edges)
    {
        var segments = new List<CompositionSegment>();
        foreach (var edge in edges)
        {
            var raw = text[edge.Start..edge.End];
            var kana = edge.English ? "" : EdgeKana(prepared, text, unitAt, unitsEnd, edge);
            if (segments.Count > 0 && !edge.English && !segments[^1].IsEnglish)
                segments[^1] = segments[^1] with { Kana = segments[^1].Kana + kana, Raw = segments[^1].Raw + raw };
            else segments.Add(new CompositionSegment(edge.English, kana, raw));
        }
        return segments.Count > 0 ? segments : [new CompositionSegment(false, "", text)];
    }

    /// <summary>
    /// 日本語の候補の表示の読み。単位の境目どうしなら単位の読み、単位の途中で区切ったなら候補の読み。
    /// 入力途中の子音 (pending) は表示・変換のときに後ろに足すので含めない。
    /// </summary>
    private string EdgeKana(Prepared prepared, string text, int[] unitAt, int unitsEnd, LatticeEdge edge)
    {
        var end = Math.Min(edge.End, unitsEnd);
        if (edge.Start >= end) return "";
        if (unitAt[edge.Start] >= 0 && unitAt[end] >= 0) return prepared.KanaRange(unitAt[edge.Start], unitAt[end]);
        if (edge.End <= unitsEnd) return edge.Kana;
        return words.Romaji.ConvertLenient(text[edge.Start..end].ToLowerInvariant(), final: false).Replace('-', 'ー');
    }

    /// <summary>ローマ字としても読める、よく使う拡張子 (Meltype.ace、setup.ini)。読めない拡張子 (dll、json) は一覧に無くても英字のまま。</summary>
    private static readonly HashSet<string> FileExtensions =
    [
        "a", "ace", "ada", "ado", "ann", "ape", "ase", "dae", "di", "do", "fifo", "fon", "geo", "go", "ifo", "ii", "ini", "ino", "ipa", "iso", "jade", "ko",
        "ma", "metadata", "midi", "mime", "mo", "mobi", "o", "oga", "ora", "oso", "otio", "po", "pyo", "ra", "re", "rego", "rei", "resi", "ru", "sami",
        "so", "suo", "tese", "ufo", "vue",
    ];

    // moe は meltype.moe のように使う (issue #1356)。ローマ字として読める前の語 (sugoku.moe) は、句点の後ろの日本語と重みで比べる
    private static readonly HashSet<string> DomainSuffixes = ["ai", "app", "au", "biz", "ca", "cn", "co", "com", "de", "dev", "edu", "eu", "fr", "gg", "gov", "info", "in", "io", "jp", "kr", "me", "moe", "net", "org", "uk", "us", "xyz"];

    /// <summary>
    /// start から始まるユーザー名の終わり。無ければ -1。
    /// @ の後ろ (Discord・X のメンション @kuraido) と、_ の入った語 (upah_setu、cafely_latte) は、ローマ字として読めても英字のまま。
    /// 英字・数字・_ が続く所までがユーザー名 (@ の後ろは、メールアドレスのドメインの . - も含める)。
    /// </summary>
    private static int UserNameEnd(Prepared prepared, IReadOnlyList<CompositionUnit> units, int start, string pending)
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
        var name = prepared.RawRange(start, end) + (end == n ? pending : "");
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
    private int ReadableStemDotSuffixEnd(Prepared prepared, LanguageLearning learning, IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        if (start > 0 && units[start - 1].Raw.Length > 0 && units[start - 1].Raw.All(char.IsAsciiLetter)) return -1;
        var dot = start;
        if (dot >= units.Count || units[dot].Raw.Length == 0 || !units[dot].Raw.All(char.IsAsciiLetter)) return -1;
        if (char.IsAsciiLetterUpper(units[dot].Raw[0])) return -1;
        while (dot < units.Count && units[dot].Raw.Length > 0 && units[dot].Raw.All(char.IsAsciiLetter)) dot++;
        if (dot >= units.Count || units[dot].Raw != ".") return -1;
        var stem = prepared.RawRange(start, dot);
        // 小文字の、日本語の語と同じ綴りの固有名詞 (wakatte) は、区間分けでかなになるので、ここでドメインとしてまとめる (wakatte.tv)
        if (words.IsKnownEnglishWord(learning, stem) && !words.IsLowercaseJapaneseProperNoun(learning, stem)) return -1;
        var suffix = DotSuffixEnd(prepared, learning, units, dot + 1, pending);
        if (suffix <= dot + 1) return -1;
        // 1.5 のような小数は拡張子ではない (soudesu.1.5 → そうです。1.5)。cpp のような英字の拡張子だけをまとめる。
        var suffixRaw = prepared.RawRange(dot + 1, Math.Min(suffix, units.Count));
        if (suffixRaw.Length > 0 && char.IsAsciiDigit(suffixRaw[0])) return -1;
        return suffix;
    }

    /// <summary>
    /// 入力先頭のドットファイル (.gitignore / .env / .dll) の終わり。無ければ -1 (issue #431)。
    /// 途中の 3.14desu や「です。」の . には使わない (start == 0 のときだけ)。
    /// </summary>
    private int LeadingDotfileEnd(Prepared prepared, LanguageLearning learning, IReadOnlyList<CompositionUnit> units, int start, string pending, bool manual)
    {
        if (manual || start != 0 || units.Count == 0 || units[0].Raw != ".") return -1;
        var end = DotSuffixEnd(prepared, learning, units, 1, pending);
        return end > 1 ? end : -1;
    }

    /// <summary>
    /// 英語の語のすぐ後ろの . に続く、ドメイン・拡張子 (tetr.io、MeltypeTip.dll、abi.json、go.mod) の終わり。無ければ -1 (issue #260)。
    /// ドメインは語全体が一致するときだけ (config の co、index の in を英字にして、残りをローマ字にしていた)。
    /// 拡張子は、ローマ字として読めない (dll、aab)・数字や . / を含む (e57、go.mod)・よく使う拡張子 (ace、ini) なら英字のまま。
    /// 最後までローマ字として読める語 (Meltype.desu) は、今までどおり判定する。後ろの助詞からは日本語 (MeltypeTip.dll|wokesu)。
    /// </summary>
    private int DotSuffixEnd(Prepared prepared, LanguageLearning learning, IReadOnlyList<CompositionUnit> units, int start, string pending)
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
        var name = prepared.RawRange(start, end) + (end == n ? pending : "");
        if (!name.Any(char.IsAsciiLetter)) return -1;
        var lower = name.ToLowerInvariant();
        static bool Known(string label) => DomainSuffixes.Contains(label) || FileExtensions.Contains(label);
        var lastLabel = lower[(lower.LastIndexOfAny(['.', '/']) + 1)..];
        if (Known(lower) || Known(lastLabel)) return end;
        if (lower.Any(c => char.IsAsciiDigit(c) || c is '.' or '/' or '_' or '+')) return end;
        // 打っている途中も、最後の子音までかなになるときだけ読めるとみなす (Meltype.bas を ばs と表示すると、確定で打ち間違いとして ば に直される)
        bool Readable(string text) => words.Romaji.Analyze(text) is { IsValid: true, Partial: "" or "n" };
        var readable = Readable(lower);
        // 短い語 (拡張子の長さ) の後ろが助詞で始まる日本語なら、そこまで。
        //   ドメイン・拡張子の後ろ: 助詞の後ろが読める・英単語でも分ける (tetr.io|de、github.com|ni|push)。全体が読める語では、今までどおりドメインだけ
        //   読めない語の後ろ: 助詞から最後まで読めるとき (dll|wokesu、json|nokakikata)。長い名前の途中 (gitig|nore)・助詞だけ (gc|no、t|ga) では分けない
        for (var k = start + 1; k < end; k++)
        {
            var stem = prepared.RawRange(start, k).ToLowerInvariant();
            if (stem.Length > 4) break;
            var rest = (prepared.RawRange(k, end) + (end == n ? pending : "")).ToLowerInvariant();
            if (TrailingParticles.FirstOrDefault(p => rest.StartsWith(p, StringComparison.Ordinal)) is not { } particle) continue;
            var afterParticle = rest[particle.Length..];
            // 英単語が続くのは 3 文字以上のときだけ (au|to|mount にしない)
            if ((readable ? DomainSuffixes.Contains(stem) : Known(stem)) &&
                (afterParticle.Length == 0 || Readable(afterParticle) || stem.Length >= 3 && words.IsKnownEnglishWord(learning, afterParticle))) return k;
            if (!readable && afterParticle.Length > 0 && Readable(rest) && !Readable(stem)) return k;
        }
        // 読めない語の後ろに、(助詞 +) 3 音以上の日本語の語から始まる読める語が続く (.env|fairu、.zshrc|nosettei)。短い語 (.js|on のおん) では分けない
        for (var k = start + 1; !readable && k < end; k++)
        {
            var stem = prepared.RawRange(start, k).ToLowerInvariant();
            if (Readable(stem)) continue;
            var rest = (prepared.RawRange(k, end) + (end == n ? pending : "")).ToLowerInvariant();
            if (!Readable(rest)) continue;
            var particle = TrailingParticles.FirstOrDefault(p => rest.StartsWith(p, StringComparison.Ordinal)) ?? "";
            var kana = words.Romaji.ConvertLenient(rest[particle.Length..], final: true);
            for (var length = 3; length <= kana.Length; length++)
                if (RomajiTypoCorrector.CommonWordCost(kana.AsSpan(0, length)) is not null || CommonReadings.Katakana.Contains(kana.AsSpan(0, length))) return k;
        }
        // 最後までローマ字として読める語 (Meltype.desu) は、ふつうに判定する
        return readable ? -1 : end;
    }

    /// <summary>
    /// 数字を含む固有名詞 (propernouns.txt の yachi8000) の終わり。start からの英字・数字が一覧にあるとき (前後に日本語が続いてもよい)。
    /// 英字の部分がローマ字として読めても、数字の前で区切ってかなにしない (やち8000 にしない: issue #1013)。
    /// </summary>
    private int ProperNounWithDigitsEnd(Prepared prepared, IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        static bool IsPart(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(char.IsAsciiLetterOrDigit);
        if (start >= units.Count || !IsPart(units[start])) return -1;
        // 数字の無い固有名詞 (korehaneovidedesu の neovide) と同じく、文の途中 (koreha|yachi8000|desu) からも引く。
        // 一覧にある一番長い頭 (yachi8000|desu の yachi8000)
        var end = -1;
        for (var i = start + 1; i <= units.Count && IsPart(units[i - 1]); i++)
        {
            var word = prepared.RawRange(start, i) + (i == units.Count && pending.All(char.IsAsciiLetterOrDigit) ? pending : "");
            if (word.Any(char.IsAsciiDigit) && word.Any(char.IsAsciiLetter) && words.ProperNouns.Contains(word.ToLowerInvariant())) end = i;
        }
        return end;
    }

    /// <summary>
    /// 数字を英字が挟む短い略語 (i18n、l10n、h2o、a11y、k8s) の終わり。無ければ -1。
    /// 末尾が数字だけの語 (win11、ps5) は区切りの重みに任せる。
    /// 日本語の途中の数字 (かいぎは3じ) を略語にしないよう、前後の英字は短く、全体も短いときだけ。
    /// 末尾の子音が打ちかけ (pending の n: i18|n) のときも、数字の後ろの英字として数える。
    /// </summary>
    private int NumeronymEnd(Prepared prepared, IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        static bool IsLetter(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(char.IsAsciiLetter);
        static bool IsDigit(CompositionUnit unit) => unit.Raw is [var d] && char.IsAsciiDigit(d);
        if (start >= units.Count || !IsLetter(units[start])) return -1;
        var i = start;
        while (i < units.Count && IsLetter(units[i])) i++;
        var headLen = prepared.RawRange(start, i).Length;
        if (headLen is < 1 or > 2 || i >= units.Count || !IsDigit(units[i])) return -1;
        var digitStart = i;
        while (i < units.Count && IsDigit(units[i])) i++;
        var digitLen = prepared.RawRange(digitStart, i).Length;
        if (digitLen is < 1 or > 2) return -1;
        var digitEnd = i;
        var pendingLetters = i == units.Count && pending.Length > 0 && pending.All(char.IsAsciiLetter) ? pending : "";
        if (i < units.Count && IsLetter(units[i]))
        {
            var tailStart = i;
            while (i < units.Count && IsLetter(units[i])) i++;
            // 数字の後ろにまた数字が続く形 (a1b2) は対象外
            if (i < units.Count && IsDigit(units[i])) return -1;
            var tailLen = prepared.RawRange(tailStart, i).Length;
            pendingLetters = i == units.Count && pending.Length > 0 && pending.All(char.IsAsciiLetter) ? pending : "";
            if (tailLen + pendingLetters.Length is < 1 or > 3) return -1;
        }
        else if (pendingLetters.Length is < 1 or > 3) return -1;
        var word = prepared.RawRange(start, i) + pendingLetters;
        if (word.Length is < 3 or > 6) return -1;
        // 数字の前と後ろが、どちらもかなとして最後まで読める (huuso|ku|6|me|-toru の ku と me) なら、
        // 略語 (i18n の n、k8s の s、h2o の h) ではなく、数字を挟んだ日本語 (風速6メートル: issue #1017)。
        // 母音 1 文字 (e2e・o2o の e・o) は、かなとして読めても略語のことが多いので、子音から始まる 2 文字以上のときだけ。
        var head = prepared.RawRange(start, digitStart).ToLowerInvariant();
        var tail = (prepared.RawRange(digitEnd, i) + pendingLetters).ToLowerInvariant();
        // 母音 1 文字でも、すぐ前に英字が続いていれば、前の語の続き (mou|1|tu の u、dai|1|dan の i) で、数字を挟んだ日本語 (もう1つ・第1弾)。
        var headContinuesWord = head.Length == 1 && start > 0 && prepared.RawRange(start - 1, start) is [.., var before] && char.IsAsciiLetter(before);
        if (head.Length >= 2 && tail.Length >= 2 && words.Romaji.AnalyzeFragment(head) is { IsValid: true, Partial: "" } &&
            words.Romaji.AnalyzeFragment(tail) is { IsValid: true, Partial: "" }) return -1;
        // 前の語の続きの母音なら、後ろが ん で終わる読み (dai|1|dan) も日本語
        if (headContinuesWord && tail.Length >= 2 && words.Romaji.AnalyzeFragment(tail) is { IsValid: true, Partial: "" or "n" }) return -1;
        return i;
    }

    /// <summary>
    /// 打った文字の位置 p の \ と、すぐ後ろに続く英字の終わり (打った文字の位置)。無ければ -1。
    /// \ の後ろの英字は 1 文字でもローマ字として読まない (￥ん になっていた: issue #1543)。
    /// 続く英字が英単語 (\begin) か、2 文字目からがかなとして最後まで読めない (\frac) なら英字をまとめて英語にし、
    /// 読めるなら \ と 1 文字だけを英語にして、その先 (\n|desu・\t|de) の区切りはコストで決める。
    /// </summary>
    private int EscapeEnd(LanguageLearning learning, string text, int p)
    {
        if (text[p] != '\\' || p + 1 >= text.Length || !char.IsAsciiLetter(text[p + 1])) return -1;
        var end = p + 1;
        while (end < text.Length && char.IsAsciiLetter(text[end])) end++;
        var letters = text[(p + 1)..end].ToLowerInvariant();
        var whole = words.IsLatticeEnglishWord(learning, letters) || words.Romaji.AnalyzeFragment(letters[1..]) is not { IsValid: true, Partial: "" or "n" };
        return whole ? end : p + 2;
    }

    /// <summary>
    /// start から始まる - の入った英単語 (e-mail、co-op) の終わり。無ければ -1。
    /// 後ろに日本語が続いてもよい (e-mail|de) ので、- の後ろは長い方から英単語になる所を探す。
    /// </summary>
    private int HyphenatedWordEnd(Prepared prepared, LanguageLearning learning, IReadOnlyList<CompositionUnit> units, int start, string pending)
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
            var word = (prepared.RawRange(start, end) + (end == n ? pending : "")).ToLowerInvariant();
            if (afterJapaneseRomaji ? WordJudge.NumericHyphenatedWords.Contains(word) : words.IsHyphenatedEnglishWord(learning, word)) return end;
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
    private bool IsContraction(LanguageLearning learning, string span)
    {
        var apostrophe = span.IndexOf('\'');
        if (apostrophe <= 0 || apostrophe != span.LastIndexOf('\'')) return false;
        var stem = span[..apostrophe].ToLowerInvariant();
        var suffix = span[(apostrophe + 1)..].ToLowerInvariant();
        // 語の最後の g を ' にした書き方 (swingin' = swinging、rockin'): stem + g が英単語なら英語
        if (suffix.Length == 0 && stem.Length >= 3 && stem.EndsWith("in", StringComparison.Ordinal) && stem.All(char.IsAsciiLetterLower)) return words.IsKnownEnglishWord(learning, stem + "g");
        if (!stem.All(char.IsAsciiLetterLower) || suffix is not ("t" or "s" or "re" or "ve" or "ll" or "d" or "m")) return false;
        if (suffix == "t") return stem.Length >= 2 && stem[^1] == 'n';
        return stem == "i" || words.IsDictionaryWord(stem) || words.ProperNouns.Contains(stem);
    }

    private static readonly string[] TrailingParticles = ["kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha", "wa", "ya"];

    /// <summary>
    /// 英字の後ろの記号 units[i] を、英文の記号 (半角のまま) として扱うか。
    /// すぐ後ろに英数字が続く . は、ドメイン・拡張子の区切り (tetr.io、MeltypeTip.dll)。
    /// </summary>
    private static bool KeepsPunctuationAfterEnglish(IReadOnlyList<CompositionUnit> units, int i, string pending, bool punctuateAfterEnglish)
    {
        if (units[i].Raw is not ("," or ".") || !punctuateAfterEnglish) return true;
        var next = i + 1 < units.Count ? units[i + 1].Raw : pending;
        return units[i].Raw == "." && next.Length > 0 && char.IsAsciiLetterOrDigit(next[0]);
    }

    /// <summary>英文の中では半角のままにする記号。[ ] は日本語の入力では「」なので含めない (英単語の後ろでも「」: bot「Thinking」)。</summary>
    private static bool IsAsciiSymbol(CompositionUnit unit) =>
        unit.Raw.Length == 1 && unit.Raw[0] is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(unit.Raw[0]) && unit.Raw[0] is not ('[' or ']');
}
