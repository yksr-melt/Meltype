// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.Direction
import io.github.ninjin0802.keyboard.app.layout.Key
import io.github.ninjin0802.keyboard.app.layout.KeyAction
import io.github.ninjin0802.keyboard.app.layout.Layouts
import io.github.ninjin0802.keyboard.app.view.FlickPopupGeometry
import io.github.ninjin0802.keyboard.app.view.FlickPopupGeometry.PREVIEW
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class FlickPopupGeometryTest {
    /** 候補バー・キー4段・下の余白を重ねた、案内を描ける範囲。 */
    private class Screen(val name: String, val width: Float, val bar: Float, val row: Float, val bottom: Float) {
        val cellW = width / 5; val height = bar + row * 4 + bottom
        fun cells(key: Key) = FloatArray(FlickPopupGeometry.SIZE).also {
            FlickPopupGeometry.layout(key, cellW, row, bar, width, height, it)
        }
    }
    private val portrait = Screen("portrait", 1080f, bar = 116f, row = 142f, bottom = 126f)
    private val landscape = Screen("landscape", 2400f, bar = 95f, row = 105f, bottom = 126f)
    private val noBottom = Screen("no bottom padding", 1080f, bar = 116f, row = 142f, bottom = 0f)
    private val screens = listOf(portrait, landscape, noBottom)
    /** かな配列と英字フリック配列の文字キー（spec 11）。 */
    private val kanaKeys = (Layouts.kana.keys + Layouts.latinFlick.keys).filter { it.action is KeyAction.Flick }

    private fun FloatArray.l(i: Int) = this[i * 4]; private fun FloatArray.t(i: Int) = this[i * 4 + 1]
    private fun FloatArray.r(i: Int) = this[i * 4 + 2]; private fun FloatArray.b(i: Int) = this[i * 4 + 3]
    private fun FloatArray.present(i: Int) = FlickPopupGeometry.isPresent(this, i)
    private val tap = Direction.TAP.ordinal; private val left = Direction.LEFT.ordinal; private val up = Direction.UP.ordinal
    private val right = Direction.RIGHT.ordinal; private val down = Direction.DOWN.ordinal

    @Test fun there_are_eleven_character_keys_in_each_flick_layout() {
        assertEquals(22, kanaKeys.size); assertEquals(22, kanaKeys.map { (it.action as KeyAction.Flick).key }.toSet().size)
    }

    @Test fun the_cross_is_centred_on_the_pressed_key_for_every_kana_key() {
        for (s in screens) for (key in kanaKeys) {
            val c = s.cells(key); val at = "${s.name} ${key.name}"
            assertEquals(key.col * s.cellW, c.l(tap), at); assertEquals((key.col + 1) * s.cellW, c.r(tap), at)
            assertEquals(s.bar + key.row * s.row, c.t(tap), at); assertEquals(s.bar + (key.row + 1) * s.row, c.b(tap), at)
            // 左右は同じ幅で中央に接し、上下は中央のすぐ上と下。
            assertEquals(c.l(tap), c.r(left), at); assertEquals(c.r(tap), c.l(right), at)
            assertEquals(s.cellW, c.r(left) - c.l(left), 0.01f, at); assertEquals(s.cellW, c.r(right) - c.l(right), 0.01f, at)
            assertEquals(c.t(tap), c.t(left), at); assertEquals(c.b(tap), c.b(right), at)
            assertEquals(c.t(tap), c.b(up), at); assertEquals(c.l(tap), c.l(up), at); assertEquals(c.r(tap), c.r(up), at)
            if (c.present(down)) { assertEquals(c.b(tap), c.t(down), at); assertEquals(c.l(tap), c.l(down), at) }
        }
    }
    @Test fun nothing_is_outside_the_drawable_area() {
        for (s in screens) for (key in kanaKeys) {
            val c = s.cells(key)
            for (i in 0..PREVIEW) if (c.present(i)) {
                assertTrue(c.l(i) >= 0f && c.r(i) <= s.width && c.t(i) >= 0f && c.b(i) <= s.height, "${s.name} ${key.name} $i")
                assertTrue(c.r(i) > c.l(i) && c.b(i) > c.t(i), "${s.name} ${key.name} $i")
            }
        }
    }
    @Test fun side_cells_and_the_up_cell_are_always_present() {
        for (s in screens) for (key in kanaKeys) for (i in listOf(tap, left, up, right, PREVIEW)) {
            assertTrue(s.cells(key).present(i), "${s.name} ${key.name} $i")
        }
    }
    @Test fun top_row_up_cell_lies_above_the_keyboard_over_the_candidate_bar() {
        for (s in screens) for (key in kanaKeys.filter { it.row == 0 }) {
            val c = s.cells(key)
            assertEquals(s.bar, c.b(up), key.name); assertEquals(0f, c.t(up), key.name)      // 候補バーの高さまで
            assertEquals(s.bar, c.b(PREVIEW), key.name); assertTrue(c.t(PREVIEW) >= 0f, key.name)
        }
    }
    @Test fun other_rows_have_a_full_height_up_cell() {
        for (s in screens) for (key in kanaKeys.filter { it.row > 0 }) {
            val c = s.cells(key); assertEquals(s.row, c.b(up) - c.t(up), 0.01f, "${s.name} ${key.name}")
        }
    }
    @Test fun bottom_row_down_cell_uses_the_padding_below_the_keys() {
        for (key in kanaKeys.filter { it.row == 3 }) {
            val c = portrait.cells(key)
            assertTrue(c.present(down), key.name); assertEquals(portrait.height, c.b(down), key.name)
            assertEquals(126f, c.b(down) - c.t(down), 0.01f, key.name)
            assertFalse(noBottom.cells(key).present(down), key.name)                      // 入りきらない分は描かない
        }
        for (key in kanaKeys.filter { it.row < 3 }) assertTrue(noBottom.cells(key).present(down), key.name)
    }
    @Test fun a_sliver_too_small_to_hold_a_character_is_not_drawn() {
        val thin = Screen("thin", 1080f, bar = 116f, row = 142f, bottom = 40f)             // 段の高さの 1/3 に足りない
        assertFalse(thin.cells(kanaKeys.first { it.row == 3 }).present(down))
    }
    @Test fun the_preview_sits_up_left_of_the_cross_and_overlaps_no_cell() {
        for (s in screens) for (key in kanaKeys) {
            val c = s.cells(key); val at = "${s.name} ${key.name}"
            assertEquals(c.l(up), c.r(PREVIEW), at); assertEquals(c.b(up), c.b(PREVIEW), at); assertEquals(c.t(up), c.t(PREVIEW), at)
            for (i in 0 until PREVIEW) if (c.present(i)) {
                val apart = c.r(PREVIEW) <= c.l(i) || c.l(PREVIEW) >= c.r(i) || c.b(PREVIEW) <= c.t(i) || c.t(PREVIEW) >= c.b(i)
                assertTrue(apart, "$at $i")
            }
        }
    }
    @Test fun the_preview_is_no_wider_than_a_key_and_not_stretched_in_landscape() {
        val p = portrait.cells(kanaKeys[4]); assertTrue(p.r(PREVIEW) - p.l(PREVIEW) <= portrait.cellW)
        val l = landscape.cells(kanaKeys[4]); assertTrue(l.r(PREVIEW) - l.l(PREVIEW) <= landscape.row * 1.5f)
    }
}
