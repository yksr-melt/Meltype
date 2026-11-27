// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using static Meltype.Libraries.Chars.TextChars;

using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>キャレットの前後と直前の確定から、判定・変換・学習の文脈を作る。</summary>
internal static class CompositionContext
{
    /// <summary>自分の確定の記録に残す文字数。文脈を使う側で最も長い Zenzai の文脈 (EngineContext.Passage の 100 文字) が入る長さ。</summary>
    private const int OwnTextLength = 100;

    /// <summary>
    /// 入力欄の文脈 (<see cref="TextEnv.Context"/>)。キャレットの前後の文字だけから決める。
    /// キャレットの前の文字は、フロントが渡した文字 (この出来事で確定した文字を入れたもの)。読めないときと、最後に確定したときのまま変わっていないときは、確定の記録を使う:
    /// Meltype キーボード・fcitx5・IBus は、確定した文字がアプリに入る前の文字を渡すことがある (Meltype キーボードは、変換中は変換を始めたときに読んだ文字を渡し続ける)。
    /// </summary>
    internal static TextContext ContextOf(CompositionState state, CompositionEnv env)
    {
        var before = state.Own is { } own && (env.Before is null || env.Before == own.Shown) ? own.Text : CompositionOutput.Before(state.Out, env);
        var after = CompositionOutput.After(env);
        return new TextContext(before, LanguageBefore(before), IsEnglishSentence(before), IsEnglishNameContext(before), after, LanguageOfStart(after));
    }

    /// <summary>
    /// キャレットの前の言語。日本語の文の中の英単語・数字のすぐ後ろは、日本語の続き (今日はGitHub + でも) とも英語 (今日はGitHub + issue) とも決めない。
    /// 英単語の後ろに空白があれば、Space で英単語を確定して英語を続けるところ (今日はsample + issue)。
    /// </summary>
    private static bool? LanguageBefore(string? before)
    {
        if (before is [.., > ' ' and < '\x7F'] && IsJapaneseText(before[(before.LastIndexOfAny(['\r', '\n']) + 1)..])) return null;
        return LanguageOf(before);
    }

    /// <summary>確定する text を記録する。text を入れる前に、キャレットの前を deleteBefore 文字だけ消す。</summary>
    internal static void NoteCommit(CompositionState state, CompositionEnv env, int deleteBefore, ComposedText text)
    {
        var before = env.Context().PrecedingText ?? "";
        var committed = before[..^Math.Min(deleteBefore, before.Length)] + NotationRules.Read(NotationRules.Write(text, env.Settings.Punctuation), env.Settings.Punctuation);
        state.Own = new OwnCommit(env.Before, committed.Length > OwnTextLength ? committed[^OwnTextLength..] : committed);
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
