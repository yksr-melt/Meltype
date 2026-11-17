// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.core.CaretWatch
import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * Which onUpdateSelection reports are the keyboard's own edits for the Core, and which are caret
 * moves by the user or the app (sent to the Core as CaretMoved). Positions are UTF-16 offsets.
 */
class CaretWatchTest {
    /** An editor holding "abc" with the caret at its end; the keyboard has made no edit yet. */
    private fun watchAtEndOfAbc() = CaretWatch().apply { reset(3, 3) }

    @Test fun own_edits_are_not_moves_and_a_tap_elsewhere_is() {
        val watch = watchAtEndOfAbc()
        watch.replaced(0, 1)                        // composing "k"
        assertTrue(watch.isOwn(4, 4, composingEnd = 4, composing = true))
        watch.replaced(1, 1)                        // "k" -> "か"
        assertTrue(watch.isOwn(4, 4, composingEnd = 4, composing = true))
        watch.replaced(1, 1)                        // committed "か"
        assertTrue(watch.isOwn(4, 4))
        assertFalse(watch.isOwn(1, 1), "a tap after the first character")
    }

    @Test fun late_reports_of_earlier_edits_are_own() {
        val watch = watchAtEndOfAbc()
        // Three keys typed before the editor reports the first one.
        watch.replaced(0, 1)
        watch.replaced(1, 2)
        watch.replaced(2, 3)
        assertTrue(watch.isOwn(4, 4))
        assertTrue(watch.isOwn(5, 5))
        assertTrue(watch.isOwn(6, 6))
        assertFalse(watch.isOwn(0, 0))
    }

    @Test fun one_report_for_several_edits_is_own() {
        val watch = watchAtEndOfAbc()
        watch.replaced(0, 1)
        watch.replaced(1, 2)
        watch.replaced(2, 3)
        assertTrue(watch.isOwn(6, 6))
        assertFalse(watch.isOwn(4, 4), "after the newest report, an older position is a move")
    }

    @Test fun late_reports_that_go_back_and_forth_are_own() {
        // Typing then deleting: the reports go right, then left (not toward the newest position).
        val watch = watchAtEndOfAbc()
        watch.replaced(0, 1)                        // composing "a"
        watch.replaced(1, 2)                        // "ab"
        watch.replaced(2, 1)                        // Back: "a"
        watch.replaced(1, 0)                        // Back: no composing text
        assertTrue(watch.isOwn(4, 4))
        assertTrue(watch.isOwn(5, 5))
        assertTrue(watch.isOwn(4, 4))
        assertTrue(watch.isOwn(3, 3))
    }

    @Test fun a_recommit_of_the_previous_word_is_own() {
        // "い " then composing "want"; the Core removes "い " and commits "I want ".
        val watch = CaretWatch().apply { reset(0, 0) }
        watch.replaced(0, 2)                        // "い "
        watch.replaced(0, 4)                        // composing "want"
        watch.replaced(4, 0)                        // composing text taken away
        watch.replaced(2, 0)                        // removeBefore "い "
        watch.replaced(0, 7)                        // "I want "
        assertTrue(watch.isOwn(7, 7))
    }

    @Test fun moving_back_into_brackets_is_own() {
        val watch = CaretWatch().apply { reset(0, 0) }
        watch.replaced(0, 2)                        // "「」"
        watch.movedBack(1)
        assertTrue(watch.isOwn(1, 1))
        assertFalse(watch.isOwn(2, 2), "the end of the brackets, after the caret went inside")
    }

    @Test fun edits_after_a_tap_continue_from_the_tapped_position() {
        val watch = watchAtEndOfAbc()
        assertFalse(watch.isOwn(1, 1))
        watch.replaced(0, 1)
        assertTrue(watch.isOwn(2, 2))
    }

    @Test fun typing_over_a_selection_replaces_it() {
        val watch = CaretWatch().apply { reset(1, 3) }
        watch.replaced(0, 1)
        assertTrue(watch.isOwn(2, 2))
    }

    @Test fun a_report_without_a_move_is_not_a_move() {
        // finishComposingText keeps the caret where it is; the editor still reports.
        val watch = watchAtEndOfAbc()
        assertTrue(watch.isOwn(3, 3))
    }

    @Test fun the_caret_at_the_end_of_own_composing_text_is_own_only_while_composing() {
        // An editor whose positions differ from what the edits predict (it changed the text).
        val composing = watchAtEndOfAbc().apply { replaced(0, 2) }
        assertTrue(composing.isOwn(9, 9, composingEnd = 9, composing = true))
        val committed = watchAtEndOfAbc().apply { replaced(0, 2) }
        assertFalse(committed.isOwn(9, 9, composingEnd = 9, composing = false))
        val inside = watchAtEndOfAbc().apply { replaced(0, 2) }
        assertFalse(inside.isOwn(4, 4, composingEnd = 5, composing = true), "a tap inside the composing text")
    }

    @Test fun a_key_passed_to_the_app_takes_the_next_report_as_own() {
        val watch = watchAtEndOfAbc()
        watch.lose()                                // Enter passed to the app: a newline, or an editor action
        watch.replaced(0, 1)
        assertTrue(watch.isOwn(5, 5))
        watch.replaced(0, 1)
        assertTrue(watch.isOwn(6, 6))
        assertFalse(watch.isOwn(0, 0))
    }

    @Test fun an_unknown_start_takes_the_first_report_as_own() {
        val watch = CaretWatch().apply { reset(-1, -1) }
        watch.replaced(0, 1)
        assertTrue(watch.isOwn(8, 8))
        watch.replaced(0, 1)
        assertTrue(watch.isOwn(9, 9))
        assertFalse(watch.isOwn(2, 2))
    }
}
