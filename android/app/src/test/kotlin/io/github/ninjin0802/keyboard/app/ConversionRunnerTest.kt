// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.ConversionRunner
import io.github.ninjin0802.keyboard.engine.Clause
import io.github.ninjin0802.keyboard.engine.ConversionRequest
import io.github.ninjin0802.keyboard.engine.ConversionResult
import io.github.ninjin0802.keyboard.engine.Converter
import io.github.ninjin0802.keyboard.engine.Segment
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse

class ConversionRunnerTest {
    private val queued = ArrayDeque<Runnable>()
    private val delivered = ArrayDeque<Runnable>()
    private val conv = FakeAppConverter()
    private val runner = ConversionRunner({ queued.add(it) }, { it.run() }, conv)
    private val steps = mutableListOf<ConversionRunner.Step>()
    private fun drain() { while (queued.isNotEmpty()) queued.removeFirst().run() }

    @Test fun only_latest_pending_request_runs() {
        val got = mutableListOf<Long>()
        for (i in 1L..3L) runner.submit(ConversionRequest(i, "あ".repeat(i.toInt())), null) { got += it.id }
        drain()
        assertEquals(listOf(3L), got)
        assertEquals(listOf("あああ"), conv.converted)
    }

    @Test fun cancel_pending_drops_request() {
        var called = false
        runner.submit(ConversionRequest(1, "あ"), null) { called = true }
        runner.cancelPending()
        drain()
        assertFalse(called)
    }

    @Test fun learn_is_forwarded_in_order() {
        runner.learn(listOf(Clause("あめ", "雨")), null)
        runner.learn(listOf(Clause("かさ", "傘")), null)
        drain()
        assertEquals(listOf("あめ", "かさ"), conv.learned.map { it[0].reading })
    }

    @Test fun request_submitted_after_one_started_also_runs() {
        val got = mutableListOf<Long>()
        runner.submit(ConversionRequest(1, "あ"), null) { got += it.id }
        drain()
        runner.submit(ConversionRequest(2, "あい"), "前") { got += it.id }
        drain()
        assertEquals(listOf(1L, 2L), got)
        assertEquals(listOf(null, "前"), conv.contexts)
    }

    @Test fun result_is_delivered_through_main_executor() {
        val runner = ConversionRunner({ queued.add(it) }, { delivered.add(it) }, conv)
        val got = mutableListOf<Long>()
        runner.submit(ConversionRequest(7, "あ"), null) { got += it.id }
        drain()
        assertEquals(emptyList(), got)
        delivered.removeFirst().run()
        assertEquals(listOf(7L), got)
    }

    @Test fun cancel_does_not_drop_learning() {
        runner.learn(listOf(Clause("あめ", "雨")), null)
        runner.cancelPending()
        drain()
        assertEquals(1, conv.learned.size)
    }

    private class ThrowingConverter : Converter {
        var broken = true
        override fun convert(reading: String, context: String?): List<Segment>? {
            if (broken) throw IllegalStateException("x")
            return listOf(Segment(reading, listOf(reading)))
        }
        override fun predict(reading: String): List<String> = emptyList()
        override fun learn(clauses: List<Clause>, context: String?) { throw NoClassDefFoundError("x") }
    }

    @Test fun converter_that_throws_gives_a_failed_result_and_the_runner_keeps_working() {
        val throwing = ThrowingConverter()
        val errors = mutableListOf<Throwable>()
        val runner = ConversionRunner({ queued.add(it) }, { it.run() }, throwing) { step, t -> steps += step; errors += t }
        val got = mutableListOf<ConversionResult>()
        runner.submit(ConversionRequest(1, "あ"), null) { got += it }
        drain()
        assertEquals(listOf(ConversionResult(1, null, emptyList())), got)
        assertEquals(listOf("IllegalStateException"), errors.map { it.javaClass.simpleName })
        assertEquals(listOf(ConversionRunner.Step.CONVERT), steps)

        throwing.broken = false
        runner.submit(ConversionRequest(2, "い"), null) { got += it }
        drain()
        assertEquals(2L, got[1].id)
        assertEquals(listOf(Segment("い", listOf("い"))), got[1].segments)
    }

    @Test fun learning_that_throws_is_contained() {
        val errors = mutableListOf<Throwable>()
        val runner = ConversionRunner({ queued.add(it) }, { it.run() }, ThrowingConverter()) { step, t -> steps += step; errors += t }
        runner.learn(listOf(Clause("あめ", "雨")), null)
        drain()
        assertEquals(listOf("NoClassDefFoundError"), errors.map { it.javaClass.simpleName })
        assertEquals(listOf(ConversionRunner.Step.LEARN), steps)
    }
}
