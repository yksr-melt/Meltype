// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

using System.Text.Json.Serialization;

namespace Meltype.Input;

[Flags]
public enum ShortcutModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Win = 8 }

/// <summary>入力モード切り替えの1割り当て。通常キー、または修飾キーと通常キーの組み合わせを指定する。</summary>
public sealed record ToggleShortcut
{
    public int Key { get; init; }
    public ShortcutModifiers Modifiers { get; init; }

    // 半角/全角キー
    public const int Hankaku = 0x19;
    // バッククォートキー(`)
    public const int Grave = 0xC0;

    // JIS の半角/全角キーは、単独押しと Alt との組み合わせで通知されるキーコードが異なる場合がある。
    // 確認した入力は、単独押しで 0xF3 の解放と 0xF4 の押下、Alt との組み合わせで 0x19 の押下。
    // US の Alt+バッククォートも 0x19 になるため、入力の識別には NormalizeShortcutKey で配列情報も使う。
    public static int NormalizeHankakuKey(int vk) => vk is 0xF3 or 0xF4 ? Hankaku : vk;
    /// <summary>US 配列のバッククォートが Alt により VK_KANJI に変わる場合だけ、配列上のキーへ戻す。</summary>
    public static int NormalizeShortcutKey(int vk, int scan, bool extended, int unmodifiedKey) =>
        scan == 0x29 && !extended && unmodifiedKey == Grave && NormalizeHankakuKey(vk) == Hankaku
            ? Grave : NormalizeHankakuKey(vk);

    public static bool IsModifier(int vk) => VirtualKeys.IsModifier(vk);
    public static ShortcutModifiers ModifierOf(int vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => ShortcutModifiers.Shift,
        0x11 or 0xA2 or 0xA3 => ShortcutModifiers.Control,
        0x12 or 0xA4 or 0xA5 => ShortcutModifiers.Alt,
        0x5B or 0x5C => ShortcutModifiers.Win,
        _ => ShortcutModifiers.None,
    };

    public bool Matches(int vk, ShortcutModifiers mods) =>
        !IsModifier(Key) && Modifiers == mods && NormalizeHankakuKey(vk) == NormalizeHankakuKey(Key);

    /// <summary>
    /// ショートカット割り当てとして有効かどうかの基本検証。
    /// - 1〜255 の有効な仮想キーコードであること (IME 予約キー 0xE5 やダミーキー 0xE7/0xE8/0xFF を除く)
    /// - 修飾キーフラグが 0〜15 の範囲内であること (Shift=1, Ctrl=2, Alt=4, Win=8)
    /// - 通常キー自身が修飾キー (Shift/Ctrl/Alt/Win) 単独でないこと
    /// - Meltype 全体の有効/無効の切り替えに予約されている Ctrl + 半角/全角 でないこと
    /// </summary>
    [JsonIgnore]
    public bool IsValid => Key is > 0 and < 256 && Key is not (0xE5 or 0xE7 or 0xE8 or 0xFF) &&
        ((int)Modifiers & ~15) == 0 &&
        !IsModifier(Key) &&
        !(NormalizeHankakuKey(Key) == Hankaku && (Modifiers & ShortcutModifiers.Control) != 0);

    public bool Overlaps(ToggleShortcut other) => Modifiers == other.Modifiers &&
        (Matches(other.Key, other.Modifiers) || other.Matches(Key, Modifiers));

    // デフォルトの切り替えキー
    public static List<ToggleShortcut> Defaults() =>
    [
        new() { Key = Hankaku },
        new() { Key = Grave, Modifiers = ShortcutModifiers.Alt },
    ];
}
