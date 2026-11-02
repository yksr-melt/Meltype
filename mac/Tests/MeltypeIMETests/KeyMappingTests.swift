// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import AppKit
import Carbon.HIToolbox
import XCTest
@testable import MeltypeIME

final class KeyMappingTests: XCTestCase {
    private func event(_ keyCode: Int, _ text: String, flags: NSEvent.ModifierFlags = []) -> NSEvent {
        NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: flags, timestamp: 0,
                         windowNumber: 0, context: nil, characters: text, charactersIgnoringModifiers: text,
                         isARepeat: false, keyCode: UInt16(keyCode))!
    }

    func testNavigationKeysPassNoTextToCore() {
        let cases: [(Int, Int, Int32)] = [
            (kVK_PageUp, NSPageUpFunctionKey, 0x21), (kVK_PageDown, NSPageDownFunctionKey, 0x22),
            (kVK_End, NSEndFunctionKey, 0x23), (kVK_Home, NSHomeFunctionKey, 0x24),
            (kVK_LeftArrow, NSLeftArrowFunctionKey, 0x25), (kVK_UpArrow, NSUpArrowFunctionKey, 0x26),
            (kVK_RightArrow, NSRightArrowFunctionKey, 0x27), (kVK_DownArrow, NSDownArrowFunctionKey, 0x28),
        ]
        for (keyCode, scalar, expectedKey) in cases {
            for flags: NSEvent.ModifierFlags in [[], .command, .shift] {
                let input = event(keyCode, String(UnicodeScalar(scalar)!), flags: flags)
                let vk = KeyMapping.virtualKey(for: input)!
                XCTAssertEqual(vk, expectedKey)
                XCTAssertEqual(KeyMapping.text(for: input), "")
            }
        }
    }

    func testDeleteClearAndFunctionKeysPassNoTextToCore() {
        let cases: [(Int, Int, Int32)] = [
            (kVK_ForwardDelete, NSDeleteFunctionKey, 0x2E),
            (kVK_ANSI_KeypadClear, NSClearLineFunctionKey, 0x0C),
            (kVK_F1, NSF1FunctionKey, 0x70),
            (kVK_F2, NSF2FunctionKey, 0x71),
            (kVK_F3, NSF3FunctionKey, 0x72),
            (kVK_F4, NSF4FunctionKey, 0x73),
            (kVK_F5, NSF5FunctionKey, 0x74),
            (kVK_F6, NSF6FunctionKey, 0x75),
            (kVK_F7, NSF7FunctionKey, 0x76),
            (kVK_F8, NSF8FunctionKey, 0x77),
            (kVK_F9, NSF9FunctionKey, 0x78),
            (kVK_F10, NSF10FunctionKey, 0x79),
            (kVK_F11, NSF11FunctionKey, 0x7A),
            (kVK_F12, NSF12FunctionKey, 0x7B),
            (kVK_F13, NSF13FunctionKey, 0x7C),
            (kVK_F14, NSF14FunctionKey, 0x7D),
            (kVK_F15, NSF15FunctionKey, 0x7E),
            (kVK_F16, NSF16FunctionKey, 0x7F),
            (kVK_F17, NSF17FunctionKey, 0x80),
            (kVK_F18, NSF18FunctionKey, 0x81),
            (kVK_F19, NSF19FunctionKey, 0x82),
            (kVK_F20, NSF20FunctionKey, 0x83),
        ]
        for (keyCode, scalar, expectedKey) in cases {
            for flags: NSEvent.ModifierFlags in [[], .command, .shift, .control, .option] {
                let input = event(keyCode, String(UnicodeScalar(scalar)!), flags: flags)
                let vk = KeyMapping.virtualKey(for: input)!
                XCTAssertEqual(vk, expectedKey)
                XCTAssertEqual(KeyMapping.text(for: input), "")
            }
        }
    }

    func testTextAndSpaceArePreserved() {
        for (keyCode, text) in [(kVK_ANSI_A, "a"), (kVK_ANSI_E, "e\u{301}"), (kVK_Space, " "), (kVK_ANSI_A, ""), (kVK_ANSI_A, "\u{E000}"), (kVK_ANSI_A, "👩‍💻")] {
            let input = event(keyCode, text)
            XCTAssertNotNil(KeyMapping.virtualKey(for: input))
            XCTAssertEqual(KeyMapping.text(for: input), text)
        }
    }
}
