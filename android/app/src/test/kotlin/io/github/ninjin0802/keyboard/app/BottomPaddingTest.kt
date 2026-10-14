// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.view.BottomInsets
import io.github.ninjin0802.keyboard.app.view.bottomPaddingPx
import kotlin.test.Test
import kotlin.test.assertEquals

/** 数字は、エミュレーター（1080x2400、420dpi）で端末が知らせてきた値。 */
class BottomPaddingTest {
    @Test fun gesture_navigation_leaves_the_strip_of_the_system_buttons() {
        // ナビゲーションバーとしては 24dp（63px）だが、「閉じる」と切り替えのボタンは 48dp（126px）を使う。
        assertEquals(126, bottomPaddingPx(35, BottomInsets(navigationBars = 63, tappableElement = 126, systemBars = 126, legacy = 126)))
    }
    @Test fun three_button_navigation_ends_right_at_the_bar() {
        assertEquals(126, bottomPaddingPx(35, BottomInsets(navigationBars = 126, tappableElement = 126, systemBars = 126, legacy = 126)))
    }
    @Test fun a_report_that_misses_the_navigation_bar_still_clears_the_buttons() {
        // 向きやナビゲーションの方式が変わる途中で、ナビゲーションバーの値だけ 0 で届くことがある。
        assertEquals(126, bottomPaddingPx(35, BottomInsets(navigationBars = 0, tappableElement = 126, systemBars = 126, legacy = 126)))
    }
    @Test fun the_navigation_bar_alone_is_enough() {
        assertEquals(63, bottomPaddingPx(35, BottomInsets(navigationBars = 63, tappableElement = 0, systemBars = 0, legacy = 0)))
    }
    @Test fun a_bar_at_the_side_needs_no_padding() {
        // 横画面の 3 ボタン：バーは右にあり、下には何もない。
        assertEquals(0, bottomPaddingPx(35, BottomInsets(navigationBars = 0, tappableElement = 0, systemBars = 0, legacy = 0)))
    }
    @Test fun before_android_11_only_the_legacy_value_exists() {
        assertEquals(126, bottomPaddingPx(26, BottomInsets(navigationBars = 0, tappableElement = 0, systemBars = 0, legacy = 126)))
        assertEquals(126, bottomPaddingPx(29, BottomInsets(navigationBars = 0, tappableElement = 0, systemBars = 0, legacy = 126)))
    }
    @Test fun from_android_11_the_legacy_value_is_not_used() {
        // 古い値にはキーボード自身の高さが混ざることがあるので、種類ごとの値だけで決める。
        assertEquals(126, bottomPaddingPx(30, BottomInsets(navigationBars = 63, tappableElement = 126, systemBars = 126, legacy = 900)))
    }
    @Test fun negative_values_are_treated_as_nothing() {
        assertEquals(0, bottomPaddingPx(35, BottomInsets(navigationBars = -1, tappableElement = -5, systemBars = -1, legacy = 0)))
        assertEquals(0, bottomPaddingPx(26, BottomInsets(navigationBars = 0, tappableElement = 0, systemBars = 0, legacy = -3)))
    }
}
