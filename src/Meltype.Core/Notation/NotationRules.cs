// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Config;

namespace Meltype.Notation;

/// <summary>
/// 見せる表記の規則の列。Composition が呼ぶのはここだけで、規則を足しても Composition は変えない (<see cref="SpanKind.Typed"/> の区間を作る所を除く)。
/// 規則は辞書・学習を関数で受け取り、入力欄の状態を知らない。
/// </summary>
internal static class NotationRules
{
    /// <summary>打った区間の並び (文字は打ったまま) の、英語の区間の表記を決める。区間の数と順は変えない。</summary>
    /// <param name="properNoun">打った語を固有名詞の表記に直した形。直さない語なら null。</param>
    public static IReadOnlyList<TextSpan> Typed(IReadOnlyList<TextSpan> spans, Func<string, string?> properNoun) => ProperNounCase.Apply(spans, properNoun);

    /// <summary>ほかの区間とつながらない 1 語 (予測の語など) の表記。</summary>
    public static string Word(string word, Func<string, string?> properNoun) => Typed([new TextSpan(SpanKind.Typed, word)], properNoun)[0].Text;

    /// <summary>表示・確定する文字。typed は打った文字 (かな入力では空)。</summary>
    public static ComposedText Show(ComposedText text, string typed) => HalfWidthStars.Apply(text, typed);

    /// <summary>確定する文字に、入力欄のキャレットの前後の文字とのつなぎを入れる。</summary>
    public static ComposedText Commit(ComposedText text, string? before, string? after, Settings settings) =>
        EnglishSpacing.Apply(text, before, after, settings.SpaceAroundEnglish, settings.SpaceAroundNumbers);

    /// <summary>text に、表記の規則が日本語とみなす文字 (英単語の前後の空白を入れる相手) があるか。</summary>
    public static bool HasJapanese(string text) => text.Any(EnglishSpacing.IsJapanese);

    /// <summary>入力欄・変換ボックスに書く文字列。</summary>
    public static string Write(ComposedText text, PunctuationStyle punctuation) => Punctuation.Write(text, punctuation);

    /// <summary>入力欄から読んだ文字を、読み・変換・学習で使う文字にする。</summary>
    public static string Read(string text, PunctuationStyle punctuation) => Punctuation.Read(text, punctuation);
}
