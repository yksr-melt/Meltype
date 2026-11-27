// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Text.Json.Serialization;

namespace Meltype.Input;

/// <summary>
/// 押した物理キーの、OS のキーコード。キーボード配列 (US・Dvorak など) に左右されない。
/// かな配列・SKY 配列は、打った文字でなく物理キーの位置で引く (<see cref="JisPosition"/>)。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Evdev), nameof(Evdev))]
[JsonDerivedType(typeof(Mac), nameof(Mac))]
[JsonDerivedType(typeof(Windows), nameof(Windows))]
public abstract record PhysicalKey
{
    private PhysicalKey() { }

    /// <summary>Linux の evdev のキーコード (IBus の keycode。fcitx5 の Key::code() は XKB のキーコードなので 8 を引く)。</summary>
    public sealed record Evdev(int Code) : PhysicalKey;

    /// <summary>macOS の仮想キーコード (NSEvent.keyCode。kVK_ANSI_A など)。</summary>
    public sealed record Mac(int KeyCode) : PhysicalKey;

    /// <summary>Windows のスキャンコード (set 1) と、拡張キー (0xE0 の付くキー) か。</summary>
    public sealed record Windows(int Scan, bool Extended) : PhysicalKey;

    /// <summary>
    /// 日本語 106/109 キーボードでその位置にあるキーの、Windows の仮想キーコード。
    /// 文字を入力するキー (<see cref="JisKeys.Names"/>) でなければ null。
    /// </summary>
    public int? JisPosition()
    {
        var index = this switch
        {
            Evdev e => Array.IndexOf(EvdevCodes, e.Code),
            Mac m => Array.IndexOf(MacCodes, m.KeyCode),
            Windows { Extended: false } w => Array.IndexOf(WindowsCodes, w.Scan),
            Windows => -1,
            _ => throw new InvalidOperationException(),
        };
        return index >= 0 ? JisKeys.Vks[index] : null;
    }

    // JisKeys.Names の順。evdev と Windows のスキャンコードは、¥ と ろ のほかは同じ値
    private static readonly int[] EvdevCodes =
    [
        2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 124,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27,
        30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 43,
        44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 89,
    ];

    private static readonly int[] WindowsCodes =
    [
        0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x7D,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B,
        0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2B,
        0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x73,
    ];

    // kVK_ANSI_* は US 配列の刻印の名前で、位置は JIS キーボードの同じ場所のキー (kVK_ANSI_LeftBracket が @)。¥ は kVK_JIS_Yen、ろ は kVK_JIS_Underscore
    private static readonly int[] MacCodes =
    [
        0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19, 0x1D, 0x1B, 0x18, 0x5D,
        0x0C, 0x0D, 0x0E, 0x0F, 0x11, 0x10, 0x20, 0x22, 0x1F, 0x23, 0x21, 0x1E,
        0x00, 0x01, 0x02, 0x03, 0x05, 0x04, 0x26, 0x28, 0x25, 0x29, 0x27, 0x2A,
        0x06, 0x07, 0x08, 0x09, 0x0B, 0x2D, 0x2E, 0x2B, 0x2F, 0x2C, 0x5E,
    ];
}

/// <summary>日本語 106/109 キーボードの、文字を入力するキー。</summary>
internal static class JisKeys
{
    /// <summary>刻印 (kana-layouts.txt の打つキーの書き方)。数字の段・Q の段・A の段・Z の段。\ は ¥ キー、_ は ろ キー。</summary>
    public const string Names = "1234567890-^\\qwertyuiop@[asdfghjkl;:]zxcvbnm,./_";

    /// <summary><see cref="Names"/> の順の、Windows の仮想キーコード。</summary>
    public static readonly int[] Vks =
    [
        0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0xBD, 0xDE, 0xDC,
        0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50, 0xC0, 0xDB,
        0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C, 0xBB, 0xBA, 0xDD,
        0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D, 0xBC, 0xBE, 0xBF, 0xE2,
    ];
}
