// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.FoldSplitLayout
import io.github.ninjin0802.keyboard.app.layout.KeyAction
import io.github.ninjin0802.keyboard.app.layout.Layouts
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertSame
import kotlin.test.assertTrue

class AdaptiveLayoutTest {
    @Test fun normalAndRotatedPhonesNeverForceSplitLayout() {
        assertEquals(FoldSplitLayout.Mode.FULL, FoldSplitLayout.mode(412f, 915f, 412))
        assertEquals(FoldSplitLayout.Mode.FULL, FoldSplitLayout.mode(840f, 390f, 390))
        assertEquals(FoldSplitLayout.Mode.FULL, FoldSplitLayout.mode(690f, 900f, 650))
        assertSame(Layouts.romaji, FoldSplitLayout.forWindow(Layouts.romaji, 840f, 390f, 390))
    }

    @Test fun shortWideFoldHasBiggerThumbKeysButNoCenterNumpad() {
        val layout = FoldSplitLayout.forWindow(Layouts.romaji, 1050f, 470f, 600)
        assertSame(FoldSplitLayout.romajiCompact, layout)
        assertEquals(14f, layout.columns)
        assertEquals(0, layout.keys.count {
            it.col >= FoldSplitLayout.LEFT_END &&
                it.col < FoldSplitLayout.COMPACT_RIGHT_START
        })
        assertTrue(FoldSplitLayout.isDeadZone(6.5f, layout.columns))
        assertFalse(FoldSplitLayout.isDeadZone(8.5f, layout.columns))
        // The old 18-column grid made both thumbs' keys 22% narrower.
        val compactKeyDp = (1050f - 24f) * 1.2f / layout.columns
        val paddedKeyDp = (1050f - 24f) * 1.2f / FoldSplitLayout.COLUMNS
        assertTrue(compactKeyDp > paddedKeyDp * 1.2f)
    }

    @Test fun fullNumpadAppearsOnlyWithGenerousWidthAndHeight() {
        assertEquals(FoldSplitLayout.Mode.SPLIT, FoldSplitLayout.mode(950f, 450f, 600))
        assertEquals(FoldSplitLayout.Mode.SPLIT_WITH_NUMPAD, FoldSplitLayout.mode(960f, 680f, 600))
        val full = FoldSplitLayout.forWindow(Layouts.romaji, 960f, 680f, 600)
        assertSame(FoldSplitLayout.romaji, full)
        val digits = full.keys.filter { it.col >= FoldSplitLayout.PAD_START &&
            it.col < FoldSplitLayout.PAD_END }
        assertEquals(10, digits.size)
        assertEquals(listOf("7", "8", "9"), digits.filter { it.row == 0 }.map { it.label })
        assertEquals(listOf("4", "5", "6"), digits.filter { it.row == 1 }.map { it.label })
        assertEquals(listOf("1", "2", "3"), digits.filter { it.row == 2 }.map { it.label })
        assertEquals("0", digits.single { it.row == 3 }.label)
    }

    @Test fun splitModesKeepPunctuationSpacesAndDoNotOverlap() {
        for (layout in listOf(FoldSplitLayout.romaji, FoldSplitLayout.english,
            FoldSplitLayout.romajiCompact, FoldSplitLayout.englishCompact)) {
            assertEquals(2, layout.keys.count { it.action == KeyAction.Space })
            assertTrue(layout.keys.any { it.action == KeyAction.Enter })
            assertTrue(layout.keys.any { it.action == KeyAction.Delete })
            assertTrue(layout.keys.any { it.action == KeyAction.Switch(
                io.github.ninjin0802.keyboard.app.layout.LayoutId.SYMBOLS) })
            for (row in 0..3) {
                val keys = layout.keys.filter { it.row == row }
                for (i in keys.indices) for (j in (i + 1) until keys.size) {
                    val a = keys[i]; val b = keys[j]
                    assertTrue(a.col + a.width <= b.col + 0.001f ||
                        b.col + b.width <= a.col + 0.001f,
                        "Overlapping keys: $a and $b")
                }
            }
        }
        assertTrue(FoldSplitLayout.isDeadZone(6.5f, FoldSplitLayout.COLUMNS))
        assertTrue(FoldSplitLayout.isDeadZone(11.5f, FoldSplitLayout.COLUMNS))
        assertFalse(FoldSplitLayout.isDeadZone(8.5f, FoldSplitLayout.COLUMNS))
    }

    @Test fun switchingModesNeverReplacesNumbersAndSymbolsScreens() {
        for (width in listOf(410f, 840f, 1100f)) {
            assertSame(Layouts.symbols, FoldSplitLayout.forWindow(Layouts.symbols, width, 650f, 600))
            assertSame(Layouts.kana, FoldSplitLayout.forWindow(Layouts.kana, width, 650f, 600))
            assertSame(Layouts.emojiBar(io.github.ninjin0802.keyboard.app.layout.LayoutId.ROMAJI),
                FoldSplitLayout.forWindow(
                    Layouts.emojiBar(io.github.ninjin0802.keyboard.app.layout.LayoutId.ROMAJI),
                    width, 650f, 600))
            assertSame(Layouts.emojiBar(io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY),
                FoldSplitLayout.forWindow(
                    Layouts.emojiBar(io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY),
                    width, 650f, 600))
        }
    }
}
