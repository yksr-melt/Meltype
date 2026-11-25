// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using static Meltype.Libraries.Chars.TextChars;

namespace Meltype.Composition;

/// <summary>キャレットの前後と直前の確定から、判定・変換・学習の文脈を作る。</summary>
internal static class CompositionContext
{
    /// <summary>自分が確定してから、この時間内はアプリ側のテキストがまだ更新されていないかもしれないので自分の記録を優先する。</summary>
    private const long OwnCommitTrustMs = 1500;

    /// <summary>
    /// 入力欄の文脈 (<see cref="TextEnv.Context"/>)。フロントが渡したキャレットの前後の文字 (この出来事で確定した文字を入れたもの) から決める。
    /// 自分が確定した直後と、キャレットの前が読めないときは、自分の確定の記録 (最後に確定した語) を使う。
    /// </summary>
    internal static TextContext ContextOf(CompositionState state, CompositionEnv env)
    {
        var after = CompositionOutput.After(env);
        // 自分が確定した直後は、アプリ側のテキストがまだ更新されていないかもしれないので自分の記録を信じる。
        var recentOwnCommit = env.NowMs - state.LastCommitTime < OwnCommitTrustMs;
        if (!recentOwnCommit && CompositionOutput.Before(state.Out, env) is { } before)
            return new TextContext(before, LanguageOf(before) ?? state.Last?.English, IsEnglishSentence(before), IsEnglishNameContext(before), after, LanguageOfStart(after));
        var last = state.Last;
        return new TextContext(last?.Text, last?.English,
            last is { English: true } && IsEnglishSentence(last.Text), last is { English: true } && IsEnglishNameContext(last.Text), after, LanguageOfStart(after));
    }

    /// <summary>
    /// 英文の途中か: 最後の行の日本語の文字より後ろが、空白で区切った英単語 2 語以上で、空白で終わる ("I want ", "Thanks, see ")。
    /// 日本語の文の中の英単語 ("今日は GitHub ") は 1 語なので当たらない。
    /// </summary>
    /// <summary>1 語でも英文の始まりとみなす、行の始めのあいさつ・感動詞。</summary>
    private static readonly HashSet<string> SentenceOpeners = ["hey", "hi", "hello", "oh", "wow", "yeah", "yes", "well", "so", "hmm", "ah", "ooh", "oops", "thanks", "sorry", "please", "dear", "yay", "whoa", "nope", "yep"];

    /// <summary>日本語の後ろでも英文の始まりとみなす感動詞 (日本語のローマ字としては使わない綴りのもの)。</summary>
    private static readonly HashSet<string> Interjections = ["oh", "wow", "yeah", "ooh", "oops", "whoa", "woah", "yay", "hmm", "hey"];

    internal static bool IsEnglishSentence(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[^1] != ' ') return false;
        var start = text.Length;
        while (start > 0 && text[start - 1] < 0x80 && text[start - 1] is not ('\n' or '\r')) start--;
        var words = text[start..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // 行の始めのあいさつ・感動詞 (hey・hi・oh …) なら 1 語でも英文の始まり ("hey " の後の yo)。
        // ほかの 1 語 (GitHub no repo の GitHub) は、日本語の文の中の英単語のことが多いので 2 語以上
        var lineStart = start == 0 || text[start - 1] is '\n' or '\r';
        if (lineStart && words is [var first] && SentenceOpeners.Contains(first.TrimEnd(',', '!', '.').ToLowerInvariant())) return true;
        // 感動詞 (oh・wow・yeah …) は日本語の後ろでも英文の始まり (爆弾にはなれない oh + no! の no を の にしない)
        if (words is [var interjection] && Interjections.Contains(interjection.TrimEnd(',', '!', '.').ToLowerInvariant())) return true;
        // 行の始めの、' で縮めた英語 (I'll・We're・don't) も 1 語で英文の始まり ("I'll " の後の go)。ローマ字には ' が入らない
        if (lineStart && words is [var contraction] && System.Text.RegularExpressions.Regex.IsMatch(contraction, @"^[A-Za-z]+['’][A-Za-z]{1,2}$")) return true;
        return words.Length >= 2 && words.All(w => w.Any(char.IsAsciiLetter) && w.All(c => char.IsAsciiLetterOrDigit(c) || c is ',' or '.' or '\'' or '-' or '!' or '?' or ':' or ';'));
    }

    internal static bool IsEnglishNameContext(string? text)
    {
        if (!IsEnglishSentence(text)) return false;
        var words = text!.TrimEnd().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var last = words[^1].ToLowerInvariant();
        return last is "called" or "named" || words.Length >= 2 && last == "is" &&
            words[^2].ToLowerInvariant() is "name" or "surname" or "nickname";
    }
}
