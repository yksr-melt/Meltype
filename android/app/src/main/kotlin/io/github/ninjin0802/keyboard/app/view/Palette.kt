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
        internal val light = Palette(
            background = 0xFFD9DCE1.toInt(), key = 0xFFFFFFFF.toInt(), functionKey = 0xFFC2C8D3.toInt(),
            keyPressed = 0xFFBCC3D0.toInt(), functionKeyPressed = 0xFFA1A9B8.toInt(),
            pill = 0xFFB3CBFB.toInt(), pillPressed = 0xFF8FAEF0.toInt(), onPill = 0xFF0B2C6B.toInt(),
            popup = 0xFFE1EAFF.toInt(), popupOutline = 0xFF4C7BE8.toInt(),
            text = 0xFF1B1D21.toInt(), subText = 0xFF6B7280.toInt(),
            accent = 0xFF2563EB.toInt(), onAccent = 0xFFFFFFFF.toInt(),
        )
        internal val dark = Palette(
            background = 0xFF1B1D21.toInt(), key = 0xFF3A3D44.toInt(), functionKey = 0xFF2A2C31.toInt(),
            keyPressed = 0xFF565A63.toInt(), functionKeyPressed = 0xFF464A53.toInt(),
            pill = 0xFF2F4A80.toInt(), pillPressed = 0xFF3F5FA0.toInt(), onPill = 0xFFDCE7FF.toInt(),
            popup = 0xFF4A5C8C.toInt(), popupOutline = 0xFF9DB8F5.toInt(),
            text = 0xFFF1F2F4.toInt(), subText = 0xFF9AA0AA.toInt(),
            accent = 0xFF5B8DEF.toInt(), onAccent = 0xFF101216.toInt(),
        )

        /** 端末のライト/ダークに合わせる。 */
        fun of(context: Context): Palette {
            val night = context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK
            return if (night == Configuration.UI_MODE_NIGHT_YES) dark else light
        }
    }
}
