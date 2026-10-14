// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

using Meltype.Config;

namespace Meltype.Input;

public enum ShortcutValidationError { None, Invalid, Duplicate, Conversion, FunctionKey, ConvertKeys }

/// <summary>設定画面と設定の読み込みで共通に使う割り当て判定。</summary>
public static class ToggleShortcutValidator
{
    public static ShortcutValidationError Validate(ToggleShortcut b, Settings settings, IEnumerable<ToggleShortcut> others)
    {
        // 基本検証 (キーコード範囲、修飾キー単独の禁止、Ctrl+半角/全角の除外など)
        if (!b.IsValid) return ShortcutValidationError.Invalid;

        // 他の登録ショートカットとの重複
        if (others.Any(other => other.Overlaps(b))) return ShortcutValidationError.Duplicate;

        // Ctrl + H / U / I / O / P (ひらがな・カタカナ・英字変換や BackSpace 操作に使うキー)
        // 0x48: H, 0x55: U, 0x49: I, 0x4F: O, 0x50: P
        if ((b.Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Win)) == ShortcutModifiers.Control && b.Key is 0x48 or 0x55 or 0x49 or 0x4F or 0x50)
            return ShortcutValidationError.Conversion;

        // F6〜F10 (0x75〜0x79) 単独 または Shift + F6〜F10: かな・英字変換用ファンクションキー
        if (b.Key is >= 0x75 and <= 0x79 && (b.Modifiers & ~ShortcutModifiers.Shift) == 0)
            return ShortcutValidationError.FunctionKey;

        // 無変換 (0x1D) / 変換 (0x1C) キー単独
        // 設定「無変換で英数 / 変換で日本語」が有効な場合は競合するため登録不可
        if (b.Modifiers == ShortcutModifiers.None &&
            (settings.ConvertKeysSwitchKeyboard && b.Key is 0x1C or 0x1D))
            return ShortcutValidationError.ConvertKeys;

        return ShortcutValidationError.None;
    }

}
