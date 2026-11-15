// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import Carbon.HIToolbox

/// Mac の文字の無いキー (Enter・矢印・F7・英数・かな …) を、Meltype の本体が使う Windows の仮想キーコードにする。
/// 文字を入力するキーは文字だけを渡す (仮想キーコードは本体が文字から決める)。
enum KeyMapping {
    /// 操作キーの characters は操作を表す特殊コードであり、入力文字ではない。
    /// Ctrl を押したキーの characters は制御文字 (Ctrl+H は 0x08) なので、キーの文字 (h) を渡す (fcitx5・IBus の keysym と同じ)。
    static func text(for event: NSEvent) -> String {
        if operationVirtualKey(for: event.keyCode) != nil { return "" }
        if event.modifierFlags.contains(.control) { return event.charactersIgnoringModifiers ?? "" }
        return event.characters ?? ""
    }

    /// 操作キーの対応表。ここに追加したキーは必ず文字なしで本体に渡す。
    private static func operationVirtualKey(for keyCode: UInt16) -> Int32? {
        switch Int(keyCode) {
        case kVK_Return, kVK_ANSI_KeypadEnter: return 0x0D
        case kVK_Tab: return 0x09
        case kVK_Delete: return 0x08            // BackSpace
        case kVK_ForwardDelete: return 0x2E
        case kVK_ANSI_KeypadClear: return 0x0C
        case kVK_Escape: return 0x1B
        case kVK_LeftArrow: return 0x25
        case kVK_UpArrow: return 0x26
        case kVK_RightArrow: return 0x27
        case kVK_DownArrow: return 0x28
        case kVK_Home: return 0x24
        case kVK_End: return 0x23
        case kVK_PageUp: return 0x21
        case kVK_PageDown: return 0x22
        case kVK_F1: return 0x70
        case kVK_F2: return 0x71
        case kVK_F3: return 0x72
        case kVK_F4: return 0x73
        case kVK_F5: return 0x74
        case kVK_F6: return 0x75
        case kVK_F7: return 0x76
        case kVK_F8: return 0x77
        case kVK_F9: return 0x78
        case kVK_F10: return 0x79
        case kVK_F11: return 0x7A
        case kVK_F12: return 0x7B
        case kVK_F13: return 0x7C
        case kVK_F14: return 0x7D
        case kVK_F15: return 0x7E
        case kVK_F16: return 0x7F
        case kVK_F17: return 0x80
        case kVK_F18: return 0x81
        case kVK_F19: return 0x82
        case kVK_F20: return 0x83
        // 入力モードのキー (意味は本体が決める)
        case kVK_JIS_Eisu: return 0xF0
        case kVK_JIS_Kana: return 0x15
        default: return nil
        }
    }

    /// テンキーの数字・記号の仮想キーコード (Windows の VK_NUMPAD0〜9・VK_MULTIPLY など)。文字と一緒に渡す。
    /// 本体は、何も打っていないときのテンキーをそのまま通す (上の段の数字とは分ける: issue #460)。
    private static func keypadVirtualKey(for keyCode: UInt16) -> Int32? {
        switch Int(keyCode) {
        case kVK_ANSI_Keypad0: return 0x60
        case kVK_ANSI_Keypad1: return 0x61
        case kVK_ANSI_Keypad2: return 0x62
        case kVK_ANSI_Keypad3: return 0x63
        case kVK_ANSI_Keypad4: return 0x64
        case kVK_ANSI_Keypad5: return 0x65
        case kVK_ANSI_Keypad6: return 0x66
        case kVK_ANSI_Keypad7: return 0x67
        case kVK_ANSI_Keypad8: return 0x68
        case kVK_ANSI_Keypad9: return 0x69
        case kVK_ANSI_KeypadMultiply: return 0x6A
        case kVK_ANSI_KeypadPlus: return 0x6B
        case kVK_ANSI_KeypadMinus: return 0x6D
        case kVK_ANSI_KeypadDecimal: return 0x6E
        case kVK_ANSI_KeypadDivide: return 0x6F
        default: return nil
        }
    }

    /// 文字の無いキーと、テンキーの数字・記号の仮想キーコード。そのほかの文字を入力するキーなら 0。
    static func virtualKey(for event: NSEvent) -> Int32 {
        if let key = operationVirtualKey(for: event.keyCode) { return key }
        if let key = keypadVirtualKey(for: event.keyCode) { return key }
        return Int(event.keyCode) == kVK_Space ? 0x20 : 0
    }
}

