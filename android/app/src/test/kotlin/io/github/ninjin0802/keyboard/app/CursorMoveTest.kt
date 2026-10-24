// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.EditorBridge
import io.github.ninjin0802.keyboard.app.input.EditorBridge.Companion.cursorDelta
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/** カーソルを [steps] 文字動かすときの、UTF-16 の単位での移動量。 */
class CursorMoveTest {
    private val face = "😀"   // サロゲートペア1組

    @Test fun plain_characters_move_one_unit_each() {
        assertEquals(-2, cursorDelta("かな", "漢字", -2)); assertEquals(2, cursorDelta("かな", "漢字", 2)); assertEquals(0, cursorDelta("a", "b", 0))
    }
    @Test fun a_surrogate_pair_is_crossed_whole() {
        assertEquals(-2, cursorDelta("a$face", "", -1)); assertEquals(2, cursorDelta("", "${face}b", 1))
        assertEquals(-3, cursorDelta("a$face", "", -2)); assertEquals(5, cursorDelta("", "${face}b$face", 3))
    }
    @Test fun stops_at_the_start_and_the_end() {
        assertEquals(-1, cursorDelta("a", "bc", -5)); assertEquals(2, cursorDelta("a", "bc", 5))
        assertEquals(0, cursorDelta("", "x", -1)); assertEquals(0, cursorDelta("x", "", 1))
    }
    @Test fun a_lone_surrogate_counts_as_one() {
        assertEquals(-1, cursorDelta("a\uDE00", "", -1)); assertEquals(1, cursorDelta("", "\uD83Da", 1))
    }
    @Test fun a_character_with_a_combining_mark_is_crossed_whole() {
        val ga = "か\u3099"   // か + 結合する濁点
        assertEquals(-2, cursorDelta("a$ga", "", -1)); assertEquals(2, cursorDelta("", "${ga}b", 1)); assertEquals(-3, cursorDelta("a$ga", "", -2))
    }
    @Test fun reads_sixteen_units_per_step() { assertEquals(16, EditorBridge.readLength(1)); assertEquals(48, EditorBridge.readLength(-3)) }
    @Test fun unreadable_text_gives_no_answer() { assertNull(cursorDelta(null, "a", -1)); assertNull(cursorDelta("a", null, 1)) }
    @Test fun only_the_side_being_moved_into_has_to_be_readable() {
        assertEquals(-1, cursorDelta("a", null, -1)); assertEquals(1, cursorDelta(null, "a", 1))
    }
}
