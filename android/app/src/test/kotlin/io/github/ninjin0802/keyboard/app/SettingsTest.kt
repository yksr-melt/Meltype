// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.LayoutId
import kotlin.test.Test
import kotlin.test.assertEquals

class SettingsTest {
    @Test fun clamp() {
        assertEquals(300, Settings.clampWait(0))
        assertEquals(2000, Settings.clampWait(9999))
        assertEquals(700, Settings.clampWait(749))
        assertEquals(800, Settings.clampWait(750))
        assertEquals(1000, Settings.clampWait(1000))
        assertEquals(300, Settings.clampWait(-500))
        assertEquals(300, Settings.clampWait(Int.MIN_VALUE))
        assertEquals(2000, Settings.clampWait(Int.MAX_VALUE))
    }

    /** spec 04「設定」: 日本語の入力の最初の値は 12キー、英字の入力の最初の値は QWERTY。 */
    @Test fun layout_choices_default_when_nothing_or_something_unknown_is_saved() {
        for (saved in listOf(null, "", "qwerty", "flick", "KANA", "x")) assertEquals(LayoutId.KANA, Settings.japaneseOf(saved), "$saved")
        for (saved in listOf(null, "", "kana", "romaji", "QWERTY", "x")) assertEquals(LayoutId.QWERTY, Settings.englishOf(saved), "$saved")
    }
    @Test fun layout_choices_read_back_what_was_saved() {
        for (layout in listOf(LayoutId.KANA, LayoutId.ROMAJI)) assertEquals(layout, Settings.japaneseOf(Settings.savedName(layout)))
        for (layout in listOf(LayoutId.QWERTY, LayoutId.LATIN_FLICK)) assertEquals(layout, Settings.englishOf(Settings.savedName(layout)))
        assertEquals(listOf("kana", "romaji", "qwerty", "flick"),
            listOf(LayoutId.KANA, LayoutId.ROMAJI, LayoutId.QWERTY, LayoutId.LATIN_FLICK).map(Settings::savedName))
    }
    @Test fun a_layout_of_the_wrong_kind_is_saved_as_the_default_of_that_setting() {
        assertEquals(LayoutId.KANA, Settings.japaneseOf(Settings.savedName(LayoutId.SYMBOLS)))
        assertEquals(LayoutId.QWERTY, Settings.englishOf(Settings.savedName(LayoutId.SYMBOLS)))
    }
    @Test fun the_two_choices_are_saved_under_their_own_keys() {
        assertEquals("japanese_layout", Settings.KEY_JAPANESE_LAYOUT); assertEquals("english_layout", Settings.KEY_ENGLISH_LAYOUT)
    }
}
