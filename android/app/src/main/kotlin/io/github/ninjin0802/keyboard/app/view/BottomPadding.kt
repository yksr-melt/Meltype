// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.view

/**
 * 端末が知らせてくる、画面の下の端からの幅（px）。
 * [navigationBars] / [tappableElement] / [systemBars] は Android 11 以降の種類ごとの値、
 * [legacy] はそれより前からある1つの値（systemWindowInsetBottom）。
 */
data class BottomInsets(val navigationBars: Int, val tappableElement: Int, val systemBars: Int, val legacy: Int)

/**
 * キーボードの下に空ける幅（px）。端末が描くもの（ナビゲーションバー、「閉じる」と切り替えのボタン）に
 * キーが入らない、いちばん小さい幅。
 */
fun bottomPaddingPx(sdk: Int, insets: BottomInsets): Int =
    if (sdk >= 30) {
        // ジェスチャーナビゲーションでは、ナビゲーションバーの値（細い帯）より、ボタンの使う幅のほうが大きい。
        maxOf(insets.navigationBars, insets.tappableElement, insets.systemBars, 0)
    } else {
        maxOf(insets.legacy, 0)
    }
