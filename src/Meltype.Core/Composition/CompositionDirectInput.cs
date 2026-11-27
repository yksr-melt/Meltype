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
    /// <summary>英数状態の判定で、この時間打鍵が無ければ英語とみなして判定をやめる。</summary>
    private const long DirectHoldIdleMs = 700;

    /// <summary>英数状態で判定中の語が、無入力のまま一定時間たったら英語とみなす。</summary>
    internal static void Tick(CompositionState state, CompositionEnv env, long nowMs)
    {
        if (state.Scene is not CompositionScene.Judging judging || nowMs - judging.LastKeyTime < DirectHoldIdleMs) return;
        Decide(state, judging, env, final: true);
    }

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)

    internal static void Hold(CompositionState state, CompositionScene.Judging judging, CompositionEnv env, KeyEvent e, int position)
    {
        judging.Letters.Append(VirtualKeys.ToLetter(e.Vk));
        judging.Positions.Add(position);
        judging.LastKeyTime = e.TimeMs;
        Send(state, judging, e);
        Decide(state, judging, env, final: false);
    }

    /// <summary>
    /// 判定中の語の打鍵を、判定を待たずにアプリへ送る。前は判定できるまで保留していたので、英単語 (can・game・today) は
    /// Space を押すまで画面に出ず、英数状態の入力が遅れて見えた。ローマ字と分かったら、送った分を BackSpace で消す。
    /// </summary>
    internal static void Send(CompositionState state, CompositionScene.Judging judging, KeyEvent e)
    {
        CompositionOutput.PassCurrent(state.Out);
        judging.Sent.Append(e.Scan is > 0 and < 0x10000 ? (char)e.Scan : '\0');
    }

    /// <summary>判定中の英字の、最後の - より後ろ (e-ma → ma)。- が無ければ全体。</summary>
    internal static string LastHyphenPart(CompositionScene.Judging judging)
    {
        var letters = judging.Letters.ToString();
        return letters[(letters.LastIndexOf('-') + 1)..];
    }

    private static void Decide(CompositionState state, CompositionScene.Judging judging, CompositionEnv env, bool final)
    {
        // 英語の接頭辞の後の - (e-、co-) の後ろだけで判定する。e-mail・co-op のような - の入った英単語なら英語。
        var part = LastHyphenPart(judging);
        var verdict = env.Detector.Words.IsHyphenatedEnglishWord(CompositionText.LearnedLanguages(env), judging.Letters.ToString()) ? Verdict.English
            : part.Length == 0 ? Verdict.Undecided
            : env.ClassifyDirect!(part, final);
        // 2 文字の英単語 (up、my) でも、ローマ字の打ちかけとして読める (う + p) なら、続きを見てから決める
        // (upa- → うぱー、my → みゃ)。Space などで打ち終われば英語。
        if (verdict == Verdict.English && !final && part.Length <= 2 && judging.Letters.Length <= 2 &&
            env.Detector.Romaji.AnalyzeFragment(part.ToLowerInvariant()) is { IsValid: true, Partial.Length: > 0 })
        {
            verdict = Verdict.Undecided;
        }
        Diagnostics.Log.Info($"英数状態の判定: {Diagnostics.Log.Text(judging.Letters.ToString())}→ {verdict}{(final ? " (打ち終わり)" : "")}");
        if (verdict == Verdict.Japanese) SwitchToJapanese(state, judging, env);
        else if (verdict != Verdict.Undecided || final) ReleaseAsEnglish(state);
    }

    /// <summary>ローマ字だった: 日本語入力に戻し、判定中の英字を変換ボックスに入れる。</summary>
    internal static void SwitchToJapanese(CompositionState state, CompositionScene.Judging judging, CompositionEnv env)
    {
        // 判定を待たずに送っていた英字を消して、変換ボックスに入れ直す。
        CompositionOutput.DeleteHeld(state.Out, judging.Sent.ToString());
        var input = CompositionLifecycle.BeginFromJudging(state, judging, env);
        foreach (var (c, position) in judging.Letters.ToString().Zip(judging.Positions))
        {
            input.Text = input.Text.KanaInput && Detection.KanaDetector.KanaForKey(position, char.IsAsciiLetterUpper(c)) is { } kana
                ? CompositionText.AppendKana(input.Text, c, kana) : CompositionText.Append(input.Text, c);
        }
    }

    /// <summary>英語だった: 打鍵はもう送ってあるので、判定をやめるだけ。</summary>
    internal static void ReleaseAsEnglish(CompositionState state)
    {
        state.Correctable.Clear();
        CompositionLifecycle.StartEnglishWord(state);
    }
}
