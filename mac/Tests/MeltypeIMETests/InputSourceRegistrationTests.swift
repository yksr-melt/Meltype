// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hrmcngs

import Foundation
import XCTest
@testable import MeltypeIME

final class InputSourceRegistrationTests: XCTestCase {
    private let parent: [String: Any] = [
        "Bundle ID": InputSourceRegistration.identifier,
        "InputSourceKind": "Keyboard Input Method",
        "metadata": ["keep": true]
    ]
    private let mode: [String: Any] = [
        "Bundle ID": InputSourceRegistration.identifier,
        "Input Mode": InputSourceRegistration.japaneseIdentifier,
        "InputSourceKind": "Input Mode",
        "metadata": ["order": 7]
    ]
    private let abc: [String: Any] = ["InputSourceKind": "Keyboard Layout", "KeyboardLayout Name": "ABC"]
    private let google: [String: Any] = ["Bundle ID": "com.google.inputmethod.Japanese", "metadata": ["enabled": true]]

    func testPreservesFirstRecordsTheirPositionsAndMetadata() {
        for expected in [[mode, abc, parent, google], [parent, google, mode, abc]] {
            var laterParent = parent
            laterParent["metadata"] = ["keep": false]
            var laterMode = mode
            laterMode["metadata"] = ["order": 99]
            let input = expected + [laterParent, laterMode, laterParent, laterMode]
            let result = InputSourceRegistration.normalized(input)
            XCTAssertTrue(NSArray(array: result).isEqual(to: expected))
            XCTAssertTrue(NSArray(array: InputSourceRegistration.normalized(result)).isEqual(to: expected))
        }
    }

    func testAppendsOnlyMissingRecord() {
        let result = InputSourceRegistration.normalized([mode, google, mode])
        XCTAssertTrue(NSArray(array: Array(result.prefix(2))).isEqual(to: [mode, google]))
        XCTAssertEqual(result.count, 3)
        XCTAssertEqual(result.last?["InputSourceKind"] as? String, "Keyboard Input Method")
    }

    func testPreservesUnrecognizedMeltypeModesAndSimilarIdentifiers() {
        let unknown: [String: Any] = ["Bundle ID": InputSourceRegistration.identifier,
                                      "Input Mode": "future-mode", "InputSourceKind": "Input Mode"]
        let similar: [String: Any] = ["Bundle ID": InputSourceRegistration.identifier + "Extra"]
        let expected = [mode, unknown, parent, similar]
        XCTAssertTrue(NSArray(array: InputSourceRegistration.normalized(expected)).isEqual(to: expected))
    }

    func testDeduplicatesEachExistingStoreWithoutMigrating() {
        let pairs = (0..<15).flatMap { _ in [parent, mode] }
        let result = InputSourceRegistration.repaired(legacy: [abc] + pairs, thirdParty: [google] + pairs)
        XCTAssertTrue(NSArray(array: result.legacy).isEqual(to: [abc, parent, mode]))
        XCTAssertTrue(NSArray(array: result.thirdParty ?? []).isEqual(to: [google, parent, mode]))
    }

    func testDoesNotCreateOrPopulateAnotherStore() {
        for thirdParty: [[String: Any]]? in [nil, [], [google]] {
            let result = InputSourceRegistration.repaired(legacy: [mode, abc, parent], thirdParty: thirdParty)
            XCTAssertTrue(NSArray(array: result.legacy).isEqual(to: [mode, abc, parent]))
            if let thirdParty {
                XCTAssertTrue(NSArray(array: result.thirdParty ?? []).isEqual(to: thirdParty))
            } else {
                XCTAssertNil(result.thirdParty)
            }
        }
        let absent = InputSourceRegistration.repaired(legacy: [abc], thirdParty: nil)
        XCTAssertTrue(NSArray(array: absent.legacy).isEqual(to: [abc]))
        XCTAssertNil(absent.thirdParty)
        let modern = InputSourceRegistration.repaired(legacy: [abc], thirdParty: [mode, google, parent, mode])
        XCTAssertTrue(NSArray(array: modern.legacy).isEqual(to: [abc]))
        XCTAssertTrue(NSArray(array: modern.thirdParty ?? []).isEqual(to: [mode, google, parent]))
    }

    func testDelayedVisibilityIsSuccessfulWithoutMenuReadiness() {
        for count in [0, 2, 15] {
            var waits = 0
            var preparations = 0
            let result = InputSourceRegistration.register(prepare: { preparations += 1 },
                enabledCount: { count }, wait: { waits += 1 })
            XCTAssertEqual(result, .pending)
            XCTAssertEqual(result.exitCode, 0)
            XCTAssertEqual(preparations, 1)
            XCTAssertEqual(waits, 20)
        }
    }

    func testBecomesReadyAfterCacheRefresh() {
        var waits = 0
        let result = InputSourceRegistration.register(prepare: {},
            enabledCount: { waits < 2 ? 0 : 1 }, wait: { waits += 1 })
        XCTAssertEqual(result, .ready)
        XCTAssertEqual(result.exitCode, 0)
        XCTAssertEqual(waits, 2)
    }

    func testPreparationFailureDoesNotBecomePendingOrPoll() {
        var observations = 0
        let result = InputSourceRegistration.register(
            prepare: { throw InputSourceRegistration.Failure("storage or TIS API failed") },
            enabledCount: { observations += 1; return 1 }, wait: { XCTFail("must not wait") })
        XCTAssertEqual(result, .failed)
        XCTAssertEqual(result.exitCode, 1)
        XCTAssertEqual(observations, 0)
    }

    func testRejectsMalformedPreferences() {
        for invalid: Any in ["not an array", [abc, "not a dictionary"] as [Any]] {
            XCTAssertThrowsError(try InputSourceRegistration.validatedSources(invalid, name: "test"))
        }
        XCTAssertNil(try InputSourceRegistration.validatedSources(nil, name: "test"))
    }
}
