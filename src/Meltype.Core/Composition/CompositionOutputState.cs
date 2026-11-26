// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;

namespace Meltype.Composition;

/// <summary>状態 (データだけ)。処理は <see cref="CompositionOutput"/> の関数が受け取って書き換える。</summary>
internal sealed class CompositionOutputState
{

    public readonly List<TextEdit> Commits = [];

    public int PendingDelete;

    public string? PendingExpect;

    // 続けて確定した文字の終わりの部分 (確定し直しで消す文字を、フロントで確かめられるように)
    public readonly StringBuilder Committed = new();

    public CompositionView? View;

    public bool Hidden;

    /// <summary>今のキーをアプリに通すか。</summary>
    public bool PassedCurrent;
}
