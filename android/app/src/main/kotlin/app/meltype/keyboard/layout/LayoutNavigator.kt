// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
package app.meltype.keyboard.layout

/**
 * いまどのページ（文字・数字と記号の 2 ページ）を出すか、絵文字の画面を開いているか（spec 04「配列の切り替え」）。
 * 文字のページが日本語の配列か英語の配列かは、Core の言語モード（[japanese]）で決まる。画面にも入力欄にも触らない。
 */
class LayoutNavigator {
    /** Core の言語モードが日本語か（応答の state.languageMode）。 */
    var japanese: Boolean = true

    /** 数字・記号のページ（SYMBOLS・SYMBOLS2）。文字のページなら null。 */
    private var page: LayoutId? = null

    /** 文字のページの配列。数字・記号のページの戻るキーの行き先でもある。 */
    val letters: LayoutId get() = if (japanese) LayoutId.ROMAJI else LayoutId.QWERTY

    /** いま出している配列。絵文字の画面が開いているあいだは、開く前の配列。 */
    val current: LayoutId get() = page ?: letters

    var emojiOpen: Boolean = false
        private set

    /** 入力欄に入ったとき。[symbols] なら数字・記号のページで始める。 */
    fun start(symbols: Boolean) {
        emojiOpen = false
        page = if (symbols) LayoutId.SYMBOLS else null
    }

    /** ?123・#+=・123（[target] は数字・記号のページ）。絵文字の画面の戻るキーも、開く前の配列を [target] にしてここへ来る。 */
    fun switchTo(target: LayoutId) {
        if (emojiOpen) {
            emojiOpen = false
            return
        }
        if (target == LayoutId.SYMBOLS || target == LayoutId.SYMBOLS2) page = target
    }

    /** 数字・記号のページの戻るキー。 */
    fun back() {
        if (!emojiOpen) page = null
    }

    fun openEmoji() { emojiOpen = true }

    fun closeEmoji() { emojiOpen = false }
}
