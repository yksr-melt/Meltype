// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.emoji

/**
 * 絵文字のます目の上の指の動きを、タップ（選ぶ）とスクロールに見分ける（spec 04「ます目」）。
 * 追うのは最初に触れた指だけ。位置と、その下のますの番号を受け取る。画面も時計も見ない。割り当てなし。
 *
 * [slopPx]: 縦にこれより大きく動いたら、スクロール。
 */
class EmojiGridGesture(private val slopPx: Float) {
    /** 指が離れたときの答え。 */
    enum class Up {
        /** 追っていない指。 */
        IGNORE,
        /** 何も選ばない（押したますの外で離した、流れを止めただけ）。 */
        NOTHING,
        /** [picked] のますを選んだ。 */
        PICK,
        /** スクロールが終わった。はじいていれば、呼び出し側が流す。 */
        SCROLLED,
    }

    /** 追っている指。なければ [NONE]。 */
    var pointer = NONE; private set

    /** スクロールしているか。 */
    var dragging = false; private set

    /** 押しているます（色を変えるます）。なければ [NONE]。 */
    var pressed = NONE; private set

    /** 最後に [Up.PICK] で選んだます。 */
    var picked = NONE; private set

    private var downY = 0f
    private var lastY = 0f

    /**
     * 最初の指が触れた。[index] はその下のます（なければ [NONE]）。
     * [flinging]: はじいたあとの流れの途中か。その指は流れを止めるだけで、選ばない。
     */
    fun down(id: Int, y: Float, index: Int, flinging: Boolean) {
        pointer = id; downY = y; lastY = y
        dragging = false; picked = NONE
        pressed = if (flinging) NONE else index
    }

    /** 指が動いた。スクロールで中身を動かす量を返す（指が下へ動くと正）。追っていない指と、タップの範囲の中では 0。 */
    fun move(id: Int, y: Float): Float {
        if (id != pointer) return 0f
        if (!dragging) {
            if (Math.abs(y - downY) <= slopPx) return 0f
            dragging = true
            pressed = NONE
            lastY = y   // ここまでの動きは捨てる（始まりで跳ねないように）
            return 0f
        }
        val dy = y - lastY
        lastY = y
        return dy
    }

    /** 指が離れた。[index] は離したところのます。スクロールしておらず、押したますの上で離したときだけ選ぶ。 */
    fun up(id: Int, index: Int): Up {
        if (id != pointer) return Up.IGNORE
        val wasDragging = dragging
        val was = pressed
        cancel()
        return when {
            wasDragging -> Up.SCROLLED
            was != NONE && was == index -> { picked = was; Up.PICK }
            else -> Up.NOTHING
        }
    }

    /** 押下をやめる。何も選ばない。 */
    fun cancel() { pointer = NONE; dragging = false; pressed = NONE; picked = NONE }

    companion object { const val NONE = -1 }
}
