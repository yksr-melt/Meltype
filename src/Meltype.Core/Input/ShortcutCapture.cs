// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

namespace Meltype.Input;

/// <summary>取り込み中に新しく押したキーだけを取り込み、押下と解放を対で止める。</summary>
public sealed class ShortcutCapture
{
    private readonly HashSet<int> _held = [];
    private readonly HashSet<int> _captured = [];
    private bool _hankakuTail;

    /// <summary>通常の入力も含むすべての物理打鍵を渡す。true ならアプリに渡さない。</summary>
    public bool OnKey(KeyEvent e, Action<KeyEvent>? capture)
    {
        if (e.Injected) return false;
        var firstDown = e.IsDown && _held.Add(e.PhysicalId);
        if (e.IsUp) _held.Remove(e.PhysicalId);
        if (_hankakuTail)
        {
            _hankakuTail = false;
            if (e.IsDown && e.Vk == VirtualKeys.OemEnlw && e.Scan == 0x29 && !e.Extended) return true;
        }
        // 取り込み前から押しているキーのリピートを新しい押下として扱うと、解放だけを止めて押したままにしてしまう (#2078)。
        // 終了後のリピートも、取り込んだ押下の解放まで止める。
        if (!_captured.Contains(e.PhysicalId) && !(capture is not null && firstDown)) return false;
        if (e.IsDown) _captured.Add(e.PhysicalId); else _captured.Remove(e.PhysicalId);
        _hankakuTail = e.IsUp && e.Vk == VirtualKeys.OemAuto && e.Scan == 0x29 && !e.Extended;
        capture?.Invoke(e);
        return true;
    }
}
