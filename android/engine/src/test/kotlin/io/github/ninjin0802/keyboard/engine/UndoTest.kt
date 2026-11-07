// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.*

/** 戻すキー（spec 05「戻す」）。 */
class UndoTest {
    private val s = InputSession(SessionConfig(toggleEnabled = true, toggleWaitMs = 1000))
    private fun tap(key: KanaKey, t: Long) = s.handle(Operation.Tap(key, t)).composing
    private fun undo(t: Long) = s.handle(Operation.Undo(t)).composing

    @Test fun steps_back_through_the_cycle() {
        tap(KanaKeys.A, 0); tap(KanaKeys.A, 100); tap(KanaKeys.A, 200)             // う
        assertEquals("い", undo(300)); assertEquals("あ", undo(400))
    }
    @Test fun wraps_from_the_first_to_the_last() { tap(KanaKeys.A, 0); assertEquals("お", undo(100)); assertEquals("え", undo(200)) }
    @Test fun only_the_last_character_changes() { s.handle(Operation.Insert('か')); tap(KanaKeys.A, 0); tap(KanaKeys.A, 100); assertEquals("かあ", undo(200)) }
    @Test fun the_cycle_stays_alive_and_the_wait_restarts_at_the_undo() {
        tap(KanaKeys.A, 0); tap(KanaKeys.A, 100); undo(900)                          // あ
        assertEquals("い", tap(KanaKeys.A, 1900))                                    // 900 から 1000 ms 以内
    }
    @Test fun after_the_wait_it_does_nothing_and_the_character_is_decided() {
        tap(KanaKeys.A, 0); tap(KanaKeys.A, 100)                                     // い
        assertEquals("い", undo(1101)); assertEquals("いあ", tap(KanaKeys.A, 1200))
    }
    @Test fun issues_a_new_request() {
        val first = s.handle(Operation.Tap(KanaKeys.A, 0)).request
        val second = s.handle(Operation.Undo(100)).request
        assertEquals("お", second?.reading); assertTrue(second!!.id > first!!.id)
    }
    @Test fun nothing_to_undo_after_a_flick_or_another_operation() {
        s.handle(Operation.Insert('あ')); assertEquals("あ", undo(0))
        tap(KanaKeys.A, 100); s.handle(Operation.Modify); assertEquals("あぁ", undo(200))
        assertNull(s.handle(Operation.Undo(300)).request)
    }
    @Test fun empty_session_stays_empty() = assertEquals(Update(), s.handle(Operation.Undo(0)))
    @Test fun toggle_off_does_nothing() {
        s.config = s.config.copy(toggleEnabled = false); tap(KanaKeys.A, 0); tap(KanaKeys.A, 100)
        assertEquals("ああ", undo(200))
    }
    @Test fun password_field_does_nothing() {
        val p = InputSession(SessionConfig(direct = true, holdLast = false, toggleEnabled = true))
        p.handle(Operation.Tap(KanaKeys.A, 0)); assertEquals(Update(), p.handle(Operation.Undo(100)))
    }
    @Test fun works_on_the_held_character_of_a_no_convert_field() {
        val d = InputSession(SessionConfig(direct = true, toggleEnabled = true))
        d.handle(Operation.Tap(KanaKeys.KA, 0)); d.handle(Operation.Tap(KanaKeys.KA, 100))
        val u = d.handle(Operation.Undo(200)); assertEquals("" to "か", u.commit to u.composing); assertNull(u.request)
    }

    private val table = mapOf("あめ" to listOf(Segment("あめ", listOf("雨", "飴"))))
    private fun selecting(): InputSession {
        val t = InputSession(SessionConfig(toggleEnabled = true))
        val c = FakeConverter(table)
        for (ch in "あめ") t.handle(Operation.Insert(ch)).request?.let { r -> t.onConversion(ConversionResult(r.id, c.convert(r.reading, null), emptyList())) }
        return t
    }
    @Test fun clears_the_selection_and_keeps_the_reading() {
        val t = selecting()
        val picked = t.handle(Operation.Space); assertNotNull(picked.selected); assertEquals("雨", picked.composing)
        val u = t.handle(Operation.Undo(0))
        assertNull(u.selected); assertEquals("あめ", u.composing); assertEquals("", u.commit); assertTrue(t.isComposing)
    }
    @Test fun cancels_a_space_that_is_still_waiting_for_the_result() {
        val t = InputSession()
        val r = t.handle(Operation.Insert('あ')).request!!
        t.handle(Operation.Space); t.handle(Operation.Undo(0))
        assertNull(t.onConversion(ConversionResult(r.id, null, emptyList()))!!.selected)
    }
}
