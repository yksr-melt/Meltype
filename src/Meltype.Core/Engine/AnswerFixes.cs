// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 Yukishiro

using Meltype.Libraries.Chars;

namespace Meltype.Engine;

/// <summary>
/// 変換エンジンの答えの直し。変換エンジンの文節の並びと文脈を受け取り、文節ごとの候補 (先頭が最初に見せる文字) を返す。
/// 辞書・学習・変換エンジンへの依頼は、<see cref="AnswerContext"/> の関数で受け取る。
/// </summary>
internal static class AnswerFixes
{
    /// <summary>
    /// 日本語のかな kana を変換エンジンが区切った文節 parts を直す。各文節の最初の候補は次の順で決める:
    ///   1. 文脈の手がかり辞書 (前後に 気温 があれば あつい → 暑い)
    ///   2. ユーザーが前に選び直した変換 (学習)
    ///   3. 変換エンジンの結果 (入力欄のキャレットの前の文字を文脈として渡している)
    /// </summary>
    /// <param name="before">kana の前の文字 (変換エンジンに渡した文脈)。小書きのかなをつなげて変換し直すときに渡す。</param>
    public static IReadOnlyList<FixedAnswer> Fix(IReadOnlyList<ConversionClause> parts, string kana, string before, AnswerContext context)
    {
        // 読み全体が補助辞書・絵文字の辞書の語 (かんがえるかお → 🤔) なら、文節に分けずに 1 つの文節にして候補を出す。
        if (parts.Count > 1 && context.IsListedReading(kana))
        {
            // 注記は候補全体の出典を示すので、すべての文節が Zenzai の答えのときに引き継ぐ。
            parts = [new ConversionClause(kana, string.Concat(parts.Select(p => p.Text)), Zenzai: parts.All(p => p.Zenzai))];
        }
        parts = JoinSmallKana(parts, before, context);
        var clauses = parts.Select(p =>
        {
            var text = TextChars.NormalizeHalfWidth(p.Text, p.Reading);
            return new FixedAnswer(p.Reading, text, [text], p.Zenzai ? text : null);
        }).ToList();
        for (var i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            var others = string.Concat(clauses.Where((_, k) => k != i).Select(c => c.Text));
            var preferred = context.ContextRule(clause.Reading, context.Surrounding + others + context.Following, clause.Text) ?? context.Learned(clause.Reading);
            // 変換エンジンは、文節が「から」だけだと記号 (～) にしてしまう。記号だけの変換結果は、ひらがなの後ろに回す。
            // ユーザー辞書・同梱の語句の記号 (なかてん → ・) はそのまま
            preferred ??= IsSymbolOnly(clause.Text) && clause.Reading.All(KanaChars.IsHiragana) && !context.UserWords(clause.Reading).Contains(clause.Text) ? clause.Reading : null;
            // 英単語に挟まれて助詞だけの文節になると、変換エンジンは漢字にしてしまう (github + に + push → 二)。助詞はかなのまま。
            preferred ??= JapaneseParticles.IsParticle(clause.Reading) && clause.Text != clause.Reading ? clause.Reading : null;
            // カタカナの語の後ろの「っ」で始まる文節 (スパイダーマ + っ！) は、カタカナの「ッ」にする (スパイダーマッ！)。
            preferred ??= i > 0 && clause.Text.StartsWith('っ') && clauses[i - 1].Text is [.., var last] && last is >= 'ァ' and <= 'ヺ' or 'ー'
                ? "ッ" + clause.Text[1..] : null;
            // 文頭・記号の後ろの「え、」「え？」(聞き返し) を、変換エンジンは 得 にしてしまう (得、知らん)。かなのまま
            preferred ??= IsInterjection(context, clauses, i) ? clause.Reading : null;
            // がち (ガチで) を、変換エンジンは 勝ち にしてしまう (勝ちでやばい)。勝ち の読みは かち なので、がち は ガチ にする
            preferred ??= clause.Reading.StartsWith("がち", StringComparison.Ordinal) && clause.Text.StartsWith("勝ち", StringComparison.Ordinal)
                ? "ガチ" + clause.Text["勝ち".Length..] : null;
            if (preferred is not null) clauses[i] = Prefer(clause, preferred);
        }
        IReadOnlyList<FixedAnswer> answers = clauses;
        if (context.TypedLetterOrDigit) answers = KeepTypedSymbols(answers, kana);
        // ワークアラウンド: 英単語 + 付属語 (python|なら、push|した): 変換エンジンは英語の後ろの読みだけを見て、文の頭の語 (奈良・下) にしてしまう。かなのまま。
        // 変換エンジンに文脈 (英単語・これ) を渡しても、名詞の読みを前に付けて変換しても、なら・しか・しよう は直らなかった。
        // 変換エンジンが英単語を前の文脈として受け取り、後ろの読みを付属語として変換するようになったら消せる
        if (context.AfterEnglish && answers.Count > 0 && IsParticleAfterEnglish(context, answers[0])) answers = [Prefer(answers[0], answers[0].Reading), .. answers.Skip(1)];
        return answers;
    }

    /// <summary>
    /// 打った記号を、文節ごとの最初の候補に戻す (<see cref="KeptSymbols"/>)。戻す前の最初の候補が Zenzai の答えなら、Zenzai の答えも戻した文字にする。
    /// 打ったものに英数字があるときだけ呼ぶ (<see cref="KeptSymbols"/> の説明)。
    /// </summary>
    public static IReadOnlyList<FixedAnswer> KeepTypedSymbols(IReadOnlyList<FixedAnswer> answers, string reading)
    {
        if (KeptSymbols([.. answers.Select(a => a.Text)], reading) is not { } kept) return answers;
        return [.. answers.Select((a, i) => kept[i] == a.Text ? a : Prefer(a, kept[i]) with { Zenzai = a.Zenzai == a.Text ? kept[i] : a.Zenzai })];
    }

    private static FixedAnswer Prefer(FixedAnswer answer, string text)
    {
        var candidates = answer.Candidates.ToList();
        candidates.Remove(text);
        candidates.Insert(0, text);
        return answer with { Candidates = candidates };
    }

    /// <summary>
    /// かなと一緒に打った記号 (読みの 、。？「 など。CompositionText.Symbol と TypedMarks が決めた文字) は、最初の候補でも読みの文字のままにする。
    /// 変換エンジンの答え・Mozc の学習・変換履歴は、記号を別の文字にすることがある
    /// (ライブ変換で 例文？ が 例文? に: issue #519、「今日は が ［今日は に: issue #1669、わたしは、 が 私は・ に)。別の候補は一覧に残す。
    /// 記号だけを打って変換したとき (、 だけ・( だけ) は、選んで覚えた候補を使う (issue #672) ので、呼ぶ側が打ったものに英数字があるときだけ呼ぶ。
    /// 記号の数が読みと違う答え (から → ～、あんはさうぇい → アン・ハサウェイ) は、どの記号がどれに当たるか分からないので変えない (null)。
    /// 打った記号を文字の種類と数で見分けているのは、変換の結果が区間の種類を持たない文字列だから。区間の種類を持ち回すようになったら (#1735)、区間で決める。
    /// </summary>
    /// <returns>文節ごとの、打った記号に戻した文字。変えないなら null。</returns>
    public static IReadOnlyList<string>? KeptSymbols(IReadOnlyList<string> texts, string reading)
    {
        var typed = reading.Where(IsSymbol).ToList();
        if (texts.Sum(t => t.Count(IsSymbol)) != typed.Count) return null;
        var next = 0;
        return [.. texts.Select(text => string.Concat(text.Select(c => IsSymbol(c) ? typed[next++] : c)))];

        // 絵文字 (サロゲートペア) は打った記号に当たらないので数えない (置き換えると壊れた文字になる)
        static bool IsSymbol(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && !char.IsSurrogate(c);
    }

    /// <summary>
    /// 小書きのかな (ぃ ぇ ゃ …) で始まる文節は前の文節とつなげる。小書きのかなから始まる語は無いので、
    /// 変換エンジンの知らない語を区切り間違えたもの (こうぃ → 光|ぃ)。つなげた読みは、その文節だけで変換し直す
    /// (こうぃ → コウィ。wi で打っていれば こゐ も候補に出る)。
    /// </summary>
    private static IReadOnlyList<ConversionClause> JoinSmallKana(IReadOnlyList<ConversionClause> parts, string before, AnswerContext context)
    {
        if (!parts.Skip(1).Any(p => p.Reading.Length > 0 && KanaChars.IsSmallKana(p.Reading[0]))) return parts;
        var joined = new List<ConversionClause>();
        foreach (var part in parts)
        {
            if (joined.Count > 0 && part.Reading.Length > 0 && KanaChars.IsSmallKana(part.Reading[0]))
            {
                var reading = joined[^1].Reading + part.Reading;
                // 変換しても漢字の後ろに小書きのかなが残る (光ぃ) なら、カタカナ (コウィ) にする。
                var text = context.Convert(reading, before + string.Concat(joined.Take(joined.Count - 1).Select(c => c.Text)));
                if (Enumerable.Range(1, Math.Max(0, text.Length - 1)).Any(i => KanaChars.IsSmallKana(text[i]) && !KanaChars.IsKana(text[i - 1]))) text = KanaChars.ToKatakana(reading);
                joined[^1] = new ConversionClause(reading, text);
            }
            else joined.Add(part);
        }
        return joined;
    }

    /// <summary>
    /// 英単語の後ろの、名詞に付く付属語 (助詞・助動詞) か する の活用の文節を、変換エンジンが漢字で始めた
    /// (なら → 奈良、しか → 歯科、なの → 名の、した → 下、したい → 死体)。
    /// </summary>
    private static bool IsParticleAfterEnglish(AnswerContext context, FixedAnswer clause) =>
        (NounAttachedWords.Contains(clause.Reading) || context.StartsWithSuruForm(clause.Reading)) &&
        clause.Text.Length > 0 && !KanaChars.IsKana(clause.Text[0]) && clause.Text != clause.Reading;

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
    private static bool IsInterjection(AnswerContext context, List<FixedAnswer> clauses, int i)
    {
        var reading = clauses[i].Reading;
        if (reading.Length == 0 || reading[0] != 'え' || !reading.Skip(1).All(SentencePunctuation.Contains) || clauses[i].Text == reading) return false;
        var before = i > 0 ? clauses[i - 1].Text : context.PrecedingText;
        if (before.Length > 0 && !SentencePunctuation.Contains(before[^1])) return false;
        if (reading.Length > 1) return true;
        return i + 1 == clauses.Count || clauses[i + 1].Text is [var next, ..] && SentencePunctuation.Contains(next);
    }

    private static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);
}

/// <summary>直した文節。Candidates の先頭が最初に見せる文字、Engine は変換エンジンの答え、Zenzai は Zenzai の答え (Zenzai の答えでなければ null)。</summary>
internal sealed record FixedAnswer(string Reading, string Engine, IReadOnlyList<string> Candidates, string? Zenzai)
{
    public string Text => Candidates[0];
}

/// <summary>
/// <see cref="AnswerFixes.Fix"/> が読む文脈。Surrounding は文脈の手がかりに渡す前の文字 (キャレットの前と変換ボックスの英語)、Following はキャレットの後ろ、
/// PrecedingText はキャレットの前の文字。AfterEnglish は直す読みが英語の区間のすぐ後ろか、TypedLetterOrDigit は打ったものに英数字があるか。
/// </summary>
/// <param name="ContextRule">文脈の手がかり辞書 (読み, 前後の文字, 今の文字) → 選ぶ文字。無ければ null。</param>
/// <param name="Learned">前に選び直した変換 (読み → 文字)。</param>
/// <param name="UserWords">ユーザー辞書の、読みの語。</param>
/// <param name="IsListedReading">補助辞書・絵文字の辞書に読み全体がある語か。</param>
/// <param name="Convert">読み全体を、前の文字を文脈にして変換した文字。</param>
/// <param name="StartsWithSuruForm">読みが する の活用で始まるか。</param>
internal sealed record AnswerContext(
    string Surrounding, string Following, string PrecedingText, bool AfterEnglish, bool TypedLetterOrDigit,
    Func<string, string, string, string?> ContextRule, Func<string, string?> Learned, Func<string, IReadOnlyCollection<string>> UserWords,
    Func<string, bool> IsListedReading, Func<string, string, string> Convert, Func<string, bool> StartsWithSuruForm);
