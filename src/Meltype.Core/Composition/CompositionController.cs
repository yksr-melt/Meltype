// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 yksr-melt

using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>
/// 入力の出来事の入口。キー・確定・再描画を呼び、1 回分の応答を返す。状態の寿命は CompositionLifecycle が管理する。
/// ほかの Composition* のクラスは、このクラスを呼び戻さない。キーの処理は変換・確定・表示を呼べるが、変換・確定・表示はキーの処理を呼ばず、表示は確定を呼ばない
/// (呼び合うと、1 回の出来事の中で処理の順序が追えなくなる)。
/// </summary>
internal static class CompositionController
{
    /// <summary>
    /// キーを押した。
    /// 変換ボックスに入力していないときは、<see cref="CompositionKeys"/> が入力を始めるキーだけを受け取り、ほかはアプリに通す。
    /// releaseFollows なら、離したことは <see cref="Release"/> で届く (false なら押した直後に離したとみなす)。
    /// </summary>
    public static SessionResult Press(CompositionState state, CompositionEnv env, KeyPress key, bool releaseFollows = false)
    {
        CompositionOutput.Begin(state.Out);
        CompositionDirectInput.Tick(state, env, key.TimeMs);
        env = env.WaitingForInput();
        var consumed = CompositionKeys.Handle(state, env, key);
        if (!releaseFollows) CompositionKeys.HandleRelease(state, env, key.Vk, key.Position);
        Settle(state, env);
        return Result(state, env, consumed);
    }

    /// <summary>ワークアラウンド: releaseFollows を付けて押したキーを離した (親指シフトの親指キー・中指NICOLA のシフトのキー)。</summary>
    public static SessionResult Release(CompositionState state, CompositionEnv env, int vk, int position)
    {
        CompositionOutput.Begin(state.Out);
        CompositionKeys.HandleRelease(state, env, vk, position);
        Settle(state, env);
        // 文字に使わずに離した親指キーが元の働きでも使われなければ、フロントは保留していた押下と解放を通せる
        return Result(state, env, consumed: true);
    }

    /// <summary>キーをアプリに通す。変換ボックスの状態は変えない (英数状態で判定中の語は、しばらく打たなければ英語とみなす)。</summary>
    public static SessionResult Pass(CompositionState state, CompositionEnv env, long nowMs)
    {
        CompositionOutput.Begin(state.Out);
        CompositionDirectInput.Tick(state, env, nowMs);
        return Result(state, env, consumed: false);
    }

    /// <summary>入力の内容を変えない出来事 (フォーカス・キャレットの知らせなど) の結果。表示は今のまま。</summary>
    public static SessionResult Current(CompositionState state, CompositionEnv env)
    {
        CompositionOutput.Begin(state.Out);
        return Result(state, env, consumed: true) with { Consumed = false };
    }

    private static SessionResult Result(CompositionState state, CompositionEnv env, bool consumed) => CompositionOutput.Result(state.Out, env, consumed && !state.Out.PassedCurrent, env.Settings);

    /// <summary>出来事を処理した後。表示を作り直し、入力が終わったら押したキーを忘れる。</summary>
    private static void Settle(CompositionState state, CompositionEnv env)
    {
        CompositionPresentation.UpdateView(state, env);
        CompositionLifecycle.EndEvent(state);
    }

    /// <summary>
    /// 背景の変換の結果が届いた。入力スレッドで呼ぶ。変換エンジンが使えなかった間の結果を捨てて、入力中の表示を作り直す。
    /// 表示を作り直したときだけ応答を返す。
    /// </summary>
    public static SessionResult? RefreshConversion(CompositionState state, CompositionEnv env)
    {
        CompositionOutput.Begin(state.Out);
        return CompositionRefresh.Refresh(state, env) ? Result(state, env, consumed: true) : null;
    }

    /// <summary>変換中の文節の候補を番号で選ぶ (Mac の候補ウィンドウをクリックしたときなど)。</summary>
    public static SessionResult SelectCandidate(CompositionState state, CompositionEnv env, int index)
    {
        CompositionOutput.Begin(state.Out);
        SelectCandidateAt(state, env, index);
        return Result(state, env, consumed: true);
    }

    private static void SelectCandidateAt(CompositionState state, CompositionEnv env, int index)
    {
        if (state.Scene is not CompositionScene.Converting converting) return;
        var clause = converting.Conversion.SelectedClause;
        var candidates = clause.Candidates;
        if (index < 0 || index >= candidates.Count) return;
        clause.Choice = candidates[index];
        clause.Changed = true;
        CompositionPresentation.UpdateView(state, env);
    }

    /// <summary>予測変換の候補を番号で選んで確定する (Meltype IME の候補の一覧をクリックしたとき)。</summary>
    public static SessionResult CommitPredictionAt(CompositionState state, CompositionEnv env, int index)
    {
        CompositionOutput.Begin(state.Out);
        if (state.Scene is CompositionScene.Typing { Input: var input } && CompositionPresentation.CurrentPredictions(input, env) is var predictions && index >= 0 && index < predictions.Count)
        {
            input.Text = CompositionText.MergeTail(input.Text);
            CompositionCommit.CommitPrediction(state, input, env, predictions[index]);
            CompositionPresentation.UpdateView(state, env);
        }
        return Result(state, env, consumed: true);
    }

    /// <summary>
    /// 無効化・フォーカス喪失などで、未確定の内容をそのまま確定する。consumed: 出来事をアプリに通さないか。
    /// preserveLatinRaw: 結合アクセントが続くので、明示的に選択していない ASCII 英字の原文を確定する。preserveText: 確定する文字列に自動空白を加えない。
    /// </summary>
    public static SessionResult CommitPending(CompositionState state, CompositionEnv env, bool consumed = true, bool preserveLatinRaw = false, bool preserveText = false)
    {
        CompositionOutput.Begin(state.Out);
        CommitAll(state, env, preserveLatinRaw, preserveText);
        return Result(state, env, consumed);
    }

    private static void CommitAll(CompositionState state, CompositionEnv env, bool preserveLatinRaw, bool preserveText)
    {
        env = env.WaitingForInput();
        if (state.Scene is CompositionScene.WithInput { Input: var editing }) editing.Text = CompositionText.MergeTail(editing.Text);
        if (state.Scene is CompositionScene.Judging) CompositionDirectInput.ReleaseAsEnglish(state);
        // e + 結合アクセントを「え + アクセント」にしない。明示した表示モード・候補・かな入力は尊重する。
        // ASCII 英字だけの原文に Latin アクセントが続く場合に限り、変換や学習を経由せず確定する。
        if (preserveLatinRaw && state.Scene is CompositionScene.Typing { Input: { Reconversion: null, Text: { KanaInput: false, Mode: DisplayMode.Auto or DisplayMode.HalfWidthAlphanumeric } text } } &&
            CompositionText.Raw(CompositionText.Read(text, env)).All(char.IsAsciiLetter))
        {
            state.Correctable.Clear();
            CompositionCommit.CommitText(state, env, ComposedText.English(CompositionText.Raw(CompositionText.Read(text, env))), english: true, preserveText: true, rule: "英字にアクセント記号が続くので打ったまま");
        }
        else CompositionCommit.CommitIfAny(state, env, preserveText);
        CompositionPresentation.UpdateView(state, env);
    }

    internal static bool Private(CompositionState state) => CompositionLifecycle.Private(state);

    internal static void SetPrivate(CompositionState state, bool value) => CompositionLifecycle.SetPrivate(state, value);

    internal static bool IsComposing(CompositionState state) => CompositionLifecycle.IsComposing(state);

    internal static bool IsConverting(CompositionState state) => CompositionLifecycle.IsConverting(state);

    internal static bool IsDirect(CompositionState state) => CompositionLifecycle.IsDirect(state);

    internal static void SetDirect(CompositionState state, bool direct) => CompositionLifecycle.SetDirect(state, direct);

    internal static bool InEnglishWord(CompositionState state) => CompositionLifecycle.InEnglishWord(state);

    internal static void LeaveEnglishWord(CompositionState state) => CompositionLifecycle.LeaveEnglishWord(state);

    internal static void ForgetDirectInput(CompositionState state) => CompositionLifecycle.ForgetDirectInput(state);

    internal static void ForgetLastCommit(CompositionState state) => CompositionLifecycle.ForgetLastCommit(state);

    internal static void ResetContext(CompositionState state) => CompositionLifecycle.ResetContext(state);

    internal static void CancelPending(CompositionState state) => CompositionLifecycle.CancelPending(state);

    internal static IReadOnlyList<int> ComposingControlKeys(CompositionState state, CompositionEnv env) => CompositionKeys.ComposingControlKeys(state, env);
}
