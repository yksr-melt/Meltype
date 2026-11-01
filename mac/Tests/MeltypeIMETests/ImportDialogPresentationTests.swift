// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import Cocoa
import XCTest
@testable import MeltypeIME

final class ImportDialogPresentationTests: XCTestCase {
    private final class Application: ImportDialogApplication {
        var policy: NSApplication.ActivationPolicy = .prohibited
        var requestedPolicies: [NSApplication.ActivationPolicy] = []
        var activationCount = 0
        var rejectedPolicy: NSApplication.ActivationPolicy?

        func activationPolicy() -> NSApplication.ActivationPolicy { policy }
        func setActivationPolicy(_ policy: NSApplication.ActivationPolicy) -> Bool {
            requestedPolicies.append(policy)
            guard policy != rejectedPolicy else { return false }
            self.policy = policy
            return true
        }
        func activate(ignoringOtherApps flag: Bool) {
            XCTAssertTrue(flag)
            XCTAssertEqual(policy, .accessory)
            activationCount += 1
        }
    }

    // Application は模擬実装。実際のパネル・通知・メニュー操作は検証しない。
    func testBodyRunsWithActivationAllowedAndRestoresPolicy() throws {
        let application = Application()
        var bodyExecuted = false
        try ImportDialogPresentation.run(application: application) {
            XCTAssertEqual(application.policy, .accessory)
            XCTAssertEqual(application.activationCount, 1)
            bodyExecuted = true
        }
        XCTAssertTrue(bodyExecuted)
        XCTAssertEqual(application.policy, .prohibited)
        XCTAssertEqual(application.requestedPolicies, [.accessory, .prohibited])
    }

    func testEarlyReturnFromBodyRestoresEachOriginalPolicy() throws {
        for policy in [NSApplication.ActivationPolicy.prohibited, .accessory, .regular] {
            let application = Application()
            application.policy = policy
            try ImportDialogPresentation.run(application: application) { return }
            XCTAssertEqual(application.policy, policy)
        }
    }

    func testPolicyFailureDoesNotRunBodyAndAllowsNextAttempt() throws {
        let application = Application()
        application.rejectedPolicy = .accessory
        XCTAssertThrowsError(try ImportDialogPresentation.run(application: application) {
            XCTFail("前面化を許可できないときはクロージャを実行しない")
        })
        XCTAssertEqual(application.policy, .prohibited)
        XCTAssertEqual(application.activationCount, 0)
        application.rejectedPolicy = nil
        try ImportDialogPresentation.run(application: application) {}
        XCTAssertEqual(application.policy, .prohibited)
    }

    func testRestoreFailureIsReported() {
        let application = Application()
        application.rejectedPolicy = .prohibited
        XCTAssertThrowsError(try ImportDialogPresentation.run(application: application) {}) { error in
            guard case ImportDialogPresentation.PresentationError.cannotRestore = error else {
                return XCTFail("復元の失敗を報告する: \(error)")
            }
        }
    }

    func testSynchronousReentryDoesNotRunNestedBodyOrRestoreEarly() throws {
        let application = Application()
        try ImportDialogPresentation.run(application: application) {
            do {
                try ImportDialogPresentation.run(application: application) {
                    XCTFail("同期的な再入では内側のクロージャを実行しない")
                }
            } catch { XCTFail("同期的な再入では例外を投げない: \(error)") }
            XCTAssertEqual(application.policy, .accessory)
            XCTAssertEqual(application.activationCount, 1)
        }
        XCTAssertEqual(application.policy, .prohibited)
    }

    func testImportRejectsControlCharactersAndCountsSkippedEntries() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let source = directory.appendingPathComponent("dictionary.plist")
        var rows = [["shortcut": "かいしゃ", "phrase": "株式会社"],
                    ["shortcut": "かいしゃ", "phrase": "株式会社"],
                    ["shortcut": "えもじ", "phrase": "👩‍💻"]]
        for value in Array(0...31) + Array(127...159) {
            let control = String(UnicodeScalar(value)!)
            rows.append(["shortcut": "あくせす", "phrase": "アクセス" + control + "X"])
            rows.append(["shortcut": "あく" + control + "せす", "phrase": "アクセス"])
        }
        let data = try PropertyListSerialization.data(fromPropertyList: rows, format: .binary, options: 0)
        try data.write(to: source)
        let result = try MacUserDictionary.importFile(source, directory: directory.path)
        XCTAssertEqual(result.added, 2)
        XCTAssertEqual(result.skipped, rows.count - 2)
        let contents = try String(contentsOf: directory.appendingPathComponent("userdict.txt"), encoding: .utf8)
        XCTAssertEqual(contents, "かいしゃ\t株式会社\nえもじ\t👩‍💻\n")
        let again = try MacUserDictionary.importFile(source, directory: directory.path)
        XCTAssertEqual(again.added, 0)
        XCTAssertEqual(again.skipped, rows.count)
    }

    func testImportWithNoValidEntriesFailsWithoutChangingDictionary() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let source = directory.appendingPathComponent("dictionary.plist")
        let destination = directory.appendingPathComponent("userdict.txt")
        let existing = "きぞん\t既存の語\n"
        try existing.write(to: destination, atomically: true, encoding: .utf8)
        let cases: [[[String: String]]] = [
            [], [["unrelated": "value"]],
            [["shortcut": "あくせす", "phrase": "アクセス\u{1B}X"]],
            [["shortcut": "あ", "phrase": "短い読み"]],
        ]
        for rows in cases {
            let data = try PropertyListSerialization.data(fromPropertyList: rows, format: .binary, options: 0)
            try data.write(to: source)
            XCTAssertThrowsError(try MacUserDictionary.importFile(source, directory: directory.path)) { error in
                guard case MacUserDictionary.ImportError.unsupported = error else {
                    return XCTFail("有効な項目がなければunsupportedを返す: \(error)")
                }
            }
            XCTAssertEqual(try String(contentsOf: destination, encoding: .utf8), existing)
        }
    }

    func testImportFailureRestoresPolicyAndPreservesExistingDictionary() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let destination = directory.appendingPathComponent("userdict.txt")
        let existing = "きぞん\t既存の語\n"
        try existing.write(to: destination, atomically: true, encoding: .utf8)
        let source = directory.appendingPathComponent("invalid.plist")
        try Data("invalid".utf8).write(to: source)
        let application = Application()
        try ImportDialogPresentation.run(application: application) {
            do {
                _ = try MacUserDictionary.importFile(source, directory: directory.path)
                XCTFail("不正な辞書は取り込まない")
            } catch {}
            XCTAssertEqual(application.policy, .accessory)
        }
        XCTAssertEqual(application.policy, .prohibited)
        XCTAssertEqual(try String(contentsOf: destination, encoding: .utf8), existing)
    }
}
