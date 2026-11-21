// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;

namespace Meltype.Composition;

/// <summary>
/// 出来事 1 つの間の、入力欄への書き込み (<see cref="TextEdit"/>)・変換ボックスの表示・キーをアプリに通すか。
/// 句読点は、読み・変換・学習では設定によらず 、。 で持ち (<see cref="CompositionText"/> の Symbol)、設定の句読点はここで書き込み・表示の文字に当てる。
/// 入力欄から読んだ文字 (<see cref="Before"/>・<see cref="After"/>) は、設定の句読点を 、。 に戻して渡す。
/// ここのほかに設定の句読点を扱うのは、再変換の読み (<see cref="FromSetting"/>) と候補の重複を消す所 (<see cref="ToSetting"/>) だけ。
/// </summary>
internal static class CompositionOutput
{
    private const int MaxCommitted = 256;

    /// <summary>キャレットの前の文字。出来事の前の文字に、この出来事で確定した文字を入れたもの (フロントは確定を入れてから変換中の文字を置く)。</summary>
    public static string? Before(CompositionOutputState state, CompositionEnv env)
    {
        var before = env.Before;
        if (before is null) return null;
        foreach (var edit in state.Commits) before = before[..^Math.Min(edit.DeleteBefore, before.Length)] + edit.Text;
        return FromSetting(env.Settings.Punctuation, before);
    }

    public static CompositionView? View(CompositionOutputState state) => state.Hidden ? null : state.View;

    /// <summary>キャレットの後ろの文字 (設定の句読点を変換ボックスの句読点に読み替えたもの)。</summary>
    public static string? After(CompositionEnv env) => env.After is null ? null : FromSetting(env.Settings.Punctuation, env.After);

    public static void Begin(CompositionOutputState state)
    {
        state.Commits.Clear();
        state.PendingDelete = 0;
        state.PendingExpect = null;
        state.PassedCurrent = false;
        state.Hidden = false;
    }

    public static SessionResult Result(CompositionOutputState state, CompositionEnv env, bool consumed, Config.Settings settings)
    {
        if (state.PendingDelete > 0) CompositionOutput.Add(state, env, "");
        // アプリに渡したキーの文字は、確定した文字の続きとしては分からない
        if (!consumed) state.Committed.Clear();
        // 操作ガイドを出さない設定では、入力に応じた案内 (手動判定の提案・予測など) だけ。どのフロントもこの表示を受け取る
        var view = state.Hidden || state.View is not { } shown ? null : SessionResult.ForFront(shown, settings.ShowsGuide, settings.NumberKeysSelectCandidates);
        return new SessionResult(consumed, state.Commits.ToList(), view);
    }

    public static void PassCurrent(CompositionOutputState state) => state.PassedCurrent = true;

    public static void Commit(CompositionOutputState state, CompositionEnv env, string text) => CompositionOutput.Add(state, env, text);

    /// <summary>直前に確定した文字の末尾から、キャレットを count 文字だけ左へ戻す。</summary>
    public static void MoveCaretBackward(CompositionOutputState state, int count)
    {
        var last = state.Commits[^1];
        state.Commits[^1] = last with { CaretBack = last.CaretBack + count };
    }

    /// <summary>
    /// 確定し直し: キャレットの前の count 文字を replacement に置き換えて、続けて今確定した語 (current) を入れる。
    /// 前の文字を消せなかったときに current だけを入れられるように、分けて渡す (Meltype IME など。<see cref="TextEdit"/>)。
    /// </summary>
    public static void ReplaceBackward(CompositionOutputState state, CompositionEnv env, int count, string replacement, string current)
    {
        state.PendingDelete += count;
        CompositionOutput.Add(state, env, replacement + current, current);
    }

    /// <summary>
    /// 英数状態で判定中の語 (アプリに通した文字 sent) を消す (日本語と分かったとき)。今のキーも通すところだったなら、通さずに消す数から引く。
    /// 消す文字は、アプリに通した文字で確かめる。
    /// </summary>
    public static void DeleteHeld(CompositionOutputState state, string sent)
    {
        if (state.PassedCurrent && sent.Length > 0) sent = sent[..^1];
        state.PassedCurrent = false;
        state.PendingExpect = sent.Length > 0 ? sent : null;
        state.PendingDelete += sent.Length;
    }

    public static void ForgetCommitted(CompositionOutputState state) => state.Committed.Clear();

    public static void Show(CompositionOutputState state, CompositionEnv env, CompositionView view)
    {
        state.View = view with
        {
            Text = CompositionOutput.Punctuate(state, env, view.Text),
            Candidates = view.Candidates.Select((text) => CompositionOutput.Punctuate(state, env, text)).ToList(),
            Clauses = view.Clauses?.Select((text) => CompositionOutput.Punctuate(state, env, text)).ToList(),
            Suggestion = view.Suggestion is { } suggestion ? CompositionOutput.Punctuate(state, env, suggestion) : null,
            Predictions = view.Predictions?.Select((text) => CompositionOutput.Punctuate(state, env, text)).ToList(),
            Committed = view.Committed is { } committed ? CompositionOutput.Punctuate(state, env, committed) : null,
        };
        state.Hidden = false;
    }

    private static string Punctuate(CompositionOutputState state, CompositionEnv env, string text) => ToSetting(env.Settings.Punctuation, text);

    /// <summary>
    /// 、。 をすべて設定の句読点にする。ここには文字しか届かないので、辞書の語・顔文字の 、。 (モーニング娘。) も変わる。
    /// 句読点にだけ当てるには、区間の種類を出口まで渡す必要がある (#1735)。
    /// </summary>
    internal static string ToSetting(Config.PunctuationStyle style, string text)
    {
        var (comma, period) = Marks(style);
        return text.Replace('、', comma).Replace('。', period);
    }

    /// <summary>設定の句読点を 、。 に戻す (<see cref="ToSetting"/> の逆)。</summary>
    internal static string FromSetting(Config.PunctuationStyle style, string text)
    {
        var (comma, period) = Marks(style);
        return text.Replace(comma, '、').Replace(period, '。');
    }

    private static (char Comma, char Period) Marks(Config.PunctuationStyle style) => style switch
    {
        Config.PunctuationStyle.Japanese => ('、', '。'),
        Config.PunctuationStyle.FullWidthCommaPeriod => ('，', '．'),
        Config.PunctuationStyle.FullWidthCommaKuten => ('，', '。'),
        Config.PunctuationStyle.ToutenFullWidthPeriod => ('、', '．'),
        _ => throw new InvalidOperationException($"句読点の設定 {style} の文字が無い"),
    };

    public static void Hide(CompositionOutputState state)
    {
        state.View = null;
        state.Hidden = true;
    }

    private static void Add(CompositionOutputState state, CompositionEnv env, string text, string? current = null)
    {
        text = CompositionOutput.Punctuate(state, env, text);
        current = current is null ? null : CompositionOutput.Punctuate(state, env, current);
        string? expect = null;
        if (state.PendingDelete > 0)
        {
            if (state.PendingExpect is { } expected)
            {
                expect = expected;
                state.PendingExpect = null;
                state.Committed.Clear();
            }
            else if (state.Committed.Length >= state.PendingDelete)
            {
                expect = state.Committed.ToString(state.Committed.Length - state.PendingDelete, state.PendingDelete);
                state.Committed.Length -= state.PendingDelete;
            }
            else state.Committed.Clear();
        }
        state.Commits.Add(new TextEdit(state.PendingDelete, text, expect, state.PendingDelete > 0 ? current : null));
        state.PendingDelete = 0;
        state.Committed.Append(text);
        if (state.Committed.Length > MaxCommitted) state.Committed.Remove(0, state.Committed.Length - MaxCommitted);
    }
}
