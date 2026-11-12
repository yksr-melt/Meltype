// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.Input;

/// <summary>
/// Windows のタッチキーボードが出ているか (issue #729)。タッチキーボードで打った文字は、ほかのソフトが送ったキー (LLKHF_INJECTED) として届くので、
/// 出ている間だけ、手で打ったキーとして扱う (設定「遠隔操作などの入力も処理する」を ON にしなくても使えるように)。
/// Windows 10 は IPTip_Main_Window、Windows 11 は TextInputHost の「Microsoft Text Input Application」のウィンドウを見る。
/// 隠れているときも残っているウィンドウなので、見えているか (Windows 11 は DWM で隠されていないか) まで確かめる。
/// </summary>
internal static class TouchKeyboard
{
    public static bool IsShown()
    {
        try
        {
            return Shown(FindWindowW("IPTip_Main_Window", null)) || Shown(FindWindowW("Windows.UI.Core.CoreWindow", "Microsoft Text Input Application"));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>ウィンドウが見えているか: 表示されていて、使えて (Windows 10 は隠すと WS_DISABLED にする)、DWM に隠されていない (cloaked でない)。</summary>
    internal static bool Shown(IntPtr window) =>
        window != IntPtr.Zero && IsWindowVisible(window) && (GetWindowLongW(window, GwlStyle) & WsDisabled) == 0 &&
        (DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) != 0 || cloaked == 0);

    private const int GwlStyle = -16;
    private const int WsDisabled = 0x08000000;
    private const int DwmwaCloaked = 14;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string className, string? windowName);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
