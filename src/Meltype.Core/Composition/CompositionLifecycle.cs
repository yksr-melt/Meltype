// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

namespace Meltype.Composition;

/// <summary>入力状態の寿命。開始、取消、直接入力への切り替え、フォーカス・キャレット移動で消す値をここに集める。</summary>
internal static class CompositionLifecycle
{
    /// <summary>出来事の最後。入力が終わったら、再変換の選択範囲と押したキーを忘れる。</summary>
    internal static void EndEvent(CompositionState state)
    {
        // 親指キーを押したままなら、離したことを受け取るまで受け取り続ける (文頭で親指キーだけを押したとき。変換ボックスは空)
        if (IsComposing(state) || state.KanaLayout.ThumbHeld) return;
        // 読みをすべて削除した場合も、元の選択範囲は置換せず再変換を終了する。
        if (state.Reconversion is not null) ClearComposition(state);
        state.Pressed.Clear();
    }

    /// <summary>入力を覚えないように求める入力欄か。その欄で扱った未確定入力は、欄を離れて確定しても学習しない。</summary>
    internal static bool Private(CompositionState state) => state.PrivateField;

    internal static void SetPrivate(CompositionState state, bool value)
    {
        state.PrivateField = value;
        if (value && IsComposing(state)) state.PrivateComposition = true;
    }

    internal static bool Learning(CompositionState state, CompositionEnv env) => env.Settings.LearningEnabled && !Private(state) && !state.PrivateComposition;

    internal static bool KanaInput(CompositionState state, CompositionEnv env) => env.Settings.InputStyle == Config.InputStyle.Kana;

    /// <summary>英語とも日本語とも読める語を、次の語の文脈に合わせて確定し直すか。</summary>
    internal static bool AutoCorrect(CompositionState state, CompositionEnv env) => env.Settings.AutoCorrectAfterCommit && env.Settings.AutoDetection;

    /// <summary>変換ボックスに入力中か、英数状態で打ち始めの語を判定中か。</summary>
    internal static bool IsComposing(CompositionState state) => !CompositionText.IsEmpty(state.Text) || state.HeldLetters.Length > 0;

    /// <summary>変換して候補を選んでいるか。</summary>
    internal static bool IsConverting(CompositionState state) => state.Clauses.Count > 0;

    /// <summary>英数 (直接入力) にする・やめる。やめたら、英数状態で判定中の語も忘れる。</summary>
    internal static void SetDirect(CompositionState state, bool direct)
    {
        var wasDirect = state.Direct;
        state.Direct = direct;
        if (direct) return;
        state.DirectEnglishWord = false;
        if (wasDirect) ClearHeld(state);
    }

    /// <summary>英数状態で打ち始めた語の判定が決まった (japanese: ローマ字だったので日本語入力に戻った / false: 英語だった)。</summary>
    internal static void DirectDecided(CompositionState state, bool japanese)
    {
        state.Direct = !japanese;
        state.DirectEnglishWord = !japanese;
    }

    /// <summary>変換中の文節・候補・選択中の文節をすべて消す。再変換の状態も消す。</summary>
    internal static void ClearComposition(CompositionState state)
    {
        state.Reconversion = null;
        FinishText(state);
    }

    /// <summary>読みと変換中の文節を消す。確定後も次の入力に使う文脈や、再変換の選択範囲には触れない。</summary>
    internal static void FinishText(CompositionState state)
    {
        CompositionText.Clear(state.Text);
        state.Conversion = null;
    }

    /// <summary>パスワード欄に移ったとき、未確定入力と前の欄の文脈を確定せずに捨てる。</summary>
    internal static void CancelPending(CompositionState state, CompositionEnv env)
    {
        ClearComposition(state);
        ClearHeld(state);
        ForgetLastCommit(state);
        state.PrivateComposition = false;
        state.KeptChoices.Clear();
        state.LiveCache.Clear();
        state.Predictions = null;
        state.Pressed.Clear();
        state.KanaLayout.Reset();
        CompositionOutput.Begin(state.Out);
        CompositionOutput.Hide(state.Out);
    }

    /// <summary>フォーカスが変わったときなど。前の入力欄の文脈を持ち越さない。</summary>
    internal static void ResetContext(CompositionState state)
    {
        state.Last = null;
        state.Correctable.Clear();
    }

    internal static void ClearHeld(CompositionState state)
    {
        state.HeldLetters.Clear();
        state.HeldSent.Clear();
    }

    /// <summary>今の未確定入力が、自動では変換してはいけない保護区間か (F9/F10 などの明示指定は除く)。</summary>
    internal static bool IsProtectedInput(CompositionState state) => !IsConverting(state) && state.Text.Mode == DisplayMode.Auto && CompositionText.HasProtectedTail(state.Text);

    /// <summary>キャレットが動いたかもしれないとき (Meltype を通らなかったキー・クリック)。直前の語は確定し直さない。</summary>
    internal static void ForgetLastCommit(CompositionState state)
    {
        state.Correctable.Clear();
        CompositionOutput.ForgetCommitted(state.Out);
        // キャレットが動いた・アプリが文字を入れた: キャレットの前は自分が確定した語ではないので、読めたらアプリの文字を使う。
        // 読めなければ前の文字も、それが英語だったかも分からないものとして扱う (消した後・行の先頭に、英単語の前後の空白を足さない)
        state.LastCommitTime = long.MinValue / 2;
        state.Last = null;
    }

    internal static void BeginComposition(CompositionState state, CompositionEnv env, bool privateInput = false)
    {
        state.PrivateComposition = Private(state) || privateInput;
        state.KeptChoices.Clear();
        state.ModeByKey = false;
        state.EscReverted = false;
        // 設定「英字を主に入力」: 判定せず、打った英字のまま見せる (issue #839)
        if (env.Settings.EnglishFirstInput && !KanaInput(state, env) && state.Text.Mode == DisplayMode.Auto) state.Text.Mode = DisplayMode.HalfWidthAlphanumeric;
        state.Text.KanaInput = KanaInput(state, env);
        state.Text.FullWidthParentheses = env.Settings.FullWidthParentheses;
        state.Text.SquareBrackets = env.Settings.SquareBrackets;
        state.Text.LaughterAfterKana = env.Settings.LaughterAfterKana;
    }
}
