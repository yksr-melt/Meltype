// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>変換ボックスの表示: 案内・候補の一覧・注記・予測変換の候補。</summary>
public sealed partial class CompositionController
{
    /// <summary>選んでいる候補の意味: 日本語の意味 (ウィクショナリー)、無ければ英訳 (JMdict)。</summary>
    private string? CandidateMeaning(Clause clause) =>
        _services.Meanings?.Lookup(clause.Text, clause.IsEnglish ? null : clause.Reading) ?? _services.Translations?.Meaning(clause.Text);

    /// <summary>
    /// 候補の右に小さく出す注記: 英訳 (complex)、半角 / 全角 (同じ記号・英数字の半角と全角が両方候補にあるとき。# と ＃ は見分けにくい)。
    /// 注記が 1 つも無ければ null。
    /// </summary>
    private static List<CandidateNote>? CandidateNotes(Clause clause)
    {
        // ユーティリティ変換が決めた注記 (括弧の幅。ASCII の幅変換では「」と｢｣の対応を判定できない)
        if (clause.Notes is { } fixedNotes) return [.. fixedNotes];
        var notes = clause.Candidates.Select(c =>
        {
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


    private void UpdateView()
    {
        if (_text.IsEmpty)
        {
            // 英数状態の判定中は何も表示しない (英語ならそのまま出るだけ)。
            _out.Hide();
            return;
        }
        if (_converting && _clauses.Count > 0)
        {
            var selected = _clauses[_selectedClause];
            // 選んでいる文節は、最初から候補の一覧をすべて出す (Space を 1 回押しただけでは一部しか出ず、選び間違えやすかった)。
            if (!selected.Expanded) Expand(selected);
            // 出している文節に Zenzai の答えがあれば、説明の頭に「Zenzai」と出す (学習・選び直しで変わった文節は数えない。issue #1152)
            var zenzai = _clauses.Any(c => c.ZenzaiText is { } z && c.Text == z);
            _out.Show(new CompositionView(
                string.Concat(_clauses.Select(c => c.Text)),
                selected.Candidates,
                selected.Index,
                true,
                (zenzai ? "Zenzai · " : "") + "←→ 文節　Space/↓ 候補　1〜9 選択　Shift+←→ 区切り　Enter 確定　Esc 戻る",
                _clauses.Select(c => c.Text).ToList(),
                _selectedClause,
                CandidateNotes(selected),
                _settings().ShowCandidateMeanings ? CandidateMeaning(selected) : null,
                MisspellingSuggestion(),
                Typed: TypedKeys(),
                Committed: string.Concat(_clauses.Select(c => c.Text)), ContextHint: zenzai ? "Zenzai" : null));
        }
        else
        {
            var suggestion = _text.Suggestion();
            var misspelling = MisspellingSuggestion();
            // 手動の英字提案・訂正提案がある間は予測を出さないため、contextHint の両方の案内は同時に出ない。
            UpdatePredictions(blocked: misspelling is not null || suggestion is not null);
            // 書き間違いの修正・英字の提案・予測の候補があれば、Tab はそちらに使う (HandleKey の Tab の順)
            var tabConverts = _settings().TabConversion && misspelling is null && suggestion is null && _predictions.Count == 0;
            var hint = _text.IsAlphanumeric ? "Enter 確定　Space 確定+空白　Shift+Space 日本語で変換　半角/全角 日本語に" : (tabConverts ? "Space/Tab 変換" : "Space 変換") + "　←→ 文節　Enter 確定　F7 カタカナ　F10 英字";
            var contextHint = suggestion is null ? null : $"Tab → {suggestion} (英字に)";
            if (contextHint is not null) hint = contextHint + "　" + hint;
            if (_predictions.Count > 0)
            {
                contextHint = "Tab 予測の候補を選ぶ";
                hint = contextHint + "　" + hint;
            }
            var selected = _predictionIndex >= 0 ? _predictions[_predictionIndex].Before + _predictions[_predictionIndex].Text : null;
            var display = selected ?? CurrentDisplay(final: false);
            // 設定「入力中も変換候補をいくつか表示」: Space の前から候補を出す (issue #666)
            var preview = selected is null && misspelling is null ? TypingCandidates() : null;
            _out.Show(new CompositionView(display, preview ?? [], preview?.IndexOf(display) ?? -1, false, hint, Suggestion: misspelling,
                Predictions: _predictions.Count > 0 ? _predictions.Select(p => p.Text).ToList() : null, SelectedPrediction: _predictionIndex, Typed: TypedKeys(),
                Committed: selected ?? CurrentDisplay(final: true), ContextHint: contextHint));
        }
    }

    /// <summary>設定「入力中も変換候補をいくつか表示」: 今打っているひらがなの変換候補 (5 件まで)。出さないときは null。</summary>
    private List<string>? TypingCandidates()
    {
        if (!_settings().ShowCandidatesWhileTyping || _text.Mode != DisplayMode.Auto || _text.KanaInput || _text.IsAlphanumeric || _text.IsEmpty) return null;
        var kana = _text.AllKana(final: false);
        if (kana.Length < 2 || !kana.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー')) return null;
        var candidates = WithoutWaiting(() => JapaneseCandidates(kana, null).Take(5).ToList());
        return candidates.Count > 1 ? candidates : null;
    }

    /// <summary>変換ボックスに出す、打ったキー (設定が ON のときだけ)。</summary>
    private string? TypedKeys() => _settings().ShowTypedKeys && _text.Raw is { Length: > 0 } raw ? raw : null;

    /// <summary>
    /// 予測変換の候補を、今の打ちかけから作り直す。打った内容が変わったら選んでいた候補は戻す。
    /// もしかして・手動の提案があるときは、Tab をそちらに使うので出さない。
    /// </summary>
    private void UpdatePredictions(bool blocked)
    {
        var key = PredictionKey();
        if (key == _predictionKey && !blocked) return;
        // 出さないときは覚えない (同じ打ちかけのまま出せるようになったら、作り直す)
        _predictionKey = blocked ? "" : key;
        _predictionIndex = -1;
        _predictions = [];
        if (blocked || _services.Predictor is not { } predictor || !_settings().PredictiveCandidates || _text.Mode != DisplayMode.Auto) return;
        if (_text.IsAlphanumeric)
        {
            // 英単語 1 語の打ちかけ (decis → decisions)
            if (_text.Raw.All(char.IsAsciiLetter)) _predictions = predictor.PredictEnglish(_text.Raw).Select(w => new Prediction(w, "", _text.Raw)).ToList();
            return;
        }
        var kana = _text.AllKana(final: false);
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
            foreach (var (before, reading, heads) in PredictionStarts(kana))
            {
                // 最後の文節の続きは、その文節の文字か読みで始まる候補だけ (たんいをとる → 単位をトルコ料理 のような、前とつながらない候補を出さない)
                IEnumerable<string> Connected(IEnumerable<string> words) =>
                    heads.Length == 0 ? words : words.Where(w => heads.Any(h => w.StartsWith(h, StringComparison.Ordinal)));
                Add(Connected(predictor.PredictJapanese(reading)), before, reading);
                // 英字で書く社名・サービス名 (しゃお → Xiaomi)。日本語の予測の後ろに足す
                if (_settings().EnglishNamePredictions) Add(Connected(predictor.PredictEnglishNames(reading)), before, reading);
            }
        }
        // 長い英単語は、打ち終わるまでローマ字として見えていることがある (decis → でしs)。英字だけを 4 文字以上打っていれば英単語の続きも出す
        if (_text.Raw.Length >= 4 && _text.Raw.All(char.IsAsciiLetter)) Add(predictor.PredictEnglish(_text.Raw), "", _text.Raw);
        _predictions = predictions;
    }

    /// <summary>
    /// 予測変換で続きを探す読み: 読み全体と、最後の文節 (変換エンジンの区切り)。
    /// 読み全体の続きだけだと、前に確定したのと同じ語句を頭から打たないと候補が出ない。
    /// 最後の文節の続きの候補を選んだときは、前の文節は見えているとおりに残す (たんいをとる → 単位を + 取ることができる)。
    /// Heads は、最後の文節の続きの候補が始まっていないといけない文字 (文節の文字と読み)。読み全体では空
    /// </summary>
    private List<(string Before, string Reading, string[] Heads)> PredictionStarts(string kana)
    {
        var starts = new List<(string Before, string Reading, string[] Heads)> { ("", kana, []) };
        // 文節の区切りはライブ変換の結果を使う (打鍵ごとに変換を増やさない)。ライブ変換が OFF のときと、読みが短いうちは読み全体だけ
        if (!_settings().LiveConversion || kana.Length < LiveConversionMinLength) return starts;
        var clauses = LiveClauses(kana);
        if (clauses.Count < 2) return starts;
        var last = clauses[^1];
        // 見えている文字の終わりが最後の文節 (変換した文字か、変換していない読み) のときだけ。
        // 英語として見せている所があるときなどは、前に残す文字が分からないので読み全体の続きだけにする
        var display = CurrentDisplay(final: false);
        var shown = display.EndsWith(last.Text, StringComparison.Ordinal) ? last.Text : display.EndsWith(last.Reading, StringComparison.Ordinal) ? last.Reading : null;
        if (shown is not null) starts.Add((display[..^shown.Length], last.Reading, [last.Text, last.Reading]));
        return starts;
    }

    private string PredictionKey() => _text.Raw + "\u0001" + _text.Mode;

    /// <summary>今の打ちかけの予測の候補 (キーをまとめて処理しているときにも、打った内容に合わせて作り直す)。</summary>
    private List<Prediction> CurrentPredictions()
    {
        UpdatePredictions(blocked: false);
        return _predictions;
    }

    /// <summary>Tab で選んでいる予測の候補 (打った内容が選んだときのままのときだけ)。</summary>
    private Prediction? SelectedPrediction() =>
        _predictionIndex >= 0 && _predictionIndex < _predictions.Count && _predictionKey == PredictionKey() ? _predictions[_predictionIndex] : null;
}
