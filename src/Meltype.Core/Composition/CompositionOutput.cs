// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;
using Meltype.Dictionary;

namespace Meltype.Composition;

/// <summary>
/// 出来事 1 つの間の、入力欄への書き込み (<see cref="TextEdit"/>)・変換ボックスの表示・キーをアプリに通すか。
/// 書き込みと表示の文字は区間の並び (<see cref="ComposedText"/>) で受け取り、ここで文字列にする (<see cref="Render"/>)。
/// 句読点は、読み・変換・学習では設定によらず 、。 で持ち (<see cref="CompositionText"/> の Symbol)、設定の句読点は文字列にするときに当てる。
/// 入力欄から読んだ文字 (<see cref="Before"/>・<see cref="After"/>) は、設定の句読点を 、。 に戻して渡す。
/// 設定の句読点を当てるのは打った句読点の区間 (<see cref="SpanKind.Punctuation"/>) だけで、語の中の 、。 (モーニング娘。) は変えない。
/// ここのほかに設定の句読点を扱うのは、再変換の読み (<see cref="FromSetting"/>) だけ。
/// </summary>
internal static class CompositionOutput
{
    private const int MaxCommitted = 256;

    /// <summary>キャレットの前の文字。出来事の前の文字に、この出来事で確定した文字を入れたもの (フロントは確定を入れてから変換中の文字を置く)。</summary>
    public static string? Before(CompositionOutputState state, CompositionEnv env) =>
        RawBefore(state, env) is { } before ? FromSetting(env.Settings.Punctuation, before) : null;

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
        if (state.PendingDelete > 0) CompositionOutput.Add(state, "");
        // アプリに渡したキーの文字は、確定した文字の続きとしては分からない
        if (!consumed) state.Committed.Clear();
        // 操作ガイドを出さない設定では、入力に応じた案内 (手動判定の提案・予測など) だけ。どのフロントもこの表示を受け取る
        var view = state.Hidden || state.View is not { } shown ? null : SessionResult.ForFront(shown, settings.ShowsGuide, settings.NumberKeysSelectCandidates);
        return new SessionResult(consumed, state.Commits.ToList(), view);
    }

    public static void PassCurrent(CompositionOutputState state) => state.PassedCurrent = true;

    public static void Commit(CompositionOutputState state, CompositionEnv env, ComposedText text) => CompositionOutput.Add(state, Render(env, text));

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
        CompositionOutput.Add(state, Render(env, replacement + current), Render(env, current));
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
        string Text(ComposedText text) => Render(env, text);
        state.View = new CompositionView(Text(view.Text), [.. view.Candidates.Select(Text)], view.SelectedIndex, view.Converting, view.Hint,
            view.Clauses?.Select(Text).ToList(), view.SelectedClause, view.Notes, view.Meaning,
            view.Suggestion is { } suggestion ? Text(suggestion) : null, view.Predictions?.Select(Text).ToList(), view.SelectedPrediction, view.Typed,
            view.Committed is { } committed ? Text(committed) : null, view.ContextHint);
        state.Hidden = false;
    }

    /// <summary>書き込み・表示の文字。打った句読点の区間にだけ設定の句読点を当てる。</summary>
    internal static string Render(CompositionEnv env, ComposedText text) =>
        string.Concat(text.Spans.Select(s => s.Kind == SpanKind.Punctuation ? Punctuate(env.Settings.Punctuation, s.Text) : s.Text));

    private static string Punctuate(Config.PunctuationStyle style, string text)
    {
        var (comma, period) = Marks(style);
        return text.Replace('、', comma).Replace('。', period);
    }

    /// <summary>入力欄から読んだ文字の、設定の句読点を 、。 に戻す。</summary>
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
    string? ContextHint = null);
