// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.*

class DirectModeTest {
    private val s = InputSession(SessionConfig(direct = true, toggleEnabled = true))
    @Test fun keeps_only_last_char_composing() {
        val a = s.handle(Operation.Insert('か')); val b = s.handle(Operation.Insert('な'))
        assertEquals("" to "か", a.commit to a.composing); assertEquals("か" to "な", b.commit to b.composing)
        assertNull(b.request); assertTrue(b.candidates.isEmpty())
    }
    @Test fun modify_and_toggle_work_on_last_char() {
        s.handle(Operation.Insert('は')); assertEquals("ば", s.handle(Operation.Modify).composing)
        s.handle(Operation.Tap(KanaKeys.A, 0)); assertEquals("い", s.handle(Operation.Tap(KanaKeys.A, 100)).composing)
    }
    @Test fun commit_and_space_flush_without_learning() {
        s.handle(Operation.Insert('か')); val u = s.handle(Operation.Space)
        assertEquals("か", u.commit); assertNull(u.learn); assertFalse(s.isComposing)
    }
    @Test fun commit_and_insert_never_learn() {
        s.handle(Operation.Insert('か')); assertNull(s.handle(Operation.Insert('き')).learn)
        assertNull(s.handle(Operation.Commit).learn)
    }
}
