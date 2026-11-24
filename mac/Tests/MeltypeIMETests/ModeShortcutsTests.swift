// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota
// Copyright (C) 2026 aster-void

import Cocoa
import XCTest
@testable import MeltypeIME

final class ModeShortcutsTests: XCTestCase {
    private final class Target: ModeServiceTarget {
        var available = true
        var actions: [ModeShortcutAction] = []
        func selectModeFromService(_ action: ModeShortcutAction) -> Bool {
            guard available else { return false }
            actions.append(action)
            return true
        }
    }

    private func invoke(_ services: ModeServices, _ identifier: String?) -> NSString? {
        var error: NSString?
        services.changeInputMode(NSPasteboard(name: .init("MeltypeModeServicesTests")), userData: identifier, error: &error)
        return error
    }

    func testAllServicesReachActiveTarget() {
        let services = ModeServices(isMeltypeSelected: { true })
        let target = Target()
        services.activate(target)
        for action in ModeShortcutAction.allCases {
            XCTAssertNil(invoke(services, action.serviceIdentifier))
            XCTAssertEqual(target.actions.last, action)
        }
    }

    func testUnavailableInputClientIsRejectedWithoutChangingMode() {
        let services = ModeServices(isMeltypeSelected: { true })
        let target = Target()
        target.available = false
        services.activate(target)
        XCTAssertNotNil(invoke(services, "direct"))
        XCTAssertTrue(target.actions.isEmpty)
        target.available = true
        XCTAssertNil(invoke(services, "direct"))
        XCTAssertEqual(target.actions, [.direct])
    }

    func testWithoutInputOrOtherSourceIsRejected() {
        var selected = true
        let services = ModeServices(isMeltypeSelected: { selected })
        let target = Target()
        XCTAssertNotNil(invoke(services, "direct"), "入力欄が無ければモードは選べない (モードは入力欄ごと)")
        services.activate(target)
        selected = false
        XCTAssertNotNil(invoke(services, "toggle"))
        XCTAssertTrue(target.actions.isEmpty)
        selected = true
        XCTAssertNil(invoke(services, "toggle"))
        XCTAssertEqual(target.actions, [.toggle])
    }

    func testOldControllerDeactivationDoesNotClearNewTarget() {
        let services = ModeServices(isMeltypeSelected: { true })
        let old = Target(), current = Target()
        services.activate(old)
        services.activate(current)
        services.deactivate(old)
        XCTAssertNil(invoke(services, "japanese"))
        XCTAssertEqual(current.actions, [.japanese])
        XCTAssertTrue(old.actions.isEmpty)
    }

    func testControllerIsNotRetained() {
        let services = ModeServices(isMeltypeSelected: { true })
        var target: Target? = Target()
        weak var reference = target
        services.activate(target!)
        target = nil
        XCTAssertNil(reference)
        XCTAssertNotNil(invoke(services, "toggle"))
    }

    func testInvalidServiceIdentifiersNeverSelectMode() {
        let services = ModeServices(isMeltypeSelected: { true })
        let target = Target()
        services.activate(target)
        for identifier in [nil, "", "0", "DIRECT", "unknown"] as [String?] {
            XCTAssertNotNil(invoke(services, identifier))
        }
        XCTAssertTrue(target.actions.isEmpty)
    }

    func testServiceDoesNotModifyPasteboard() {
        let services = ModeServices(isMeltypeSelected: { true })
        let target = Target()
        services.activate(target)
        let board = NSPasteboard.withUniqueName()
        defer { board.releaseGlobally() }
        board.setString("既存の文字", forType: .string)
        var error: NSString?
        services.changeInputMode(board, userData: "toggle", error: &error)
        XCTAssertNil(error)
        XCTAssertEqual(board.string(forType: .string), "既存の文字")
    }

    func testBundleDeclaresAllServicesWithoutTextSelectionOrDefaultKeys() throws {
        let file = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Resources/Info.plist")
        let plist = try PropertyListSerialization.propertyList(
            from: Data(contentsOf: file),
            options: PropertyListSerialization.ReadOptions(),
            format: nil
        ) as! [String: Any]
        let services = plist["NSServices"] as! [[String: Any]]
        XCTAssertEqual(services.count, ModeShortcutAction.allCases.count)
        for (entry, action) in zip(services, ModeShortcutAction.allCases) {
            XCTAssertEqual((entry["NSMenuItem"] as? [String: String])?["default"], action.serviceTitle)
            XCTAssertEqual(entry["NSUserData"] as? String, action.serviceIdentifier)
            XCTAssertEqual(entry["NSPortName"] as? String, ModeServices.portName)
            XCTAssertEqual(entry["NSMessage"] as? String, "changeInputMode")
            XCTAssertEqual((entry["NSRequiredContext"] as? [String: String])?.count, 0)
            XCTAssertNil(entry["NSSendTypes"])
            XCTAssertNil(entry["NSReturnTypes"])
            XCTAssertNil(entry["NSKeyEquivalent"])
        }
        XCTAssertTrue(ModeServices(isMeltypeSelected: { true }).responds(to: NSSelectorFromString("changeInputMode:userData:error:")))
    }
}
