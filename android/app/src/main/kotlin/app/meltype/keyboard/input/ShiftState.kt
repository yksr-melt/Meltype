// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package app.meltype.keyboard.input

enum class ShiftState {
    OFF, ONCE, LOCKED;

    /**
     * 1 回押すと次の 1 文字だけ大文字 (ONCE)。続けて素早く押したときだけ固定 (LOCKED) にする。
     * ONCE のまま間を空けて押したとき・LOCKED で押したときは OFF に戻す。
     * 押すたびに LOCKED まで回ると、大文字を 1 文字だけ打つつもりで 2 回触れたときに固定されてしまう。
     */
    fun onShiftPressed(doubleTap: Boolean): ShiftState = when (this) {
        OFF -> ONCE
        ONCE -> if (doubleTap) LOCKED else OFF
        LOCKED -> OFF
    }

    fun onCharTyped(): ShiftState = if (this == ONCE) OFF else this

    fun apply(text: String): String = if (this == OFF) text else text.uppercase()
}
