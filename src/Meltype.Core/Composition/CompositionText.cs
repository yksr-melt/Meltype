// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
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
    /// 打った英字を読み直す断片は、読み直す元の入力の文脈を使う。
    /// </summary>
    public static bool? LearningContext(TextEnv env) =>
        env.Fragment is { } origin ? origin.LearningContext : env.Settings.ContextualLearning ? env.Context().PrecedingEnglish : null;

    /// <summary>
    /// 打った英字の一部を読み直すために作る断片に渡すもの。env は読み直す元の入力のもの。
    /// 断片は設定によらず既定の設定 (自動判定・z の記号・/ のまま) で読み、打ち間違いを直さない。学習の文脈は元の入力のもの。
    /// </summary>
    public static TextEnv FragmentEnv(TextEnv env) =>
        new(env.Detector, FragmentSettings, null) { Fragment = new FragmentOrigin(LearningContext(env)), Context = () => TextContext.None };

    private static readonly Config.Settings FragmentSettings = new();

    public static IReadOnlyList<CompositionUnit> Units(CompositionTextState state) => state.Units;
    public static string Pending(CompositionTextState state) => state.Pending.ToString();
    public static bool IsEmpty(CompositionTextState state) => state.Units.Count == 0 && state.Pending.Length == 0;

    private static bool EffectiveManual(CompositionTextState state, TextEnv env) => !state.SuggestionAccepted && !env.Settings.AutoDetection;

    /// <summary>
    /// かな入力の 1 キー。raw はそのキーの英字 (英単語の判定と、英語として見せるときに使う)。
    /// 濁点・半濁点は直前のかなに付ける (か + ゛ → が)。
    /// </summary>
    public static void AppendKana(CompositionTextState state, char raw, char kana)
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

    /// <summary>打った文字そのもの。</summary>
    public static string Raw(CompositionTextState state) => string.Concat(state.Units.Select(u => u.Raw)) + state.Pending;

    public static void Append(CompositionTextState state, TextEnv env, char c)
    {
        // 英数字の間に打った - は略語の区切り。入力直後は後続文字が分からないため、次の英数字が来た時点で長音として読んだ - を半角に戻す (J-core)。
        if (char.IsAsciiLetterOrDigit(c) && state.Units.Count >= 2 && state.Units[^1].Raw == "-" &&
            state.Units[^2].Raw.Length > 0 && char.IsAsciiLetterOrDigit(state.Units[^2].Raw[^1]))
        {
            // かなにならずに残った英字 (p-hub の p、kyouhapt-query-digest の t) の後の - は長音ではない。かなになった ん (n-to) の後は長音
            var unconverted = state.Units[^2].Kana == state.Units[^2].Raw && state.Units[^2].Raw.All(char.IsAsciiLetterLower);
            if (unconverted || TypedMarks.JoinsName(state.Units, state.Units.Count - 1, env)) state.Units[^1] = state.Units[^1] with { Kana = "-" };
        }
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
            while (state.Units.Count > 0 && state.Units[^1] is { Raw.Length: 1 } last && last.Kana == last.Raw && char.IsAsciiLetter(last.Raw[0]) &&
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
        // 数字の前では打ち間違いを直さない (rta2026・ps5・win11 の英字は略語)。数字の後ろの単位を英字のままにするのは FixTypos の中
        if (!char.IsAsciiDigit(c)) CompositionText.FixTypos(state, env);
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
    public static bool IsSlashAtStart(CompositionTextState state) => state.Pending.Length == 0 && state.Units is [{ Kana: "・", Raw: "/" }];

    /// <summary>1 音 (または 1 文字) 消す。入力途中の子音があればそれを 1 文字消す。</summary>
    public static void RemoveLast(CompositionTextState state, TextEnv env)
    {
        // 保護範囲 (メンション・URL・パス) の中は、1 音 (く・ら…) ではなく打った原文の 1 文字ずつ消す。
        if (!state.KanaInput && (CompositionText.HasProtectedTail(state) || state.Pending.Length == 0 && state.Units.Count > 0 &&
            CompositionText.ProtectedSpans(state).Any(span => span.End > CompositionText.Raw(state).Length - state.Units[^1].Raw.Length)))
        {
            var raw = CompositionText.Raw(state);
            if (raw.Length > 0)
            {
                var truncated = raw[..^1];
                state.Units.Clear();
                state.Pending.Clear();
                foreach (var c in truncated) CompositionText.Append(state, env, c);
                if (CompositionText.IsEmpty(state))
                {
                    state.Mode = DisplayMode.Auto;
                    state.SuggestionAccepted = false;
                }
                return;
            }
        }
        if (state.Pending.Length > 0 && state.KanaInput) state.Pending.Length--;
        else if (state.Pending.Length > 0)
        {
            // 打ちかけの子音を 1 文字消し、残った原文を打ち直す。後ろのキーを見て決めた単位 (ff の っ、nk の ん) は、
            // そのキーを消したら決め直す (ff + BackSpace + uronto が っうろんと、nk + BackSpace + a が んあ になっていた: issue #1636)
            var truncated = CompositionText.Raw(state)[..^1];
            state.Units.Clear();
            state.Pending.Clear();
            foreach (var c in truncated) CompositionText.Append(state, env, c);
        }
        else if (state.Units.Count > 0)
        {
            // 見えている最後の 1 音 (英字なら 1 文字) の分だけ後ろから単位を外し、外しすぎた原文を打ち直す。
            // 単位 (nn|o・sha) とラティスの区切り (python|no・medals|ha) は揃うとは限らない。
            var length = state.KanaInput ? state.Units[^1].Raw.Length : CompositionText.TailLength(state, env);
            var removed = "";
            while (state.Units.Count > 0 && removed.Length < length)
            {
                removed = state.Units[^1].Raw + removed;
                state.Units.RemoveAt(state.Units.Count - 1);
            }
            foreach (var c in removed[..^length]) CompositionText.Append(state, env, c);
        }
        if (CompositionText.IsEmpty(state))
        {
            state.Mode = DisplayMode.Auto;
            state.Case = LetterCase.AsTyped;
            state.SuggestionAccepted = false;
        }
    }

    /// <summary>Backspace で消す原文の長さ (英字なら 1 文字、日本語の区間なら区間を読み直した最後の 1 音)。</summary>
    private static int TailLength(CompositionTextState state, TextEnv env)
    {
        if (state.Mode is DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric) return 1;
        var segment = state.Mode == DisplayMode.Auto && CompositionText.Segments(state, env) is { Count: > 0 } segments ? segments[^1] : default;
        if (segment.IsEnglish) return 1;
        CompositionTextState fragment = new CompositionTextState();
        var fragmentEnv = FragmentEnv(env);
        foreach (var c in segment.Raw ?? "") CompositionText.Append(fragment, fragmentEnv, c);
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
    private static void SplitUnitAfterNumber(CompositionTextState state, TextEnv env, bool final)
    {
        // 最後の数字の位置 (その後ろが英字だけのとき)
        var digit = state.Units.Count - 1;
        while (digit >= 0 && IsLetters(state.Units[digit])) digit--;
        if (digit < 0 || state.Units[digit].Raw is not [var d] || !char.IsAsciiDigit(d)) return;
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
    private static void Normalize(CompositionTextState state, TextEnv env, bool final)
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
    public static bool IsNumeric(CompositionTextState state) =>
        !state.KanaInput && state.Pending.Length == 0 && state.Units.Count > 0 && state.Units.Any(u => u.Raw.Any(char.IsAsciiDigit)) &&
        state.Units.All(u => u.Raw.All(c => char.IsAsciiDigit(c) || c is '.' or ',' or ':' or '-' or '/'));

    /// <summary>今の表示が英字 (か数字) だけか (Space で「確定して空白」にするかどうか)。</summary>
    public static bool IsAlphanumeric(CompositionTextState state, TextEnv env) => CompositionText.IsAlphanumericAt(state, env, final: false);

    /// <summary>今の表示が英字 (か数字) だけか。final なら打ち終わったとみなして判定する (Space・Enter のとき)。</summary>
    public static bool IsAlphanumericAt(CompositionTextState state, TextEnv env, bool final) => state.Mode switch
    {
        DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric => true,
        DisplayMode.Auto => CompositionText.IsNumeric(state) ? CompositionText.Numeric(state, env).Spans[^1].Kind == SpanKind.English : CompositionText.Segments(state, env).All(s => s.IsEnglish),
        _ => false,
    };

    /// <summary>Markdown 記法の記号だけ (- > * + # ## …) の入力か。Space で、半角の記号と空白で確定する。</summary>
    public static bool IsMarkdownMarker(CompositionTextState state) =>
        !state.KanaInput && state.Units.Count > 0 &&
        (CompositionText.Raw(state) is "-" or ">" or "*" or "+" || (CompositionText.Raw(state).Length <= 6 && CompositionText.Raw(state).All(c => c == '#')));  // 見出しは 6 段まで

    /// <summary>水平線・front matter の区切り (3 個以上の -) だけの入力か。打った半角のまま表示・確定する。</summary>
    public static bool IsHorizontalRule(CompositionTextState state) => !state.KanaInput && CompositionText.Raw(state).Length >= 3 && CompositionText.Raw(state).All(c => c == '-');

    /// <summary>表記の規則が見る打った文字。かな入力では、打ったキーの英字・記号は入力した文字でないので空。</summary>
    private static string TypedForNotation(CompositionTextState state) => state.KanaInput ? "" : CompositionText.Raw(state);

    /// <summary>変換の最初の候補を、入力中の表示と同じ半角にする (* が 2 個以上・---)。当てはまらなければそのまま返す。</summary>
    public static string AsDisplayedHalfWidth(CompositionTextState state, string candidate, string? clauseRaw) =>
        CompositionText.IsHorizontalRule(state) ? clauseRaw ?? candidate : NotationRules.Show(ComposedText.Of(SpanKind.Japanese, candidate), TypedForNotation(state)).Text;

}
