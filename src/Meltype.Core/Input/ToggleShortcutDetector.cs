// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

namespace Meltype.Input;

/// <summary>押下の繰り返し、キー解放を追跡する。OS に依存しない。</summary>
public sealed class ToggleShortcutDetector
{
    // 押下済みの物理キー。仮想キーコードが変わっても、長押しの繰り返しを同じキーとして扱う。
    private readonly HashSet<int> _held = [];
    // 押下を飲み込んだキー。後続のリピートと解放も飲み込み、アプリへ片方だけ渡さない。
    private readonly HashSet<int> _consumed = [];
    // 解放通知が来ない JIS 半角/全角キーと、押したときの修飾キー。未登録の操作も追跡する。
    private readonly Dictionary<int, ShortcutModifiers> _jisModifiers = [];

    /// <summary>Consume はフックで入力を飲み込むか、Toggle は入力モードの切り替えを実行するか。</summary>
    public readonly record struct Result(bool Consume, bool Toggle);

    /// <summary>配列に基づくキー識別と修飾キーが登録内容に一致した、最初の押下だけで切り替える。</summary>
    public Result OnKey(KeyEvent e, ShortcutModifiers modifiers, IReadOnlyList<ToggleShortcut> bindings)
    {
        // 外部からの再入力は状態を更新しない (許可する入力は呼び出し側で Injected を解除する)。
        if (e.Injected) return default;
        if (e.IsUp) return new(ReleaseConsumed(e), false);
        var first = _held.Add(e.PhysicalId);
        // JIS の VK_KANJI は解放が来ない場合がある。修飾キーを離した後の押下は新しい操作。
        if (first && _jisModifiers.Remove(e.PhysicalId)) _consumed.Remove(e.PhysicalId);
        // 消費済みのキーは、押している間に設定や修飾キーが変わっても再判定しない。
        if (_consumed.Contains(e.PhysicalId)) return new(true, false);
        if (!first) return default;
        if (ToggleShortcut.IsModifier(e.Vk)) return default;
        // 未登録でも記録し、後で登録されたときに「押しっぱなし」の状態が残らないようにする。
        // US の Alt+バッククォートも VK_KANJI になるので、配列で識別した ShortcutKey も確認する。
        if (e.ShortcutKey == ToggleShortcut.Hankaku && e.Vk == VirtualKeys.Kanji && e.Scan == 0x29 && !e.Extended && modifiers != ShortcutModifiers.None)
            _jisModifiers[e.PhysicalId] = modifiers;
        if (!bindings.Any(b => b.Matches(e.ShortcutKey, modifiers))) return default;
        _consumed.Add(e.PhysicalId);
        return new(true, true);
    }

    /// <summary>登録内容を再判定せず、消費した押下に対応する解放を飲み込む。動作モードや入力言語の変更後も呼ぶ。</summary>
    public bool ReleaseConsumed(KeyEvent e)
    {
        if (!e.IsUp || e.Injected) return false;
        // JIS 主キーの解放が来なくても、組み合わせた修飾キーの解放で次の押下を受け付ける。
        // 消費フラグは残し、遅れて主キーの解放が届いた場合も飲み込めるようにする。
        var released = ToggleShortcut.ModifierOf(e.Vk);
        if (released != ShortcutModifiers.None)
            foreach (var (key, modifiers) in _jisModifiers)
                if ((modifiers & released) != 0) _held.Remove(key);
        _jisModifiers.Remove(e.PhysicalId);
        _held.Remove(e.PhysicalId);
        return _consumed.Remove(e.PhysicalId);
    }
}
