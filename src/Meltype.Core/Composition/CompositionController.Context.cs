// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>入力欄の文脈: キャレットの前後の文字 (英語か日本語か)、学習の文脈。</summary>
public sealed partial class CompositionController
{
    /// <summary>設定「英語 / 日本語の学習を前の文脈ごとに分ける」: 学習の文脈 (前が英語か日本語か) を今の入力に合わせる。</summary>
    private void UpdateLearningContext()
    {
        if (_services.Languages is not { } memory) return;
        memory.ContextSensitive = _settings().ContextualLearning;
        memory.Context = _text.PrecedingEnglish;
    }

    private void BeginComposition(bool privateInput = false)
    {
        _privateComposition = Private || privateInput;
        _keptChoices.Clear();
        _modeByKey = false;
        _escReverted = false;
        // 設定「英字を主に入力」: 判定せず、打った英字のまま見せる (issue #839)
        if (_settings().EnglishFirstInput && !KanaInput && _text.Mode == DisplayMode.Auto) _text.Mode = DisplayMode.HalfWidthAlphanumeric;
        _text.KanaInput = KanaInput;
        _text.FullWidthParentheses = _settings().FullWidthParentheses;
        _text.SquareBrackets = _settings().SquareBrackets;
        _text.LaughterAfterKana = _settings().LaughterAfterKana;
        // 自分が確定した直後は、アプリ側のテキストがまだ更新されていないかもしれないので自分の記録を信じる。
        var recentOwnCommit = Environment.TickCount64 - _lastCommitTime < OwnCommitTrustMs;
        _precedingText = _lastCommitEnglish is null ? null : _lastCommitText;
        _followingText = null;
        _text.PrecedingEnglish = _lastCommitEnglish;
        UpdateLearningContext();
        _text.PrecedingEnglishSentence = _lastCommitEnglish == true && IsEnglishSentence(_lastCommitText);
        _text.PrecedingEnglishName = _lastCommitEnglish == true && IsEnglishNameContext(_lastCommitText);
        // フロントが渡したキャレットの前の文字 (このキーで確定した文字を入れたもの)。読めなければ null で、キャレットの位置は確かめない (今までどおり直す)。
        // 確定し直してよいか (直前に確定した語が今のキャレットの位置にあるか) の判断にも使う。
        var before = _out.Before;
        _caretBefore = before;
        if (!recentOwnCommit && before is not null)
        {
            _precedingText = before;
            if (TextLanguage.Of(before) is { } english) _text.PrecedingEnglish = english;
            UpdateLearningContext();
            _text.PrecedingEnglishSentence = IsEnglishSentence(before);
            _text.PrecedingEnglishName = IsEnglishNameContext(before);
        }
        _followingText = _out.After;
        _text.FollowingEnglish = TextLanguage.OfStart(_out.After);
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
