// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package app.meltype.keyboard

import app.meltype.keyboard.layout.*
import kotlin.test.*

class ScreenCoordinatorTest {
    private val nav = LayoutNavigator()
    private val history = mutableListOf<String>()
    private var hasEmoji = true
    private val screens = ScreenCoordinator(nav, object : ScreenCoordinator.Screen {
        override fun showKeyboard() { history += "keyboard:${nav.current}" }
        override fun releaseShift() { history += "shift" }
        override fun showEmojiPanel(pressedAtMs: Long): Boolean {
            if (!hasEmoji) return false
            history += "emoji:${nav.current}"
            return true
        }
    })
    private fun run(action: () -> Unit) = history.clear().let { action(); history.toList() }
    @Test fun fieldChoosesLettersOrSymbols() {
        assertEquals(listOf("shift","keyboard:ROMAJI"), run { screens.fieldStarted(symbols = false) })
        assertEquals(listOf("shift","keyboard:SYMBOLS"), run { screens.fieldStarted(symbols = true) })
    }
    @Test fun pageNavigationShowsTheKeyboard() {
        screens.fieldStarted(symbols = false)
        assertEquals(listOf("shift","keyboard:SYMBOLS"), run { screens.switchTo(LayoutId.SYMBOLS) })
        assertEquals(listOf("shift","keyboard:ROMAJI"), run { screens.back() })
    }
    @Test fun emojiPanelCanReturn() {
        screens.fieldStarted(symbols = false)
        assertEquals(listOf("emoji:ROMAJI"), run { screens.openEmoji(10) })
        assertTrue(nav.emojiOpen)
        assertEquals(listOf("shift","keyboard:ROMAJI"), run { screens.switchTo(LayoutId.ROMAJI) })
        assertFalse(nav.emojiOpen)
    }
    @Test fun missingEmojiPanelDoesNotChangeNavigation() {
        hasEmoji = false
        screens.fieldStarted(symbols = false)
        assertEquals(emptyList(), run { screens.openEmoji(10) })
        assertFalse(nav.emojiOpen)
    }
}
