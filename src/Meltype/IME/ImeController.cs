// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.IME;

public enum InputLanguage { Japanese, Other }

public enum ImeMode { Open, Closed, Unknown }

/// <summary>入力言語と IME の開閉を独立に持つ (設計書 §8)。</summary>
public readonly record struct ImeState(InputLanguage Language, ImeMode Mode, int ConversionMode, IntPtr KeyboardLayout)
{
    public override string ToString() =>
        $"{Language}/{Mode}/conv=0x{ConversionMode:X2}/hkl=0x{KeyboardLayout.ToInt64() & 0xFFFFFFFF:X8}";
}

/// <summary>操作対象のウィンドウ。IME の状態はフォーカスのあるコントロールのスレッドに属する。</summary>
public sealed record ImeTarget(IntPtr TopLevel, IntPtr Focus, uint ThreadId, uint ProcessId)
{
    public IntPtr EffectiveWindow => Focus != IntPtr.Zero ? Focus : TopLevel;

    public static ImeTarget? FromForeground()
    {
        var top = Native.GetForegroundWindow();
        if (top == IntPtr.Zero) return null;
        var thread = Native.GetWindowThreadProcessId(top, out var process);
        var info = new Native.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        var focus = Native.GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : IntPtr.Zero;
        if (focus != IntPtr.Zero) thread = Native.GetWindowThreadProcessId(focus, out process);
        return new ImeTarget(top, focus, thread, process);
    }
}
