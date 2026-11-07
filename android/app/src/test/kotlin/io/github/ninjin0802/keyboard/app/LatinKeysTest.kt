// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.Direction
import io.github.ninjin0802.keyboard.app.input.charAt
import io.github.ninjin0802.keyboard.app.layout.LatinKeys
import kotlin.test.Test
import kotlin.test.assertEquals

/** 英字フリック配列のフリックの割り当て（spec 04 の表の全行・全方向）。 */
class LatinKeysTest {
    /** spec 04 の表の写し。キーごとに「タップ 左 上 右 下」。文字のない向きは「なし」。 */
    private val spec = """
        @ # / & 1
        a b c なし 2
        d e f なし 3
        g h i なし 4
        j k l なし 5
        m n o なし 6
        p q r s 7
        t u v なし 8
        w x y z 9
        ' " ( ) 0
        . , ? ! -
    """.trimIndent().lines().map { line -> line.trim().split(" ").map { if (it == "なし") null else it.single() } }

    @Test fun every_key_and_every_direction_matches_the_table() {
        assertEquals(spec, LatinKeys.all.map { key -> Direction.entries.map { key.charAt(it) } })
    }
    @Test fun toggle_input_goes_round_the_letters_and_ends_with_the_digit() {
        assertEquals(listOf("@#/&1", "abc2", "def3", "ghi4", "jkl5", "mno6", "pqrs7", "tuv8", "wxyz9", "'\"()0", ".,?!-"), LatinKeys.all.map { it.cycle })
    }
    @Test fun named_keys() {
        assertEquals("abc2", LatinKeys.ABC.cycle); assertEquals("pqrs7", LatinKeys.PQRS.cycle); assertEquals("wxyz9", LatinKeys.WXYZ.cycle)
        assertEquals(11, LatinKeys.all.toSet().size)
    }
}
