// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.EnterKind
import io.github.ninjin0802.keyboard.app.input.ShiftState
import io.github.ninjin0802.keyboard.app.layout.KeyAction
import io.github.ninjin0802.keyboard.app.layout.LayoutId
import io.github.ninjin0802.keyboard.app.layout.Layouts
import io.github.ninjin0802.keyboard.app.view.KeyIcon
import io.github.ninjin0802.keyboard.app.view.keyIcon
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class KeyIconTest {
    private fun icon(action: KeyAction, shift: ShiftState = ShiftState.OFF, enter: EnterKind = EnterKind.NEWLINE) = keyIcon(action, shift, enter)

    @Test fun enter_picture_follows_what_the_key_will_do() {
        assertEquals(
            listOf(KeyIcon.ENTER, KeyIcon.SEARCH, KeyIcon.SEND, KeyIcon.ARROW, KeyIcon.ARROW, KeyIcon.CHECK, KeyIcon.ARROW),
            EnterKind.entries.map { icon(KeyAction.Enter, enter = it) },
        )
    }
    @Test fun shift_picture_follows_its_state() {
        assertEquals(
            listOf(KeyIcon.SHIFT, KeyIcon.SHIFT_ONCE, KeyIcon.SHIFT_LOCKED),
            listOf(ShiftState.OFF, ShiftState.ONCE, ShiftState.LOCKED).map { icon(KeyAction.Shift, shift = it) },
        )
    }
    @Test fun fixed_pictures() {
        assertEquals(KeyIcon.DELETE, icon(KeyAction.Delete)); assertEquals(KeyIcon.SPACE, icon(KeyAction.Space))
        assertEquals(KeyIcon.LEFT, icon(KeyAction.Left)); assertEquals(KeyIcon.RIGHT, icon(KeyAction.Right))
        assertEquals(KeyIcon.UNDO, icon(KeyAction.Undo)); assertEquals(KeyIcon.EMOJI, icon(KeyAction.Emoji))
    }
    @Test fun a_key_has_a_picture_exactly_when_it_has_no_label() {
        for (key in LayoutId.entries.flatMap { Layouts.of(it).keys }) {
            if (key.label.isEmpty()) kotlin.test.assertNotNull(icon(key.action), key.name) else assertNull(icon(key.action), key.name)
        }
    }
}
