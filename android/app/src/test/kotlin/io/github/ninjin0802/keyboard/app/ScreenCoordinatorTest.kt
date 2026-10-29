// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.LayoutId.KANA
import io.github.ninjin0802.keyboard.app.layout.LayoutId.LATIN_FLICK
import io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY
import io.github.ninjin0802.keyboard.app.layout.LayoutId.ROMAJI
import io.github.ninjin0802.keyboard.app.layout.LayoutId.SYMBOLS
import io.github.ninjin0802.keyboard.app.layout.LayoutNavigator
import io.github.ninjin0802.keyboard.app.layout.ScreenCoordinator
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse

/** キーボードと絵文字の画面の入れ替えと、その前の確定（spec 04「配列の切り替え」「絵文字の画面」）。 */
class ScreenCoordinatorTest {
    private val nav = LayoutNavigator()
    private val log = ArrayList<String>()

    /** いま出ているもの。"keyboard:配列" か "emoji:開く前の配列"。 */
    private var shown = ""
    private val screens = ScreenCoordinator(nav, object : ScreenCoordinator.Screen {
        override fun commitComposing() { log += "commit" }
        override fun showKeyboard() { shown = "keyboard:${nav.current}"; log += shown }
        override fun releaseShift() { shiftReleases++ }
        override fun showEmojiPanel(pressedAtMs: Long): Boolean {
            if (!panelExists) return false
            shown = "emoji:${nav.current}"; log += shown; return true
        }
    })
    private var shiftReleases = 0
    private var panelExists = true
    private fun did(block: () -> Unit): List<String> { log.clear(); block(); return log.toList() }

    @Test fun a_field_starts_with_its_keyboard() {
        assertEquals(listOf("keyboard:KANA"), did { screens.fieldStarted(KANA) })
        assertEquals(listOf("keyboard:QWERTY"), did { screens.fieldStarted(QWERTY) })
    }

    @Test fun composing_text_is_committed_before_the_emoji_panel_opens() {
        screens.fieldStarted(KANA)
        assertEquals(listOf("emoji:KANA"), did { screens.openEmoji(0L) })
    }

    @Test fun the_return_key_closes_the_panel_and_shows_the_layout_it_was_opened_from() {
        for (first in listOf(KANA, QWERTY, SYMBOLS)) {
            screens.fieldStarted(first)
            val origin = nav.current
            screens.openEmoji(0L)
            assertEquals(listOf("keyboard:$origin"), did { screens.switchTo(origin) })
            assertFalse(nav.emojiOpen)
        }
    }

    @Test fun navigating_layouts_keeps_uncommitted_text_intact() {
        screens.fieldStarted(KANA)
        assertEquals(listOf("keyboard:QWERTY"), did { screens.language() })
        assertEquals(listOf("keyboard:SYMBOLS"), did { screens.switchTo(SYMBOLS) })
        assertEquals(listOf("keyboard:QWERTY"), did { screens.back() })
    }

    /** 入力欄が替わったとき、キーボードが出し直されたとき。絵文字の画面は閉じて、その欄の最初の配列を出す。 */
    @Test fun a_new_field_closes_the_panel() {
        screens.fieldStarted(KANA); screens.openEmoji(0L)
        assertEquals(listOf("keyboard:QWERTY"), did { screens.fieldStarted(QWERTY) })
        assertFalse(nav.emojiOpen)
    }

    /** 設定で、いま出している側の配列を選び直した。確定してから、新しい配列を出す。絵文字の画面は閉じる。 */
    @Test fun choosing_another_layout_for_the_current_language_commits_and_shows_it() {
        screens.fieldStarted(KANA)
        assertEquals(listOf("commit", "keyboard:ROMAJI"), did { screens.settingsChanged(ROMAJI, QWERTY) })
        screens.openEmoji(0L)
        assertEquals(listOf("commit", "keyboard:KANA"), did { screens.settingsChanged(KANA, QWERTY) })
        assertFalse(nav.emojiOpen); assertEquals("keyboard:KANA", shown)
    }

    /** いま出している配列が替わらない設定の変更（もう一方の言語の配列、トグル入力など）では、画面に触らない。 */
    @Test fun a_settings_change_that_keeps_the_current_layout_touches_nothing() {
        screens.fieldStarted(KANA)
        assertEquals(emptyList(), did { screens.settingsChanged(KANA, LATIN_FLICK) })
        assertEquals(emptyList(), did { screens.settingsChanged(KANA, LATIN_FLICK) })
        screens.openEmoji(0L)
        assertEquals(emptyList(), did { screens.settingsChanged(KANA, QWERTY) })
        assertEquals("emoji:KANA", shown); assertEquals(true, nav.emojiOpen)
    }

    /** 配列が替わるたびに ⇧ を解除する（英字配列で固定した大文字が、ローマ字配列に持ち越されない）。 */
    @Test fun every_layout_change_releases_shift() {
        screens.fieldStarted(KANA); assertEquals(1, shiftReleases)
        screens.language(); screens.switchTo(SYMBOLS); screens.back(); assertEquals(4, shiftReleases)
        screens.settingsChanged(KANA, LATIN_FLICK); assertEquals(5, shiftReleases)
        screens.settingsChanged(KANA, LATIN_FLICK); assertEquals(5, shiftReleases)           // 何も替わらない
        screens.openEmoji(0L); assertEquals(5, shiftReleases)
    }

    /** 絵文字の画面がまだ作られていないとき。開いたことにしない（開いたことにすると、あa と戻るキーが効かなくなる）。 */
    @Test fun the_emoji_key_does_not_commit_when_there_is_no_panel() {
        screens.fieldStarted(KANA); panelExists = false
        assertEquals(emptyList(), did { screens.openEmoji(0L) })
        assertFalse(nav.emojiOpen)
        assertEquals(listOf("keyboard:QWERTY"), did { screens.language() })
    }
}
