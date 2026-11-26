// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import Cocoa
import XCTest
@testable import MeltypeIME

final class CompositionPanelTests: XCTestCase {
    func testPlacementKeepsCaretEdgeWhenContentHeightChanges() throws {
        let screen = NSRect(x: -1000, y: 120, width: 1000, height: 700)
        for above in [true, false] {
            let line = NSRect(x: -15, y: above ? 130 : 785, width: 1, height: 28)
            let placement = try XCTUnwrap(CompositionPanelPlacement(start: line, end: line, visible: screen))
            XCTAssertEqual(placement.above, above)
            let small = placement.frame(size: NSSize(width: 90, height: 32))
            let large = placement.frame(size: NSSize(width: 300, height: 260))
            XCTAssertTrue(screen.contains(small))
            XCTAssertTrue(screen.contains(large))
            XCTAssertEqual(above ? small.minY : small.maxY, above ? large.minY : large.maxY)
            XCTAssertEqual(large.maxX, screen.maxX)
        }
    }

    func testPlacementAvoidsAllWrappedLinesAndRejectsMissingCoordinates() throws {
        let screen = NSRect(x: 0, y: 0, width: 500, height: 400)
        let start = NSRect(x: 160, y: 210, width: 1, height: 30)
        let end = NSRect(x: 10, y: 120, width: 0, height: 30)
        let placement = try XCTUnwrap(CompositionPanelPlacement(start: start, end: end, visible: screen))
        let frame = placement.frame(size: NSSize(width: 150, height: placement.availableHeight))
        XCTAssertEqual(frame.minX, start.minX)
        XCTAssertGreaterThanOrEqual(frame.minY, start.maxY)
        XCTAssertTrue(screen.contains(frame))
        XCTAssertNil(CompositionPanelPlacement(start: start, end: .zero, visible: screen))
    }

    func testPlacementWithoutCaretStaysOnScreenBottom() {
        let screen = NSRect(x: -1000, y: 120, width: 1000, height: 700)
        let placement = CompositionPanelPlacement(bottomOf: screen)
        let frame = placement.frame(size: NSSize(width: 300, height: placement.availableHeight))
        XCTAssertTrue(placement.above)
        XCTAssertEqual(frame.midX, screen.midX)
        XCTAssertTrue(screen.contains(frame))
    }

    func testColorSwatchParserParsesHexRgbAndHsl() {
        XCTAssertEqual(ColorSwatchParser.parseRgba("#3b82f6"), ColorSwatchRgba(r: 59, g: 130, b: 246, a: 255))
        XCTAssertEqual(ColorSwatchParser.parseRgba("#f00"), ColorSwatchRgba(r: 255, g: 0, b: 0, a: 255))
        XCTAssertEqual(ColorSwatchParser.parseRgba("rgba(255, 0, 0, 0.5)"), ColorSwatchRgba(r: 255, g: 0, b: 0, a: 128))
        XCTAssertEqual(ColorSwatchParser.parseRgba("hsl(0, 100%, 50%)"), ColorSwatchRgba(r: 255, g: 0, b: 0, a: 255))
        XCTAssertNil(ColorSwatchParser.parseRgba("#3b82f6 Issueタイトル"))
        XCTAssertNil(ColorSwatchParser.parseRgba("rgb(256, 0, 0)"))
    }
}
