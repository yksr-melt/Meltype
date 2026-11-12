// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Input;

/// <summary>打鍵が入力する文字 (前面アプリのキーボード配列で)。フックのスレッドからも呼べる。</summary>
internal static class KeyText
{
    /// <summary>その打鍵で入力される文字。文字を生まないキーなら null。</summary>
    public static char? CharFromKey(int vk, int scan, bool shift)
    {
        shift |= (Native.GetAsyncKeyState(VirtualKeys.Shift) & 0x8000) != 0;
        var state = new byte[256];
        if (shift) state[VirtualKeys.Shift] = state[VirtualKeys.LShift] = 0x80;
        var foreground = Native.GetForegroundWindow();
        var layout = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(foreground, out _));
        var buffer = new char[8];
        // flags 0x4: キーボードの状態 (デッドキー) を変更しない (Windows 10 1607 以降)。
        var count = Native.ToUnicodeEx((uint)vk, (uint)scan, state, buffer, buffer.Length, 0x4, layout);
        return count == 1 && !char.IsControl(buffer[0]) ? buffer[0] : null;
    }

    /// <summary>ウィンドウのタイトル (メッセージを送らないので、応答しないアプリでも止まらない)。</summary>
    public static string WindowTitle(IntPtr window)
    {
        if (window == IntPtr.Zero) return "";
        var buffer = new char[512];
        var length = Native.InternalGetWindowText(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "";
    }
}
