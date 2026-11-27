// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Libraries.Chars;
using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>未確定の入力の英日の区切り (表示と確定・変換で同じ区切り) と、保護区間 (メンション・URL・パス)。</summary>
internal static partial class CompositionText
{
    /// <summary>自動判定の区間分け (Mode が Auto のときに使う)。</summary>
    /// 入力中の表示と、Space・Enter で確定・変換するときで同じ区切りになる。
    public static IReadOnlyList<CompositionSegment> Segments(TextReading state, TextEnv env)
    {
        // 1 キーごとに、表示・打ち間違いの直し・Space の扱いなどで何度も呼ばれる。区間分けは重い (打った文字のあらゆる区間を調べる) ので、
        // 打った内容と判定の条件が同じなら前の結果を使い回す。
        var key = CompositionText.SegmentsKey(state, env);
        var cache = CompositionText.SegmentsCacheOf(state);
        return CompositionText.CachedSegments(cache, key, env) is { } cached ? cached.Segments : CompositionText.ComputeSegmentsKept(state, env, key, cache).Segments;
    }

    private static SegmentsReading? CachedSegments(Recent<string, SegmentsReading>? cache, string key, TextEnv env) =>
        cache is not null && cache.TryGetValue(key, out var cached) && ReferenceEquals(cached.Memory, LearnedMemory(env)) ? cached : null;

    private static SegmentsReading ComputeSegmentsKept(TextReading state, TextEnv env, string key, Recent<string, SegmentsReading>? cache)
    {
        // 区切りを選んだ理由も、区切りと同じ計算から取る (確定のログが、実際に確定した区切りと食い違わないように。issue #1644)
        var reasons = new List<Func<string>>();
        var reading = new SegmentsReading(LearnedMemory(env), CompositionText.Adjusted(state, env, CompositionText.ComputeSegments(state, env, reasons)[0], reasons), reasons);
        cache?.Set(key, reading);
        return reading;
    }

    /// <summary>区間分けに、設定の区間の直し (全角の括弧) をかける。直したら理由を reasons に足す。</summary>
    private static IReadOnlyList<CompositionSegment> Adjusted(TextReading state, TextEnv env, IReadOnlyList<CompositionSegment> result, List<Func<string>> reasons)
    {
        if (state.FullWidthParentheses && result.Any(s => s.IsEnglish && s.Raw is "(" or ")"))
        {
            result = [.. result.Select(s => s.IsEnglish && s.Raw is "(" or ")" ? new CompositionSegment(false, s.Raw == "(" ? "（" : "）", s.Raw) : s)];
            reasons.Add(() => "設定「カッコを全角で入力」で ( ) を全角に");
        }
        return result;
    }

    /// <summary>
    /// 変換用の区間分けと、その英日の区切りの別案 (軽い順に count 案まで。先頭は <see cref="ConversionSegments"/>)。
    /// 案は <see cref="Segments"/> と同じ順番で作る (<see cref="ComputeSegments"/> → <see cref="Adjusted"/>)。
    /// 別案の区切りの理由は確定のログに書かない (確定のログは <see cref="Segments"/> の理由)。
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<CompositionSegment>> ConversionSegmentAlternatives(TextReading state, TextEnv env, int count)
    {
        var alternatives = new List<IReadOnlyList<CompositionSegment>>();
        foreach (var segments in CompositionText.ComputeSegments(state, env, [], count).Select(a => CompositionText.WithFinalPending(state, env, CompositionText.Adjusted(state, env, a, []))))
            if (!alternatives.Any(a => a.SequenceEqual(segments))) alternatives.Add(segments);
        return alternatives;
    }

    /// <summary>打った英字をすべてローマ字として読んだ、日本語の 1 区間の区間分け (botto → ぼっと)。ローマ字として読めない英字は残る (google → ごおgぇ)。</summary>
    public static IReadOnlyList<CompositionSegment> AllJapaneseSegments(TextReading state, TextEnv env) => [new CompositionSegment(false, CompositionText.AllKana(state, env, final: true), CompositionText.Raw(state))];

    /// <summary>今の区切り (Segments) を選んだ理由。確定のログに書く (issue #1644)。</summary>
    public static string ExplainSegments(TextReading state, TextEnv env)
    {
        var key = CompositionText.SegmentsKey(state, env);
        var cache = CompositionText.SegmentsCacheOf(state);
        var reasons = CompositionText.CachedSegments(cache, key, env)?.Reasons ?? CompositionText.ComputeSegmentsKept(state, env, key, cache).Reasons;
        return string.Join("; ", reasons.Select(r => r()));
    }

    /// <summary>区間分けの結果を決めるもの (打った単位・入力途中の子音・前後・自動判定を使うか・学習した語の版) をつないだもの。</summary>
    private static string SegmentsKey(TextReading state, TextEnv env)
    {
        var key = new StringBuilder();
        foreach (var unit in state.Units) key.Append(unit.Kana).Append('\u0001').Append(unit.Raw).Append('\u0002');
        key.Append('\u0003').Append(state.Pending).Append('\u0003')
            .Append(env.Context().PrecedingEnglish switch { true => 'E', false => 'J', null => '-' })
            .Append(env.Context().FollowingEnglish switch { true => 'E', false => 'J', null => '-' })
            .Append(env.Context().PrecedingEnglishSentence ? 'S' : '-').Append(env.Context().EndsWithEnglishAndSpace ? 'P' : '-').Append(state.KanaInput ? 'K' : '-')
            .Append(env.Context().PrecedingEnglishName ? 'N' : '-').Append(state.FullWidthParentheses ? 'W' : '-')
            .Append(CompositionText.EffectiveManual(state, env) ? 'M' : '-').Append(':').Append(LearnedMemory(env)?.Version ?? -1).Append(':').Append(LearningContext(env) switch { true => 'e', false => 'j', null => '-' });
        return key.ToString();
    }

    /// <summary>
    /// 名前を紹介する英文の名前か。辞書にないローマ字の名前も英字で残す。
    /// 手動・かな入力・明示的な日本語学習・後ろの日本語は優先する。
    /// </summary>
    private static bool IsNameInEnglishSentence(TextReading state, TextEnv env) =>
        !state.KanaInput && env.Context().PrecedingEnglishName && env.Context().FollowingEnglish != false && !CompositionText.EffectiveManual(state, env) &&
        CompositionText.Raw(state) is { Length: > 0 } raw && raw.All(char.IsAsciiLetter) && LearnedLanguages(env).Get(raw.ToLowerInvariant()) != false;

    /// <summary>
    /// 区間分けと、その英日の区切りの別案 (軽い順に count 案まで。先頭が表示・確定に使う区切り)。reasons には先頭の区切りの理由を足す。
    /// 別案は <see cref="CompositionDetector.SegmentAlternatives"/> の案。
    /// 英文の中の名前・保護区間・半角の開き括弧・手動のように、ラティスの外の決まりで区切ったときは別案を出さない
    /// (決まりで決めた区切りを、ラティスの案で崩さない)。
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<CompositionSegment>> ComputeSegments(TextReading state, TextEnv env, List<Func<string>> reasons, int count = 1)
    {
        if (CompositionText.IsNameInEnglishSentence(state, env))
        {
            reasons.Add(() => "名前を紹介する英文の続きなので英字のまま");
            return [[new CompositionSegment(true, "", CompositionText.Raw(state))]];
        }
        var alternatives = CompositionText.SegmentWithProtection(state, env, TypedMarks.Read(state, env), reasons, CompositionText.EffectiveManual(state, env) ? 1 : count);
        if (CompositionText.HalfWidthOpeners(state, alternatives[0]) is { Count: > 0 } halfWidth)
        {
            // 区切り直した結果だけが確定するので、最初の区切りの理由は捨てる
            reasons.Clear();
            reasons.Add(() => "後ろが英語なので開きの記号を半角にして区切り直した");
            return CompositionText.SegmentWithProtection(state, env, TypedMarks.Read(state, env, halfWidth), reasons, 1);
        }
        return alternatives;
    }

    /// <summary>英単語のすぐ後ろの , . を句読点にするか (設定「英字の後ろの , . も句読点にする」)。キャレットの前が空白で終わる英語なら英文の続きなので半角のまま。</summary>
    private static bool PunctuatesAfterEnglish(TextEnv env) => env.Settings.JapanesePunctuationAfterEnglish && !env.Context().EndsWithEnglishAndSpace;

    /// <summary>保護区間を英字のまま区切る。保護区間があれば別案は出さない (1 案だけ)。</summary>
    private static IReadOnlyList<IReadOnlyList<CompositionSegment>> SegmentWithProtection(TextReading state, TextEnv env, IReadOnlyList<CompositionUnit> units, List<Func<string>> reasons, int count)
    {
        var spans = CompositionText.ProtectedSpans(state);
        if (spans.Count == 0)
            return env.Detector.SegmentAlternatives(LearnedLanguages(env), units, CompositionText.Pending(state), env.Context().PrecedingEnglish, env.Context().FollowingEnglish, CompositionText.EffectiveManual(state, env), env.Context().PrecedingEnglishSentence, state.KanaInput, CompositionText.PunctuatesAfterEnglish(env), count, reasons);
        var raw = CompositionText.Raw(state);
        var result = new List<CompositionSegment>();
        var start = 0;
        foreach (var span in spans)
        {
            AddGap(start, span.Start);
            result.Add(new CompositionSegment(true, "", raw.Substring(span.Start, span.Length)) { IsProtected = true });
            var length = span.Length;
            reasons.Add(() => $"保護区間 ({length} 文字。メンション・URL・メール・パス) は英字のまま");
            start = span.End;
        }
        AddGap(start, raw.Length);
        return [result];

        void AddGap(int from, int to)
        {
            if (from == to) return;
            var gapUnits = CompositionText.UnitsInRawRange(state, env, units, from, to, out var pending);
            var segments = env.Detector.Segment(LearnedLanguages(env), gapUnits, pending, from == 0 ? env.Context().PrecedingEnglish : false,
                to == raw.Length ? env.Context().FollowingEnglish : false, CompositionText.EffectiveManual(state, env),
                from == 0 && env.Context().PrecedingEnglishSentence, state.KanaInput, CompositionText.PunctuatesAfterEnglish(env), reasons);
            // 内部の区切りの子音は後の保護文字列とは結合しない。末尾の Pending は表示側が扱う。
            if (to < raw.Length && pending.Length > 0 && !segments[^1].IsEnglish)
                segments = [.. segments.Take(segments.Count - 1), segments[^1] with
                    { Kana = segments[^1].Kana + env.Detector.Romaji.ConvertLenient(pending.ToLowerInvariant(), final: true) }];
            result.AddRange(segments);
        }
    }

    private static List<CompositionUnit> UnitsInRawRange(TextReading state, TextEnv env, IReadOnlyList<CompositionUnit> units, int from, int to, out string pending)
    {
        var selected = new List<CompositionUnit>();
        var offset = 0;
        var partial = false;
        foreach (var unit in units)
        {
            var end = offset + unit.Raw.Length;
            if (offset < to && end > from)
            {
                partial |= offset < from || end > to;
                selected.Add(unit);
            }
            offset = end;
        }
        if (partial)
        {
            // 構造の境界がローマ字単位の途中に入った場合だけ、その区間の原文を読み直す。
            var fragment = CompositionText.Fragment(CompositionText.Raw(state)[from..to], env);
            var fragmentEnv = FragmentEnv(env);
            pending = CompositionText.Pending(fragment);
            return [.. TypedMarks.Read(fragment, fragmentEnv)];
        }
        pending = to > offset ? CompositionText.Raw(state)[Math.Max(from, offset)..to] : "";
        return selected;
    }

    /// <summary>英数字以外の半角の記号 1 文字か (! . , ? など)。</summary>
    private static bool IsAsciiSymbol(string raw) => raw is [var c] && c is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(c);

    /// <summary>開きの記号と、その閉じの記号。</summary>
    // [ ] は日本語の入力では「」なので、英語の前後でも半角にしない
    private static readonly Dictionary<string, string> Openers = new() { ["("] = ")", ["{"] = "}", ["\""] = "\"", ["'"] = "'", ["`"] = "`" };

    /// <summary>
    /// 開きの記号 (「(」「"」) は打った時点ではまだ後ろが分からないので全角になる。後ろが分かったら、
    /// すぐ後ろが英語 ((Ooh) か、対になる閉じの記号が半角 ("…Go Through!") なら、開きの記号も半角にする。半角にする単位の番号を返す。
    /// ("打ち上げ話はGo Through!" の最初の " だけ全角 ” になっていた、(Ooh … wow…) の ( だけ全角 （ になっていた)
    /// </summary>
    private static HashSet<int> HalfWidthOpeners(TextReading state, IReadOnlyList<CompositionSegment> segments)
    {
        if (state.KanaInput || !state.Units.Any(u => Openers.ContainsKey(u.Raw) && u.Kana != u.Raw)) return [];
        // 打った英字の 1 文字ずつが英語の区間か
        var english = new List<bool>();
        foreach (var segment in segments) foreach (var _ in segment.Raw) english.Add(segment.IsEnglish);
        var offsets = new int[state.Units.Count];
        for (int i = 0, offset = 0; i < state.Units.Count; offset += state.Units[i].Raw.Length, i++) offsets[i] = offset;
        bool IsHalf(int i) => state.Units[i].Kana == state.Units[i].Raw || (offsets[i] < english.Count && english[offsets[i]]);

        var halfWidth = new HashSet<int>();
        for (var i = 0; i < state.Units.Count; i++)
        {
            if (!Openers.TryGetValue(state.Units[i].Raw, out var closer) || state.Units[i].Kana == state.Units[i].Raw) continue;
            if (state.FullWidthParentheses && state.Units[i].Raw == "(") continue; // 設定「カッコを全角で入力」: 英語のそばでも全角のまま
            var nextIsEnglish = i + 1 < state.Units.Count ? offsets[i + 1] < english.Count && english[offsets[i + 1]] && char.IsAsciiLetter(state.Units[i + 1].Raw[0])
                : CompositionText.Pending(state).Length > 0 && segments.Count > 0 && segments[^1].IsEnglish;
            var closing = Enumerable.Range(i + 1, state.Units.Count - i - 1).FirstOrDefault(k => state.Units[k].Raw == closer, -1);
            // 閉じの記号が英語の語のすぐ後ろ (間の記号 ! . は飛ばす: …Through!") なら、閉じも、間の記号も半角にする
            var afterEnglish = -1;
            if (closing > i)
            {
                var k = closing - 1;
                while (k > i && IsAsciiSymbol(state.Units[k].Raw)) k--;
                if (k > i && offsets[k] < english.Count && english[offsets[k]] && char.IsAsciiLetter(state.Units[k].Raw[^1])) afterEnglish = k;
            }
            // まだ閉じていない開きの記号の後ろに英語の語がある ("打ち上げ話はGo + Space で確定するとき) も半角 (英語の文を続けて打っている)
            var unclosedBeforeEnglish = closing < 0 && Enumerable.Range(i + 1, state.Units.Count - i - 1)
                .Any(k => offsets[k] < english.Count && english[offsets[k]] && char.IsAsciiLetter(state.Units[k].Raw[0]));
            if (!nextIsEnglish && !(closing >= 0 && IsHalf(closing)) && afterEnglish < 0 && !unclosedBeforeEnglish) continue;
            halfWidth.Add(i);
            for (var k = afterEnglish + 1; afterEnglish >= 0 && k <= closing; k++) halfWidth.Add(k);
        }
        return halfWidth;
    }

    /// <summary>自動判定を使わないとき、自動判定なら英字にする部分 (提案)。無ければ null。</summary>
    public static string? Suggestion(TextReading state, TextEnv env)
    {
        if (state.Mode != DisplayMode.Auto || !CompositionText.EffectiveManual(state, env) || CompositionText.IsNumeric(state)) return null;
        var suggested = env.Detector.Segment(LearnedLanguages(env), TypedMarks.Read(state, env), CompositionText.Pending(state), env.Context().PrecedingEnglish, env.Context().FollowingEnglish, false, env.Context().PrecedingEnglishSentence, state.KanaInput, CompositionText.PunctuatesAfterEnglish(env));
        if (!suggested.Any(s => s.IsEnglish) || suggested.SequenceEqual(CompositionText.Segments(state, env))) return null;
        return string.Join(" ", TypedSpans(suggested, env).Where(s => s.Kind != SpanKind.Japanese).Select(s => s.Text));
    }

    /// <summary>変換用の区間分け。末尾の入力途中の子音は確定扱い (n → ん) にして日本語区間の読みに含める。</summary>
    public static IReadOnlyList<CompositionSegment> ConversionSegments(TextReading state, TextEnv env) => CompositionText.WithFinalPending(state, env, CompositionText.Segments(state, env));

    private static List<CompositionSegment> WithFinalPending(TextReading state, TextEnv env, IReadOnlyList<CompositionSegment> segments)
    {
        var result = segments.ToList();
        if (result.Count > 0 && !result[^1].IsEnglish)
        {
            result[^1] = result[^1] with { Kana = result[^1].Kana + CompositionText.PendingText(state, env, result[^1], final: true) };
        }
        return result;
    }

    /// <summary>今の原文の保護区間 (原文が変わったら再計算する)。</summary>
    public static IReadOnlyList<ProtectedSpan> ProtectedSpans(TextReading state) =>
        CompositionText.ProtectedSpans(state.KanaInput, CompositionText.Raw(state), state.Units is [{ Kana: "・", Raw: "/" }, ..]);

    /// <summary>打った文字 (打ち間違いを直す前) の保護区間。</summary>
    public static IReadOnlyList<ProtectedSpan> ProtectedSpans(CompositionTextState state) =>
        CompositionText.ProtectedSpans(state.KanaInput, state.TypedRaw, state.SlashAtStart && state.Keys is [TypedKey.Letter { Char: '/' }, ..]);

    private static IReadOnlyList<ProtectedSpan> ProtectedSpans(bool kanaInput, string raw, bool slashAtStart)
    {
        // かな入力では "@" は濁点などのキーで、ローマ字の原文とは意味が違う。保護判定はローマ字入力だけにする。
        if (kanaInput) return [];
        // 打ち始めの / を ・ にしたときは、続けて打った文字と合わせて / から始まるパス (/kyou) にしない。
        return slashAtStart ? ProtectedSpanScanner.Scan(raw[1..]).Select(s => s with { Start = s.Start + 1 }).ToList() : ProtectedSpanScanner.Scan(raw);
    }

    /// <summary>原文全体が 1 つの保護区間か (自動変換・幅変換・誤字補正の対象外)。</summary>
    public static bool IsProtectedRaw(TextReading state) => !CompositionText.IsEmpty(state) && CompositionText.VerifyProtectedRaw(state);

    private static bool VerifyProtectedRaw(TextReading state)
    {
        var spans = CompositionText.ProtectedSpans(state);
        return !CompositionText.IsEmpty(state) && spans.Count == 1 && spans[0].Start == 0 && spans[0].End == CompositionText.Raw(state).Length;
    }

    /// <summary>原文の末尾が保護区間で終わっているか (Space/Enter/Tab/確定で原文のまま確定する)。</summary>
    public static bool HasProtectedTail(TextReading state) => CompositionText.ProtectedSpans(state) is [.., var last] && last.End == CompositionText.Raw(state).Length;

    public static bool HasProtectedTail(CompositionTextState state) => CompositionText.ProtectedSpans(state) is [.., var last] && last.End == state.TypedRaw.Length;
}
