// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

using Meltype.Config;

namespace Meltype.Input;

public enum ShortcutValidationError { None, Invalid, Duplicate, Conversion, FunctionKey, UnmodifiedKey, ControlKeyBinding, ShiftSpaceHalfWidth }

/// <summary>設定画面と設定の読み込みで共通に使う割り当て判定。</summary>
public static class ToggleShortcutValidator
{
    public static ShortcutValidationError Validate(ToggleShortcut b, Settings settings, IEnumerable<ToggleShortcut> others)
    {
        // 基本検証 (キーコード範囲、修飾キー単独の禁止、Ctrl+半角/全角の除外など)
        if (!b.IsValid) return ShortcutValidationError.Invalid;

        // 他の登録ショートカットとの重複
        if (others.Any(other => other.Overlaps(b))) return ShortcutValidationError.Duplicate;

        // 入力中に Control + 英字へ割り当てた操作は、Shift の有無によらず優先する。
        if ((b.Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Win)) == ShortcutModifiers.Control
            && ControlKeyBindingParser.Parse(settings.ControlKeyBindings).ContainsKey(b.Key))
            return ShortcutValidationError.ControlKeyBinding;

        // Ctrl + H / U / I / O / P (ひらがな・カタカナ・英字変換や BackSpace 操作に使うキー)
        // 0x48: H, 0x55: U, 0x49: I, 0x4F: O, 0x50: P
        if ((b.Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Win)) == ShortcutModifiers.Control && b.Key is 0x48 or 0x55 or 0x49 or 0x4F or 0x50)
            return ShortcutValidationError.Conversion;

        // F6〜F10 (0x75〜0x79) 単独 または Shift + F6〜F10: かな・英字変換用ファンクションキー
        if (b.Key is >= 0x75 and <= 0x79 && (b.Modifiers & ~ShortcutModifiers.Shift) == 0)
            return ShortcutValidationError.FunctionKey;

        // 通常入力を奪わないよう、Ctrl・Alt・Win を伴わないキーは IME 用キーと CapsLock だけに絞る。
        // US キーボードには IME 用キーが無いので、CapsLock と、PowerToys などで CapsLock から割り当て直す
        // カタカナ/ひらがな を 1 キーの切り替えに使えるようにする。
        // Shift だけの英字・記号も文字入力に使うので、単独押しと同じ規則にする。
        // Shift+Space だけは例外にする (#1033)。日本語入力中の Shift+Space は、全角スペースではなく
        // 日本語 / 英数の切り替えに使いたいという要望があり、Space は文字入力に使わないため。
        var shiftSpace = b.Modifiers == ShortcutModifiers.Shift && b.Key == VirtualKeys.Space;
        if (!shiftSpace && (b.Modifiers & ~ShortcutModifiers.Shift) == ShortcutModifiers.None &&
            ToggleShortcut.NormalizeKey(b.Key) is not (ToggleShortcut.Hankaku or ToggleShortcut.CapsLock or VirtualKeys.Kana or 0x1C or 0x1D))
            return ShortcutValidationError.UnmodifiedKey;

        // 設定「スペースの幅」が「半角」のときは、Shift+Space を切り替えに使うと競合する。
        // 先に設定を変えてもらう。
        if (shiftSpace && settings.SpaceWidth == SpaceWidth.HalfWidth)
            return ShortcutValidationError.ShiftSpaceHalfWidth;

        return ShortcutValidationError.None;
    }

}
