// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;
using Meltype.Config;

namespace Meltype;

/// <summary>
/// 動いている Meltype に、ほかのプロセスから合図を送る: 終了 (Meltype.exe --exit。インストール・アンインストールの前に使う) と、
/// 画面を開く (Meltype.exe --open。設定アプリが使う)。
/// 管理者として動いている Meltype は、ふつうの権限のプロセスからは止められない (Stop-Process が拒否される) ので、
/// 見えない窓で合図を受け取り、ふつうの権限からの合図も受け付ける (ChangeWindowMessageFilterEx)。
/// </summary>
internal sealed class AppSignal : NativeWindow, IDisposable
{
    // 前の版の Meltype.exe --exit (インストーラーが実行する) と、動いている前の版の Meltype が、互いに窓を見つけられるように名前を変えない。
    // 前の版は画面を開く合図を知らないので、受け取っても何もしない
    private const string Caption = "Meltype.ExitSignal";
    private const uint MSGFLT_ALLOW = 1;
    private static readonly uint ExitMessage = RegisterWindowMessage("Meltype.Exit");
    private static readonly uint OpenMessage = RegisterWindowMessage("Meltype.Open");

    /// <summary>画面を開く合図が届いた (UI スレッド)。</summary>
    public event Action<AppWindow>? OpenRequested;

    public AppSignal()
    {
        CreateHandle(new CreateParams { Caption = Caption });
        // ふつうの権限からの合図を受け付けるのは、管理者として動いているときだけ。ふつうの権限で動いているときは、
        // 送り手 (インストーラーなど) からはそのまま届く。広げると、サンドボックス (整合性レベル Low) のプロセスからも終了させられてしまう。
        if (!Input.ForegroundTracker.SelfElevated) return;
        ChangeWindowMessageFilterEx(Handle, ExitMessage, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(Handle, OpenMessage, MSGFLT_ALLOW, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        if (ExitMessage != 0 && m.Msg == (int)ExitMessage)
        {
            Diagnostics.Log.Info("終了の合図を受け取りました (インストール・アンインストール)。");
            // 合図の後すぐに強制終了されることがあるので、まだ書いていない学習データを先に書く
            Config.LearningSaves.FlushAll();
            Application.Exit();
            return;
        }
        // 値はほかのプロセスが決めるので、知らない値は無視する
        if (OpenMessage != 0 && m.Msg == (int)OpenMessage)
        {
            var window = (AppWindow)(int)m.WParam;
            if (Enum.IsDefined(window)) OpenRequested?.Invoke(window);
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>動いている Meltype に終了の合図を送り、学習データを書いて窓を閉じるまで (最長 5 秒) 待つ。送れたら true。</summary>
    public static bool SendExit()
    {
        var window = FindWindow(null, Caption);
        if (window == IntPtr.Zero || !PostMessage(window, ExitMessage, IntPtr.Zero, IntPtr.Zero)) return false;
        // インストーラーはこの後すぐに強制終了する
        for (var i = 0; i < 50 && IsWindow(window); i++) Thread.Sleep(100);
        return true;
    }

    /// <summary>--open の引数の画面の名前 (<see cref="AppWindow"/> の名前と完全に一致するものだけ。数字・大文字小文字の違いは受け取らない)。</summary>
    public static AppWindow? ParseWindow(string? name) => Enum.GetNames<AppWindow>().Contains(name) ? Enum.Parse<AppWindow>(name!) : null;

    /// <summary>動いている Meltype に画面を開く合図を送る。送れたら true (動いていなければ false)。</summary>
    public static bool SendOpen(AppWindow open)
    {
        var window = FindWindow(null, Caption);
        if (window == IntPtr.Zero) return false;
        // 押した設定アプリの後ろに開かないように、このプロセス (前面の設定アプリが起動した) が持つ、前面の窓を変える権利を渡す
        _ = GetWindowThreadProcessId(window, out var process);
        AllowSetForegroundWindow(process);
        return PostMessage(window, OpenMessage, (int)open, IntPtr.Zero);
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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
