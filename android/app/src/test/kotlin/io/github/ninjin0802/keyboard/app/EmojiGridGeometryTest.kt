// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.emoji.EmojiGrid
import io.github.ninjin0802.keyboard.app.emoji.EmojiPanelHeights
import io.github.ninjin0802.keyboard.app.emoji.emojiColumns
import io.github.ninjin0802.keyboard.app.view.KeyboardHeights
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class EmojiGridGeometryTest {
    @Test fun emojiPanelUsesTheSameViewportGeometryAsKeyboard() {
        for ((width, height) in listOf(
            410f to 915f, 840f to 390f, 1050f to 470f, 960f to 680f,
        )) {
            val g = KeyboardHeights.forViewport(width, height)
            assertEquals(g.barDp, EmojiPanelHeights.tabsDp(width, height))
            assertEquals(3 * g.rowDp, EmojiPanelHeights.gridDp(width, height))
            assertEquals(g.rowDp + g.topDp + g.bottomDp,
                EmojiPanelHeights.keysDp(width, height))
            assertEquals(g.barDp + 4 * g.rowDp + g.topDp + g.bottomDp,
                EmojiPanelHeights.tabsDp(width, height) +
                    EmojiPanelHeights.gridDp(width, height) +
                    EmojiPanelHeights.keysDp(width, height))
        }
    }

    @Test fun emojiPanelMatchesKeyboardPixelsAtAllCommonDensities() {
        for ((w, h) in listOf(410f to 915f, 1050f to 470f, 960f to 680f))
            for (density in listOf(1f, 1.5f, 2f, 2.625f, 2.75f, 3f, 3.5f, 4f)) {
                val g = KeyboardHeights.forViewport(w, h)
                val bar = (g.barDp * density).toInt()
                val outer = (g.topDp + g.bottomDp) * density
                val keyboard = (4f * g.rowDp * density + outer).toInt()
                val oneKeyRow = (g.rowDp * density + outer).toInt()
                val emoji = bar + EmojiPanelHeights.gridPx(w, h, density) + oneKeyRow
                assertEquals(bar + keyboard, emoji, "$w x $h, density $density")
            }
    }
    @Test fun eight_columns_in_portrait_and_more_in_landscape() {
        assertEquals(8, emojiColumns(320f)); assertEquals(8, emojiColumns(360f)); assertEquals(8, emojiColumns(411f))
        assertEquals(8, emojiColumns(449f)); assertEquals(9, emojiColumns(450f))
        assertEquals(16, emojiColumns(800f)); assertEquals(18, emojiColumns(914f))
        assertEquals(8, emojiColumns(0f))
    }

    // 幅 800、高さ 300、8 列、ますの高さ 100、絵文字 20 個 → 3 行（最後の行は 4 個）。
    private val grid = EmojiGrid(width = 800f, height = 300f, columns = 8, cellHeight = 100f, count = 20)

    @Test fun cell_size_and_rows() {
        assertEquals(100f, grid.cellWidth); assertEquals(3, grid.rows); assertEquals(300f, grid.contentHeight)
        assertEquals(1, grid.copy(count = 8).rows); assertEquals(2, grid.copy(count = 9).rows); assertEquals(0, grid.copy(count = 0).rows)
    }
    @Test fun cell_at_a_point() {
        assertEquals(0, grid.indexAt(0f, 0f, 0f)); assertEquals(0, grid.indexAt(99f, 99f, 0f))
        assertEquals(1, grid.indexAt(100f, 0f, 0f)); assertEquals(7, grid.indexAt(799f, 50f, 0f))
        assertEquals(8, grid.indexAt(50f, 100f, 0f)); assertEquals(19, grid.indexAt(350f, 250f, 0f))
    }
    @Test fun no_cell_outside_the_grid_or_past_the_last_emoji() {
        assertEquals(-1, grid.indexAt(450f, 250f, 0f))   // 最後の行の、絵文字のないます
        assertEquals(-1, grid.indexAt(-1f, 50f, 0f)); assertEquals(-1, grid.indexAt(800f, 50f, 0f))
        assertEquals(-1, grid.indexAt(50f, -1f, 0f)); assertEquals(-1, grid.indexAt(50f, 300f, 0f))
        assertEquals(-1, grid.copy(count = 0).indexAt(50f, 50f, 0f))
    }
    @Test fun cell_at_a_point_follows_the_scroll() {
        val tall = grid.copy(count = 80)   // 10 行
        assertEquals(8, tall.indexAt(50f, 0f, 100f)); assertEquals(0, tall.indexAt(50f, 49f, 50f)); assertEquals(8, tall.indexAt(50f, 50f, 50f))
        assertEquals(79, tall.indexAt(799f, 299f, 700f))
    }
    @Test fun scroll_bounds() {
        assertEquals(0f, grid.maxScroll)                       // 収まるときはスクロールしない
        val tall = grid.copy(count = 80)
        assertEquals(700f, tall.maxScroll)
        assertEquals(0f, tall.clampScroll(-30f)); assertEquals(700f, tall.clampScroll(900f)); assertEquals(123f, tall.clampScroll(123f))
        assertEquals(0f, grid.clampScroll(50f))
        assertEquals(50f, grid.copy(count = 20, height = 250f).maxScroll)
    }
    @Test fun visible_rows() {
        val tall = grid.copy(count = 80)
        assertEquals(0, tall.firstVisibleRow(0f)); assertEquals(2, tall.lastVisibleRow(0f))
        assertEquals(0, tall.firstVisibleRow(50f)); assertEquals(3, tall.lastVisibleRow(50f))    // 途中の行が上下に見える
        assertEquals(7, tall.firstVisibleRow(700f)); assertEquals(9, tall.lastVisibleRow(700f))
        assertEquals(2, grid.lastVisibleRow(0f))
        assertTrue(grid.copy(count = 0).lastVisibleRow(0f) < grid.copy(count = 0).firstVisibleRow(0f))   // 描く行がない
    }
    @Test fun scroll_thumb_shows_the_position() {
        val tall = grid.copy(count = 80)   // 中身 1000、見える分 300
        assertEquals(90f, tall.thumbHeight); assertEquals(0f, tall.thumbTop(0f)); assertEquals(210f, tall.thumbTop(700f))
        assertEquals(0f, grid.thumbHeight)   // スクロールしないときは出さない
    }
}
