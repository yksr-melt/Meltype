// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.LayoutId
import kotlin.test.Test
import kotlin.test.assertEquals

class SettingsTest {
    /** 日本語・英語ともに、未設定の入力配列はQWERTY。 */
    @Test fun layout_choices_default_when_nothing_or_something_unknown_is_saved() {
        for (saved in listOf(null, "", "qwerty", "flick", "KANA", "x")) assertEquals(LayoutId.ROMAJI, Settings.japaneseOf(saved), "$saved")
        for (saved in listOf(null, "", "kana", "romaji", "QWERTY", "x")) assertEquals(LayoutId.QWERTY, Settings.englishOf(saved), "$saved")
    }
    @Test fun legacy_flick_choices_are_disabled_without_ever_being_selected() {
        for (saved in listOf(null, "kana", "romaji", "flick", "qwerty")) {
            assertEquals(LayoutId.ROMAJI, Settings.japaneseOf(saved))
            assertEquals(LayoutId.QWERTY, Settings.englishOf(saved))
        }
        assertEquals("romaji", Settings.savedName(LayoutId.ROMAJI))
        assertEquals("qwerty", Settings.savedName(LayoutId.QWERTY))
    }
    @Test fun a_layout_of_the_wrong_kind_is_saved_as_the_default_of_that_setting() {
        assertEquals(LayoutId.ROMAJI, Settings.japaneseOf(Settings.savedName(LayoutId.SYMBOLS)))
        assertEquals(LayoutId.QWERTY, Settings.englishOf(Settings.savedName(LayoutId.SYMBOLS)))
    }
    @Test fun the_two_choices_are_saved_under_their_own_keys() {
        assertEquals("japanese_layout", Settings.KEY_JAPANESE_LAYOUT); assertEquals("english_layout", Settings.KEY_ENGLISH_LAYOUT)
    }
}
