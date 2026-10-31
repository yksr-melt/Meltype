// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.*

class CandidatesTest {
    private val segs = listOf(Segment("きょうは", listOf("今日は", "京は")), Segment("てんき", listOf("天気", "転機")))
    @Test fun order_and_consumed() {
        val c = buildCandidates("きょうはてんき", segs, listOf("今日は天気がいい"))
        assertEquals(listOf("今日は天気", "今日は天気がいい", "京は", "きょうはてんき", "キョウハテンキ"), c.map { it.text })
        assertEquals(listOf(7, 7, 4, 7, 7), c.map { it.consumed })
    }
    @Test fun duplicates_keep_first() {
        val c = buildCandidates("あめ", listOf(Segment("あめ", listOf("雨", "アメ", "あめ"))), listOf("雨"))
        assertEquals(listOf("雨", "アメ", "あめ"), c.map { it.text })
    }
    @Test fun without_segments_only_kana_forms() =
        assertEquals(listOf("あめ", "アメ"), buildCandidates("あめ", null, emptyList()).map { it.text })
    @Test fun empty_reading_has_no_candidates() = assertTrue(buildCandidates("", null, listOf("x")).isEmpty())
    @Test fun kinds_follow_the_source() {
        val c = buildCandidates("きょうはてんき", segs, listOf("今日は天気がいい"))
        assertEquals(
            listOf(CandidateKind.CONVERSION, CandidateKind.PREDICTION, CandidateKind.FIRST_CLAUSE, CandidateKind.KANA, CandidateKind.KATAKANA),
            c.map { it.kind },
        )
    }
    @Test fun prediction_equal_to_another_candidate_is_dropped_not_the_other() {
        // 予測候補が「読みそのまま」や先頭の文節の候補と同じ表記でも、空白で選べる候補は減らさない。
        val c = buildCandidates("あめ", listOf(Segment("あめ", listOf("雨", "飴"))), listOf("あめ", "飴", "雨が降る"))
        assertEquals(listOf("雨", "雨が降る", "飴", "あめ", "アメ"), c.map { it.text })
        assertEquals(listOf(CandidateKind.PREDICTION), c.filter { it.text == "雨が降る" }.map { it.kind })
        assertTrue(c.filter { it.text != "雨が降る" }.none { it.kind == CandidateKind.PREDICTION })
    }
}
