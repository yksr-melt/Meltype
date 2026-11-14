// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.layout

/**
 * キーボードと絵文字の画面のどちらを出すか、出す前に未確定文字を確定するか（spec 04「配列の切り替え」「絵文字の画面」）。
 * 行き先は [nav] が決め、画面と入力欄に触るのは [screen]。ここは順番だけを決める。
 */
class ScreenCoordinator(private val nav: LayoutNavigator, private val screen: Screen) {
    interface Screen {
        /** 未確定文字があれば確定する。 */
        fun commitComposing()

        /** [nav] のいまの配列のキーボードを出す。絵文字の画面が開いていれば閉じる。 */
        fun showKeyboard()

        /** ⇧ を解除する。 */
        fun releaseShift()

        /** キーボードの代わりに絵文字の画面を出す。[pressedAtMs] は絵文字キーが押された時刻。出せなければ false。 */
        fun showEmojiPanel(pressedAtMs: Long): Boolean
    }

    /** 入力欄に入った、またはキーボードが出し直された。絵文字の画面は閉じて、その欄の最初の配列を出す。 */
    fun fieldStarted(first: LayoutId) {
        nav.start(first)
        screen.releaseShift()
        screen.showKeyboard()
    }

    /** 設定が変わった。いま出している配列が替わるときだけ、確定してから出し直す（絵文字の画面は閉じる）。 */
    fun settingsChanged(japanese: LayoutId, english: LayoutId) {
        if (!nav.choose(japanese, english)) return
        screen.commitComposing()
        screen.releaseShift()
        screen.showKeyboard()
    }

    /** あa。 */
    fun language() = navigate { nav.toggleLanguage() }

    /** ?123、または絵文字の画面の戻るキー（[target] は開く前の配列）。 */
    fun switchTo(target: LayoutId) = navigate { nav.switchTo(target) }

    /** 数字・記号の配列の戻るキー。 */
    fun back() = navigate { nav.back() }

    /** 絵文字キー。確定してから、絵文字の画面を出す。出せなかったときは、開いたことにしない。 */
    fun openEmoji(pressedAtMs: Long) {
        if (screen.showEmojiPanel(pressedAtMs)) nav.openEmoji()
    }

    /** Page/mode navigation never commits: only punctuation and Enter do. */
    private inline fun navigate(move: () -> Unit) {
        move()
        screen.releaseShift()
        screen.showKeyboard()
    }
}
