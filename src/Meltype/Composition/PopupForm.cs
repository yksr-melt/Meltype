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

    private const int WM_ACTIVATE = 0x0006, WA_INACTIVE = 0;

    // アクティブにされたときに入力欄に返したウィンドウと時刻 (返したことで来る前面の切り替えを、変換を捨てる切り替えと区別する)
    private static IntPtr s_returnedTo;
    private static long s_returnedAt;

    /// <summary>
    /// 前面の切り替え (EVENT_SYSTEM_FOREGROUND) が、変換ボックスなどから入力欄に返したことで来たものか (issue #997)。
    /// そうなら 1 回だけ true (変換中の内容を捨てない)。
    /// </summary>
    public static bool IsReturnedFocus(IntPtr window)
    {
        if (window == IntPtr.Zero || Interlocked.CompareExchange(ref s_returnedTo, IntPtr.Zero, window) != window) return false;
        return Environment.TickCount64 - Interlocked.Read(ref s_returnedAt) < 2000;
    }

    protected override void WndProc(ref Message m)
    {
        // マウスを乗せたウィンドウをアクティブにする Windows の設定は、WS_EX_NOACTIVATE を見ずに SetForegroundWindow と同じように
        // アクティブにする (WM_MOUSEACTIVATE は来ない)。アクティブにされたら、前にアクティブだったウィンドウ (入力欄) にすぐ返す。
        // 返さないと、確定した文字が入力欄に届かずに消える (issue #997)
        if (m.Msg == WM_ACTIVATE && (m.WParam.ToInt64() & 0xFFFF) != WA_INACTIVE && m.LParam != IntPtr.Zero)
        {
            base.WndProc(ref m);
            Interlocked.Exchange(ref s_returnedAt, Environment.TickCount64);
            Interlocked.Exchange(ref s_returnedTo, m.LParam);
            SetForegroundWindow(m.LParam);
            return;
        }
        base.WndProc(ref m);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
