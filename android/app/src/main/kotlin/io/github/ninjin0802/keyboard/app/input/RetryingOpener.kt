// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

/**
 * 開くのに失敗することがあるもの（Mozc）を、あとでもう一度開こうとする。
 *
 * 失敗したら、[minIntervalMs] たつまではやり直さない。やり直しは [maxRetries] 回まで。
 * それでも開けなければ、あとは何もせず null を返し続ける。一度開けたら、同じものを返す。
 *
 * [get] の中で [open] を呼ぶ。[open] は時間がかかるので、[get] は変換用のスレッドからだけ呼ぶ
 * （同時に呼ばれることは考えていない）。[now] はミリ秒の時計。
 */
class RetryingOpener<T : Any>(
    private val open: () -> T?,
    private val now: () -> Long,
    private val maxRetries: Int = 5,
    private val minIntervalMs: Long = 30_000,
) {
    private var value: T? = null
    private var failures = 0
    private var lastFailureMs = 0L

    fun get(): T? {
        value?.let { return it }
        if (!mayAttempt()) return null
        val opened = try {
            open()
        } catch (t: Throwable) {
            null
        }
        if (opened == null) {
            failures++
            lastFailureMs = now()   // 終わった時刻から数える（長くかかった試みのすぐあとに、次を始めない）
        }
        value = opened
        return opened
    }

    private fun mayAttempt(): Boolean = when {
        failures == 0 -> true
        failures > maxRetries -> false
        else -> now() - lastFailureMs >= minIntervalMs
    }
}
