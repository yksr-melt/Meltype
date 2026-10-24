// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.view.KeyboardHeights
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class KeyboardHeightsTest {
    @Test fun portrait_heights() {
        assertEquals(54, KeyboardHeights.rowDp(landscape = false)); assertEquals(44, KeyboardHeights.barDp(landscape = false))
    }
    @Test fun landscape_heights() {
        assertEquals(40, KeyboardHeights.rowDp(landscape = true)); assertEquals(36, KeyboardHeights.barDp(landscape = true))
    }
    @Test fun four_rows_and_the_bar_take_less_than_half_of_a_small_landscape_phone() {
        // 横にした 411dp の画面（確認に使うエミュレーター）で、半分より小さい。
        assertTrue(4 * KeyboardHeights.rowDp(true) + KeyboardHeights.barDp(true) < 411 / 2)
    }
}
