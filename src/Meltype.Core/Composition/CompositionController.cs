// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 yksr-melt

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
        var passNext = CompositionDirectInput.Tick(state, env, key.TimeMs);
        env = env.WaitingForInput();
        // フロントは保留中なのでキーを送ったが、通常の英数判定ではこの続きのキーはアプリに通っていた。
        var englishContinuation = key.Ch is { } ch && (char.IsAsciiLetterOrDigit(ch) || ch is '@' or '_');
        var consumed = !(passNext && englishContinuation) && CompositionKeys.Handle(state, env, key);
        if (!releaseFollows) CompositionKeys.HandleRelease(state, env, key.Vk);
        Settle(state, env);
        return Result(state, env, consumed);
    }

    /// <summary>ワークアラウンド: releaseFollows を付けて押したキーを離した (親指シフトの親指キー・中指NICOLA のシフトのキー)。</summary>
    public static SessionResult Release(CompositionState state, CompositionEnv env, int vk)
    {
        CompositionOutput.Begin(state.Out);
        CompositionKeys.HandleRelease(state, env, vk);
        Settle(state, env);
        // 離したことはフロントが押下のとおりに通す。Consumed が false なら、離したことで今のキーの働きをした (文字に使わずに離した親指キー)
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

    /// <summary>出来事を処理した後。表示を作り直し、入力が終わったら再変換の状態と押したキーを忘れる。</summary>
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

    internal static void AbandonDirectHold(CompositionState state) => CompositionLifecycle.ClearHeld(state);

    /// <summary>変換中の文節の候補を番号で選ぶ (Mac の候補ウィンドウをクリックしたときなど)。</summary>
    public static SessionResult SelectCandidate(CompositionState state, CompositionEnv env, int index)
    {
        CompositionOutput.Begin(state.Out);
        SelectCandidateAt(state, env, index);
        return Result(state, env, consumed: true);
    }

    private static void SelectCandidateAt(CompositionState state, CompositionEnv env, int index)
    {
        // 入力中に出した候補 (設定「入力中も変換候補をいくつか表示」) をクリックしたら、変換を始めてからその候補を選ぶ
        // 入力中の候補は読み全体の候補なので、変換を始めて文節に分かれると一覧が変わり、番号が別の語を指す。
        // 見えていた一覧のまま、読み全体 1 つの文節にして、クリックした語を選ぶ
        if (!CompositionLifecycle.IsConverting(state) && CompositionPresentation.TypingCandidates(state, env) is { } typing)
        {
            if (index < 0 || index >= typing.Count) return;
            state.Conversion = new Conversion([new Clause(CompositionText.AllKana(state.Text, env, final: false), false, typing) { Raw = CompositionText.Raw(state.Text), Expanded = true }], ConversionTrigger.Other, preferJapanese: false);
        }
        if (!CompositionLifecycle.IsConverting(state) || state.Clauses.Count == 0) return;
        var clause = state.Clauses[state.SelectedClause];
        if (index < 0 || index >= clause.Candidates.Count) return;
        clause.Index = index;
        clause.Changed = true;
        CompositionPresentation.UpdateView(state, env);
    }

    /// <summary>予測変換の候補を番号で選んで確定する (Meltype IME の候補の一覧をクリックしたとき)。</summary>
    public static SessionResult CommitPredictionAt(CompositionState state, CompositionEnv env, int index)
    {
        CompositionOutput.Begin(state.Out);
        var predictions = CompositionPresentation.CurrentPredictions(state, env);
        if (!CompositionLifecycle.IsConverting(state) && index >= 0 && index < predictions.Count)
        {
            CompositionCommit.CommitPrediction(state, env, predictions[index]);
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
        if (state.HeldLetters.Length > 0) CompositionDirectInput.ReleaseHeldAsEnglish(state, env);
        // e + 結合アクセントを「え + アクセント」にしない。明示した表示モード・候補・かな入力は尊重する。
        // ASCII 英字だけの原文に Latin アクセントが続く場合に限り、変換や学習を経由せず確定する。
        if (preserveLatinRaw && !CompositionLifecycle.IsConverting(state) && state.Reconversion is null && !state.Text.KanaInput &&
            state.Text.Mode is DisplayMode.Auto or DisplayMode.HalfWidthAlphanumeric &&
            !CompositionText.IsEmpty(state.Text) && CompositionText.Raw(state.Text).All(char.IsAsciiLetter))
        {
            state.Correctable.Clear();
            CompositionCommit.CommitText(state, env, ComposedText.English(CompositionText.Raw(state.Text)), english: true, preserveText: true, rule: "英字にアクセント記号が続くので打ったまま");
        }
        else CompositionCommit.CommitIfAny(state, env, preserveText);
        CompositionPresentation.UpdateView(state, env);
    }

    internal static bool Private(CompositionState state) => CompositionLifecycle.Private(state);

    internal static void SetPrivate(CompositionState state, bool value) => CompositionLifecycle.SetPrivate(state, value);

    internal static bool IsComposing(CompositionState state) => CompositionLifecycle.IsComposing(state);

    internal static bool IsConverting(CompositionState state) => CompositionLifecycle.IsConverting(state);

    internal static void SetDirect(CompositionState state, bool direct) => CompositionLifecycle.SetDirect(state, direct);

    internal static void ForgetLastCommit(CompositionState state) => CompositionLifecycle.ForgetLastCommit(state);

    internal static void ResetContext(CompositionState state) => CompositionLifecycle.ResetContext(state);

    internal static void CancelPending(CompositionState state, CompositionEnv env) => CompositionLifecycle.CancelPending(state, env);

    internal static IReadOnlyList<int> ComposingControlKeys(CompositionState state, CompositionEnv env) => CompositionKeys.ComposingControlKeys(state, env);

    internal static void MarkSlashAtStart(CompositionState state) => CompositionConversion.MarkSlashAtStart(state);
}
