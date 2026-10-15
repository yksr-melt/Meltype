// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>枠もタスクバーのボタンも無く、常に手前に出て、フォーカスを奪わない小さなウィンドウ (変換ボックス・意味・入力モードの表示)。</summary>
internal abstract class PopupForm : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020;

    protected PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
    }

    /// <summary>クリックを下のウィンドウへ通すか。</summary>
    protected virtual bool ClickThrough => false;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            if (ClickThrough) cp.ExStyle |= WS_EX_TRANSPARENT;
            return cp;
        }
    }
}
