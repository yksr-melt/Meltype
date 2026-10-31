// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.emoji

import io.github.ninjin0802.keyboard.app.view.KeyboardHeights

/**
 * 絵文字の画面の3つの部分の高さ（dp）。合わせると、候補バーとキー4段を合わせた高さになる
 * （開いても閉じても、キーボードの高さが変わらない。spec 04）。
 */
object EmojiPanelHeights {
    /** ます目が使う、キーの段の数。 */
    private const val GRID_ROWS = 3

    fun tabsDp(landscape: Boolean): Int = KeyboardHeights.barDp(landscape)
    fun gridDp(landscape: Boolean): Int = GRID_ROWS * KeyboardHeights.rowDp(landscape)
    fun keysDp(landscape: Boolean): Int = KeyboardHeights.rowDp(landscape)

    /**
     * ます目の高さ（px）。キー4段ぶんの px から、操作キーの行の px を引いた残り。
     * dp から別々に直すと、切り捨てで合計が 1 px ずれて、開いたときにキーボードの高さが変わる。
     */
    fun gridPx(landscape: Boolean, density: Float): Int {
        val row = KeyboardHeights.rowDp(landscape) * density
        return (row * (GRID_ROWS + 1)).toInt() - row.toInt()
    }
}

/** ますの高さ（dp）。 */
const val EMOJI_CELL_HEIGHT_DP = 46f

private const val MIN_COLUMNS = 8
private const val COLUMN_DP = 50f

/** 1行のますの数。幅を 50dp で割った数で、8 より少なくしない。 */
fun emojiColumns(widthDp: Float): Int = maxOf(MIN_COLUMNS, (widthDp / COLUMN_DP).toInt())

/**
 * 絵文字のます目。[width] × [height] の中に、[columns] 列で [count] 個を並べる（単位は px）。
 * スクロールの量 scroll は、中身のいちばん上から、見えている範囲のいちばん上まで。
 */
data class EmojiGrid(val width: Float, val height: Float, val columns: Int, val cellHeight: Float, val count: Int) {
    val cellWidth: Float get() = width / columns
    val rows: Int get() = (count + columns - 1) / columns
    val contentHeight: Float get() = rows * cellHeight

    /** いちばん下まで送ったときのスクロールの量。中身が収まるときは 0。 */
    val maxScroll: Float get() = maxOf(0f, contentHeight - height)

    fun clampScroll(scroll: Float): Float = scroll.coerceIn(0f, maxScroll)

    /** 画面の (x, y) にある絵文字の番号。ます目の外、絵文字のないますは -1。 */
    fun indexAt(x: Float, y: Float, scroll: Float): Int {
        if (x < 0f || x >= width || y < 0f || y >= height) return -1
        val row = ((y + scroll) / cellHeight).toInt()
        val index = row * columns + minOf((x / cellWidth).toInt(), columns - 1)
        return if (index < count) index else -1
    }

    /** 一部でも見えている、最初と最後の行。描く行がないときは、最後のほうが小さい。 */
    fun firstVisibleRow(scroll: Float): Int = maxOf(0, (scroll / cellHeight).toInt())
    fun lastVisibleRow(scroll: Float): Int {
        val bottom = scroll + height
        // 下の端ちょうどで終わる行の、次の行は見えていない。
        val row = (bottom / cellHeight).toInt().let { if (it * cellHeight == bottom) it - 1 else it }
        return minOf(rows - 1, row)
    }

    /** いまの位置を示す印の高さと、上の端。スクロールしないときは高さ 0。 */
    val thumbHeight: Float get() = if (maxScroll == 0f) 0f else height * height / contentHeight
    fun thumbTop(scroll: Float): Float = if (maxScroll == 0f) 0f else (height - thumbHeight) * scroll / maxScroll
}
