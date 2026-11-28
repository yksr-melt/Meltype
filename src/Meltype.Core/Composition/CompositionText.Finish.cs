// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>確定・変換に使う読みの仕上げ (数字の後ろの単位・笑いの w)。</summary>
internal static partial class CompositionText
{
    /// <summary>
    /// 打ったものを読んだだけの単位に、確定・変換に使う読みの仕上げをかける: 数字の後ろの単位を英字のままにし (5min を 5みん にしない)、
    /// 音として確定した入力途中の子音を単位に移し、英字の並びの最後の w の連続 (笑い) を w のまま残す。打ったものは書き換えない。
    /// </summary>
    private static void Finish(TextReading state, TextEnv env)
    {
        CompositionText.SplitUnitAfterNumber(state, env, final: true);
        CompositionText.Normalize(state, env, final: false);
        CompositionText.KeepLaughter(state, env);
    }

    /// <summary>
    /// 英字の並びの最後の w の連続は、チャットの笑い (きたw・だねww・www・ほんと？w)。ww を っw と読まずに、
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
    /// 英字を打って、英字かかなを見せている単位か。英字から作った記号 (zl → →) は含めない (英字の並びに入れると、単位・笑いの判定に混ざる)。
    /// </summary>
    private static bool IsLetters(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(char.IsAsciiLetter) && unit.Kana.All(char.IsLetter);
}
