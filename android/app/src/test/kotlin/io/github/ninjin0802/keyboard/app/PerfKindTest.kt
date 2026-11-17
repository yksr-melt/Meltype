// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.*
import kotlin.test.*

class PerfKindTest {
    @Test fun allRomajiKeysAreIndistinguishableInLogs() {
        assertEquals(setOf("romaji"), "aq-,.".map { perfKind(KeyAction.Romaji(it)) }.toSet())
    }
    @Test fun allTextKeysAreIndistinguishableInLogs() {
        assertEquals(setOf("text"), "aZ1@「".map { perfKind(KeyAction.Text(it.toString())) }.toSet())
    }
    @Test fun supportedKeysLogOnlyTheirKindNotUserContent() {
        val kinds = LayoutId.entries.flatMap { Layouts.of(it).keys }.map { perfKind(it.action) }.toSet()
        assertEquals(setOf("romaji","text","delete","space","enter","shift","switch","back"), kinds)
        assertTrue(kinds.all { name -> name.all { it in 'a'..'z' } })
    }
}
