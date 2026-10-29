// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.view

import android.content.Context
import android.content.res.Configuration

data class Palette(
    val background: Int,
    val key: Int,
    val functionKey: Int,
    val keyPressed: Int,
    val functionKeyPressed: Int,
    /** あa と確定キー（両端が半円のキー）の色と、その上に描く文字や絵の色。 */
    val pill: Int,
    val pillPressed: Int,
    val onPill: Int,
    /** フリックの案内の中の色と、ふちの線の色。キーと見分けがつく色にする。 */
    val popup: Int,
    val popupOutline: Int,
    val text: Int,
    val subText: Int,
    val accent: Int,
    /** [accent] で塗った上に書く文字の色。 */
    val onAccent: Int,
) {
    companion object {
        // Both visual modes reuse the same keycaps and behavior. The IME scrim
        // has no hue; underlying content provides all wallpaper tint.
        private fun create(dark: Boolean): Palette = Palette(
            background = if (dark) 0xB8CACACA.toInt() else 0xB8F4F4F4.toInt(),
            key = 0xFFFFFFFF.toInt(),
            functionKey = 0xFFFFFFFF.toInt(),
            keyPressed = 0xFFE4E4E6.toInt(),
            functionKeyPressed = 0xFFE4E4E6.toInt(),
            pill = 0xFF0069D9.toInt(),
            pillPressed = 0xFF0056B8.toInt(),
            onPill = 0xFFFFFFFF.toInt(),
            popup = 0xFFA2B8DE.toInt(),
            popupOutline = 0xFF3154A1.toInt(),
            text = 0xFF1B1D21.toInt(),
            subText = 0xFF6B7280.toInt(),
            accent = 0xFF1368D3.toInt(),
            onAccent = 0xFFFFFFFF.toInt(),
        )
        internal val light by lazy { create(false) }
        internal val dark by lazy { create(true) }

        fun of(context: Context): Palette {
            val night = context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK
            return if (night == Configuration.UI_MODE_NIGHT_YES) dark else light
        }
    }
}
