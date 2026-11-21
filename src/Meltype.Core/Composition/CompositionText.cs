// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Libraries.Chars;

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
internal static class CompositionText
{
    /// <summary>
    /// 英語 / 日本語の学習を引く文脈: 前が英語か (true)・日本語か (false)。設定「英語 / 日本語の学習を前の文脈ごとに分ける」が OFF なら null。
    /// 打った英字を読み直す断片は、読み直す元の入力の文脈を使う。
    /// </summary>
    public static bool? LearningContext(CompositionTextState state, TextEnv env) =>
        env.Fragment is { } origin ? origin.LearningContext : env.Settings.ContextualLearning ? env.Context().PrecedingEnglish : null;

    /// <summary>
    /// 打った英字の一部を読み直すために作る断片に渡すもの。state は読み直す元の入力。
    /// 断片は設定によらず既定の設定 (自動判定・z の記号・/ のまま) で読み、打ち間違いを直さない。学習の文脈は元の入力のもの。
    /// </summary>
    public static TextEnv FragmentEnv(CompositionTextState state, TextEnv env) =>
        new(env.Detector, FragmentSettings, null) { Fragment = new FragmentOrigin(LearningContext(state, env)), Context = () => TextContext.None };

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
        // 英数字の間に打った . / - はドメイン名や略語の区切り。入力直後は後続文字が分からないため、
        // 次の英数字が来た時点で句点・長音として読んだ記号を半角に戻す (tetr.io、J-core)。
        if (char.IsAsciiLetterOrDigit(c) && state.Units.Count >= 2 && (state.Units[^1].Raw is "." or "-") &&
            state.Units[^2].Raw.Length > 0 && char.IsAsciiLetterOrDigit(state.Units[^2].Raw[^1]))
        {
            var preceding = new StringBuilder();
            for (var i = state.Units.Count - 2; i >= 0 && state.Units[i].Raw.Length > 0 && state.Units[i].Raw.All(char.IsAsciiLetterOrDigit); i--) preceding.Insert(0, state.Units[i].Raw);
            var word = preceding.ToString();
            var lower = word.ToLowerInvariant();
            var acronym = word.Any(char.IsAsciiLetterUpper) && word.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c));
            var knownUnambiguous = word.Length >= 3 && env.Detector.IsKnownEnglishWord(LearningContext(state, env), word) && !env.Detector.Romaji.AnalyzeFragment(lower).IsValid;
            var properNoun = env.Detector.ProperNouns.Contains(lower) && (state.Units[^1].Raw != "-" || word.Any(char.IsAsciiLetterUpper) || !env.Detector.Romaji.AnalyzeFragment(lower).IsValid);
            // かなにならずに残った英字 (p-hub の p、kyouhapt-query-digest の t) の後の - は長音ではない。かなになった ん (n-to) の後は長音
            var unconverted = state.Units[^2].Kana == state.Units[^2].Raw && state.Units[^2].Raw.All(char.IsAsciiLetterLower);
            if (acronym || knownUnambiguous || properNoun || state.Units[^1].Raw == "-" && unconverted)
                state.Units[^1] = state.Units[^1] with { Kana = state.Units[^1].Raw };
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
        // 数字の後の , は、次も数字なら桁区切り (1,000) のまま、それ以外なら読点 (x64、arm64)。
        if (!char.IsAsciiDigit(c) && state.Pending.Length == 0 && state.Units.Count >= 2 && state.Units[^1] is { Raw: ",", Kana: "," } &&
            state.Units[^2].Raw is [var digit] && char.IsAsciiDigit(digit))
        {
            state.Units[^1] = state.Units[^1] with { Kana = "、" };
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
        // 数字の後の . と , は小数点・桁区切り (GPL3.0、1,000)。句点・読点にしない。
        if (c is ',' or '.' && state.Units.Count > 0 && state.Units[^1].Raw is [var previous] && char.IsAsciiDigit(previous))
        {
            state.Units.Add(new CompositionUnit(c.ToString(), c.ToString()));
            return;
        }
        // 、 や 。 を 3 つ続けたら ... にする (、、、 → ...)。
        if (c is ',' or '.')
        {
            var run = 0;
            while (run < state.Units.Count && state.Units[^(run + 1)].Raw is "," or ".") run++;
            if (run >= 2)
            {
                for (var k = state.Units.Count - run; k < state.Units.Count; k++) state.Units[k] = state.Units[k] with { Kana = "." };
                state.Units.Add(new CompositionUnit(".", c.ToString()));
                return;
            }
        }
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
                    state.SlashAtStart = false;
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
            state.SlashAtStart = false;
        }
    }

    /// <summary>Backspace で消す原文の長さ (英字なら 1 文字、日本語の区間なら区間を読み直した最後の 1 音)。</summary>
    private static int TailLength(CompositionTextState state, TextEnv env)
    {
        if (state.Mode is DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric) return 1;
        var segment = state.Mode == DisplayMode.Auto && CompositionText.Segments(state, env) is { Count: > 0 } segments ? segments[^1] : default;
        if (segment.IsEnglish) return 1;
        CompositionTextState fragment = new CompositionTextState();
        var fragmentEnv = FragmentEnv(state, env);
        foreach (var c in segment.Raw ?? "") CompositionText.Append(fragment, fragmentEnv, c);
        return CompositionText.Pending(fragment).Length == 0 && CompositionText.Units(fragment).Count > 0 ? CompositionText.Units(fragment)[^1].Raw.Length : state.Units[^1].Raw.Length;
    }

    public static void Clear(CompositionTextState state)
    {
        state.Units.Clear();
        state.Pending.Clear();
        state.ProtectedCache = null;
        state.ProtectedCacheRaw = null;
        state.Mode = DisplayMode.Auto;
        state.Case = LetterCase.AsTyped;
        state.SuggestionAccepted = false;
        state.SlashAtStart = false;
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
        while (digit >= 0 && IsLetters(state.Units[digit].Raw)) digit--;
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

    /// <summary>数字だけ (と . , : - /) の入力か。数字は半角のまま、Space で確定して空白を入れる (英語と同じ扱い)。</summary>
    public static bool IsNumeric(CompositionTextState state) =>
        !state.KanaInput && state.Pending.Length == 0 && state.Units.Count > 0 && state.Units.Any(u => u.Raw.Any(char.IsAsciiDigit)) &&
        state.Units.All(u => u.Raw.All(c => char.IsAsciiDigit(c) || c is '.' or ',' or ':' or '-' or '/'));

    /// <summary>今の表示が英字 (か数字) だけか (Space で「確定して空白」にするかどうか)。</summary>
    public static bool IsAlphanumeric(CompositionTextState state, TextEnv env) => CompositionText.IsAlphanumericAt(state, env, final: false);

    /// <summary>今の表示が英字 (か数字) だけか。final なら打ち終わったとみなして判定する (Space・Enter のとき)。</summary>
    public static bool IsAlphanumericAt(CompositionTextState state, TextEnv env, bool final) => state.Mode switch
    {
        DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric => true,
        DisplayMode.Auto => CompositionText.IsNumeric(state) || CompositionText.Segments(state, env).All(s => s.IsEnglish),
        _ => false,
    };

    /// <summary>Markdown 記法の記号だけ (- > * + # ## …) の入力か。Space で、半角の記号と空白で確定する。</summary>
    public static bool IsMarkdownMarker(CompositionTextState state) =>
        !state.KanaInput && state.Units.Count > 0 &&
        (CompositionText.Raw(state) is "-" or ">" or "*" or "+" || (CompositionText.Raw(state).Length <= 6 && CompositionText.Raw(state).All(c => c == '#')));  // 見出しは 6 段まで

    /// <summary>水平線・front matter の区切り (3 個以上の -) だけの入力か。打った半角のまま表示・確定する。</summary>
    public static bool IsHorizontalRule(CompositionTextState state) => !state.KanaInput && CompositionText.Raw(state).Length >= 3 && CompositionText.Raw(state).All(c => c == '-');

    /// <summary>変換区間に * が 2 個以上あるときだけ、表示の ＊ を打った半角の * にする (強調 **bold**・*italic*)。</summary>
    private static string HalfWidthStars(CompositionTextState state, string text) => !state.KanaInput && CompositionText.Raw(state).Count(c => c == '*') >= 2 ? text.Replace('＊', '*') : text;

    /// <summary>変換の最初の候補を、入力中の表示と同じ半角にする (* が 2 個以上・---)。当てはまらなければそのまま返す。</summary>
    public static string AsDisplayedHalfWidth(CompositionTextState state, string candidate, string? clauseRaw) =>
        CompositionText.IsHorizontalRule(state) ? clauseRaw ?? candidate : CompositionText.HalfWidthStars(state, candidate);

    /// <summary>自動判定の区間分け (Mode が Auto のときに使う)。</summary>
    /// 入力中の表示と、Space・Enter で確定・変換するときで同じ区切りになる。
    public static IReadOnlyList<CompositionSegment> Segments(CompositionTextState state, TextEnv env)
    {
        // 1 キーごとに、表示・打ち間違いの直し・Space の扱いなどで何度も呼ばれる。区間分けは重い (打った文字のあらゆる区間を調べる) ので、
        // 打った内容と判定の条件が同じなら前の結果を使い回す。
        var key = CompositionText.SegmentsKey(state, env);
        if (state.SegmentsCache is { } cached && cached.Key == key && ReferenceEquals(cached.Memory, env.Detector.Memory))
        {
            return cached.Segments;
        }
        // 区切りを選んだ理由も、区切りと同じ計算から取る (確定のログが、実際に確定した区切りと食い違わないように。issue #1644)
        var reasons = new List<Func<string>>();
        var result = CompositionText.Adjusted(state, env, CompositionText.ComputeSegments(state, env, reasons)[0], reasons);
        state.SegmentsCache = (key, env.Detector.Memory, result, reasons);
        return result;
    }

    /// <summary>区間分けに、設定の区間の直し (全角の括弧・かな + w の笑い) をかける。直したら理由を reasons に足す。</summary>
    private static IReadOnlyList<CompositionSegment> Adjusted(CompositionTextState state, TextEnv env, IReadOnlyList<CompositionSegment> result, List<Func<string>> reasons)
    {
        if (state.FullWidthParentheses && result.Any(s => s.IsEnglish && s.Raw is "(" or ")"))
        {
            result = [.. result.Select(s => s.IsEnglish && s.Raw is "(" or ")" ? new CompositionSegment(false, s.Raw == "(" ? "（" : "）", s.Raw) : s)];
            reasons.Add(() => "設定「カッコを全角で入力」で ( ) を全角に");
        }
        if (state.LaughterAfterKana && CompositionText.SplitLaughter(state, env, result) is var split && !ReferenceEquals(split, result))
        {
            result = split;
            reasons.Add(() => "設定「かな + w を笑い (ねw・さw) にする」で かな + w に分けた");
        }
        return result;
    }

    /// <summary>
    /// 変換用の区間分けと、その英日の区切りの別案 (軽い順に count 案まで。先頭は <see cref="ConversionSegments"/>)。
    /// 案は <see cref="Segments"/> と同じ順番で作る (<see cref="ComputeSegments"/> → <see cref="Adjusted"/>)。
    /// 別案の区切りの理由は確定のログに書かない (確定のログは <see cref="Segments"/> の理由)。
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<CompositionSegment>> ConversionSegmentAlternatives(CompositionTextState state, TextEnv env, int count)
    {
        var alternatives = new List<IReadOnlyList<CompositionSegment>>();
        foreach (var segments in CompositionText.ComputeSegments(state, env, [], count).Select(a => CompositionText.WithFinalPending(state, env, CompositionText.Adjusted(state, env, a, []))))
            if (!alternatives.Any(a => a.SequenceEqual(segments))) alternatives.Add(segments);
        return alternatives;
    }

    /// <summary>打った英字をすべてローマ字として読んだ、日本語の 1 区間の区間分け (botto → ぼっと)。ローマ字として読めない英字は残る (google → ごおgぇ)。</summary>
    public static IReadOnlyList<CompositionSegment> AllJapaneseSegments(CompositionTextState state, TextEnv env) => [new CompositionSegment(false, CompositionText.AllKana(state, env, final: true), CompositionText.Raw(state))];

    /// <summary>今の区切り (Segments) を選んだ理由。確定のログに書く (issue #1644)。</summary>
    public static string ExplainSegments(CompositionTextState state, TextEnv env)
    {
        CompositionText.Segments(state, env);
        return string.Join("; ", state.SegmentsCache!.Value.Reasons.Select(r => r()));
    }

    /// <summary>
    /// 設定「かな + w を笑いにする」: 英語の区間が、1 つのかなの音 + w (new・saw) だけなら、かな (ね・さ) と笑いの w に分ける (issue #713・#492)。
    /// 前が日本語 (またはなにも無い) ときだけ。前が英語の文 (I saw) のときは、英単語のまま。
    /// </summary>
    private static IReadOnlyList<CompositionSegment> SplitLaughter(CompositionTextState state, TextEnv env, IReadOnlyList<CompositionSegment> segments)
    {
        List<CompositionSegment>? changed = null;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.IsEnglish && segment.Raw.Length == 3 && segment.Raw[2] is 'w' && !(i > 0 ? segments[i - 1].IsEnglish : env.Context().PrecedingEnglishSentence) &&
                char.IsAsciiLetterLower(segment.Raw[0]) && char.IsAsciiLetterLower(segment.Raw[1]) &&
                env.Detector.Romaji.Analyze(segment.Raw[..2]) is { IsValid: true, Partial: "" } reading && reading.Kana.Length == 1)
            {
                // 前の分割で要素が増えた分、元の i とずれる。ずれ込んだ位置で書き換える (2 つ目以降が別の区間を書き換えていた)
                changed ??= [.. segments];
                var at = i + (changed.Count - segments.Count);
                changed[at] = new CompositionSegment(false, reading.Kana, segment.Raw[..2]);
                changed.Insert(at + 1, new CompositionSegment(true, "w", "w"));
            }
        }
        return changed ?? segments;
    }

    /// <summary>区間分けの結果を決めるもの (打った単位・入力途中の子音・前後・自動判定を使うか・学習した語の版) をつないだもの。</summary>
    private static string SegmentsKey(CompositionTextState state, TextEnv env)
    {
        var key = new StringBuilder();
        foreach (var unit in state.Units) key.Append(unit.Kana).Append('\u0001').Append(unit.Raw).Append('\u0002');
        key.Append('\u0003').Append(state.Pending).Append('\u0003')
            .Append(env.Context().PrecedingEnglish switch { true => 'E', false => 'J', null => '-' })
            .Append(env.Context().FollowingEnglish switch { true => 'E', false => 'J', null => '-' })
            .Append(env.Context().PrecedingEnglishSentence ? 'S' : '-').Append(state.KanaInput ? 'K' : '-')
            .Append(env.Context().PrecedingEnglishName ? 'N' : '-').Append(state.FullWidthParentheses ? 'W' : '-').Append(state.LaughterAfterKana ? 'L' : '-')
            .Append(CompositionText.EffectiveManual(state, env) ? 'M' : '-').Append(':').Append(env.Detector.Memory?.Version ?? -1).Append(':').Append(LearningContext(state, env) switch { true => 'e', false => 'j', null => '-' });
        return key.ToString();
    }

    /// <summary>
    /// 名前を紹介する英文の名前か。辞書にないローマ字の名前も英字で残す。
    /// 手動・かな入力・明示的な日本語学習・後ろの日本語は優先する。
    /// </summary>
    private static bool IsNameInEnglishSentence(CompositionTextState state, TextEnv env) =>
        !state.KanaInput && env.Context().PrecedingEnglishName && env.Context().FollowingEnglish != false && !CompositionText.EffectiveManual(state, env) &&
        CompositionText.Raw(state) is { Length: > 0 } raw && raw.All(char.IsAsciiLetter) && env.Detector.Memory?.Get(raw.ToLowerInvariant(), LearningContext(state, env)) != false;

    /// <summary>
    /// 区間分けと、その英日の区切りの別案 (軽い順に count 案まで。先頭が表示・確定に使う区切り)。reasons には先頭の区切りの理由を足す。
    /// 別案は <see cref="CompositionDetector.SegmentAlternatives"/> の案。
    /// 英文の中の名前・保護区間・半角の開き括弧・手動のように、ラティスの外の決まりで区切ったときは別案を出さない
    /// (決まりで決めた区切りを、ラティスの案で崩さない)。
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<CompositionSegment>> ComputeSegments(CompositionTextState state, TextEnv env, List<Func<string>> reasons, int count = 1)
    {
        if (CompositionText.IsNameInEnglishSentence(state, env))
        {
            reasons.Add(() => "名前を紹介する英文の続きなので英字のまま");
            return [[new CompositionSegment(true, "", CompositionText.Raw(state))]];
        }
        var alternatives = CompositionText.SegmentWithProtection(state, env, state.Units, reasons, CompositionText.EffectiveManual(state, env) ? 1 : count);
        if (CompositionText.HalfWidthOpeners(state, alternatives[0]) is { } adjusted)
        {
            // 区切り直した結果だけが確定するので、最初の区切りの理由は捨てる
            reasons.Clear();
            reasons.Add(() => "後ろが英語なので開きの記号を半角にして区切り直した");
            return CompositionText.SegmentWithProtection(state, env, adjusted, reasons, 1);
        }
        return alternatives;
    }

    /// <summary>保護区間を英字のまま区切る。保護区間があれば別案は出さない (1 案だけ)。</summary>
    private static IReadOnlyList<IReadOnlyList<CompositionSegment>> SegmentWithProtection(CompositionTextState state, TextEnv env, IReadOnlyList<CompositionUnit> units, List<Func<string>> reasons, int count)
    {
        var spans = CompositionText.ProtectedSpans(state);
        if (spans.Count == 0)
            return env.Detector.SegmentAlternatives(LearningContext(state, env), units, CompositionText.Pending(state), env.Context().PrecedingEnglish, env.Context().FollowingEnglish, CompositionText.EffectiveManual(state, env), env.Context().PrecedingEnglishSentence, state.KanaInput, count, reasons);
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
            var segments = env.Detector.Segment(LearningContext(state, env), gapUnits, pending, from == 0 ? env.Context().PrecedingEnglish : false,
                to == raw.Length ? env.Context().FollowingEnglish : false, CompositionText.EffectiveManual(state, env),
                from == 0 && env.Context().PrecedingEnglishSentence, state.KanaInput, reasons);
            // 内部の区切りの子音は後の保護文字列とは結合しない。末尾の Pending は表示側が扱う。
            if (to < raw.Length && pending.Length > 0 && !segments[^1].IsEnglish)
                segments = [.. segments.Take(segments.Count - 1), segments[^1] with
                    { Kana = segments[^1].Kana + env.Detector.Romaji.ConvertLenient(pending.ToLowerInvariant(), final: true) }];
            result.AddRange(segments);
        }
    }

    private static List<CompositionUnit> UnitsInRawRange(CompositionTextState state, TextEnv env, IReadOnlyList<CompositionUnit> units, int from, int to, out string pending)
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
            CompositionTextState fragment = new CompositionTextState();
            var fragmentEnv = FragmentEnv(state, env);
            foreach (var c in CompositionText.Raw(state)[from..to]) CompositionText.Append(fragment, fragmentEnv, c);
            pending = CompositionText.Pending(fragment);
            return [.. CompositionText.Units(fragment)];
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
    /// すぐ後ろが英語 ((Ooh) か、対になる閉じの記号が半角 ("…Go Through!") なら、開きの記号も半角にした単位の並びを返す。変えなければ null。
    /// ("打ち上げ話はGo Through!" の最初の " だけ全角 ” になっていた、(Ooh … wow…) の ( だけ全角 （ になっていた)
    /// </summary>
    private static List<CompositionUnit>? HalfWidthOpeners(CompositionTextState state, IReadOnlyList<CompositionSegment> segments)
    {
        if (state.KanaInput || !state.Units.Any(u => Openers.ContainsKey(u.Raw) && u.Kana != u.Raw)) return null;
        // 打った英字の 1 文字ずつが英語の区間か
        var english = new List<bool>();
        foreach (var segment in segments) foreach (var _ in segment.Raw) english.Add(segment.IsEnglish);
        var offsets = new int[state.Units.Count];
        for (int i = 0, offset = 0; i < state.Units.Count; offset += state.Units[i].Raw.Length, i++) offsets[i] = offset;
        bool IsHalf(int i) => state.Units[i].Kana == state.Units[i].Raw || (offsets[i] < english.Count && english[offsets[i]]);

        List<CompositionUnit>? adjusted = null;
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
            adjusted ??= [.. state.Units];
            adjusted[i] = state.Units[i] with { Kana = state.Units[i].Raw };
            for (var k = afterEnglish + 1; afterEnglish >= 0 && k <= closing; k++) adjusted[k] = state.Units[k] with { Kana = state.Units[k].Raw };
        }
        return adjusted;
    }

    /// <summary>自動判定を使わないとき、自動判定なら英字にする部分 (提案)。無ければ null。</summary>
    public static string? Suggestion(CompositionTextState state, TextEnv env)
    {
        if (state.Mode != DisplayMode.Auto || !CompositionText.EffectiveManual(state, env) || CompositionText.IsNumeric(state)) return null;
        var suggested = env.Detector.Segment(LearningContext(state, env), state.Units, CompositionText.Pending(state), env.Context().PrecedingEnglish, env.Context().FollowingEnglish, false, env.Context().PrecedingEnglishSentence, state.KanaInput);
        if (!suggested.Any(s => s.IsEnglish) || suggested.SequenceEqual(CompositionText.Segments(state, env))) return null;
        return string.Join(" ", suggested.Where(s => s.IsEnglish).Select(s => s.Raw));
    }

    /// <summary>変換用の区間分け。末尾の入力途中の子音は確定扱い (n → ん) にして日本語区間の読みに含める。</summary>
    public static IReadOnlyList<CompositionSegment> ConversionSegments(CompositionTextState state, TextEnv env) => CompositionText.WithFinalPending(state, env, CompositionText.Segments(state, env));

    private static List<CompositionSegment> WithFinalPending(CompositionTextState state, TextEnv env, IReadOnlyList<CompositionSegment> segments)
    {
        var result = segments.ToList();
        if (result.Count > 0 && !result[^1].IsEnglish)
        {
            result[^1] = result[^1] with { Kana = result[^1].Kana + CompositionText.PendingText(state, env, final: true) };
        }
        return result;
    }

    /// <summary>
    /// 変換用の区間分け segments (<see cref="ConversionSegmentAlternatives"/> の 1 案) の segmentIndex 番目 (日本語の区間) の読みのうち [start, start + length) に対応する、打った英字
    /// (あぴ → api)。変換の候補に「打ったままの英字」を出すのに使う。読みの区切りが 1 音の区切りと合わないときや、かな入力では null。
    /// </summary>
    public static string? RawForReading(CompositionTextState state, TextEnv env, IReadOnlyList<CompositionSegment> segments, int segmentIndex, int start, int length)
    {
        if (state.KanaInput) return null;
        if (segmentIndex < 0 || segmentIndex >= segments.Count) return null;
        var from = segments.Take(segmentIndex).Sum(s => s.Raw.Length);
        var units = CompositionText.UnitsInRawRange(state, env, state.Units, from, from + segments[segmentIndex].Raw.Length, out var pending);
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
    public static IReadOnlyList<string?> RawForKanas(CompositionTextState state, TextEnv env, IReadOnlyList<string> kanas)
    {
        var none = new string?[kanas.Count];
        if (state.KanaInput) return none;
        var units = CompositionText.UnitsInRawRange(state, env, state.Units, 0, CompositionText.Raw(state).Length, out var pending);
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
    public static string? KanaForSegment(CompositionTextState state, TextEnv env, IReadOnlyList<CompositionSegment> segments, int segmentIndex)
    {
        if (segmentIndex < 0 || segmentIndex >= segments.Count) return null;
        var start = segments.Take(segmentIndex).Sum(segment => segment.Raw.Length);
        var end = start + segments[segmentIndex].Raw.Length;
        var unitsEnd = CompositionText.Raw(state).Length - state.Pending.Length;
        var builder = new StringBuilder();
        if (CompositionText.UnitIndexAt(state, start) is int first && CompositionText.UnitIndexAt(state, Math.Min(end, unitsEnd)) is int last)
        {
            for (var unit = first; unit < last; unit++) builder.Append(state.Units[unit].Kana);
            if (end > unitsEnd) builder.Append(CompositionText.PendingText(state, env, final: true));
        }
        else
        {
            // ラティスが単位の途中で区切った (medals|ha の sha): 区間の打った英字を読み直す
            CompositionTextState fragment = new CompositionTextState();
            var fragmentEnv = FragmentEnv(state, env);
            foreach (var c in CompositionText.Raw(state)[start..end]) CompositionText.Append(fragment, fragmentEnv, c);
            foreach (var unit in fragment.Units) builder.Append(unit.Kana);
            builder.Append(CompositionText.PendingText(fragment, fragmentEnv, final: true));
        }
        return builder.Length > 0 ? builder.ToString() : null;
    }

    /// <summary>打った英字の offset 文字目が単位の境目なら、そこから始まる単位の番号 (末尾なら単位の数)。</summary>
    private static int? UnitIndexAt(CompositionTextState state, int offset)
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

    /// <param name="final">確定・変換のときは true (語末の n を ん にする)。</param>
    /// <param name="convert">日本語の区間 (かな, 前の区間が英語か, 変換ボックスの中でその区間より前に出ている文字) を漢字に変換する関数 (ライブ変換)。null ならかなのまま。</param>
    public static string Display(CompositionTextState state, TextEnv env, bool final, Func<string, bool, string, string>? convert = null) => state.Mode switch
    {
        DisplayMode.HalfWidthAlphanumeric => ApplyCase(CompositionText.Raw(state), state.Case),
        DisplayMode.FullWidthAlphanumeric => ToFullWidth(ApplyCase(CompositionText.Raw(state), state.Case)),
        DisplayMode.Hiragana => CompositionText.AllKana(state, env, final),
        DisplayMode.Katakana => ToKatakana(CompositionText.AllKana(state, env, final)),
        DisplayMode.HalfWidthKatakana => ToHalfWidthKatakana(ToKatakana(CompositionText.AllKana(state, env, final))),
        _ => CompositionText.IsNumeric(state) || CompositionText.IsHorizontalRule(state) ? CompositionText.Raw(state) : CompositionText.RenderSegments(state, env, final, convert),
    };

    /// <summary>英字の大文字・小文字を変える (ai → AI / Ai)。</summary>
    public static string ApplyCase(string text, LetterCase letterCase) => letterCase switch
    {
        LetterCase.Upper => text.ToUpperInvariant(),
        LetterCase.Capitalized => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant(),
        _ => text,
    };

    /// <summary>今の原文の保護区間 (原文が変わったら再計算する)。</summary>
    public static IReadOnlyList<ProtectedSpan> ProtectedSpans(CompositionTextState state)
    {
        // かな入力では "@" は濁点などのキーで、ローマ字の原文とは意味が違う。保護判定はローマ字入力だけにする。
        if (state.KanaInput) return [];
        var raw = CompositionText.Raw(state);
        // 打ち始めの / を ・ にしたときは、続けて打った文字と合わせて / から始まるパス (/kyou) にしない。
        var slash = state.Units is [{ Kana: "・", Raw: "/" }, ..];
        var key = slash ? "・" + raw : raw;
        if (state.ProtectedCache is not null && state.ProtectedCacheRaw == key) return state.ProtectedCache;
        state.ProtectedCache = slash ? ProtectedSpanScanner.Scan(raw[1..]).Select(s => s with { Start = s.Start + 1 }).ToList() : ProtectedSpanScanner.Scan(raw);
        state.ProtectedCacheRaw = key;
        return state.ProtectedCache;
    }

    /// <summary>原文全体が 1 つの保護区間か (自動変換・幅変換・誤字補正の対象外)。</summary>
    public static bool IsProtectedRaw(CompositionTextState state) => !CompositionText.IsEmpty(state) && CompositionText.VerifyProtectedRaw(state);

    private static bool VerifyProtectedRaw(CompositionTextState state)
    {
        var spans = CompositionText.ProtectedSpans(state);
        return !CompositionText.IsEmpty(state) && spans.Count == 1 && spans[0].Start == 0 && spans[0].End == CompositionText.Raw(state).Length;
    }

    /// <summary>原文の末尾が保護区間で終わっているか (Space/Enter/Tab/確定で原文のまま確定する)。</summary>
    public static bool HasProtectedTail
(CompositionTextState state)
    {
        var spans = CompositionText.ProtectedSpans(state);
        return spans.Count > 0 && spans[^1].End == CompositionText.Raw(state).Length;
    }

    /// <summary>英語区間は英字のまま、日本語区間はかな (または漢字)。保護範囲は打った原文のまま。</summary>
    public static string RenderSegments(CompositionTextState state, TextEnv env, bool final, Func<string, bool, string, string>? convert)
    {
        // 保護範囲 (技術的な文字列) は、幅変換・かな化・漢字変換をせずに原文のまま出す (INV-01)。
        if (CompositionText.VerifyProtectedRaw(state)) return CompositionText.Raw(state);
        var builder = new StringBuilder();
        var segments = CompositionText.Segments(state, env);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.IsEnglish)
            {
                builder.Append(segment.Raw);
                continue;
            }
            var isLast = i == segments.Count - 1;
            var kana = segment.Kana;
            var pending = isLast ? CompositionText.PendingText(state, env, final) : "";
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
            builder.Append(convert is not null && kana.Length > 0 ? convert(kana, i > 0 && segments[i - 1].IsEnglish, builder.ToString()) : kana);
            builder.Append(pending);
        }
        return CompositionText.HalfWidthStars(state, builder.ToString());
    }

    /// <summary>英語判定を無視してすべてかなにしたもの (F6)。</summary>
    public static string AllKana(CompositionTextState state, TextEnv env, bool final) => string.Concat(state.Units.Select(u => u.Kana)) + CompositionText.PendingText(state, env, final);

    /// <summary>
    /// 日本語の部分の読みに書き間違い (ブレスレッド) があれば、その位置 (単位の範囲) と正しい読みを返す。
    /// 英語として見せている部分や、入力途中の子音は見ない。読みの区切りが単位の区切りと合わないときも null。
    /// </summary>
    public static (int Start, int End, Misspelling Misspelling)? FindMisspelling(CompositionTextState state, TextEnv env, MisspellingDictionary dictionary)
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
                reading.Append(state.Units[i].Kana);
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
    /// 英字を続けて打った部分 (ローマ字) の打ち間違いを直す (onegaishimsu → onegaishimasu)。直したら true。
    /// 確定・変換の直前 (Space・Enter・記号) に呼ぶ。打っている途中は続きを打つと読めることがあり、
    /// 直し方も続きで変わる (futtemshi の時点では ふってんし、futtemshita まで打てば ふってました) ので直さない。
    /// 大文字を含む部分・英単語は直さない。
    /// </summary>
    public static bool FixTypos(CompositionTextState state, TextEnv env, bool correct = true)
    {
        // 確定・変換の前に、数字の後ろの単位を英字のままにする (5min を 5みん にしない)。打ち間違いを直す設定に関係なく
        CompositionText.SplitUnitAfterNumber(state, env, final: true);
        CompositionText.Normalize(state, env, final: false);
        // 英字の並びの最後の w の連続 (笑い) はそのまま残し、打ち間違いの直しの対象にもしない
        CompositionText.KeepLaughter(state, env);
        if (!correct || env.RomajiTypos is not { } corrector || !env.Settings.CorrectTypos || state.Mode != DisplayMode.Auto || state.KanaInput) return false;
        var changed = false;
        // 今の表示で英字に見えている文字 (きょうは|meeting|です の meeting)。ここは直さない
        // (表示では英単語なのに、確定したら g が が に直されて めえちんがです になっていた)。
        var shownEnglish = CompositionText.EnglishMask(state, env, out var firmEnglish);
        var offset = 0;
        var start = 0;
        while (start < state.Units.Count)
        {
            if (!IsLetters(state.Units[start].Raw) || IsLaughter(state.Units[start]))
            {
                offset += state.Units[start].Raw.Length;
                start++;
                continue;
            }
            var end = start;
            while (end < state.Units.Count && IsLetters(state.Units[end].Raw) && !IsLaughter(state.Units[end])) end++;
            var atEnd = end == state.Units.Count;
            var letters = string.Concat(state.Units.Skip(start).Take(end - start).Select(u => u.Raw)) + (atEnd ? CompositionText.Pending(state) : "");
            var runOffset = offset;
            offset += letters.Length;
            // 数字のすぐ後ろの単位 (10|mm|で) は打ち間違いではない
            var unitAfterNumber = start > 0 && state.Units[start - 1].Raw is [var digit] && char.IsAsciiDigit(digit) &&
                UnitWords.Any(u => letters.StartsWith(u, StringComparison.OrdinalIgnoreCase));
            if (!unitAfterNumber && letters.All(char.IsAsciiLetterLower) && !env.Detector.IsKnownEnglishWord(LearningContext(state, env), letters) &&
                corrector.FirstUnreadable(letters, final: true) is var first and > 0 &&
                !(runOffset + first < shownEnglish.Count && shownEnglish[runOffset + first] && (firmEnglish[runOffset + first] || !CompositionText.FixesIntoLongWord(state, env, corrector, letters, first))) &&
                // 数字のすぐ前の短い英字 (kaibunsyo|rta|2026 の rta) は略語。打ち間違いとして直さない
                !(end < state.Units.Count && state.Units[end].Raw is [var after] && char.IsAsciiDigit(after) && first >= letters.Length - 6) &&
                !CompositionText.ContainsEnglishWord(state, env, letters, first) &&
                corrector.Fix(letters, final: true) is { } fix)
            {
                Diagnostics.Log.Decision($"ローマ字の打ち間違いを直しました: {Diagnostics.Log.Text(fix.Wrong)}→{Diagnostics.Log.Text(fix.Right)}");
                var analysis = env.Detector.Romaji.AnalyzeFragment(fix.Right);
                var units = analysis.Tokens.Select(t => new CompositionUnit(t.Kana, t.Romaji)).ToList();
                state.Units.RemoveRange(start, end - start);
                state.Units.InsertRange(start, units);
                if (atEnd)
                {
                    state.Pending.Clear();
                    state.Pending.Append(analysis.Partial);
                    CompositionText.Normalize(state, env, final: true);
                }
                changed = true;
                end = start + units.Count;
            }
            start = end;
        }
        return changed;
    }

    /// <summary>
    /// 英字の並びの最後の w の連続は、チャットの笑い (きたw・だねww・www・ほんと？w)。ww を っw と読んだり、打ち間違いとして消したりせず、
    /// 1 文字ずつ w のまま残す (確定・変換・記号の直前に呼ぶ)。英単語の最後の w (new・aww) はそのまま。
    /// </summary>
    private static void KeepLaughter(CompositionTextState state, TextEnv env)
    {
        if (state.KanaInput || state.Mode != DisplayMode.Auto) return;
        // 後ろの英字の並びから順に見る (前を入れ替えると後ろの位置がずれるので)
        var end = state.Units.Count;
        var withPending = state.Pending.Length > 0;
        while (end > 0 || withPending)
        {
            if (!withPending && !IsLetters(state.Units[end - 1].Raw))
            {
                end--;
                continue;
            }
            var runStart = end;
            while (runStart > 0 && IsLetters(state.Units[runStart - 1].Raw)) runStart--;
            CompositionText.KeepLaughterAt(state, env, end, withPending);
            withPending = false;
            end = runStart;
        }
    }

    /// <summary>笑いとして w のまま残した単位 (打ち間違いの直しの対象にしない)。</summary>
    private static bool IsLaughter(CompositionUnit unit) => unit.Raw is "w" or "W" && unit.Kana == unit.Raw;

    /// <summary>end の手前で終わる英字の並び (withPending なら入力途中の子音も) の最後の w の連続を、笑いなら w の単位にする。笑いの始まりを返す。</summary>
    private static int KeepLaughterAt(CompositionTextState state, TextEnv env, int end, bool withPending)
    {
        var pending = withPending ? state.Pending.ToString() : "";
        if (!pending.All(c => c is 'w' or 'W')) return end;
        var start = end;
        while (start > 0 && state.Units[start - 1].Raw is [var c] && c is 'w' or 'W' && state.Units[start - 1].Kana is "っ" or "w" or "W") start--;
        var count = end - start + pending.Length;
        if (count == 0) return end;
        // 1 つだけの w は、日本語のかなの後ろ (きた + w) のときだけ笑いとみなす
        if (count == 1 && !(start > 0 && state.Units[start - 1].Kana is [var kana, ..] && kana is >= 'ぁ' and <= 'ヺ')) return end;
        // 前から続く英単語の終わり (aww・new・show) なら笑いではない。英単語とみなすのは、英字の並びの頭から始まる語か、同梱の辞書の語だけ
        // (english-words.txt の短い語が音の途中から見つかる kiyagat|taw の taw で、笑いの w を英字にしていた)
        var letters = string.Concat(state.Units.Skip(start).Take(end - start).Select(u => u.Raw)) + pending;
        var laugh = letters;
        // 設定「かな + w を笑いにする」: 1 つのかなの音 (ね・さ) だけの後ろの w は、その 2 つで英単語 (new・saw) にしない (issue #713・#492)。
        // もっと長い英単語 (snow・window) の終わりは、今までどおり英単語
        var soleSyllable = state.LaughterAfterKana && start > 0 && state.Units[start - 1].Kana is [>= 'ぁ' and <= 'ん'] && state.Units[start - 1].Raw.All(char.IsAsciiLetter);
        for (var i = start - 1; i >= 0 && state.Units[i].Raw.Length > 0 && state.Units[i].Raw.All(char.IsAsciiLetter); i--)
        {
            letters = state.Units[i].Raw + letters;
            if (soleSyllable && i == start - 1) continue;
            var runStart = i == 0 || !IsLetters(state.Units[i - 1].Raw);
            if (letters.Length >= 3 && (runStart ? env.Detector.IsKnownEnglishWord(LearningContext(state, env), letters) : env.Detector.IsListedEnglishWord(LearningContext(state, env), letters.ToLowerInvariant()))) return end;
        }
        state.Units.RemoveRange(start, end - start);
        if (withPending) state.Pending.Clear();
        state.Units.InsertRange(start, laugh.Select(w => new CompositionUnit(w.ToString(), w.ToString())));
        return start;
    }

    /// <summary>
    /// 打った英字 (Raw) の 1 文字ずつが、今の表示で英単語 (5 文字以上の知っている語: meeting) の区間に入っているか。
    /// firm は、そのうち守った区間か 5 文字以上の語 (短い語 mas と違い、直すと長い語になっても英字のまま)。
    /// 短い語 (onegai|shim|su の shim) は、ローマ字の途中に偶然現れることが多いので含めない (打ち間違いとして直す)。
    /// </summary>
    private static List<bool> EnglishMask(CompositionTextState state, TextEnv env, out List<bool> firm)
    {
        var mask = new List<bool>();
        firm = [];
        var segments = CompositionText.Segments(state, env);
        for (var s = 0; s < segments.Count; s++)
        {
            var segment = segments[s];
            // 英語の語.の後ろの英字 (ドメイン・拡張子: Meltype.amr、MeltypeTip.dll) も、表示のまま英字にする (amr を あめ に直していた: issue #260)
            if (segment.IsEnglish && s >= 2 && segments[s - 1] is { IsEnglish: true, Raw: "." } && segments[s - 2].IsEnglish && segments[s - 2].Raw.Any(char.IsAsciiLetter))
            {
                for (var i = 0; i < segment.Raw.Length; i++)
                {
                    mask.Add(true);
                    firm.Add(true);
                }
                continue;
            }
            // 5 文字以上の知っている語 (meeting) か、同梱の英語の辞書の 2〜4 文字の語 (user・rta・av)
            // 4 文字の知っている語で、ローマ字として読めないもの (help・milk) も英語 (help|pe-ji → へおっぺーじ にしない)
            var lower = segment.Raw.ToLowerInvariant();
            var word = segment.IsProtected || segment.IsEnglish && (segment.Raw.Length >= 5 && env.Detector.IsKnownEnglishWord(LearningContext(state, env), segment.Raw) || segment.Raw.Length is >= 2 and <= 4 && env.Detector.IsListedEnglishWord(LearningContext(state, env), lower) ||
                segment.Raw.Length == 4 && env.Detector.IsKnownEnglishWord(LearningContext(state, env), lower) && !env.Detector.Romaji.Analyze(lower).IsValid ||
                // 3 文字のよく使う英単語で、最後の子音がかなにならないもの (tab|de の b)。表示で英字にしたものを、確定で打ち間違いとして たべ に直さない (issue #220)
                segment.Raw.Length == 3 && lower.All(char.IsAsciiLetterLower) && CompositionDetector.IsCommonEnglishWord(lower) && env.Detector.Romaji.AnalyzeFragment(lower).Partial.Length > 0);
            for (var i = 0; i < segment.Raw.Length; i++)
            {
                mask.Add(word);
                // 守った区間と 5 文字以上の語 (shopping) は、直すと長い語になっても英字のまま (FixesIntoLongWord を見ない)
                firm.Add(segment.IsProtected || segment.Raw.Length >= 5);
            }
        }
        return mask;
    }

    /// <summary>
    /// 表示で英字に見えている短い語 (すみ|mas|ね の mas) でも、直すとその前のかなと合わせて 4 文字以上の 1 つの語になるなら直す
    /// (sumimasne → すみません: issue #971)。tab|de (たべ)・tab|ga (たば) のように、直しても短い語にしかならないものは英字のまま。
    /// </summary>
    private static bool FixesIntoLongWord(CompositionTextState state, TextEnv env, RomajiTypoCorrector corrector, string letters, int first)
    {
        if (corrector.Fix(letters, final: true) is not { } fix || first > fix.Right.Length) return false;
        // 読めない文字の直前のかな (sumima の ま) が、直した後の語に入っているか
        var before = env.Detector.Romaji.AnalyzeFragment(fix.Right[..first]).Kana.Length;
        return corrector.InLongWord(fix.Kana, before - 1, minLength: 4);
    }

    /// <summary>
    /// 読めない文字 (位置 first) が英単語の一部か。
    /// 先頭から打った英単語 (google|de、kotlin|ni、ok|no) と、同梱の辞書・固有名詞にある 5 文字以上の英単語 (…github|ni) は直さない。
    /// english-words.txt だけにある語は、先頭からの 5 文字以上のときだけ見る
    /// (日本語のローマ字の途中にも、shims や him のような英単語が偶然現れる)。
    /// </summary>
    private static bool ContainsEnglishWord(CompositionTextState state, TextEnv env, string letters, int first)
    {
        for (var s = Math.Max(0, first - 12); s <= first; s++)
        {
            for (var e = Math.Min(letters.Length, s + 16); e > first; e--)
            {
                var word = letters[s..e];
                if (s == 0 && word.Length >= 5 && env.Detector.IsKnownEnglishWord(LearningContext(state, env), word)) return true;
                if ((s == 0 || word.Length >= 5) && env.Detector.IsListedEnglishWord(LearningContext(state, env), word)) return true;
            }
        }
        return false;
    }

    private static bool IsLetters(string raw) => raw.Length > 0 && raw.All(char.IsAsciiLetter);

    /// <summary>単位 [start, end) の読みを、正しい読み (カタカナ) に置き換える。end が単位の数を超えるときは語末の n も含む。</summary>
    public static void ReplaceReading(CompositionTextState state, int start, int end, string rightKatakana)
    {
        var hiragana = KanaChars.ToHiragana(rightKatakana);
        if (end > state.Units.Count)
        {
            state.Pending.Clear();
            end = state.Units.Count;
        }
        state.Units.RemoveRange(start, end - start);
        // かなの単位にする (英字の Raw を持たないので、英語と判定されることはない)。
        state.Units.InsertRange(start, hiragana.Select(c => new CompositionUnit(c.ToString(), c.ToString())));
    }

    /// <summary>単位ごとに、英語として見せている区間に入っているか。</summary>
    private static bool[] UnitIsEnglish(CompositionTextState state, TextEnv env)
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

    private static string PendingText(CompositionTextState state, TextEnv env, bool final)
    {
        var pending = CompositionText.Pending(state);
        if (pending.Length == 0) return "";
        return env.Detector.Romaji.ConvertLenient(pending.ToLowerInvariant(), final);
    }

    /// <summary>日本語の中で打った記号 (Microsoft IME と同じく全角)。英語の区間では打ったままの半角で出す。数字と括弧は半角のまま。</summary>
    private static char Symbol(CompositionTextState state, char c) => c switch
    {
        '-' => 'ー',
        // 句読点は、読みでは設定によらず 、。 (設定の句読点は CompositionOutput が当てる)。
        ',' => '、',
        '.' => '。',
        '[' => state.SquareBrackets ? '［' : '「',
        ']' => state.SquareBrackets ? '］' : '」',
        // ASCII の括弧はチャット本文でもそのまま使われるため、入力した幅を保つ。
        '(' => state.FullWidthParentheses ? '（' : '(',
        ')' => state.FullWidthParentheses ? '）' : ')',
        // ! ? は Microsoft IME と同じく全角 (テスターのチャットでも、日本語の後ろの ？ ！ は 96% が全角だった)。
        // : ; | はチャット本文でも半角で使うので、入力した幅を保つ。
        ':' => ':',
        ';' => ';',
        '|' => '|',
        '~' => '～',
        '\'' => '’',
        '"' => '”',
        // ` は Markdown 記法に使うので、日本語の中でも半角のまま (Space で ‘ ｀)。
        '`' => '`',
        // @ はメールアドレス・メンション、/ は URL・日付・パスで使うので、日本語の中でも半角のまま (Space で ＠ ／ ・)。
        '@' => '@',
        // # はハッシュタグ・チャンネル名 (Discord の #雑談) で使うので半角のまま (Space で # と半角空白に確定)。
        '#' => '#',
        '/' => '/',
        // JIS キーボードの ￥ キー
        '\\' => '￥',
        _ when c is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(c) => (char)(c + 0xFEE0),
        _ => c,
    };

    /// <summary>全角の記号 (＃ （ ％ ’ ￥) を半角に戻す。かな・漢字・句読点 (、。「」ー) はそのまま。</summary>
    public static string SymbolsToHalfWidth(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                '’' => '\'',
                '”' => '"',
                '￥' => '¥',
                >= '！' and <= '～' when !char.IsLetterOrDigit(chars[i]) => (char)(chars[i] - 0xFEE0),
                _ => chars[i],
            };
        }
        return new string(chars);
    }

    /// <summary>z + 記号 (Microsoft IME と同じ): z/ → ・、z. → …、z, → ‥、z- → ～、z[ → 『、z] → 』。zh zj zk zl → ← ↓ ↑ →。</summary>
    private static char? ZSymbol(char c) => char.ToLowerInvariant(c) switch
    {
        '/' => '・',
        '.' => '…',
        ',' => '‥',
        '-' => '～',
        '[' => '『',
        ']' => '』',
        'h' => '←',
        'j' => '↓',
        'k' => '↑',
        'l' => '→',
        _ => null,
    };

    public static string ToKatakana(string hiragana) => KanaChars.ToKatakana(hiragana);

    /// <summary>全角カタカナを半角カタカナにする。濁点・半濁点は半角の ﾞ / ﾟ に分ける。</summary>
    public static string ToHalfWidthKatakana(string katakana)
    {
        const string full = "。「」、・ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワンヵヶ";
        const string half = "｡｢｣､･ｦｧｨｩｪｫｬｭｮｯｰｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝｶｹ";
        var builder = new StringBuilder(katakana.Length);
        foreach (var c in katakana.Normalize(NormalizationForm.FormD))
        {
            if (c is '\u3099' or '゛')
            {
                builder.Append('ﾞ');
                continue;
            }
            if (c is '\u309A' or '゜')
            {
                builder.Append('ﾟ');
                continue;
            }
            var index = full.IndexOf(c);
            builder.Append(index >= 0 ? half[index] : c);
        }
        return builder.ToString();
    }

    public static string ToFullWidth(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '!' and <= '~') chars[i] = (char)(chars[i] + 0xFEE0);
            else if (chars[i] == ' ') chars[i] = '　';
        }
        return new string(chars);
    }
}
