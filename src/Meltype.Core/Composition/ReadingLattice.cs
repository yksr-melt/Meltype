// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// 日英の区切り。日本語の語・英単語・未知語をノードにしたラティスを作り、Viterbi で最小コスト経路を選ぶ。
/// 日本語の語の生起コストは RomajiTypoCorrector.WordCost と共有する。
///
/// コストで比べない決まり:
/// - 大文字を含む区間は日本語のノードにしない。
/// - 綴りを名指しする条件は持たない。
/// - 知っている英単語は、英単語の書き方の大文字・小文字 (its・Its・ITS・GitHub) のときだけノードにする (iTs は its にしない)。
/// - 1 文字の英単語 (a・i・u・r) は、前後を英字以外で区切ったときだけノードにする。
/// - 判定の強さが手動なら、Shift を押して打った大文字で始まる語だけを英語のノードにする。
/// - 記号・数字の決まり (ドメイン・拡張子・ユーザー名・- の入った語・数字を挟んだ略語・英文の中の記号) は、コストで比べずにその区間を英語にする。
/// - ローマ字入力では 1 文字ずつを区切りの位置にする (単位の途中で区切れる: get|Element、medal|s|ha)。かな入力では打った単位の境目だけ。
/// </summary>
public sealed partial class CompositionDetector
{
    // ---- 日本語のノードの生起コスト ----
    // 1 文字の語 1.5・付属語 0.8 は RomajiTypoCorrector.WordCost
    // 日本語の語 (readings.txt・readings-extra.txt と活用した形)・同梱の語句の読み (phrases.txt の どしたの)・
    // ユーザーがかなに直して覚えた語・english.txt の日本語由来の語 (sushi) をローマ字で読んだかな
    private const double JapaneseWordCost = 1;
    // katakana.txt の語 (ほすてぃんぐ) と brands.txt の読み (ぎっとはぶ)。katakana.txt は英単語をカタカナにした語 (アマゾン) が多いので、同じ綴りの英単語 (amazon) に譲る
    private const double KatakanaWordCost = 1.1;
    // 語にならないかな 1 音、日本語の中に残る読めない英字 1 文字
    private const double UnknownKanaCost = 3;
    // 語にならない っ・ー、x で打った小書きの母音が語の 1 音目 (だめ|ぇ)
    private const double LightKanaCost = 0.5;
    // 子音を重ねて打った っ で、前後の片方がかなでない (excel|と|ppt の tt)。語にならないかなと同じ 3 にする
    private const double DanglingSokuonCost = UnknownKanaCost - LightKanaCost;

    // ---- 英語のノードの生起コスト ----
    // 知っている英単語 (english.txt・固有名詞・english-words.txt のうちスペルチェッカーも知っている語)
    private const double KnownEnglishCost = 1;
    // english-readable.txt の語 (open・feature): ローマ字としても読めるが、英語で打つことの多い語
    private const double ReadableEnglishCost = 0.7;
    // ユーザーが英字に直して覚えた語。同じ読みの日本語の語 (sushi = すし、重み 1) より軽くする
    private const double LearnedEnglishCost = 0.7;
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
    // 判定の強さ 積極的・慎重 で、英語のノードごとに足す
    private const double AggressiveEnglishBias = -0.5;
    private const double ConservativeEnglishBias = 1;
    // 変換の候補に出す英語寄りの区切りを作るとき英語のノードごとに足す: 英単語 + 切り替え + 付属語 (midi + か = 2.8) が日本語の語 1 つ (みぢか = 1) より軽くなる
    private const double LeaningEnglishBias = -2;

    // ---- 連接コスト ----
    // 日本語と英語の切り替え。日本語の側が名詞に付く付属語 (が・を・に・で・する: github|で) なら 0
    private const double SwitchCost = 1;
    // 英文の続き ("I want ") から日本語への切り替え。付属語でも足す
    private const double SentenceSwitchCost = 2;
    // 英単語どうしがスペースなしで続く
    private const double AdjacentEnglishCost = 1;
    // 英字 → 日本語 → 英字 と区切りなしに戻る (procras|ち|cle)。あいだが付属語を含むとき (excel|と|ppt) は除く
    private const double SandwichCost = 1;
    // 大文字で始まる知らない語を小文字の途中で切って、付属語でない日本語を続ける (Ta|なか)
    private const double NameSplitCost = 3;
    // ltu・xtu で打った っ の後ろに入力が続く (mai|ltu|kau)。かなの前の っ は子音を重ねて打つ
    private const double SpelledSokuonCost = 2;
    // 小文字で終わる英語のすぐ後ろの日本語が あ行のかな (thing|う・rate|ぃす) で始まる: 英単語の最後の子音と次の母音の切り間違い。USBいれた と、母音字で終わる英語の後ろの大きい あ行 (issue|ある) は除く
    private const double VowelAfterEnglishCost = 1;
    // 日本語の中の c 行の綴り (ca・ci・cu・ce・co。ch は除く) 1 つ: 日本語を打つときは k を使う
    private const double CRowCost = 0.5;
    // 同じコストなら日本語にする
    private const double EnglishTieBreak = 1e-6;
    // 1 つの候補として見る長さの上限 (ローマ字の文字数)
    private const int MaxSpan = 48;

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

    /// <summary>調べる用 (--segments): 区間分けのたびに、最後の状態ごとの一番軽い道と、候補ごとの重み (+) を 1 行ずつ渡す。* が選んだ道。</summary>
    internal Action<string>? LatticeTrace { get; set; }

    private List<CompositionSegment> FindSpansByReading(IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish, bool? followingEnglish,
        DetectionLevel level, bool englishSentence, bool kanaInput, double extraEnglishBias = 0)
    {
        var text = _preparedRaw + pending;
        var length = text.Length;
        if (length == 0) return [new CompositionSegment(false, "", "")];
        var unitsEnd = _preparedRaw.Length;
        // 文字の位置 → その位置から始まる単位 (単位の境目でなければ -1)
        var unitAt = new int[length + 1];
        Array.Fill(unitAt, -1);
        for (var k = 0; k < units.Count; k++) unitAt[_preparedRawOffsets[k]] = k;
        unitAt[unitsEnd] = units.Count;
        // 文字の位置 → その文字を含む単位
        var unitOf = new int[unitsEnd];
        for (var k = 0; k < units.Count; k++)
            for (var p = _preparedRawOffsets[k]; p < _preparedRawOffsets[k + 1]; p++) unitOf[p] = k;
        bool IsBoundary(int position) => !kanaInput || unitAt[position] >= 0 || position == length;

        // 知らない英字として英語にできる文字 (ローマ字として読めない英字・大文字・打ち終わって読めない入力途中の子音) の数の累積
        var unknown = new int[length + 1];
        // 入力途中の子音は、入力中も Enter のときと同じ読みで重みを付ける (表示と確定の区切りを揃える)
        var finalPending = _romaji.ConvertLenient(pending.ToLowerInvariant(), final: true);
        for (var p = 0; p < length; p++)
        {
            var c = text[p];
            var unreadable = char.IsAsciiLetterUpper(c) ||
                (p < unitsEnd ? unitAt[p] is var k and >= 0 && _preparedUnreadable[k + 1] > _preparedUnreadable[k] : finalPending.Any(char.IsAsciiLetter));
            unknown[p + 1] = unknown[p] + (char.IsAsciiLetter(c) && unreadable ? 1 : 0);
        }

        // 判定の強さ: 英語の候補の重みに足す
        var englishBias = extraEnglishBias + level switch { DetectionLevel.Aggressive => AggressiveEnglishBias, DetectionLevel.Conservative => ConservativeEnglishBias, _ => 0 };
        var best = new LatticeNode?[length + 1, LatticeState.Count];
        var start = new LatticeState(precedingEnglish switch { true => englishSentence ? Side.Sentence : Side.English, false => Side.Japanese, null => Side.None },
            false, true, precedingEnglish == true, ContextSwitch: precedingEnglish == true);
        best[0, start.Key] = new LatticeNode(0, null, default);

        void Offer(int position, LatticeState state, LatticeNode node)
        {
            ref var current = ref best[position, state.Key];
            if (current is null || node.Cost < current.Cost) current = node;
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

        void OfferEdge(LatticeNode from, LatticeState state, LatticeEdge edge, double cost)
        {
            double step;
            LatticeState next;
            var japanese = !edge.English && !edge.Neutral;
            // 子音を重ねて打った っ は、前後をかなに挟まれているときだけ軽い (excelとっpt・cwっで にしない。ltu・xtu で打った っ と、Goって の って は除く)
            var vowel = japanese && state.PreviousEnglish && edge.Start > 0 && char.IsAsciiLetterLower(text[edge.Start - 1]) && edge.Kana is [var head, ..] &&
                ("ぁぃぅぇぉ".Contains(head) || "あいうえお".Contains(head) && !"aeiou".Contains(text[edge.Start - 1])) ? VowelAfterEnglishCost : 0;
            // 大文字で始まる知らない語 (Tanaka) を小文字の途中で切って、付属語でない日本語を続ける (Ta|なか)
            var nameSplit = japanese && !edge.Particle && from.Edge.Name && from.Edge.End == edge.Start && edge.Start < length && char.IsAsciiLetterLower(text[edge.Start]) ? NameSplitCost : 0;
            // ltu・xtu で打った っ の後ろに入力が続く (まいっ|かう・れあ|っ|かう)。っ の後ろにかなが続くときは子音を重ねて打つ (maikka・moratte)
            var spelledSokuon = japanese ? SpelledSokuonCost * SpelledSokuons(edge.Start, edge.End) : 0;
            var sokuon = vowel + nameSplit + spelledSokuon + (state.Sokuon && !japanese ? DanglingSokuonCost : 0) +
                (japanese && edge.Kana == "っ" && DoubledAt(edge.Start) && state.Side != Side.Japanese ? DanglingSokuonCost : 0);
            if (edge.English)
            {
                step = cost + EnglishTieBreak + englishBias;
                if (state.Side == Side.Japanese && edge.Start > 0 && char.IsAsciiLetter(text[edge.Start - 1]) && char.IsAsciiLetter(text[edge.Start]))
                {
                    // 英字 → 日本語 → 英字 と、英字のあいだで区切りなしに戻る (procras|ち|cle)。あいだが付属語を含むとき (excel|と|ppt) は除く
                    var run = from;
                    var particle = run.Edge.Particle;
                    while (run.Previous is { } previous && !run.Edge.English && !run.Edge.Neutral) { if (previous.Edge.English || previous.Edge.Neutral) break; run = previous; particle |= run.Edge.Particle; }
                    if (!particle && run.Previous is { Edge.English: true } before && before.Edge.End == run.Edge.Start && run.Edge.Start > 0 && char.IsAsciiLetter(text[run.Edge.Start - 1])) step += SandwichCost;
                }
                if (state.Side == Side.Japanese && !state.Particle) step += SwitchCost;
                if (state.IsEnglish && edge.Start > 0 && char.IsAsciiLetter(text[edge.Start - 1]) && char.IsAsciiLetter(text[edge.Start])) step += AdjacentEnglishCost;
                // 英文の続きの強さは、最初の英語の語までで使い終わる (英文の中の記号では使い終わらない)
                var sentence = state.Side == Side.Sentence && !char.IsAsciiLetter(text[edge.Start]);
                next = new LatticeState(sentence ? Side.Sentence : Side.English, false, state.AllEnglish, true, ContextSwitch: state.ContextSwitch);
            }
            else if (edge.Neutral)
            {
                step = cost;
                next = state with { PreviousEnglish = false };
            }
            else
            {
                step = cost;
                if (state.Side == Side.Sentence) step += SentenceSwitchCost;
                // 3 文字以上の英単語の後ろの、助詞と同じかなで始まる語 (java|のこ・java|でかく) も、助詞からの切り替えと同じ重みにする
                // (日本語の道だけが の|こ を のこ にまとめられて、英語が負ける。so|ですか・ma|のなか のような 2 文字の語は除く)
                else if (state.IsEnglish && !edge.Particle &&
                         !(edge.Kana.Length > 1 && NounParticles.Contains(edge.Kana[..1]) && from.Edge.English && from.Edge.End - from.Edge.Start >= 3)) step += SwitchCost;
                // 前の英語の文脈からの切り替えは、英語の語から始めた道も最初の日本語で数える (hello + sushi|が を すしが と同じ重さの差で比べる)
                if (state.ContextSwitch && edge.Start > 0) step += englishSentence ? SentenceSwitchCost : SwitchCost;
                next = new LatticeState(Side.Japanese, edge.Particle, state.AllEnglish && !edge.Letters, false);
            }
            next = next with { Sokuon = japanese && edge.Kana.EndsWith('っ') && DoubledAt(edge.End - 1) };
            Offer(edge.End, next, new LatticeNode(from.Cost + step + sokuon, from, edge));
        }

        var englishLevel = level != DetectionLevel.Manual;
        for (var p = 0; p < length; p++)
        {
            if (!IsBoundary(p)) continue;
            var japanese = JapaneseEdges(units, text, p, unitOf, unitsEnd, pending, kanaInput);
            var english = EnglishEdges(text, p, unknown, kanaInput, level, IsBoundary, precedingEnglish == true);
            var unit = p < unitsEnd ? unitAt[p] : -1;
            // 記号・数字・空白・z の記号 (zh = ←) の単位: 言語を切り替えない
            LatticeEdge? neutral = unit >= 0 && !IsKanaUnit(units[unit]) && !IsLetterUnit(units[unit])
                ? new LatticeEdge(p, _preparedRawOffsets[unit + 1], false, units[unit].Kana, false, false, true)
                : null;

            // 記号・数字の決まり (状態によらないもの)
            var forced = -1;
            // ローマ字として読める語 + . + ドメイン・拡張子 (llama.cpp・hoge.io): 句点の後ろの日本語 (ryoukaidesu.|deha) と重みで比べる
            var domain = unit >= 0 && !kanaInput && englishLevel ? ReadableStemDotSuffixEnd(units, unit, pending) : -1;
            if (unit >= 0 && !kanaInput)
            {
                forced = LeadingDotfileEnd(units, unit, pending, level);
                if (forced < 0) forced = UserNameEnd(units, unit, pending);
                if (forced < 0 && englishLevel) forced = HyphenatedWordEnd(units, unit, pending);
                if (forced < 0 && englishLevel) forced = NumeronymEnd(units, unit, pending);
                if (forced < 0 && englishLevel) forced = VersionSuffixEnd(units, unit, pending);
            }

            for (var key = 0; key < LatticeState.Count; key++)
            {
                if (best[p, key] is not { } node) continue;
                var state = LatticeState.FromKey(key);
                var end = forced;
                // ドメイン・拡張子の . の後ろ (tetr.io、MeltypeTip.dll)
                if (end < 0 && unit > 0 && englishLevel && !kanaInput && units[unit - 1].Raw == "." && state.PreviousEnglish) end = DotSuffixEnd(units, unit, pending);
                if (end > unit)
                {
                    var to = end == units.Count ? length : _preparedRawOffsets[end];
                    OfferEdge(node, state, new LatticeEdge(p, to, true, "", false, false), KnownEnglishCost);
                    continue;
                }
                if (domain > unit)
                    OfferEdge(node, state, new LatticeEdge(p, domain == units.Count ? length : _preparedRawOffsets[domain], true, "", false, false),
                        KnownEnglishCost + ReadableDomainLetterCost * (text.IndexOf('.', p) - p));
                // 英文の中の記号 (, . ! ? -) は半角のまま
                if (unit >= 0 && !kanaInput && IsAsciiSymbol(units[unit]) && state.PreviousEnglish && state.AllEnglish &&
                    KeepsPunctuationAfterEnglish(units, unit, pending, precedingEnglish))
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
        for (var key = 0; key < LatticeState.Count; key++)
        {
            if (best[length, key] is not { } node) continue;
            var state = LatticeState.FromKey(key);
            var cost = node.Cost;
            if (state.Sokuon) cost += DanglingSokuonCost;
            if (followingEnglish == true && state.Side == Side.Japanese && !state.Particle) cost += SwitchCost;
            if (followingEnglish == false && state.IsEnglish && node.Edge.English) cost += SwitchCost;
            finals.Add((cost, state, node));
            if (cost < chosenCost)
            {
                chosen = node;
                chosenCost = cost;
            }
        }
        if (LatticeTrace is { } trace)
        {
            trace($"lattice {text} (level {level}, preceding {precedingEnglish?.ToString() ?? "-"}, following {followingEnglish?.ToString() ?? "-"}, bias {englishBias})");
            foreach (var (cost, state, node) in finals.OrderBy(f => f.Cost))
            {
                var path = new List<string>();
                for (var at = node; at.Previous is { } previous; at = previous)
                    path.Add($"{(at.Edge.English ? "EN" : at.Edge.Neutral ? "--" : "JA")}:{text[at.Edge.Start..at.Edge.End]}{(at.Edge.Kana.Length > 0 && at.Edge.Kana != text[at.Edge.Start..at.Edge.End] ? "=" + at.Edge.Kana : "")}+{at.Cost - previous.Cost:0.##}");
                path.Reverse();
                trace($"  {(node == chosen ? "*" : " ")} {cost:0.00} {state.Side}{(state.Particle ? " particle" : "")}{(state.Sokuon ? " sokuon" : "")}: {string.Join(" ", path)}");
            }
        }
        var edges = new List<LatticeEdge>();
        for (var node = chosen; node?.Previous is not null; node = node.Previous) edges.Add(node.Edge);
        edges.Reverse();
        return ToSegments(units, text, unitAt, unitsEnd, edges);
    }

    /// <summary>名詞に付く付属語か (が・を・に・で・する …)。英単語との切り替えの重みを 0 にする。</summary>
    private static bool IsNounParticle(string kana) => NounParticles.Contains(kana) || IsSuruForm(kana) || kana is "し" or "さ" or "せ";

    private static readonly HashSet<string> NounParticles = ["が", "を", "に", "で", "と", "の", "は", "も", "へ", "や", "から", "まで", "より", "って", "だ", "です"];

    /// <summary>かなを、語にならないかなとして読むときの重み (1 音 3、っ・ー は 0.5。小書きのかな ゃ・ぃ は前のかなと 1 音)。</summary>
    private static double UnknownKana(string kana) =>
        kana.Select((c, i) => c is 'っ' or 'ー' ? LightKanaCost : i > 0 && IsSmallKana(c) ? 0 : UnknownKanaCost).Sum();

    private static bool IsSmallKana(char c) => c is 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ' or 'ゃ' or 'ゅ' or 'ょ' or 'ゎ';

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
        if (CommonReadings.Phrases.Contains(kana) || _userJapanese.Contains(kana)) return (new LatticeEdge(start, end, false, kana, true, false), JapaneseWordCost);
        if (CommonReadings.Katakana.Contains(kana) || CommonReadings.Brands.Contains(kana)) return (new LatticeEdge(start, end, false, kana, true, false), KatakanaWordCost);
        return (new LatticeEdge(start, end, false, kana, true, false), double.NaN);
    }

    /// <summary>
    /// start から始まる日本語の候補 (語・語にならないかな 1 音・読めない英字 1 文字)。読みは単位の読み (変換ボックスの表示と同じ) を使う。
    /// ローマ字入力では、英語の候補が単位の途中で終わった所 (medal|s|ha の ha) は、単位の残りをローマ字として読み直す。
    /// 日本語の語は単位の途中で終わってもよい (単位の頭をローマ字として読み直す)。大文字を含む単位は日本語にしない (かな入力を除く)。
    /// </summary>
    private List<(LatticeEdge, double)> JapaneseEdges(IReadOnlyList<CompositionUnit> units, string text, int start, int[] unitOf, int unitsEnd, string pending,
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
            if (_romaji.ConvertLenient(text[start..].ToLowerInvariant(), final: true) is var tail && tail.All(KanaChars.IsHiraganaOrLongMark)) Add(length, tail);
            else LetterEdge(start);
            return edges;
        }
        var u = unitOf[start];
        var unitStart = _preparedRawOffsets[u];
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
                var rest = text[start.._preparedRawOffsets[next]];
                if (rest.Any(char.IsAsciiLetterUpper)) break;
                if (_romaji.AnalyzeFragment(rest) is { IsValid: true, Partial: "" } complete)
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
            Add(_preparedRawOffsets[next], kana);
            if (next == units.Count) AddPending();
            u = next;
        }
        for (var k = u; k < units.Count; k++)
        {
            // 語の頭でなくなったら、それより長い語は無い (1 音目は語にならなくても読む)
            if (!first && !RomajiTypoCorrector.CommonHasPrefix(kana) && !CommonReadings.Katakana.HasPrefix(kana) && !CommonReadings.Brands.HasPrefix(kana) &&
                !CommonReadings.Phrases.HasPrefix(kana) && !_userJapanesePrefixes.Contains(kana)) break;
            var unit = units[k];
            if (!IsKanaUnit(unit) || !kanaInput && unit.Raw.Any(char.IsAsciiLetterUpper)) break;
            var from = _preparedRawOffsets[k];
            // 語が単位の途中で終わる (日本語の後ろの英語が単位の途中から始まる)
            for (var end = from + 1; !kanaInput && !first && end < _preparedRawOffsets[k + 1]; end++)
                if (_romaji.AnalyzeFragment(text[from..end].ToLowerInvariant()) is { IsValid: true, Partial: "" } head) Add(end, kana + head.Kana, wordOnly: true);
            kana += unit.Kana;
            Add(_preparedRawOffsets[k + 1], kana);
            if (k + 1 == units.Count) AddPending();
        }
        return edges;

        // 入力の最後まで: 後ろの入力途中の子音 (pending) と、する の活用を打っている途中 (commit|す・test|す|r。名詞に付く付属語)
        void AddPending()
        {
            if (unitsEnd == length)
            {
                if (kana.Length > 0 && CompositionDetector.SuruForms.Any(form => form.Length > kana.Length && form.StartsWith(kana, StringComparison.Ordinal)))
                    edges.Add((new LatticeEdge(start, length, false, kana, true, true), JapaneseWordCost));
                return;
            }
            if (pending.Any(char.IsAsciiLetterUpper)) return;
            if (_romaji.ConvertLenient(pending.ToLowerInvariant(), final: true) is var tail && tail.All(KanaChars.IsHiraganaOrLongMark)) Add(length, kana + tail);
            else if (IsPendingSuruForm(kana, pending)) edges.Add((new LatticeEdge(start, length, false, kana, true, true), JapaneseWordCost));
        }
    }

    /// <summary>kana の後ろに入力途中の子音 (す|r・し|t) を打っている所で、母音を足すと する の活用になるか。</summary>
    private bool IsPendingSuruForm(string kana, string pending) =>
        pending.All(char.IsAsciiLetterLower) &&
        "aiueo".Any(vowel => _romaji.ConvertLenient(pending + vowel, final: true) is var next && CompositionDetector.IsSuruForm(kana + next));

    /// <summary>
    /// start から始まる、綴りで決まる候補: 英語 (知っている英単語・知らない英字・短縮形) と、
    /// ユーザーがかなに直して覚えた語 (日本語の語として重み 1)。
    /// </summary>
    private List<(LatticeEdge, double)> EnglishEdges(string text, int start, int[] unknown, bool kanaInput, DetectionLevel level, Func<int, bool> isBoundary, bool afterEnglish)
    {
        var edges = new List<(LatticeEdge, double)>();
        if (!char.IsAsciiLetter(text[start])) return edges;
        // 手動では Shift を押して打った大文字で始まる語だけ
        if (level == DetectionLevel.Manual && !char.IsAsciiLetterUpper(text[start])) return edges;
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
                if (!IsContraction(span)) continue;
                cost = KnownEnglishCost;
            }
            else if (IsWordCasing(span) && IsLatticeEnglishWord(span.ToLowerInvariant()) && (span.Length > 1 || Standalone(text, start, end, afterEnglish)) &&
                     !IsLowercaseJapaneseProperNoun(span))
            {
                cost = Memory?.Get(span.ToLowerInvariant()) == true ? LearnedEnglishCost
                    : ReadableEnglish.Value.ContainsWord(span.ToLowerInvariant()) ? ReadableEnglishCost : KnownEnglishCost;
                if (kanaInput && span.Length >= 4) cost += KanaInputEnglishBias;
                // 日本語由来の語 (suzuki・sushi) は、ローマ字で読んだかなも日本語の語として並べる
                if (span.All(char.IsAsciiLetterLower) && IsJapaneseOriginWord(span) &&
                    _romaji.ConvertLenient(span, final: true) is var reading && reading.All(KanaChars.IsHiraganaOrLongMark))
                    edges.Add((new LatticeEdge(start, end, false, reading, true, false), JapaneseWordCost));
            }
            else if (Memory?.Get(span.ToLowerInvariant()) == false)
            {
                // かなに直して覚えた語 (google を ごおgぇ に) は、日本語の語として読む
                edges.Add((new LatticeEdge(start, end, false, _romaji.ConvertLenient(span.ToLowerInvariant(), final: true), true, false), JapaneseWordCost));
                continue;
            }
            else if (!kanaInput && end == length && span.Length >= 4 && span.All(char.IsAsciiLetterLower) &&
                     unknown[Math.Max(start, Math.Min(end, _preparedRaw.Length))] > unknown[start] && _english.IsPrefix(span))
                // 入力の最後の、英単語の頭 (insta|ll)。入力途中の子音より前に、ローマ字で読めない英字があるときだけ (mala は まぁ、mits は みつ の途中)
                cost = EnglishPrefixCost;
            else if (!kanaInput && unknown[end] > unknown[start])
            {
                var name = span.Length >= 3 && char.IsAsciiLetterUpper(span[0]) && char.IsAsciiLetterLower(span[1]);
                cost = span.Sum(ch => char.IsAsciiLetterUpper(ch) ? UnknownUpperCost : name ? NameLetterCost : UnknownLetterCost);
                edges.Add((new LatticeEdge(start, end, true, "", false, false, Name: name), cost));
                continue;
            }
            else continue;
            edges.Add((new LatticeEdge(start, end, true, "", false, false), cost));
        }
        return edges;
    }

    /// <summary>英単語の書き方の大文字・小文字か (its・Its・ITS と、固有名詞の書き方 GitHub)。iTs は its の書き方ではない。</summary>
    private bool IsWordCasing(string span) =>
        !span.Skip(1).Any(char.IsAsciiLetterUpper) || span.All(char.IsAsciiLetterUpper) || _proper.Canonical(span.ToLowerInvariant()) == span;

    /// <summary>
    /// 小文字で打った、japanese.txt の語と同じ綴りの固有名詞か (wakatte は わかって、Wakatte.TV は固有名詞)。
    /// 英単語の辞書には固有名詞の小文字も入っているので、固有名詞で見分ける。
    /// </summary>
    private bool IsLowercaseJapaneseProperNoun(string span) =>
        span.All(char.IsAsciiLetterLower) && Memory?.Get(span) is null && _proper.Contains(span) &&
        _kana?.IsJapaneseWord(_romaji.ConvertLenient(span, final: true)) == true;

    /// <summary>
    /// [start, end) の前後が英字以外で区切られているか (1 文字の英単語 a・i は、区切ったときだけ語)。
    /// 入力の頭は、前が英語のときだけ区切りとして数える (a だけを打ったら あ)。
    /// </summary>
    private static bool Standalone(string text, int start, int end, bool afterEnglish) =>
        (start == 0 ? afterEnglish : !char.IsAsciiLetter(text[start - 1])) && (end == text.Length || !char.IsAsciiLetter(text[end]));

    /// <summary>
    /// 知っている英単語か (同梱の辞書・固有名詞・スペルチェッカー・english-readable.txt・-pedia・決まり文句・ユーザーが英字に直して覚えた語)。
    /// 1 文字は a・i と、チャットの略し方の u (you)・r (are) だけ。
    /// </summary>
    private bool IsLatticeEnglishWord(string lower)
    {
        if (Memory?.Get(lower) is { } learned) return learned;
        if (lower.Length == 1) return lower is "a" or "i" or "u" or "r";
        return _english.Words.ContainsWord(lower) || _proper.Contains(lower) || IsLatticeSpellWord(lower) || ReadableEnglish.Value.ContainsWord(lower) ||
            IsPediaEnglishWord(lower) || EnglishPhraseSpacing.IsPhrase(lower);
    }

    /// <summary>
    /// スペルチェッカーが知っている語のうち、同梱のよく使う英単語 (english-words.txt) にもある語。
    /// OS のスペルチェッカー (Windows) は kore・cy・gattaw も語にするので、OS のスペルチェッカーだけが知っている語は数えない。
    /// </summary>
    private bool IsLatticeSpellWord(string lower) => IsSpellWord(lower) && Detection.BuiltInWordChecker.Shared.IsWord(lower);

    /// <summary>選んだ候補の並びを、英語・日本語の区間の並びにする (続いた日本語の候補は 1 つの区間にする。英語の候補は 1 つずつの区間)。</summary>
    private List<CompositionSegment> ToSegments(IReadOnlyList<CompositionUnit> units, string text, int[] unitAt, int unitsEnd, List<LatticeEdge> edges)
    {
        var segments = new List<CompositionSegment>();
        foreach (var edge in edges)
        {
            var raw = text[edge.Start..edge.End];
            var kana = edge.English ? "" : EdgeKana(units, text, unitAt, unitsEnd, edge);
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
    private string EdgeKana(IReadOnlyList<CompositionUnit> units, string text, int[] unitAt, int unitsEnd, LatticeEdge edge)
    {
        var end = Math.Min(edge.End, unitsEnd);
        if (edge.Start >= end) return "";
        if (unitAt[edge.Start] >= 0 && unitAt[end] >= 0) return Kana(units, unitAt[edge.Start], unitAt[end]);
        if (edge.End <= unitsEnd) return edge.Kana;
        return _romaji.ConvertLenient(text[edge.Start..end].ToLowerInvariant(), final: false).Replace('-', 'ー');
    }
}
