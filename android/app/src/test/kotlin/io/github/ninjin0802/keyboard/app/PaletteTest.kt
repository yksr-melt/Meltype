// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.view.Palette
import kotlin.math.pow
import kotlin.test.Test
import kotlin.test.assertTrue

class PaletteTest {
    /** WCAG 2 の相対輝度。 */
    private fun luminance(color: Int): Double {
        fun channel(v: Int): Double {
            val c = v / 255.0
            return if (c <= 0.03928) c / 12.92 else ((c + 0.055) / 1.055).pow(2.4)
        }
        return 0.2126 * channel(color shr 16 and 0xFF) + 0.7152 * channel(color shr 8 and 0xFF) +
            0.0722 * channel(color and 0xFF)
    }

    private fun contrast(a: Int, b: Int): Double {
        val (hi, lo) = listOf(luminance(a), luminance(b)).sortedDescending()
        return (hi + 0.05) / (lo + 0.05)
    }

    private fun assertReadable(name: String, text: Int, background: Int) {
        val ratio = contrast(text, background)
        assertTrue(ratio >= 4.5, "$name: コントラスト比 %.2f".format(ratio))
    }

    @Test fun text_is_readable_on_every_surface_it_is_drawn_on() {
        for ((name, p) in listOf("light" to Palette.light, "dark" to Palette.dark)) {
            assertReadable("$name key", p.text, p.key)
            assertReadable("$name functionKey", p.text, p.functionKey)
            assertReadable("$name keyPressed", p.text, p.keyPressed)
            assertReadable("$name background", p.text, p.background)
            // 固定した ⇧、フリックの案内で選んでいる向き、選択中の候補、導入画面の目立たせたボタン。
            assertReadable("$name accent", p.onAccent, p.accent)
            assertReadable("$name functionKeyPressed", p.text, p.functionKeyPressed)
            // あa と確定キー。
            assertReadable("$name pill", p.onPill, p.pill)
            assertReadable("$name pillPressed", p.onPill, p.pillPressed)
            // フリックの案内の、選んでいない向き。
            assertReadable("$name popup", p.text, p.popup)
        }
    }

    @Test fun a_pressed_key_differs_from_the_same_key_at_rest() {
        for ((name, p) in listOf("light" to Palette.light, "dark" to Palette.dark)) {
            assertDistinct("$name key", p.key, p.keyPressed)
            assertDistinct("$name functionKey", p.functionKey, p.functionKeyPressed)
            assertDistinct("$name pill", p.pill, p.pillPressed)
        }
    }

    @Test fun the_flick_popup_does_not_look_like_a_key() {
        for ((name, p) in listOf("light" to Palette.light, "dark" to Palette.dark)) {
            for ((surface, color) in listOf("key" to p.key, "functionKey" to p.functionKey, "background" to p.background, "keyPressed" to p.keyPressed)) {
                assertDistinct("$name popup / $surface", p.popup, color)
            }
            // ふちの線は、案内の中の色とも、まわりのキーや背景とも、はっきり違う。
            for ((surface, color) in listOf("popup" to p.popup, "key" to p.key, "functionKey" to p.functionKey, "background" to p.background)) {
                val ratio = contrast(p.popupOutline, color)
                assertTrue(ratio >= 1.8, "$name popupOutline / $surface: %.2f".format(ratio))
            }
            assertDistinct("$name accent / popup", p.accent, p.popup)
        }
    }

    @Test fun every_keycap_remains_white_in_light_and_dark_modes() {
        for ((name, p) in listOf("light" to Palette.light, "dark" to Palette.dark)) {
            kotlin.test.assertEquals(0xFFFFFFFF.toInt(), p.key, "$name character")
            kotlin.test.assertEquals(p.key, p.functionKey, "$name function")
            kotlin.test.assertEquals(0xFF0069D9.toInt(), p.pill, "$name distinct blue return")
            kotlin.test.assertEquals(0xFFFFFFFF.toInt(), p.onPill, "$name white return label")
            kotlin.test.assertEquals(p.keyPressed, p.functionKeyPressed, "$name pressed")
            kotlin.test.assertTrue(p.pill != p.key, "$name return must differ only in color")
            val r = p.background shr 16 and 0xFF
            val g = p.background shr 8 and 0xFF
            val b = p.background and 0xFF
            kotlin.test.assertEquals(r, g, "$name neutral background")
            kotlin.test.assertEquals(g, b, "$name neutral background")
        }
        kotlin.test.assertEquals(Palette.light.key, Palette.dark.key)
    }


    /** 見分けがつく: 明るさの比が 1.12 以上あるか、色みがはっきり違う。 */
    private fun assertDistinct(name: String, a: Int, b: Int) {
        fun channels(c: Int) = listOf(c shr 16 and 0xFF, c shr 8 and 0xFF, c and 0xFF)
        val farthest = channels(a).zip(channels(b)).maxOf { (x, y) -> kotlin.math.abs(x - y) }
        assertTrue(contrast(a, b) >= 1.12 || farthest >= 40, "$name: 比 %.2f、色の差 %d".format(contrast(a, b), farthest))
    }
}
