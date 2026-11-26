// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Libraries.Chars;
using Meltype.Notation;
using static Meltype.Libraries.Chars.TextChars;

namespace Meltype.Composition;

/// <summary>読みからの文節と候補の組み立て、候補の追加、文節の区切りの変更。キーや表示の処理は呼ばない。</summary>
internal static class CompositionConversion
{
    /// <summary>候補の一覧の 1 ページの数 (変換ボックス・Linux の候補の一覧と合わせる)。数字キー 1〜9 でこのページの中から選ぶ。</summary>
    internal const int CandidatePageSize = 9;

    /// <summary>
    /// ライブ変換は 2 拍以上のかなだけ。2〜3 拍の語 (きた → 来た, りかい → 理解) もライブ変換する。
    /// 1 拍は漢字を推測できず、ひらがな 1 字のまま打つことも多いので、かなのまま見せる。拍の数え方は CountsAsLive。
    /// </summary>
    private const int LiveConversionMinLength = 2;

    internal static bool HasRegisteredReading(CompositionInput input, CompositionEnv env) =>
        !input.Text.KanaInput && input.Text.Mode == DisplayMode.Auto && CompositionText.Raw(input.Text) is { Length: >= 2 } raw && raw.All(char.IsAsciiLetter) &&
        (env.Services.UserDictionary?.Lookup(raw.ToLowerInvariant()) is { Count: > 0 } ||
         env.Settings.ConsonantAbbreviations && env.Services.Candidates?.Lookup(raw.ToLowerInvariant()) is { Count: > 0 });

    /// <summary>ローマ字のまま入った文か (英字を含み、半角の英数字・記号・空白だけ)。</summary>
    internal static bool IsRomajiText(string text) =>
        text.Any(char.IsAsciiLetter) && text.All(c => c is >= ' ' and <= '~') && text.Trim().Length > 0;

    /// <summary>
    /// ローマ字のまま入った文を選んで変換キーを押したとき (Meltype が止まっていた・ターミナルで打った: issue #288)。
    /// Meltype の判定で日本語の所だけを日本語にした文を候補として見せ、Enter で選んだ文字と置き換える。
    /// 候補は 日本語にした文 → 選んだままの文。日本語にする所が無ければ何もしない。
    /// </summary>
    internal static void StartRomajiReconversion(CompositionState state, CompositionEnv env, ReconversionSelection selection)
    {
        var converted = ConvertRomajiText(env, selection.Text);
        if (converted == selection.Text)
        {
            Diagnostics.Log.Info("再変換: 選んだ文字に、日本語にする所がありませんでした。");
            return;
        }
        var input = CompositionLifecycle.Begin(state, env, reconversion: selection);
        foreach (var c in converted) CompositionText.AppendKana(input.Text, c, c);
        input.Text.Mode = DisplayMode.Hiragana;
        CompositionLifecycle.Convert(state, input, new Conversion([new Clause(converted, false, [converted, selection.Text])], ConversionTrigger.Other, preferJapanese: false));
    }

    /// <summary>
    /// ローマ字の文を、打ったときと同じ判定で日本語にする。空白で区切った語ごとに英語 / 日本語を判定し (前の語が英語かも手がかりにする)、
    /// 続く日本語の語はつなげて 1 つの文として変換する (asita no → 明日の)。英語の語は前後に空白を残す
    /// (asita no meeting de review shimasu → 明日の meeting で review します)。
    /// </summary>
    internal static string ConvertRomajiText(CompositionEnv env, string text)
    {
        var output = new StringBuilder();
        var kana = new StringBuilder();
        var englishWords = 0;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // 前の語が日本語でも「日本語の続き」とはしない (空白で区切って打った語なので、語の途中ではない)。
            // 英語の語が 2 語以上続いた後は、打つときと同じく英文の続き (this is + a)
            CompositionTextState typed = new CompositionTextState();
            var context = TextContext.None with { PrecedingEnglish = englishWords > 0 ? true : null, PrecedingEnglishSentence = englishWords >= 2 };
            var typedEnv = CompositionText.FragmentEnv(env) with { Context = () => context };
            foreach (var c in word) CompositionText.Append(typed, typedEnv, c);
            var segments = CompositionText.Segments(typed, typedEnv);
            var shown = CompositionText.TypedSpans(segments, env);
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (!segment.IsEnglish)
                {
                    kana.Append(segment.Kana);
                    continue;
                }
                // 英語の語: 前の日本語をまとめて変換してから、空白で区切って足す
                if (kana.Length > 0) output.Append(Convert(env, kana.ToString(), SegmentBefore(env, output.ToString()), zenzai: true));
                kana.Clear();
                if (output.Length > 0 && output[^1] != ' ') output.Append(' ');
                output.Append(shown[i].Text).Append(' ');
            }
            englishWords = segments.Count > 0 && segments[^1].IsEnglish ? (segments.Count == 1 ? englishWords + 1 : 1) : 0;
        }
        if (kana.Length > 0) output.Append(Convert(env, kana.ToString(), SegmentBefore(env, output.ToString()), zenzai: true));
        return output.ToString().TrimEnd();
    }

    /// <summary>
    /// 区切りを文節にして変換する (<see cref="BuildClauses"/>)。英日の区切りの別案があれば、文全体を 1 つの文節にして、案ごとの文を候補にする。
    /// F6〜F8 でかなにして見せているときは、打った英字をすべてローマ字として読んで変換する。
    /// </summary>
    internal static CompositionScene.Converting? StartConversion(CompositionState state, CompositionInput input, CompositionEnv env, ConversionTrigger trigger = ConversionTrigger.Other, bool preferJapanese = false)
        => CompositionLifecycle.Convert(state, input, BuildConversion(input, env, trigger, preferJapanese));

    /// <summary>今開いている変換を変えずに、新しい文節と候補を組み立てる。背景の結果は、採用を決めてから置き換える。</summary>
    internal static Conversion? BuildConversion(CompositionInput input, CompositionEnv env, ConversionTrigger trigger, bool preferJapanese)
    {
        var asKana = input.Text.Mode is DisplayMode.Hiragana or DisplayMode.Katakana or DisplayMode.HalfWidthKatakana;
        var segments = asKana ? CompositionText.AllJapaneseSegments(input.Text, env) : CompositionText.ConversionSegments(input.Text, env);
        var utility = Utilities(input, env, segments);
        if (utility is { Placement: UtilityPlacement.Replace })
        {
            var fixedClause = new Clause(utility.Reading ?? CompositionText.Raw(input.Text), utility.Reading is null, [.. utility.Candidates])
            {
                Raw = CompositionText.Raw(input.Text),
                Expanded = true,
                Fixed = true,
                Notes = utility.Notes,
            };
            fixedClause.NotLearned.UnionWith(utility.NotLearned);
            // 続けて入力する前の選択だけを復元し、候補の順 (注記と同じ順) は変えない。
            if (input.KeptChoices.TryGetValue(fixedClause.Reading, out var kept) && fixedClause.Candidates.IndexOf(kept) is >= 0 and var index)
                fixedClause.Index = index;
            return new Conversion([fixedClause], trigger, preferJapanese);
        }
        // 打った英字全体がユーザー辞書の読み (helloworld → 登録した語) か子音の略入力の読み (ast → 明日。issue #62) なら、
        // 区間に分けずに 1 つの文節にして、その語を候補の先頭にする。ローマ字として読めれば、その読みの漢字も出す (go → 後・語・碁)
        if (HasRegisteredReading(input, env))
        {
            var typedRaw = CompositionText.Raw(input.Text);
            var lower = typedRaw.ToLowerInvariant();
            List<string> registered = [.. env.Services.UserDictionary?.Lookup(lower) ?? [], .. env.Settings.ConsonantAbbreviations ? env.Services.Candidates?.Lookup(lower) ?? [] : []];
            var reading = RomajiReading(env, typedRaw);
            List<string> romaji = reading is null ? [] : JapaneseCandidates(env, reading, null, SegmentBefore(env, ""), zenzai: true);
            var english = EnglishCandidates(env, typedRaw, NotationRules.Word(typedRaw, env.ProperNoun));
            return new Conversion([new Clause(typedRaw, true, preferJapanese
                ? Distinct([.. registered, .. romaji, .. english])
                : Distinct([.. registered, .. english, .. romaji])) { Raw = typedRaw, Kana = reading }], trigger, preferJapanese);
        }
        // 区切りの別案があれば、別案ごとに変換した文を 1 つの文節の候補にする (issue #1737)
        var alternatives = utility is null && !asKana ? CompositionText.ConversionSegmentAlternatives(input.Text, env, MaxSegmentationAlternatives) : [segments];
        var clauses = BuildClauses(input, env, alternatives[0], preferJapanese, zenzai: true);
        if (clauses.Count == 0) return null;
        if (alternatives.Count > 1 && WholeSentenceClause(input, env, [clauses, .. OtherAlternatives(input, env, alternatives, preferJapanese)]) is { } whole)
            clauses = [whole];
        if (utility is not null) clauses = [WithUtility(input, env, clauses, utility, segments[0].Kana)];
        // 変換したあとに続けて打っていたら、前に選んでいた候補を同じ読みの文節で使う
        foreach (var clause in clauses)
            if (input.KeptChoices.TryGetValue(clause.Reading, out var kept) && clause.Candidates.Contains(kept)) Prefer(clause, kept);
        // 打ち始めの ・ は、Space で / にする。
        if (CompositionText.IsSlashAtStart(input.Text) && clauses is [var slash]) Prefer(slash, "/");
        return new Conversion(clauses, trigger, preferJapanese) { Selected = InitialClause(env, clauses.Count) };
    }

    /// <summary>変換を始めたときに選ぶ文節。</summary>
    internal static int InitialClause(CompositionEnv env, int clauseCount) => env.Settings.ConversionStartsAtLastClause ? clauseCount - 1 : 0;

    /// <summary>
    /// 区切りの 1 案の文節。英語区間はそのまま、日本語区間は文脈を付けて変換エンジンで文節に区切って変換する。
    /// 英語区間も、ローマ字として読めれば日本語の候補 (go → 語 ご ゴ) を後ろに足す。preferJapanese (Shift+Space) なら前に出す。
    /// zenzai は Zenzai にも頼むか (英日の区切りの案の 2 番目以降は頼まない: <see cref="OtherAlternatives"/>)。
    /// </summary>
    private static List<Clause> BuildClauses(CompositionInput input, CompositionEnv env, IReadOnlyList<CompositionSegment> segments, bool preferJapanese, bool zenzai)
    {
        var clauses = new List<Clause>();
        var typed = CompositionText.TypedSpans(segments, env);
        for (var s = 0; s < segments.Count; s++)
        {
            var segment = segments[s];
            if (segment.IsEnglish)
            {
                if (segment.IsProtected && !preferJapanese)
                {
                    clauses.Add(new Clause(segment.Raw, true, [segment.Raw]));
                    continue;
                }
                var english = Distinct([.. env.Services.UserDictionary?.Lookup(segment.Raw.ToLowerInvariant()) ?? [], .. EnglishCandidates(env, segment.Raw, typed[s].Text)]);
                // 子音の略入力 (ast → 明日): 英字のまま見える語にも、candidates.txt の英字の読みの候補を足す (issue #62)
                if (env.Settings.ConsonantAbbreviations && segment.Raw.Length >= 2 && segment.Raw.All(char.IsAsciiLetter) && env.Services.Candidates is { } abbreviations)
                {
                    foreach (var word in abbreviations.Lookup(segment.Raw.ToLowerInvariant()))
                    {
                        if (!english.Contains(word)) english.Add(word);
                    }
                }
                // 記号だけの英語区間にも、同梱の記号候補 (... → …) を出す。
                if (segment.Raw.All(c => !char.IsLetterOrDigit(c)))
                    english = Distinct([.. english, .. env.Services.Candidates?.Lookup(segment.Raw) ?? []]);
                var reading = RomajiReading(env, segment.Raw);
                List<string> romaji = reading is null ? [] : JapaneseCandidates(env, reading, null, SegmentBefore(env, string.Concat(clauses.Select(c => c.Text))), zenzai);
                // カタカナ表記 (export → エクスポート) は候補の最後。英字・ローマ字として読んだ候補の順は変えず、選んだときだけカタカナにする
                var katakana = (env.Services.Candidates?.KatakanaSpellings(segment.Raw.ToLowerInvariant()) ?? []).Where(k => !romaji.Contains(k)).ToList();
                var clause = new Clause(segment.Raw, true, preferJapanese && romaji.Count > 0
                    ? Distinct([.. romaji, .. english, .. katakana])
                    : Distinct([.. english, .. romaji, .. katakana]))
                { Kana = CompositionText.KanaForSegment(input.Text, env, segments, s) };
                // カタカナ表記を選んでも、英語の綴りは日本語の読みでないので、その語を日本語として覚えない (覚えると次から export が えxぽrt になる)
                clause.NotLearned.UnionWith(katakana);
                clauses.Add(clause);
                continue;
            }
            if (segment.Kana.Length == 0) continue;
            var japanese = ConvertJapaneseSegment(input, env, segment.Kana, s > 0 && segments[s - 1].IsEnglish, SegmentBefore(env, string.Concat(clauses.Select(c => c.Text))), zenzai);
            // 候補の最後に、打ったままの英字 (あぴ → api) と全角の英字も出す (Space を連打して英字に戻せる)。
            var offset = 0;
            foreach (var clause in japanese)
            {
                clause.Raw = CompositionText.RawForReading(input.Text, env, segments, s, offset, clause.Reading.Length);
                offset += clause.Reading.Length;
                AddCandidates(env, clause);
                AddOldKana(clause);
                AddTranslations(env, clause);
                AddRawCandidates(input, env, clause);
                MoveEmojiLast(env, clause);
            }
            clauses.AddRange(japanese);
        }
        return clauses;
    }

    // 変換の候補に出す英日の区切りの案の数 (いちばん良い案を含む)
    private const int MaxSegmentationAlternatives = 4;

    /// <summary>英日の区切りの案のうち 2 番目以降の文節。空になった案は除く。Zenzai には頼まない (<see cref="ConversionRequest.Zenzai"/>)。</summary>
    private static List<List<Clause>> OtherAlternatives(CompositionInput input, CompositionEnv env, IReadOnlyList<IReadOnlyList<CompositionSegment>> alternatives, bool preferJapanese) =>
        [.. alternatives.Skip(1).Select(a => BuildClauses(input, env, a, preferJapanese, zenzai: false)).Where(c => c.Count > 0)];

    /// <summary>
    /// 区切りの案ごとの文節 (先頭がいちばん良い案) を、文全体の 1 つの文節にする。候補は案ごとの文 (botto → botと、ボット) の後ろに、
    /// 文節が 1 つの案のその文節の候補 (sushi → 寿司 の 鮨・すし)。案の文がすべて同じなら null。
    /// </summary>
    private static Clause? WholeSentenceClause(CompositionInput input, CompositionEnv env, List<List<Clause>> alternatives)
    {
        var texts = new Dictionary<string, List<Clause>>(StringComparer.Ordinal);
        foreach (var clauses in alternatives) texts.TryAdd(string.Concat(clauses.Select(c => c.Text)), clauses);
        if (texts.Count < 2) return null;
        var whole = new Clause(CompositionText.AllKana(input.Text, env, final: true), false, [.. texts.Keys]) { Raw = CompositionText.Raw(input.Text), Alternatives = texts };
        AddSingleClauseCandidates(whole);
        return whole;
    }

    /// <summary>文全体の文節の候補の後ろに、文節が 1 つの案のその文節の候補を足す。</summary>
    private static void AddSingleClauseCandidates(Clause whole)
    {
        foreach (var clauses in whole.Alternatives!.Values)
        {
            if (clauses is not [var only]) continue;
            foreach (var candidate in only.Candidates)
                if (!whole.Candidates.Contains(candidate)) whole.Candidates.Add(candidate);
        }
    }

    /// <summary>
    /// 文全体の文節 (<see cref="Clause.Alternatives"/>) を、選んでいる案の文節に分ける。文節ごとに候補を選び直す・区切りを動かす・確定する前に呼ぶ。
    /// 文節が 1 つの案の候補を選んでいれば、その文節でその候補を選ぶ。
    /// 文全体の文節でなければ何もしない。どの案にも無い文 (打ち始めの ・ を Space で / にした文) を選んでいれば、分けずに学習しない文節にする。
    /// 分けたら、後から届いた変換エンジンの答えで文全体の文節に戻さない (<see cref="Conversion.Edited"/>)。
    /// </summary>
    internal static void SplitWholeSentence(Conversion conversion)
    {
        if (conversion.Clauses is not [{ Alternatives: { } alternatives } whole]) return;
        if (SelectedAlternative(whole) is not { } clauses)
        {
            whole.NotLearned.Add(whole.Text);
            return;
        }
        if (!alternatives.ContainsKey(whole.Text) && clauses is [var single])
        {
            single.Index = single.Candidates.IndexOf(whole.Text);
            single.Changed = whole.Changed;
        }
        conversion.Clauses = clauses;
        conversion.Selected = 0;
        conversion.Edited = true;
    }

    /// <summary>
    /// 文全体の文節で選んでいる案の文節。文節が 1 つの案の候補を選んでいれば、その文節。どの案にも無い文なら null。
    /// </summary>
    private static List<Clause>? SelectedAlternative(Clause whole) =>
        whole.Alternatives!.TryGetValue(whole.Text, out var clauses) ? clauses
        : whole.Alternatives.Values.FirstOrDefault(c => c is [var only] && only.Candidates.Contains(whole.Text));

    /// <summary>文節の読み (英語の文節は打った英字)。文全体の文節は、選んでいる案の読み。</summary>
    internal static string Reading(Clause clause) =>
        clause.Alternatives is not null && SelectedAlternative(clause) is { } selected ? string.Concat(selected.Select(c => c.Reading)) : clause.Reading;

    /// <summary>入力の全体に一致したユーティリティ変換の候補。一致しなければ null。</summary>
    private static UtilityCandidates? Utilities(CompositionInput input, CompositionEnv env, IReadOnlyList<CompositionSegment> segments)
    {
        var reading = segments is [{ IsEnglish: false, Kana.Length: > 0 } only] ? only.Kana : null;
        var context = new UtilityContext(env.Settings, env.Services.Now(), env.Clipboard, env.NowPlaying, env.Services.GitHubTitles(), env.OtherTipVersion);
        return UtilityConversions.Convert(new UtilityInput(input.Text, reading, input.Direct, context));
    }

    /// <summary>
    /// 読み全体を 1 つの文節にして、普通の変換の候補のかなの後ろ (絵文字・打ったままの英字より前) に、ユーティリティ変換の候補を足す
    /// (Prepend なら普通の変換の候補の前に足して、最初の候補にする)。
    /// 変換エンジンが文節を分けたら (じこうの|あいさつ)、読み全体をその文節だけで変換し直す。
    /// </summary>
    private static Clause WithUtility(CompositionInput input, CompositionEnv env, List<Clause> clauses, UtilityCandidates utility, string reading)
    {
        var clause = clauses is [var only] && only.Reading == reading ? only : WholeClause(input, env, clauses, reading);
        var katakana = CompositionText.ToKatakana(reading);
        var at = utility.Placement == UtilityPlacement.Prepend ? 0
            : new[] { reading, katakana, CompositionText.ToHalfWidthKatakana(katakana) }.Max(clause.Candidates.IndexOf) + 1;
        if (at == 0 && utility.Placement != UtilityPlacement.Prepend) at = clause.Candidates.Count;
        clause.NotLearned.UnionWith(utility.NotLearned);
        foreach (var text in utility.Candidates)
        {
            if (clause.Candidates.Contains(text)) continue;
            clause.Candidates.Insert(at++, text);
        }
        if (utility.Placement == UtilityPlacement.Prepend)
        {
            clause.Index = 0;
            clause.Leading = at;
        }
        return clause;
    }

    private static Clause WholeClause(CompositionInput input, CompositionEnv env, List<Clause> clauses, string reading)
    {
        var clause = new Clause(reading, false, JapaneseCandidates(env, reading, string.Concat(clauses.Select(c => c.Text)), null, zenzai: true)) { Raw = CompositionText.Raw(input.Text) };
        // 文の中の文節と同じく、文脈の手がかりか、前に選び直した語を最初の候補にする (ConvertWithEngine)
        var surrounding = (env.Context().PrecedingText ?? "") + EnglishInComposition(input, env) + (env.Context().FollowingText ?? "");
        if ((env.Services.ContextRules?.Choose(reading, surrounding, clause.Text, text => Standalone(env, text)) ?? env.Learning.LearnedConversion(reading)) is { } preferred)
            Prefer(clause, preferred);
        KeepTypedSymbols(input, [clause], reading);
        AddOldKana(clause);
        AddTranslations(env, clause);
        AddRawCandidates(input, env, clause);
        MoveEmojiLast(env, clause);
        return clause;
    }

    /// <summary>
    /// 英訳の候補 (複雑な → complex, complicated …) を、日本語の候補の後ろに足す。
    /// 選んだことのある英訳は前に出す: 1 回なら英訳の中の先頭、2 回以上なら 2 番目 (変換エンジンの 1 番目の候補は動かさない)。
    /// </summary>
    private static void AddTranslations(CompositionEnv env, Clause clause)
    {
        if (clause.IsEnglish || env.Services.Translations is not { } dictionary) return;
        var words = dictionary.Lookup(clause.Text, clause.Reading).Where(w => !clause.Candidates.Contains(w)).ToList();
        if (words.Count == 0) return;
        var learned = env.Services.TranslationHistory?.Get(clause.Reading) ?? [];
        // 選んだ回数の多い順に前へ
        words = learned.Select(l => l.Word).Where(words.Contains).Concat(words.Where(w => !learned.Any(l => l.Word == w))).ToList();
        foreach (var word in words) clause.Translations.Add(word);
        var often = learned.FirstOrDefault(l => l.Count >= 2 && words.Contains(l.Word)).Word;
        if (often is not null)
        {
            clause.Candidates.Insert(Math.Min(1, clause.Candidates.Count), often);
            words.Remove(often);
        }
        clause.Candidates.AddRange(words);
    }

    /// <summary>打った英字を英語寄りに区切り直して、英語と日本語が混ざる形 (midiか) になれば返す。</summary>
    private static string? EnglishSplit(CompositionEnv env, string raw)
    {
        CompositionTextState typed = new CompositionTextState();
        var typedEnv = CompositionText.FragmentEnv(env);
        foreach (var c in raw) CompositionText.Append(typed, typedEnv, c);
        if (CompositionText.Pending(typed).Length > 0) return null;
        var segments = env.Detector.SegmentLeaningEnglish(CompositionText.LearningContext(env), TypedMarks.Read(typed, typedEnv), env.Settings.JapanesePunctuationAfterEnglish);
        if (!segments.Any(s => s.IsEnglish) || segments.All(s => s.IsEnglish)) return null;
        return string.Concat(segments.Select(s => s.IsEnglish ? s.Raw : s.Kana));
    }

    private static void AddRawCandidates(CompositionInput input, CompositionEnv env, Clause clause)
    {
        // 子音の略入力 (ast → 明日): 打った英字を読みとする候補 (candidates.txt の英字の読み) を足す (issue #62)
        if (env.Settings.ConsonantAbbreviations && clause.Raw is { Length: >= 2 } abbreviation && abbreviation.All(char.IsAsciiLetter) && env.Services.Candidates is { } abbreviations)
        {
            foreach (var word in abbreviations.Lookup(abbreviation.ToLowerInvariant()))
            {
                if (!clause.Candidates.Contains(word)) clause.Candidates.Add(word);
            }
        }
        // 入力中の表示が半角の記号 (* が 2 個以上・---) のときは、最初の候補も同じ半角にする。
        if (input.Text.Mode == DisplayMode.Auto && clause.Candidates.Count > 0 &&
            CompositionText.AsDisplayedHalfWidth(input.Text, clause.Candidates[0], clause.Raw) is var shown && shown != clause.Candidates[0])
        {
            clause.Candidates.Remove(shown);
            clause.Candidates.Insert(0, shown);
        }
        // 全角にした記号 (＃ （ ％) は、半角に戻した候補も出す (Space を続けて押すと半角の # ( %)。
        if (clause.Candidates.Count > 0 && CompositionText.SymbolsToHalfWidth(clause.Candidates[0]) is var half && half != clause.Candidates[0] &&
            !clause.Candidates.Contains(half))
        {
            clause.Candidates.Add(half);
        }
        if (clause.Raw is not { } raw || !raw.Any(c => c is >= '!' and <= '~')) return;
        // 英語を含む区切り (midika → 身近 の候補に midiか)
        if (raw.All(char.IsAsciiLetter) && EnglishSplit(env, raw) is { } split && !clause.Candidates.Contains(split)) clause.Candidates.Add(split);
        foreach (var candidate in new[] { raw, CompositionText.ToFullWidth(raw) })
        {
            if (!clause.Candidates.Contains(candidate)) clause.Candidates.Add(candidate);
        }
    }

    /// <summary>
    /// wi / we で打った うぃ / うぇ は、ゐ / ゑ (ヰ / ヱ) にした候補も出す (うぃすきー → ゐすきー、ヰスキー)。
    /// 候補の中の うぃ ウィ うぇ ウェ を置き換えた形を、打ったままの英字の候補より前に足す。
    /// </summary>
    private static void AddOldKana(Clause clause)
    {
        if (clause.IsEnglish || clause.Raw is not { } raw) return;
        var lower = raw.ToLowerInvariant();
        if (!lower.Contains("wi") && !lower.Contains("we")) return;
        if (!clause.Reading.Contains("うぃ") && !clause.Reading.Contains("うぇ")) return;
        static string Old(string text) => text.Replace("うぃ", "ゐ").Replace("うぇ", "ゑ").Replace("ウィ", "ヰ").Replace("ウェ", "ヱ");
        var added = new List<string>();
        foreach (var candidate in clause.Candidates.Concat([clause.Reading, CompositionText.ToKatakana(clause.Reading)]))
        {
            var old = Old(candidate);
            if (old != candidate && !clause.Candidates.Contains(old) && !added.Contains(old)) added.Add(old);
        }
        clause.Candidates.AddRange(added);
    }

    /// <summary>英日の区間分けの日本語の区間を変換する。Space の変換もライブ変換も、区間ごとにこれを通す。</summary>
    internal static List<Clause> ConvertJapaneseSegment(CompositionInput input, CompositionEnv env, string kana, bool afterEnglish, string before, bool zenzai)
    {
        var japanese = ConvertJapanese(input, env, kana, before, zenzai);
        // ワークアラウンド: 英単語 + 付属語 (python|なら、push|した): 変換エンジンは英語の後ろの読みだけを見て、文の頭の語 (奈良・下) にしてしまう。かなのまま。
        // 変換エンジンに文脈 (英単語・これ) を渡しても、名詞の読みを前に付けて変換しても、なら・しか・しよう は直らなかった。
        // 変換エンジンが英単語を前の文脈として受け取り、後ろの読みを付属語として変換するようになったら消せる
        if (afterEnglish && japanese.Count > 0 && IsParticleAfterEnglish(japanese[0])) Prefer(japanese[0], japanese[0].Reading);
        return japanese;
    }

    /// <summary>日本語の区間を変換エンジンで変換する。英字・記号の部分はそのままの文節にする。</summary>
    private static List<Clause> ConvertJapanese(CompositionInput input, CompositionEnv env, string kana, string before, bool zenzai)
    {
        // ローマ字として読めずに残った英字 (こほぃc の c) と半角の記号 (... @) は、変換エンジンに渡すと別の記号 (© ．．． ＠) にされるので、
        // そのままの文節にする。数字は、記号・英字とつながっていればそのまま (3.0 の 3 を 1 文字だけ渡すと ❸ にされる)、
        // それ以外は変換エンジンに渡す (2026ねん → 2026年)。
        var keep = new bool[kana.Length];
        for (var i = 0; i < kana.Length; i++) keep[i] = kana[i] is >= '!' and <= '~' && !char.IsAsciiDigit(kana[i]);
        for (var i = 0; i < kana.Length; i++)
        {
            if (!char.IsAsciiDigit(kana[i])) continue;
            var end = i;
            while (end < kana.Length && char.IsAsciiDigit(kana[end])) end++;
            var touches = (i > 0 && keep[i - 1]) || (end < kana.Length && keep[end]);
            for (var k = i; k < end; k++) keep[k] = touches;
            i = end - 1;
        }
        if (keep.Any(k => k))
        {
            var result = new List<Clause>();
            var start = 0;
            for (var i = 1; i <= kana.Length; i++)
            {
                if (i < kana.Length && keep[i] == keep[start]) continue;
                var run = kana[start..i];
                // ユーザー辞書に英字のまま登録した読み (zh → ←) はここでも引く
                if (keep[start]) result.Add(new Clause(run, false, Distinct([.. env.Services.UserDictionary?.Lookup(run) ?? [], run, .. env.Services.Candidates?.Lookup(run) ?? [], CompositionText.ToFullWidth(run)])));
                else result.AddRange(ConvertJapanese(input, env, run, before + string.Concat(result.Select(c => c.Text)), zenzai));
                start = i;
            }
            return result;
        }
        return ConvertWithEngine(input, env, kana, before, zenzai);
    }

    /// <summary>
    /// 日本語のかなを変換エンジンで文節に区切って変換する。各文節の最初の候補は次の順で決める:
    ///   1. 文脈の手がかり辞書 (前後に 気温 があれば あつい → 暑い)
    ///   2. ユーザーが前に選び直した変換 (学習)
    ///   3. 変換エンジンの結果 (入力欄のキャレットの前の文字を文脈として渡している)
    /// </summary>
    private static List<Clause> ConvertWithEngine(CompositionInput input, CompositionEnv env, string kana, string before, bool zenzai)
    {
        IReadOnlyList<ConversionClause> parts = Engine(env, kana, before, zenzai) ?? [.. SplitAtMarks(new ConversionClause(kana, kana))];
        // 読み全体が補助辞書・絵文字の辞書の語 (かんがえるかお → 🤔) なら、文節に分けずに 1 つの文節にして候補を出す。
        if (parts.Count > 1 && env.Services.Candidates?.Contains(kana) == true)
        {
            parts = [new ConversionClause(kana, string.Concat(parts.Select(p => p.Text)))];
        }
        parts = JoinSmallKana(env, parts, before, zenzai);
        var clauses = parts.Select(p =>
        {
            var text = NormalizeHalfWidth(p.Text, p.Reading);
            return new Clause(p.Reading, false, [text]) { Engine = text, ZenzaiText = p.Zenzai ? text : null };
        }).ToList();
        for (var i = 0; i < clauses.Count; i++)
        {
            var others = string.Concat(clauses.Where((_, k) => k != i).Select(c => c.Text));
            var surrounding = (env.Context().PrecedingText ?? "") + EnglishInComposition(input, env) + others + (env.Context().FollowingText ?? "");
            var preferred = env.Services.ContextRules?.Choose(clauses[i].Reading, surrounding, clauses[i].Text, text => Standalone(env, text)) ?? env.Learning.LearnedConversion(clauses[i].Reading);
            // 変換エンジンは、文節が「から」だけだと記号 (～) にしてしまう。記号だけの変換結果は、ひらがなの後ろに回す。
            // ユーザー辞書・同梱の語句の記号 (なかてん → ・) はそのまま
            preferred ??= IsSymbolOnly(clauses[i].Text) && clauses[i].Reading.All(KanaChars.IsHiragana) &&
                env.Services.UserDictionary?.Lookup(clauses[i].Reading).Contains(clauses[i].Text) != true ? clauses[i].Reading : null;
            // 英単語に挟まれて助詞だけの文節になると、変換エンジンは漢字にしてしまう (github + に + push → 二)。助詞はかなのまま。
            preferred ??= JapaneseParticles.IsParticle(clauses[i].Reading) && clauses[i].Text != clauses[i].Reading ? clauses[i].Reading : null;
            // カタカナの語の後ろの「っ」で始まる文節 (スパイダーマ + っ！) は、カタカナの「ッ」にする (スパイダーマッ！)。
            preferred ??= i > 0 && clauses[i].Text.StartsWith('っ') && clauses[i - 1].Text is [.., var last] && last is >= 'ァ' and <= 'ヺ' or 'ー'
                ? "ッ" + clauses[i].Text[1..] : null;
            // 文頭・記号の後ろの「え、」「え？」(聞き返し) を、変換エンジンは 得 にしてしまう (得、知らん)。かなのまま
            preferred ??= IsInterjection(env, clauses, i) ? clauses[i].Reading : null;
            // がち (ガチで) を、変換エンジンは 勝ち にしてしまう (勝ちでやばい)。勝ち の読みは かち なので、がち は ガチ にする
            preferred ??= clauses[i].Reading.StartsWith("がち", StringComparison.Ordinal) && clauses[i].Text.StartsWith("勝ち", StringComparison.Ordinal)
                ? "ガチ" + clauses[i].Text["勝ち".Length..] : null;
            if (preferred is not null) Prefer(clauses[i], preferred);
        }
        KeepTypedSymbols(input, clauses, kana);
        return clauses;
    }

    /// <summary>
    /// かなと一緒に打った記号 (読みの 、。？「 など。CompositionText.Symbol と TypedMarks が決めた文字) は、最初の候補でも読みの文字のままにする。
    /// 変換エンジンの答え・Mozc の学習・変換履歴は、記号を別の文字にすることがある
    /// (ライブ変換で 例文？ が 例文? に: issue #519、「今日は が ［今日は に: issue #1669、わたしは、 が 私は・ に)。別の候補は一覧に残す。
    /// 記号だけを打って変換したとき (、 だけ・( だけ) は、選んで覚えた候補を使う (issue #672)。記号だけかは、文節の読みでなく、
    /// 打ったもの全体で決める (わたしは、 の区切りを変えてできた 、 だけの文節にも、覚えた ・ を出さない)。
    /// 記号の数が読みと違う答え (から → ～、あんはさうぇい → アン・ハサウェイ) は、どの記号がどれに当たるか分からないので変えない。
    /// 打った記号を文字の種類と数で見分けているのは、変換の結果が区間の種類を持たない文字列だから。区間の種類を持ち回すようになったら (#1735)、区間で決める。
    /// 最初の候補を決める所 (ConvertWithEngine・Reconvert・WholeClause) の最後に呼ぶ。
    /// </summary>
    private static void KeepTypedSymbols(CompositionInput input, IReadOnlyList<Clause> clauses, string reading)
    {
        if (!CompositionText.Raw(input.Text).Any(char.IsLetterOrDigit)) return;
        var typed = reading.Where(IsSymbol).ToList();
        if (clauses.Sum(c => c.Text.Count(IsSymbol)) != typed.Count) return;
        var next = 0;
        foreach (var clause in clauses)
        {
            var text = string.Concat(clause.Text.Select(c => IsSymbol(c) ? typed[next++] : c));
            if (text == clause.Text) continue;
            if (clause.ZenzaiText == clause.Text) clause.ZenzaiText = text;
            Prefer(clause, text);
        }

        // 絵文字 (サロゲートペア) は打った記号に当たらないので数えない (置き換えると壊れた文字になる)
        static bool IsSymbol(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && !char.IsSurrogate(c);
    }

    /// <summary>
    /// 確定し直しで英語に戻さない、する活用のローマ字。
    /// sure は make sure で英文に直す必要があるので入れない (すれ と読めても除外)。
    /// </summary>
    private static readonly HashSet<string> SuruRomajiKeepJapanese =
        ["site", "sita", "suru", "shite", "shita", "shimasu", "shinai", "shiyou", "shiro", "sezu"];

    internal static bool IsSuruRomajiKeepJapanese(CompositionEnv env, string raw)
    {
        var lower = raw.ToLowerInvariant();
        if (!SuruRomajiKeepJapanese.Contains(lower)) return false;
        var reading = env.Detector.Romaji.ConvertLenient(lower, final: true);
        return CompositionDetector.IsSuruForm(reading);
    }

    /// <summary>
    /// 英単語の後ろの、名詞に付く付属語 (助詞・助動詞) か する の活用の文節を、変換エンジンが漢字で始めた
    /// (なら → 奈良、しか → 歯科、なの → 名の、した → 下、したい → 死体)。
    /// </summary>
    private static bool IsParticleAfterEnglish(Clause clause) =>
        (NounAttachedWords.Contains(clause.Reading) || CompositionDetector.SuruForms.Any(clause.Reading.StartsWith)) &&
        clause.Text.Length > 0 && !IsKana(clause.Text[0]) && clause.Text != clause.Reading;

    // 名詞に付く付属語。文節の読み全体と比べるので、変換エンジンが 1 つの文節にまとめる形 (には・なので) も並べる。
    // か は入れない: 英単語の後ろの 化 (docker化・API化) を変換エンジンが出したときに、かなに戻してしまう
    private static readonly HashSet<string> NounAttachedWords =
    [
        "が", "を", "に", "で", "と", "の", "は", "も", "へ", "や", "から", "まで", "より", "って", "とか", "とは", "には", "では", "にも", "でも",
        "への", "での", "との", "なら", "しか", "だけ", "のみ", "ほど", "など", "くらい", "ぐらい", "ばかり", "さえ", "すら", "こそ", "ずつ", "ごと", "とも", "ども", "やら", "なり",
        "だ", "です", "だった", "でした", "だろう", "でしょう", "だよ", "だね", "だし", "だから", "じゃ", "なの", "なのに", "なので", "かも", "みたい", "っぽい", "らしい",
    ];

    private static readonly HashSet<char> SentencePunctuation = ['、', '。', '，', '．', ',', '.', '！', '？', '!', '?', '…', '‥', '「', '」', '(', ')', '（', '）', ' ', '　'];

    /// <summary>
    /// 文節が「え」だけ (後ろに記号が付いていてもよい) で、文の頭か記号の後ろにあり、後ろが記号か文の終わりか。
    /// こういう「え」は聞き返し・驚き (え、しらん) で、絵 や 得 ではない。
    /// </summary>
    private static bool IsInterjection(CompositionEnv env, List<Clause> clauses, int i)
    {
        var reading = clauses[i].Reading;
        if (reading.Length == 0 || reading[0] != 'え' || !reading.Skip(1).All(SentencePunctuation.Contains) || clauses[i].Text == reading) return false;
        var before = i > 0 ? clauses[i - 1].Text : env.Context().PrecedingText ?? "";
        if (before.Length > 0 && !SentencePunctuation.Contains(before[^1])) return false;
        if (reading.Length > 1) return true;
        return i + 1 == clauses.Count || clauses[i + 1].Text is [var next, ..] && SentencePunctuation.Contains(next);
    }

    /// <summary>
    /// 小書きのかな (ぃ ぇ ゃ …) で始まる文節は前の文節とつなげる。小書きのかなから始まる語は無いので、
    /// 変換エンジンの知らない語を区切り間違えたもの (こうぃ → 光|ぃ)。つなげた読みは、その文節だけで変換し直す
    /// (こうぃ → コウィ。wi で打っていれば こゐ も候補に出る)。
    /// </summary>
    private static IReadOnlyList<ConversionClause> JoinSmallKana(CompositionEnv env, IReadOnlyList<ConversionClause> parts, string before, bool zenzai)
    {
        if (!parts.Skip(1).Any(p => p.Reading.Length > 0 && KanaChars.IsSmallKana(p.Reading[0]))) return parts;
        var joined = new List<ConversionClause>();
        foreach (var part in parts)
        {
            if (joined.Count > 0 && part.Reading.Length > 0 && KanaChars.IsSmallKana(part.Reading[0]))
            {
                var reading = joined[^1].Reading + part.Reading;
                // 変換しても漢字の後ろに小書きのかなが残る (光ぃ) なら、カタカナ (コウィ) にする。
                var text = Convert(env, reading, before + string.Concat(joined.Take(joined.Count - 1).Select(c => c.Text)), zenzai);
                if (Enumerable.Range(1, Math.Max(0, text.Length - 1)).Any(i => KanaChars.IsSmallKana(text[i]) && !IsKana(text[i - 1]))) text = CompositionText.ToKatakana(reading);
                joined[^1] = new ConversionClause(reading, text);
            }
            else joined.Add(part);
        }
        return joined;
    }

    private static bool IsKana(char c) => c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー';

    private static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);

    private static void Prefer(Clause clause, string text)
    {
        clause.Candidates.Remove(text);
        clause.Candidates.Insert(0, text);
        clause.Index = 0;
    }

    /// <summary>
    /// 日本語の文節に候補を足す (Space で変換を始めたとき。ライブ変換の表示は最初の候補だけを使うので足さない)。
    /// 候補は <see cref="JapaneseCandidates"/> の順で、文節を作るときに選び直した候補 (文脈の手がかり・学習など) は先頭のまま。
    /// </summary>
    private static void AddCandidates(CompositionEnv env, Clause clause)
    {
        if (clause.Engine is not { } engine || clause.EngineCandidatesAdded) return;
        var (chosen, text) = (clause.Candidates, clause.Text);
        clause.Candidates = [.. chosen, .. JapaneseCandidates(env, clause.Reading, engine, null, zenzai: false).Where(c => !chosen.Contains(c))];
        clause.Index = clause.Candidates.IndexOf(text);
        clause.EngineCandidatesAdded = true;
    }

    /// <summary>
    /// 文節の候補: 文の中での変換結果 → その文節だけでの変換結果 → 補助辞書の同音異義語 → ひらがな → 全角カタカナ → 半角カタカナ
    /// → 分野の辞書の語 → 絵文字・顔文字 (逆順)。
    /// 分野の辞書の語 (病名・駅名など) は、使わない人には要らない候補なので、ふだんの候補 (変換エンジンの候補・ひらがな・カタカナ) を
    /// 1 つも後ろに押さないよう、その後ろに並べる。こうすれば誰にとっても困らないので、分野ごとに外す設定は作らない (#1650)。
    /// 絵文字・顔文字は最後に逆順で並べるので、変換してすぐ ↑ を押すと、いちばんよく使う絵文字 (えがお → 😊) になる (issue #133)。
    /// </summary>
    /// <param name="before">文の中での変換結果 (inContext) が無いときに、その読みだけの変換に渡す文脈 (<see cref="SegmentBefore"/>)。inContext があれば null。</param>
    private static List<string> JapaneseCandidates(CompositionEnv env, string reading, string? inContext, string? before, bool zenzai)
    {
        // 文の中での変換結果が無いとき (文節の区切りを変えた・英語の文節をローマ字として読んだ) は、その読みだけの変換が最初の候補になるので、Zenzai も使う (Convert)
        var standalone = inContext is null ? Convert(env, reading, before, zenzai) : Standalone(env, reading);
        var katakana = CompositionText.ToKatakana(reading);
        // ユーザー辞書に登録した単語は、文の中での変換結果の次に出す (文節の区切りを変えて読みが一致したときなど)。
        return DistinctShown(env, reading, english: false, [inContext, .. env.Services.UserDictionary?.Lookup(reading) ?? [], standalone, .. env.Services.Candidates?.LookupWords(reading) ?? [],
            reading, katakana, CompositionText.ToHalfWidthKatakana(katakana), .. env.Services.Candidates?.LookupDomain(reading) ?? [], .. EmojiBlock(env, reading)]);
    }

    /// <summary>
    /// 入力欄に出る文字 (<see cref="NotationRules.Write"/>) が同じ候補は、最初の 1 つだけ残す。「，．」の設定では、打った 、 の候補と、変換エンジンが返す ， の候補が同じ ， になる。
    /// 変換エンジンの候補が入る所 (この関数を呼ぶ所) だけで重複を消す。
    /// </summary>
    private static List<string> DistinctShown(CompositionEnv env, string reading, bool english, IEnumerable<string?> candidates)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        return [.. candidates.OfType<string>().Where(c => c.Length > 0 && shown.Add(NotationRules.Write(english ? ComposedText.English(c) : ComposedText.Converted(reading, c), env.Settings.Punctuation)))];
    }

    /// <summary>絵文字・顔文字の候補を、最後に並べる順 (逆順: いちばんよく使うものが最後) で。</summary>
    private static IEnumerable<string> EmojiBlock(CompositionEnv env, string reading) =>
        (env.Services.Candidates?.LookupEmoji(reading) ?? []).Reverse();

    /// <summary>
    /// 絵文字・顔文字の候補を候補の一覧の最後に移す (英訳・打ったままの英字を足した後に呼ぶ)。
    /// 変換エンジンが最初の候補に絵文字を返したときなど、今選んでいる候補は動かさない。
    /// </summary>
    private static void MoveEmojiLast(CompositionEnv env, Clause clause)
    {
        var block = EmojiBlock(env, clause.Reading).Where(e => clause.Candidates.IndexOf(e) > 0).ToList();
        if (block.Count == 0) return;
        var current = clause.Text;
        clause.Candidates.RemoveAll(block.Contains);
        clause.Candidates.AddRange(block);
        clause.Index = Math.Max(0, clause.Candidates.IndexOf(current));
    }

    /// <summary>英語の文節の候補: 見せている表記 (打ったまま、iPhone など自動で大文字小文字を直す固有名詞はその形) → 打ったまま → 固有名詞の正しい形 → 先頭だけ大文字 → すべて大文字 → 全角。</summary>
    private static List<string> EnglishCandidates(CompositionEnv env, string raw, string shown)
    {
        var lower = raw.ToLowerInvariant();
        var canonical = env.Detector.ProperNouns.Canonical(lower);
        var capitalized = raw.Length > 0 ? char.ToUpperInvariant(raw[0]) + raw[1..] : raw;
        return Distinct(shown, raw, canonical, capitalized, raw.ToUpperInvariant(), CompositionText.ToFullWidth(raw));
    }

    /// <summary>英語と判定した語を、ローマ字として読んだかな (go → ご)。読み切れなければ null。</summary>
    private static string? RomajiReading(CompositionEnv env, string raw)
    {
        // 大文字だけで打った語 (TEN) は、最後の n を ん と読む (TEN → てん: issue #833)。小文字の英文の中の語 (pen) は日本語にしない
        var finalN = raw.All(char.IsAsciiLetterUpper);
        if (env.Detector.Romaji.AnalyzeFragment(raw.ToLowerInvariant()) is not { IsValid: true } analysis || analysis.Partial != "" && !(finalN && analysis.Partial == "n") ||
            analysis.Kana.Length == 0 && analysis.Partial.Length == 0) return null;
        return analysis.Kana + (analysis.Partial == "n" ? "ん" : "");
    }

    /// <summary>日本語の候補 (かな・漢字) か。英語の文節で選ばれたら、その語を日本語として覚える。</summary>
    internal static List<string> Distinct(params string?[] candidates)
    {
        var list = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && !list.Contains(candidate)) list.Add(candidate);
        }
        return list;
    }

    /// <summary>候補の一覧の次 (前) のページの最初の候補を選ぶ。最後のページの次は最初のページに戻る。</summary>
    internal static void NextCandidatePage(Conversion conversion, CompositionEnv env, int step)
    {
        var clause = conversion.SelectedClause;
        if (!clause.IsEnglish && !clause.Expanded) Expand(env, clause);
        var pages = (clause.Candidates.Count + CandidatePageSize - 1) / CandidatePageSize;
        if (pages <= 1) return;
        var page = (Math.Max(0, clause.Index) / CandidatePageSize + step + pages) % pages;
        clause.Index = page * CandidatePageSize;
        clause.Changed = true;
    }

    internal static void NextCandidate(Conversion conversion, CompositionEnv env, int step)
    {
        var clause = conversion.SelectedClause;
        if (!clause.Expanded) Expand(env, clause);
        clause.Index = (clause.Index + step + clause.Candidates.Count) % clause.Candidates.Count;
        clause.Changed = true;
    }

    /// <summary>
    /// 候補の一覧で、ユーザー辞書・同梱の辞書 (candidates.txt・brands.txt) の語のうち、読み (かな) の候補より前にある最後のものの位置。
    /// 無ければ 0 (最初の候補)。読みより後ろに回した辞書の語 (記号の 、 など) は数えない。
    /// </summary>
    private static int DictionaryWordsEnd(CompositionEnv env, Clause clause, string reading)
    {
        var kana = clause.Candidates.IndexOf(reading);
        var limit = kana < 0 ? clause.Candidates.Count : kana;
        var words = (env.Services.UserDictionary?.Lookup(reading) ?? []).Concat(env.Services.Candidates?.LookupWords(reading) ?? []);
        return Math.Max(0, words.Select(word => clause.Candidates.IndexOf(word)).Where(index => index < limit).DefaultIfEmpty(-1).Max());
    }

    /// <summary>
    /// 変換中に文節を選んだときに、変換エンジンの候補の一覧 (はし → 橋 端 箸 …) を足す。
    /// 日本語の文節は、最初の候補とユーザー辞書・同梱の辞書の語の後ろに足す (DictionaryWordsEnd)。
    /// 英語の文節は、ローマ字として読んだかなで一覧を引き、かなの候補 (go → ご) の後ろに足す。
    /// 打つたびには呼ばない (時間がかかることがあるため。ライブ変換では呼ばない)。今選んでいる候補はそのまま選んだ状態にする。
    /// </summary>
    internal static void Expand(CompositionEnv env, Clause clause)
    {
        clause.Expanded = true;
        // 文全体の文節は、文節が 1 つの案のその文節の一覧を足す
        if (clause.Alternatives is { } alternatives)
        {
            foreach (var only in alternatives.Values.Where(c => c.Count == 1).Select(c => c[0]).Where(c => !c.Expanded)) Expand(env, only);
            AddSingleClauseCandidates(clause);
            return;
        }
        if (env.Services.MoreCandidates is not { } more) return;
        var reading = clause.IsEnglish ? RomajiReading(env, clause.Reading) : clause.Reading;
        if (reading is null) return;
        // 変換エンジンの候補は、日本語の文節では、最初の候補とユーザー辞書・同梱の辞書の語の後ろに入れる。
        // 最初の候補のすぐ後ろに入れると、変換エンジンが知っている読みでは、辞書の語が候補の一覧の後ろに押しやられる (saiki → PSYQUI が 58 件中 51 番目: issue #1681)
        var anchor = clause.IsEnglish ? clause.Candidates.IndexOf(reading) : Math.Max(DictionaryWordsEnd(env, clause, reading), clause.Leading - 1);
        if (anchor < 0) return;
        var current = clause.Text;
        var merged = DistinctShown(env, clause.Reading, clause.IsEnglish, [.. clause.Candidates.Take(anchor + 1), .. more(reading, env.Deadline).Select(c => NormalizeHalfWidth(c, reading)), .. clause.Candidates.Skip(anchor + 1)]);
        clause.Candidates = merged;
        clause.Index = Math.Max(0, merged.IndexOf(current));
    }

    /// <summary>
    /// Shift+→ / Shift+←: 選択中の文節を 1 文字伸ばす / 縮める。はみ出した・空いた分は次の文節とやり取りし、
    /// 変わった文節は読みから変換し直す。
    /// 英語の文節との区切りを動かすときは、英語の文節を打ったローマ字のかなで日本語の文節に読み直してからやり取りする
    /// (みー|thin|ぐ の みー を伸ばす → みーて|ぃん|ぐ)。縮めて空いた分は、後ろが英語の文節なら英語の文節はそのままにして間に新しい文節を作る。
    /// </summary>
    internal static void Resize(CompositionScene.Converting scene, CompositionEnv env, int delta)
    {
        var (input, conversion) = (scene.Input, scene.Conversion);
        conversion.Edited = true;
        SplitWholeSentence(conversion);
        var (clauses, selected) = (conversion.Clauses, conversion.Selected);
        var current = clauses[selected];
        // ユーティリティ変換の Replace の文節 (空の括弧のペアなど) は、1 つの単位として選んで確定する。
        if (current.Fixed) return;
        var next = selected + 1 < clauses.Count ? clauses[selected + 1] : null;
        // 区切りを動かせないときは、英語の文節を読み直さずにそのままにする
        if (current.IsEnglish && !CanReadAsJapanese(current)) return;
        // 打った句読点は、いつも句読点だけの文節 (SplitAtMarks) のままにする
        if (IsMarks(current) || delta > 0 && next is not null && IsMarks(next)) return;
        if (delta > 0 && (next is null || next.IsEnglish && !CanReadAsJapanese(next))) return;
        if (delta < 0 && (current.IsEnglish ? current.Kana! : current.Reading).Length <= 1) return;

        if (current.IsEnglish)
        {
            ReadAsJapanese(input, clauses, env, selected);
            current = clauses[selected];
        }
        if (delta < 0 && next is not null && IsMarks(next)) next = null;
        if (next is { IsEnglish: true })
        {
            if (delta < 0) next = null;
            else
            {
                ReadAsJapanese(input, clauses, env, selected + 1);
                next = clauses[selected + 1];
            }
        }

        if (delta > 0)
        {
            current.Reading += next!.Reading[0];
            next.Reading = next.Reading[1..];
            Reconvert(input, clauses, env, selected);
            if (next.Reading.Length == 0) clauses.Remove(next);
            else Reconvert(input, clauses, env, selected + 1);
        }
        else
        {
            var moved = current.Reading[^1];
            current.Reading = current.Reading[..^1];
            if (next is null)
            {
                next = new Clause(moved.ToString(), false, []);
                clauses.Insert(selected + 1, next);
            }
            else next.Reading = moved + next.Reading;
            Reconvert(input, clauses, env, selected);
            Reconvert(input, clauses, env, selected + 1);
        }
        RestoreRaw(input, clauses, env);
    }

    /// <summary>
    /// 文節の区切りを動かすと Reconvert が Raw を消すので、文節ごとの打った英字を取り直す (F9 / F10 が選んだ文節だけに効くのに要る)。
    /// 英語の文節は Kana (打った英字をローマ字として読んだかな) の長さ分、日本語の文節は Reading の長さ分を、文全体のかなの上で数える。
    /// 英語の文節に Kana が無い (保護区間など) ときは位置が決まらないので、取り直さない。
    /// </summary>
    private static void RestoreRaw(CompositionInput input, List<Clause> clauses, CompositionEnv env)
    {
        if (clauses.Any(c => c.IsEnglish && c.Kana is null)) return;
        var raws = CompositionText.RawForKanas(input.Text, env, [.. clauses.Select(c => c.IsEnglish ? c.Kana! : c.Reading)]);
        for (var i = 0; i < clauses.Count; i++)
        {
            if (!clauses[i].IsEnglish) clauses[i].Raw = raws[i];
        }
    }

    /// <summary>英語の文節を、打ったローマ字のかなを読みにした日本語の文節に置き換える。かなに読めない英字だけ (xyz) なら置き換えずに false。</summary>
    private static bool ReadAsJapanese(CompositionInput input, List<Clause> clauses, CompositionEnv env, int index)
    {
        if (!CanReadAsJapanese(clauses[index])) return false;
        clauses[index] = new Clause(clauses[index].Kana!, false, []);
        Reconvert(input, clauses, env, index);
        return true;
    }

    private static bool IsMarks(Clause clause) => !clause.IsEnglish && clause.Reading.All(ComposedText.IsMark);

    private static bool CanReadAsJapanese(Clause clause) => clause is { IsEnglish: true, Kana: { } kana } && kana.Any(IsKana);

    /// <summary>index の文節を、読みだけで変換し直す。文脈は、変換ボックスの中でその文節より前にある文字まで。</summary>
    private static void Reconvert(CompositionInput input, List<Clause> clauses, CompositionEnv env, int index)
    {
        var clause = clauses[index];
        clause.Candidates = JapaneseCandidates(env, clause.Reading, null, SegmentBefore(env, string.Concat(clauses.Take(index).Select(c => c.Text))), zenzai: true);
        clause.Index = 0;
        clause.Changed = false;
        // 前の読みのもの。候補の一覧は、文節を選んだときに新しい読みで取り直す
        clause.Expanded = false;
        clause.Raw = null;
        clause.Translations.Clear();
        if (env.Learning.LearnedConversion(clause.Reading) is { } learned) Prefer(clause, learned);
        KeepTypedSymbols(input, [clause], clause.Reading);
    }

    /// <summary>変換エンジンで文節に区切って変換する (待つのは env.Deadline まで)。zenzai は Zenzai にも頼むか (<see cref="ConversionRequest.Zenzai"/>)。</summary>
    private static List<ConversionClause>? Engine(CompositionEnv env, string kana, string? before, bool zenzai) =>
        env.Converter.Convert(new ConversionRequest(kana, string.IsNullOrEmpty(before) ? null : before) { Zenzai = zenzai, Deadline = env.Deadline })?.SelectMany(SplitAtMarks).ToList();

    /// <summary>
    /// 変換エンジンの文節を、打った句読点 (読みの 、。) だけの文節とほかの文節に分ける。Mozc は自分で分けるので、分けるのは読み全体で 1 つの文節の Zenzai の答えなど。
    /// 読みと文字の句読点が合わない文節・ユーザー辞書の語 (読みに 、。 を入れて登録した語) は分けない。
    /// </summary>
    internal static IEnumerable<ConversionClause> SplitAtMarks(ConversionClause clause)
    {
        if (clause.UserWord || !clause.Reading.Any(ComposedText.IsMark)) return [clause];
        var (readings, texts) = (Runs(clause.Reading), Runs(clause.Text));
        return readings.Where(Marks).SequenceEqual(texts.Where(Marks)) && readings.Select(Marks).SequenceEqual(texts.Select(Marks))
            ? readings.Zip(texts, (reading, text) => clause with { Reading = reading, Text = text })
            : [clause];

        static List<string> Runs(string text) => [.. System.Text.RegularExpressions.Regex.Matches(text, "[、。]+|[^、。]+").Select(m => m.Value)];
        static bool Marks(string run) => ComposedText.IsMark(run[0]);
    }

    /// <summary>読み全体を変換した文字列。変換できなければかなのまま。</summary>
    private static string Convert(CompositionEnv env, string kana, string? before, bool zenzai) =>
        NormalizeHalfWidth(Engine(env, kana, before, zenzai) is { Count: > 0 } clauses ? string.Concat(clauses.Select(c => c.Text)) : kana, kana);

    /// <summary>
    /// 文の中での変換結果がある文節の候補に足す「その読みだけの変換」(前の文脈なし)。Zenzai には頼まない (<see cref="ConversionRequest.Zenzai"/>)。
    /// 最初の候補は文の中での変換結果なので変わらない。
    /// </summary>
    private static string Standalone(CompositionEnv env, string kana) => Convert(env, kana, null, zenzai: false);

    /// <summary>
    /// 同じ変換ボックスの英字の区間 (ph|しけんし の ph)。日本語の区間は区間ごとに変換するので、文脈の手がかりに足す (issue #1276)。
    /// 続いた英字の区間 (gra|ph) は 1 語のままにし、日本語の区間を空白にする (英字の手がかりは英単語の境界で当てるため)。
    /// </summary>
    private static string EnglishInComposition(CompositionInput input, CompositionEnv env) =>
        string.Concat(CompositionText.ConversionSegments(input.Text, env).Select(s => s.IsEnglish ? s.Raw : " "));

    /// <summary>
    /// ライブ変換: Space を押したときと同じく、文脈・手がかり辞書・学習を使って文節ごとに変換した結果を見せる。
    /// 打つたびに呼ばれるので、かな・文脈・学習の状態が同じならキャッシュを使う。
    /// </summary>
    internal static ComposedText LiveConvert(CompositionInput input, CompositionEnv env, string kana, bool afterEnglish, string before)
    {
        // 記号 (～ ! ? …) は数えない。かなが短いうちに記号を足しただけで、的外れな漢字にしない (な～～～ が 名～～～ になっていた: issue #505)
        if (!CountsAsLive(kana)) return ComposedText.Kana(kana);
        return ComposedText.Concat(LiveClauses(input, env, kana, afterEnglish, before).Select(c => ComposedText.Converted(c.Reading, c.Text)));
    }

    /// <summary>
    /// ライブ変換するだけの拍があるか。小書きの母音・拗音は直前のかなと合わせて 1 拍 (きょ・ふゅ・しぃ は 1 拍)、っ・ん・ー は 1 拍と数える。
    /// 1 拍の拗音・外来音を、打っている途中に変換しない (きょ が 虚、ふゅ が フュ になっていた)。
    /// </summary>
    internal static bool CountsAsLive(string kana) =>
        kana.Where((c, i) => IsKana(c) && !(i > 0 && KanaChars.IsSmallKana(c) && IsKana(kana[i - 1]))).Count() >= LiveConversionMinLength;

    /// <summary>ライブ変換の文節 (読み・変換エンジンの答え・学習などで選び直した後の文字)。予測変換で最後の文節を探すときも、同じ打鍵で変換した結果を使う。</summary>
    internal static List<(string Reading, string? Engine, string Text)> LiveClauses(CompositionInput input, CompositionEnv env, string kana, bool afterEnglish, string before)
    {
        var key = string.Join("\u0001", kana, afterEnglish, before, EnglishInComposition(input, env), env.Context().FollowingText, env.Services.History?.Version);
        if (env.LiveCache.TryGetValue(key, out var cached)) return cached;
        var clauses = ConvertJapaneseSegment(input, env.WithoutWaiting(), kana, afterEnglish, before, zenzai: true).Select(c => (c.Reading, c.Engine, c.Text)).ToList();
        if (env.LiveCache.Count > 256) env.LiveCache.Clear();
        env.LiveCache[key] = clauses;
        return clauses;
    }

    /// <summary>
    /// 変換ボックスの日本語の区間を変換するときの、キャレットの前の文字列: 入力欄のキャレットの前に、変換ボックスの中でその区間より前に出ている文字
    /// (英語の区間は英字のまま) を足したもの (github + にpushした の に は、github の後ろとして変換する)。
    /// </summary>
    internal static string SegmentBefore(CompositionEnv env, string inComposition) => (env.Context().PrecedingText ?? "") + inComposition;

    /// <summary>
    /// 選択中の確定済みの文字と、その読み。ローマ字のまま入った文 (kyouhaiitenki) は、読みにせずにそのまま渡す (Meltype の判定で日本語にする: issue #288)。
    /// 読みの句読点は 、。 にする。
    /// </summary>
    internal static ReconversionSelection? ReconversionSelectionNow(CompositionEnv env)
    {
        if (env.Selection is not { Length: > 0 } text) return null;
        var reading = IsRomajiText(text) ? text : env.ReadingOf(text) is { } read ? NotationRules.Read(read, env.Settings.Punctuation) : null;
        return reading is { Length: > 0 } ? new ReconversionSelection(text, reading) : null;
    }
}
