// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.LayoutId
import io.github.ninjin0802.keyboard.app.layout.LayoutNavigator
import kotlin.test.*

class LayoutNavigatorTest {
    @Test fun japaneseQwertyIsDefaultAndLanguageToggles() {
        val n = LayoutNavigator()
        assertEquals(LayoutId.ROMAJI, n.current)
        n.toggleLanguage(); assertEquals(LayoutId.QWERTY, n.current)
        n.toggleLanguage(); assertEquals(LayoutId.ROMAJI, n.current)
    }
    @Test fun emailAndNumberFieldsStartCorrectly() {
        val n = LayoutNavigator()
        n.start(LayoutId.QWERTY); assertEquals(LayoutId.QWERTY, n.current)
        n.start(LayoutId.SYMBOLS); assertEquals(LayoutId.SYMBOLS, n.current)
        assertEquals(LayoutId.QWERTY, n.backTarget)
    }
    @Test fun symbolPagesReturnToPreviousLanguage() {
        val n = LayoutNavigator()
        for (from in listOf(LayoutId.ROMAJI, LayoutId.QWERTY)) {
            n.start(from)
            n.switchTo(LayoutId.SYMBOLS)
            n.switchTo(LayoutId.SYMBOLS2)
            assertEquals(from, n.backTarget)
            n.back()
            assertEquals(from, n.current)
        }
    }
    @Test fun emojiPanelDoesNotChangeTheSelectedLanguage() {
        val n = LayoutNavigator()
        n.start(LayoutId.QWERTY)
        n.openEmoji(); assertTrue(n.emojiOpen)
        n.toggleLanguage(); assertEquals(LayoutId.QWERTY, n.current)
        n.switchTo(LayoutId.QWERTY); assertFalse(n.emojiOpen)
        assertEquals(LayoutId.QWERTY, n.current)
    }
    @Test fun invalidChoicesDoNotChangeSupportedLayouts() {
        val n = LayoutNavigator(LayoutId.SYMBOLS, LayoutId.ROMAJI)
        assertEquals(LayoutId.ROMAJI, n.current)
        assertFalse(n.choose(LayoutId.QWERTY, LayoutId.ROMAJI))
        assertEquals(LayoutId.ROMAJI, n.current)
    }
}
