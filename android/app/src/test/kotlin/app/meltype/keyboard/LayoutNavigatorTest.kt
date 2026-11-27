// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
package app.meltype.keyboard

import app.meltype.keyboard.layout.LayoutId
import app.meltype.keyboard.layout.LayoutNavigator
import kotlin.test.*

class LayoutNavigatorTest {
    @Test fun lettersFollowTheCoreLanguageMode() {
        val n = LayoutNavigator()
        assertEquals(LayoutId.ROMAJI, n.current)
        n.japanese = false; assertEquals(LayoutId.QWERTY, n.current)
        n.japanese = true; assertEquals(LayoutId.ROMAJI, n.current)
    }
    @Test fun numberFieldsStartWithSymbols() {
        val n = LayoutNavigator()
        n.start(symbols = true); assertEquals(LayoutId.SYMBOLS, n.current)
        n.start(symbols = false); assertEquals(LayoutId.ROMAJI, n.current)
    }
    @Test fun symbolPagesReturnToTheLettersOfTheCurrentLanguage() {
        val n = LayoutNavigator()
        for (japanese in listOf(true, false)) {
            n.japanese = japanese
            n.start(symbols = false)
            n.switchTo(LayoutId.SYMBOLS)
            n.switchTo(LayoutId.SYMBOLS2)
            assertEquals(LayoutId.SYMBOLS2, n.current)
            n.back()
            assertEquals(n.letters, n.current)
        }
    }
    @Test fun emojiPanelKeepsThePage() {
        val n = LayoutNavigator()
        n.switchTo(LayoutId.SYMBOLS)
        n.openEmoji(); assertTrue(n.emojiOpen)
        n.back(); assertEquals(LayoutId.SYMBOLS, n.current)
        n.switchTo(LayoutId.SYMBOLS); assertFalse(n.emojiOpen)
        assertEquals(LayoutId.SYMBOLS, n.current)
        n.openEmoji(); n.closeEmoji(); assertFalse(n.emojiOpen)
    }
}
