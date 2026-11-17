// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.SpaceCursorGesture
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class SpaceCursorGestureTest {
    /** 1 dp = 1 px として、1文字 12 px、タップの範囲 20 px。 */
    private val g = SpaceCursorGesture(stepPx = 12f)
    private fun held(x: Float = 100f): SpaceCursorGesture { g.down(x, 50f, slopPx = 20f, timeMs = 1000); assertTrue(g.longPress(1300)); return g }

    @Test fun constants_match_the_spec() { assertEquals(300L, SpaceCursorGesture.LONG_PRESS_MS); assertEquals(12f, SpaceCursorGesture.STEP_DP) }
    @Test fun enters_after_300_ms() { g.down(100f, 50f, 20f, 1000); assertFalse(g.active); assertTrue(g.longPress(1300)); assertTrue(g.active) }
    @Test fun does_not_enter_before_300_ms() { g.down(100f, 50f, 20f, 1000); assertFalse(g.longPress(1299)); assertFalse(g.active) }
    @Test fun a_short_press_is_an_ordinary_space() { g.down(100f, 50f, 20f, 1000); assertEquals(0, g.move(105f, 50f)); assertFalse(g.end()) }
    @Test fun small_drift_inside_the_slop_still_enters() { g.down(100f, 50f, 20f, 1000); g.move(110f, 60f); assertTrue(g.longPress(1300)) }
    @Test fun moving_beyond_the_slop_first_never_enters() {
        g.down(100f, 50f, 20f, 1000); assertEquals(0, g.move(121f, 50f)); g.move(100f, 50f)
        assertFalse(g.longPress(1300)); assertEquals(0, g.move(200f, 50f)); assertFalse(g.end())
    }
    @Test fun vertical_movement_beyond_the_slop_also_prevents_entering() { g.down(100f, 50f, 20f, 1000); g.move(100f, 71f); assertFalse(g.longPress(1300)) }
    @Test fun without_a_press_nothing_happens() { assertFalse(g.longPress(5000)); assertEquals(0, g.move(500f, 0f)); assertFalse(g.end()) }
    @Test fun one_step_per_12_px_carrying_the_remainder() {
        held()
        assertEquals(listOf(0, 1, 0, 1, 3), listOf(111f, 113f, 123f, 125f, 160.5f).map { g.move(it, 50f) })
    }
    @Test fun steps_are_measured_from_where_the_finger_was_when_the_mode_began() {
        g.down(100f, 50f, 20f, 1000); g.move(115f, 50f); assertTrue(g.longPress(1300))
        assertEquals(0, g.move(126f, 50f)); assertEquals(1, g.move(127f, 50f))
    }
    @Test fun goes_left_and_back_and_forth() {
        held()
        assertEquals(listOf(-2, 0, 1, 1, 2, -3), listOf(76f, 78f, 89f, 100f, 124f, 88f).map { g.move(it, 50f) })
    }
    @Test fun vertical_movement_is_ignored_in_the_mode() { held(); assertEquals(0, g.move(100f, 900f)); assertEquals(2, g.move(124f, -300f)); assertTrue(g.active) }
    @Test fun release_ends_the_mode_and_reports_that_no_space_is_due() {
        held(); g.move(150f, 50f); assertTrue(g.end()); assertFalse(g.active)
        assertEquals(0, g.move(300f, 50f)); assertFalse(g.end())
    }
    @Test fun a_new_press_starts_fresh() {
        held(); g.move(160f, 50f); g.end()
        g.down(10f, 50f, 20f, 5000); assertFalse(g.active); assertTrue(g.longPress(5300)); assertEquals(1, g.move(22f, 50f))
    }
    @Test fun cancel_forgets_the_press() { g.down(100f, 50f, 20f, 1000); g.cancel(); assertFalse(g.longPress(1300)) }

    /** 取り消したあとは、長押しの時刻が来ても入らず、指が動いてもカーソルを動かさず、離しても空白の扱いを変えない。 */
    @Test fun after_cancel_nothing_is_left_armed() {
        g.down(100f, 50f, 20f, 1000); g.cancel()
        assertFalse(g.longPress(9000)); assertFalse(g.active); assertEquals(0, g.move(400f, 50f)); assertFalse(g.end())
    }
    @Test fun cancel_in_the_mode_leaves_it() {
        held(); g.cancel()
        assertFalse(g.active); assertEquals(0, g.move(400f, 50f)); assertFalse(g.longPress(9000)); assertFalse(g.end())
    }
}
