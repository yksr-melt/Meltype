// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.*
import kotlin.test.*

class LayoutsTest {
    private val layouts = LayoutId.entries.map(Layouts::of)
    private fun names(layout: KeyboardLayout, row: Int) =
        layout.keys.filter { it.row == row }.sortedBy { it.col }.map { it.name }

    @Test fun availableLayoutsContainOnlyQwertyAndSymbols() {
        assertEquals(setOf(LayoutId.ROMAJI, LayoutId.QWERTY, LayoutId.SYMBOLS, LayoutId.SYMBOLS2),
            LayoutId.entries.toSet())
        assertEquals(LayoutId.ROMAJI, Layouts.romaji.id)
        assertEquals(LayoutId.QWERTY, Layouts.qwerty.id)
    }
    @Test fun japaneseAndEnglishQwertyRowsAreStable() {
        assertEquals(listOf("q","w","e","r","t","y","u","i","o","p"), names(Layouts.romaji, 0))
        assertEquals(listOf("a","s","d","f","g","h","j","k","l","ー"), names(Layouts.romaji, 1))
        assertEquals(listOf("シフト","z","x","c","v","b","n","m","削除"), names(Layouts.qwerty, 2))
        assertEquals(listOf("記号","、","空白","。","確定"), names(Layouts.romaji, 3))
        assertEquals(listOf("記号",",","空白",".","確定"), names(Layouts.qwerty, 3))
    }
    @Test fun japaneseLettersProduceRomajiEvents() {
        val sent = Layouts.romaji.keys.mapNotNull { (it.action as? KeyAction.Romaji)?.char }.joinToString("")
        assertEquals("qwertyuiopasdfghjkl-zxcvbnm", sent)
    }
    @Test fun supportedLayoutsHaveNonOverlappingKeysAndRepeatOnlyDelete() {
        for (layout in layouts) {
            assertEquals(10f, layout.columns)
            assertEquals(4, layout.rows)
            for (row in 0..3) {
                val keys = layout.keys.filter { it.row == row }.sortedBy { it.col }
                for (i in 0 until keys.lastIndex) assertTrue(keys[i].col + keys[i].width <= keys[i+1].col + 0.001f)
            }
            assertEquals(listOf(KeyAction.Delete), layout.keys.map { it.action }.filter { it.repeats })
        }
    }
    @Test fun symbolPagesAndEmojiReturnUseTheirOrigin() {
        assertEquals("abc", Layouts.backLabel(LayoutId.QWERTY))
        assertEquals("あa", Layouts.backLabel(LayoutId.ROMAJI))
        assertEquals("あa", Layouts.emojiBar(LayoutId.ROMAJI).keys.first().label)
        assertEquals("abc", Layouts.emojiBar(LayoutId.QWERTY).keys.first().label)
        assertEquals("?123", Layouts.emojiBar(LayoutId.SYMBOLS).keys.first().label)
    }
}
