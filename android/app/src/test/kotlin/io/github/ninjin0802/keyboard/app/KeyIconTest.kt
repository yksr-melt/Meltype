// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.EnterKind
import io.github.ninjin0802.keyboard.app.input.ShiftState
import io.github.ninjin0802.keyboard.app.layout.*
import io.github.ninjin0802.keyboard.app.view.KeyIcon
import io.github.ninjin0802.keyboard.app.view.keyIcon
import kotlin.test.*

class KeyIconTest {
    private fun icon(action: KeyAction, shift: ShiftState = ShiftState.OFF, enter: EnterKind = EnterKind.NEWLINE) = keyIcon(action, shift, enter)
    @Test fun enterIconDependsOnEditorAction() {
        assertEquals(listOf(KeyIcon.ENTER,KeyIcon.SEARCH,KeyIcon.SEND,KeyIcon.ARROW,KeyIcon.ARROW,KeyIcon.CHECK,KeyIcon.ARROW),
            EnterKind.entries.map { icon(KeyAction.Enter, enter = it) })
    }
    @Test fun shiftIconDependsOnState() {
        assertEquals(listOf(KeyIcon.SHIFT,KeyIcon.SHIFT_ONCE,KeyIcon.SHIFT_LOCKED),
            ShiftState.entries.map { icon(KeyAction.Shift, shift = it) })
    }
    @Test fun keyIconMatchesPresenceOfAVisibleLabel() {
        assertEquals(KeyIcon.DELETE, icon(KeyAction.Delete))
        assertEquals(KeyIcon.SPACE, icon(KeyAction.Space))
        assertEquals(KeyIcon.EMOJI, icon(KeyAction.Emoji))
        for (key in LayoutId.entries.flatMap { Layouts.of(it).keys }) {
            if (key.label.isEmpty()) assertNotNull(icon(key.action), key.name)
            else assertNull(icon(key.action), key.name)
        }
    }
}
