// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using Meltype.Config;
using Meltype.Dictionary;
using Meltype.Learning;
using Meltype.Libraries.Chars;
using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>変換ボックスの表示、案内、候補の注記と予測。確定やキーの処理は呼ばない。</summary>
internal static class CompositionPresentation
{

    /// <summary>選んでいる候補の意味: 日本語の意味 (ウィクショナリー)、無ければ英訳 (JMdict)。</summary>
    private static string? CandidateMeaning(CompositionEnv env, Clause clause) =>
        env.Services.Meanings?.Lookup(clause.Text, clause.IsEnglish ? null : clause.Reading) ?? env.Services.Translations?.Meaning(clause.Text);

    /// <summary>
    /// 候補の右に小さく出す注記: Zenzai の答え、英訳 (complex)、半角 / 全角 (同じ記号・英数字の半角と全角が両方候補にあるとき。# と ＃ は見分けにくい)。
    /// 注記が 1 つも無ければ null。
    /// </summary>
    private static List<CandidateNote>? CandidateNotes(Clause clause)
    {
        // ユーティリティ変換が決めた注記 (括弧の幅。ASCII の幅変換では「」と｢｣の対応を判定できない)
        if (clause.Notes is { } fixedNotes) return [.. fixedNotes];
        var notes = clause.Candidates.Select(c =>
        {
            if (c == clause.ZenzaiText) return CandidateNote.Zenzai;
            if (clause.Translations.Contains(c)) return CandidateNote.Translation;
            var half = ToHalfWidth(c);
            if (half != c && clause.Candidates.Contains(half)) return CandidateNote.FullWidth;
            var full = CompositionText.ToFullWidth(c);
            if (full != c && clause.Candidates.Contains(full)) return CandidateNote.HalfWidth;
            return CandidateNote.None;
        }).ToList();
        return notes.Any(n => n != CandidateNote.None) ? notes : null;
    }

    /// <summary>全角の英数字・記号 (！〜～) と全角の空白を半角にする。</summary>
    private static string ToHalfWidth(string text) =>
        new(text.Select(c => c is >= '！' and <= '～' ? (char)(c - 0xFEE0) : c == '　' ? ' ' : c).ToArray());

    internal static void UpdateView(CompositionState state, CompositionEnv env)
    {
        // 英数状態の判定中は何も表示しない (英語ならそのまま出るだけ)。
        var view = state.Scene.Match(new CompositionScene.Cases<ComposedView?>
        {
            Japanese = _ => null,
            Direct = _ => null,
            Judging = _ => null,
            EnglishWord = _ => null,
            Typing = typing => TypingView(typing.Input, env),
            Converting = converting => ConvertingView(converting, env),
        });
        if (view is null) CompositionOutput.Hide(state.Out);
        else CompositionOutput.Show(state.Out, env, view);
    }

    private static ComposedView ConvertingView(CompositionScene.Converting scene, CompositionEnv env)
    {
        var (input, conversion) = (scene.Input, scene.Conversion);
        var selected = conversion.SelectedClause;
        // 選んでいる文節は、最初から候補の一覧をすべて出す (Space を 1 回押しただけでは一部しか出ず、選び間違えやすかった)。
        if (!selected.Expanded) CompositionConversion.Expand(env, selected);
        // 出している文節に Zenzai の答えがあれば、説明の頭に「Zenzai」と出す (学習・選び直しで変わった文節は数えない。issue #1152)
        var zenzai = conversion.Clauses.Any(c => c.ZenzaiText is { } z && c.Text == z);
        var clauses = conversion.Clauses.Select(c => Composed([c])).ToList();
        return new ComposedView(
            ComposedText.Concat(clauses),
            [.. selected.Candidates.Select(c => Candidate(selected, c))],
            selected.Index,
            true,
            (zenzai ? "Zenzai · " : "") + "←→ 文節　Space/↓ 候補　1〜9 選択　Shift+←→ 区切り　Enter 確定　Esc 戻る",
            clauses,
            conversion.Selected,
            CandidateNotes(selected),
            CandidateMeaning(env, selected),
            Suggestion(CompositionSpelling.MisspellingSuggestion(input, env)),
            Typed: TypedKeys(input, conversion, env),
            Committed: ComposedText.Concat(clauses), ContextHint: zenzai ? "Zenzai" : null, Colors: selected.Colors);
    }

    private static ComposedView TypingView(CompositionInput input, CompositionEnv env)
    {
        var suggestion = CompositionText.Suggestion(CompositionText.Read(input.Text, env), env);
        var misspelling = CompositionSpelling.MisspellingSuggestion(input, env);
        // 手動の英字提案・訂正提案がある間は予測を出さないため、contextHint の両方の案内は同時に出ない。
        UpdatePredictions(input, env, blocked: misspelling is not null || suggestion is not null);
        // 書き間違いの修正・英字の提案・予測の候補があれば、Tab はそちらに使う (HandleKey の Tab の順)
        var predictions = input.Predictions?.Items ?? [];
        var predictionIndex = input.Predictions?.Index ?? -1;
        var tabConverts = env.Settings.TabConversion && misspelling is null && suggestion is null && predictions.Count == 0;
        var hint = CompositionText.IsAlphanumeric(CompositionText.Read(input.Text, env), env) ? "Enter 確定　Space 確定+空白　Shift+Space 日本語で変換　半角/全角 日本語に" : (tabConverts ? "Space/Tab 変換" : "Space 変換") + "　←→ 文節　Enter 確定　F7 カタカナ　F10 英字";
        var contextHint = suggestion is null ? null : $"Tab → {suggestion} (英字に)";
        if (contextHint is not null) hint = contextHint + "　" + hint;
        if (predictions.Count > 0)
        {
            contextHint = "Tab 予測の候補を選ぶ";
            hint = contextHint + "　" + hint;
        }
        var selected = predictionIndex >= 0 ? PredictionText(predictions[predictionIndex]) : null;
        var display = selected ?? CurrentComposed(input, env, final: false);
        return new ComposedView(display, [], -1, false, hint,
            Suggestion: Suggestion(misspelling),
            Predictions: predictions.Count > 0 ? predictions.Select(p => ComposedText.Of(SpanKind.Japanese, p.Text)).ToList() : null, SelectedPrediction: predictionIndex, Typed: TypedKeys(input, null, env),
            Committed: selected ?? CurrentComposed(input, env, final: true), ContextHint: contextHint,
            CaretBack: selected is null ? CompositionText.TailText(input.Text).Length : 0);
    }

    /// <summary>文節 clause の候補 candidate の文字。文全体の文節の案は、確定と同じく案の文節から作る (<see cref="CompositionConversion.SplitWholeSentence"/>)。</summary>
    /// <remarks>英語の文節のかな・漢字の入った候補 (ハロー・midiか) は日本語の区間にする (英単語の前後の空白は、日本語の区間の中の英字の並びの前後に入る)。</remarks>
    internal static ComposedText Candidate(Clause clause, string candidate) =>
        clause.Alternatives?.TryGetValue(candidate, out var clauses) == true ? Composed(clauses)
        : clause.IsEnglish && !NotationRules.HasJapanese(candidate) ? ComposedText.English(candidate)
        : clause.IsEnglish ? ComposedText.Of(SpanKind.Japanese, candidate)
        : ComposedText.Converted(clause.Reading, candidate);

    /// <summary>書き間違いの修正の案内 (もしかして)。</summary>
    private static ComposedText? Suggestion(string? suggestion) => suggestion is null ? null : ComposedText.Of(SpanKind.Japanese, suggestion);

    /// <summary>変換ボックスに出す、打ったもの (設定「打ったキーを表示」)。漢字変換前は、変換中なら文節の読み、変換していなければ確定される形 (<see cref="CurrentComposed"/>) を漢字にせずに出す。</summary>
    private static string? TypedKeys(CompositionInput input, Conversion? conversion, CompositionEnv env) => env.Settings.TypedKeys switch
    {
        TypedKeysStyle.Keys when input.Text.KanaInput => input.Text.KanaKeyLabels,
        TypedKeysStyle.Keys => CompositionText.Raw(CompositionText.Read(input.Text, env)),
        TypedKeysStyle.Kana when conversion is not null => string.Concat(conversion.Clauses.Select(CompositionConversion.Reading)),
        TypedKeysStyle.Kana => CompositionText.Compose(CompositionText.Read(input.Text, env), env, final: true).Text,
        TypedKeysStyle.Off => null,
        var style => throw new InvalidOperationException($"不明な TypedKeysStyle: {style}"),
    };

    /// <summary>
    /// 予測変換の候補を、今の打ちかけから作り直す。打った内容が変わったら選んでいた候補は戻す。
    /// もしかして・手動の提案があるときは、Tab をそちらに使うので出さない。
    /// </summary>
    private static void UpdatePredictions(CompositionInput input, CompositionEnv env, bool blocked)
    {
        var key = PredictionKey(input);
        if (input.Predictions?.Key == key && !blocked) return;
        // 出さないときは覚えない (同じ打ちかけのまま出せるようになったら、作り直す)
        input.Predictions = blocked ? null : new PredictionList(key, BuildPredictions(input, env));
    }

    private static List<Prediction> BuildPredictions(CompositionInput input, CompositionEnv env)
    {
        if (!env.Settings.PredictiveCandidates || input.Text.Mode != DisplayMode.Auto) return [];
        var raw = CompositionText.Raw(CompositionText.Read(input.Text, env));
        if (env.Services.Predictor is not { } predictor) return [];
        IEnumerable<string> PredictEnglish() => Predictor.PredictEnglish(raw).Select(w => NotationRules.Word(w, env.ProperNoun));
        if (CompositionText.IsAlphanumeric(CompositionText.Read(input.Text, env), env))
        {
            // 英単語 1 語の打ちかけ (decis → decisions)
            return raw.All(char.IsAsciiLetter) ? PredictEnglish().Select(w => new Prediction(w, "", raw)).ToList() : [];
        }
        var kana = CompositionText.AllKana(CompositionText.Read(input.Text, env), env, final: false);
        var predictions = new List<Prediction>();
        void Add(IEnumerable<string> words, string before, string reading)
        {
            foreach (var word in words)
            {
                if (predictions.Count < Predictor.MaxPredictions && !predictions.Any(p => p.Text == word)) predictions.Add(new Prediction(word, before, reading));
            }
        }
        if (kana.All(KanaChars.IsHiraganaOrLongMark))
        {
            foreach (var (before, reading, heads) in PredictionStarts(input, env, kana))
            {
                // 最後の文節の続きは、その文節の文字か読みで始まる候補だけ (たんいをとる → 単位をトルコ料理 のような、前とつながらない候補を出さない)
                IEnumerable<string> Connected(IEnumerable<string> words) =>
                    heads.Length == 0 ? words : words.Where(w => heads.Any(h => w.StartsWith(h, StringComparison.Ordinal)));
                Add(Connected(predictor.PredictJapanese(reading)), before, reading);
                // 英字で書く社名・サービス名 (しゃお → Xiaomi)。日本語の予測の後ろに足す
                if (env.Settings.EnglishNamePredictions) Add(Connected(Predictor.PredictEnglishNames(reading)), before, reading);
            }
        }
        // 長い英単語は、打ち終わるまでローマ字として見えていることがある (decis → でしs)。英字だけを 4 文字以上打っていれば英単語の続きも出す
        if (raw.Length >= 4 && raw.All(char.IsAsciiLetter)) Add(PredictEnglish(), "", raw);
        return predictions;
    }

    /// <summary>
    /// 予測変換で続きを探す読み: 読み全体と、最後の文節 (変換エンジンの区切り)。
    /// 読み全体の続きだけだと、前に確定したのと同じ語句を頭から打たないと候補が出ない。
    /// 最後の文節の続きの候補を選んだときは、前の文節は見えているとおりに残す (たんいをとる → 単位を + 取ることができる)。
    /// Heads は、最後の文節の続きの候補が始まっていないといけない文字 (文節の文字と読み)。読み全体では空
    /// </summary>
    private static List<(string Before, string Reading, string[] Heads)> PredictionStarts(CompositionInput input, CompositionEnv env, string kana)
    {
        var starts = new List<(string Before, string Reading, string[] Heads)> { ("", kana, []) };
        // 文節の区切りはライブ変換の結果を使う (打鍵ごとに変換を増やさない)。ライブ変換が OFF のときと、読みが短いうちは読み全体だけ
        if (!env.Settings.LiveConversion || !CompositionConversion.CountsAsLive(kana)) return starts;
        var clauses = CompositionConversion.LiveClauses(input, env, kana, afterEnglish: false, CompositionConversion.SegmentBefore(env, ""));
        if (clauses.Count < 2) return starts;
        var last = clauses[^1];
        // 見えている文字の終わりが最後の文節 (変換した文字か、変換していない読み) のときだけ。
        // 英語として見せている所があるときなどは、前に残す文字が分からないので読み全体の続きだけにする
        var display = CurrentDisplay(input, env, final: false);
        var shown = display.EndsWith(last.Text, StringComparison.Ordinal) ? last.Text : display.EndsWith(last.Reading, StringComparison.Ordinal) ? last.Reading : null;
        if (shown is not null) starts.Add((display[..^shown.Length], last.Reading, [last.Text, last.Reading]));
        return starts;
    }

    private static string PredictionKey(CompositionInput input) => input.Text.TypedRaw + "\u0001" + input.Text.Mode;

    /// <summary>今の打ちかけの予測の候補 (キーをまとめて処理しているときにも、打った内容に合わせて作り直す)。</summary>
    internal static List<Prediction> CurrentPredictions(CompositionInput input, CompositionEnv env)
    {
        UpdatePredictions(input, env, blocked: false);
        return input.Predictions?.Items ?? [];
    }

    /// <summary>Tab で選んでいる予測の候補 (打った内容が選んだときのままのときだけ)。</summary>
    internal static Prediction? SelectedPrediction(CompositionInput input) =>
        input.Predictions is { Index: >= 0 } p && p.Index < p.Items.Count && p.Key == PredictionKey(input) ? p.Items[p.Index] : null;

    /// <summary>今の表示 (確定するときと同じ読み。ライブ変換が有効なら日本語区間は漢字に変換済み)。</summary>
    internal static ComposedText CurrentComposed(CompositionInput input, CompositionEnv env, bool final) =>
        ComposedBeforeCaret(input, env, final) + ComposedText.Kana(CompositionText.TailText(input.Text));

    // 読みの途中を直している間の、カーソルより前 (後ろはひらがなのまま足す)
    private static ComposedText ComposedBeforeCaret(CompositionInput input, CompositionEnv env, bool final) =>
        CompositionText.Compose(CompositionText.Read(input.Text, env), env, final, LiveAnswer(input, env, wait: false));

    /// <summary>自動の表示の文節の並び (<see cref="CompositionText.Clauses"/>)。ライブ変換の答えは待たない。</summary>
    internal static IReadOnlyList<TypingClause> AutoClauses(CompositionInput input, CompositionEnv env, bool final) =>
        CompositionText.Clauses(CompositionText.Read(input.Text, env), env, final, LiveAnswer(input, env, wait: false));

    private static Func<string, bool, string, IReadOnlyList<TypingClause.Japanese>>? LiveAnswer(CompositionInput input, CompositionEnv env, bool wait) => env.Settings.LiveConversion
        ? (kana, afterEnglish, shown) => CompositionConversion.LiveAnswer(input, env, kana, afterEnglish, CompositionConversion.SegmentBefore(env, shown), wait)
        : null;

    /// <summary><see cref="CurrentComposed"/> の文字。</summary>
    internal static string CurrentDisplay(CompositionInput input, CompositionEnv env, bool final) => CurrentComposed(input, env, final).Text;

    /// <summary>
    /// 変換していないときに確定する文字。答えを後から受け取れるフロントでは、ライブ変換の答えが間に合っていなくても env.Deadline までは答えを待つ
    /// (受け取れないフロントは、表示のときから待っている)。
    /// </summary>
    internal static ComposedText CommittedDisplay(CompositionInput input, CompositionEnv env) =>
        CompositionText.Compose(CompositionText.Read(input.Text, env), env, final: true, LiveAnswer(input, env, wait: env.LateResults));

    /// <summary>文節の文字。</summary>
    internal static ComposedText Composed(IEnumerable<Clause> clauses) => ComposedText.Concat(clauses.Select(c => Candidate(c, c.Text)));

    /// <summary>予測の候補を、前の文節 (見えている文字) とつなげた文字。どちらも打った句読点は無い (予測はひらがなだけの読みで出し、語句は確定した文字のまま覚えている)。</summary>
    internal static ComposedText PredictionText(Prediction prediction) =>
        ComposedText.Of(SpanKind.Japanese, prediction.Before) + ComposedText.Of(prediction.Text.All(c => c < 0x80) ? SpanKind.English : SpanKind.Japanese, prediction.Text);
}
