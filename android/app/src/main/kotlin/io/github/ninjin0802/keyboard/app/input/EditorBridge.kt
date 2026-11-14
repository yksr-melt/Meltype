// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

import android.os.SystemClock
import android.view.KeyCharacterMap
import android.view.KeyEvent
import android.view.inputmethod.InputConnection
import java.text.BreakIterator

/** Android editor cursor and grapheme operations; no input composition or conversion decisions. */
class EditorBridge(private val connection: () -> InputConnection?) {
    /**
     * カーソルの前の1文字を消す。サロゲートペアや、符号を組み合わせて1文字に見せているもの（絵文字、結合する濁点）は
     * まとめて消す。読めないときは削除キーを送る。読んだ文字は残さない。
     */
    fun deleteBeforeCursor() {
        val ic = connection() ?: return
        val n = deleteLength(ic.getTextBeforeCursor(DELETE_READ_LENGTH, 0))
        if (n > 0) {
            ic.deleteSurroundingText(n, 0)
        } else {
            sendKey(ic, KeyEvent.KEYCODE_DEL)
        }
    }


    /**
     * カーソルを [from] から [steps] 文字（右が正）動かし、動かした先の位置を返す。
     *
     * 位置を指定して動かす（setSelection）。矢印キーを送る方法だと、文章の端でほかの入力欄へ移ってしまい、
     * ウェブページには矢印キーが押されたと伝わる（候補の一覧が動くなど）。読むのは、文字の途中に
     * 止まらないために要る分（[readLength]）だけ。読んだ文字は残さない。
     * 位置が分からない、または文章が読めない入力欄では、矢印キーを送る（返すのは null）。
     */
    fun moveCursor(steps: Int, from: Int?): Int? {
        val ic = connection() ?: return null
        val delta = if (from == null) null else cursorDelta(
            before = if (steps < 0) ic.getTextBeforeCursor(readLength(steps), 0) else null,
            after = if (steps > 0) ic.getTextAfterCursor(readLength(steps), 0) else null,
            steps = steps,
        )
        if (from == null || delta == null) {
            repeat(if (steps < 0) -steps else steps) { sendKey(ic, if (steps < 0) KeyEvent.KEYCODE_DPAD_LEFT else KeyEvent.KEYCODE_DPAD_RIGHT) }
            return null
        }
        val target = from + delta
        ic.setSelection(target, target)
        return target
    }

    private fun sendKey(ic: InputConnection, keyCode: Int) {
        val now = SystemClock.uptimeMillis()
        for (action in intArrayOf(KeyEvent.ACTION_DOWN, KeyEvent.ACTION_UP)) {
            ic.sendKeyEvent(
                KeyEvent(
                    now, now, action, keyCode, 0, 0, KeyCharacterMap.VIRTUAL_KEYBOARD, 0,
                    KeyEvent.FLAG_SOFT_KEYBOARD or KeyEvent.FLAG_KEEP_TOUCH_MODE,
                ),
            )
        }
    }


    companion object {
        /** カーソルを [steps] 文字動かすために、動かす向きの側から読む長さ（UTF-16 の単位）。 */
        fun readLength(steps: Int): Int = (if (steps < 0) -steps else steps) * UNITS_PER_STEP

        /**
         * カーソルを [steps] 文字（右が正）動かすときの、UTF-16 の単位での移動量。サロゲートペアや、符号を
         * 組み合わせて1文字に見せているもの（結合する濁点、組み合わせた絵文字）は、まとめて越える。
         * [before] / [after] はカーソルの前と後ろの文章で、尽きたところで止まる。動かす側が読めなければ null。
         */
        fun cursorDelta(before: CharSequence?, after: CharSequence?, steps: Int): Int? {
            if (steps == 0) return 0
            val text = (if (steps < 0) before else after)?.toString() ?: return null
            val boundaries = BreakIterator.getCharacterInstance().apply { setText(text) }
            var position = if (steps < 0) boundaries.last() else boundaries.first()
            repeat(if (steps < 0) -steps else steps) {
                val next = if (steps < 0) boundaries.previous() else boundaries.next()
                if (next != BreakIterator.DONE) position = next
            }
            return if (steps < 0) position - text.length else position
        }

        /** 1文字が使う単位の見積もり。組み合わせた絵文字（家族の絵文字で 11 単位）が収まる長さ。 */
        private const val UNITS_PER_STEP = 16

        /** 削除で消す長さを決めるために、カーソルの前から読む長さ（UTF-16 の単位）。 */
        const val DELETE_READ_LENGTH = UNITS_PER_STEP

        /**
         * 削除の1回で消す長さ（UTF-16 の単位）。[textBeforeCursor] の最後の1文字ぶん。
         * 分け方はカーソルの移動（[cursorDelta]）と同じ。null か空なら 0。
         */
        fun deleteLength(textBeforeCursor: CharSequence?): Int = -(cursorDelta(textBeforeCursor, null, -1) ?: 0)

    }
}
