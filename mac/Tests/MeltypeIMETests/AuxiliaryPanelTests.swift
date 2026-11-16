// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import Cocoa
import InputMethodKit
import XCTest
@testable import MeltypeIME

final class AuxiliaryPanelTests: XCTestCase {
    private let visible = NSRect(x: -800, y: 100, width: 800, height: 600)
    private let size = NSSize(width: 120, height: 32)
    private let candidates = NSSize(width: 95, height: 266)

    func testCandidatesPreferTheRoomierSideBelow() throws {
        let caret = NSRect(x: -600, y: 470, width: 0, height: 20)
        let auxiliary = try XCTUnwrap(MeltypeInputController.auxiliaryFrame(size: size, caret: caret, visible: visible))
        let placement = MeltypeInputController.candidatePlacement(auxiliary: auxiliary, caret: caret, visible: visible)

        XCTAssertEqual(auxiliary, NSRect(x: -600, y: 496, width: 120, height: 32))
        XCTAssertEqual(placement.hint, kIMKLocateCandidatesBelowHint)
        XCTAssertTrue(MeltypeInputController.candidateFits(candidates, placement: placement))
    }

    func testCandidatesPreferTheRoomierSideAbove() throws {
        let caret = NSRect(x: -600, y: 300, width: 0, height: 20)
        let auxiliary = try XCTUnwrap(MeltypeInputController.auxiliaryFrame(size: size, caret: caret, visible: visible))
        let placement = MeltypeInputController.candidatePlacement(auxiliary: auxiliary, caret: caret, visible: visible)

        XCTAssertEqual(auxiliary, NSRect(x: -600, y: 262, width: 120, height: 32))
        XCTAssertEqual(placement.hint, kIMKLocateCandidatesAboveHint)
        XCTAssertTrue(MeltypeInputController.candidateFits(candidates, placement: placement))
    }

    func testScreenEdgesKeepAuxiliaryPanelOnScreenButLeaveNoRoomForBoth() throws {
        for caretY in [105.0, 675.0] {
            let caret = NSRect(x: -600, y: caretY, width: 0, height: 20)
            let auxiliary = try XCTUnwrap(MeltypeInputController.auxiliaryFrame(size: size, caret: caret, visible: visible))
            let placement = MeltypeInputController.candidatePlacement(auxiliary: auxiliary, caret: caret, visible: visible)

            XCTAssertTrue(visible.contains(auxiliary))
            XCTAssertFalse(MeltypeInputController.candidateFits(candidates, placement: placement))
        }
    }

    func testAuxiliaryPanelMovesLeftOnlyWhenItReachesTheRightEdge() throws {
        let caret = NSRect(x: -150, y: 105, width: 0, height: 20)
        let longSize = NSSize(width: 200, height: size.height)

        let short = try XCTUnwrap(MeltypeInputController.auxiliaryFrame(size: size, caret: caret, visible: visible))
        let long = try XCTUnwrap(MeltypeInputController.auxiliaryFrame(size: longSize, caret: caret, visible: visible))

        XCTAssertEqual(short.minX, caret.minX)
        XCTAssertEqual(long.maxX, visible.maxX)
        XCTAssertEqual(long.minY, short.minY)
        XCTAssertEqual(long.width, longSize.width)
        XCTAssertLessThan(long.minX, short.minX)
    }

    func testCandidateSizeDeterminesWhetherBothCanBeShownAtTheSamePosition() throws {
        let caret = NSRect(x: -600, y: 400, width: 0, height: 20)
        let auxiliary = try XCTUnwrap(MeltypeInputController.auxiliaryFrame(size: size, caret: caret, visible: visible))
        let placement = MeltypeInputController.candidatePlacement(auxiliary: auxiliary, caret: caret, visible: visible)

        XCTAssertFalse(MeltypeInputController.candidateFits(NSSize(width: 95, height: 295), placement: placement))
        XCTAssertFalse(MeltypeInputController.candidateFits(.zero, placement: placement))
        XCTAssertTrue(MeltypeInputController.candidateFits(candidates, placement: placement))
        XCTAssertEqual(auxiliary.minY, 426)
    }

    func testWrappedCompositionLeavesRoomOutsideBothLines() throws {
        let start = NSRect(x: -600, y: 470, width: 0, height: 20)
        let wrapped = NSRect(x: -600, y: 450, width: 0, height: 20)
        let bounds = try XCTUnwrap(MeltypeInputController.compositionBounds(start: start, end: wrapped, visible: visible))
        let auxiliary = try XCTUnwrap(MeltypeInputController.auxiliaryFrame(size: size, caret: bounds, visible: visible))
        let placement = MeltypeInputController.candidatePlacement(auxiliary: auxiliary, caret: bounds, visible: visible)

        XCTAssertEqual(bounds, NSRect(x: -600, y: 450, width: 0, height: 40))
        XCTAssertEqual(auxiliary.minY, 496)
        XCTAssertEqual(placement.hint, kIMKLocateCandidatesBelowHint)
        XCTAssertTrue(MeltypeInputController.candidateFits(candidates, placement: placement))
        XCTAssertFalse(MeltypeInputController.candidateFits(NSSize(width: 95, height: 345), placement: placement))
    }

    func testCompositionCanEndOnTheRightEdgeOfTheScreen() {
        let start = NSRect(x: -150, y: 470, width: 0, height: 20)
        let edge = NSRect(x: visible.maxX, y: 470, width: 0, height: 20)
        let beyond = edge.offsetBy(dx: 1, dy: 0)

        XCTAssertEqual(MeltypeInputController.compositionBounds(start: start, end: edge, visible: visible), start)
        XCTAssertNil(MeltypeInputController.compositionBounds(start: start, end: .zero, visible: visible))
        XCTAssertNil(MeltypeInputController.compositionBounds(start: start, end: beyond, visible: visible))
    }

    func testAuxiliaryPanelShrinksToStayOppositeCandidatesNearScreenEdges() throws {
        for (caretY, expectedY, hint) in [
            (130.0, 100.0, kIMKLocateCandidatesAboveHint),
            (650.0, 672.0, kIMKLocateCandidatesBelowHint),
        ] {
            let caret = NSRect(x: -600, y: caretY, width: 0, height: 20)
            let auxiliary = try XCTUnwrap(MeltypeInputController.auxiliaryFrame(size: size, caret: caret, visible: visible))
            let placement = MeltypeInputController.candidatePlacement(auxiliary: auxiliary, caret: caret, visible: visible)

            XCTAssertEqual(auxiliary, NSRect(x: -600, y: expectedY, width: 120, height: 28))
            XCTAssertEqual(placement.hint, hint)
            XCTAssertTrue(MeltypeInputController.candidateFits(candidates, placement: placement))
        }
    }
}
