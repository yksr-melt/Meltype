// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.ShiftState
import kotlin.test.Test
import kotlin.test.assertEquals

class ShiftStateTest {
    @Test fun press_cycles() = assertEquals(listOf(ShiftState.ONCE, ShiftState.LOCKED, ShiftState.OFF),
        generateSequence(ShiftState.OFF.onShiftPressed()) { it.onShiftPressed() }.take(3).toList())
    @Test fun once_clears_after_char_but_locked_stays() { assertEquals(ShiftState.OFF, ShiftState.ONCE.onCharTyped()); assertEquals(ShiftState.LOCKED, ShiftState.LOCKED.onCharTyped()) }
    @Test fun apply_uppercases() { assertEquals("A", ShiftState.ONCE.apply("a")); assertEquals("a", ShiftState.OFF.apply("a")); assertEquals(",", ShiftState.LOCKED.apply(",")) }
}
