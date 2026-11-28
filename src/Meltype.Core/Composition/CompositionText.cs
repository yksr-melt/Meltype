// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Collections.Immutable;
using System.Text;
using Meltype.Input;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Libraries.Chars;
using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>変換ボックスの表示形式。Auto 以外は F6〜F10 などでユーザーが明示的に選んだもの。</summary>
internal enum DisplayMode { Auto, Hiragana, Katakana, FullWidthAlphanumeric, HalfWidthAlphanumeric, HalfWidthKatakana }

/// <summary>英字 (F9 / F10) で見せるときの大文字・小文字。F9 / F10 を続けて押すと 打ったまま → すべて大文字 → 先頭だけ大文字 と切り替わる。</summary>
internal enum LetterCase { AsTyped, Upper, Capitalized }

/// <summary>
/// 未確定の入力。打ったそばからローマ字をかな 1 音ずつの単位にしていき、まだ音にならない子音
/// (k, ky, n …) だけを Pending に残す。BackSpace は 1 音ずつ消える。
/// 英単語の判定は区間ごとに行うので、「きょうは google」のように日本語の後ろの英単語だけが英字になる。
/// </summary>
internal static partial class CompositionText
{
    /// <summary>
    /// 英語 / 日本語の学習を引く文脈: 前が英語か (true)・日本語か (false)。設定「英語 / 日本語の学習を前の文脈ごとに分ける」が OFF なら null。
    /// </summary>
    public static bool? LearningContext(TextEnv env) => LearnedLanguages(env).Context;

    /// <summary>判定に使う英語 / 日本語の学習。打った英字を読み直す断片は、読み直す元の入力のものを使う。</summary>
    public static LanguageLearning LearnedLanguages(TextEnv env) =>
        env.Fragment is { } origin ? origin.Languages : new(env.Languages, env.Settings.ContextualLearning ? env.Context().PrecedingEnglish : null);

    /// <summary><see cref="LearnedLanguages"/> の学習だけ。文脈 (<see cref="TextEnv.Context"/>) を計算しない。</summary>
    private static LanguageMemory? LearnedMemory(TextEnv env) => env.Fragment is { } origin ? origin.Languages.Memory : env.Languages;

    /// <summary>
    /// 打った英字の一部を読み直すために作る断片に渡すもの。env は読み直す元の入力のもの。
    /// 断片は設定によらず既定の設定 (自動判定・z の記号・/ のまま) で読む。英語 / 日本語の学習は元の入力のもの。
    /// </summary>
    public static TextEnv FragmentEnv(TextEnv env) =>
        new(env.Detector, FragmentSettings) { Fragment = new FragmentOrigin(LearnedLanguages(env)), Context = () => TextContext.None };

    private static readonly Config.Settings FragmentSettings = new();

    public static IReadOnlyList<CompositionUnit> Units(TextReading state) => state.Units;
    public static string Pending(TextReading state) => state.Pending.ToString();
    public static bool IsEmpty(TextReading state) => state.Units.Count == 0 && state.Pending.Length == 0;

    /// <summary>打ったものを、確定に使う読みにしたもの。表示・変換・確定はこれを読む。打ったものと env が同じなら前の結果を使う。</summary>
    public static TextReading Read(CompositionTextState state, TextEnv env) => CompositionText.Derive(state, env).Read;

    /// <summary>打ったものを 1 要素ずつ読んだだけのもの (数字の後ろの単位・笑いの w の前)。単位の打った文字の位置が、打ったものの要素の位置と一致する。</summary>
    internal static TextReading Typed(CompositionTextState state, TextEnv env) => CompositionText.Derive(state, env).Typed;

    /// <summary>打った英字の一部 raw を、断片の env (<see cref="FragmentEnv"/>) で読んだもの。</summary>
    private static TextReading Fragment(string raw, TextEnv env) => CompositionText.Typed(CompositionText.OfLetters(raw), FragmentEnv(env));

    /// <summary>読みの途中を直していたら、カーソルを外す (末尾にする)。打ちかけのローマ字は、後ろの読みの単位の前で確定させる (<see cref="ReadKept"/>)。</summary>
    public static CompositionTextState MergeTail(CompositionTextState state) => state.Caret is null ? state : state with { Caret = null };

    /// <summary>カーソルより後ろに読みがある (読みの途中を直している)。</summary>
    public static bool HasTail(CompositionTextState state) => state.Caret is { } caret && caret < state.Keys.Count;

    /// <summary>
    /// 読みの途中を直すカーソルを動かす (← → Home End)。打ちかけのローマ字があれば動かさない。動かしたら、動かした後の値。
    /// ← と Home は、カーソルより前を見えている読みの単位どおりの打った要素にする (確定・変換に進んだ範囲で直した読みも、見えていたまま)。
    /// </summary>
    public static CompositionTextState? MoveCaret(CompositionTextState state, TextEnv env, int vk)
    {
        var reading = CompositionText.Read(state, env);
        if (reading.Pending.Length > 0) return null;
        var tail = state.Keys.Skip(state.Caret ?? state.Keys.Count);
        switch (vk)
        {
            case VirtualKeys.Left when reading.Units.Count > 0:
                var before = CompositionText.UnitKeys(reading.Units.SkipLast(1)).ToList();
                return state with { Keys = [.. before, .. CompositionText.UnitKeys([reading.Units[^1]]), .. tail], Caret = before.Count, Settled = null };
            case VirtualKeys.Right when CompositionText.HasTail(state):
                return state with { Caret = state.Caret + CompositionText.FirstUnitKeys(state) };
            case VirtualKeys.Home:
                return state with { Keys = [.. CompositionText.UnitKeys(reading.Units), .. tail], Caret = 0, Settled = null };
            case VirtualKeys.End:
                return CompositionText.MergeTail(state);
            default:
                return null;
        }
    }

    /// <summary>カーソルのすぐ後ろの単位を消す (Delete)。</summary>
    public static CompositionTextState RemoveNext(CompositionTextState state) =>
        state with { Keys = state.Keys.RemoveRange(state.Caret!.Value, CompositionText.FirstUnitKeys(state)) };

    // カーソルのすぐ後ろの単位の要素の数 (Kept と、続く KeptRaw)
    private static int FirstUnitKeys(CompositionTextState state)
    {
        var start = state.Caret!.Value;
        var end = start + 1;
        while (end < state.Keys.Count && state.Keys[end] is TypedKey.KeptRaw) end++;
        return end - start;
    }

    /// <summary>単位どおりに読む打った要素 (<see cref="TypedKey.Kept"/>・<see cref="TypedKey.KeptRaw"/>)。</summary>
    private static IEnumerable<TypedKey> UnitKeys(IEnumerable<CompositionUnit> units) =>
        units.SelectMany(u => u.Raw.Select((c, i) => i == 0 ? (TypedKey)new TypedKey.Kept(c, u.Kana) : new TypedKey.KeptRaw(c)));

    /// <summary>カーソルより後ろに見えている文字 (読みの途中を直している間だけ。ひらがなで見せる)。</summary>
    public static string TailText(CompositionTextState state) =>
        state.Caret is { } caret ? string.Concat(state.Keys.Skip(caret).OfType<TypedKey.Kept>().Select(k => k.Value)) : "";

    /// <summary>読みの途中を直している値の、カーソルより前だけの値 (打つ・消す・読むのはここ)。</summary>
    private static CompositionTextState BeforeCaret(CompositionTextState state)
    {
        if (Prefixes.TryGetValue(state, out var cached)) return cached;
        var before = state with { Keys = state.Keys.GetRange(0, state.Caret!.Value), Caret = null };
        Prefixes.Set(state, before);
        return before;
    }

    /// <summary>カーソルより前を before にした値。</summary>
    private static CompositionTextState WithBeforeCaret(CompositionTextState state, CompositionTextState before) =>
        before with { Keys = before.Keys.AddRange(state.Keys.Skip(state.Caret!.Value)), Caret = before.Keys.Count };

    /// <summary>打った文字を、ローマ字入力で打った文字の並びにする (打った英字の一部を読み直す断片)。</summary>
    public static CompositionTextState OfLetters(string raw) => new() { Keys = [.. raw.Select(c => (TypedKey)new TypedKey.Letter(c))] };

    private static CompositionTextState Add(CompositionTextState state, TypedKey key)
    {
        if (state.Caret is not null) return CompositionText.WithBeforeCaret(state, CompositionText.Add(CompositionText.BeforeCaret(state), key));
        var next = state with { Keys = state.Keys.Add(key) };
        Befores.Set(next, state);
        return next;
    }

    /// <summary>値ごとの読んだ結果。表示の形などが値ごとに違うので、値 (参照) ごとに持つ。</summary>
    private sealed record Reading(ReadingKey Key, TextReading Typed, TextReading Read);

    // 控えはスレッドごとに、最近読んだ値だけ持つ。取りこぼしても読み直すだけで結果は同じ。
    // ConditionalWeakTable は値ごとに GC の dependent handle を作り、1 キーごとに値を作るので GC の停止が倍近くになった
    private const int RecentCount = 8;

    private sealed class ReadingCaches
    {
        public Recent<CompositionTextState, Reading> Readings { get; } = new(RecentCount, ReferenceEqualityComparer.Instance);
        public Recent<ImmutableList<TypedKey>, KeysReading> ByKeys { get; } = new(RecentCount, ReferenceEqualityComparer.Instance);
        public Recent<CompositionTextState, CompositionTextState> Befores { get; } = new(RecentCount, ReferenceEqualityComparer.Instance);
        public Recent<Settlement, SettledReadingEntry> Settlements { get; } = new(RecentCount, ReferenceEqualityComparer.Instance);
        public Recent<CompositionTextState, CompositionTextState> Prefixes { get; } = new(RecentCount, ReferenceEqualityComparer.Instance);
    }

    private static readonly ThreadLocal<ReadingCaches> Caches = new(() => new());

    private static Recent<CompositionTextState, Reading> Readings => Caches.Value!.Readings;
    private static Recent<ImmutableList<TypedKey>, KeysReading> ByKeys => Caches.Value!.ByKeys;
    private static Recent<CompositionTextState, CompositionTextState> Befores => Caches.Value!.Befores;
    private static Recent<Settlement, SettledReadingEntry> Settlements => Caches.Value!.Settlements;
    private static Recent<CompositionTextState, CompositionTextState> Prefixes => Caches.Value!.Prefixes;

    /// <summary>
    /// 打ったものの並び (Keys) ごとの読んだ結果。with で表示の形だけを変えた値も、同じ並びの読みと区間分けを使う。
    /// Replayed は、打ったものを読んで英数字の間の - を決める前。Reads は、表示の形・提案の受け入れごとの、仕上げた読み (<see cref="Finish"/>)。
    /// Segments は、その並びを読んだ単位の区間分け (<see cref="SegmentsKey"/> ごと)。
    /// </summary>
    private sealed class KeysReading(ReadingKey key, CompositionTextState template, TextReading replayed, TextReading typed)
    {
        public ReadingKey Key { get; } = key;
        public CompositionTextState Template { get; } = template;
        public TextReading Replayed { get; } = replayed;
        public TextReading Typed { get; } = typed;
        public Recent<(DisplayMode Mode, bool SuggestionAccepted), TextReading> Reads { get; } = new(2, EqualityComparer<(DisplayMode, bool)>.Default);
        // 打ったものを読んだだけの単位・仕上げた単位・確定・変換に進んだ範囲を含む単位で、区切り方が 3 つ以上になることがある
        public Recent<string, SegmentsReading> Segments { get; } = new(4, StringComparer.Ordinal);
    }

    // Befores: 1 要素を足した値 → 足す前の値。読むときに、足す前の値の読みの続きから読む (長い入力で 1 キーごとに全体を読み直さない)。読んだら消す。

    // 1 キーごとに、表示・Space の扱いなどで何度も読む。笑いの w の判定などは英単語の辞書を引くので、同じ打ったものと env なら前の結果を使う
    private static Reading Derive(CompositionTextState state, TextEnv env)
    {
        if (state.Caret is not null) return CompositionText.Derive(CompositionText.BeforeCaret(state), env);
        var key = CompositionText.ReadingKeyOf(env);
        if (Readings.TryGetValue(state, out var cached) && cached.Key == key) return cached;
        var keys = CompositionText.KeysReadingOf(state, env, key);
        Befores.Remove(state);
        var typed = CompositionText.Bind(keys.Typed, state);
        var mode = (state.Mode, state.SuggestionAccepted);
        TextReading read;
        if (keys.Reads.TryGetValue(mode, out var fixedReading)) read = CompositionText.Bind(fixedReading, state);
        else
        {
            read = CompositionText.Finished(typed, state, env);
            keys.Reads.Set(mode, read);
        }
        var reading = new Reading(key, typed, read);
        Readings.Set(state, reading);
        return reading;
    }

    private static ReadingKey ReadingKeyOf(TextEnv env) =>
        new(env.Detector, env.Settings, env.Fragment, env.Context(), LearnedMemory(env), LearnedMemory(env)?.Version ?? -1);

    /// <summary>打ったものを読んだだけの typed に、確定・変換に使う読みの仕上げ (<see cref="Finish"/>) をかけた読み。</summary>
    private static TextReading Finished(TextReading typed, CompositionTextState state, TextEnv env)
    {
        var read = typed.Clone(state);
        CompositionText.Finish(read, env);
        return read;
    }

    /// <summary>
    /// 確定・変換に進んだ範囲 settled を読んだ単位。その範囲を、進んだときの表示の形で、入力途中の子音で終わる並びも直して読む。
    /// 入力途中の子音は確定させない (続けて打った文字とつなげて読む: kan + Space + a は かな)。
    /// </summary>
    private static TextReading SettledReading(CompositionTextState state, Settlement settled, TextEnv env, ReadingKey key)
    {
        if (Settlements.TryGetValue(settled, out var cached) && cached.Key == key && CompositionText.SameInput(cached.Template, state)) return cached.Reading;
        var prefix = state with { Keys = settled.Count == state.Keys.Count ? state.Keys : state.Keys.GetRange(0, settled.Count), Mode = settled.Mode, Settled = settled.Previous };
        var read = CompositionText.Finished(CompositionText.KeysReadingOf(prefix, env, key).Typed, prefix, env);
        read.SettledUnits = read.Units.Count;
        Settlements.Set(settled, new SettledReadingEntry(key, prefix, read));
        return read;
    }

    private sealed record SettledReadingEntry(ReadingKey Key, CompositionTextState Template, TextReading Reading);

    /// <summary>確定・変換に進んだ: 今までに打った範囲を、今の表示の形で読んだ形に決める (<see cref="CompositionTextState.Settled"/>)。</summary>
    public static CompositionTextState Settle(CompositionTextState state) =>
        state.Settled is { } settled && settled.Count == state.Keys.Count && settled.Mode == state.Mode ? state : state with { Settled = new Settlement(state.Keys.Count, state.Mode, state.Settled) };

    /// <summary>
    /// 自動判定の表示で確定するときの値 (F6〜F10 で表示の形を選ぶ前に見えていた形)。確定・変換に進んだ範囲も、自動判定の表示で読む。
    /// </summary>
    public static CompositionTextState Automatic(CompositionTextState state) =>
        CompositionText.Settle(state with { Mode = DisplayMode.Auto, Settled = AutomaticSettlement(state.Settled) });

    private static Settlement? AutomaticSettlement(Settlement? settled) =>
        settled is null ? null : settled with { Mode = DisplayMode.Auto, Previous = AutomaticSettlement(settled.Previous) };

    /// <summary>読み reading を、値 state の表示の形・flag で読むもの。reading がその値の読みならそのまま使う。</summary>
    private static TextReading Bind(TextReading reading, CompositionTextState state) => ReferenceEquals(reading.State, state) ? reading : reading.Clone(state);

    private static KeysReading KeysReadingOf(CompositionTextState state, TextEnv env, ReadingKey key)
    {
        if (ByKeys.TryGetValue(state.Keys, out var cached) && cached.Key == key && CompositionText.SameInput(cached.Template, state)) return cached;
        TextReading replayed;
        if (Befores.TryGetValue(state, out var before) && ByKeys.TryGetValue(before.Keys, out var previous) && previous.Key == key && CompositionText.SameInput(previous.Template, state))
        {
            replayed = previous.Replayed.Clone(state);
            state.Keys[^1].ReadInto(replayed, env);
        }
        else
        {
            // 確定・変換に進んだ範囲は、進んだときの読みから続ける
            replayed = state.Settled is { } settled ? CompositionText.SettledReading(state, settled, env, key).Clone(state) : new TextReading(state);
            foreach (var typedKey in state.Keys.Skip(state.Settled?.Count ?? 0)) typedKey.ReadInto(replayed, env);
        }
        var typed = replayed.Clone(state);
        CompositionText.JoinHyphens(typed, env);
        var reading = new KeysReading(key, state, replayed, typed);
        ByKeys.Set(state.Keys, reading);
        return reading;
    }

    /// <summary>打ったものを読んだ単位が同じになる値か (入力の始めに決まる flag が同じ)。</summary>
    private static bool SameInput(CompositionTextState a, CompositionTextState b) =>
        (a.KanaInput, a.SlashAtStart, a.FullWidthParentheses, a.SquareBrackets, a.SuggestionAccepted) == (b.KanaInput, b.SlashAtStart, b.FullWidthParentheses, b.SquareBrackets, b.SuggestionAccepted) &&
        a.Settled == b.Settled;

    /// <summary>区間分けの控え。Reasons は区切りを選んだ理由 (確定のログ用)。</summary>
    private sealed record SegmentsReading(LanguageMemory? Memory, IReadOnlyList<CompositionSegment> Segments, List<Func<string>> Reasons);

    /// <summary>単位の区間分けの控え (打ったものの並びごと)。並びの読みを作る前 (読みを作る途中) は無い。</summary>
    private static Recent<string, SegmentsReading>? SegmentsCacheOf(TextReading state) => ByKeys.TryGetValue(state.State.Keys, out var keys) ? keys.Segments : null;

    private static bool EffectiveManual(TextReading state, TextEnv env) => !state.SuggestionAccepted && !env.Settings.AutoDetection;

    /// <summary>
    /// かな入力の 1 キー。raw はそのキーの英字 (英単語の判定と、英語として見せるときに使う)。
    /// 濁点・半濁点は直前のかなに付ける (か + ゛ → が)。
    /// </summary>
    internal static void ReadKana(TextReading state, char raw, char kana)
    {
        if (kana is '゛' or '゜' && state.Units.Count > 0 && state.Units[^1].Kana.Length == 1 &&
            Detection.KanaDetector.Combine(state.Units[^1].Kana[0], kana) is { } combined)
        {
            var last = state.Units[^1];
            state.Units[^1] = new CompositionUnit(combined.ToString(), last.Raw + raw);
            return;
        }
        state.Units.Add(new CompositionUnit(kana.ToString(), raw.ToString()));
    }

    public static CompositionTextState AppendKana(CompositionTextState state, char raw, char kana) => CompositionText.Add(state, new TypedKey.Kana(raw, kana));

    /// <summary>キー以外から入った読み (選んだ候補の読みなど) を足す。</summary>
    public static CompositionTextState AppendReading(CompositionTextState state, string kana) => kana.Aggregate(state, (s, c) => CompositionText.Add(s, new TypedKey.Reading(c)));

    /// <summary>かな配列の打鍵。単打は JIS かな同様、英語と判定できるようにする。シフトとの組み合わせは表示だけに使う。</summary>
    public static CompositionTextState AppendLayoutKana(CompositionTextState state, string kana, string keys, bool repeat)
    {
        if (kana is [var sound] && keys is [var key])
        {
            if (!repeat) return AppendKana(state, key, sound);
            // 長押しで増えたかなは、最初の打鍵も含めて英単語にはしない (W の長押し → かかか)。
            var last = (state.Caret ?? state.Keys.Count) - 1;
            if (last >= 0 && state.Keys[last] is TypedKey.Kana previous && previous.Key == key && previous.Value == sound)
                state = state with { Keys = state.Keys.SetItem(last, new TypedKey.Reading(sound, keys)) };
        }
        for (var i = 0; i < kana.Length; i++)
            state = Add(state, new TypedKey.Reading(kana[i], i == 0 ? keys : ""));
        return state;
    }

    public static CompositionTextState Append(CompositionTextState state, char c) => CompositionText.Add(state, new TypedKey.Letter(c));

    /// <summary>読みの英字。</summary>
    public static string Raw(TextReading state) => string.Concat(state.Units.Select(u => u.Raw)) + state.Pending;

    /// <summary>
    /// 英数字の間に打った - (J-core・p-hub) を、長音ではなく半角の - にする。打った時点では後ろが分からないので、読みを作った後に決める。
    /// かなにならずに残った英字 (p-hub の p、kyouhapt-query-digest の t) の後の - は長音ではない。かなになった ん (n-to) の後は長音
    /// </summary>
    private static void JoinHyphens(TextReading state, TextEnv env)
    {
        for (var i = 1; i < state.Units.Count; i++)
        {
            if (state.Units[i] is not { Raw: "-", Kana: "ー" or "-" } hyphen) continue;
            var previous = state.Units[i - 1];
            // すぐ後ろに打ったのが英数字 (かな入力のかなのキーは除く)
            var next = i + 1 < state.Units.Count
                ? state.Units[i + 1] is { Raw: [var c, ..] } unit && (!state.KanaInput || unit.Kana == unit.Raw) ? c : (char?)null
                : state.Pending.Length > 0 ? state.Pending[0] : null;
            var joins = previous.Raw is [.., var before] && char.IsAsciiLetterOrDigit(before) && next is { } after && char.IsAsciiLetterOrDigit(after) &&
                (previous.Kana == previous.Raw && previous.Raw.All(char.IsAsciiLetterLower) || TypedMarks.JoinsName(state.Units, i, env));
            state.Units[i] = hyphen with { Kana = joins ? "-" : "ー" };
        }
    }

    internal static void ReadKept(TextReading state, TextEnv env, char raw, string kana)
    {
        CompositionText.Normalize(state, env, final: true);
        state.Units.Add(new CompositionUnit(kana, raw.ToString()));
        // 残した音は、後ろに打った英字と合わせて読み直さない (残した w の後ろの w を っ にしない)
        state.KeptUnits = state.Units.Count;
    }

    internal static void ReadLetter(TextReading state, TextEnv env, char c)
    {
        // … になった / の続きの / は、同じ … に足す (4〜6 個で ……、7〜9 個で ………: 3 個ごとに … を 1 つ増やす。issue #495)
        if (c == '/' && state.Pending.Length == 0 && state.Units.Count >= 1 && state.Units[^1] is { Kana: [.., '…'] } ellipsis && ellipsis.Raw.Length >= 3 && ellipsis.Raw.All(x => x == '/') &&
            ellipsis.Kana.All(x => x == '…'))
        {
            var raw = ellipsis.Raw + "/";
            state.Units[^1] = new CompositionUnit(new string('…', (raw.Length + 2) / 3), raw);
            return;
        }
        // / を 3 つ続けて打ったら … (三点リーダー)。URL (file:///) の : の後ろは除く。
        if (c == '/' && state.Pending.Length == 0 && state.Units.Count >= 2 && state.Units[^1].Raw == "/" && state.Units[^2].Raw == "/" &&
            !(state.Units.Count >= 3 && state.Units[^3].Raw == ":"))
        {
            state.Units.RemoveRange(state.Units.Count - 2, 2);
            state.Units.Add(new CompositionUnit("…", "///"));
            return;
        }
        if (char.IsAsciiLetter(c))
        {
            // 大文字に続けて打った大文字 (TS, IME の S, M) は略語の 1 文字。後ろのローマ字 (yu) とつなげて かな (しゅ) にしない。
            var lastTyped = state.Pending.Length > 0 ? state.Pending[^1] : state.Units.Count > 0 && state.Units[^1].Raw.Length > 0 ? state.Units[^1].Raw[^1] : '\0';
            if (char.IsAsciiLetterUpper(c) && char.IsAsciiLetterUpper(lastTyped))
            {
                CompositionText.Normalize(state, env, final: true);
                state.Units.Add(new CompositionUnit(c.ToString(), c.ToString()));
                return;
            }
            // 直前の「ローマ字として読めなかった英字」は、次の文字と合わせると読めることがある
            // (test の t を消して u を打つ → s + u = す)。入力途中の子音に戻して読み直す。略語の大文字 (TS の S) は戻さない。
            // 英単語との区切り (commit|suru の t|s) は、単位の途中でも区切りの側 (ReadingLattice.cs) で選ぶ。
            var pulled = new StringBuilder();
            while (state.Units.Count > Math.Max(state.SettledUnits, state.KeptUnits) && state.Units[^1] is { Raw.Length: 1 } last && last.Kana == last.Raw && char.IsAsciiLetter(last.Raw[0]) &&
                   !(char.IsAsciiLetterUpper(last.Raw[0]) && state.Units.Count >= 2 && state.Units[^2].Raw is { Length: > 0 } before && char.IsAsciiLetterUpper(before[^1])))
            {
                pulled.Insert(0, last.Raw);
                state.Units.RemoveAt(state.Units.Count - 1);
            }
            state.Pending.Insert(0, pulled.ToString());
            // zh / zj / zk / zl → ← ↓ ↑ →。z が 1 文字のときだけ (puzzle の zz の次の l は英単語)。
            if (state.Pending.Length == 1 && char.ToLowerInvariant(state.Pending[0]) == 'z' && env.Settings.ZSymbols && ZSymbol(c) is { } arrow)
            {
                var raw = state.Pending[0].ToString() + c;
                state.Pending.Clear();
                CompositionText.Normalize(state, env, final: true);
                state.Units.Add(new CompositionUnit(arrow.ToString(), raw));
                return;
            }
            state.Pending.Append(c);
            CompositionText.SplitUnitAfterNumber(state, env, final: false);
            CompositionText.Normalize(state, env, final: false);
            return;
        }
        if (state.Pending.Length > 0 && char.ToLowerInvariant(state.Pending[^1]) == 'z' && env.Settings.ZSymbols && ZSymbol(c) is { } z)
        {
            var raw = state.Pending[^1].ToString() + c;
            state.Pending.Length--;
            CompositionText.Normalize(state, env, final: true);
            state.Units.Add(new CompositionUnit(z.ToString(), raw));
            return;
        }
        // 記号・数字の前で、途中の n は ん に、読めない子音は英字のまま確定させる。
        CompositionText.Normalize(state, env, final: true);
        // 文章の入力欄で打ち始めに打った / は ・ (Microsoft IME と同じ)。Space で / にできる。
        if (c == '/' && state.Units.Count == 0 && state.SlashAtStart)
        {
            state.Units.Add(new CompositionUnit("・", "/"));
            return;
        }
        // 設定「かなの後ろの /」: かなのすぐ後ろの / を ・ (いろん/ますく → いろん・ますく) か ／ にする (issue #502)。英字・数字の後ろ (and/or、3/4、URL)
        // と入力の始め (/help) は / のまま (issue #122)。英語の区間に入ったときは、打ったままの / を見せる。
        if (c == '/' && state.Units.Count > 0 && state.Units[^1].Kana is [.., var kana] && kana is >= 'ぁ' and <= 'ヺ' or 'ー')
        {
            var slash = env.Settings.Slash switch
            {
                Config.SlashStyle.MiddleDot => "・",
                Config.SlashStyle.FullWidth => "／",
                Config.SlashStyle.Slash => null,
                var style => throw new InvalidOperationException($"不明な SlashStyle: {style}"),
            };
            if (slash is not null)
            {
                state.Units.Add(new CompositionUnit(slash, "/"));
                return;
            }
        }
        state.Units.Add(new CompositionUnit(CompositionText.Symbol(state, c).ToString(), c.ToString()));
    }

    /// <summary>打ち始めの / だけを ・ にして入れているか。</summary>
    public static bool IsSlashAtStart(TextReading state) => state.Pending.Length == 0 && state.Units is [{ Kana: "・", Raw: "/" }];

    /// <summary>1 音 (または 1 文字) 消す。入力途中の子音があればそれを 1 文字消す。残った打ったものは読み直す。</summary>
    public static CompositionTextState RemoveLast(CompositionTextState state, TextEnv env) => state.Caret is null
        ? CompositionText.RemoveLast(state, env, materialized: false)
        : CompositionText.WithBeforeCaret(state, CompositionText.RemoveLast(CompositionText.BeforeCaret(state), env, materialized: false));

    /// <param name="materialized">確定・変換に進んだ範囲が <see cref="Materialized"/> の形 (範囲の単位と打った要素の位置が揃う)。</param>
    private static CompositionTextState RemoveLast(CompositionTextState state, TextEnv env, bool materialized)
    {
        var typed = CompositionText.Typed(state, env);
        var count = CompositionText.RemovedKeys(typed, env, out var kept);
        // 確定・変換に進んだ範囲の中まで消すなら、範囲の読んだ単位を打った要素にしてから消す (範囲を縮める)
        if (!materialized && state.Settled is { } settled && (count > state.Keys.Count - settled.Count || kept < typed.SettledUnits))
            return CompositionText.RemoveLast(CompositionText.Materialized(state, settled, env, rereadLast: kept is null), env, materialized: true);
        var keys = state.Keys.RemoveRange(state.Keys.Count - count, count);
        if (keys.IsEmpty) return state with { Keys = keys, Settled = null, Mode = DisplayMode.Auto, Case = LetterCase.AsTyped, SuggestionAccepted = false };
        var shortened = state.Settled is { } range && range.Count > keys.Count ? range with { Count = keys.Count } : state.Settled;
        return CompositionText.KeepUnits(state with { Keys = keys, Settled = shortened }, env, typed.Units.Take(kept ?? 0).ToList(), typed.SettledUnits);
    }

    /// <summary>
    /// 確定・変換に進んだ範囲 settled を、その範囲を読んだ単位どおりの打った要素 (<see cref="TypedKey.Kept"/>・<see cref="TypedKey.KeptRaw"/>) にした値。
    /// 範囲の単位 (笑いの w・数字の後ろの単位) は、範囲を決めずに読み直すと変わるので、範囲の中を消す前にこの形にする。
    /// rereadLast なら、最後の単位は打った英字のまま入力途中の子音と一緒に読み直す (入力途中の子音を消すと、後ろのキーを見て決めた ww の っ も決め直す)。
    /// </summary>
    private static CompositionTextState Materialized(CompositionTextState state, Settlement settled, TextEnv env, bool rereadLast)
    {
        var reading = CompositionText.SettledReading(state, settled, env, CompositionText.ReadingKeyOf(env));
        var frozen = rereadLast && reading.Units.Count > 0 && reading.Units[^1].Raw.All(char.IsAsciiLetter) ? reading.Units.Count - 1 : reading.Units.Count;
        var units = CompositionText.UnitKeys(reading.Units.Take(frozen));
        var reread = string.Concat(reading.Units.Skip(frozen).Select(u => u.Raw)) + CompositionText.Pending(reading);
        var prefix = units.Concat(reread.Select(c => (TypedKey)new TypedKey.Letter(c))).ToImmutableList();
        return state with { Keys = prefix.AddRange(state.Keys.Skip(settled.Count)), Settled = new Settlement(prefix.Count, settled.Mode, null) };
    }

    /// <summary>
    /// 消した後に残した単位 kept を、見えていたときの音のまま残す。読み直して変わる 1 文字のかなの単位 (後ろのキーを見て決めた っ・ん) を <see cref="TypedKey.Kept"/> にする。
    /// 英字のままの単位 (abc の b) は、読み直して入力途中の子音になっても同じ文字に見えるので残さない。
    /// </summary>
    private static CompositionTextState KeepUnits(CompositionTextState state, TextEnv env, List<CompositionUnit> kept, int settledUnits)
    {
        for (var i = settledUnits; i < kept.Count;)
        {
            var units = CompositionText.Typed(state, env).Units;
            if (i < units.Count && units[i] == kept[i])
            {
                i++;
                continue;
            }
            // 確定・変換に進んだ範囲の後ろの単位は、打った要素と位置が揃う
            var offset = (state.Settled?.Count ?? 0) + kept.Skip(settledUnits).Take(i - settledUnits).Sum(u => u.Raw.Length);
            if (kept[i].Raw.Length != 1 || kept[i].Kana == kept[i].Raw || state.Keys[offset] is TypedKey.Kept)
            {
                i++;
                continue;
            }
            state = state with { Keys = state.Keys.SetItem(offset, new TypedKey.Kept(kept[i].Raw[0], kept[i].Kana)) };
        }
        return state;
    }

    /// <summary>BackSpace で消す、打ったものの要素の数 (打った文字の数)。kept は、消した後に残す単位の数。null なら残った打ったものを読み直す (入力途中の子音・保護範囲の 1 文字を消す)。</summary>
    private static int RemovedKeys(TextReading state, TextEnv env, out int? kept)
    {
        kept = null;
        // 保護範囲 (メンション・URL・パス) の中は、1 音 (く・ら…) ではなく打った原文の 1 文字ずつ消す。
        if (!state.KanaInput && (CompositionText.HasProtectedTail(state) || state.Pending.Length == 0 && state.Units.Count > 0 &&
            CompositionText.ProtectedSpans(state).Any(span => span.End > CompositionText.Raw(state).Length - state.Units[^1].Raw.Length)))
            return 1;
        // 打ちかけの子音を 1 文字消す。後ろのキーを見て決めた単位 (ff の っ、nk の ん) は、残った原文を読み直すので決め直す
        // (ff + BackSpace + uronto が っうろんと、nk + BackSpace + a が んあ になっていた: issue #1636)
        if (state.Pending.Length > 0) return 1;
        if (state.Units.Count == 0) return 0;
        // 見えている最後の 1 音 (英字なら 1 文字) の分。単位 (nn|o・sha) とラティスの区切り (python|no・medals|ha) は揃うとは限らない。
        var length = state.KanaInput ? state.Units[^1].Raw.Length : CompositionText.TailLength(state, env);
        // 消す音を含む単位より前の単位は残す (子音の後ろまで打った っ: matcha + BackSpace は まっ)
        var left = state.Units.Count;
        for (var removed = 0; left > 0 && removed < length; left--) removed += state.Units[left - 1].Raw.Length;
        kept = left;
        return length;
    }

    /// <summary>Backspace で消す原文の長さ (英字なら 1 文字、日本語の区間なら区間を読み直した最後の 1 音)。</summary>
    private static int TailLength(TextReading state, TextEnv env)
    {
        if (state.Mode is DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric) return 1;
        var segment = state.Mode == DisplayMode.Auto && CompositionText.Segments(state, env) is { Count: > 0 } segments ? segments[^1] : default;
        if (segment.IsEnglish) return 1;
        var fragment = CompositionText.Fragment(segment.Raw ?? "", env);
        return CompositionText.Pending(fragment).Length == 0 && CompositionText.Units(fragment).Count > 0 ? CompositionText.Units(fragment)[^1].Raw.Length : state.Units[^1].Raw.Length;
    }

    /// <summary>
    /// 数字のすぐ後ろの単位 (10mm、5min、3mol)。ローマ字として読める綴りでも英字のままにする
    /// (10mmで が 10っまで、5min が 5みん、50ccで が 50っcで になっていた: issue #130)。
    /// ローマ字として読めない単位 (kg、cm、px) は今までどおり英字になる。
    /// 数字の後ろでよく打つ日本語 (人 nin、個 ko、回 kai、万 man、度 do、時 ji) と同じ綴りの単位は入れない。
    /// </summary>
    private static readonly string[] UnitWords =
    [
        "mmol", "kcal", "mhz", "ghz", "khz", "kwh", "mah", "mol", "min", "sec", "rem", "dpi", "ppi", "fps", "bpm", "rpm", "mph", "kph", "ppm", "ppb", "ppt",
        "mm", "cm", "km", "nm", "um", "mg", "kg", "ml", "dl", "ms", "ns", "hz", "kb", "mb", "gb", "tb", "px", "pt", "em", "wh", "cc",
    ];

    /// <summary>
    /// 数字の後ろに打った英字 (と打ちかけ) が単位で始まるなら、その単位を英字のままの単位 (1 文字ずつ) にする。
    /// 単位の後ろにまだ文字が続くとき (10mm + で) か、確定するとき (final) だけ。より長い単位の打ちかけ (mm + o → mmol) なら待つ。
    /// </summary>
    private static void SplitUnitAfterNumber(TextReading state, TextEnv env, bool final)
    {
        // 最後の数字の位置 (その後ろが英字だけのとき)
        var digit = state.Units.Count - 1;
        while (digit >= 0 && IsLetters(state.Units[digit])) digit--;
        // 確定・変換に進んだ範囲の単位は変えない
        if (digit < 0 || digit + 1 < state.SettledUnits || state.Units[digit].Raw is not [var d] || !char.IsAsciiDigit(d)) return;
        var run = string.Concat(state.Units.Skip(digit + 1).Select(u => u.Raw)) + state.Pending;
        var lower = run.ToLowerInvariant();
        // 確定のとき、数字の後ろに母音 1 文字だけ (GPT-4o の 4o) は型番。4お にしない (issue #1417)。
        // 単位 (5min) と同じく、確定するときに決める (打っている間は 4お。続けて打つ 4oku は 4億)。
        // i だけ除くのは、数字の直後の い 1 文字が 位 (1位・2位) として日本語でよく打たれるから。
        // a・u・e・o は、数字の直後で 1 文字だけで終わる日本語の語にならない (を は wo、へ は he と打つ。円 en・億 oku などは
        // 2 文字以上続くので、この決まりに当たらない)
        var digitsStart = digit;
        while (digitsStart > 0 && state.Units[digitsStart - 1].Raw is [var earlier] && char.IsAsciiDigit(earlier)) digitsStart--;
        // 数字のすぐ前がかなとして読んだ英字 (kono|4o の no) なら、日本語の後ろの数字なので、ここでは決めない (前のかなを英字に戻さない)。
        // 数字の前の英字が 1 文字だけ (m3e) なら、英字 1 文字 + 数字 + 英字 1 文字の略語の形 (i18n・m3e) なので、ここでも決めない。
        // かなにならなかった 2 文字以上の英字 (gpt4o) は型番の名前なので対象にする
        var letters = 0;
        var afterKana = false;
        for (var k = digitsStart - 1; k >= 0 && state.Units[k].Raw is { Length: > 0 } raw && raw.All(char.IsAsciiLetter); k--)
        {
            if (state.Units[k].Kana != raw) { afterKana = true; break; }
            letters += raw.Length;
        }
        if (final && lower is "a" or "u" or "e" or "o" && letters != 1 && !afterKana)
        {
            state.Units.RemoveRange(digit + 1, state.Units.Count - digit - 1);
            state.Units.Add(new CompositionUnit(run, run));
            state.Pending.Clear();
            return;
        }
        foreach (var unit in UnitWords)
        {
            if (!lower.StartsWith(unit, StringComparison.Ordinal)) continue;
            // まだ続きを打つかもしれない。ただし同じ子音を重ねた単位 (cc) は、日本語なら っ + 次の音 で続きが要るので、
            // 打った時点で単位として見せる (50cc を打っている途中に 50っc と出ていた: issue #130)。
            if (lower.Length == unit.Length && !final && !(unit is [var c1, var c2] && c1 == c2 && UnitWords.All(u => u == unit || !u.StartsWith(unit, StringComparison.Ordinal)))) return;
            if (lower.Length > unit.Length && UnitWords.Any(u => u.Length > unit.Length && u.StartsWith(lower[..(unit.Length + 1)], StringComparison.Ordinal))) return;
            // 小文字の母音が続くなら、ローマ字の語の途中 (10mina → 10みな) かもしれないので単位にしない
            // ただし単位の最後の文字の前までがローマ字として読めない (51km|ijou の k) なら、母音とつなげても読めないので単位 (51km以上)
            if (lower.Length > unit.Length && lower[unit.Length] is 'a' or 'i' or 'u' or 'e' or 'o' &&
                env.Detector.Romaji.Analyze(lower[..(unit.Length - 1)]) is { IsValid: true, Partial: "" }) return;
            state.Units.RemoveRange(digit + 1, state.Units.Count - digit - 1);
            foreach (var letter in run[..unit.Length]) state.Units.Add(new CompositionUnit(letter.ToString(), letter.ToString()));
            state.Pending.Clear();
            state.Pending.Append(run[unit.Length..]);
            return;
        }
    }

    /// <summary>Pending のうち、音として確定した部分を単位に移す。</summary>
    private static void Normalize(TextReading state, TextEnv env, bool final)
    {
        if (final) CompositionText.SplitUnitAfterNumber(state, env, final: true);
        var romaji = env.Detector.Romaji;
        while (state.Pending.Length > 0)
        {
            var original = state.Pending.ToString();
            var analysis = romaji.AnalyzeFragment(original.ToLowerInvariant());
            var position = 0;
            foreach (var token in analysis.Tokens)
            {
                state.Units.Add(new CompositionUnit(token.Kana, original.Substring(position, token.Romaji.Length)));
                position += token.Romaji.Length;
            }

            if (analysis.IsValid)
            {
                var rest = original[position..];
                state.Pending.Clear();
                if (final && rest.Length > 0)
                {
                    // 確定時: 残った n / nn は ん、それ以外の子音は英字のまま。
                    if (rest.ToLowerInvariant() is "n" or "nn") state.Units.Add(new CompositionUnit("ん", rest));
                    else foreach (var c in rest) state.Units.Add(new CompositionUnit(c.ToString(), c.ToString()));
                }
                else
                {
                    state.Pending.Append(rest);
                }
                return;
            }

            // ローマ字として読めない文字 (google の l など) は英字 1 文字の単位にして、続きを読み直す。
            state.Units.Add(new CompositionUnit(original[position].ToString(), original[position].ToString()));
            state.Pending.Clear();
            state.Pending.Append(original[(position + 1)..]);
        }
    }

    /// <summary>数字だけ (と . , : - /) の入力か。数字は半角のまま、読点で終わらなければ Space で確定して空白を入れる (英語と同じ扱い)。</summary>
    public static bool IsNumeric(TextReading state) =>
        !state.KanaInput && state.Pending.Length == 0 && state.Units.Count > 0 && state.Units.Any(u => u.Raw.Any(char.IsAsciiDigit)) &&
        state.Units.All(u => u.Raw.All(c => char.IsAsciiDigit(c) || c is '.' or ',' or ':' or '-' or '/'));

    /// <summary>今の表示が英字 (か数字) だけか (Space で「確定して空白」にするかどうか)。</summary>
    public static bool IsAlphanumeric(TextReading state, TextEnv env) => CompositionText.IsAlphanumericAt(state, env, final: false);

    /// <summary>今の表示が英字 (か数字) だけか。final なら打ち終わったとみなして判定する (Space・Enter のとき)。</summary>
    public static bool IsAlphanumericAt(TextReading state, TextEnv env, bool final) => state.Mode switch
    {
        DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric => true,
        DisplayMode.Auto => CompositionText.IsNumeric(state) ? CompositionText.Numeric(state, env).Spans[^1].Kind == SpanKind.English : CompositionText.Segments(state, env).All(s => s.IsEnglish),
        _ => false,
    };

    /// <summary>Markdown 記法の記号だけ (- > * + # ## …) の入力か。Space で、半角の記号と空白で確定する。</summary>
    public static bool IsMarkdownMarker(CompositionTextState state) =>
        !state.KanaInput && !state.IsEmpty &&
        (state.TypedRaw is "-" or ">" or "*" or "+" || (state.TypedRaw.Length <= 6 && state.TypedRaw.All(c => c == '#')));  // 見出しは 6 段まで

    /// <summary>水平線・front matter の区切り (3 個以上の -) だけの入力か。打った半角のまま表示・確定する。</summary>
    public static bool IsHorizontalRule(TextReading state) => !state.KanaInput && CompositionText.Raw(state).Length >= 3 && CompositionText.Raw(state).All(c => c == '-');

    /// <summary>表記の規則が見る打った文字。かな入力では、打ったキーの英字・記号は入力した文字でないので空。</summary>
    private static string TypedForNotation(TextReading state) => state.KanaInput ? "" : CompositionText.Raw(state);

    /// <summary>変換の最初の候補を、入力中の表示と同じ半角にする (* が 2 個以上・---)。当てはまらなければそのまま返す。</summary>
    public static string AsDisplayedHalfWidth(TextReading state, string candidate, string? clauseRaw) =>
        CompositionText.IsHorizontalRule(state) ? clauseRaw ?? candidate : NotationRules.Show(ComposedText.Of(SpanKind.Japanese, candidate), TypedForNotation(state)).Text;

}
