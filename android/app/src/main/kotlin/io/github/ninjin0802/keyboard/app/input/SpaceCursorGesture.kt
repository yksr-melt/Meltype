// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

/**
 * 空白キーの長押しでカーソルを動かす操作（spec 04「空白キーの長押し」）。指の位置と時刻を受け取り、
 * カーソルを何文字動かすかを答える。時計も入力欄も見ない。
 *
 * [stepPx]: カーソル1文字ぶんの、指の横の移動量（px）。
 */
class SpaceCursorGesture(private val stepPx: Float) {
    private var pressed = false
    private var downX = 0f
    private var downY = 0f
    private var downTime = 0L
    private var slop = 0f
    private var lastX = 0f

    /** タップの範囲を超えて動いた。この押下では、もうカーソルを動かす状態に入らない。 */
    private var moved = false

    /** ここから [stepPx] 動くごとに1文字。動かすたびに、その分だけ進める（端数は残る）。 */
    private var anchorX = 0f

    /** カーソルを動かす状態にあるか。 */
    var active = false; private set

    fun down(x: Float, y: Float, slopPx: Float, timeMs: Long) {
        pressed = true; active = false; moved = false
        downX = x; downY = y; lastX = x; slop = slopPx; downTime = timeMs
    }

    /** 押し続けの時間がたったかを確かめる。カーソルを動かす状態に入ったら true（入るのは1回だけ）。 */
    fun longPress(timeMs: Long): Boolean {
        if (!pressed || active || moved || timeMs - downTime < LONG_PRESS_MS) return false
        active = true
        anchorX = lastX
        return true
    }

    /** 指が動いた。カーソルを動かす文字数を返す（右が正、左が負）。動かす状態でなければ 0。縦の動きは見ない。 */
    fun move(x: Float, y: Float): Int {
        if (!pressed) return 0
        lastX = x
        if (!active) {
            val dx = x - downX
            val dy = y - downY
            if (dx * dx + dy * dy >= slop * slop) moved = true
            return 0
        }
        val steps = ((x - anchorX) / stepPx).toInt()
        anchorX += steps * stepPx
        return steps
    }

    /** 指が離れた（または、ほかの指に押下を譲った）。カーソルを動かす状態だったら true（空白は入れない）。 */
    fun end(): Boolean {
        val was = active
        cancel()
        return was
    }

    fun cancel() { pressed = false; active = false }

    companion object {
        /** カーソルを動かす状態に入るまでの、押し続けの時間。 */
        const val LONG_PRESS_MS = 300L

        /** カーソル1文字ぶんの、指の横の移動量（dp）。 */
        const val STEP_DP = 12f
    }
}
