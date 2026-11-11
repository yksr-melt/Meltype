// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>入力欄の文脈: キャレットの前後の文字 (英語か日本語か)、変換エンジンに渡す文脈、学習の文脈。</summary>
public sealed partial class CompositionController
{
    /// <summary>設定「英語 / 日本語の学習を前の文脈ごとに分ける」: 学習の文脈 (前が英語か日本語か) を今の入力に合わせる。</summary>
    private void UpdateLearningContext()
    {
        if (_services.Languages is not { } memory) return;
        memory.ContextSensitive = _settings().ContextualLearning;
        memory.Context = _text.PrecedingEnglish;
    }

    private void BeginComposition()
    {
        _keptChoices.Clear();
        _modeByKey = false;
        _escReverted = false;
        // 設定「英字を主に入力」: 判定せず、打った英字のまま見せる (issue #839)
        if (_settings().EnglishFirstInput && !KanaInput && _text.Mode == DisplayMode.Auto) _text.Mode = DisplayMode.HalfWidthAlphanumeric;
        _text.KanaInput = KanaInput;
        _text.Punctuation = _settings().Punctuation;
        _text.FullWidthParentheses = _settings().FullWidthParentheses;
        _text.SquareBrackets = _settings().SquareBrackets;
        _text.LaughterAfterKana = _settings().LaughterAfterKana;
        // 自分が確定した直後は、アプリ側のテキストがまだ更新されていないかもしれないので自分の記録を信じる。
        var recentOwnCommit = Environment.TickCount64 - _lastCommitTime < OwnCommitTrustMs;
        _precedingText = _lastCommitEnglish is null ? null : _lastCommitText;
        UpdateWideContext();
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
            UpdateWideContext();
            if (LanguageOf(before) is { } english) _text.PrecedingEnglish = english;
            UpdateLearningContext();
            _text.PrecedingEnglishSentence = IsEnglishSentence(before);
            _text.PrecedingEnglishName = IsEnglishNameContext(before);
        }
        _followingText = _out.After;
        _text.FollowingEnglish = LanguageOfStart(_out.After);
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

    /// <summary>確定済みの文字列の最後の (空白以外の) 文字が英数字なら英語、かな・漢字・全角記号なら日本語。</summary>
    internal static bool? LanguageOf(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (Classify(text[i]) is { } english) return english;
            if (text[i] >= 0x80 && !char.IsWhiteSpace(text[i]) && text[i] != '　') return null;
        }
        return null;
    }

    /// <summary>キャレットの後ろの文字列の最初の (空白以外の) 文字で判断する。</summary>
    internal static bool? LanguageOfStart(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var c in text)
        {
            if (c is '\r' or '\n') return null; // 次の行は別の文
            if (Classify(c) is { } english) return english;
            if (c >= 0x80 && !char.IsWhiteSpace(c) && c != '　') return null;
        }
        return null;
    }

    /// <summary>
    /// 英数字なら英語 (true)、かな・漢字・全角文字・日本語の文で使う記号 (○ ※ ★ 「」 など U+2000 以降) なら日本語 (false)、
    /// 空白や英文の記号なら判断しない (null)。
    /// </summary>
    private static bool? Classify(char c)
    {
        if (char.IsAsciiLetterOrDigit(c)) return true;
        if (c >= '\u2000' && !char.IsWhiteSpace(c) && c != '\u3000') return false;
        return null;
    }

    /// <summary>変換エンジンに渡す文脈: キャレットの前の確定済みの文字のうち、同じ文の日本語の部分 (最大 10 文字)。</summary>
    private string? ConversionContext()
    {
        var text = _precedingText;
        if (string.IsNullOrEmpty(text) || LanguageOf(text) != false) return null;
        var start = text.AsSpan().LastIndexOfAny(SentenceEndSearch);
        text = text[(start + 1)..].Trim();
        return text.Length == 0 ? null : text.Length > 10 ? text[^10..] : text;
    }

    /// <summary>広い文脈 (文をまたいで最大 100 文字の、日本語の文章) を、使える変換エンジン (Zenzai) に渡す。英文のときは渡さない。</summary>
    private void UpdateWideContext()
    {
        if (_converter is not IWideContextConverter wide) return;
        var text = _precedingText?.Replace("\r", "").Replace("\n", "").Trim();
        wide.WideContext = string.IsNullOrEmpty(text) || LanguageOf(text) != false ? null : text.Length > 100 ? text[^100..] : text;
    }

    private static readonly System.Buffers.SearchValues<char> SentenceEndSearch = System.Buffers.SearchValues.Create("。．！？\n\r");
}
