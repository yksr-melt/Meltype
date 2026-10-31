// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.*

/** ローマ字入力と入力セッション（spec 05「ローマ字入力」、09）。読みと打ちかけの英字は、セッションが持つ。 */
class RomajiSessionTest {
    private val s = InputSession()
    private fun type(text: String): Update = text.map { s.handle(Operation.Romaji(it)) }.last()
    private fun segs(vararg p: Pair<String, String>) = p.map { Segment(it.first, listOf(it.second)) }
    private fun answer(request: ConversionRequest, vararg p: Pair<String, String>) =
        s.onConversion(ConversionResult(request.id, segs(*p), emptyList()))!!

    // ---- 表示と変換の依頼
    @Test fun kana_goes_into_the_reading_and_pending_letters_follow_as_typed() {
        assertEquals("k", type("k").composing); assertEquals("ky", type("y").composing); assertEquals("きょ", type("o").composing)
        assertEquals("きょk", type("k").composing); assertTrue(s.isComposing)
    }
    @Test fun a_request_is_made_only_when_the_reading_changes() {
        assertNull(type("k").request); assertNull(type("y").request)
        val r = type("o").request!!; assertEquals("きょ", r.reading)
        assertNull(type("k").request)
        assertEquals("きょか", type("a").request!!.reading)
    }
    @Test fun the_conversion_is_shown_for_the_reading_and_the_pending_letter_stays_after_it() {
        val r = type("kyouha").request!!
        assertEquals("今日は", answer(r, "きょうは" to "今日は").composing)
        val u = type("k"); assertEquals("今日はk", u.composing); assertNull(u.request)
        assertEquals("今日は", u.candidates.first().text)
    }
    @Test fun the_result_for_the_reading_is_accepted_while_a_letter_is_pending() {
        val r = type("kyouha").request!!; type("t")
        assertEquals("今日はt", answer(r, "きょうは" to "今日は").composing)
    }
    @Test fun the_live_threshold_counts_the_reading_only() {
        val r = type("kyou").request!!; type("h")                                  // きょう + h: 読みは3文字
        assertEquals("きょうh", answer(r, "きょう" to "今日").composing)
    }
    @Test fun candidates_are_for_the_reading_only() {
        val u = type("kak"); assertEquals(listOf("か", "カ"), u.candidates.map { it.text })
        s.reset(); assertTrue(type("k").candidates.isEmpty())
    }
    @Test fun letters_that_start_no_spelling_stay_in_the_reading_as_typed() {
        assertEquals("kq", type("kq").composing); assertEquals("k", s.handle(Operation.Right).candidates.first().text)
        assertEquals("kくぁ", type("a").request!!.reading)
    }
    @Test fun punctuation_and_the_long_vowel_mark() = assertEquals("らーめん、", type("ra-menn,").composing)

    // ---- 仮の ん（spec 05）
    @Test fun a_trailing_n_is_shown_and_requested_as_kana() {
        val u = type("kan"); assertEquals("かん", u.composing); assertEquals("かん", u.request!!.reading)
        assertEquals(listOf("かん", "カン"), u.candidates.map { it.text })
    }
    @Test fun nihonjin_then_enter_commits_what_the_converter_returned_for_the_whole_word() {
        val r = type("nihonjin").request!!; assertEquals("にほんじん", r.reading)
        assertEquals("日本人", answer(r, "にほんじん" to "日本人").composing)
        val u = s.handle(Operation.Commit)
        assertEquals("日本人" to "", u.commit to u.composing); assertEquals(listOf(Clause("にほんじん", "日本人")), u.learn)
    }
    @Test fun a_vowel_re_forms_the_provisional_n_with_exactly_one_new_request() {
        val before = type("kan").request!!
        val u = type("a"); assertEquals("かな", u.composing); assertEquals("かな", u.request!!.reading)
        assertEquals(before.id + 1, u.request!!.id)
        assertNull(s.onConversion(ConversionResult(before.id, segs("かん" to "缶"), emptyList())))    // かん の結果は古い
    }
    @Test fun y_re_forms_the_provisional_n() {
        type("kan"); val y = type("y"); assertEquals("かny", y.composing); assertEquals("か", y.request!!.reading)
        assertEquals("かにゃ", type("a").composing)
    }
    @Test fun a_second_n_makes_it_definite_without_a_new_request() {
        type("kan"); val u = type("n"); assertEquals("かん", u.composing); assertNull(u.request)
        assertEquals("かんあ", type("a").composing); s.reset()       // nn のあとの母音は あ行（spec 05）
        assertEquals("かんな", type("kannna").composing)
    }
    @Test fun another_consonant_makes_it_definite_and_goes_on_pending() {
        type("kan"); val u = type("j"); assertEquals("かんj", u.composing); assertNull(u.request)
        assertEquals("かんじ", type("i").composing)
    }
    @Test fun delete_removes_the_provisional_n() {
        type("kan"); val u = s.handle(Operation.Delete); assertEquals("か", u.composing); assertEquals("か", u.request!!.reading)
        s.reset(); type("n"); assertEquals(Update(), s.handle(Operation.Delete)); assertFalse(s.isComposing)
    }
    @Test fun delete_back_to_a_lone_n_makes_it_provisional_again() {
        type("kany"); val u = s.handle(Operation.Delete); assertEquals("かん", u.composing); assertEquals("かん", u.request!!.reading)
    }
    @Test fun the_live_threshold_counts_the_provisional_n() {
        val r = type("kyoun").request!!; assertEquals("きょうん", r.reading)
        assertEquals("興運", answer(r, "きょうん" to "興運").composing)
    }
    @Test fun while_the_result_for_the_n_reading_is_awaited_the_previous_result_is_kept_in_front() {
        val r = type("kyouha").request!!; answer(r, "きょうは" to "今日は")
        val u = type("n"); assertEquals("今日はん", u.composing); assertEquals("きょうはん", u.request!!.reading)
    }
    @Test fun the_limit_counts_the_provisional_n() {
        s.config = SessionConfig(maxReading = 4)
        type("akasata")
        val u = type("n"); assertEquals("あかさた" to "ん", u.commit to u.composing); assertEquals("ん", u.request!!.reading)
        assertEquals("な", type("a").composing)
    }
    @Test fun the_provisional_n_fits_as_the_last_character() {
        s.config = SessionConfig(maxReading = 4)
        val u = type("akasan"); assertEquals("" to "あかさん", u.commit to u.composing)
        val v = type("a"); assertEquals("" to "あかさな", v.commit to v.composing)
    }
    @Test fun picking_the_whole_reading_settles_the_provisional_n() {
        val r = type("kan").request!!; answer(r, "かん" to "缶")
        val u = s.handle(Operation.Pick(0)); assertEquals("缶" to "", u.commit to u.composing); assertFalse(s.isComposing)
        assertEquals("あ", type("a").composing)
    }
    @Test fun picking_a_first_clause_candidate_keeps_the_n_provisional_in_the_rest() {
        val r = type("kyouhahon").request!!
        s.onConversion(ConversionResult(r.id, listOf(Segment("きょうは", listOf("今日は", "京は")), Segment("ほん", listOf("本"))), emptyList()))
        val i = s.handle(Operation.Right).candidates.indexOfFirst { it.text == "京は" }
        val u = s.handle(Operation.Pick(i)); assertEquals("京は" to "ほん", u.commit to u.composing); assertEquals("ほん", u.request!!.reading)
        assertEquals("ほの", type("o").composing)
    }
    @Test fun a_letter_typed_while_selecting_commits_the_n_as_kana() {
        val r = type("kan").request!!; answer(r, "かん" to "缶"); s.handle(Operation.Space)
        val u = type("a"); assertEquals("缶" to "あ", u.commit to u.composing)
    }
    @Test fun a_no_convert_field_shows_the_provisional_n_as_kana() {
        s.config = SessionConfig(direct = true)
        val a = type("kan"); assertEquals("" to "かん", a.commit to a.composing); assertNull(a.request)
        val b = type("a"); assertEquals("か" to "な", b.commit to b.composing)
        val c = type("nk"); assertEquals("な" to "んk", c.commit to c.composing)
    }

    // ---- 削除
    @Test fun delete_removes_pending_letters_first_and_then_kana() {
        type("kyoky")
        val a = s.handle(Operation.Delete); assertEquals("きょk", a.composing); assertNull(a.request)
        assertEquals("きょ", s.handle(Operation.Delete).composing)
        val b = s.handle(Operation.Delete); assertEquals("き", b.composing); assertEquals("き", b.request!!.reading)
        val c = s.handle(Operation.Delete); assertEquals("", c.composing); assertFalse(s.isComposing)
    }
    @Test fun deleting_the_only_pending_letter_ends_the_composition() {
        type("k"); assertEquals(Update(), s.handle(Operation.Delete)); assertFalse(s.isComposing)
    }
    @Test fun a_letter_typed_after_a_delete_starts_from_what_is_left() {
        type("ky"); s.handle(Operation.Delete); assertEquals("か", type("a").composing)
    }

    // ---- 空白
    @Test fun space_selects_for_the_reading_that_ends_in_the_provisional_n() {
        val r = type("kan").request!!; assertEquals("かん", r.reading)
        val u = s.handle(Operation.Space); assertEquals("かん", u.composing); assertNull(u.request); assertNull(u.selected)   // 結果を待つ
        val after = answer(r, "かん" to "缶"); assertEquals("缶", after.composing); assertEquals(0, after.selected)
    }
    @Test fun space_on_a_lone_n_selects_for_n() {
        val r = type("n").request!!; assertEquals("ん", r.reading); answer(r, "ん" to "ん")
        assertEquals("ン", s.handle(Operation.Space).composing)
    }
    @Test fun space_keeps_other_pending_letters_after_the_selected_candidate() {
        val r = type("ame").request!!; answer(r, "あめ" to "雨"); type("k")
        val u = s.handle(Operation.Space); assertEquals("雨k", u.composing); assertEquals(0, u.selected); assertNull(u.request)
        assertEquals("アメk", s.handle(Operation.Space).let { s.handle(Operation.Space) }.composing)
        val done = s.handle(Operation.Commit); assertEquals("アメk", done.commit); assertEquals("", done.composing)
    }
    @Test fun space_with_only_pending_letters_commits_them_as_typed() {
        type("ky"); val u = s.handle(Operation.Space)
        assertEquals("ky" to "", u.commit to u.composing); assertFalse(s.isComposing); assertNull(u.learn)
    }
    @Test fun space_with_nothing_does_nothing() = assertEquals(Update(), s.handle(Operation.Space))
    @Test fun undo_clears_the_selection_and_keeps_the_pending_letter() {
        val r = type("ame").request!!; answer(r, "あめ" to "雨"); type("k"); s.handle(Operation.Space)
        assertEquals("あめk", s.handle(Operation.Undo(0)).composing)
    }
    @Test fun delete_while_selecting_only_clears_the_selection() {
        val r = type("ame").request!!; answer(r, "あめ" to "雨"); type("k"); s.handle(Operation.Space)
        assertEquals("あめk", s.handle(Operation.Delete).composing); assertEquals("あめ", s.handle(Operation.Delete).composing)
    }

    // ---- 確定
    @Test fun commit_turns_a_trailing_n_into_kana() {
        assertEquals("しんぶん", type("shinbun").composing); val u = s.handle(Operation.Commit)
        assertEquals("しんぶん" to "", u.commit to u.composing); assertFalse(s.isComposing)
    }
    @Test fun commit_on_a_lone_n() { type("n"); assertEquals("ん", s.handle(Operation.Commit).commit) }
    @Test fun commit_keeps_other_pending_letters_as_typed_and_learns_the_reading_only() {
        val r = type("kyouha").request!!; answer(r, "きょうは" to "今日は"); type("ky")
        val u = s.handle(Operation.Commit)
        assertEquals("今日はky", u.commit); assertEquals(listOf(Clause("きょうは", "今日は")), u.learn)
    }
    @Test fun commit_of_the_converted_reading_plus_trailing_n_keeps_what_was_shown() {
        val r = type("kyouha").request!!; answer(r, "きょうは" to "今日は"); type("n")
        assertEquals("今日はん", s.handle(Operation.Commit).commit)
    }
    @Test fun commit_of_only_pending_letters() { type("k"); assertEquals("k", s.handle(Operation.Commit).commit) }

    // ---- 候補のタップ
    @Test fun picking_a_candidate_leaves_the_pending_letter_composing() {
        val r = type("kyouha").request!!; answer(r, "きょうは" to "今日は"); type("k")
        val u = s.handle(Operation.Pick(0))
        assertEquals("今日は" to "k", u.commit to u.composing); assertTrue(s.isComposing); assertNull(u.request)
        assertEquals("か", type("a").composing)
    }
    @Test fun picking_a_first_clause_candidate_leaves_the_rest_of_the_reading_and_the_pending_letter() {
        val r = type("kyouhaha").request!!
        s.onConversion(ConversionResult(r.id, listOf(Segment("きょうは", listOf("今日は", "京は")), Segment("は", listOf("葉"))), emptyList()))
        type("k")
        val i = s.handle(Operation.Right).candidates.indexOfFirst { it.text == "京は" }
        val u = s.handle(Operation.Pick(i))
        assertEquals("京は" to "はk", u.commit to u.composing); assertEquals("は", u.request!!.reading)
    }

    // ---- 選んでいるあいだに次を打つ
    @Test fun a_letter_typed_while_selecting_commits_what_is_shown_and_starts_again() {
        val r = type("ame").request!!; answer(r, "あめ" to "雨"); type("k"); s.handle(Operation.Space)
        val u = s.handle(Operation.Romaji('a'))
        assertEquals("雨k" to "あ", u.commit to u.composing); assertEquals(listOf(Clause("あめ", "雨")), u.learn)
    }

    // ---- 上限
    @Test fun the_limit_counts_the_reading_and_commits_it_before_kana_that_would_not_fit() {
        s.config = SessionConfig(maxReading = 4)
        type("akasata"); assertEquals("あかさたk", type("k").composing)                  // 4文字 + 打ちかけ
        val u = type("ya")                                                                // きゃ は入らない
        assertEquals("あかさた" to "きゃ", u.commit to u.composing); assertEquals("きゃ", u.request!!.reading)
    }
    @Test fun kana_that_exactly_fills_the_limit_is_added() {
        s.config = SessionConfig(maxReading = 4)
        val u = type("akakya"); assertEquals("" to "あかきゃ", u.commit to u.composing)
        assertEquals("あかきゃ" to "あ", type("a").let { it.commit to it.composing })
    }

    // ---- かなの操作と混ざったとき
    @Test fun a_kana_inserted_while_letters_are_pending_keeps_them_in_front_as_typed() {
        type("kak"); assertEquals("かkあ", s.handle(Operation.Insert('あ')).composing)
        assertFalse(type("n").let { s.handle(Operation.Insert('あ')) }.composing.contains('n'))   // 末尾の n は ん
    }

    // ---- 変換しない欄（パスワード以外）
    @Test fun a_no_convert_field_holds_pending_letters_and_one_kana_at_a_time() {
        s.config = SessionConfig(direct = true)
        assertEquals("" to "k", type("k").let { it.commit to it.composing })
        val a = type("ya"); assertEquals("" to "きゃ", a.commit to a.composing); assertNull(a.request); assertTrue(a.candidates.isEmpty())
        val b = type("k"); assertEquals("" to "きゃk", b.commit to b.composing)
        val c = type("u"); assertEquals("きゃ" to "く", c.commit to c.composing); assertNull(c.learn)
    }
    @Test fun a_no_convert_field_flushes_on_space_and_commit() {
        s.config = SessionConfig(direct = true)
        type("kan"); assertEquals("かん", s.handle(Operation.Space).commit); assertFalse(s.isComposing)
        type("kak"); val u = s.handle(Operation.Commit); assertEquals("かk", u.commit); assertNull(u.learn)
    }
    @Test fun a_no_convert_field_deletes_pending_letters_and_then_the_held_kana() {
        s.config = SessionConfig(direct = true)
        type("kak"); assertEquals("か", s.handle(Operation.Delete).composing); assertEquals("", s.handle(Operation.Delete).composing)
        assertFalse(s.isComposing)
    }
    @Test fun a_small_tsu_is_held_with_its_consonant() {
        s.config = SessionConfig(direct = true)
        val a = type("kapp"); assertEquals("か" to "っp", a.commit to a.composing)
        val b = type("a"); assertEquals("っ" to "ぱ", b.commit to b.composing)
    }

    // ---- パスワードの欄
    @Test fun a_password_field_composes_nothing_and_commits_each_letter_as_typed() {
        s.config = SessionConfig(direct = true, holdLast = false)
        val seen = "kya-,.".map { s.handle(Operation.Romaji(it)) }
        assertEquals("kya-,.".map { it.toString() }, seen.map { it.commit })
        assertTrue(seen.all { it.composing.isEmpty() && it.request == null && it.learn == null && it.candidates.isEmpty() })
        assertFalse(s.isComposing)
        for (op in listOf(Operation.Delete, Operation.Space, Operation.Commit)) assertEquals(Update(), s.handle(op), "$op")
    }

    // ---- 後始末
    @Test fun reset_drops_the_pending_letters() { type("ky"); s.reset(); assertFalse(s.isComposing); assertEquals("あ", type("a").composing) }
    @Test fun twelve_key_input_is_unchanged_by_the_pending_tail() {
        val u = "きょうは".map { s.handle(Operation.Insert(it)) }.last()
        assertEquals("きょうは", u.composing); assertEquals("きょうは", u.request!!.reading)
    }
}
