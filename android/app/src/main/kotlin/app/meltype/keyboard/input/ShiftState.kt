// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
package app.meltype.keyboard.input

sealed interface ShiftState {
    data object Off : ShiftState
    data class Once(val pressedAtMs: Long) : ShiftState
    data object Locked : ShiftState

    /**
     * 1 回押すと次の 1 文字だけ大文字 (Once)。[doubleTapMs] 以内に続けて押したときだけ固定 (Locked) にする。
     * Once のまま間を空けて押したとき・Locked で押したときは Off に戻す。
     * 押すたびに Locked まで回ると、大文字を 1 文字だけ打つつもりで 2 回触れたときに固定されてしまう。
     */
    fun onShiftPressed(timeMs: Long, doubleTapMs: Int): ShiftState = when (this) {
        Off -> Once(timeMs)
        is Once -> if (timeMs - pressedAtMs <= doubleTapMs) Locked else Off
        Locked -> Off
    }

    fun onCharTyped(): ShiftState = if (this is Once) Off else this

    fun apply(text: String): String = if (this == Off) text else text.uppercase()
}
