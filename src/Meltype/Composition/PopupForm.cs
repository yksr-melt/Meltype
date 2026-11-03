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
        // 最前面には CreateParams の WS_EX_TOPMOST を使う。
        // Form.TopMost を使うと、WinForms が表示時に SetFocus を呼んで入力欄のフォーカスを奪う。
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

    private const int WM_ACTIVATE = 0x0006, WA_INACTIVE = 0;
    private const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;

    protected override void WndProc(ref Message m)
    {
        // Windows の「マウスを合わせたウィンドウをアクティブにする」は WS_EX_NOACTIVATE を見ない。
        // アクティブになる前に断り、クリック自体は消さない (候補の操作を妨げない)。
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        // 別の経路でアクティブにされた場合は、前にアクティブだったウィンドウ (入力欄) に返す。
        // 返さないと、確定した文字が入力欄に届かずに消える (issue #997)
        if (m.Msg == WM_ACTIVATE && (m.WParam.ToInt64() & 0xFFFF) != WA_INACTIVE && m.LParam != IntPtr.Zero)
        {
            base.WndProc(ref m);
            SetForegroundWindow(m.LParam);
            return;
        }
        base.WndProc(ref m);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
