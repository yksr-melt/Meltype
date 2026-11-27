// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Text;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Libraries.Chars;

namespace Meltype.Composition;

/// <summary>確定・変換の直前に、打ち間違いを直す。</summary>
internal static partial class CompositionText
{
    /// <summary>
    /// 英字を続けて打った部分 (ローマ字) の打ち間違いを直す (onegaishimsu → onegaishimasu)。直したら true。
    /// 打ったものは書き換えず、読み (<see cref="Read"/>) を作るときにかける。直し方は続きで変わる
    /// (futtemshi の時点では ふってんし、futtemshita まで打てば ふってました) ので、毎回打ったもの全体から決め直す。大文字を含む部分・英単語は直さない。
    /// </summary>
    /// <param name="typing">確定・変換に進んだ範囲 (<see cref="CompositionTextState.Settled"/>) を読むときは false。入力途中の子音で終わる並びを直さない (下の if) のと、ログを残さないことだけが違う。</param>
    private static bool FixTypos(TextReading state, TextEnv env, bool typing)
    {
        // 確定・変換の前に、数字の後ろの単位を英字のままにする (5min を 5みん にしない)。打ち間違いを直す設定に関係なく
        CompositionText.SplitUnitAfterNumber(state, env, final: true);
        CompositionText.Normalize(state, env, final: false);
        // 英字の並びの最後の w の連続 (笑い) はそのまま残し、打ち間違いの直しの対象にもしない
        CompositionText.KeepLaughter(state, env);
        if (env.RomajiTypos is not { } corrector || !env.Settings.CorrectTypos || state.Mode != DisplayMode.Auto || state.KanaInput) return false;
        var changed = false;
        // 今の表示で英字に見えている文字 (きょうは|meeting|です の meeting)。ここは直さない
        // (表示では英単語なのに、確定したら g が が に直されて めえちんがです になっていた)。
        var shownEnglish = CompositionText.EnglishMask(state, env, out var firmEnglish);
        // 確定・変換に進んだ範囲の単位は直さない。範囲の後ろの英字の並びは、範囲の境から始める
        var start = state.SettledUnits;
        var offset = state.Units.Take(start).Sum(u => u.Raw.Length);
        while (start < state.Units.Count)
        {
            if (!IsLetters(state.Units[start]) || IsLaughter(state.Units[start]))
            {
                offset += state.Units[start].Raw.Length;
                start++;
                continue;
            }
            var end = start;
            while (end < state.Units.Count && IsLetters(state.Units[end]) && !IsLaughter(state.Units[end])) end++;
            var atEnd = end == state.Units.Count;
            // 表示と確定で違うのはここだけ。入力途中の子音で終わる並び (kyouh・sumimasn) は、打っている途中は続きを打つと読めるので、表示では直さない
            // (直すと、打った子音が表示から消える)。Enter で直す (kyouh → きょう) のは今までの挙動で、打ち間違いの直しの改修で見直す
            if (typing && atEnd && state.Pending.Length > 0)
            {
                start = end;
                continue;
            }
            var letters = string.Concat(state.Units.Skip(start).Take(end - start).Select(u => u.Raw)) + (atEnd ? CompositionText.Pending(state) : "");
            var runOffset = offset;
            offset += letters.Length;
            // 数字のすぐ後ろの単位 (10|mm|で) は打ち間違いではない
            var unitAfterNumber = start > 0 && state.Units[start - 1].Raw is [var digit] && char.IsAsciiDigit(digit) &&
                UnitWords.Any(u => letters.StartsWith(u, StringComparison.OrdinalIgnoreCase));
            if (!unitAfterNumber && letters.All(char.IsAsciiLetterLower) && !env.Detector.Words.IsKnownEnglishWord(LearnedLanguages(env), letters) &&
                corrector.FirstUnreadable(letters, final: true) is var first and > 0 &&
                !(runOffset + first < shownEnglish.Count && shownEnglish[runOffset + first] && (firmEnglish[runOffset + first] || !CompositionText.FixesIntoLongWord(state, env, corrector, letters, first))) &&
                // 数字のすぐ前の短い英字 (kaibunsyo|rta|2026 の rta) は略語。打ち間違いとして直さない
                !(end < state.Units.Count && state.Units[end].Raw is [var after] && char.IsAsciiDigit(after) && first >= letters.Length - 6) &&
                !CompositionText.ContainsEnglishWord(state, env, letters, first) &&
                corrector.Fix(letters, final: true) is { } fix)
            {
                if (!typing) Diagnostics.Log.Decision($"ローマ字の打ち間違いを直しました: {Diagnostics.Log.Text(fix.Wrong)}→{Diagnostics.Log.Text(fix.Right)}");
                var analysis = env.Detector.Romaji.AnalyzeFragment(fix.Right);
                var units = analysis.Tokens.Select(t => new CompositionUnit(t.Kana, t.Romaji)).ToList();
                // 後ろに記号などが続く並び (sumimasn. の sumimasen) の、音にならずに残った子音は、記号の前と同じく確定させる (n は ん、ほかは英字のまま)
                if (!atEnd && analysis.Partial.Length > 0)
                    units.AddRange(analysis.Partial.ToLowerInvariant() is "n" or "nn" ? [new CompositionUnit("ん", analysis.Partial)] : analysis.Partial.Select(c => new CompositionUnit(c.ToString(), c.ToString())));
                state.Units.RemoveRange(start, end - start);
                state.Units.InsertRange(start, units);
                if (atEnd)
                {
                    state.Pending.Clear();
                    state.Pending.Append(analysis.Partial);
                    CompositionText.Normalize(state, env, final: true);
                }
                changed = true;
                end = start + units.Count;
            }
            start = end;
        }
        return changed;
    }

    /// <summary>
    /// 英字の並びの最後の w の連続は、チャットの笑い (きたw・だねww・www・ほんと？w)。ww を っw と読んだり、打ち間違いとして消したりせず、
    /// 1 文字ずつ w のまま残す (確定・変換・記号の直前に呼ぶ)。英単語の最後の w (new・aww) はそのまま。
    /// </summary>
    private static void KeepLaughter(TextReading state, TextEnv env)
    {
        if (state.KanaInput || state.Mode != DisplayMode.Auto) return;
        // 後ろの英字の並びから順に見る (前を入れ替えると後ろの位置がずれるので)
        var end = state.Units.Count;
        var withPending = state.Pending.Length > 0;
        while (end > state.SettledUnits || withPending)
        {
            if (!withPending && !IsLetters(state.Units[end - 1]))
            {
                end--;
                continue;
            }
            var runStart = end;
            while (runStart > state.SettledUnits && IsLetters(state.Units[runStart - 1])) runStart--;
            CompositionText.KeepLaughterAt(state, env, end, withPending);
            withPending = false;
            end = runStart;
        }
    }

    /// <summary>笑いとして w のまま残した単位 (打ち間違いの直しの対象にしない)。</summary>
    private static bool IsLaughter(CompositionUnit unit) => unit.Raw is "w" or "W" && unit.Kana == unit.Raw;

    /// <summary>end の手前で終わる英字の並び (withPending なら入力途中の子音も) の最後の w の連続を、笑いなら w の単位にする。笑いの始まりを返す。</summary>
    private static int KeepLaughterAt(TextReading state, TextEnv env, int end, bool withPending)
    {
        var pending = withPending ? state.Pending.ToString() : "";
        if (!pending.All(c => c is 'w' or 'W')) return end;
        var start = end;
        while (start > state.SettledUnits && state.Units[start - 1].Raw is [var c] && c is 'w' or 'W' && state.Units[start - 1].Kana is "っ" or "w" or "W") start--;
        var count = end - start + pending.Length;
        if (count == 0) return end;
        // 1 つだけの w は、日本語のかなの後ろ (きた + w) のときだけ笑いとみなす
        if (count == 1 && !(start > 0 && state.Units[start - 1].Kana is [var kana, ..] && kana is >= 'ぁ' and <= 'ヺ')) return end;
        // 前から続く英単語の終わり (aww・new・show) なら笑いではない。英単語とみなすのは、英字の並びの頭から始まる語か、同梱の辞書の語だけ
        // (english-words.txt の短い語が音の途中から見つかる kiyagat|taw の taw で、笑いの w を英字にしていた)
        var letters = string.Concat(state.Units.Skip(start).Take(end - start).Select(u => u.Raw)) + pending;
        var laugh = letters;
        for (var i = start - 1; i >= 0 && IsLetters(state.Units[i]); i--)
        {
            letters = state.Units[i].Raw + letters;
            var runStart = i == 0 || !IsLetters(state.Units[i - 1]);
            if (letters.Length >= 3 && (runStart ? env.Detector.Words.IsKnownEnglishWord(LearnedLanguages(env), letters) : env.Detector.Words.IsListedEnglishWord(LearnedLanguages(env), letters.ToLowerInvariant()))) return end;
        }
        state.Units.RemoveRange(start, end - start);
        if (withPending) state.Pending.Clear();
        state.Units.InsertRange(start, laugh.Select(w => new CompositionUnit(w.ToString(), w.ToString())));
        return start;
    }

    /// <summary>
    /// 打った英字 (Raw) の 1 文字ずつが、今の表示で英単語 (5 文字以上の知っている語: meeting) の区間に入っているか。
    /// firm は、そのうち守った区間か 5 文字以上の語 (短い語 mas と違い、直すと長い語になっても英字のまま)。
    /// 短い語 (onegai|shim|su の shim) は、ローマ字の途中に偶然現れることが多いので含めない (打ち間違いとして直す)。
    /// </summary>
    private static List<bool> EnglishMask(TextReading state, TextEnv env, out List<bool> firm)
    {
        var mask = new List<bool>();
        firm = [];
        var segments = CompositionText.Segments(state, env);
        for (var s = 0; s < segments.Count; s++)
        {
            var segment = segments[s];
            // 英字の並びの頭から英語と判定した区間 (inclu・ob|sidi の ob・ドメインと拡張子の Meltype.amr の amr: issue #260) は、
            // 見えている英字のまま確定する (直すと、見えていなかった かな で入る)。
            // ローマ字の後ろの英語の区間 (onegaishi|m|su の m・sumi|mas|ne の mas・ariga|tpu の tpu) は、ローマ字の打ち間違いとして直す
            var afterRomaji = s > 0 && segments[s - 1] is { IsEnglish: false } before && before.Raw.Any(char.IsAsciiLetter);
            if (segment.IsEnglish && segment.Raw.Any(char.IsAsciiLetter) && !afterRomaji)
            {
                for (var i = 0; i < segment.Raw.Length; i++)
                {
                    mask.Add(true);
                    firm.Add(true);
                }
                continue;
            }
            // 5 文字以上の知っている語 (meeting) か、同梱の英語の辞書の 2〜4 文字の語 (user・rta・av)
            // 4 文字の知っている語で、ローマ字として読めないもの (help・milk) も英語 (help|pe-ji → へおっぺーじ にしない)
            var lower = segment.Raw.ToLowerInvariant();
            var listed = segment.IsEnglish && segment.Raw.Length is >= 2 and <= 4 && env.Detector.Words.IsListedEnglishWord(LearnedLanguages(env), lower);
            var word = segment.IsProtected || listed || segment.IsEnglish && (segment.Raw.Length >= 5 && env.Detector.Words.IsKnownEnglishWord(LearnedLanguages(env), segment.Raw) ||
                segment.Raw.Length == 4 && env.Detector.Words.IsKnownEnglishWord(LearnedLanguages(env), lower) && !env.Detector.Romaji.Analyze(lower).IsValid ||
                // 3 文字のよく使う英単語で、最後の子音がかなにならないもの (tab|de の b)。表示で英字にしたものを、確定で打ち間違いとして たべ に直さない (issue #220)
                segment.Raw.Length == 3 && lower.All(char.IsAsciiLetterLower) && WordJudge.IsCommonEnglishWord(lower) && env.Detector.Romaji.AnalyzeFragment(lower).Partial.Length > 0);
            for (var i = 0; i < segment.Raw.Length; i++)
            {
                mask.Add(word);
                // 守った区間・5 文字以上の語 (shopping)・同梱の辞書の語 (main|ni|push|su の push) は、直すと長い語になっても英字のまま (FixesIntoLongWord を見ない)
                firm.Add(segment.IsProtected || segment.Raw.Length >= 5 || listed);
            }
        }
        return mask;
    }

    /// <summary>
    /// 表示で英字に見えている短い語 (すみ|mas|ね の mas) でも、直すとその前のかなと合わせて 4 文字以上の 1 つの語になるなら直す
    /// (sumimasne → すみません: issue #971)。tab|de (たべ)・tab|ga (たば) のように、直しても短い語にしかならないものは英字のまま。
    /// </summary>
    private static bool FixesIntoLongWord(TextReading state, TextEnv env, RomajiTypoCorrector corrector, string letters, int first)
    {
        if (corrector.Fix(letters, final: true) is not { } fix || first > fix.Right.Length) return false;
        // 読めない文字の直前のかな (sumima の ま) が、直した後の語に入っているか
        var before = env.Detector.Romaji.AnalyzeFragment(fix.Right[..first]).Kana.Length;
        return corrector.InLongWord(fix.Kana, before - 1, minLength: 4);
    }

    /// <summary>
    /// 読めない文字 (位置 first) が英単語の一部か。
    /// 先頭から打った英単語 (google|de、kotlin|ni、ok|no) と、同梱の辞書・固有名詞にある 5 文字以上の英単語 (…github|ni) は直さない。
    /// english-words.txt だけにある語は、先頭からの 5 文字以上のときだけ見る
    /// (日本語のローマ字の途中にも、shims や him のような英単語が偶然現れる)。
    /// </summary>
    private static bool ContainsEnglishWord(TextReading state, TextEnv env, string letters, int first)
    {
        for (var s = Math.Max(0, first - 12); s <= first; s++)
        {
            for (var e = Math.Min(letters.Length, s + 16); e > first; e--)
            {
                var word = letters[s..e];
                if (s == 0 && word.Length >= 5 && env.Detector.Words.IsKnownEnglishWord(LearnedLanguages(env), word)) return true;
                if ((s == 0 || word.Length >= 5) && env.Detector.Words.IsListedEnglishWord(LearnedLanguages(env), word)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 英字を打って、英字かかなを見せている単位か。英字から作った記号 (zl → →) は含めない
    /// (英字の並びに入れると、打ち間違いとして直したり (hogezl → ほげぞ: issue #2013)、単位・笑いの判定に混ざったりする)。
    /// </summary>
    private static bool IsLetters(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(char.IsAsciiLetter) && unit.Kana.All(char.IsLetter);

    /// <summary>
    /// 打ったものを読んだ単位 (<see cref="Typed"/>) の [start, end) を打った要素を、正しい読み (カタカナ) に置き換える。end が単位の数を超えるときは入力途中の子音も含む。
    /// かなの読みにする (英字の Raw を持たないので、英語と判定されることはない)。
    /// 単位は、確定・変換に進んだ範囲を決めずに読んだもの (<see cref="Unsettled"/>: 単位の位置が打った要素と揃う)。置き換えたら範囲も決め直す。
    /// </summary>
    public static CompositionTextState ReplaceReading(CompositionTextState state, TextEnv env, int start, int end, string rightKatakana)
    {
        state = CompositionText.Unsettled(state);
        var typed = CompositionText.Typed(state, env);
        var from = typed.Units.Take(start).Sum(u => u.Raw.Length);
        var to = end > typed.Units.Count ? state.Keys.Count : typed.Units.Take(end).Sum(u => u.Raw.Length);
        var reading = KanaChars.ToHiragana(rightKatakana).Select(c => (TypedKey)new TypedKey.Reading(c));
        return state with { Keys = state.Keys.RemoveRange(from, to - from).InsertRange(from, reading) };
    }

    /// <summary>確定・変換に進んだ範囲を決めていない値。</summary>
    public static CompositionTextState Unsettled(CompositionTextState state) => state.Settled is null ? state : state with { Settled = null };


    /// <summary>単位ごとに、英語として見せている区間に入っているか。</summary>
    private static bool[] UnitIsEnglish(TextReading state, TextEnv env)
    {
        var result = new bool[state.Units.Count];
        var unit = 0;
        foreach (var segment in CompositionText.Segments(state, env))
        {
            var remaining = segment.Raw.Length;
            while (unit < state.Units.Count && remaining > 0)
            {
                result[unit] = segment.IsEnglish;
                remaining -= state.Units[unit].Raw.Length;
                unit++;
            }
        }
        return result;
    }
}
