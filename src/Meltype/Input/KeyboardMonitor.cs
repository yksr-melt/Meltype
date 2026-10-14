// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.Input;

/// <summary>
/// キー入力監視 (設計書 §11)。WH_KEYBOARD_LL / WH_MOUSE_LL / WinEvent を専用スレッドで受ける。
///
/// フックの呼び出しは一定時間 (レジストリの LowLevelHooksTimeout) 内に返らないと Windows に飛ばされ、打鍵が保留を追い越してしまう。
/// Windows 7 以降は、そのままフックも通知なしに外される。
/// そのためこのスレッドは自前のメッセージループだけを回し、IME 操作や SendInput など
/// 待ちが発生しうる処理は行わない (それらは再入力ワーカーの仕事)。
/// 例外は打ち始めの判定で、フォーカスを調べ終わるのを合わせて 150ms ほどまで待つ (FocusInspector.CanCaptureWaiting・RecheckStale)。
/// LowLevelHooksTimeout の値が無いときは 300ms ほどとされ (Microsoft のドキュメントには書かれていない)、これより短い。
/// </summary>
internal sealed class KeyboardMonitor : IDisposable
{
    /// <summary>Meltype 自身が SendInput した打鍵に付ける印 ("MELT")。InjectedInput と PhysicalInput の区別に使う (設計書 §7)。</summary>
    public static readonly UIntPtr InjectedMarker = unchecked((UIntPtr)0x4D454C54u);

    private readonly Func<KeyEvent, bool> _onKey;
    private readonly Func<Composition.MouseButtonEvent, bool> _onMouseButton;
    private readonly Action<IntPtr> _onForegroundChanged;
    private readonly Action _onFocusChanged;
    private readonly Native.HookProc _keyboardProc;
    private readonly Native.HookProc _mouseProc;
    private readonly Native.WinEventProc _winEventProc;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private Exception? _startError;
    private uint _threadId;
    private IntPtr _keyboardHook, _mouseHook, _foregroundHook, _focusHook;

    public KeyboardMonitor(Func<KeyEvent, bool> onKey, Func<Composition.MouseButtonEvent, bool> onMouseButton, Action<IntPtr> onForegroundChanged, Action onFocusChanged)
    {
        _onKey = onKey;
        _onMouseButton = onMouseButton;
        _onForegroundChanged = onForegroundChanged;
        _onFocusChanged = onFocusChanged;
        // デリゲートを GC されないようフィールドで保持する。
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
        _winEventProc = WinEventCallback;
        _thread = new Thread(Run) { IsBackground = true, Name = "Meltype keyboard hook", Priority = ThreadPriority.Highest };
    }

    public void Start()
    {
        _thread.Start();
        _started.Wait();
        if (_startError is not null) throw _startError;
    }

    private void Run()
    {
        try
        {
            _threadId = Native.GetCurrentThreadId();
            var module = Native.GetModuleHandle(null);
            _keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyboardProc, module, 0);
            if (_keyboardHook == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "キーボードフックを設定できませんでした。");
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, module, 0);
            _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            _focusHook = Native.SetWinEventHook(Native.EVENT_OBJECT_FOCUS, Native.EVENT_OBJECT_FOCUS, IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }
        catch (Exception ex)
        {
            _startError = ex;
            Unhook();
            _started.Set();
            return;
        }
        _started.Set();

        while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessage(ref msg);
        }
        Unhook();
    }

    private IntPtr KeyboardCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var message = unchecked((int)wParam.ToInt64());
                if (message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN or Native.WM_KEYUP or Native.WM_SYSKEYUP)
                {
                    var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                    // 自分が再入力した打鍵は判定対象から除外する (ループ防止)。
                    if (data.dwExtraInfo != InjectedMarker)
                    {
                        var e = new KeyEvent(
                            Vk: (int)data.vkCode,
                            Scan: (int)data.scanCode,
                            Extended: (data.flags & Native.LLKHF_EXTENDED) != 0,
                            IsUp: (data.flags & Native.LLKHF_UP) != 0,
                            Injected: (data.flags & Native.LLKHF_INJECTED) != 0,
                            TimeMs: Environment.TickCount64);
                        if (e.Scan == 0x29 && !e.Extended)
                        {
                            var thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
                            e = e with { UnmodifiedKey = (int)Native.MapVirtualKeyEx(0x29, 3, Native.GetKeyboardLayout(thread)) };
                        }
                        if (_onKey(e)) return new IntPtr(1);
                    }
                }
            }
            catch (Exception ex)
            {
                // ネイティブのフックに例外を伝播させない。素通しにして入力を失わないようにする。
                Diagnostics.Log.Error($"キーボードフックで例外: {ex.Message}");
            }
        }
        return Native.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = unchecked((int)wParam.ToInt64());
            if (message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN
                or Native.WM_LBUTTONUP or Native.WM_RBUTTONUP or Native.WM_MBUTTONUP or Native.WM_XBUTTONUP)
            {
                try
                {
                    var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                    if (data.dwExtraInfo != InjectedMarker &&
                        _onMouseButton(new Composition.MouseButtonEvent(message, data.x, data.y, data.mouseData)))
                    {
                        return new IntPtr(1);
                    }
                }
                catch (Exception ex) { Diagnostics.Log.Error($"マウスフックで例外: {ex.Message}"); }
            }
        }
        return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void WinEventCallback(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (eventType == Native.EVENT_SYSTEM_FOREGROUND) _onForegroundChanged(hwnd);
            else if (eventType == Native.EVENT_OBJECT_FOCUS) _onFocusChanged();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"WinEvent で例外: {ex.Message}");
        }
    }

    private void Unhook()
    {
        if (_keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_foregroundHook != IntPtr.Zero) { Native.UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
        if (_focusHook != IntPtr.Zero) { Native.UnhookWinEvent(_focusHook); _focusHook = IntPtr.Zero; }
    }

    public void Dispose()
    {
        if (_threadId != 0 && _thread.IsAlive)
        {
            Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(2000);
        }
        _started.Dispose();
    }
}
