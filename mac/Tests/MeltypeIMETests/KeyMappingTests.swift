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
                XCTAssertEqual(KeyMapping.text(for: input, virtualKey: vk), "")
            }
        }
    }

    func testTextAndSpaceArePreserved() {
        for (keyCode, text) in [(kVK_ANSI_A, "a"), (kVK_ANSI_E, "e\u{301}"), (kVK_Space, " ")] {
            let input = event(keyCode, text)
            let vk = KeyMapping.virtualKey(for: input)!
            XCTAssertEqual(KeyMapping.text(for: input, virtualKey: vk), text)
        }
    }
}
