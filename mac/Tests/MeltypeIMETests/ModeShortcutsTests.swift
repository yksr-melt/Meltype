// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import Cocoa
import XCTest
@testable import MeltypeIME

final class ModeShortcutsTests: XCTestCase {
    private final class Target: ModeServiceTarget {
        var mode: MacInputMode = .automatic
        var available = true
        var actions: [ModeShortcutAction] = []
        func selectModeFromService(_ action: ModeShortcutAction) -> Bool {
            guard available else { return false }
            actions.append(action)
            mode = action.mode(after: mode)
            return true
        }
    }

    private func invoke(_ services: ModeServices, _ identifier: String?) -> NSString? {
        var error: NSString?
        services.changeInputMode(NSPasteboard(name: .init("MeltypeModeServicesTests")), userData: identifier, error: &error)
        return error
    }

    func testCycleAndDirectSelection() {
        XCTAssertEqual(ModeShortcutAction.cycle.mode(after: .automatic), .direct)
        XCTAssertEqual(ModeShortcutAction.cycle.mode(after: .direct), .code)
        XCTAssertEqual(ModeShortcutAction.cycle.mode(after: .code), .automatic)
        for mode in MacInputMode.allCases {
            XCTAssertEqual(ModeShortcutAction.automatic.mode(after: mode), .automatic)
            XCTAssertEqual(ModeShortcutAction.direct.mode(after: mode), .direct)
            XCTAssertEqual(ModeShortcutAction.code.mode(after: mode), .code)
        }
    }

    func testAllServicesReachActiveTarget() {
        let services = ModeServices(isMeltypeSelected: { true }, selectWithoutInput: { _ in })
        let target = Target()
        services.activate(target)
        for action in ModeShortcutAction.allCases {
            XCTAssertNil(invoke(services, action.serviceIdentifier))
            XCTAssertEqual(target.actions.last, action)
        }
        XCTAssertEqual(target.mode, .automatic)
    }

    func testUnavailableInputClientIsRejectedWithoutChangingMode() {
        let services = ModeServices(isMeltypeSelected: { true }, selectWithoutInput: { _ in })
        let target = Target()
        target.available = false
        services.activate(target)
        XCTAssertNotNil(invoke(services, "direct"))
        XCTAssertEqual(target.mode, .automatic)
        XCTAssertTrue(target.actions.isEmpty)
        target.available = true
        XCTAssertNil(invoke(services, "direct"))
        XCTAssertEqual(target.mode, .direct)
    }

    func testInactiveInputUsesCentralSelectionButOtherSourceIsRejected() {
        var selected = true
        var idleActions: [ModeShortcutAction] = []
        let services = ModeServices(isMeltypeSelected: { selected }, selectWithoutInput: { idleActions.append($0) })
        let target = Target()
        XCTAssertNil(invoke(services, "direct"))
        XCTAssertEqual(idleActions, [.direct])
        services.activate(target)
        selected = false
        XCTAssertNotNil(invoke(services, "code"))
        XCTAssertTrue(target.actions.isEmpty)
        XCTAssertEqual(idleActions, [.direct])
        selected = true
        services.deactivate(target)
        XCTAssertNil(invoke(services, "cycle"))
        XCTAssertEqual(idleActions, [.direct, .cycle])
    }

    func testOldControllerDeactivationDoesNotClearNewTarget() {
        let services = ModeServices(isMeltypeSelected: { true }, selectWithoutInput: { _ in })
        let old = Target(), current = Target()
        services.activate(old)
        services.activate(current)
        services.deactivate(old)
        XCTAssertNil(invoke(services, "code"))
        XCTAssertEqual(current.mode, .code)
        XCTAssertTrue(old.actions.isEmpty)
    }

    func testControllerIsNotRetained() {
        let services = ModeServices(isMeltypeSelected: { true }, selectWithoutInput: { _ in })
        var target: Target? = Target()
        weak var reference = target
        services.activate(target!)
        target = nil
        XCTAssertNil(reference)
        XCTAssertNil(invoke(services, "cycle"))
    }

    func testInvalidServiceIdentifiersNeverSelectMode() {
        let services = ModeServices(isMeltypeSelected: { true }, selectWithoutInput: { _ in })
        let target = Target()
        services.activate(target)
        for identifier in [nil, "", "0", "DIRECT", "unknown"] as [String?] {
            XCTAssertNotNil(invoke(services, identifier))
        }
        XCTAssertTrue(target.actions.isEmpty)
    }

    func testServiceDoesNotModifyPasteboard() {
        let services = ModeServices(isMeltypeSelected: { true }, selectWithoutInput: { _ in })
        let target = Target()
        services.activate(target)
        let board = NSPasteboard.withUniqueName()
        defer { board.releaseGlobally() }
        board.setString("既存の文字", forType: .string)
        var error: NSString?
        services.changeInputMode(board, userData: "cycle", error: &error)
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
        XCTAssertTrue(ModeServices(isMeltypeSelected: { true }, selectWithoutInput: { _ in }).responds(to: NSSelectorFromString("changeInputMode:userData:error:")))
    }

    func testIndicatorPreferenceDefaultsEnabledAndPersistsIndependently() {
        let suite = "MeltypeModeIndicatorTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = ModeIndicatorSettings(defaults: defaults)
        XCTAssertTrue(settings.isEnabled)
        settings.isEnabled = false
        XCTAssertFalse(ModeIndicatorSettings(defaults: defaults).isEnabled)
        settings.isEnabled = true
        XCTAssertTrue(ModeIndicatorSettings(defaults: defaults).isEnabled)
    }
}
