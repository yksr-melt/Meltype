// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

import io.github.ninjin0802.keyboard.engine.Clause
import io.github.ninjin0802.keyboard.engine.ConversionRequest
import io.github.ninjin0802.keyboard.engine.ConversionResult
import io.github.ninjin0802.keyboard.engine.Converter
import io.github.ninjin0802.keyboard.engine.run
import java.util.concurrent.Executor

/**
 * 変換を [worker] で行い、結果を [main] で返す。
 *
 * 「次に実行する依頼」は1件だけ持つ。打つのが変換より速いとき、間の依頼は実行せずに捨てる。
 * [worker] は1本のスレッドで順に実行するものを渡す。
 *
 * [converter] が何を投げても [worker] の外へは出さない（キーボードごと落ちるのを防ぐ）。
 * 変換は失敗として結果を返し、学習はあきらめる。
 */
class ConversionRunner(
    private val worker: Executor,
    private val main: Executor,
    private val converter: Converter,
    /** [worker] の上で思いがけない例外が出たときに、[worker] の上で呼ばれる。どちらの作業の途中だったかを付ける。 */
    private val onError: (Step, Throwable) -> Unit = { _, _ -> },
) {
    enum class Step { CONVERT, LEARN }


    private class Pending(val request: ConversionRequest, val context: String?, val onResult: (ConversionResult) -> Unit)

    private val lock = Any()
    private var pending: Pending? = null

    /** どのスレッドから呼んでもよい。[onResult] は [main] で呼ばれる。 */
    fun submit(request: ConversionRequest, context: String?, onResult: (ConversionResult) -> Unit) {
        synchronized(lock) { pending = Pending(request, context, onResult) }
        worker.execute(::runPending)
    }

    /** 確定した内容を覚えさせる。頼んだ順に、変換と同じスレッドで行う。 */
    fun learn(clauses: List<Clause>, context: String?) {
        worker.execute {
            try {
                converter.learn(clauses, context)
            } catch (t: Throwable) {
                onError(Step.LEARN, t)
            }
        }
    }

    /** まだ始まっていない変換の依頼を捨てる（学習は捨てない）。 */
    fun cancelPending() {
        synchronized(lock) { pending = null }
    }

    private fun runPending() {
        val next = synchronized(lock) { pending.also { pending = null } } ?: return
        val result = try {
            converter.run(next.request, next.context)
        } catch (t: Throwable) {
            onError(Step.CONVERT, t)
            ConversionResult(next.request.id, null, emptyList())
        }
        main.execute { next.onResult(result) }
    }
}
