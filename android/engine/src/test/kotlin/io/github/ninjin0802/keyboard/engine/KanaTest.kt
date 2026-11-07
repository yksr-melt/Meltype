// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class KanaTest {
    private fun cycle(start: Char) = generateSequence(start) { Kana.modify(it) }.take(4).joinToString("")

    @Test fun ka_sa_ta_rows_toggle_dakuten() {
        for ((a, b) in "かが きぎ くぐ けげ こご さざ しじ すず せぜ そぞ ただ ちぢ てで とど".split(" ").map { it[0] to it[1] }) {
            assertEquals(b, Kana.modify(a)); assertEquals(a, Kana.modify(b))
        }
    }
    @Test fun tsu_cycles_small_then_dakuten() = assertEquals("つっづつ", cycle('つ'))
    @Test fun ha_row_cycles_dakuten_then_handakuten() {
        for (s in listOf("はばぱは", "ひびぴひ", "ふぶぷふ", "へべぺへ", "ほぼぽほ")) assertEquals(s, cycle(s[0]))
    }
    @Test fun vowels_ya_row_wa_toggle_small() {
        for ((a, b) in "あぁ いぃ えぇ おぉ やゃ ゆゅ よょ わゎ".split(" ").map { it[0] to it[1] }) {
            assertEquals(b, Kana.modify(a)); assertEquals(a, Kana.modify(b))
        }
    }
    @Test fun u_cycles_small_then_vu() = assertEquals("うぅゔう", cycle('う'))
    @Test fun others_are_unchanged() { for (c in "なまらん、ー（a") assertNull(Kana.modify(c)) }
    @Test fun katakana() = assertEquals("グーグル（、a", Kana.toKatakana("ぐーぐる（、a"))
    @Test fun key_cycle_wraps() {
        assertEquals('い', KanaKeys.A.next('あ')); assertEquals('あ', KanaKeys.A.next('お'))
        assertEquals('（', KanaKeys.YA.next('や')); assertEquals('か', KanaKeys.KA.next('x'))
    }
    @Test fun all_keys_have_five_chars() { assertEquals(11, KanaKeys.all.size); KanaKeys.all.forEach { assertEquals(5, it.chars.length) } }
}
