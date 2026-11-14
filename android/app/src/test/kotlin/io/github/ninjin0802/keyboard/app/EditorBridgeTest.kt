// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.EditorBridge
import kotlin.test.*

class EditorBridgeTest {
    @Test fun deleteLengthUsesWholeCharacters() {
        assertEquals(1, EditorBridge.deleteLength("ab"))
        assertEquals(2, EditorBridge.deleteLength("a😀"))
        assertEquals(1, EditorBridge.deleteLength("😀a"))
        assertEquals(2, EditorBridge.deleteLength("aか\u3099"))
        assertEquals(2, EditorBridge.deleteLength("a☺️"))
        assertEquals(3, EditorBridge.deleteLength("a1️⃣"))
        assertEquals(0, EditorBridge.deleteLength(""))
        assertEquals(0, EditorBridge.deleteLength(null))
        assertEquals(16, EditorBridge.DELETE_READ_LENGTH)
    }
    @Test fun absentEditorConnectionIsSafe() {
        EditorBridge { null }.deleteBeforeCursor()
        assertNull(EditorBridge { null }.moveCursor(-1, 10))
    }
}
