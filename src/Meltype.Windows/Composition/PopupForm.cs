// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

namespace Meltype.Composition;

/// <summary>枠もタスクバーのボタンも無く、常に手前に出て、フォーカスを奪わない小さなウィンドウ (変換ボックス・意味・入力モードの表示)。</summary>
internal abstract class PopupForm : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020;

    protected PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        // Form.TopMost は使わない。WinForms が表示時に SetFocus を呼んで入力欄のフォーカスを奪う。
        // 最前面には CreateParams の WS_EX_TOPMOST で作り、表示・更新のたびに SetWindowPos で上げる。
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

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    /// <summary>表示して、最前面の先頭へ上げる。表示中なら、ほかの最前面のウィンドウ (通知など) の下に回っていても上げる。</summary>
    public void ShowOnTop()
    {
        if (!Visible) Show();
        // SW_SHOWNOACTIVATE は Z オーダーを変えないので、上げないと一度背面へ回ったあとの表示も背面に出続ける
        // (issue #1549・#1613・#1655・#1727・#1731)
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
