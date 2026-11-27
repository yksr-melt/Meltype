// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>英数状態の打鍵をすぐアプリに通し、ローマ字と分かったら未確定入力へ移す。</summary>
internal static class CompositionDirectInput
{
    /// <summary>英数状態の判定で、この時間打鍵が無ければ英語とみなして判定をやめる。</summary>
    private const long DirectHoldIdleMs = 700;

    /// <summary>無入力で保留を終える。通常の英数判定で次の打鍵を通すはずだった位置なら true。</summary>
    internal static bool Tick(CompositionState state, CompositionEnv env, long nowMs)
    {
        if (state.Scene is not CompositionScene.Judging judging || nowMs - judging.LastKeyTime < DirectHoldIdleMs) return false;
        var passNext = judging.ResumeAt > judging.Letters.Length;
        ResumeNormalHold(state);
        if (judging.Letters.Length > 0) Decide(state, judging, env, final: true);
        return passNext;
    }

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)

    internal static void Hold(CompositionState state, CompositionScene.Judging judging, CompositionEnv env, KeyEvent e, int position, char? passedChar)
    {
        judging.Letters.Append(VirtualKeys.ToLetter(e.Vk));
        judging.Positions.Add(position);
        judging.LastKeyTime = e.TimeMs;
        Send(state, judging, passedChar);
        Decide(state, judging, env, final: false);
    }

    /// <summary>
    /// 判定中の語の打鍵を、判定を待たずにアプリへ送る。前は判定できるまで保留していたので、英単語 (can・game・today) は
    /// Space を押すまで画面に出ず、英数状態の入力が遅れて見えた。ローマ字と分かったら、送った分を BackSpace で消す。
    /// </summary>
    internal static void Send(CompositionState state, CompositionScene.Judging judging, char? passedChar)
    {
        CompositionOutput.PassCurrent(state.Out);
        judging.Sent.Append(passedChar ?? '\0');
    }

    /// <summary>判定中の英字の、最後の - より後ろ (e-ma → ma)。- が無ければ全体。</summary>
    internal static string LastHyphenPart(CompositionScene.Judging judging)
    {
        var letters = judging.Letters.ToString();
        return letters[(letters.LastIndexOf('-') + 1)..];
    }

    private static void Decide(CompositionState state, CompositionScene.Judging judging, CompositionEnv env, bool final)
    {
        var part = LastHyphenPart(judging);
        var verdict = judging.ResumeAt == 0 ? Classify(env, part, judging.Letters.ToString(), final) : Verdict.Undecided;
        if (!final && (judging.ResumeAt > 0 || verdict == Verdict.English && CanWaitForRomaji(judging, env, part)))
        {
            if (judging.ResumeAt == 0)
            {
                // 英語と判定した直後の文字キーはアプリに通る。延長に失敗したら、その次からの打鍵を使う。
                judging.ResumeAt = judging.Letters.Length + 1;
                return;
            }

            var resume = judging.ResumeAt;
            var suffix = part[Math.Min(resume, part.Length)..];
            verdict = Classify(env, suffix, suffix, final: false);
            var analysis = env.Detector.Romaji.Analyze(RomajiDetector.ReadCRow(part));
            var reading = analysis.Kana;
            var before = MatchingPrefix(judging, env);
            // memori には memorial の可能性がある。最後まで読めた語か、語に助詞が続いたところで切り替える。
            if (before is not null && part.Length >= ScoreEngine.MaxPendingKeys &&
                analysis is { IsValid: true, Partial.Length: 0, Tokens: [.., var last] } && !BuiltInWordChecker.Shared.HasPrefix(part) &&
                (CommonReadings.Words.Contains(reading) ||
                 DictionaryDetector.StartsWithParticle(last.Romaji) == last.Romaji &&
                 CommonReadings.Words.Contains(reading.AsSpan(0, reading.Length - last.Kana.Length))))
            {
                SwitchToJapanese(state, judging, env);
                return;
            }
            // 6 打で英語と競合したときでも、次の子音と母音までで判断する (memori → memoriga)。
            if (before is not null && verdict != Verdict.Japanese && analysis.IsValid && part.Length < ScoreEngine.MaxPendingKeys + 2 &&
                CommonReadings.Words.HasPrefix(reading))
            {
                if (verdict is Verdict.English or Verdict.Unknown) judging.ResumeAt = part.Length + 1;
                return;
            }

            ResumeNormalHold(state);
            if (resume > part.Length || verdict is Verdict.English or Verdict.Unknown)
            {
                ReleaseAsEnglish(state);
                return;
            }
        }
        Diagnostics.Log.Info($"英数状態の判定: {Diagnostics.Log.Text(judging.Letters.ToString())}→ {verdict}{(final ? " (打ち終わり)" : "")}");
        if (verdict == Verdict.Japanese) SwitchToJapanese(state, judging, env);
        else if (verdict != Verdict.Undecided || final) ReleaseAsEnglish(state);
    }

    private static Verdict Classify(CompositionEnv env, string part, string held, bool final)
    {
        // 英語の接頭辞の後の - (e-、co-) の後ろだけで判定する。e-mail・co-op のような語全体の一致も見る。
        if (env.Detector.Words.IsHyphenatedEnglishWord(CompositionText.LearnedLanguages(env), held)) return Verdict.English;
        var verdict = part.Length == 0 ? Verdict.Undecided : env.ClassifyDirect!(part, final);
        // up や my も、ローマ字の打ちかけなら続きを待つ。
        if (verdict == Verdict.English && !final && held.Length <= 2 &&
            env.Detector.Romaji.AnalyzeFragment(part) is { IsValid: true, Partial.Length: > 0 })
            return Verdict.Undecided;
        return verdict;
    }

    private static bool CanWaitForRomaji(CompositionScene.Judging judging, CompositionEnv env, string part)
    {
        // 英単語の途中を拾い直した部分や、完成した英単語は通常の判定に任せる。
        if (env.Settings.InputStyle != InputStyle.Romaji || part.Length != judging.Letters.Length ||
            part.Length >= ScoreEngine.MaxPendingKeys || BuiltInWordChecker.Shared.IsWord(part) ||
            MatchingPrefix(judging, env) is not { } before) return false;
        if (before.Length > 0 && (char.IsAsciiLetterOrDigit(before[^1]) || before[^1] is '@' or '_')) return false;
        return env.Detector.Romaji.Analyze(RomajiDetector.ReadCRow(part)) is { IsValid: true, Tokens.Count: > 0, Partial.Length: > 0 } analysis &&
            CommonReadings.Words.HasPrefix(analysis.Kana);
    }

    private static string? MatchingPrefix(CompositionScene.Judging judging, CompositionEnv env)
    {
        // 最後のキーはまだ送っていない。すでに送った分を、今回実際に読めた前文と照合する。
        var echoed = judging.Sent.ToString(0, judging.Sent.Length - 1);
        return env.EditableBefore is { } before && before.EndsWith(echoed, StringComparison.Ordinal) ? before[..^echoed.Length] : null;
    }

    internal static void ResumeNormalHold(CompositionState state)
    {
        if (state.Scene is not CompositionScene.Judging { ResumeAt: > 0 } judging) return;
        var skip = Math.Min(judging.ResumeAt, judging.Letters.Length);
        // 英字はすでにアプリに届いている。再送せず通常判定に残る末尾だけを持つ。
        judging.Letters.Remove(0, skip);
        judging.Sent.Remove(0, skip);
        judging.Positions.RemoveRange(0, skip);
        judging.ResumeAt = 0;
        if (judging.Letters.Length == 0) CompositionLifecycle.ForgetDirectInput(state);
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
