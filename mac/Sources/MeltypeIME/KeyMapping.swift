// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import Carbon.HIToolbox

/// Mac のキー (キーコード・文字) を、Meltype の本体が使う Windows の仮想キーコードにする。
/// 本体が見るのは「特別なキーか (Enter・矢印・F7 …)」と「英字・数字のキーか」だけで、入力する文字は別に渡す。
enum KeyMapping {
    /// 文字を入力するキーで、特別な扱いのないもの (記号など)。本体では文字だけを見る。
    private static let otherCharacterKey: Int32 = 0x07

    /// ナビゲーションキーの characters は操作を表す特殊コードであり、入力文字ではない。
    static func text(for event: NSEvent, virtualKey: Int32) -> String {
        if (0x21...0x28).contains(virtualKey) { return "" }
        return event.characters ?? ""
    }

    static func virtualKey(for event: NSEvent) -> Int32? {
        switch Int(event.keyCode) {
        case kVK_Return, kVK_ANSI_KeypadEnter: return 0x0D
        case kVK_Tab: return 0x09
        case kVK_Space: return 0x20
        case kVK_Delete: return 0x08            // BackSpace
        case kVK_ForwardDelete: return 0x2E
        case kVK_Escape: return 0x1B
        case kVK_LeftArrow: return 0x25
        case kVK_UpArrow: return 0x26
        case kVK_RightArrow: return 0x27
        case kVK_DownArrow: return 0x28
        case kVK_Home: return 0x24
        case kVK_End: return 0x23
        case kVK_PageUp: return 0x21
        case kVK_PageDown: return 0x22
        case kVK_F6: return 0x75                // ひらがな
        case kVK_F7: return 0x76                // カタカナ
        case kVK_F8: return 0x77
        case kVK_F9: return 0x78                // 全角英数
        case kVK_F10: return 0x79               // 半角英数
        default: break
        }
        guard let scalar = event.charactersIgnoringModifiers?.lowercased().unicodeScalars.first else { return nil }
        switch scalar {
        case "a"..."z": return Int32(scalar.value) - 0x20   // A-Z = 0x41-0x5A
        case "0"..."9": return Int32(scalar.value)          // 0-9 = 0x30-0x39
        case ",": return 0xBC
        case ".": return 0xBE
        case "-": return 0xBD
        case "/": return 0xBF
        case "[": return 0xDB
        case "]": return 0xDD
        default:
            return scalar.value >= 0x20 ? otherCharacterKey : nil
        }
    }
}
