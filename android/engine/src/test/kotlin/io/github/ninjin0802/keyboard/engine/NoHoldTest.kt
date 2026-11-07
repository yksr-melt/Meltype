// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.*

/** パスワードの欄: 変換しないうえに、最後の1文字も持たない（spec 09）。 */
class NoHoldTest {
    private val s = InputSession(SessionConfig(direct = true, holdLast = false, toggleEnabled = true))

    @Test fun each_kana_is_committed_at_once_and_nothing_is_held() {
        val a = s.handle(Operation.Insert('か')); val b = s.handle(Operation.Insert('な'))
        assertEquals("か" to "", a.commit to a.composing); assertEquals("な" to "", b.commit to b.composing)
        assertFalse(s.isComposing)
        assertNull(b.request); assertNull(b.learn); assertTrue(b.candidates.isEmpty())
    }
    @Test fun toggle_input_is_inert() {
        val taps = listOf(0L, 100L, 200L).map { s.handle(Operation.Tap(KanaKeys.A, it)) }
        assertEquals(listOf("あ", "あ", "あ"), taps.map { it.commit })
        assertEquals(listOf("", "", ""), taps.map { it.composing })
    }
    @Test fun modify_delete_space_commit_have_nothing_to_act_on() {
        s.handle(Operation.Insert('か'))
        for (op in listOf(Operation.Modify, Operation.Delete, Operation.Space, Operation.Commit, Operation.Right)) {
            assertEquals(Update(), s.handle(op), "$op")
        }
    }
    @Test fun a_character_held_before_the_field_stopped_holding_is_flushed_first() {
        s.config = s.config.copy(holdLast = true); s.handle(Operation.Insert('か'))
        s.config = s.config.copy(holdLast = false)
        val u = s.handle(Operation.Insert('な'))
        assertEquals("かな" to "", u.commit to u.composing); assertFalse(s.isComposing)
    }
    @Test fun other_no_convert_fields_still_hold_the_last_character() {
        val t = InputSession(SessionConfig(direct = true))
        assertEquals("か", t.handle(Operation.Insert('か')).composing)
        assertEquals("が", t.handle(Operation.Modify).composing)
    }
    @Test fun the_flag_means_nothing_in_a_converting_field() {
        val t = InputSession(SessionConfig(direct = false, holdLast = false))
        val u = t.handle(Operation.Insert('か'))
        assertEquals("" to "か", u.commit to u.composing); assertNotNull(u.request)
    }
}
