// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

class LiveConversionTest {
    private val s = InputSession()
    private fun type(text: String): Update = text.map { s.handle(Operation.Insert(it)) }.last()
    private fun segs(vararg p: Pair<String, String>) = p.map { Segment(it.first, listOf(it.second)) }

    @Test fun every_change_requests_whole_reading_with_increasing_id() {
        val a = type("き").request!!; val b = type("ょ").request!!
        assertEquals("き", a.reading); assertEquals("きょ", b.reading); assertTrue(b.id > a.id)
    }
    @Test fun short_reading_stays_kana_even_with_result() {
        val r = type("きょう").request!!
        assertEquals("きょう", s.onConversion(ConversionResult(r.id, segs("きょう" to "今日"), emptyList()))!!.composing)
    }
    @Test fun four_chars_show_converted_text() {
        val r = type("きょうは").request!!
        assertEquals("今日は", s.onConversion(ConversionResult(r.id, segs("きょうは" to "今日は"), emptyList()))!!.composing)
    }
    @Test fun stale_result_is_dropped() {
        val old = type("きょうは").request!!; type("て")
        assertNull(s.onConversion(ConversionResult(old.id, segs("きょうは" to "今日は"), emptyList())))
    }
    @Test fun while_waiting_keeps_converted_prefix_plus_new_kana() {
        val r = type("きょうは").request!!
        s.onConversion(ConversionResult(r.id, segs("きょうは" to "今日は"), emptyList()))
        assertEquals("今日はて", type("て").composing)
    }
    @Test fun while_waiting_after_delete_shows_reading() {
        val r = type("きょうはて").request!!
        s.onConversion(ConversionResult(r.id, segs("きょうは" to "今日は", "て" to "手"), emptyList()))
        assertEquals("きょうは", s.handle(Operation.Delete).composing)
    }
    @Test fun failed_conversion_shows_reading() {
        val r = type("きょうは").request!!
        assertEquals("きょうは", s.onConversion(ConversionResult(r.id, null, emptyList()))!!.composing)
    }
    @Test fun commit_after_result_commits_converted_text() {
        val r = type("きょうは").request!!
        s.onConversion(ConversionResult(r.id, segs("きょうは" to "今日は"), emptyList()))
        assertEquals("今日は", s.handle(Operation.Commit).commit)
    }
    @Test fun result_after_commit_is_dropped() {
        val r = type("きょうは").request!!; s.handle(Operation.Commit)
        assertNull(s.onConversion(ConversionResult(r.id, segs("きょうは" to "今日は"), emptyList())))
    }
    @Test fun emptied_reading_has_no_request() {
        type("あ")
        assertNull(s.handle(Operation.Delete).request)
    }
    @Test fun direct_mode_never_requests() {
        s.config = SessionConfig(direct = true)
        assertNull(type("きょうは").request)
    }
}
