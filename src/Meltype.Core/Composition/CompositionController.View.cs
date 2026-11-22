// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Config;
using Meltype.Dictionary;
using Meltype.Learning;
using Meltype.Libraries.Chars;

namespace Meltype.Composition;

/// <summary>変換ボックスの表示: 案内・候補の一覧・注記・予測変換の候補。</summary>
internal static partial class CompositionController
{
    /// <summary>選んでいる候補の意味: 日本語の意味 (ウィクショナリー)、無ければ英訳 (JMdict)。</summary>
    private static string? CandidateMeaning(CompositionState state, CompositionEnv env, Clause clause) =>
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

    private static void UpdateView(CompositionState state, CompositionEnv env)
    {
        if (CompositionText.IsEmpty(state.Text))
        {
            // 英数状態の判定中は何も表示しない (英語ならそのまま出るだけ)。
            CompositionOutput.Hide(state.Out);
            return;
        }
        if (IsConverting(state) && state.Clauses.Count > 0)
        {
            var selected = state.Clauses[state.SelectedClause];
            // 選んでいる文節は、最初から候補の一覧をすべて出す (Space を 1 回押しただけでは一部しか出ず、選び間違えやすかった)。
            if (!selected.Expanded) Expand(state, env, selected);
            // 出している文節に Zenzai の答えがあれば、説明の頭に「Zenzai」と出す (学習・選び直しで変わった文節は数えない。issue #1152)
            var zenzai = state.Clauses.Any(c => c.ZenzaiText is { } z && c.Text == z);
            CompositionOutput.Show(state.Out, env, new CompositionView(
                string.Concat(state.Clauses.Select(c => c.Text)),
                selected.Candidates,
                selected.Index,
                true,
                (zenzai ? "Zenzai · " : "") + "←→ 文節　Space/↓ 候補　1〜9 選択　Shift+←→ 区切り　Enter 確定　Esc 戻る",
                state.Clauses.Select(c => c.Text).ToList(),
                state.SelectedClause,
                CandidateNotes(selected),
                env.Settings.ShowCandidateMeanings ? CandidateMeaning(state, env, selected) : null,
                MisspellingSuggestion(state, env),
                Typed: TypedKeys(state, env),
                Committed: string.Concat(state.Clauses.Select(c => c.Text)), ContextHint: zenzai ? "Zenzai" : null));
        }
        else
        {
            var suggestion = CompositionText.Suggestion(state.Text, env);
            var misspelling = MisspellingSuggestion(state, env);
            // 手動の英字提案・訂正提案がある間は予測を出さないため、contextHint の両方の案内は同時に出ない。
            UpdatePredictions(state, env, blocked: misspelling is not null || suggestion is not null);
            // 書き間違いの修正・英字の提案・予測の候補があれば、Tab はそちらに使う (HandleKey の Tab の順)
            var tabConverts = env.Settings.TabConversion && misspelling is null && suggestion is null && state.Predictions.Count == 0;
            var hint = CompositionText.IsAlphanumeric(state.Text, env) ? "Enter 確定　Space 確定+空白　Shift+Space 日本語で変換　半角/全角 日本語に" : (tabConverts ? "Space/Tab 変換" : "Space 変換") + "　←→ 文節　Enter 確定　F7 カタカナ　F10 英字";
            var contextHint = suggestion is null ? null : $"Tab → {suggestion} (英字に)";
            if (contextHint is not null) hint = contextHint + "　" + hint;
            if (state.Predictions.Count > 0)
            {
                contextHint = "Tab 予測の候補を選ぶ";
                hint = contextHint + "　" + hint;
            }
            var selected = state.PredictionIndex >= 0 ? state.Predictions[state.PredictionIndex].Before + state.Predictions[state.PredictionIndex].Text : null;
            var display = selected ?? CurrentDisplay(state, env, final: false);
            // 設定「入力中も変換候補をいくつか表示」: Space の前から候補を出す (issue #666)
            var preview = selected is null && misspelling is null ? TypingCandidates(state, env) : null;
            CompositionOutput.Show(state.Out, env, new CompositionView(display, preview ?? [], preview?.IndexOf(display) ?? -1, false, hint, Suggestion: misspelling,
                Predictions: state.Predictions.Count > 0 ? state.Predictions.Select(p => p.Text).ToList() : null, SelectedPrediction: state.PredictionIndex, Typed: TypedKeys(state, env),
                Committed: selected ?? CurrentDisplay(state, env, final: true), ContextHint: contextHint));
        }
    }

    /// <summary>設定「入力中も変換候補をいくつか表示」: 今打っているひらがなの変換候補 (5 件まで)。出さないときは null。</summary>
    private static List<string>? TypingCandidates(CompositionState state, CompositionEnv env)
    {
        if (!env.Settings.ShowCandidatesWhileTyping || state.Text.Mode != DisplayMode.Auto || state.Text.KanaInput || CompositionText.IsAlphanumeric(state.Text, env) || CompositionText.IsEmpty(state.Text)) return null;
        var kana = CompositionText.AllKana(state.Text, env, final: false);
        if (kana.Length < 2 || !kana.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー')) return null;
        var candidates = JapaneseCandidates(state, WithoutWaiting(env), kana, null, SegmentBefore(env, ""), zenzai: true).Take(5).ToList();
        return candidates.Count > 1 ? candidates : null;
    }

    /// <summary>変換ボックスに出す、打ったキー (設定が ON のときだけ)。</summary>
    private static string? TypedKeys(CompositionState state, CompositionEnv env) => env.Settings.ShowTypedKeys && CompositionText.Raw(state.Text) is { Length: > 0 } raw ? raw : null;

    /// <summary>
    /// 予測変換の候補を、今の打ちかけから作り直す。打った内容が変わったら選んでいた候補は戻す。
    /// もしかして・手動の提案があるときは、Tab をそちらに使うので出さない。
    /// </summary>
    private static void UpdatePredictions(CompositionState state, CompositionEnv env, bool blocked)
    {
        var key = PredictionKey(state);
        if (key == state.PredictionKey && !blocked) return;
        // 出さないときは覚えない (同じ打ちかけのまま出せるようになったら、作り直す)
        state.PredictionKey = blocked ? "" : key;
        state.PredictionIndex = -1;
        state.Predictions = [];
        if (blocked || env.Services.Predictor is not { } predictor || !env.Settings.PredictiveCandidates || state.Text.Mode != DisplayMode.Auto) return;
        if (CompositionText.IsAlphanumeric(state.Text, env))
        {
            // 英単語 1 語の打ちかけ (decis → decisions)
            if (CompositionText.Raw(state.Text).All(char.IsAsciiLetter)) state.Predictions = predictor.PredictEnglish(CompositionText.Raw(state.Text)).Select(w => new Prediction(w, "", CompositionText.Raw(state.Text))).ToList();
            return;
        }
        var kana = CompositionText.AllKana(state.Text, env, final: false);
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
            foreach (var (before, reading, heads) in PredictionStarts(state, env, kana))
            {
                // 最後の文節の続きは、その文節の文字か読みで始まる候補だけ (たんいをとる → 単位をトルコ料理 のような、前とつながらない候補を出さない)
                IEnumerable<string> Connected(IEnumerable<string> words) =>
                    heads.Length == 0 ? words : words.Where(w => heads.Any(h => w.StartsWith(h, StringComparison.Ordinal)));
                Add(Connected(predictor.PredictJapanese(reading)), before, reading);
                // 英字で書く社名・サービス名 (しゃお → Xiaomi)。日本語の予測の後ろに足す
                if (env.Settings.EnglishNamePredictions) Add(Connected(predictor.PredictEnglishNames(reading)), before, reading);
            }
        }
        // 長い英単語は、打ち終わるまでローマ字として見えていることがある (decis → でしs)。英字だけを 4 文字以上打っていれば英単語の続きも出す
        if (CompositionText.Raw(state.Text).Length >= 4 && CompositionText.Raw(state.Text).All(char.IsAsciiLetter)) Add(predictor.PredictEnglish(CompositionText.Raw(state.Text)), "", CompositionText.Raw(state.Text));
        state.Predictions = predictions;
    }

    /// <summary>
    /// 予測変換で続きを探す読み: 読み全体と、最後の文節 (変換エンジンの区切り)。
    /// 読み全体の続きだけだと、前に確定したのと同じ語句を頭から打たないと候補が出ない。
    /// 最後の文節の続きの候補を選んだときは、前の文節は見えているとおりに残す (たんいをとる → 単位を + 取ることができる)。
    /// Heads は、最後の文節の続きの候補が始まっていないといけない文字 (文節の文字と読み)。読み全体では空
    /// </summary>
    private static List<(string Before, string Reading, string[] Heads)> PredictionStarts(CompositionState state, CompositionEnv env, string kana)
    {
        var starts = new List<(string Before, string Reading, string[] Heads)> { ("", kana, []) };
        // 文節の区切りはライブ変換の結果を使う (打鍵ごとに変換を増やさない)。ライブ変換が OFF のときと、読みが短いうちは読み全体だけ
        if (!env.Settings.LiveConversion || !CountsAsLive(kana)) return starts;
        var clauses = LiveClauses(state, env, kana, afterEnglish: false, SegmentBefore(env, ""));
        if (clauses.Count < 2) return starts;
        var last = clauses[^1];
        // 見えている文字の終わりが最後の文節 (変換した文字か、変換していない読み) のときだけ。
        // 英語として見せている所があるときなどは、前に残す文字が分からないので読み全体の続きだけにする
        var display = CurrentDisplay(state, env, final: false);
        var shown = display.EndsWith(last.Text, StringComparison.Ordinal) ? last.Text : display.EndsWith(last.Reading, StringComparison.Ordinal) ? last.Reading : null;
        if (shown is not null) starts.Add((display[..^shown.Length], last.Reading, [last.Text, last.Reading]));
        return starts;
    }

    private static string PredictionKey(CompositionState state) => CompositionText.Raw(state.Text) + "\u0001" + state.Text.Mode;

    /// <summary>今の打ちかけの予測の候補 (キーをまとめて処理しているときにも、打った内容に合わせて作り直す)。</summary>
    private static List<Prediction> CurrentPredictions(CompositionState state, CompositionEnv env)
    {
        UpdatePredictions(state, env, blocked: false);
        return state.Predictions;
    }

    /// <summary>Tab で選んでいる予測の候補 (打った内容が選んだときのままのときだけ)。</summary>
    private static Prediction? SelectedPrediction(CompositionState state) =>
        state.PredictionIndex >= 0 && state.PredictionIndex < state.Predictions.Count && state.PredictionKey == PredictionKey(state) ? state.Predictions[state.PredictionIndex] : null;
}
