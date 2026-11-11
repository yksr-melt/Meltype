// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;

namespace Meltype.Composition;

/// <summary>出来事 1 つの間の、入力欄への書き込み (<see cref="TextEdit"/>)・変換ボックスの表示・キーをアプリに通すか。</summary>
internal sealed class CompositionOutput
{
    private readonly List<TextEdit> _commits = [];
    private int _pendingDelete;
    private string? _pendingExpect;
    // 続けて確定した文字の終わりの部分 (確定し直しで消す文字を、フロントで確かめられるように)
    private readonly StringBuilder _committed = new();
    private const int MaxCommitted = 256;
    // 英数状態で判定中の語のうち、アプリに通した文字 (日本語と分かったら消す)
    private readonly StringBuilder _heldPassed = new();
    private string? _before;
    private CompositionView? _view;
    private bool _hidden;

    /// <summary>今のキーをアプリに通すか。</summary>
    public bool PassedCurrent { get; private set; }

    /// <summary>キャレットの前の文字。出来事の前の文字に、この出来事で確定した文字を入れたもの (フロントは確定を入れてから変換中の文字を置く)。</summary>
    public string? Before
    {
        get
        {
            var before = _before;
            if (before is null) return null;
            foreach (var edit in _commits) before = before[..^Math.Min(edit.DeleteBefore, before.Length)] + edit.Text;
            return before;
        }
    }

    public string? After { get; private set; }

    public CompositionView? View => _hidden ? null : _view;

    public void Begin(string? before, string? after)
    {
        _commits.Clear();
        _pendingDelete = 0;
        _pendingExpect = null;
        _before = before;
        After = after;
        PassedCurrent = false;
        _hidden = false;
    }

    public SessionResult Result(bool consumed, Config.Settings settings)
    {
        if (_pendingDelete > 0) Add("");
        // アプリに渡したキーの文字は、確定した文字の続きとしては分からない
        if (!consumed) _committed.Clear();
        // 操作ガイドを出さない設定では、入力に応じた案内 (手動判定の提案・予測など) だけ。どのフロントもこの表示を受け取る
        var view = _hidden || _view is not { } shown ? null : SessionResult.ForFront(shown, settings.ShowsGuide, settings.NumberKeysSelectCandidates);
        return new SessionResult(consumed, _commits.ToList(), view);
    }

    public void PassCurrent() => PassedCurrent = true;

    public void Commit(string text) => Add(text);

    /// <summary>直前に確定した文字の末尾から、キャレットを count 文字だけ左へ戻す。</summary>
    public void MoveCaretBackward(int count)
    {
        var last = _commits[^1];
        _commits[^1] = last with { CaretBack = last.CaretBack + count };
    }

    /// <summary>
    /// 確定し直し: キャレットの前の count 文字を replacement に置き換えて、続けて今確定した語 (current) を入れる。
    /// 前の文字を消せなかったときに current だけを入れられるように、分けて渡す (Meltype IME など。<see cref="TextEdit"/>)。
    /// </summary>
    public void ReplaceBackward(int count, string replacement, string current)
    {
        _pendingDelete += count;
        Add(replacement + current, current);
    }

    /// <summary>
    /// 英数状態で判定中の語 (count 文字) を消す (日本語と分かったとき)。今のキーも通すところだったなら、通さずに消す数から引く。
    /// 消す文字は、アプリに通した文字で確かめる。
    /// </summary>
    public void DeleteHeld(int count)
    {
        if (PassedCurrent) count--;
        PassedCurrent = false;
        _pendingExpect = count > 0 && _heldPassed.Length >= count ? _heldPassed.ToString(0, count) : null;
        _heldPassed.Clear();
        _pendingDelete += Math.Max(0, count);
    }

    public void RecordHeldPassed(char c)
    {
        if (char.IsAscii(c) && !char.IsControl(c)) _heldPassed.Append(c);
    }

    public void ResetHeldPassed() => _heldPassed.Clear();

    public void ForgetCommitted() => _committed.Clear();

    public void Show(CompositionView view)
    {
        _view = view;
        _hidden = false;
    }

    public void Hide()
    {
        _view = null;
        _hidden = true;
    }

    private void Add(string text, string? current = null)
    {
        string? expect = null;
        if (_pendingDelete > 0)
        {
            if (_pendingExpect is { } expected)
            {
                expect = expected;
                _pendingExpect = null;
                _committed.Clear();
            }
            else if (_committed.Length >= _pendingDelete)
            {
                expect = _committed.ToString(_committed.Length - _pendingDelete, _pendingDelete);
                _committed.Length -= _pendingDelete;
            }
            else _committed.Clear();
        }
        _commits.Add(new TextEdit(_pendingDelete, text, expect, _pendingDelete > 0 ? current : null));
        _pendingDelete = 0;
        _committed.Append(text);
        if (_committed.Length > MaxCommitted) _committed.Remove(0, _committed.Length - MaxCommitted);
    }
}
