// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

/**
 * 押し続けの繰り返し。すぐ1回、400 ms 後から 50 ms ごと。
 *
 * 時刻が来るたびに alive を確かめ、false なら動作を起こさずに自分で止まる（止める呼び出しが抜けても、押下より長くは続かない）。
 */
class KeyRepeater(
    private val schedule: (delayMs: Long, task: () -> Unit) -> Unit,
    private val cancel: () -> Unit,
) {
    private var generation = 0

    fun start(alive: () -> Boolean = { true }, action: () -> Unit) {
        stop()
        val gen = generation
        action()
        if (gen != generation) return   // 動作の中から止められた
        lateinit var tick: () -> Unit
        tick = {
            if (gen == generation) {
                if (alive()) {
                    action()
                    if (gen == generation) schedule(REPEAT_MS, tick)
                } else {
                    stop()
                }
            }
        }
        schedule(FIRST_DELAY_MS, tick)
    }

    fun stop() {
        generation++
        cancel()
    }

    private companion object {
        const val FIRST_DELAY_MS = 400L
        const val REPEAT_MS = 50L
    }
}
