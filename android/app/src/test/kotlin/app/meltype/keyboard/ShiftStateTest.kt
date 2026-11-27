// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
package app.meltype.keyboard

import app.meltype.keyboard.input.ShiftState
import kotlin.test.Test
import kotlin.test.assertEquals

class ShiftStateTest {
    private fun press(vararg timesMs: Long) =
        timesMs.fold<ShiftState>(ShiftState.Off) { state, timeMs -> state.onShiftPressed(timeMs, doubleTapMs = 300) }

    // 間を空けて押すと、1 文字だけ大文字 → なし を繰り返し、固定にはならない。
    @Test fun slow_presses_toggle_once_without_locking() {
        assertEquals(ShiftState.Once(1000), press(1000))
        assertEquals(ShiftState.Off, press(1000, 1301))
        assertEquals(ShiftState.Once(1602), press(1000, 1301, 1602))
        assertEquals(ShiftState.Off, press(1000, 1301, 1602, 1903))
    }
    // 素早く 2 回押すと固定。固定中はもう 1 回押せば (速さによらず) なしに戻る。
    @Test fun double_tap_locks_and_next_press_releases() {
        assertEquals(ShiftState.Locked, press(1000, 1300))
        assertEquals(ShiftState.Off, press(1000, 1300, 1400))
        assertEquals(ShiftState.Off, press(1000, 1300, 5000))
    }
    // 固定を解いた直後に押しても、固定し直さず 1 文字だけ大文字になる。
    @Test fun press_right_after_release_is_once() = assertEquals(ShiftState.Once(1300), press(1000, 1100, 1200, 1300))
    @Test fun once_clears_after_char_but_locked_stays() { assertEquals(ShiftState.Off, ShiftState.Once(1000).onCharTyped()); assertEquals(ShiftState.Locked, ShiftState.Locked.onCharTyped()) }
    @Test fun apply_uppercases() { assertEquals("A", ShiftState.Once(1000).apply("a")); assertEquals("a", ShiftState.Off.apply("a")); assertEquals(",", ShiftState.Locked.apply(",")) }
}
