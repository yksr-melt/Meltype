// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.KeyAction
import io.github.ninjin0802.keyboard.app.layout.KeyboardLayout
import io.github.ninjin0802.keyboard.app.layout.LatinKeys
import io.github.ninjin0802.keyboard.app.layout.LayoutId
import io.github.ninjin0802.keyboard.app.layout.Layouts
import io.github.ninjin0802.keyboard.app.layout.nearestKeyAt
import io.github.ninjin0802.keyboard.engine.KanaKeys
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class LayoutsTest {
    private val all = listOf(Layouts.kana, Layouts.romaji, Layouts.qwerty, Layouts.latinFlick, Layouts.symbols, Layouts.symbols2)
    private fun labels(l: KeyboardLayout, r: Int) = l.keys.filter { it.row == r }.sortedBy { it.col }.joinToString(" ") { it.label }
    private fun names(l: KeyboardLayout, r: Int) = l.keys.filter { it.row == r }.sortedBy { it.col }.joinToString(" ") { it.name }
    private fun widths(l: KeyboardLayout, r: Int) = l.keys.filter { it.row == r }.sortedBy { it.col }.map { it.width }

    @Test fun kana_grid_matches_spec() {
        assertEquals("戻す あ か さ 削除", names(Layouts.kana, 0))
        assertEquals("左 た な は 右", names(Layouts.kana, 1))
        assertEquals("記号 絵文字 ま や ら 空白", names(Layouts.kana, 2))
        assertEquals("かな・英字切り替え 濁点・半濁点・小文字 わ 、。?! 確定", names(Layouts.kana, 3))
        assertEquals(listOf(0.5f, 0.5f, 1f, 1f, 1f, 1f), widths(Layouts.kana, 2))
        assertTrue(Layouts.kana.keys.all { it.rowSpan == 1 })
    }
    @Test fun kana_has_all_eleven_kana_keys() =
        assertEquals(KanaKeys.all.toSet(), Layouts.kana.keys.mapNotNull { (it.action as? KeyAction.Flick)?.key }.toSet())
    @Test fun kana_half_width_keys_each_own_their_half() {
        var x = 0.01f
        while (x < 1f) {
            assertEquals(if (x < 0.5f) "記号" else "絵文字", Layouts.kana.nearestKeyAt(x, 2.5f)!!.name, "$x")
            x += 0.07f
        }
        assertEquals(KeyAction.Switch(LayoutId.SYMBOLS), Layouts.kana.keyAt(0.25f, 2.5f)!!.action)
        assertEquals(KeyAction.Emoji, Layouts.kana.keyAt(0.75f, 2.5f)!!.action)
    }
    @Test fun qwerty_rows() {
        fun row(r: Int) = Layouts.qwerty.keys.filter { it.row == r }.sortedBy { it.col }.joinToString(" ") { it.label }
        assertEquals("q w e r t y u i o p", row(0)); assertEquals("a s d f g h j k l", row(1))
        assertEquals("シフト z x c v b n m 削除", names(Layouts.qwerty, 2))
        assertEquals("記号 , 空白 . 確定", names(Layouts.qwerty, 3))
        assertEquals(listOf(1.65f, 1f, 4.45f, 1f, 1.9f), widths(Layouts.qwerty, 3))
    }
    @Test fun romaji_grid_matches_spec() {
        assertEquals("q w e r t y u i o p", labels(Layouts.romaji, 0)); assertEquals("a s d f g h j k l ー", labels(Layouts.romaji, 1))
        assertEquals("シフト z x c v b n m 削除", names(Layouts.romaji, 2))
        assertEquals("記号 、 空白 。 確定", names(Layouts.romaji, 3))
        assertEquals(widths(Layouts.qwerty, 3), widths(Layouts.romaji, 3)); assertEquals(widths(Layouts.qwerty, 2), widths(Layouts.romaji, 2))
        assertEquals(List(10) { 1f }, widths(Layouts.romaji, 1)); assertEquals(LayoutId.ROMAJI, Layouts.romaji.id)
        assertEquals(10f, Layouts.romaji.columns); assertEquals(4, Layouts.romaji.rows)
    }
    @Test fun romaji_keys_send_lower_case_letters_and_the_three_marks() {
        val sent = Layouts.romaji.keys.mapNotNull { (it.action as? KeyAction.Romaji)?.char }.joinToString("")
        assertEquals("qwertyuiopasdfghjkl-zxcvbnm", sent)
        assertTrue(Layouts.romaji.keys.filter { it.action is KeyAction.Text }.map { it.label }.toSet() == setOf("、", "。"))
    }
    @Test fun latin_flick_grid_matches_spec() {
        val l = Layouts.latinFlick
        assertEquals("戻す @#/& abc def 削除", names(l, 0)); assertEquals("左 ghi jkl mno 右", names(l, 1))
        assertEquals("記号 絵文字 pqrs tuv wxyz 空白", names(l, 2)); assertEquals("かな・英字切り替え 大文字・小文字 '\"() .,?! 確定", names(l, 3))
        assertEquals("a⇔A", l.keys.single { it.action == KeyAction.ToggleCase }.label)
        assertEquals(LayoutId.LATIN_FLICK, l.id); assertEquals(5f, l.columns); assertEquals(4, l.rows)
        // 操作キーの並びと大きさは、かな配列と同じ。
        fun frame(x: KeyboardLayout) = x.keys.filter { it.action !is KeyAction.Flick && it.action != KeyAction.Modify && it.action != KeyAction.ToggleCase }
        assertEquals(frame(Layouts.kana), frame(l))
        assertEquals(Layouts.kana.keys.map { listOf(it.col, it.row.toFloat(), it.width) }, l.keys.map { listOf(it.col, it.row.toFloat(), it.width) })
    }
    @Test fun latin_flick_has_all_eleven_keys_in_reading_order() =
        assertEquals(LatinKeys.all, Layouts.latinFlick.keys.sortedWith(compareBy({ it.row }, { it.col })).mapNotNull { (it.action as? KeyAction.Flick)?.key })
    @Test fun the_language_key_is_on_the_four_typing_layouts_and_not_on_symbols() {
        for (l in all) assertEquals(if (l.id == LayoutId.KANA || l.id == LayoutId.LATIN_FLICK) 1 else 0, l.keys.count { it.action == KeyAction.Language }, "${l.id}")
        for (l in all) assertEquals(if (l.id == LayoutId.SYMBOLS) 0 else 1, l.keys.count { it.action == KeyAction.Switch(LayoutId.SYMBOLS) }, "${l.id}")
    }
    @Test fun layouts_by_id() {
        for (id in LayoutId.entries) assertEquals(id, Layouts.of(id).id)
        assertEquals(listOf(true, true, false, false, false, false), LayoutId.entries.map { it.isJapanese })
        assertEquals(listOf(false, false, true, true, false, false), LayoutId.entries.map { it.isEnglish })
    }
    @Test fun symbols_rows() {
        fun row(r: Int) = Layouts.symbols.keys.filter { it.row == r }.sortedBy { it.col }.joinToString(" ") { it.label }
        assertEquals("1 2 3 4 5 6 7 8 9 0", row(0)); assertEquals("- / : @ ( ) 「 」 ¥ &", row(1))
        assertEquals("記号2ページ 。 、 ? ! ^^ 削除", names(Layouts.symbols, 2))
        assertEquals("前の配列に戻る 空白 確定", names(Layouts.symbols, 3))
        assertEquals(listOf(2.2f, 5.5f, 2.3f), widths(Layouts.symbols, 3))
    }
    @Test fun every_row_is_filled_from_edge_to_edge() {
        for (l in all) for (r in 0 until l.rows) {
            val margin = if (l.id == LayoutId.QWERTY && r == 1) 1f else 0f      // a と l の外の半キーずつ
            assertEquals(l.columns - margin, widths(l, r).sum(), "${l.id} $r")
        }
    }
    @Test fun every_point_inside_a_layout_hits_a_key() {
        for (l in all) for (r in 0 until l.rows) {
            var x = 0.05f
            while (x < l.columns) {
                val inMargin = l.id == LayoutId.QWERTY && r == 1 && (x < 0.5f || x > 9.5f)
                if (!inMargin) assertNotNull(l.keyAt(x, r + 0.5f), "${l.id} $x $r")
                assertNotNull(l.nearestKeyAt(x, r + 0.5f), "${l.id} $x $r")
                x += 0.1f
            }
        }
    }
    @Test fun outside_returns_null() { assertNull(Layouts.kana.keyAt(-0.1f, 0f)); assertNull(Layouts.kana.keyAt(5.1f, 0f)) }
    @Test fun only_delete_and_arrows_repeat() =
        assertEquals(setOf("左", "右", "削除"), all.flatMap { it.keys }.filter { it.action.repeats }.map { it.name }.toSet())
    @Test fun every_key_has_a_name_and_keys_drawn_as_pictures_have_no_label() {
        for (key in all.flatMap { it.keys }) {
            assertTrue(key.name.isNotBlank(), "${key.action}")
            val picture = key.action in setOf(
                KeyAction.Delete, KeyAction.Space, KeyAction.Enter, KeyAction.Left, KeyAction.Right,
                KeyAction.Undo, KeyAction.Emoji, KeyAction.Shift,
            )
            assertEquals(picture, key.label.isEmpty(), key.name)
        }
    }
    @Test fun every_layout_has_the_emoji_key_and_only_the_flick_layouts_have_undo() {
        for (l in all) assertEquals(if (l.id == LayoutId.KANA || l.id == LayoutId.LATIN_FLICK) 1 else 0, l.keys.count { it.action == KeyAction.Emoji }, "${l.id}")
        assertEquals(listOf(LayoutId.KANA, LayoutId.LATIN_FLICK), all.filter { l -> l.keys.any { it.action == KeyAction.Undo } }.map { it.id })
    }
    @Test fun text_labels() {
        fun label(l: KeyboardLayout, name: String) = l.keys.single { it.name == name }.label
        assertEquals("?123", label(Layouts.kana, "記号")); assertEquals("あa", label(Layouts.kana, "かな・英字切り替え"))
        assertEquals("?123", label(Layouts.qwerty, "記号")); assertEquals(0, Layouts.qwerty.keys.count { it.action == KeyAction.Language })
    }
    @Test fun back_key_shows_where_it_returns_to() {
        assertEquals("あa", Layouts.backLabel(LayoutId.KANA)); assertEquals("abc", Layouts.backLabel(LayoutId.QWERTY))
        assertEquals("あa", Layouts.backLabel(LayoutId.ROMAJI)); assertEquals("abc", Layouts.backLabel(LayoutId.LATIN_FLICK))
    }
    @Test fun two_symbol_pages_match_reference_and_keep_punctuation() {
        val p1 = Layouts.symbols
        val p2 = Layouts.symbols2
        assertEquals(LayoutId.SYMBOLS2, (p1.keys.single {
            it.action == KeyAction.Switch(LayoutId.SYMBOLS2)
        }.action as KeyAction.Switch).to)
        assertTrue(p1.keys.any { it.label == "!" })
        assertTrue(p1.keys.any { it.label == "?" })
        assertTrue(p1.keys.any { it.label == "。" })
        assertTrue(p1.keys.any { it.label == "、" })
        assertEquals(1, p2.keys.count { it.action == KeyAction.Switch(LayoutId.SYMBOLS) })
        for (layout in listOf(p1, p2)) {
            for (row in 0 until layout.rows) {
                assertEquals(10f, layout.keys.filter { it.row == row }.sumOf { it.width.toDouble() }.toFloat(), 0.001f)
            }
        }
        assertTrue(Layouts.romaji.keys.any { it.label == "。" })
        assertTrue(Layouts.romaji.keys.any { it.label == "、" })
        assertTrue(Layouts.romaji.keys.none { it.action == KeyAction.Language })
    }

    @Test fun emoji_bar_is_one_row_filled_edge_to_edge() {
        for (origin in LayoutId.entries) {
            val bar = Layouts.emojiBar(origin)
            assertEquals(1, bar.rows); assertEquals(10f, bar.columns)
            assertEquals("キーボードに戻る 空白 削除 確定", names(bar, 0))
            assertEquals(listOf(1.5f, 5.5f, 1.5f, 1.5f), widths(bar, 0))
            assertEquals(listOf(KeyAction.Switch(origin), KeyAction.Space, KeyAction.Delete, KeyAction.Enter), bar.keys.map { it.action })
            assertEquals(listOf(KeyAction.Delete), bar.keys.map { it.action }.filter { it.repeats })
        }
    }
    @Test fun emoji_bar_return_key_shows_where_it_goes() {
        assertEquals("あa", Layouts.emojiBar(LayoutId.KANA).keys.first().label)
        assertEquals("abc", Layouts.emojiBar(LayoutId.QWERTY).keys.first().label)
        assertEquals("あa", Layouts.emojiBar(LayoutId.ROMAJI).keys.first().label)
        assertEquals("abc", Layouts.emojiBar(LayoutId.LATIN_FLICK).keys.first().label)
        assertEquals("?123", Layouts.emojiBar(LayoutId.SYMBOLS).keys.first().label)
    }
}
