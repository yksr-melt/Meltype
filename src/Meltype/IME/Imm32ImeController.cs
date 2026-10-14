// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;

namespace Meltype.IME;

/// <summary>
/// IMM32 経由の IME 操作。ImmGetContext は別プロセスのウィンドウには使えないので、
/// 対象スレッドの既定 IME ウィンドウに WM_IME_CONTROL を送る方式を取る (TSF ベースの MS-IME でも有効)。
/// </summary>
public sealed class Imm32ImeController
{
    private const int IMC_GETCONVERSIONMODE = 0x0001, IMC_SETCONVERSIONMODE = 0x0002;
    private const int IMC_GETOPENSTATUS = 0x0005, IMC_SETOPENSTATUS = 0x0006;
    private const int IMC_GETSENTENCEMODE = 0x0003;
    private const ushort LangJapanese = 0x0411;
    private const uint QueryTimeoutMs = 100;

    public ImeState GetState(ImeTarget target)
    {
        var hkl = Native.GetKeyboardLayout(target.ThreadId);
        var language = (hkl.ToInt64() & 0xFFFF) == LangJapanese ? InputLanguage.Japanese : InputLanguage.Other;
        var imeWindow = Native.ImmGetDefaultIMEWnd(target.EffectiveWindow);
        if (imeWindow == IntPtr.Zero) return new ImeState(language, ImeMode.Unknown, 0, hkl);

        if (!TrySend(imeWindow, IMC_GETOPENSTATUS, IntPtr.Zero, QueryTimeoutMs, out var open))
            return new ImeState(language, ImeMode.Unknown, 0, hkl);
        TrySend(imeWindow, IMC_GETCONVERSIONMODE, IntPtr.Zero, QueryTimeoutMs, out var conversion);
        return new ImeState(language, open != IntPtr.Zero ? ImeMode.Open : ImeMode.Closed, unchecked((int)conversion.ToInt64()), hkl);
    }

    /// <summary>Affinity の入力判定用。読み取りの待ち時間を全体で timeoutMs に制限する。</summary>
    internal static bool HasInputContext(IntPtr window, uint timeoutMs)
    {
        var ime = Native.ImmGetDefaultIMEWnd(window);
        var started = Stopwatch.GetTimestamp();
        return ime != IntPtr.Zero && HasInputContext(timeoutMs, (command, remaining) =>
        {
            var ok = TrySend(ime, command, IntPtr.Zero, remaining, out var value);
            return (ok, unchecked((int)value.ToInt64()));
        }, () => Stopwatch.GetElapsedTime(started));
    }

    internal static bool HasInputContext(uint timeoutMs, Func<int, uint, (bool Success, int Value)> read, Func<TimeSpan> clock)
    {
        var deadline = clock() + TimeSpan.FromMilliseconds(timeoutMs);
        foreach (var command in new[] { IMC_GETCONVERSIONMODE, IMC_GETSENTENCEMODE, IMC_GETOPENSTATUS })
        {
            var remaining = (deadline - clock()).TotalMilliseconds;
            // 整数 ms に切り捨てて残りの予算を超えないようにし、0ms では問い合わせない。
            if (remaining < 1) return false;
            var result = read(command, (uint)remaining);
            if (!result.Success || result.Value < 0 || clock() > deadline) return false;
            if (command == IMC_GETOPENSTATUS ? result.Value == 1 : result.Value > 0) return true;
        }
        return false;
    }

    public bool TrySetOpen(ImeTarget target, bool open, int? conversionMode, int timeoutMs)
    {
        var imeWindow = Native.ImmGetDefaultIMEWnd(target.EffectiveWindow);
        if (imeWindow == IntPtr.Zero) return false;
        if (!TrySend(imeWindow, IMC_SETOPENSTATUS, open ? new IntPtr(1) : IntPtr.Zero, (uint)timeoutMs, out _)) return false;
        if (open && conversionMode is { } mode)
        {
            TrySend(imeWindow, IMC_SETCONVERSIONMODE, new IntPtr(mode), (uint)timeoutMs, out _);
        }
        return WaitFor(() =>
        {
            if (!TrySend(imeWindow, IMC_GETOPENSTATUS, IntPtr.Zero, QueryTimeoutMs, out var current)) return false;
            return (current != IntPtr.Zero) == open;
        }, timeoutMs);
    }

    private static bool TrySend(IntPtr imeWindow, int command, IntPtr value, uint timeoutMs, out IntPtr result) =>
        Native.SendMessageTimeout(imeWindow, Native.WM_IME_CONTROL, new IntPtr(command), value, Native.SMTO_ABORTIFHUNG, timeoutMs, out result) != IntPtr.Zero;

    private static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            if (condition()) return true;
            if (Environment.TickCount64 >= deadline) return false;
            Thread.Sleep(10);
        }
    }
}
