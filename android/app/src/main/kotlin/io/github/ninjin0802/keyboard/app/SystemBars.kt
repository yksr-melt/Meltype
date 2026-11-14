// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import android.graphics.Rect
import android.os.Build
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets

/**
 * 端末の端までの描画になるので、ステータスバーとナビゲーションバーの分を空ける。
 * キーボードが出ているあいだは、その高さだけ View を縮め、入力中の欄が見える位置へ送る
 * （空けるだけでは、ScrollView は欄が隠れていることに気づかない）。
 */
fun View.padForSystemBars() {
    setOnApplyWindowInsetsListener { v, insets ->
        @Suppress("DEPRECATION")
        val top = insets.systemWindowInsetTop
        @Suppress("DEPRECATION")
        val bars = insets.systemWindowInsetBottom
        // Android 10 以前は、画面のほうがキーボードの分だけ縮む（adjustResize）。
        val keyboard = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            insets.getInsets(WindowInsets.Type.ime()).bottom
        } else {
            0
        }
        v.setPadding(0, top, 0, if (keyboard > 0) 0 else bars)
        val params = v.layoutParams as? ViewGroup.MarginLayoutParams
        if (params != null && params.bottomMargin != keyboard) {
            params.bottomMargin = keyboard
            v.layoutParams = params
            // 縮んだあとの配置が済んでから送る（その前だと、前の大きさで位置を決めてしまう）。
            v.addOnLayoutChangeListener(object : View.OnLayoutChangeListener {
                override fun onLayoutChange(
                    view: View, l: Int, t: Int, r: Int, b: Int, ol: Int, ot: Int, or: Int, ob: Int,
                ) {
                    view.removeOnLayoutChangeListener(this)
                    val focused = view.findFocus() ?: return
                    focused.requestRectangleOnScreen(Rect(0, 0, focused.width, focused.height), true)
                }
            })
        }
        insets
    }
}
