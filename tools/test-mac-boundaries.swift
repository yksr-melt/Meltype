// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

import Foundation
import InputMethodKit

// 実際の NativeCore / InputController / KeyMapping と一緒にコンパイルする。
var candidatesWindow: IMKCandidates? = nil

@main
enum BoundaryTests {
    static func main() throws {
        let core = NativeCore.shared
        core.initialize()
        let cases: [(String, String, Int32?, String?)] = [
            ("e", "\u{0301}\u{0300}", nil, "e"),
            ("ka", "\u{3099}\u{FE0F}", nil, "か"),
            ("ka", "\u{E0100}\u{E0101}", nil, "か"),
            ("e", "\u{0301}\u{0300}", 0x75, "え"),
            ("ka", "👩‍👩‍👧‍👦", nil, "か "),
            ("ka", "e\u{0301}", nil, "か "),
            ("@kuraido", "\u{0301}\u{0300}", nil, "@kuraido"),
            ("", "\u{0301}\u{0300}", nil, nil),
        ]
        for (raw, external, modeKey, expected) in cases {
            guard let host = core.createHost() else { fatalError("Native host creation failed") }
            defer { core.destroyHost(host) }
            func send(_ key: [String: Any]) -> ImeOutput {
                guard let output = core.send(host, ["version": 1, "type": "KeyPressed", "instance": "t", "field": ["after": "world"], "key": key]) else {
                    fatalError("Key returned NULL")
                }
                return output
            }
            // 文字を入力するキーは文字だけを渡す (仮想キーコードは本体が決める)
            for scalar in raw.unicodeScalars { _ = send(["ch": scalar.value]) }
            if let modeKey { _ = send(["vk": modeKey]) }
            // 1 回のキーで複数のスカラーが入る: 本体が未確定の内容を確定して、元のイベントをアプリに通させる
            let result = send(["text": external]).result
            precondition(!result.consumed, "External text must pass through")
            precondition(result.view == nil, "Composition was not cleared")
            precondition(result.commits.count == (expected == nil ? 0 : 1), "Unexpected edits")
            if let expected {
                precondition(Array(result.commits[0].text.unicodeScalars) == Array(expected.unicodeScalars), "Unexpected committed scalars")
                precondition(result.commits[0].removeBefore == nil, "Unexpected deletion")
            }
            print("PASS: \(raw.debugDescription) + \(external.debugDescription)")
        }
        print("8/8 Swift + NativeAOT boundary cases passed (converter stub; no IMK GUI)")
    }
}
