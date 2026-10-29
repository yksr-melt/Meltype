// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

import io.github.ninjin0802.keyboard.engine.FlickKey
import kotlin.math.abs

/** 順番は FlickKey.chars の並び（タップ、左、上、右、下）と同じ。 */
enum class Direction { TAP, LEFT, UP, RIGHT, DOWN }

object FlickDetector {
    /** 移動の長さがキー幅の 1/4 未満ならタップ。以上なら大きく動いた軸の向き。 */
    fun direction(dx: Float, dy: Float, keyWidth: Float): Direction {
        val threshold = keyWidth / 4
        if (dx * dx + dy * dy < threshold * threshold) return Direction.TAP
        return if (abs(dx) >= abs(dy)) {
            if (dx < 0) Direction.LEFT else Direction.RIGHT
        } else {
            if (dy < 0) Direction.UP else Direction.DOWN
        }
    }
}

/** [d] の向きの文字。その向きに文字のないキーでは null。 */
fun FlickKey.charAt(d: Direction): Char? = charAt(d.ordinal)
