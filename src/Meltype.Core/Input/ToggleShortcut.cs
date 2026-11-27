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
    public const int CapsLock = 0x14;

    // JIS の半角/全角キーは、単独押しと Alt との組み合わせで通知されるキーコードが異なる場合がある。
    // 確認した入力は、単独押しで 0xF3 の解放と 0xF4 の押下、Alt との組み合わせで 0x19 の押下。
    // US の Alt+バッククォートも 0x19 になるため、入力の識別には NormalizeShortcutKey で配列情報も使う。
    // カタカナ/ひらがな キーは、Windows が VK_OEM_COPY (0xF2)、IBus・fcitx5・Mac のフロントが VK_KANA (0x15) で送るので、0x15 にそろえる。
    public static int NormalizeKey(int vk) => vk switch
    {
        0xF3 or 0xF4 => Hankaku,
        VirtualKeys.OemCopy => VirtualKeys.Kana,
        _ => vk,
    };
    /// <summary>US 配列のバッククォートが Alt により VK_KANJI に変わる場合だけ、配列上のキーへ戻す。</summary>
    public static int NormalizeShortcutKey(int vk, int scan, bool extended, int unmodifiedKey) =>
        scan == 0x29 && !extended && unmodifiedKey == Grave && NormalizeKey(vk) == Hankaku
            ? Grave : NormalizeKey(vk);

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
        !IsModifier(Key) && Modifiers == mods && NormalizeKey(vk) == NormalizeKey(Key);

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
        !(NormalizeKey(Key) == Hankaku && (Modifiers & ShortcutModifiers.Control) != 0);

    public bool Overlaps(ToggleShortcut other) => Modifiers == other.Modifiers &&
        (Matches(other.Key, other.Modifiers) || other.Matches(Key, Modifiers));

    /// <summary>画面・通知に出す名前 (Ctrl + Alt + F10 など)。</summary>
    public string Describe()
    {
        var names = new List<string>();
        foreach (var (flag, label) in new[] { (ShortcutModifiers.Control, "Ctrl"), (ShortcutModifiers.Alt, "Alt"), (ShortcutModifiers.Shift, "Shift"), (ShortcutModifiers.Win, OperatingSystem.IsMacOS() ? "Command" : "Win") })
        {
            if ((Modifiers & flag) != 0) names.Add(label);
        }
        names.Add(KeyName(Key));
        return string.Join(" + ", names);
    }

    /// <summary>仮想キーコードの名前。</summary>
    public static string KeyName(int key) => key switch
    {
        Hankaku => "半角/全角",
        Grave => "` (バッククォート)",
        CapsLock => "CapsLock",
        VirtualKeys.OemAttn => "英数",
        VirtualKeys.Kana => "カタカナ/ひらがな",
        0x1C => "変換",
        0x1D => "無変換",
        0x20 => "Space",
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
        _ => $"キー 0x{key:X2}",
    };

    /// <summary>設定画面で選べるキー (上の名前を付けたもの)。</summary>
    public static IReadOnlyList<int> SelectableKeys { get; } =
        [Hankaku, Grave, CapsLock, VirtualKeys.OemAttn, VirtualKeys.Kana, 0x1C, 0x1D, 0x20, .. Enumerable.Range(0x41, 26), .. Enumerable.Range(0x30, 10), .. Enumerable.Range(0x70, 24)];

    // デフォルトの切り替えキー
    public static List<ToggleShortcut> Defaults() =>
    [
        new() { Key = Hankaku },
        new() { Key = Grave, Modifiers = ShortcutModifiers.Alt },
    ];
}
