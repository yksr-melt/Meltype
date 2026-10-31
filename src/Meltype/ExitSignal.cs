// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype;

/// <summary>
/// 動いている Meltype に「終了して」と伝える (Meltype.exe --exit)。インストール・アンインストールの前に使う。
/// 管理者として動いている Meltype は、ふつうの権限のプロセスからは止められない (Stop-Process が拒否される) ので、
/// 見えない窓で終了の合図を受け取り、ふつうの権限からの合図も受け付ける (ChangeWindowMessageFilterEx)。
/// </summary>
internal sealed class ExitSignal : NativeWindow, IDisposable
{
    private const string Caption = "Meltype.ExitSignal";
    private const uint MSGFLT_ALLOW = 1;
    private static readonly uint ExitMessage = RegisterWindowMessage("Meltype.Exit");

    public ExitSignal()
    {
        CreateHandle(new CreateParams { Caption = Caption });
        // ふつうの権限からの合図を受け付けるのは、管理者として動いているときだけ。ふつうの権限で動いているときは、
        // 送り手 (インストーラーなど) からはそのまま届く。広げると、サンドボックス (整合性レベル Low) のプロセスからも終了させられてしまう。
        if (Input.ForegroundTracker.SelfElevated) ChangeWindowMessageFilterEx(Handle, ExitMessage, MSGFLT_ALLOW, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        if (ExitMessage != 0 && m.Msg == (int)ExitMessage)
        {
            Diagnostics.Log.Info("終了の合図を受け取りました (インストール・アンインストール)。");
            // 合図の後すぐに強制終了されることがあるので、まだ書いていない学習データを先に書く
            Composition.LearningSaves.FlushAll();
            // Application.Run の前 (起動時の更新のダイアログを出している間) は、Application.Exit ではダイアログが閉じず終わらない
            if (!Application.MessageLoop) Environment.Exit(0);
            Application.Exit();
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>動いている Meltype に終了の合図を送り、学習データを書いて窓を閉じるまで (最長 5 秒) 待つ。送れたら true。</summary>
    public static bool Send()
    {
        var window = FindWindow(null, Caption);
        if (window == IntPtr.Zero || !PostMessage(window, ExitMessage, IntPtr.Zero, IntPtr.Zero)) return false;
        // インストーラーはこの後すぐに強制終了する
        for (var i = 0; i < 50 && IsWindow(window); i++) Thread.Sleep(100);
        return true;
    }

    public void Dispose() => DestroyHandle();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeFilterStruct);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
