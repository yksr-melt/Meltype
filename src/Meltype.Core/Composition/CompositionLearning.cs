// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.RegularExpressions;
using Meltype.Detection;
using Meltype.Engine;
using Meltype.Learning;
using Meltype.Libraries.Chars;

namespace Meltype.Composition;

/// <summary>
/// 確定した入力の文節 1 つ (<see cref="CompositionCommit.CommittedClauses"/>)。Engine は変換エンジンの答え (英語の文節など、変換エンジンに頼まなかった文節は null)。
/// Text は確定した文字のうちこの文節の分 (F6〜F8 の形を当てた入力で、文節の読みをつなげたものが入力のかなと合わなければ null)。
/// </summary>
internal sealed record CommittedClause(string Reading, string? Engine, string? Text);

/// <summary>
/// 確定した入力を学習する (変換履歴・言語の学習・英訳の記録・予測変換の語句)。
/// 設定「ユーザー学習を使う」と入力欄が学習を求めないかは、呼ぶ側 (<see cref="CompositionCommit"/>) が見る。
/// 学習しない候補は <see cref="Learnable"/> の 1 か所で決める。
/// </summary>
internal sealed partial class CompositionLearning(CompositionServices services, ProperNouns? properNouns = null)
{
    /// <summary>計算の言葉 (かける・わる …) の文節の読み。前後に数字が付いていてもよい (変換エンジンが 3かける を 1 つの文節にする)。</summary>
    [GeneratedRegex("^[0-9０-９]*(?:かける|わる|たす|ひく|ぷらす|まいなす|いこーる)[0-9０-９]*$")]
    private static partial Regex MathOperator();

    /// <summary>確定したこの文節を学習するか。ユーティリティ変換の候補・日時や版から作る語・計算の言葉の記号は学習しない。</summary>
    public static bool Learnable(Clause clause) =>
        !clause.NotLearned.Contains(clause.Text) && !StaleWords.IsStale(clause.Reading, clause.Text) && !IsOperatorChoice(clause);

    /// <summary>計算の言葉の文節で、変換エンジンの記号の候補 (3かける4 → 3×4) を選んだか。学習しない (覚えると 電話をかける まで × になる)。</summary>
    private static bool IsOperatorChoice(Clause clause) =>
        MathOperator().IsMatch(clause.Reading) && clause.Text.IndexOfAny(['×', '÷', '+', '＋', '-', '－', '=', '＝']) >= 0;

    /// <summary>変換履歴の語。</summary>
    public string? LearnedConversion(string reading) => services.History?.Get(reading);

    /// <summary>確定した文節を学習する (次に同じ読みを変換したとき最初の候補にする)。context は英語 / 日本語の学習の文脈。</summary>
    public void Learn(IReadOnlyList<Clause> clauses, bool? context)
    {
        // 変換の候補から打ったままの英字 (api) を選んで確定したら、その語は次から英字にする (F10 と同じ)。
        foreach (var clause in clauses.Where(c => !c.IsEnglish && c.Changed && c.Raw is { } raw && c.Text == raw && Learnable(c)))
        {
            services.Languages?.Remember(clause.Raw!, english: true, context: context);
        }
        // 英語と判定した語で日本語の候補 (go → 語) を選んだら、その語は次から日本語にする (F6 と同じ)。
        foreach (var clause in clauses.Where(c => c.IsEnglish && TextChars.IsJapaneseText(c.Text) && c.Reading.All(char.IsAsciiLetter) && Learnable(c)))
        {
            services.Languages?.Remember(clause.Reading, english: false, context: context);
        }
        // 自動で大文字にする固有名詞 (iPhone) で打ったままの小文字 (iphone) を選んだら、次から小文字のままにする。大文字の形を選び直したら忘れる。
        if (properNouns is not null)
        {
            foreach (var clause in clauses.Where(c => c.Changed))
            {
                if (properNouns.AutoCanonical(clause.Reading) is not { } auto) continue;
                if (clause.Text == clause.Reading) services.History?.Remember(clause.Reading, clause.Text);
                else if (clause.Text == auto) services.History?.Forget(clause.Reading);
            }
        }
        // 英訳を選んだら、英訳の記録に (普通の変換の学習より弱く効く)。
        foreach (var clause in clauses.Where(c => c.Translations.Contains(c.Text)))
        {
            services.TranslationHistory?.Remember(clause.Reading, clause.Text);
        }
        foreach (var clause in clauses)
        {
            // 1 つの文字を複数の書式が出すときは、どの書式を選んだか区別できないので、すべての書式を覚える
            if (!clause.UtilityRankingKeys.TryGetValue(clause.Text, out var keys)) continue;
            foreach (var format in keys) services.UtilityFormats?.Remember(format.Id, format.Format);
        }
        if (services.History is not { } history) return;
        foreach (var clause in clauses.Where(c => !c.IsEnglish && c.Changed && ConversionHistory.IsLearnableReading(c.Reading)))
        {
            // 打ったままの英字を選んだのは、上で英語として覚えた。
            if (clause.Text == clause.Raw) continue;
            // 英訳は上で英訳の記録に入れた (ここで覚えると次から 1 番目に出てしまう)。
            if (clause.Translations.Contains(clause.Text)) continue;
            if (!Learnable(clause)) continue;
            history.Remember(clause.Reading, clause.Text);
        }
    }

    /// <summary>
    /// ユーザーが自分で英字 / かなに直して確定した語を覚える (api と打って F10 で英字にした → 次から api は英字)。
    /// 自動の判定と違う方を選んだときだけ。1 語の英字だけを対象にする。automatic は自動の判定の区間。
    /// </summary>
    public void LearnLanguage(TextReading text, IReadOnlyList<CompositionSegment> automatic, bool? context)
    {
        var raw = CompositionText.Raw(text);
        if (raw.Length < 2 || !raw.All(char.IsAsciiLetter)) return;
        if (text.Mode == DisplayMode.HalfWidthAlphanumeric && text.Case == LetterCase.AsTyped && properNouns?.AutoCanonical(raw) is not null)
            services.History?.Remember(raw, raw);
        if (services.Languages is not { } memory) return;
        switch (text.Mode)
        {
            case DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric when !automatic.All(s => s.IsEnglish):
                memory.Remember(raw, english: true, explicitChoice: true, context: context);
                break;
            case DisplayMode.Hiragana or DisplayMode.Katakana or DisplayMode.HalfWidthKatakana when automatic.Any(s => s.IsEnglish):
                memory.Remember(raw, english: false, explicitChoice: true, context: context);
                break;
            case DisplayMode.Auto when text.SuggestionAccepted && automatic.All(s => s.IsEnglish):
                // 自動判定を使わないときに、Tab で提案どおり英字にした。
                memory.Remember(raw, english: true, explicitChoice: true, context: context);
                break;
        }
    }

    /// <summary>漢字を含む日本語の語句を確定したら、予測変換のために読みと一緒に覚える (英字だけ・かなのままは覚えない)。</summary>
    public void RememberPhrase(string reading, string text)
    {
        if (services.Predictor?.Phrases is not { } phrases) return;
        if (!text.Any(c => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿') || reading.Length < 3 || reading.Any(char.IsAsciiLetter)) return;
        if (StaleWords.IsStale(reading, text)) return;
        phrases.Remember(reading, text);
    }
}
