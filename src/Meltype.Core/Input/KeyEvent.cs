// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Input;

/// <summary>
/// 低レベルフックで受け取った 1 打鍵。
/// Injected は他のソフトが SendInput したもの (Meltype 自身の再入力はここまで届かない)。
/// </summary>
public readonly record struct KeyEvent(int Vk, int Scan, bool Extended, bool IsUp, bool Injected, long TimeMs)
{
    public bool IsDown => !IsUp;
    public override string ToString() => $"{(IsUp ? "↑" : "↓")}{Vk:X2}";
}

internal static class VirtualKeys
{
    public const int Back = 0x08, Tab = 0x09, Return = 0x0D, Escape = 0x1B, Space = 0x20;
    public const int Shift = 0x10, Control = 0x11, Menu = 0x12, LShift = 0xA0, RShift = 0xA1,
        LControl = 0xA2, RControl = 0xA3, LMenu = 0xA4, RMenu = 0xA5, LWin = 0x5B, RWin = 0x5C;
    public const int Kana = 0x15, ImeOn = 0x16, Kanji = 0x19, ImeOff = 0x1A, Convert = 0x1C, NonConvert = 0x1D;
    public const int OemAttn = 0xF0, OemFinish = 0xF1, OemCopy = 0xF2, OemAuto = 0xF3, OemEnlw = 0xF4;

    public const int OemComma = 0xBC, OemPeriod = 0xBE, OemMinus = 0xBD, Oem2 = 0xBF, Oem4 = 0xDB, Oem6 = 0xDD;
    public const int Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, F6 = 0x75, F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79;

    public static bool IsLetter(int vk) => vk is >= 0x41 and <= 0x5A;

    /// <summary>半角/全角キー (日本語キーボードでは押すたびに 0xF3 / 0xF4 が交互に来る。0x19 の環境もある)。</summary>
    public static bool IsHankakuZenkaku(int vk) => vk is OemAuto or OemEnlw or Kanji;

    public static char ToLetter(int vk) => (char)('a' + (vk - 0x41));

    public static bool IsModifier(int vk) => vk is Shift or Control or Menu or LShift or RShift or LControl or RControl or LMenu or RMenu or LWin or RWin;

    /// <summary>入力セッションの区切り (設計書 §5)。</summary>
    public static bool IsSessionBoundary(int vk) => vk is Return or Space or Escape or Tab;

    /// <summary>
    /// ユーザーが IME の ON/OFF を手で切り替えたとみなすキー。
    /// 半角/全角 (0xF3/0xF4 と 0x19)、英数 (0xF0)、カタカナひらがな (0xF2)、変換/無変換、IME ON/OFF、かな。
    /// </summary>
    public static bool IsImeToggle(int vk) =>
        vk is Kanji or Kana or ImeOn or ImeOff or Convert or NonConvert or OemAttn or OemCopy or OemAuto or OemEnlw;
}
