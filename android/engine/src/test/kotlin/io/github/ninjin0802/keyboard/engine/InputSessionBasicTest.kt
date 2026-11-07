// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class InputSessionBasicTest {
    private val s = InputSession()
    private fun type(text: String): Update = text.map { s.handle(Operation.Insert(it)) }.last()

    @Test fun empty_session_is_not_composing() { assertFalse(s.isComposing); assertEquals(Update(), s.handle(Operation.Commit)) }
    @Test fun insert_appends_to_reading() { val u = type("かな"); assertEquals("かな", u.composing); assertEquals("", u.commit); assertTrue(s.isComposing) }
    @Test fun tap_inserts_first_char_when_toggle_off() = assertEquals("かか",
        listOf(1000L, 1100L).map { s.handle(Operation.Tap(KanaKeys.KA, it)) }.last().composing)
    @Test fun modify_changes_last_char() { type("はは"); assertEquals("はば", s.handle(Operation.Modify).composing); assertEquals("はぱ", s.handle(Operation.Modify).composing) }
    @Test fun modify_on_empty_or_unmodifiable_does_nothing() { assertEquals(Update(), s.handle(Operation.Modify)); type("な"); assertEquals("な", s.handle(Operation.Modify).composing) }
    @Test fun delete_removes_last_char_and_ends_composition() {
        type("かな"); assertEquals("か", s.handle(Operation.Delete).composing)
        assertEquals(Update(), s.handle(Operation.Delete).copy(request = null)); assertFalse(s.isComposing)
    }
    @Test fun commit_commits_display_and_clears() { type("かな"); val u = s.handle(Operation.Commit); assertEquals("かな", u.commit); assertEquals("", u.composing); assertFalse(s.isComposing) }
    @Test fun reading_over_limit_commits_first() {
        type("あ".repeat(100)); val u = s.handle(Operation.Insert('か'))
        assertEquals("あ".repeat(100), u.commit); assertEquals("か", u.composing)
    }
    @Test fun reset_discards_without_commit() { type("かな"); s.reset(); assertFalse(s.isComposing); assertEquals(Update(), s.handle(Operation.Commit)) }
    @Test fun run_rejects_segments_whose_readings_do_not_match() {
        val c = FakeConverter(mapOf("かな" to listOf(Segment("か", listOf("蚊")))))
        assertNull(c.run(ConversionRequest(1, "かな"), null).segments)
    }
}
