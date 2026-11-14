// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.RegularExpressions;
using Clause = Meltype.Composition.CompositionController.Clause;

namespace Meltype.Composition;

/// <summary>
/// 確定した入力を学習する (変換履歴・言語の学習・英訳の記録・予測変換の語句・変換エンジン)。
/// 設定「ユーザー学習を使う」と入力欄が学習を求めないかは、呼ぶ側 (<see cref="CompositionController"/>) が見る。
/// 学習しない候補は <see cref="Learnable"/> の 1 か所で決める。
/// </summary>
internal sealed partial class CompositionLearning(CompositionServices services, IKanjiConverter converter)
{
    private static readonly string[] EraNames = ["令和", "平成", "昭和", "大正", "明治"];
    private static readonly string[] EraReadings = ["れいわ", "へいせい", "しょうわ", "たいしょう", "めいじ"];

    /// <summary>計算の言葉 (かける・わる …) の文節の読み。前後に数字が付いていてもよい (変換エンジンが 3かける を 1 つの文節にする)。</summary>
    [GeneratedRegex("^[0-9０-９]*(?:かける|わる|たす|ひく|ぷらす|まいなす|いこーる)[0-9０-９]*$")]
    private static partial Regex MathOperator();

    /// <summary>確定したこの文節を学習するか。ユーティリティ変換の候補・日時や版から作る語・計算の言葉の記号は学習しない。</summary>
    public static bool Learnable(Clause clause) =>
        !clause.NotLearned.Contains(clause.Text) && !IsStale(clause.Reading, clause.Text) && !IsOperatorChoice(clause);

    /// <summary>
    /// 変換エンジンが今の日時・版から作る語か (きょう → 2026/11/10・火曜日、ことし → 令和八年、ばーじょん → Mozc-2.31)。学習しない。
    /// 読みに無い数字・元号を含むか、読みに無い「曜日」で終わる語。いちばん → 1番 も当てはまるが、Meltype の学習から外すだけで、Mozc は自分で学習する。
    /// </summary>
    // TODO: 推測をやめ、変換エンジンが付ける「学習しない」の印で決める。Mozc は日時・版の候補に NO_LEARNING を付けているので、
    // meltype_mozc_helper が候補と一緒に返す。
    // そうすれば いちばん → 1番 を巻き添えにしない。
    public static bool IsVolatile(string reading, string text) =>
        !reading.Any(IsDigit) && text.Any(IsDigit) ||
        EraNames.Any(text.Contains) && !EraReadings.Any(reading.Contains) ||
        text.EndsWith("曜日", StringComparison.Ordinal) && !reading.Contains("ようび", StringComparison.Ordinal);

    private static bool IsDigit(char c) => char.IsAsciiDigit(c) || c is >= '０' and <= '９';

    /// <summary>
    /// 覚えない語 (学習データに入っていても、読み込むときに捨てる)。日時・版から作る語、前の版が学習から外し漏れていた
    /// ユーティリティ変換の候補 (時候の挨拶・クリップボードの文字)、アプリの画面の文字が混ざった語 (U+FFFC を含む。
    /// Discord の画面の文字が文脈に入り、Zenzai がそれをそのまま返した: issue #1537)。
    /// </summary>
    public static bool IsStale(string reading, string text) =>
        IsVolatile(reading, text) ||
        text.Contains('\uFFFC', StringComparison.Ordinal) ||
        reading is "じこう" or "じこうのあいさつ" && text.Contains("の候", StringComparison.Ordinal) ||
        reading is "くりっぷぼーど" or "ぺーすと" && text != CompositionText.ToKatakana(reading);

    /// <summary>計算の言葉の文節で、変換エンジンの記号の候補 (3かける4 → 3×4) を選んだか。学習しない (覚えると 電話をかける まで × になる)。</summary>
    private static bool IsOperatorChoice(Clause clause) =>
        MathOperator().IsMatch(clause.Reading) && clause.Text.IndexOfAny(['×', '÷', '+', '＋', '-', '－', '=', '＝']) >= 0;

    /// <summary>
    /// 句読点だけの文節か。学習しない (変換エンジンにも覚えさせない)。読みの 、。 は句読点の設定によらず同じなので、
    /// ここで選んだ候補 (、 → ・) を覚えると、ほかの句読点の設定や、句読点を含む文節 (わたしは、 → 私は・) にも効く。
    /// </summary>
    private static bool IsPunctuationOnly(Clause clause) => clause.Reading.All(c => c is '、' or '。');

    /// <summary>変換履歴の語。</summary>
    public string? LearnedConversion(string reading) => services.History?.Get(reading);

    /// <summary>確定した文節を学習する (次に同じ読みを変換したとき最初の候補にする)。context は変換エンジンに渡す文脈。</summary>
    public void Learn(IReadOnlyList<Clause> clauses, string? context)
    {
        // 変換の候補から打ったままの英字 (api) を選んで確定したら、その語は次から英字にする (F10 と同じ)。
        foreach (var clause in clauses.Where(c => !c.IsEnglish && c.Changed && c.Raw is { } raw && c.Text == raw && Learnable(c)))
        {
            services.Languages?.Remember(clause.Raw!, english: true);
        }
        // 英語と判定した語で日本語の候補 (go → 語) を選んだら、その語は次から日本語にする (F6 と同じ)。
        foreach (var clause in clauses.Where(c => c.IsEnglish && CompositionController.IsJapaneseText(c.Text) && c.Reading.All(char.IsAsciiLetter) && Learnable(c)))
        {
            services.Languages?.Remember(clause.Reading, english: false);
        }
        // 英訳を選んだら、英訳の記録に (普通の変換の学習より弱く効く)。
        foreach (var clause in clauses.Where(c => c.Translations.Contains(c.Text)))
        {
            services.TranslationHistory?.Remember(clause.Reading, clause.Text);
        }
        LearnConversion(clauses, context);
        if (services.History is not { } history) return;
        // 1 文字の読み (き → 記) を覚えると、関係ない変換 (き + ごうとう) まで巻き込むので 2 文字以上だけ。
        // ただし数字・記号の 1 文字 (1 → ①、( → （) は、かなの途中に巻き込まれないので覚える (issue #672)。
        foreach (var clause in clauses.Where(c => !c.IsEnglish && c.Changed && (c.Reading.Length >= 2 || c.Reading.Length == 1 && c.Reading[0] is < 'ぁ' or > 'ヿ' && !char.IsAsciiLetter(c.Reading[0]))))
        {
            // かな・カタカナのまま確定したのは、その場限りのことが多いので覚えない。
            // 打ったままの英字を選んだのは、上で英語として覚えた。
            if (clause.Text == clause.Reading || clause.Text == CompositionText.ToKatakana(clause.Reading) || clause.Text == clause.Raw) continue;
            // 英訳は上で英訳の記録に入れた (ここで覚えると次から 1 番目に出てしまう)。
            if (clause.Translations.Contains(clause.Text)) continue;
            if (!Learnable(clause) || IsPunctuationOnly(clause)) continue;
            history.Remember(clause.Reading, clause.Text);
        }
    }

    /// <summary>
    /// 変換エンジン (Mozc) にも、確定した文節を覚えさせる。英語の文節・打ったままの英字・ユーティリティ変換の候補で区切った、日本語の文節のまとまりごとに。
    /// 日時・版から作る語 (<see cref="IsVolatile"/>) は Mozc が自分で学習しないので、そのまま渡す。変換エンジンとのやり取りで入力を待たせないよう、裏で行う。
    /// </summary>
    private void LearnConversion(IReadOnlyList<Clause> clauses, string? context)
    {
        if (converter is not ILearningConverter learner) return;
        var run = new List<ConversionClause>();
        void Flush()
        {
            if (run.Count == 0) return;
            var learned = run.ToList();
            run.Clear();
            ThreadPool.QueueUserWorkItem(_ => learner.Learn(context, learned));
        }
        foreach (var clause in clauses)
        {
            if (clause.IsEnglish || clause.Text == clause.Raw || clause.Translations.Contains(clause.Text) || clause.Reading.Any(char.IsAsciiLetterOrDigit) || clause.NotLearned.Contains(clause.Text) || IsOperatorChoice(clause) || IsPunctuationOnly(clause))
            {
                Flush();
                continue;
            }
            run.Add(new ConversionClause(clause.Reading, clause.Text));
        }
        Flush();
    }

    /// <summary>
    /// ユーザーが自分で英字 / かなに直して確定した語を覚える (api と打って F10 で英字にした → 次から api は英字)。
    /// 自動の判定と違う方を選んだときだけ。1 語の英字だけを対象にする。
    /// </summary>
    public void LearnLanguage(CompositionText text)
    {
        if (services.Languages is not { } memory) return;
        var raw = text.Raw;
        if (raw.Length < 2 || !raw.All(char.IsAsciiLetter)) return;
        var automatic = text.Segments();
        switch (text.Mode)
        {
            case DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric when !automatic.All(s => s.IsEnglish):
                memory.Remember(raw, english: true, explicitChoice: true);
                break;
            case DisplayMode.Hiragana or DisplayMode.Katakana or DisplayMode.HalfWidthKatakana when automatic.Any(s => s.IsEnglish):
                memory.Remember(raw, english: false, explicitChoice: true);
                break;
            case DisplayMode.Auto when text.SuggestionAccepted && automatic.All(s => s.IsEnglish):
                // 自動判定を使わないときに、Tab で提案どおり英字にした。
                memory.Remember(raw, english: true, explicitChoice: true);
                break;
        }
    }

    /// <summary>漢字を含む日本語の語句を確定したら、予測変換のために読みと一緒に覚える (英字だけ・かなのままは覚えない)。</summary>
    public void RememberPhrase(string reading, string text)
    {
        if (services.Predictor?.Phrases is not { } phrases) return;
        if (!text.Any(c => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿') || reading.Length < 3 || reading.Any(char.IsAsciiLetter)) return;
        if (IsStale(reading, text)) return;
        phrases.Remember(reading, text);
    }
}
