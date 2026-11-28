// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Libraries.Chars;
using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>未確定の入力を、表示する文字・変換に渡す読み・打った英字に直す。</summary>
internal static partial class CompositionText
{
    /// <summary>
    /// 変換用の区間分け segments (<see cref="ConversionSegmentAlternatives"/> の 1 案) の segmentIndex 番目 (日本語の区間) の読みのうち [start, start + length) に対応する、打った英字
    /// (あぴ → api)。変換の候補に「打ったままの英字」を出すのに使う。読みの区切りが 1 音の区切りと合わないときや、かな入力では null。
    /// </summary>
    public static string? RawForReading(TextReading state, TextEnv env, IReadOnlyList<CompositionSegment> segments, int segmentIndex, int start, int length)
    {
        if (state.KanaInput) return null;
        if (segmentIndex < 0 || segmentIndex >= segments.Count) return null;
        var from = segments.Take(segmentIndex).Sum(s => s.Raw.Length);
        var units = CompositionText.UnitsInRawRange(state, env, TypedMarks.Read(state, env), from, from + segments[segmentIndex].Raw.Length, out var pending);
        var pieces = new List<(string Kana, string Raw)>();
        foreach (var unit in units) pieces.Add((unit.Kana, unit.Raw));
        if (pending.Length > 0) pieces.Add((env.Detector.Romaji.ConvertLenient(pending.ToLowerInvariant(), final: true), pending));

        return RawInPieces(pieces, start, length);
    }

    private static string? RawInPieces(List<(string Kana, string Raw)> pieces, int start, int length)
    {
        var builder = new StringBuilder();
        var position = 0;
        foreach (var (kana, raw) in pieces)
        {
            var end = position + kana.Length;
            if (end > start && position < start + length)
            {
                // 1 音の途中で区切れているなら、打った英字に対応させられない。
                if (position < start || end > start + length) return null;
                builder.Append(raw);
            }
            position = end;
        }
        return builder.Length > 0 ? builder.ToString() : null;
    }

    /// <summary>
    /// 文全体のかなを kanas の順に区切ったときの、各区切りに対応する打った英字。区切りは <see cref="RawForReading"/> と同じく 1 音の途中に入ると null。
    /// かな入力のときと、kanas をつないだものが文全体のかな (<see cref="KanaForSegment"/> を全区間つないだもの) と一致しないときは、すべて null を返す。
    /// 変換中に文節の区切りを動かしたあとで、文節ごとの打った英字を取り直すのに使う。
    /// </summary>
    public static IReadOnlyList<string?> RawForKanas(TextReading state, TextEnv env, IReadOnlyList<string> kanas)
    {
        var none = new string?[kanas.Count];
        if (state.KanaInput) return none;
        var units = CompositionText.UnitsInRawRange(state, env, TypedMarks.Read(state, env), 0, CompositionText.Raw(state).Length, out var pending);
        var pieces = new List<(string Kana, string Raw)>();
        foreach (var unit in units) pieces.Add((unit.Kana, unit.Raw));
        if (pending.Length > 0) pieces.Add((env.Detector.Romaji.ConvertLenient(pending.ToLowerInvariant(), final: true), pending));
        if (string.Concat(pieces.Select(piece => piece.Kana)) != string.Concat(kanas)) return none;

        var raws = new string?[kanas.Count];
        var start = 0;
        for (var i = 0; i < kanas.Count; i++)
        {
            raws[i] = RawInPieces(pieces, start, kanas[i].Length);
            start += kanas[i].Length;
        }
        return raws;
    }

    /// <summary>
    /// 変換用の区間分け segments の segmentIndex 番目の区間を、打った英字をローマ字として読んだかな (thin → てぃん)。
    /// 英語の区間を日本語の文節として読み直すのに使う。ローマ字として読めない英字はそのまま残る。かな入力では打ったかな。
    /// </summary>
    public static string? KanaForSegment(TextReading state, TextEnv env, IReadOnlyList<CompositionSegment> segments, int segmentIndex)
    {
        if (segmentIndex < 0 || segmentIndex >= segments.Count) return null;
        var start = segments.Take(segmentIndex).Sum(segment => segment.Raw.Length);
        var end = start + segments[segmentIndex].Raw.Length;
        var unitsEnd = CompositionText.Raw(state).Length - state.Pending.Length;
        var builder = new StringBuilder();
        if (CompositionText.UnitIndexAt(state, start) is int first && CompositionText.UnitIndexAt(state, Math.Min(end, unitsEnd)) is int last)
        {
            foreach (var unit in TypedMarks.Read(state, env).Take(first..last)) builder.Append(unit.Kana);
            builder.Append(CompositionText.PendingText(state, env, start, end, final: true));
        }
        else
        {
            // ラティスが単位の途中で区切った (medals|ha の sha): 区間の打った英字を読み直す
            var fragment = CompositionText.Fragment(CompositionText.Raw(state)[start..end], env);
            var fragmentEnv = FragmentEnv(env);
            foreach (var unit in TypedMarks.Read(fragment, fragmentEnv)) builder.Append(unit.Kana);
            builder.Append(CompositionText.PendingText(fragment, fragmentEnv, final: true));
        }
        return builder.Length > 0 ? builder.ToString() : null;
    }

    /// <summary>打った英字の offset 文字目が単位の境目なら、そこから始まる単位の番号 (末尾なら単位の数)。</summary>
    private static int? UnitIndexAt(TextReading state, int offset)
    {
        var at = 0;
        for (var i = 0; at <= offset; i++)
        {
            if (at == offset) return i;
            if (i == state.Units.Count) break;
            at += state.Units[i].Raw.Length;
        }
        return null;
    }

    /// <summary><see cref="Compose"/> の文字。</summary>
    public static string Display(TextReading state, TextEnv env, bool final) => Compose(state, env, final).Text;

    /// <summary>変換していないときの変換ボックスの表示 (確定するときの文字にもする)。自動の表示は <see cref="Clauses"/> の並びを見せる。</summary>
    /// <param name="final">確定・変換のときは true (語末の n を ん にする)。</param>
    /// <param name="answer"><see cref="Clauses"/> の answer。</param>
    public static ComposedText Compose(TextReading state, TextEnv env, bool final, Func<string, bool, string, IReadOnlyList<TypingClause.Japanese>>? answer = null) => state.Mode switch
    {
        DisplayMode.HalfWidthAlphanumeric => ComposedText.English(ApplyCase(CompositionText.Raw(state), state.Case)),
        DisplayMode.FullWidthAlphanumeric => ComposedText.English(ToFullWidth(ApplyCase(CompositionText.Raw(state), state.Case))),
        DisplayMode.Hiragana => ComposedText.Kana(CompositionText.AllKana(state, env, final)),
        DisplayMode.Katakana => ComposedText.Kana(ToKatakana(CompositionText.AllKana(state, env, final))),
        DisplayMode.HalfWidthKatakana => ComposedText.Kana(ToHalfWidthKatakana(ToKatakana(CompositionText.AllKana(state, env, final)))),
        _ => CompositionText.Shown(CompositionText.Clauses(state, env, final, answer), state),
    };

    /// <summary>数字だけの入力 (<see cref="IsNumeric"/>) の表示。読点のほかは打った半角のまま (10:00、-)。英文の続きと、, から始まる入力 (1 を確定した後の ,000) は、すべて打ったまま。</summary>
    internal static ComposedText Numeric(TextReading state, TextEnv env) =>
        env.Context().EndsWithEnglishAndSpace || state.Units[0].Raw == "," ? ComposedText.English(CompositionText.Raw(state))
        : ComposedText.Concat(TypedMarks.Read(state, env).Select(unit => unit.Kana == "、" ? ComposedText.Kana(unit.Kana) : ComposedText.English(unit.Raw)));

    /// <summary>英字の大文字・小文字を変える (ai → AI / Ai)。</summary>
    public static string ApplyCase(string text, LetterCase letterCase) => letterCase switch
    {
        LetterCase.Upper => text.ToUpperInvariant(),
        LetterCase.Capitalized => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant(),
        _ => text,
    };

    /// <summary>区間ごとの打った文字 (区間と同じ数・順)。英語の区間は見せる表記にしてある。</summary>
    internal static IReadOnlyList<TextSpan> TypedSpans(IReadOnlyList<CompositionSegment> segments, TextEnv env) =>
        NotationRules.Typed([.. segments.Select(s => new TextSpan(s.IsProtected ? SpanKind.Protected : s.IsEnglish ? SpanKind.Typed : SpanKind.Japanese, s.Raw))], env.ProperNoun);

    /// <summary>
    /// 自動の表示の文節の並び。英語の区間は英字のまま、日本語の区間はかな (answer があれば answer の文節)。数字だけ・水平線・保護区間だけの入力は、打ったままの 1 文節。
    /// </summary>
    /// <param name="answer">日本語の区間 (かな, 前の区間が英語か, 並びのその区間より前の文字) の文節を返す関数 (ライブ変換)。null ならかなのまま。</param>
    internal static IReadOnlyList<TypingClause> Clauses(TextReading state, TextEnv env, bool final, Func<string, bool, string, IReadOnlyList<TypingClause.Japanese>>? answer)
    {
        if (CompositionText.IsNumeric(state)) return CompositionText.Literal(state, env, final, CompositionText.Numeric(state, env));
        if (CompositionText.IsHorizontalRule(state)) return CompositionText.Literal(state, env, final, ComposedText.English(CompositionText.Raw(state)));
        // 保護範囲 (技術的な文字列) は、幅変換・かな化・漢字変換をせずに原文のまま出す (INV-01)。
        if (CompositionText.VerifyProtectedRaw(state)) return [new TypingClause.Literal(CompositionText.Raw(state), ComposedText.Of(SpanKind.Protected, CompositionText.Raw(state)))];
        var clauses = new List<TypingClause>();
        // 並びのその区間より前の文字 (answer に渡す)
        var before = new System.Text.StringBuilder();
        var segments = CompositionText.Segments(state, env);
        var typed = TypedSpans(segments, env);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.IsEnglish)
            {
                clauses.Add(new TypingClause.Typed(segment.Kana, typed[i]));
                before.Append(typed[i].Text);
                continue;
            }
            var isLast = i == segments.Count - 1;
            var kana = segment.Kana;
            var pending = isLast ? CompositionText.PendingText(state, env, segment, final) : "";
            if (final && isLast && pending == "ん")
            {
                kana += pending;
                pending = "";
            }
            // 打っている途中の w の連続 (くさ + www) は、確定のときと同じく笑いの w で見せる (ww を っw にしない。wwa = っわ は続きで読む)
            if (!final && isLast && state.Mode == DisplayMode.Auto && !state.KanaInput && pending is "w" or "W")
            {
                var laughter = 0;
                while (laughter < state.Units.Count && state.Units[^(laughter + 1)] is { Raw: "w" or "W", Kana: "っ" }) laughter++;
                if (laughter > 0 && kana.Length >= laughter)
                {
                    pending = string.Concat(state.Units.Skip(state.Units.Count - laughter).Select(u => u.Raw)) + pending;
                    kana = kana[..^laughter];
                }
            }
            IReadOnlyList<TypingClause.Japanese> answered = kana.Length == 0 ? [] : answer is not null ? answer(kana, i > 0 && segments[i - 1].IsEnglish, before.ToString()) : [new TypingClause.Kana(kana)];
            // 入力途中の子音だけの区間 (bug|w の w) は、読みが空の文節が子音を持つ
            if (pending.Length > 0) answered = answered.Count > 0 ? [.. answered.Take(answered.Count - 1), answered[^1].WithPending(pending)] : [new TypingClause.Kana("", pending)];
            clauses.AddRange(answered);
            foreach (var clause in answered) before.Append(clause.Shown.Text);
        }
        return clauses;
    }

    private static IReadOnlyList<TypingClause> Literal(TextReading state, TextEnv env, bool final, ComposedText text) =>
        [new TypingClause.Literal(CompositionText.AllKana(state, env, final), text)];

    /// <summary>文節の並びの見せる文字。打ったままの入力のほかは、見せる表記の規則を当てる。</summary>
    internal static ComposedText Shown(IReadOnlyList<TypingClause> clauses, TextReading state) => clauses is [TypingClause.Literal literal]
        ? literal.Text
        : NotationRules.Show(ComposedText.Concat(clauses.Select(c => c.Shown)), CompositionText.TypedForNotation(state));

    /// <summary>英語判定を無視してすべてかなにしたもの (F6)。</summary>
    public static string AllKana(TextReading state, TextEnv env, bool final) => string.Concat(TypedMarks.Read(state, env).Select(u => u.Kana)) + CompositionText.PendingText(state, env, final);

    /// <summary>
    /// 日本語の部分の読みに書き間違い (ブレスレッド) があれば、その位置 (単位の範囲) と正しい読みを返す。
    /// 英語として見せている部分や、入力途中の子音は見ない。読みの区切りが単位の区切りと合わないときも null。
    /// </summary>
    public static (int Start, int End, Misspelling Misspelling)? FindMisspelling(TextReading state, TextEnv env, MisspellingDictionary dictionary)
    {
        if (state.Mode != DisplayMode.Auto || state.Units.Count == 0) return null;
        var english = CompositionText.UnitIsEnglish(state, env);
        // 保護区間の ASCII 原文を、日本語の誤字候補としてかなへ書き換えない。
        // 英語と同じく検索対象から外す。保護区間の外の日本語は従来どおり検査する。
        var spans = CompositionText.ProtectedSpans(state);
        var rawOffset = 0;
        var spanIndex = 0;
        for (var i = 0; i < state.Units.Count; i++)
        {
            var end = rawOffset + state.Units[i].Raw.Length;
            while (spanIndex < spans.Count && spans[spanIndex].End <= rawOffset) spanIndex++;
            if (spanIndex < spans.Count && spans[spanIndex].Start < end) english[i] = true;
            rawOffset = end;
        }
        // 日本語の単位が続く範囲ごとに探す。
        var units = TypedMarks.Read(state, env);
        var start = 0;
        while (start < state.Units.Count)
        {
            if (english[start])
            {
                start++;
                continue;
            }
            var end = start;
            while (end < state.Units.Count && !english[end]) end++;
            var offsets = new List<int>();
            var reading = new StringBuilder();
            for (var i = start; i < end; i++)
            {
                offsets.Add(reading.Length);
                reading.Append(units[i].Kana);
            }
            offsets.Add(reading.Length);
            // 語末の n (まだ ん になっていない) も ん として読む (しゅみれーしょn)。
            if (end == state.Units.Count && CompositionText.Pending(state).ToLowerInvariant() is "n" or "nn")
            {
                reading.Append('ん');
                offsets.Add(reading.Length);
            }
            if (dictionary.Find(reading.ToString()) is { } found)
            {
                var first = offsets.IndexOf(found.Start);
                var last = offsets.IndexOf(found.Start + found.Length);
                if (first >= 0 && last > first) return (start + first, start + last, found);
            }
            start = end;
        }
        return null;
    }

    /// <summary>
    /// 打ったものを読んだ単位 (<see cref="Typed"/>) の [start, end) を打った要素を、正しい読み (カタカナ) に置き換える。end が単位の数を超えるときは入力途中の子音も含む。
    /// かなの読みにする (英字の Raw を持たないので、英語と判定されることはない)。
    /// 単位は、確定・変換に進んだ範囲を決めずに読んだもの (<see cref="Unsettled"/>: 単位の位置が打った要素と揃う)。置き換えたら範囲も決め直す。
    /// </summary>
    public static CompositionTextState ReplaceReading(CompositionTextState state, TextEnv env, int start, int end, string rightKatakana)
    {
        state = CompositionText.Unsettled(state);
        var typed = CompositionText.Typed(state, env);
        var from = typed.Units.Take(start).Sum(u => u.Raw.Length);
        var to = end > typed.Units.Count ? state.Keys.Count : typed.Units.Take(end).Sum(u => u.Raw.Length);
        var reading = KanaChars.ToHiragana(rightKatakana).Select(c => (TypedKey)new TypedKey.Reading(c));
        return state with { Keys = state.Keys.RemoveRange(from, to - from).InsertRange(from, reading) };
    }

    /// <summary>確定・変換に進んだ範囲を決めていない値。</summary>
    public static CompositionTextState Unsettled(CompositionTextState state) => state.Settled is null ? state : state with { Settled = null };

    /// <summary>単位ごとに、英語として見せている区間に入っているか。</summary>
    private static bool[] UnitIsEnglish(TextReading state, TextEnv env)
    {
        var result = new bool[state.Units.Count];
        var unit = 0;
        foreach (var segment in CompositionText.Segments(state, env))
        {
            var remaining = segment.Raw.Length;
            while (unit < state.Units.Count && remaining > 0)
            {
                result[unit] = segment.IsEnglish;
                remaining -= state.Units[unit].Raw.Length;
                unit++;
            }
        }
        return result;
    }
}
