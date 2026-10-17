// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

import Foundation
import InputMethodKit

// テストだけの変換スタブ。実際の NativeCore / InputController / KeyMapping と一緒にコンパイルする。
final class MeltypeConverter {
    static let shared = MeltypeConverter()
    func clauses(for text: String, context: String?) -> [(reading: String, text: String)] { [] }
    func candidates(for text: String) -> [String] { [] }
}
var candidatesWindow: IMKCandidates? = nil

@main
enum BoundaryTests {
    static func main() throws {
        let core = NativeCore.shared
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
            guard let session = core.createSession() else { fatalError("Native session creation failed") }
            defer { core.destroySession(session) }
            for scalar in raw.unicodeScalars {
                let vk: Int32 = (97...122).contains(scalar.value) ? Int32(scalar.value) - 32 : 0x07
                guard core.handleKey(session, vk: vk, character: Int32(scalar.value), modifiers: 0,
                                     before: nil, after: "world") != nil else { fatalError("Key returned NULL") }
            }
            if let modeKey {
                _ = core.handleKey(session, vk: modeKey, character: 0, modifiers: 0, before: nil, after: nil)
            }
            guard let result = core.commitBeforeExternalText(session, text: external) else { fatalError("Commit returned NULL") }
            precondition(result.view == nil, "Composition was not cleared")
            precondition(result.commits.count == (expected == nil ? 0 : 1), "Unexpected edits")
            if let expected {
                precondition(Array(result.commits[0].text.unicodeScalars) == Array(expected.unicodeScalars), "Unexpected committed scalars")
                precondition(result.commits[0].deleteBefore == 0, "Unexpected deletion")
            }
            print("PASS: \(raw.debugDescription) + \(external.debugDescription)")
        }
        print("8/8 Swift + NativeAOT boundary cases passed (converter stub; no IMK GUI)")
        try codeCommentSettings(core)
    }

    static func codeCommentSettings(_ core: NativeCore) throws {
        guard let directory = core.dataDirectory else { fatalError("Missing data directory") }
        let path = URL(fileURLWithPath: directory).appendingPathComponent("config.json")
        let original = try Data(contentsOf: path)
        defer {
            do { try original.write(to: path, options: .atomic) }
            catch { fatalError("Could not restore temporary config: \(error)") }
        }
        var settings = try JSONSerialization.jsonObject(with: original) as! [String: Any]
        func configure(_ builtIn: [String: Bool], _ custom: [[String: Any]]) throws {
            settings["CodeSingleComments"] = ["BuiltIn": builtIn, "Custom": custom]
            try JSONSerialization.data(withJSONObject: settings).write(to: path, options: .atomic)
        }
        func assertInput(_ prefix: String, japanese: Bool, typePrefix: Bool = false) {
            guard let session = core.createSession() else { fatalError("Configured session creation failed") }
            defer { core.destroySession(session) }
            core.setCodeInput(session, true)
            if typePrefix {
                for scalar in prefix.unicodeScalars {
                    let vk = (97...122).contains(scalar.value) ? Int32(scalar.value) - 32 : Int32(scalar.value)
                    guard let result = core.handleKey(session, vk: vk, character: Int32(scalar.value),
                                                      modifiers: 0, before: nil, after: nil) else { fatalError("Prefix key returned NULL") }
                    precondition(!result.consumed && result.view == nil, "REM prefix must pass through before its body")
                }
            }
            var last: SessionResult?
            for scalar in "kyouha".unicodeScalars {
                last = core.handleKey(session, vk: Int32(scalar.value) - 32, character: Int32(scalar.value),
                                      modifiers: 0, before: typePrefix ? nil : prefix, after: nil)
                guard let result = last else { fatalError("Code key returned NULL") }
                if !japanese {
                    precondition(!result.consumed && result.view == nil, "Disabled token must pass through as code")
                }
            }
            if japanese {
                precondition(last?.view?.text == "きょうは", "New session must compose inside configured comment")
            }
        }
        let disabledBuiltIn = ["//": false, "#": false, "--": false, "REM": false]
        for enabled in [true, false] {
            try configure(disabledBuiltIn, [["Token": "%", "Enabled": enabled]])
            assertInput("% ", japanese: enabled)
        }
        // Erlang 用の組合せはテストデータ。製品に言語別設定や自動判別は追加しない。
        try configure(disabledBuiltIn, [["Token": "%", "Enabled": true]])
        erlangRecords(core)
        for token in ["//", "#", "--", "REM"] {
            for enabled in [true, false] {
                var builtIn = disabledBuiltIn
                builtIn[token] = enabled
                try configure(builtIn, [])
                assertInput(token + " ", japanese: enabled, typePrefix: token == "REM")
            }
        }
        var remBuiltIn = disabledBuiltIn
        remBuiltIn["REM"] = true
        try configure(remBuiltIn, [])
        for prefix in ["reM ", " \tReM "] {
            assertInput(prefix, japanese: true)
        }
        for prefix in ["echo REM ", "REMfoo ", "REM\t", "REM note\nvalue"] {
            assertInput(prefix, japanese: false)
        }
        let invalidLists: [[[String: Any]]] = [
            [["Token": " ", "Enabled": true]],
            [["Token": "//", "Enabled": true]],
            [["Token": "REM", "Enabled": true]],
            [["Token": "rem", "Enabled": false]],
            [["Token": "ReM", "Enabled": true]],
            [["Token": "%", "Enabled": true], ["Token": "%", "Enabled": false]],
        ]
        for custom in invalidLists {
            try configure(disabledBuiltIn, custom)
            if let session = core.createSession() {
                core.destroySession(session)
                fatalError("Invalid token or duplicate registration must reject session creation")
            }
        }
        print("PASS: JSON BuiltIn/Custom settings reach Swift + NativeAOT sessions; ON/OFF, REM boundaries and invalid/duplicate rejection")
    }

    static func erlangRecords(_ core: NativeCore) {
        let records = [
            "P = #person{name = hana, age = 20}.",
            "P#person{age = 21}.",
            "P#person.name.",
        ]
        for record in records {
            guard let session = core.createSession() else { fatalError("Erlang test session creation failed") }
            defer { core.destroySession(session) }
            core.setCodeInput(session, true)
            for scalar in (record + " % ").unicodeScalars {
                let vk: Int32
                switch scalar.value {
                case 97...122: vk = Int32(scalar.value) - 32
                case 65...90, 48...57, 32: vk = Int32(scalar.value)
                default: vk = 0x07
                }
                guard let result = core.handleKey(session, vk: vk, character: Int32(scalar.value),
                                                  modifiers: 0, before: nil, after: nil) else { fatalError("Record key returned NULL") }
                precondition(!result.consumed && result.view == nil && result.commits.isEmpty,
                             "Record creation/update/access must preserve ASCII direct input")
            }
            var last: SessionResult?
            for scalar in "kyouha".unicodeScalars {
                last = core.handleKey(session, vk: Int32(scalar.value) - 32, character: Int32(scalar.value),
                                      modifiers: 0, before: nil, after: nil)
                guard let result = last else { fatalError("Comment key returned NULL") }
                precondition(result.commits.isEmpty, "Comment body must remain uncommitted before Enter")
            }
            precondition(last?.view?.text == "きょうは", "Percent comment must show Japanese composition")
            guard let committed = core.handleKey(session, vk: 0x0D, character: 0, modifiers: 0,
                                                 before: nil, after: nil) else { fatalError("Enter returned NULL") }
            precondition(committed.consumed && committed.view == nil && committed.commits.count == 1,
                         "Enter must finish the comment composition exactly once")
            precondition(committed.commits[0].text == "きょうは" && committed.commits[0].deleteBefore == 0,
                         "Comment commit must preserve the record and insert only Japanese text")
        }
        print("PASS: Erlang test configuration preserves record ASCII input and composes/commits percent comments")
    }

}
