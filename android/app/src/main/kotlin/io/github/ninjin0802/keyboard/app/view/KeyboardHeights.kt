// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app.view

import kotlin.math.roundToInt

/**
 * One source of truth for key and candidate geometry. Inputs are the actual
 * keyboard window width and the available display height in density-independent
 * pixels, not raw physical pixels or an assumed device model.
 */
object KeyboardHeights {
    data class Metrics(
        val rowDp: Int,
        val barDp: Int,
        val gapXDp: Int,
        val gapYDp: Int,
        val sideDp: Int,
        val topDp: Int,
        val bottomDp: Int,
    )

    fun forViewport(widthDp: Float, heightDp: Float): Metrics {
        val w = widthDp.takeIf { it.isFinite() && it > 0f } ?: 400f
        val h = heightDp.takeIf { it.isFinite() && it > 0f } ?: 800f
        // Square and portrait unfolded screens retain full-height touch targets;
        // only truly wide viewports use the compact landscape scale.
        val wide = w > h * 1.12f
        val row = if (wide) (h * 0.11f).roundToInt().coerceIn(46, 58)
                  else (h * 0.074f).roundToInt().coerceIn(54, 66)
        val bar = if (wide) (h * 0.082f).roundToInt().coerceIn(32, 44)
                  else (h * 0.09f).roundToInt().coerceIn(70, 76)
        return if (wide) Metrics(row, bar, 6, 6, 12, 7, 7)
               else Metrics(row, bar, 7, 9, 10, 11, 10)
    }
}
