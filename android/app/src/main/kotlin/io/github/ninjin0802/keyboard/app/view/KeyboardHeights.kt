// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.view

/** キーの1段と候補バーの高さ（dp）。横画面では低くして、入力先のアプリが見える広さを残す（spec 04）。 */
object KeyboardHeights {
    fun rowDp(landscape: Boolean): Int = if (landscape) 40 else 54
    fun barDp(landscape: Boolean): Int = if (landscape) 36 else 44
}
