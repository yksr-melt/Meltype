// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

namespace Meltype.Input;

/// <summary>登録する打鍵を物理キーで追跡する。UI の処理速度や仮想キーの変化に依存しない。</summary>
public sealed class ShortcutRecorder
{
    private readonly Dictionary<int, KeyEvent> _held = [];
    private ToggleShortcut? _candidate;
    public bool Complete { get; private set; }
    public ToggleShortcut? OnKey(KeyEvent e)
    {
        if (Complete || e.Injected) return null;
        // JIS の半角/全角は F3 の解放と F4 の押下で通知される環境がある。
        // Alt と組み合わせた VK_KANJI も解放が来ない場合があるので、修飾キーの解放を待つ。
        if (e.ShortcutKey == ToggleShortcut.Hankaku && e.Scan == 0x29 && !e.Extended)
        {
            if (e.Vk == VirtualKeys.OemEnlw && e.IsDown) return null;
            if (e.IsDown || e.Vk == VirtualKeys.OemAuto && e.IsUp)
            {
                var mods = _held.Values.Aggregate(ShortcutModifiers.None, (value, k) => value | ToggleShortcut.ModifierOf(k.Vk));
                _candidate = new ToggleShortcut { Key = ToggleShortcut.Hankaku, Modifiers = mods };
                if (_held.Count == 0) { Complete = true; return _candidate; }
            }
            return null;
        }
        if (e.IsDown)
        {
            if (!_held.TryAdd(e.PhysicalId, e)) return null;
            if (!ToggleShortcut.IsModifier(e.Vk))
            {
                var mods = _held.Values.Where(k => k.PhysicalId != e.PhysicalId)
                    .Aggregate(ShortcutModifiers.None, (value, k) => value | ToggleShortcut.ModifierOf(k.Vk));
                _candidate = new ToggleShortcut { Key = e.ShortcutKey, Modifiers = mods };
            }
            else _candidate = null;
        }
        else
        {
            if (!_held.Remove(e.PhysicalId)) return null;
            if (_held.Count == 0 && _candidate is not null) { Complete = true; return _candidate; }
        }
        return null;
    }
}
