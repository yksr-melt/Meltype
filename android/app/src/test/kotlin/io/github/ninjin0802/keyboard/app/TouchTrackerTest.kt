// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.TouchTracker
import io.github.ninjin0802.keyboard.app.input.TouchTracker.Outcome
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class TouchTrackerTest {
    private val t = TouchTracker()
    private fun first(id: Int, x: Float, y: Float) = t.down(id, x, y, fresh = true)
    private fun more(id: Int, x: Float, y: Float) = t.down(id, x, y, fresh = false)

    @Test fun first_down_starts() { assertEquals(Outcome.START, first(0, 1f, 2f)); assertTrue(t.tracking) }

    @Test fun second_down_completes_first_at_its_last_position() {
        first(0, 1f, 2f); t.move(0, 5f, 6f)
        assertEquals(Outcome.COMPLETE_THEN_START, more(1, 9f, 9f))
        assertEquals(5f, t.completeX); assertEquals(6f, t.completeY)
    }

    /** 前のキーの動作が何も取り消さなければ、後から触れた指のキーは、ふつうに押される。 */
    @Test fun rollover_without_cancel_completes_then_tracks_the_second_finger() {
        first(0, 1f, 2f)
        assertEquals(Outcome.COMPLETE_THEN_START, more(1, 9f, 9f))
        assertTrue(t.tracking)
        assertTrue(t.move(1, 10f, 9f))
        assertEquals(Outcome.COMPLETE, t.up(1, 10f, 9f))
        assertFalse(t.tracking)
        assertEquals(Outcome.IGNORE, t.up(0, 1f, 2f))
        // 離した指がまた触れても、同じように乗り換える。
        first(0, 1f, 2f); more(1, 9f, 9f); t.up(0, 1f, 2f)
        assertEquals(Outcome.COMPLETE_THEN_START, more(0, 3f, 3f))
    }

    @Test fun first_up_after_second_down_is_ignored() {
        first(0, 1f, 2f); more(1, 9f, 9f)
        assertEquals(Outcome.IGNORE, t.up(0, 3f, 3f))
        assertEquals(Outcome.COMPLETE, t.up(1, 8f, 7f))
        assertEquals(8f, t.completeX); assertEquals(7f, t.completeY)
    }

    @Test fun move_of_other_pointer_is_ignored() {
        first(0, 1f, 2f); more(1, 9f, 9f)
        assertEquals(false, t.move(0, 100f, 100f))
        assertEquals(true, t.move(1, 10f, 11f))
    }

    @Test fun cancel_emits_nothing_and_clears() {
        first(0, 1f, 2f); t.cancel()
        assertFalse(t.tracking)
        assertFalse(t.move(0, 5f, 5f))
        assertEquals(Outcome.IGNORE, t.up(0, 1f, 2f))
        assertEquals(Outcome.START, first(2, 0f, 0f))
    }

    /**
     * 2本目の指が触れて前のキーを完了させたら、そのキーの動作が配列を切り替えて、タッチを取り消した。
     * 2本目の指の押下は始めず、その指の動きも離れたことも無視する。全部の指が離れて、最初の指が触れ直すまで。
     */
    @Test fun cancel_while_completing_the_first_press_ignores_every_finger_until_a_fresh_down() {
        first(0, 1f, 2f)
        assertEquals(Outcome.COMPLETE_THEN_START, more(1, 9f, 9f))
        t.cancel()   // 前のキーの動作の中から
        assertFalse(t.tracking)
        assertFalse(t.move(1, 10f, 9f))
        // 取り消したあとに触れた3本目の指も、押下にしない。
        assertEquals(Outcome.IGNORE, more(2, 4f, 4f))
        assertFalse(t.tracking)
        assertFalse(t.move(2, 5f, 4f))
        assertEquals(Outcome.IGNORE, t.up(2, 5f, 4f))
        assertEquals(Outcome.IGNORE, t.up(1, 10f, 9f))
        assertEquals(Outcome.IGNORE, t.up(0, 1f, 2f))
        assertFalse(t.tracking)
        assertEquals(Outcome.START, first(0, 3f, 3f))
        assertTrue(t.tracking)
        assertEquals(Outcome.COMPLETE, t.up(0, 3f, 3f))
    }

    /**
     * 指を置いたまま、タッチの外から取り消された（設定で配列を選び直した知らせ、画面の回転、絵文字の画面が閉じた）。
     * 置いたままの指も、そのあと触れた指も、最初の指が触れ直すまで無視する。
     */
    @Test fun cancel_arriving_between_events_ignores_the_resting_finger_and_later_fingers() {
        first(0, 1f, 2f); t.move(0, 3f, 2f)
        t.cancel()
        assertFalse(t.tracking)
        assertFalse(t.move(0, 4f, 2f))
        assertEquals(Outcome.IGNORE, more(1, 9f, 9f))
        assertFalse(t.tracking)
        assertEquals(Outcome.IGNORE, t.up(1, 9f, 9f))
        assertEquals(Outcome.IGNORE, t.up(0, 4f, 2f))
        // 取り消しが重なっても同じ（配列の切り替えは、絵文字の画面を閉じてから配列を替える）。
        t.cancel()
        assertEquals(Outcome.IGNORE, more(2, 5f, 5f))
        assertEquals(Outcome.START, first(0, 1f, 2f))
        assertEquals(Outcome.COMPLETE, t.up(0, 1f, 2f))
    }

    /** 指が触れていないときの取り消し（キーボードを閉じたときなど）のあとも、次の最初の指から、ふつうに働く。 */
    @Test fun cancel_with_no_finger_down_does_not_block_the_next_touch() {
        t.cancel(); t.cancel()
        assertEquals(Outcome.START, first(0, 1f, 2f))
        assertEquals(Outcome.COMPLETE_THEN_START, more(1, 9f, 9f))
        assertEquals(Outcome.COMPLETE, t.up(1, 9f, 9f))
    }
}
