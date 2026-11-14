// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.emoji.EmojiGridGesture
import io.github.ninjin0802.keyboard.app.emoji.EmojiGridGesture.Up
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/** 絵文字のます目の上の指の動きの見分け（spec 04「ます目」）。動かした量の許容は 10 px。 */
class EmojiGridGestureTest {
    private val g = EmojiGridGesture(slopPx = 10f)
    private val none = EmojiGridGesture.NONE

    @Test fun a_tap_picks_the_pressed_cell_on_release() {
        g.down(0, y = 100f, index = 7, flinging = false)
        assertEquals(7, g.pressed)
        assertEquals(Up.PICK, g.up(0, index = 7)); assertEquals(7, g.picked)
        assertEquals(none, g.pressed)
    }

    @Test fun small_movement_within_the_slop_is_still_a_tap() {
        g.down(0, 100f, 7, flinging = false)
        assertEquals(0f, g.move(0, 104f)); assertEquals(0f, g.move(0, 90f)); assertEquals(0f, g.move(0, 110f))
        assertFalse(g.dragging); assertEquals(7, g.pressed)
        assertEquals(Up.PICK, g.up(0, 7)); assertEquals(7, g.picked)
    }

    /** 選ぶのは、指を離したところのます。押したますから外れて離したら、どれも選ばない。 */
    @Test fun the_release_point_decides() {
        g.down(0, 100f, 7, flinging = false)
        assertEquals(Up.NOTHING, g.up(0, index = 8)); assertEquals(none, g.picked)
        g.down(0, 100f, 7, flinging = false)
        assertEquals(Up.NOTHING, g.up(0, index = none))
        // 絵文字のないますを押した。
        g.down(0, 100f, none, flinging = false)
        assertEquals(Up.NOTHING, g.up(0, index = none)); assertEquals(none, g.picked)
    }

    @Test fun vertical_movement_beyond_the_slop_scrolls_and_never_picks() {
        g.down(0, 100f, 7, flinging = false)
        assertEquals(0f, g.move(0, 111f))          // 許容を超えたところまでの動きは捨てる（始まりで跳ねない）
        assertTrue(g.dragging); assertEquals(none, g.pressed)
        assertEquals(9f, g.move(0, 120f)); assertEquals(-30f, g.move(0, 90f))
        // 元のますの上に戻って離しても、選ばない。
        assertEquals(10f, g.move(0, 100f))
        assertEquals(Up.SCROLLED, g.up(0, index = 7)); assertEquals(none, g.picked)
        assertFalse(g.dragging)
    }

    @Test fun upward_movement_scrolls_too() {
        g.down(0, 100f, 7, flinging = false)
        assertEquals(0f, g.move(0, 80f)); assertEquals(-5f, g.move(0, 75f))
        assertEquals(Up.SCROLLED, g.up(0, 3))
    }

    /** 流れているあいだに触れた指は、流れを止めるだけ。動かさずに離しても選ばない。その指で、続けてスクロールはできる。 */
    @Test fun a_touch_that_stops_a_fling_never_picks() {
        g.down(0, 100f, index = 7, flinging = true)
        assertEquals(none, g.pressed)
        assertEquals(Up.NOTHING, g.up(0, 7)); assertEquals(none, g.picked)
        g.down(0, 100f, 7, flinging = true)
        assertEquals(0f, g.move(0, 120f)); assertEquals(5f, g.move(0, 125f))
        assertEquals(Up.SCROLLED, g.up(0, 7))
        // 次の、止まっているときのタップは選ぶ。
        g.down(0, 100f, 7, flinging = false)
        assertEquals(Up.PICK, g.up(0, 7))
    }

    @Test fun a_second_finger_is_ignored() {
        g.down(0, 100f, 7, flinging = false)
        assertEquals(0f, g.move(1, 300f)); assertFalse(g.dragging); assertEquals(7, g.pressed)
        assertEquals(Up.IGNORE, g.up(1, index = 20)); assertEquals(7, g.pressed)
        assertEquals(0, g.pointer)
        assertEquals(Up.PICK, g.up(0, 7)); assertEquals(7, g.picked)
        // 最初の指が離れたあとは、残っている指も無視する。
        assertEquals(0f, g.move(1, 400f)); assertEquals(Up.IGNORE, g.up(1, 20))
    }

    @Test fun cancel_picks_nothing() {
        g.down(0, 100f, 7, flinging = false)
        g.cancel()
        assertEquals(none, g.pressed); assertEquals(none, g.pointer)
        assertEquals(0f, g.move(0, 200f)); assertFalse(g.dragging)
        assertEquals(Up.IGNORE, g.up(0, 7)); assertEquals(none, g.picked)
        // スクロールの途中で取り消されても同じ。
        g.down(0, 100f, 7, flinging = false); g.move(0, 150f); g.cancel()
        assertFalse(g.dragging); assertEquals(0f, g.move(0, 200f)); assertEquals(Up.IGNORE, g.up(0, 7))
    }

    @Test fun a_pick_does_not_leak_into_the_next_touch() {
        g.down(0, 100f, 7, flinging = false); g.up(0, 7)
        g.down(0, 100f, 9, flinging = false)
        assertEquals(none, g.picked)
        assertEquals(Up.NOTHING, g.up(0, 10)); assertEquals(none, g.picked)
    }
}
