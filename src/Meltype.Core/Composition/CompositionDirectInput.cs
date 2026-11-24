// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>英数状態の打鍵をすぐアプリに通し、ローマ字と分かったら未確定入力へ移す。</summary>
internal static class CompositionDirectInput
{
    /// <summary>英数状態でふつうの語の判定を打ち切る無入力時間。</summary>
    private const long DirectHoldIdleMs = 700;

    /// <summary>英数状態で一定時間打鍵が止まったら、続きがないものとして判定する。</summary>
    internal static void Tick(CompositionState state, CompositionEnv env, long nowMs)
    {
        if (state.HeldLetters.Length == 0 || nowMs - state.HeldLastKeyTime < DirectHoldIdleMs) return;
        // bare- に続く bones も、とれー に続く「にんぐ」も、途中の無入力だけでは区別できない。
        var letters = state.HeldLetters.ToString();
        if (!IsAmbiguousHyphen(letters, letters.LastIndexOf('-'))) DecideHeld(state, env, final: true);
    }

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)

    internal static void Hold(CompositionState state, CompositionEnv env, KeyEvent e)
    {
        if (state.HeldLetters.Length == 0) state.PrivateComposition = CompositionLifecycle.Private(state);
        state.HeldLetters.Append(VirtualKeys.ToLetter(e.Vk));
        state.HeldLastKeyTime = e.TimeMs;
        SendHeld(state, e);
        DecideHeld(state, env, final: false);
    }

    /// <summary>
    /// 判定中の語の打鍵を、判定を待たずにアプリへ送る。前は判定できるまで保留していたので、英単語 (can・game・today) は
    /// Space を押すまで画面に出ず、英数状態の入力が遅れて見えた。ローマ字と分かったら、送った分を BackSpace で消す。
    /// </summary>
    internal static void SendHeld(CompositionState state, KeyEvent e)
    {
        CompositionOutput.PassCurrent(state.Out);
        state.HeldSent.Append(e.Scan is > 0 and < 0x10000 ? (char)e.Scan : '\0');
    }

    /// <summary>判定中の英字の、最後の - より後ろ (e-ma → ma)。- が無ければ全体。</summary>
    internal static string LastHyphenPart(CompositionState state)
    {
        var letters = state.HeldLetters.ToString();
        return letters[(letters.LastIndexOf('-') + 1)..];
    }

    /// <summary>長音としても読める語が、まだ - の後ろを待っているか。終わりなら Space・Enter で変換・確定する。</summary>
    internal static bool HasPendingLongVowel(CompositionState state) =>
        state.HeldLetters.Length > 1 && state.HeldLetters[^1] == '-' &&
        IsAmbiguousHyphen(state.HeldLetters.ToString(), state.HeldLetters.Length - 1);

    /// <summary>英数状態の保留を判定する。final は次の打鍵がないものとして判定するとき。</summary>
    internal static void DecideHeld(CompositionState state, CompositionEnv env, bool final)
    {
        // ハイフン付きの英単語 (e-mail、co-op) は英語。長音かもしれないときは前後の読みも調べる (tore-do → とれーど)。
        var letters = state.HeldLetters.ToString();
        var hyphen = letters.LastIndexOf('-');
        var part = letters[(hyphen + 1)..];
        var verdict = env.Detector.IsHyphenatedEnglishWord(CompositionText.LearningContext(state.Text, env), letters) ? Verdict.English
            : IsAmbiguousHyphen(letters, hyphen) && IsKnownLongVowelReading(env, letters[..hyphen], part, final) ? Verdict.Japanese
            : part.Length == 0 ? Verdict.Undecided
            : env.ClassifyDirect!(part, final);
        // 2 文字の英単語 (up、my) でも、ローマ字の打ちかけとして読める (う + p) なら、続きを見てから決める
        // (upa- → うぱー、my → みゃ)。Space などで打ち終われば英語。
        if (verdict == Verdict.English && !final && part.Length <= 2 && state.HeldLetters.Length <= 2 &&
            env.Detector.Romaji.AnalyzeFragment(part.ToLowerInvariant()) is { IsValid: true, Partial.Length: > 0 })
        {
            verdict = Verdict.Undecided;
        }
        Diagnostics.Log.Info($"英数状態の判定: {Diagnostics.Log.Text(state.HeldLetters.ToString())}→ {verdict}{(final ? " (打ち終わり)" : "")}");
        if (verdict == Verdict.Japanese) SwitchHeldToJapanese(state, env);
        else if (verdict != Verdict.Undecided || final) ReleaseHeldAsEnglish(state, env);
    }

    // e- などの英語の接頭辞は従来の規則で保留する。ほかの語は、長音の読みもあるときだけ - が保留に入る。
    private static bool IsAmbiguousHyphen(string letters, int hyphen) =>
        hyphen > 0 && !CompositionDetector.IsHyphenPrefix(letters[..hyphen]);

    private static bool IsKnownLongVowelReading(CompositionEnv env, string beforeHyphen, string afterHyphen, bool final)
    {
        if (afterHyphen.Length == 0) return final;
        if (env.Detector.Romaji.AnalyzeFragment(afterHyphen) is not { IsValid: true, Partial: "" } suffix) return false;
        var prefix = env.Detector.Romaji.AnalyzeFragment(beforeHyphen);
        return CommonReadings.Words.Contains(prefix.Kana + 'ー' + suffix.Kana);
    }

    /// <summary>ローマ字だった: 日本語入力に戻し、判定中の英字を変換ボックスに入れる。</summary>
    internal static void SwitchHeldToJapanese(CompositionState state, CompositionEnv env)
    {
        var letters = state.HeldLetters.ToString();
        var sent = state.HeldSent.ToString();
        // 判定を待たずに送っていた英字を消して、変換ボックスに入れ直す。
        CompositionOutput.DeleteHeld(state.Out, sent);
        CompositionLifecycle.ClearHeld(state);
        CompositionLifecycle.DirectDecided(state, true);
        CompositionLifecycle.BeginComposition(state, env, state.PrivateComposition);
        foreach (var c in letters)
        {
            if (state.Text.KanaInput && Detection.KanaDetector.KanaForKey(char.ToUpperInvariant(c), char.IsAsciiLetterUpper(c)) is { } kana) CompositionText.AppendKana(state.Text, c, kana);
            else CompositionText.Append(state.Text, env, c);
        }
    }

    /// <summary>英語だった: 打鍵はもう送ってあるので、判定をやめるだけ。</summary>
    internal static void ReleaseHeldAsEnglish(CompositionState state, CompositionEnv env)
    {
        CompositionLifecycle.ClearHeld(state);
        state.Correctable.Clear();
        CompositionLifecycle.DirectDecided(state, false);
    }
}
