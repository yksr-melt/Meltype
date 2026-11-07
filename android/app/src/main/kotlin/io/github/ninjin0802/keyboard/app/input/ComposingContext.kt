// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

import io.github.ninjin0802.keyboard.engine.Update

/**
 * 未確定文字の直前にある文章（変換と学習の手がかり。spec 06）。いつ読むかを決め、読んだものを覚えておく。
 *
 * 読むのは、未確定文字が始まるとき、それを入力欄に出す前の1回だけ。出したあとに読むと、未確定文字そのもの
 * （ローマ字配列の、最初の打ちかけの英字など）が混ざる。覚えるのは最後の [length] 文字まで。保存もログもしない。
 */
class ComposingContext(private val length: Int) {
    /** いまの未確定文字の直前の文章。未確定文字がない、読まない欄、読めない欄では null。 */
    var text: String? = null
        private set

    private var composing = false

    /**
     * [update] を入力欄に反映する前に呼ぶ。未確定文字が始まるところなら、[read] で入力欄から読む
     * （引数は読む長さ）。[converts] が false の欄（変換しない欄）では、使わないので読まない。
     */
    fun beforeApply(update: Update, converts: Boolean, read: (Int) -> String?) {
        if (composing || update.composing.isEmpty()) return
        composing = true
        text = if (converts) read(length)?.takeLast(length) else null
    }

    /** [update] を入力欄に反映し、学習を頼んだあとに呼ぶ。 */
    fun afterApply(update: Update) {
        if (update.composing.isEmpty()) {
            reset()
        } else if (update.commit.isNotEmpty() && text != null) {
            // 先頭だけ確定して残りが未確定のまま続くとき。確定した分も、残りにとっては直前の文章になる。
            text = (text + update.commit).takeLast(length)
        }
    }

    /** 未確定文字が、入力欄の側でなくなった。 */
    fun reset() {
        composing = false
        text = null
    }
}
