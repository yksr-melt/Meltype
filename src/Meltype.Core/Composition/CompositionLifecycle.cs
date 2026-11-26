// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

namespace Meltype.Composition;

/// <summary>
/// 入力状態の寿命。場面 (<see cref="CompositionScene"/>) を移すのはここだけ。
/// 場面と寿命の違う値 (直前の確定・押したキー) は、フォーカス・キャレットの移動などに分けて消す。
/// </summary>
internal static class CompositionLifecycle
{
    /// <summary>出来事の最後。入力が終わったら、押したキーを忘れる。</summary>
    internal static void EndEvent(CompositionState state)
    {
        // 親指キーを押したままなら、離したことを受け取るまで受け取り続ける (文頭で親指キーだけを押したとき。変換ボックスは空)
        if (IsComposing(state) || state.KanaLayout.ThumbHeld) return;
        state.Pressed.Clear();
    }

    /// <summary>入力を覚えないように求める入力欄か。</summary>
    internal static bool Private(CompositionState state) => state.PrivateField;

    internal static void SetPrivate(CompositionState state, bool value)
    {
        state.PrivateField = value;
        if (!value) return;
        CompositionScene WithPrivateInput(CompositionScene.WithInput scene)
        {
            scene.Input.Private = true;
            return scene;
        }
        state.Scene = state.Scene.Match(new CompositionScene.Cases<CompositionScene>
        {
            Japanese = Keep,
            Direct = Keep,
            Judging = judging =>
            {
                judging.Private = true;
                return judging;
            },
            EnglishWord = Keep,
            Typing = WithPrivateInput,
            Converting = WithPrivateInput,
        });
    }

    /// <summary>場面を移さない。</summary>
    private static CompositionScene Keep(CompositionScene scene) => scene;

    internal static bool Learning(CompositionState state, CompositionInput input, CompositionEnv env) => env.Settings.LearningEnabled && !Private(state) && !input.Private;

    internal static bool KanaInput(CompositionEnv env) => env.Settings.InputStyle == Config.InputStyle.Kana;

    /// <summary>英語とも日本語とも読める語を、次の語の文脈に合わせて確定し直すか。</summary>
    internal static bool AutoCorrect(CompositionEnv env) => env.Settings.AutoCorrectAfterCommit && env.Settings.AutoDetection;

    /// <summary>変換ボックスに入力中か、英数状態で打ち始めの語を判定中か。</summary>
    internal static bool IsComposing(CompositionState state) => state.Scene.Match(new CompositionScene.Cases<bool>
    {
        Japanese = _ => false,
        Direct = _ => false,
        Judging = _ => true,
        EnglishWord = _ => false,
        Typing = _ => true,
        Converting = _ => true,
    });

    /// <summary>変換して候補を選んでいるか。</summary>
    internal static bool IsConverting(CompositionState state) => state.Scene is CompositionScene.Converting;

    /// <summary>英数 (直接入力) か。</summary>
    internal static bool IsDirect(CompositionState state) => state.Scene.Match(new CompositionScene.Cases<bool>
    {
        Japanese = _ => false,
        Direct = _ => true,
        Judging = _ => true,
        EnglishWord = _ => true,
        Typing = typing => typing.Input.Direct,
        Converting = converting => converting.Input.Direct,
    });

    /// <summary>英数状態で英語と判定した語の続きを打っているか。</summary>
    internal static bool InEnglishWord(CompositionState state) => state.Scene.Match(new CompositionScene.Cases<bool>
    {
        Japanese = _ => false,
        Direct = _ => false,
        Judging = judging => judging.InEnglishWord,
        EnglishWord = _ => true,
        Typing = _ => false,
        Converting = _ => false,
    });

    /// <summary>英数 (直接入力) にする・やめる。入力中なら、入力が終わったときの英数 / 日本語を変える。</summary>
    internal static void SetDirect(CompositionState state, bool direct)
    {
        CompositionScene Return(CompositionScene.WithInput scene)
        {
            scene.Input.Direct = direct;
            return scene;
        }
        state.Scene = state.Scene.Match(new CompositionScene.Cases<CompositionScene>
        {
            Japanese = scene => direct ? Idle(true) : Keep(scene),
            Direct = scene => direct ? Keep(scene) : Idle(false),
            Judging = scene => direct ? Keep(scene) : Idle(false),
            EnglishWord = scene => direct ? Keep(scene) : Idle(false),
            Typing = Return,
            Converting = Return,
        });
    }

    /// <summary>何も打っていない場面。</summary>
    private static CompositionScene Idle(bool direct) => direct ? new CompositionScene.Direct() : new CompositionScene.Japanese();

    /// <summary>英数状態で打ち始めた語の判定を始める。</summary>
    internal static CompositionScene.Judging StartJudging(CompositionState state)
    {
        var judging = new CompositionScene.Judging { InEnglishWord = InEnglishWord(state), Private = Private(state) };
        state.Scene = judging;
        return judging;
    }

    /// <summary>英数状態で打ち始めた語が英語だった。</summary>
    internal static void StartEnglishWord(CompositionState state) => state.Scene = new CompositionScene.EnglishWord();

    /// <summary>英数状態で打っている語を忘れる (キャレットが動いた・入力モードが変わった)。</summary>
    internal static void ForgetDirectInput(CompositionState state) => state.Scene = state.Scene.Match(new CompositionScene.Cases<CompositionScene>
    {
        Japanese = Keep,
        Direct = Keep,
        Judging = _ => Idle(true),
        EnglishWord = _ => Idle(true),
        Typing = Keep,
        Converting = Keep,
    });

    /// <summary>英語と判定した語の続きではなくなった (英数字でない文字を打った)。</summary>
    internal static void LeaveEnglishWord(CompositionState state) => state.Scene = state.Scene.Match(new CompositionScene.Cases<CompositionScene>
    {
        Japanese = Keep,
        Direct = Keep,
        Judging = judging =>
        {
            judging.InEnglishWord = false;
            return judging;
        },
        EnglishWord = _ => Idle(true),
        Typing = Keep,
        Converting = Keep,
    });

    /// <summary>変換ボックスの入力を始める。</summary>
    internal static CompositionInput Begin(CompositionState state, CompositionEnv env, bool privateInput = false, ReconversionSelection? reconversion = null)
    {
        var kana = KanaInput(env);
        var input = new CompositionInput
        {
            Text = new CompositionTextState
            {
                // 設定「英字を主に入力」: 判定せず、打った英字のまま見せる (issue #839)
                Mode = env.Settings.EnglishFirstInput && !kana ? DisplayMode.HalfWidthAlphanumeric : DisplayMode.Auto,
                KanaInput = kana,
                SlashAtStart = env.SlashAtStart,
                FullWidthParentheses = env.Settings.FullWidthParentheses,
                SquareBrackets = env.Settings.SquareBrackets,
            },
            Direct = IsDirect(state),
            Private = Private(state) || privateInput,
            Reconversion = reconversion,
        };
        state.Scene = new CompositionScene.Typing(input);
        return input;
    }

    /// <summary>英数状態で打った語がローマ字だった: 日本語に戻し、変換ボックスの入力を始める。</summary>
    internal static CompositionInput BeginFromJudging(CompositionState state, CompositionScene.Judging judging, CompositionEnv env)
    {
        state.Scene = Idle(false);
        return Begin(state, env, judging.Private);
    }

    /// <summary>変換して候補を選ぶ場面にする。conversion が null (変換する文節が無い) なら、打っている場面。</summary>
    internal static CompositionScene.Converting? Convert(CompositionState state, CompositionInput input, Conversion? conversion)
    {
        if (conversion is null)
        {
            StopConverting(state, input);
            return null;
        }
        var converting = new CompositionScene.Converting(input, conversion);
        state.Scene = converting;
        return converting;
    }

    /// <summary>変換をやめて、打っている場面に戻る。</summary>
    internal static void StopConverting(CompositionState state, CompositionInput input) => state.Scene = new CompositionScene.Typing(input);

    /// <summary>打った文字がすべて消えたら、入力を終える。</summary>
    internal static void FinishIfEmpty(CompositionState state, CompositionInput input)
    {
        if (CompositionText.IsEmpty(input.Text)) Finish(state);
    }

    /// <summary>入力を終える (確定した・取り消した)。</summary>
    internal static void Finish(CompositionState state) => state.Scene = Idle(IsDirect(state));

    /// <summary>パスワード欄に移ったとき、未確定入力と前の欄の文脈を確定せずに捨てる。</summary>
    internal static void CancelPending(CompositionState state)
    {
        Finish(state);
        ForgetLastCommit(state);
        state.LiveCache.Clear();
        state.Pressed.Clear();
        state.KanaLayout.Reset();
        CompositionOutput.Begin(state.Out);
        CompositionOutput.Hide(state.Out);
    }

    /// <summary>フォーカスが変わったときなど。前の入力欄の文脈を持ち越さない。</summary>
    internal static void ResetContext(CompositionState state)
    {
        state.Own = null;
        state.Correctable.Clear();
    }

    /// <summary>今の未確定入力が、自動では変換してはいけない保護区間か (F9/F10 などの明示指定は除く)。</summary>
    internal static bool IsProtectedInput(CompositionScene.WithInput scene) =>
        scene is CompositionScene.Typing { Input.Text: { Mode: DisplayMode.Auto } text } && CompositionText.HasProtectedTail(text);

    /// <summary>キャレットが動いたかもしれないとき (Meltype を通らなかったキー・クリック)。直前の語は確定し直さない。</summary>
    internal static void ForgetLastCommit(CompositionState state)
    {
        CompositionOutput.ForgetCommitted(state.Out);
        // キャレットが動いた・アプリが文字を入れた: キャレットの前は自分が確定した語ではないので、読めたらアプリの文字を使う。
        // 読めなければ前の文字は分からないものとして扱う (消した後・行の先頭に、英単語の前後の空白を足さない)
        ResetContext(state);
    }
}
