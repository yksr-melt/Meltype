// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

import android.os.SystemClock
import android.view.KeyCharacterMap
import android.view.KeyEvent
import android.view.inputmethod.InputConnection
import java.text.BreakIterator
import io.github.ninjin0802.keyboard.engine.Kana
import io.github.ninjin0802.keyboard.engine.Update

/** engine が返した指示を入力欄に書く。入力欄にいま出している未確定文字を覚えておく。 */
class EditorBridge(private val connection: () -> InputConnection?) {
    private var composing = ""

    /** いまの未確定文字を、入力欄が「未確定の範囲」として知らせてきたか。 */
    private var shownByEditor = false

    /** 確定を先に、次に未確定文字を反映する。入力欄がなくても、未確定文字の状態は更新する。 */
    fun apply(update: Update) {
        val edits = edits(composing, update)
        if (composing.isEmpty() || update.composing.isEmpty()) shownByEditor = false   // 新しく始まった、または終わった
        composing = update.composing
        if (edits == null) return
        val ic = connection() ?: return
        ic.beginBatchEdit()
        // 未確定文字が出ているあいだの commitText は、その未確定文字を置き換える。
        edits.commit?.let { ic.commitText(it, 1) }
        edits.composing?.let { ic.setComposingText(it, 1) }
        ic.endBatchEdit()
    }

    /** 入力欄の側で未確定文字がなくなった（入力欄の切り替えなど）。入力欄には何も送らない。 */
    fun forget() {
        composing = ""
        shownByEditor = false
    }

    /**
     * 入力欄からカーソルの位置が知らされたとき。true ならセッションを捨てる。
     * 未確定文字があるのに、入力欄がそれを外したか、カーソルがその末尾にないとき。
     *
     * 知らせは遅れて届く。確定のすぐあとに次の文字を打つと、確定についての知らせ（未確定の範囲なし）が、
     * 次の未確定文字を出したあとに届く。これを「外された」と取り違えないよう、入力欄がいまの未確定文字の
     * 範囲を一度知らせてくるまでは、範囲なしの知らせを無視する。
     */
    fun onSelectionChanged(newSelStart: Int, newSelEnd: Int, composingStart: Int, composingEnd: Int): Boolean {
        if (composing.isEmpty()) return false
        if (composingStart == -1) return shownByEditor
        shownByEditor = true
        return newSelStart != composingEnd || newSelEnd != composingEnd
    }

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
     * カーソルの前の1文字を、[change] が返す文字（濁点・半濁点・小文字の次の形、または大文字・小文字を入れ替えたもの）に
     * 置き換える。未確定文字を持たない欄（パスワード）で使う。読めないとき、切り替えられない文字のときは何もしない。
     */
    fun modifyBeforeCursor(change: (Char) -> Char? = Kana::modify) {
        val ic = connection() ?: return
        val replacement = modified(ic.getTextBeforeCursor(1, 0), change) ?: return
        ic.beginBatchEdit()
        ic.deleteSurroundingText(1, 0)
        ic.commitText(replacement, 1)
        ic.endBatchEdit()
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

    /** 入力欄への操作。null の項目は送らない。 */
    data class Edits(val commit: String?, val composing: String?)

    companion object {
        /** [textBeforeCursor] の最後の1文字を [change]（省くと ゛゜小）で切り替えた文字。切り替えられない、または読めなかったら null。 */
        fun modified(textBeforeCursor: CharSequence?, change: (Char) -> Char? = Kana::modify): String? =
            textBeforeCursor?.lastOrNull()?.let(change)?.toString()

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

        /**
         * [previousComposing] が出ている入力欄を [update] のとおりにするための操作。変えるものがなければ null。
         *
         * 未確定文字がなくなるときは、空の文字で置き換える（[Edits.commit] が ""）。
         * finishComposingText では、消したはずの文字が入力欄に残ってしまう。
         */
        fun edits(previousComposing: String, update: Update): Edits? {
            val ends = previousComposing.isNotEmpty() && update.composing.isEmpty()
            val commit = update.commit.takeIf { it.isNotEmpty() || ends }
            val composing = update.composing.takeIf {
                it.isNotEmpty() && (commit != null || it != previousComposing)
            }
            return if (commit == null && composing == null) null else Edits(commit, composing)
        }
    }
}
