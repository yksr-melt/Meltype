// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.*

class SelectionTest {
    private val s = InputSession()
    private val segs = listOf(Segment("きょうは", listOf("今日は", "京は")), Segment("てんき", listOf("天気", "転機")))
    private fun typeAndConvert(reading: String, segments: List<Segment>?, predictions: List<String> = emptyList()): Update {
        val r = reading.map { s.handle(Operation.Insert(it)) }.last().request!!
        return s.onConversion(ConversionResult(r.id, segments, predictions))!!
    }
    @Test fun first_space_skips_candidate_already_shown() {
        typeAndConvert("きょうはてんき", segs)
        val u = s.handle(Operation.Space); assertEquals(1, u.selected); assertEquals("京はてんき", u.composing)
    }
    @Test fun first_space_on_short_reading_selects_first() {
        typeAndConvert("あめ", listOf(Segment("あめ", listOf("雨", "飴"))))
        val u = s.handle(Operation.Space); assertEquals(0, u.selected); assertEquals("雨", u.composing)
    }
    @Test fun space_wraps_around() {
        typeAndConvert("あめ", listOf(Segment("あめ", listOf("雨"))))   // 候補: 雨, あめ, アメ
        assertEquals(listOf(0, 1, 2, 0), List(4) { s.handle(Operation.Space).selected })
    }
    @Test fun space_before_result_selects_when_result_arrives() {
        val r = "あめ".map { s.handle(Operation.Insert(it)) }.last().request!!
        assertNull(s.handle(Operation.Space).selected)
        val u = s.onConversion(ConversionResult(r.id, listOf(Segment("あめ", listOf("雨"))), emptyList()))!!
        assertEquals(0, u.selected); assertEquals("雨", u.composing)
    }
    @Test fun space_without_composition_does_nothing() = assertEquals(Update(), s.handle(Operation.Space))
    @Test fun pick_partial_commits_and_keeps_rest() {
        typeAndConvert("きょうはてんき", segs)
        val u = s.handle(Operation.Pick(1))
        assertEquals("京は", u.commit); assertEquals("てんき", u.composing); assertEquals("てんき", u.request!!.reading)
        assertEquals(listOf(Clause("きょうは", "京は")), u.learn)
    }
    @Test fun pick_whole_commits_everything() {
        typeAndConvert("きょうはてんき", segs)
        val u = s.handle(Operation.Pick(0))
        assertEquals("今日は天気", u.commit); assertEquals("", u.composing); assertFalse(s.isComposing)
    }
    @Test fun pick_out_of_range_does_nothing() { typeAndConvert("あめ", null); assertEquals("", s.handle(Operation.Pick(9)).commit); assertTrue(s.isComposing) }
    @Test fun insert_while_selected_commits_selection_first() {
        typeAndConvert("あめ", listOf(Segment("あめ", listOf("雨")))); s.handle(Operation.Space)
        val u = s.handle(Operation.Insert('が')); assertEquals("雨", u.commit); assertEquals("が", u.composing)
    }
    @Test fun delete_while_selected_only_unselects() {
        typeAndConvert("あめ", listOf(Segment("あめ", listOf("雨")))); s.handle(Operation.Space)
        val u = s.handle(Operation.Delete); assertNull(u.selected); assertEquals("あめ", u.composing)
    }
    @Test fun commit_of_live_result_learns_each_segment() {
        typeAndConvert("きょうはてんき", segs)
        assertEquals(listOf(Clause("きょうは", "今日は"), Clause("てんき", "天気")), s.handle(Operation.Commit).learn)
    }
    @Test fun commit_of_plain_reading_learns_nothing() { typeAndConvert("あめ", null); assertNull(s.handle(Operation.Commit).learn) }
    @Test fun commit_of_selected_whole_candidate_learns_it() {
        typeAndConvert("あめ", listOf(Segment("あめ", listOf("雨", "飴")))); s.handle(Operation.Space); s.handle(Operation.Space)
        val u = s.handle(Operation.Commit); assertEquals("飴", u.commit); assertEquals(listOf(Clause("あめ", "飴")), u.learn)
    }

    @Test fun insert_while_selected_learns_the_selection() {
        typeAndConvert("あめ", listOf(Segment("あめ", listOf("雨", "飴")))); s.handle(Operation.Space); s.handle(Operation.Space)
        val u = s.handle(Operation.Insert('が')); assertEquals("飴", u.commit); assertEquals(listOf(Clause("あめ", "飴")), u.learn)
    }
    @Test fun forced_commit_at_max_reading_learns_like_commit() {
        val t = InputSession(SessionConfig(maxReading = 4))
        val r = "あめがふ".map { t.handle(Operation.Insert(it)) }.last().request!!
        t.onConversion(ConversionResult(r.id, listOf(Segment("あめ", listOf("雨")), Segment("がふ", listOf("我負"))), emptyList()))
        val u = t.handle(Operation.Insert('る'))
        assertEquals("雨我負", u.commit); assertEquals(listOf(Clause("あめ", "雨"), Clause("がふ", "我負")), u.learn)
    }

    // 空白は変換候補だけを回る。予測候補（覚えた長い文など）は、空白では選ばない（spec 05）。
    @Test fun first_space_skips_predictions() {
        val u0 = typeAndConvert("きょうはてんき", segs, listOf("今日は天気がいいので公園まで行く"))
        assertEquals(listOf("今日は天気", "今日は天気がいいので公園まで行く", "京は", "きょうはてんき", "キョウハテンキ"), u0.candidates.map { it.text })
        val u = s.handle(Operation.Space)
        assertEquals(2, u.selected); assertEquals("京はてんき", u.composing)
    }
    @Test fun space_cycle_never_lands_on_a_prediction() {
        typeAndConvert("あめ", listOf(Segment("あめ", listOf("雨", "飴"))), listOf("雨が降る", "雨模様"))   // 雨, 雨が降る, 雨模様, 飴, あめ, アメ
        assertEquals(listOf(0, 3, 4, 5, 0, 3), List(6) { s.handle(Operation.Space).selected })
    }
    @Test fun space_with_only_predictions_cycles_kana_and_katakana() {
        val u0 = typeAndConvert("あめ", null, listOf("雨が降る"))                       // 雨が降る, あめ, アメ
        assertEquals(listOf("雨が降る", "あめ", "アメ"), u0.candidates.map { it.text })
        val seen = List(4) { s.handle(Operation.Space) }
        assertEquals(listOf(2, 1, 2, 1), seen.map { it.selected })                      // 「あめ」は出ているので、最初はカタカナ
        assertEquals(listOf("アメ", "あめ", "アメ", "あめ"), seen.map { it.composing })
    }
    @Test fun space_before_result_skips_predictions_when_result_arrives() {
        val r = "あめ".map { s.handle(Operation.Insert(it)) }.last().request!!
        s.handle(Operation.Space)
        val u = s.onConversion(ConversionResult(r.id, null, listOf("雨が降る")))!!
        assertEquals(2, u.selected); assertEquals("アメ", u.composing)
    }
    @Test fun commit_after_space_never_commits_a_prediction() {
        typeAndConvert("きょうはてんき", segs, listOf("今日は天気がいいので公園まで行く"))
        s.handle(Operation.Space)
        assertEquals("京はてんき", s.handle(Operation.Commit).commit)
    }
    @Test fun tapping_a_prediction_still_commits_and_learns_it() {
        typeAndConvert("きょうはてんき", segs, listOf("今日は天気がいい"))
        val u = s.handle(Operation.Pick(1))
        assertEquals("今日は天気がいい", u.commit); assertEquals("", u.composing)
        assertEquals(listOf(Clause("きょうはてんき", "今日は天気がいい")), u.learn)
    }
}
