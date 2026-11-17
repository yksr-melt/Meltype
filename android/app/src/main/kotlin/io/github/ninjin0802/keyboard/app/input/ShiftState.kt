// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

enum class ShiftState {
    OFF, ONCE, LOCKED;

    fun onShiftPressed(): ShiftState = when (this) {
        OFF -> ONCE
        ONCE -> LOCKED
        LOCKED -> OFF
    }

    fun onCharTyped(): ShiftState = if (this == ONCE) OFF else this

    fun apply(text: String): String = if (this == OFF) text else text.uppercase()
}
