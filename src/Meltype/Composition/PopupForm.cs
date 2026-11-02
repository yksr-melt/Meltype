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

    private const int WM_MOUSEACTIVATE = 0x0021, WM_ACTIVATE = 0x0006, MA_NOACTIVATE = 3, WA_INACTIVE = 0;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                // クリック・マウスを乗せたこと (Windows の「マウスポインターを合わせたウィンドウをアクティブにする」) でアクティブにならない (issue #997)
                m.Result = MA_NOACTIVATE;
                return;
            case WM_ACTIVATE when (m.WParam.ToInt64() & 0xFFFF) != WA_INACTIVE && m.LParam != IntPtr.Zero:
                // それでもアクティブにされた (マウスを乗せたウィンドウをアクティブにする設定は WS_EX_NOACTIVATE を見ないことがある)。
                // 入力欄のウィンドウにすぐ返す。返さないと、確定した文字が入力欄に届かずに消える (issue #997)
                base.WndProc(ref m);
                SetForegroundWindow(m.LParam);
                return;
        }
        base.WndProc(ref m);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
