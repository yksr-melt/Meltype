// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// 区切りを点数で選ぶ (α版。設定「区切りを点数で選ぶ (α版)」、既定 OFF)。
///
/// 今までの FindSpans は、先頭から見て最初に「英語と言える」区間を見つけたら、そこで区切りを決めてしまう。
/// 後ろにもっと良い区切りがあっても戻れない (some|teal|coholic と alcoholic、reflect|sa の tsa など)。
/// ここでは、あり得る区切り方を全部並べて、全体の点が一番高いものを選ぶ (変換エンジンと同じ考え方)。
///
/// 英語の区間になれるかどうかは、今までと同じ IsEnglishSpan で決める (決まりはそのまま使う)。
/// 点数で決めるのは「どの英語の区間を取るか」だけ:
///   - 知っている英単語・決まり文句の区間は、長さの 2 乗の点 (長い 1 語を、短い語の組より優先する: mo|string、alcoholic)
///   - 辞書に無い英字の並びは、長さに比例する低い点 (前の日本語を巻き込まない: ぜんぶ|glowers を zen|buglowers にしない)
///   - 英語の区間を置くたびに少し減点 (細切れにしない)。英字どうしがくっついた 2 語 (cup|sha) はもっと減点
///   - 日本語の中に、ローマ字として読めずに英字のまま残る文字があれば減点
///   - 英単語の最後の子音と助詞の は が 1 つの単位 (sha・tha・dha) になっていても、英単語 + は に分けられる (medals|は、president|は)
/// URL のドメイン・英文の中の記号・ユーザー名・大文字で始まる語などの、はっきりした決まりは今までどおり先に決める。
/// 点の重みは環境変数 (MELTYPE_SCORED_*) で変えられる (--mixed-bench・品質テストで比べるため)。
/// </summary>
public sealed partial class CompositionDetector
{
    /// <summary>知っている英単語の区間の点の係数 (長さの LengthExponent 乗に掛ける)。</summary>
    internal static double EnglishCharScore = 1.0;
    /// <summary>辞書に無い英字の並び (buglowers) の 1 文字あたりの点。知っている英単語 (glowers) より低くして、前の日本語を巻き込まないようにする。</summary>
    internal static double UnknownCharScore = Weight("UNKNOWN", 0.5);
    /// <summary>英語の区間の長さを何乗して点にするか (1 より大きいと、長い 1 語を短い 2 語より優先する)。</summary>
    internal static double LengthExponent = Weight("EXP", 2.0);
    /// <summary>英語の区間を 1 つ置くたびの減点 (日本語との切り替え)。</summary>
    internal static double SwitchPenalty = 0.5;
    /// <summary>英語の区間のすぐ後ろに英語の区間を続けるときの減点 (英字の並びを 2 語に分ける)。</summary>
    internal static double AdjacentPenalty = Weight("ADJ", 4.0);
    /// <summary>日本語の区間に、読めない英字が 1 文字残るたびの減点。</summary>
    internal static double UnreadablePenalty = Weight("UNREADABLE", 1.0);

    /// <summary>点の重み。環境変数 MELTYPE_SCORED_&lt;name&gt; があればその値 (重みを変えて比べるとき)。</summary>
    private static double Weight(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable("MELTYPE_SCORED_" + name), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    /// <summary>ここまでの区切り方 (点・英語の区間・最後の日本語の区間の始まり)。</summary>
    /// <param name="English">英語の区間。Split なら、End の単位の 1 文字目までを英語にして、残りを日本語にする (medal|sha → medals|ha)。</param>
    private sealed record ScoredPath(double Score, List<(int Start, int End, bool Split)> English, int JapaneseStart);

    private List<CompositionSegment> FindSpansScored(IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish, bool? followingEnglish,
        DetectionLevel level, bool englishSentence, bool final) =>
        MergeJapanese(FindSpansScoredCore(units, pending, precedingEnglish, followingEnglish, level, englishSentence, final));

    /// <summary>続いた日本語の区間 (単位の途中で区切った残り + 次の日本語) を 1 つにする。</summary>
    private static List<CompositionSegment> MergeJapanese(List<CompositionSegment> segments)
    {
        var merged = new List<CompositionSegment>(segments.Count);
        foreach (var segment in segments)
        {
            if (!segment.IsEnglish && merged.Count > 0 && !merged[^1].IsEnglish)
                merged[^1] = new CompositionSegment(false, merged[^1].Kana + segment.Kana, merged[^1].Raw + segment.Raw);
            else merged.Add(segment);
        }
        return merged;
    }

    private List<CompositionSegment> FindSpansScoredCore(IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish, bool? followingEnglish,
        DetectionLevel level, bool englishSentence, bool final)
    {
        var n = units.Count;
        // best[i, e]: 位置 i まで区切ったときの一番良い区切り方。e は「直前の区間が i で終わる英語の区間か」。
        var best = new ScoredPath?[n + 1, 2];
        best[0, 0] = new ScoredPath(0, [], 0);

        void Offer(int position, bool english, ScoredPath path)
        {
            var e = english ? 1 : 0;
            // 同点なら先に見つけたもの (今までの FindSpans と同じく、前から始まる区間) を残す
            if (best[position, e] is { } current && current.Score >= path.Score) return;
            best[position, e] = path;
        }

        for (var i = 0; i < n; i++)
        {
            for (var e = 0; e < 2; e++)
            {
                if (best[i, e] is not { } path) continue;
                // 区間の並びは、記号・数字の決まりを見るときだけ作る (毎回作ると長い入力で遅い)
                List<CompositionSegment>? segments = null;
                List<CompositionSegment> Segments() => segments ??= ToSegments(units, path);
                bool? PrecededByEnglish(int start) => start == 0 ? precedingEnglish : e == 1;
                var before = i == 0 && englishSentence ? 2 : Score(PrecededByEnglish(i));

                // はっきりした決まり (今までの FindSpans と同じ順) に当たれば、その区間だけを取る
                var forced = -1;
                if (level != DetectionLevel.Manual && i > 0 && units[i - 1].Raw == "." && PrecededByEnglish(i) == true) forced = DotSuffixEnd(units, i, pending, final);
                if (forced < 0 && level != DetectionLevel.Manual) forced = ReadableStemDotSuffixEnd(units, i, pending, final, level);
                if (forced < 0) forced = LeadingDotfileEnd(units, i, pending, final, level);
                if (IsAsciiSymbol(units[i]) && PrecededByEnglish(i) == true && Segments().All(s => s.IsEnglish || !s.Raw.Any(char.IsAsciiLetter)) &&
                    KeepsPunctuationAfterEnglish(units, i, pending, precedingEnglish)) forced = i + 1;
                if (forced < 0) forced = UserNameEnd(units, i, pending);
                if (forced < 0 && level != DetectionLevel.Manual) forced = CapitalizedWordEnd(units, i, pending, final);
                if (forced < 0 && level != DetectionLevel.Manual) forced = HyphenatedWordEnd(units, i, pending);
                if (forced < 0 && level != DetectionLevel.Manual) forced = NumeronymEnd(units, i, pending);
                if (forced < 0 && level != DetectionLevel.Manual && (i > 0 && units[i - 1].Raw is [var digit] && char.IsAsciiDigit(digit) ? AlphanumericSuffixEnd(units, i, pending, Segments(), path.JapaneseStart) : -1) is var suffix and > 0) forced = suffix;
                if (forced < 0 && level != DetectionLevel.Manual) forced = EnglishBeforeParticleAndEnglishTail(units, i, pending, LongestWord());
                if (forced > i)
                {
                    Offer(forced, true, Extend(path, i, forced, units, pending));
                    continue;
                }

                // 日本語として 1 単位進む
                var unreadable = HasUnreadable(units, i, i + 1) ? UnreadablePenalty : 0;
                Offer(i + 1, false, path with { Score = path.Score - unreadable });

                // 英語の区間として i から j まで取る (IsEnglishSpan が英語と言える区間だけ)
                for (var j = n; j > i; j--)
                {
                    var symbolsAfter = i == 0 && j < n && pending.Length == 0 && Enumerable.Range(j, n - j).All(k => IsAsciiSymbol(units[k]));
                    var after = j == n || symbolsAfter ? followingEnglish : false;
                    if (!CanBeEnglishSpan(i, j, j == n ? pending : "", j == n && !final, j < n ? units[j].Raw : null)) continue;
                    if (PrecededByEnglish(i) == true && IsSuruForm(Kana(units, i, j))) continue;
                    if (!IsEnglishSpan(Raw(units, i, j) + (j == n ? pending : ""), atEnd: j == n, before, after, startOfInput: i == 0, level, final, endsWord: symbolsAfter,
                            unreadable: HasUnreadable(units, i, j) || EndsWithLoneSokuon(units, j), next: j < n ? units[j].Raw + (j + 1 == n ? pending : "") : null)) continue;
                    Offer(j, true, Extend(path, i, j, units, pending));
                }

                // 単位の途中で区切る: 英単語の最後の子音と、続く日本語の頭が 1 つの単位になっているとき (medals + ha が medal + sha (しゃ) になる)。
                // 単位 j の 1 文字目までを英語、残り (ha = 助詞の は) を日本語にする。
                // 残りが は のときだけ (de を d|e、su を s|u にすると、sored|e・gas|uki のように普通の日本語を壊す)
                for (var j = n - 1; j > i; j--)
                {
                    var unit = units[j].Raw;
                    if (unit.Length != 3 || !char.IsAsciiLetterLower(unit[0]) || "aiueon".Contains(unit[0]) || unit[1..] != "ha") continue;
                    var rest = unit[1..];
                    // その単位を含むよく使う日本語の語があれば区切らない (densha = でんしゃ、shabushabu = しゃぶしゃぶ。dens|は・bus|は にしない)
                    if (CoveredByJapaneseWord(units, j) || IsCommonJapanese?.Invoke(Raw(units, i, j + 1).ToLowerInvariant()) == true) continue;
                    var word = Raw(units, i, j) + unit[0];
                    if (PrecededByEnglish(i) == true && IsSuruForm(Kana(units, i, j))) continue;
                    if (!IsKnownEnglishWord(word) || !IsEnglishSpan(word, atEnd: false, before, false, startOfInput: i == 0, level, final,
                            unreadable: true, next: rest)) continue;
                    Offer(j + 1, false, Extend(path, i, j, units, pending, split: true));
                }
            }
        }

        // 末尾まで英語で終わる区切りと、日本語で終わる区切りのうち、点の高いほう
        var english = best[n, 1];
        var japanese = best[n, 0];
        var chosen = english is not null && (japanese is null || english.Score >= japanese.Score) ? english : japanese!;
        var result = ToSegments(units, chosen);
        if (chosen == english && n > 0)
        {
            // 末尾の英語の区間に、打ちかけの子音を付ける
            var last = result[^1];
            result[^1] = last with { Raw = last.Raw + pending };
            return result;
        }
        // 以下は今までの FindSpans の終わりと同じ
        if (n == 0 && pending.Length == 1 && char.IsAsciiLetterLower(pending[0]) &&
            IsEnglishSpan(pending, atEnd: true, englishSentence ? 2 : Score(precedingEnglish), followingEnglish, startOfInput: true, level, final))
        {
            return [new CompositionSegment(true, "", pending)];
        }
        if (pending.Length > 0 && char.IsAsciiLetterUpper(pending[0]))
        {
            if (chosen.JapaneseStart < n) result.Add(Japanese(units, chosen.JapaneseStart, n, ""));
            result.Add(new CompositionSegment(true, "", pending));
            return result;
        }
        if (chosen.JapaneseStart < n || pending.Length > 0 || result.Count == 0) result.Add(Japanese(units, chosen.JapaneseStart, n, pending));
        return result;
    }

    /// <summary>単位 unit を含む、3 文字以上のよく使う日本語の読み (readings.txt) があるか。</summary>
    private bool CoveredByJapaneseWord(IReadOnlyList<CompositionUnit> units, int unit)
    {
        // 読みは長くても十数文字なので、前後 8 単位まで見れば足りる
        for (var start = Math.Max(0, unit - 8); start <= unit; start++)
            for (var end = unit + 1; end <= Math.Min(units.Count, unit + 9); end++)
                if (Kana(units, start, end) is { Length: >= 3 } kana && Readings.Value.Contains(kana)) return true;
        return false;
    }

    private ScoredPath Extend(ScoredPath path, int start, int end, IReadOnlyList<CompositionUnit> units, string pending, bool split = false)
    {
        var raw = split ? Raw(units, start, end) + units[end].Raw[0] : Raw(units, start, end) + (end == units.Count ? pending : "");
        // すぐ前も英語の区間 (cup|sha、most|ring) なら、切り替えの減点の代わりに、語を 2 つに分けた減点 (AdjacentPenalty)。
        // 短い 2 語の合計 (3² + 3²) が、正しい区切りの 1 語 (cups|は = 4²、mo|string = 6²) より高くならないようにする
        // (空白・記号をはさむ (Let's Think wow...) のは語の自然な区切りなので、英字どうしがくっついているときだけ)
        var adjacent = path.English is [.., (_, var lastEnd, false)] && lastEnd == start && path.JapaneseStart == start &&
            start > 0 && units[start - 1].Raw is [.., var before] && char.IsAsciiLetter(before) && raw is [var first, ..] && char.IsAsciiLetter(first);
        // 知っている英単語・決まり文句 (大文字・小文字は問わない) と、記号・数字の入った区間 (v1.2、node.js) は、長さの a 乗の点:
        // 長い 1 語を、短い 2 語 (most + ring) より高くする (some|teal|coholic より alcoholic)。
        // 辞書に無い英字の並び (buglowers、macOSnoupdate) は長さに比例するだけ (長いほど得にならないので、前後の日本語や知っている語を巻き込まない)。
        var letters = raw.All(char.IsAsciiLetter);
        var lower = raw.ToLowerInvariant();
        var known = !letters || IsKnownEnglishWord(lower) || IsListedEnglishWord(lower) || EnglishPhraseSpacing.IsPhrase(lower);
        var score = known ? EnglishCharScore * Math.Pow(raw.Length, LengthExponent) : UnknownCharScore * raw.Length;
        return new ScoredPath(path.Score + score - (adjacent ? AdjacentPenalty : SwitchPenalty), [.. path.English, (start, end, split)], split ? end + 1 : end);
    }

    /// <summary>区切り方を、最後の英語の区間までの区間の並びにする (その後ろの日本語と、打ちかけの子音は付けない)。</summary>
    private List<CompositionSegment> ToSegments(IReadOnlyList<CompositionUnit> units, ScoredPath path)
    {
        var segments = new List<CompositionSegment>();
        var position = 0;
        foreach (var (start, end, split) in path.English)
        {
            if (start > position) segments.Add(Japanese(units, position, start, ""));
            if (!split)
            {
                segments.Add(new CompositionSegment(true, "", Raw(units, start, end)));
                position = end;
                continue;
            }
            var unit = units[end].Raw;
            segments.Add(new CompositionSegment(true, "", Raw(units, start, end) + unit[0]));
            segments.Add(new CompositionSegment(false, _romaji.ConvertLenient(unit[1..], final: true), unit[1..]));
            position = end + 1;
        }
        return segments;
    }
}
