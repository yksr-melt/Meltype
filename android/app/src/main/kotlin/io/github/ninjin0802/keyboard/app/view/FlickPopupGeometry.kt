// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.view

import io.github.ninjin0802.keyboard.app.layout.Key

/**
 * フリックの案内の、ますの位置（spec 04「フリックの案内」）。座標は、案内を描ける範囲
 * （候補バー・キー・下の余白を重ねた範囲）の左上から。
 *
 * ますは6つ。0〜4 は Direction の順（中央、左、上、右、下）、[PREVIEW] は選んでいる文字の表示。
 * 1ますは left, top, right, bottom の4つの値で、[SIZE] 個の配列に並べる。
 */
object FlickPopupGeometry {
    const val PREVIEW = 5
    const val SIZE = (PREVIEW + 1) * 4

    /** これより低い・狭いますには文字が入らないので、描かない。低いだけのますには、文字を小さくして書く（段の高さ・キーの幅に対する割合）。 */
    private const val MIN_FRACTION = 1f / 3

    /** 選んでいる文字の表示の幅の上限（段の高さに対する割合）。横画面でキーの幅いっぱいに伸びないようにする。 */
    private const val PREVIEW_ASPECT = 1.25f

    /**
     * 中央は押したキーにぴったり重ねる。ほかのますは中央に接して並べ、範囲の外に出る分は切る。
     * 切った残りが小さすぎるますは、ないもの（[isPresent] が false）にする。
     *
     * [keyboardTop]: キーのいちばん上の段の上の端（＝候補バーの高さ）。
     */
    fun layout(key: Key, cellW: Float, cellH: Float, keyboardTop: Float, areaW: Float, areaH: Float, out: FloatArray) {
        val w = cellW * key.width
        val h = cellH * key.rowSpan
        val left = key.col * cellW
        val top = keyboardTop + key.row * cellH
        val right = left + w
        val bottom = top + h
        put(out, 0, left, top, right, bottom)
        put(out, 1, maxOf(left - w, 0f), top, left, bottom, minW = w * MIN_FRACTION)
        put(out, 2, left, maxOf(top - h, 0f), right, top, minH = h * MIN_FRACTION)
        put(out, 3, right, top, minOf(right + w, areaW), bottom, minW = w * MIN_FRACTION)
        put(out, 4, left, bottom, right, minOf(bottom + h, areaH), minH = h * MIN_FRACTION)
        // 選んでいる文字: 上のますの左どなり。上のますがなければ出せない。
        val previewW = minOf(w, h * PREVIEW_ASPECT)
        if (isPresent(out, 2)) {
            put(out, PREVIEW, maxOf(left - previewW, 0f), out[2 * 4 + 1], left, top, minW = previewW * MIN_FRACTION)
        } else {
            put(out, PREVIEW, 0f, 0f, 0f, 0f)
        }
    }

    fun isPresent(cells: FloatArray, index: Int): Boolean =
        cells[index * 4 + 2] > cells[index * 4] && cells[index * 4 + 3] > cells[index * 4 + 1]

    private fun put(
        out: FloatArray, index: Int, l: Float, t: Float, r: Float, b: Float, minW: Float = 0f, minH: Float = 0f,
    ) {
        val present = r - l > 0f && b - t > 0f && r - l >= minW && b - t >= minH
        val i = index * 4
        out[i] = if (present) l else 0f
        out[i + 1] = if (present) t else 0f
        out[i + 2] = if (present) r else 0f
        out[i + 3] = if (present) b else 0f
    }
}
