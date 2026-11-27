// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Composition.Utility;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Learning;

namespace Meltype.Composition;

/// <summary>CompositionController が使う辞書・学習データと、設定でない外の入力。設定は <see cref="Config.Settings"/> を別に渡す。</summary>
internal sealed record CompositionServices
{
    /// <summary>同音異義語などの補助候補。</summary>
    public CandidateDictionary? Candidates { get; init; }

    /// <summary>前後の文字列の手がかりで候補を選ぶ規則 (気温 → 暑い)。</summary>
    public ContextRules? ContextRules { get; init; }

    /// <summary>ユーザーが選び直した変換の記録 (次から最初の候補にする)。</summary>
    public ConversionHistory? History { get; init; }

    /// <summary>
    /// 読みに対する変換エンジンの候補の一覧 (読み, 答えを待ってよい時刻 <see cref="Engine.ConversionRequest.Deadline"/>)。時間がかかることがあるので、
    /// 候補を切り替え始めたときだけ呼ぶ。null なら補助辞書の候補だけ。
    /// </summary>
    public Func<string, long, IReadOnlyList<string>>? MoreCandidates { get; init; }

    /// <summary>英訳の候補 (複雑な → complex)。null なら出さない。</summary>
    public TranslationDictionary? Translations { get; init; }

    /// <summary>今の時刻 (いま・なう → 17:22 の候補に使う。テストで差し替える)。</summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;

    /// <summary>候補の日本語の意味 (ウィクショナリー)。null なら英訳だけ。</summary>
    public MeaningDictionary? Meanings { get; init; }

    /// <summary>選んだ英訳の記録 (普通の変換の学習より弱く効かせる)。</summary>
    public TranslationHistory? TranslationHistory { get; init; }

    /// <summary>ユーザー辞書 (変換で最優先に使う)。</summary>
    public UserDictionary? UserDictionary { get; init; }

    /// <summary>よくある書き間違い (ブレスレッド → ブレスレット) の辞書。「もしかして」に使う。null なら出さない。</summary>
    public MisspellingDictionary? Misspellings { get; init; }

    /// <summary>ローマ字の打ち間違いを直すもの (onegaishimsu → お願いします)。null なら直さない。</summary>
    public RomajiTypoCorrector? RomajiTypos { get; init; }

    internal Func<GitHubIssueTitles?> GitHubTitles { get; init; } = () => null;

    /// <summary>ユーザーが英字 / かなに直した語の学習。</summary>
    public LanguageMemory? Languages { get; init; }

    /// <summary>入力欄ごとの入力モードの学習。null なら覚えない。</summary>
    public FieldModes? FieldModes { get; init; }

    /// <summary>予測変換の候補を出す元 (null なら出さない)。</summary>
    public Predictor? Predictor { get; init; }

    /// <summary>学習データを除いたもの (設定「ユーザー学習を使う」が OFF のとき)。学習データを足したら、ここでも除く。</summary>
    public CompositionServices WithoutLearning() => this with { History = null, TranslationHistory = null, Languages = null, FieldModes = null, Predictor = Predictor?.WithoutLearning() };
}
