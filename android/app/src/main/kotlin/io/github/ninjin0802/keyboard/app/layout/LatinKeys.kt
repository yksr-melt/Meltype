// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.layout

import io.github.ninjin0802.keyboard.engine.FlickKey

/** 英字フリック配列の文字キー（spec 04「英字フリック配列」の表）。下のフリックは、電話のキーと同じ数字。 */
object LatinKeys {
    /** 英字が3つのキー。右には文字がない。 */
    private fun three(letters: String, digit: Char) = FlickKey("$letters${FlickKey.NONE}$digit")

    val SYMBOL = FlickKey("@#/&1")
    val ABC = three("abc", '2')
    val DEF = three("def", '3')
    val GHI = three("ghi", '4')
    val JKL = three("jkl", '5')
    val MNO = three("mno", '6')
    val PQRS = FlickKey("pqrs7")
    val TUV = three("tuv", '8')
    val WXYZ = FlickKey("wxyz9")
    val QUOTE = FlickKey("'\"()0")
    val PUNCT = FlickKey(".,?!-")

    /** 配列に並ぶ順（左上から右へ、上の段から下へ）。 */
    val all: List<FlickKey> = listOf(SYMBOL, ABC, DEF, GHI, JKL, MNO, PQRS, TUV, WXYZ, QUOTE, PUNCT)

    /** キーに書く文字。タップと左・上・右の文字を並べたもの（数字は書かない）。 */
    fun label(key: FlickKey): String = key.cycle.dropLast(1)
}
