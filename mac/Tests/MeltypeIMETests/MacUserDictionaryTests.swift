// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 kikutadev

import Foundation
import XCTest
@testable import MeltypeIME

final class MacUserDictionaryTests: XCTestCase {
    private func plist(_ rows: [[String: String]]) throws -> Data {
        try PropertyListSerialization.data(fromPropertyList: rows, format: .binary, options: 0)
    }

    func testImportSkipsControlCharactersRejectedByCore() throws {
        let input = try plist([
            ["reading": "かな", "phrase": "仮名"],
            ["reading": "かな", "phrase": "危険\u{001b}文節"],
            ["reading": "か\u{007f}な", "phrase": "区切り"],
            ["reading": "かな", "phrase": "C1\u{0085}制御"],
            ["reading": "かな", "phrase": "改\n行"],
        ])
        let entries = try MacUserDictionary.entries(from: input)
        XCTAssertEqual(entries.map { "\($0.0)\t\($0.1)" }, ["かな\t仮名"])
    }

    func testImportRejectsPlistContainingOnlyUnusableEntries() throws {
        let input = try plist([
            ["reading": "かな", "phrase": "隠れた\u{001b}制御文字"],
            ["reading": "か\u{0000}な", "phrase": "仮名"],
        ])
        XCTAssertThrowsError(try MacUserDictionary.entries(from: input)) { error in
            XCTAssertNotNil(error as? MacUserDictionary.ImportError)
        }
    }

    func testImportFileReportsOnlyUsableEntries() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        defer { try? FileManager.default.removeItem(at: root) }
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let source = root.appendingPathComponent("input.plist")
        let target = root.appendingPathComponent("output", isDirectory: true)
        try plist([
            ["reading": "かな", "phrase": "仮名"],
            ["reading": "かな", "phrase": "偽\u{001b}名"],
        ]).write(to: source)
        XCTAssertEqual(try MacUserDictionary.importFile(source, directory: target.path), 1)
        let saved = try String(contentsOf: target.appendingPathComponent("userdict.txt"), encoding: .utf8)
        XCTAssertEqual(saved, "かな\t仮名\n")
        XCTAssertEqual(try MacUserDictionary.importFile(source, directory: target.path), 0)
    }
}
