// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.Input;

internal interface IKeyInjector
{
    /// <summary>打鍵を元の順序のまま 1 回の SendInput でまとめて送る (物理入力が間に割り込まない)。</summary>
    bool Inject(IReadOnlyList<KeyEvent> events);
}

/// <summary>
/// 保留していた打鍵の再入力 (設計書 §7)。Unicode 文字ではなく元の仮想キー + スキャンコードとして送るので、
/// 日本語 IME に切り替えた後は既存 IME がふつうの打鍵として処理する (変換は IME の仕事)。
/// </summary>
internal sealed class KeyInjector : IKeyInjector
{
    public bool Inject(IReadOnlyList<KeyEvent> events)
    {
        if (events.Count == 0) return true;
        var inputs = new Native.INPUT[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var flags = 0u;
            if (e.Extended) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            if (e.IsUp) flags |= Native.KEYEVENTF_KEYUP;
            inputs[i] = new Native.INPUT
            {
                type = Native.INPUT_KEYBOARD,
                u = new Native.InputUnion
                {
                    ki = new Native.KEYBDINPUT
                    {
                        wVk = (ushort)e.Vk,
                        wScan = (ushort)e.Scan,
                        dwFlags = flags,
                        dwExtraInfo = KeyboardMonitor.InjectedMarker,
                    },
                },
            };
        }
        return Native.SendAll(inputs, "再入力");
    }

    /// <summary>修飾キーと一緒にキーを送る (Ctrl+C など)。</summary>
    public static void SendShortcut(int modifier, int vk)
    {
        new KeyInjector().Inject(
        [
            new KeyEvent(modifier, 0, false, false, false, 0),
            new KeyEvent(vk, 0, false, false, false, 0),
            new KeyEvent(vk, 0, false, true, false, 0),
            new KeyEvent(modifier, 0, false, true, false, 0),
        ]);
    }

    public static void SendKey(int vk)
    {
        new KeyInjector().Inject(
        [
            new KeyEvent(vk, 0, false, false, false, 0),
            new KeyEvent(vk, 0, false, true, false, 0),
        ]);
    }
}
