// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.*
import kotlin.test.*

class ScreenCoordinatorTest {
    private val nav = LayoutNavigator()
    private val history = mutableListOf<String>()
    private var hasEmoji = true
    private val screens = ScreenCoordinator(nav, object : ScreenCoordinator.Screen {
        override fun commitComposing() { history += "commit" }
        override fun showKeyboard() { history += "keyboard:${nav.current}" }
        override fun releaseShift() { history += "shift" }
        override fun showEmojiPanel(pressedAtMs: Long): Boolean {
            if (!hasEmoji) return false
            history += "emoji:${nav.current}"
            return true
        }
    })
    private fun run(action: () -> Unit) = history.clear().let { action(); history.toList() }
    @Test fun fieldChoosesQwertyOrSymbols() {
        assertEquals(listOf("shift","keyboard:ROMAJI"), run { screens.fieldStarted(LayoutId.ROMAJI) })
        assertEquals(listOf("shift","keyboard:QWERTY"), run { screens.fieldStarted(LayoutId.QWERTY) })
        assertEquals(listOf("shift","keyboard:SYMBOLS"), run { screens.fieldStarted(LayoutId.SYMBOLS) })
    }
    @Test fun pageNavigationDoesNotCommitComposingText() {
        screens.fieldStarted(LayoutId.ROMAJI)
        assertEquals(listOf("shift","keyboard:QWERTY"), run { screens.language() })
        assertEquals(listOf("shift","keyboard:SYMBOLS"), run { screens.switchTo(LayoutId.SYMBOLS) })
        assertEquals(listOf("shift","keyboard:QWERTY"), run { screens.back() })
    }
    @Test fun emojiPanelCanReturnWithoutCommitting() {
        screens.fieldStarted(LayoutId.ROMAJI)
        assertEquals(listOf("emoji:ROMAJI"), run { screens.openEmoji(10) })
        assertTrue(nav.emojiOpen)
        assertEquals(listOf("shift","keyboard:ROMAJI"), run { screens.switchTo(LayoutId.ROMAJI) })
        assertFalse(nav.emojiOpen)
    }
    @Test fun missingEmojiPanelDoesNotChangeNavigation() {
        hasEmoji = false
        screens.fieldStarted(LayoutId.QWERTY)
        assertEquals(emptyList(), run { screens.openEmoji(10) })
        assertFalse(nav.emojiOpen)
    }
}
