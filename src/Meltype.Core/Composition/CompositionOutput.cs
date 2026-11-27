// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Dictionary;
using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>
/// 出来事 1 つの間の、入力欄への書き込み (<see cref="TextEdit"/>)・変換ボックスの表示・キーをアプリに通すか。
/// 書き込みと表示の文字は区間の並び (<see cref="ComposedText"/>) で受け取り、ここで文字列にする (<see cref="NotationRules.Write"/>)。
/// 入力欄から読んだ文字 (<see cref="Before"/>・<see cref="After"/>) は、設定の句読点を 、。 に戻して渡す (<see cref="NotationRules.Read"/>)。
/// </summary>
internal static class CompositionOutput
{
    private const int MaxCommitted = 256;

    /// <summary>キャレットの前の文字。出来事の前の文字に、この出来事で確定した文字を入れたもの (フロントは確定を入れてから変換中の文字を置く)。</summary>
    public static string? Before(CompositionOutputState state, CompositionEnv env) =>
        RawBefore(state, env) is { } before ? NotationRules.Read(before, env.Settings.Punctuation) : null;

    /// <summary><see cref="Before"/> の、設定の句読点を戻す前の文字 (入力欄の文字のまま)。</summary>
    public static string? RawBefore(CompositionOutputState state, CompositionEnv env)
    {
        var before = env.Before;
        if (before is null) return null;
        foreach (var edit in state.Commits) before = before[..^Math.Min(edit.DeleteBefore, before.Length)] + edit.Text;
        return before;
    }

    public static CompositionView? View(CompositionOutputState state) => state.Hidden ? null : state.View;

    /// <summary>キャレットの後ろの文字 (設定の句読点を変換ボックスの句読点に読み替えたもの)。</summary>
    public static string? After(CompositionEnv env) => env.After is null ? null : NotationRules.Read(env.After, env.Settings.Punctuation);

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
        if (state.PendingDelete > 0) CompositionOutput.Add(state, "");
        // アプリに渡したキーの文字は、確定した文字の続きとしては分からない
        if (!consumed) state.Committed.Clear();
        // 操作ガイドを出さない設定では、入力に応じた案内 (手動判定の提案・予測など) だけ。どのフロントもこの表示を受け取る
        var view = state.Hidden || state.View is not { } shown ? null : SessionResult.ForFront(shown, settings.ShowsGuide, settings.NumberKeysSelectCandidates);
        return new SessionResult(consumed, state.Commits.ToList(), view);
    }

    public static void PassCurrent(CompositionOutputState state) => state.PassedCurrent = true;

    public static void Commit(CompositionOutputState state, CompositionEnv env, ComposedText text) => CompositionOutput.Add(state, NotationRules.Write(text, env.Settings.Punctuation));

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
    public static void ReplaceBackward(CompositionOutputState state, CompositionEnv env, int count, ComposedText replacement, ComposedText current)
    {
        state.PendingDelete += count;
        CompositionOutput.Add(state, NotationRules.Write(replacement + current, env.Settings.Punctuation), NotationRules.Write(current, env.Settings.Punctuation));
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

    public static void Show(CompositionOutputState state, CompositionEnv env, ComposedView view)
    {
        string Text(ComposedText text) => NotationRules.Write(text, env.Settings.Punctuation);
        state.View = new CompositionView(Text(view.Text), [.. view.Candidates.Select(Text)], view.SelectedIndex, view.Converting, view.Hint,
            view.Clauses?.Select(Text).ToList(), view.SelectedClause, view.Notes, view.Meaning,
            view.Suggestion is { } suggestion ? Text(suggestion) : null, view.Predictions?.Select(Text).ToList(), view.SelectedPrediction, view.Typed,
            view.Committed is { } committed ? Text(committed) : null, view.ContextHint, CaretBack: view.CaretBack, Colors: view.Colors);
        state.Hidden = false;
    }

    public static void Hide(CompositionOutputState state)
    {
        state.View = null;
        state.Hidden = true;
    }

    private static void Add(CompositionOutputState state, string text, string? current = null)
    {
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

/// <summary>変換ボックスの表示 (<see cref="CompositionView"/>) の、文字を区間の並びで持つ形。<see cref="CompositionOutput.Show"/> が文字列にする。</summary>
internal sealed record ComposedView(
    ComposedText Text,
    IReadOnlyList<ComposedText> Candidates,
    int SelectedIndex,
    bool Converting,
    string Hint,
    IReadOnlyList<ComposedText>? Clauses = null,
    int SelectedClause = -1,
    IReadOnlyList<CandidateNote>? Notes = null,
    string? Meaning = null,
    ComposedText? Suggestion = null,
    IReadOnlyList<ComposedText>? Predictions = null,
    int SelectedPrediction = -1,
    string? Typed = null,
    ComposedText? Committed = null,
    string? ContextHint = null,
    int CaretBack = 0,
    IReadOnlyList<string?>? Colors = null);
