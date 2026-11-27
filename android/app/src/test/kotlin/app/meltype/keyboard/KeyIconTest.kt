// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
package app.meltype.keyboard

import app.meltype.keyboard.input.EnterKind
import app.meltype.keyboard.input.ShiftState
import app.meltype.keyboard.layout.*
import app.meltype.keyboard.view.KeyIcon
import app.meltype.keyboard.view.keyIcon
import kotlin.test.*

class KeyIconTest {
    private fun icon(action: KeyAction, shift: ShiftState = ShiftState.Off, enter: EnterKind = EnterKind.NEWLINE) = keyIcon(action, shift, enter)
    @Test fun enterIconDependsOnEditorAction() {
        assertEquals(listOf(KeyIcon.ENTER,KeyIcon.SEARCH,KeyIcon.SEND,KeyIcon.ARROW,KeyIcon.ARROW,KeyIcon.CHECK,KeyIcon.ARROW),
            EnterKind.entries.map { icon(KeyAction.Enter, enter = it) })
    }
    @Test fun shiftIconDependsOnState() {
        assertEquals(listOf(KeyIcon.SHIFT,KeyIcon.SHIFT_ONCE,KeyIcon.SHIFT_LOCKED),
            listOf(ShiftState.Off, ShiftState.Once(0), ShiftState.Locked).map { icon(KeyAction.Shift, shift = it) })
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
