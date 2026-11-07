// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.layout

/**
 * いまどの配列を出すか、あa・?123・絵文字・戻るキーがどこへ行くか（spec 04「配列の切り替え」）。
 * 画面にも入力欄にも触らない。[japanese] と [english] は、設定で選んである日本語の配列と英語の配列。
 * 日本語の配列でないもの・英語の配列でないものを渡されたら、それぞれの最初の値を使う。
 */
class LayoutNavigator(japanese: LayoutId = LayoutId.KANA, english: LayoutId = LayoutId.QWERTY) {
    private var japanese = japanese.takeIf { it.isJapanese } ?: LayoutId.KANA
    private var english = english.takeIf { it.isEnglish } ?: LayoutId.QWERTY

    /** いま出している配列。絵文字の画面が開いているあいだは、開く前の配列。 */
    var current: LayoutId = this.japanese
        private set

    /** 数字・記号の配列の戻るキーの行き先。 */
    var backTarget: LayoutId = this.japanese
        private set

    var emojiOpen: Boolean = false
        private set

    /** 空白キーが全角の空白を入れるか。日本語の配列（と、そこから開いた絵文字の画面）のとき。 */
    val spaceIsFullWidth: Boolean get() = current.isJapanese

    /**
     * 入力欄に入ったとき。[first] は入力欄が決める最初の配列（FieldPolicy.layout）で、
     * 日本語の配列のどちらかなら日本語、英語の配列のどちらかなら英語を表す。
     */
    fun start(first: LayoutId) {
        emojiOpen = false
        current = chosen(first)
        // 数字・記号で始まる欄の「戻る」は、英語の配列へ。
        backTarget = if (current == LayoutId.SYMBOLS) english else current
    }

    /** 設定で選び直したとき。いま出している配列が替わったら true（呼び出し側が、未確定文字を確定して出し直す）。 */
    fun choose(japanese: LayoutId, english: LayoutId): Boolean {
        if (japanese.isJapanese) this.japanese = japanese
        if (english.isEnglish) this.english = english
        backTarget = chosen(backTarget)
        val before = current
        current = chosen(current)
        if (current != before) emojiOpen = false
        return current != before
    }

    /** あa。日本語の配列と英語の配列を入れ替える。数字・記号の配列と絵文字の画面には、このキーがない。 */
    fun toggleLanguage() {
        if (emojiOpen || current == LayoutId.SYMBOLS || current == LayoutId.SYMBOLS2) return
        current = if (current.isJapanese) english else japanese
    }

    /** ?123（[target] は数字・記号の配列）。絵文字の画面の戻るキーも、開く前の配列を [target] にしてここへ来る。 */
    fun switchTo(target: LayoutId) {
        if (emojiOpen) {
            emojiOpen = false
            return
        }
        if (target != LayoutId.SYMBOLS && target != LayoutId.SYMBOLS2) return
        if (current != LayoutId.SYMBOLS && current != LayoutId.SYMBOLS2) backTarget = current
        current = target
    }

    /** 数字・記号の配列の戻るキー。 */
    fun back() {
        if (emojiOpen || (current != LayoutId.SYMBOLS && current != LayoutId.SYMBOLS2)) return
        current = backTarget
    }

    fun openEmoji() { emojiOpen = true }

    /** [layout] と同じ言語の、選んである配列。数字・記号はそのまま。 */
    private fun chosen(layout: LayoutId): LayoutId = when {
        layout.isJapanese -> japanese
        layout.isEnglish -> english
        else -> layout
    }
}
