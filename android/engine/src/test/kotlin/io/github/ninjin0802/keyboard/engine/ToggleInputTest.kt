// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.*

class ToggleInputTest {
    private val s = InputSession(SessionConfig(toggleEnabled = true, toggleWaitMs = 1000))
    private fun tap(key: KanaKey, t: Long) = s.handle(Operation.Tap(key, t)).composing

    @Test fun same_key_cycles_and_wraps() =
        assertEquals(listOf("あ", "い", "う", "え", "お", "あ"), (0L..5L).map { tap(KanaKeys.A, it * 100) })
    @Test fun wait_time_boundary() {
        tap(KanaKeys.A, 0); assertEquals("い", tap(KanaKeys.A, 1000)); assertEquals("いあ", tap(KanaKeys.A, 2001))
    }
    @Test fun custom_wait_time() {
        s.config = s.config.copy(toggleWaitMs = 300)
        tap(KanaKeys.A, 0); assertEquals("ああ", tap(KanaKeys.A, 301))
    }
    @Test fun other_key_ends_cycle() { tap(KanaKeys.A, 0); tap(KanaKeys.KA, 100); assertEquals("あかあ", tap(KanaKeys.A, 200)) }
    @Test fun flick_insert_is_not_cycled() { s.handle(Operation.Insert('あ')); assertEquals("ああ", tap(KanaKeys.A, 0)) }
    @Test fun right_fixes_char() { tap(KanaKeys.A, 0); assertEquals("あ", s.handle(Operation.Right).composing); assertEquals("ああ", tap(KanaKeys.A, 100)) }
    @Test fun modify_applies_then_ends_cycle() {
        tap(KanaKeys.HA, 0); tap(KanaKeys.HA, 100)                    // ひ
        assertEquals("び", s.handle(Operation.Modify).composing); assertEquals("びは", tap(KanaKeys.HA, 200))
    }
    @Test fun delete_ends_cycle() { tap(KanaKeys.A, 0); tap(KanaKeys.A, 100); s.handle(Operation.Delete); assertEquals("あ", tap(KanaKeys.A, 200)) }
    @Test fun off_inserts_same_char() {
        s.config = s.config.copy(toggleEnabled = false); tap(KanaKeys.A, 0); assertEquals("ああ", tap(KanaKeys.A, 100))
    }
    @Test fun toggle_issues_a_new_request_in_normal_mode() {
        val first = s.handle(Operation.Tap(KanaKeys.A, 0)).request
        val second = s.handle(Operation.Tap(KanaKeys.A, 100)).request
        assertEquals("い", second?.reading); assertTrue(second!!.id > first!!.id)
    }
}
