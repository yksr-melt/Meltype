// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.LayoutId
import io.github.ninjin0802.keyboard.app.layout.LayoutId.KANA
import io.github.ninjin0802.keyboard.app.layout.LayoutId.LATIN_FLICK
import io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY
import io.github.ninjin0802.keyboard.app.layout.LayoutId.ROMAJI
import io.github.ninjin0802.keyboard.app.layout.LayoutId.SYMBOLS
import io.github.ninjin0802.keyboard.app.layout.LayoutNavigator
import io.github.ninjin0802.keyboard.app.layout.Layouts
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/** どの配列を出すか、あa・?123・絵文字・戻るキーがどこへ行くか（spec 04「配列の切り替え」）。 */
class LayoutNavigatorTest {
    /** 設定の4通りの組み合わせ（日本語の配列, 英語の配列）。 */
    private val choices = listOf(KANA to QWERTY, KANA to LATIN_FLICK, ROMAJI to QWERTY, ROMAJI to LATIN_FLICK)

    /** 入力欄が決める最初の配列（FieldPolicy.layout）。KANA は日本語、QWERTY は英語を表す。 */
    private val starts = listOf(KANA, QWERTY, SYMBOLS)

    private fun each(block: (LayoutNavigator, japanese: LayoutId, english: LayoutId) -> Unit) {
        for ((japanese, english) in choices) block(LayoutNavigator(japanese, english), japanese, english)
    }
    private fun LayoutNavigator.state() = Triple(current, backTarget, emojiOpen)

    @Test fun a_field_starts_in_the_chosen_layout_of_its_language() = each { nav, japanese, english ->
        nav.start(KANA); assertEquals(Triple(japanese, japanese, false), nav.state())
        nav.start(QWERTY); assertEquals(Triple(english, english, false), nav.state())
        // 数字・記号で始まる欄の戻るキーは、英語の配列へ。
        nav.start(SYMBOLS); assertEquals(Triple(SYMBOLS, english, false), nav.state())
    }
    @Test fun before_any_field_the_japanese_layout_is_shown() = each { nav, japanese, _ ->
        assertEquals(Triple(japanese, japanese, false), nav.state())
    }
    @Test fun the_language_key_goes_back_and_forth_between_the_two_chosen_layouts() = each { nav, japanese, english ->
        nav.start(KANA)
        nav.toggleLanguage(); assertEquals(english, nav.current)
        nav.toggleLanguage(); assertEquals(japanese, nav.current)
        nav.start(QWERTY)
        nav.toggleLanguage(); assertEquals(japanese, nav.current)
        nav.toggleLanguage(); assertEquals(english, nav.current)
    }
    @Test fun symbols_and_back_return_to_the_layout_they_were_opened_from() = each { nav, japanese, english ->
        for (from in listOf(japanese, english)) {
            nav.start(KANA); if (nav.current != from) nav.toggleLanguage()
            nav.switchTo(SYMBOLS); assertEquals(Triple(SYMBOLS, from, false), nav.state())
            nav.switchTo(SYMBOLS); assertEquals(Triple(SYMBOLS, from, false), nav.state())      // 行き先は変わらない
            nav.back(); assertEquals(from, nav.current)
        }
    }
    @Test fun back_from_a_field_that_starts_in_symbols_leads_to_english_and_then_follows_the_user() = each { nav, japanese, english ->
        nav.start(SYMBOLS); nav.back(); assertEquals(english, nav.current)
        nav.toggleLanguage(); nav.switchTo(SYMBOLS); nav.back(); assertEquals(japanese, nav.current)
    }
    @Test fun the_emoji_panel_returns_to_the_layout_it_was_opened_from() = each { nav, japanese, english ->
        for (from in listOf(japanese, english, SYMBOLS)) {
            nav.start(KANA)
            when (from) { english -> nav.toggleLanguage(); SYMBOLS -> nav.switchTo(SYMBOLS); else -> Unit }
            val before = nav.state()
            nav.openEmoji(); assertEquals(Triple(from, before.second, true), nav.state())
            nav.switchTo(from); assertEquals(before, nav.state())                                // 絵文字の画面の戻るキー
        }
    }
    @Test fun symbols_then_emoji_then_back_twice_ends_where_it_began() = each { nav, japanese, _ ->
        nav.start(KANA); nav.switchTo(SYMBOLS); nav.openEmoji()
        nav.switchTo(SYMBOLS); assertEquals(Triple(SYMBOLS, japanese, false), nav.state())
        nav.back(); assertEquals(Triple(japanese, japanese, false), nav.state())
    }
    @Test fun a_new_field_closes_the_emoji_panel() = each { nav, japanese, _ ->
        nav.start(QWERTY); nav.openEmoji(); nav.start(KANA); assertEquals(Triple(japanese, japanese, false), nav.state())
    }
    @Test fun the_return_keys_show_where_they_lead() = each { nav, _, _ ->
        nav.start(KANA); nav.switchTo(SYMBOLS); assertEquals("あa", Layouts.backLabel(nav.backTarget))
        assertEquals("あa", Layouts.emojiBar(nav.backTarget).keys.first().label)
        nav.start(QWERTY); nav.switchTo(SYMBOLS); assertEquals("abc", Layouts.backLabel(nav.backTarget))
        assertEquals("abc", Layouts.emojiBar(nav.backTarget).keys.first().label)
        nav.openEmoji(); assertEquals("?123", Layouts.emojiBar(nav.current).keys.first().label)
    }
    @Test fun space_is_full_width_only_in_the_japanese_layouts() = each { nav, _, _ ->
        nav.start(KANA); assertTrue(nav.spaceIsFullWidth)
        nav.openEmoji(); assertTrue(nav.spaceIsFullWidth); nav.switchTo(nav.current)            // 日本語の配列から開いた絵文字の画面
        nav.switchTo(SYMBOLS); assertFalse(nav.spaceIsFullWidth); nav.back()
        nav.toggleLanguage(); assertFalse(nav.spaceIsFullWidth)
    }
    @Test fun the_language_key_does_nothing_in_symbols_and_in_the_emoji_panel() = each { nav, _, _ ->
        nav.start(SYMBOLS); val symbols = nav.state(); nav.toggleLanguage(); assertEquals(symbols, nav.state())
        nav.start(KANA); nav.openEmoji(); val emoji = nav.state(); nav.toggleLanguage(); assertEquals(emoji, nav.state())
        nav.back(); assertEquals(emoji, nav.state())                                            // 数字・記号の戻るキーは、絵文字の画面にはない
    }

    // ---- 設定を選び直したとき
    @Test fun choosing_another_layout_replaces_the_current_one_of_the_same_language_at_once() {
        val nav = LayoutNavigator(KANA, QWERTY); nav.start(KANA)
        assertTrue(nav.choose(ROMAJI, QWERTY)); assertEquals(Triple(ROMAJI, ROMAJI, false), nav.state())
        assertFalse(nav.choose(ROMAJI, LATIN_FLICK)); assertEquals(ROMAJI, nav.current)           // 出ていない側は、いまは変わらない
        nav.toggleLanguage(); assertEquals(LATIN_FLICK, nav.current)
        assertTrue(nav.choose(ROMAJI, QWERTY)); assertEquals(QWERTY, nav.current)
        assertFalse(nav.choose(ROMAJI, QWERTY))                                                   // 同じ値
        nav.toggleLanguage(); assertEquals(ROMAJI, nav.current)
    }
    @Test fun choosing_while_in_symbols_changes_where_back_leads() {
        val nav = LayoutNavigator(KANA, QWERTY); nav.start(KANA); nav.switchTo(SYMBOLS)
        assertFalse(nav.choose(ROMAJI, LATIN_FLICK)); assertEquals(Triple(SYMBOLS, ROMAJI, false), nav.state())
        nav.start(SYMBOLS); nav.choose(KANA, QWERTY); assertEquals(Triple(SYMBOLS, QWERTY, false), nav.state())
    }
    @Test fun choosing_while_the_emoji_panel_is_open_closes_it_when_the_layout_under_it_changes() {
        val nav = LayoutNavigator(KANA, QWERTY); nav.start(KANA); nav.openEmoji()
        assertFalse(nav.choose(KANA, LATIN_FLICK)); assertTrue(nav.emojiOpen)
        assertTrue(nav.choose(ROMAJI, LATIN_FLICK)); assertEquals(Triple(ROMAJI, ROMAJI, false), nav.state())
    }
    @Test fun a_choice_of_the_wrong_kind_is_ignored() {
        val nav = LayoutNavigator(SYMBOLS, KANA); nav.start(KANA); assertEquals(KANA, nav.current)
        nav.toggleLanguage(); assertEquals(QWERTY, nav.current)
        assertFalse(nav.choose(QWERTY, ROMAJI)); nav.toggleLanguage(); assertEquals(KANA, nav.current)
    }
    @Test fun the_choice_is_used_by_the_next_field() {
        val nav = LayoutNavigator(KANA, QWERTY); nav.start(SYMBOLS); nav.choose(ROMAJI, LATIN_FLICK)
        nav.start(KANA); assertEquals(ROMAJI, nav.current); nav.start(QWERTY); assertEquals(LATIN_FLICK, nav.current)
    }
}
