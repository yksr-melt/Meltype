// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package app.meltype.keyboard

import app.meltype.keyboard.input.ShiftState
import kotlin.test.Test
import kotlin.test.assertEquals

class ShiftStateTest {
    // 間を空けて押すと、1 文字だけ大文字 → なし を繰り返し、固定にはならない。
    @Test fun slow_presses_toggle_once_without_locking() = assertEquals(listOf(ShiftState.ONCE, ShiftState.OFF, ShiftState.ONCE, ShiftState.OFF),
        generateSequence(ShiftState.OFF.onShiftPressed(doubleTap = false)) { it.onShiftPressed(doubleTap = false) }.take(4).toList())
    // 素早く 2 回押すと固定。固定中はもう 1 回押せば (速さによらず) なしに戻る。
    @Test fun double_tap_locks_and_next_press_releases() {
        assertEquals(ShiftState.LOCKED, ShiftState.ONCE.onShiftPressed(doubleTap = true))
        assertEquals(ShiftState.OFF, ShiftState.LOCKED.onShiftPressed(doubleTap = false))
        assertEquals(ShiftState.OFF, ShiftState.LOCKED.onShiftPressed(doubleTap = true))
    }
    // なしからは、速さによらず 1 文字だけ大文字になる (前の固定を解いた直後の連打で固定し直さない)。
    @Test fun press_from_off_is_always_once() {
        assertEquals(ShiftState.ONCE, ShiftState.OFF.onShiftPressed(doubleTap = false))
        assertEquals(ShiftState.ONCE, ShiftState.OFF.onShiftPressed(doubleTap = true))
    }
    @Test fun once_clears_after_char_but_locked_stays() { assertEquals(ShiftState.OFF, ShiftState.ONCE.onCharTyped()); assertEquals(ShiftState.LOCKED, ShiftState.LOCKED.onCharTyped()) }
    @Test fun apply_uppercases() { assertEquals("A", ShiftState.ONCE.apply("a")); assertEquals("a", ShiftState.OFF.apply("a")); assertEquals(",", ShiftState.LOCKED.apply(",")) }
}
