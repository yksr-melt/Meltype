// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.EditorBridge
import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 削除の1回で消す長さを、端末の上で確かめる。国旗や ZWJ でつないだ絵文字をどこで分けるかは、端末の Android（ICU）が
 * 決める。PC の Java 17 はその規則を持たないので、単体テストでは確かめられない。
 */
class DeleteLengthDeviceTest {
    private val japan = "\uD83C\uDDEF\uD83C\uDDF5"                       // 🇯🇵
    private val usa = "\uD83C\uDDFA\uD83C\uDDF8"                         // 🇺🇸
    private val family = "\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67\u200D\uD83D\uDC66"   // 👨‍👩‍👧‍👦
    private val thumbsUpMedium = "\uD83D\uDC4D\uD83C\uDFFD"              // 👍🏽
    private val keycapOne = "1\uFE0F\u20E3"                              // 1️⃣
    private val england = "\uD83C\uDFF4\uDB40\uDC67\uDB40\uDC62\uDB40\uDC65\uDB40\uDC6E\uDB40\uDC67\uDB40\uDC7F"   // 🏴󠁧󠁢󠁥󠁮󠁧󠁿
    private val face = "\uD83D\uDE00"                                    // 😀

    private fun deleted(text: String) = EditorBridge.deleteLength(text.takeLast(EditorBridge.DELETE_READ_LENGTH))

    @Test fun a_flag_goes_in_one_press() {
        assertEquals(4, japan.length); assertEquals(4, deleted(japan)); assertEquals(4, deleted("a$japan"))
    }

    @Test fun of_two_flags_in_a_row_only_the_last_goes() {
        assertEquals(4, deleted(japan + usa)); assertEquals(4, deleted("a$japan$usa$japan"))
        // 入力欄から読むのは 16 単位まで。国旗がそれより長く続いていても、消えるのは1つ。
        assertEquals(4, deleted(japan + usa + japan + usa + japan + usa))
    }

    @Test fun a_zwj_family_goes_in_one_press() {
        assertEquals(11, family.length); assertEquals(11, deleted(family)); assertEquals(11, deleted("かな$family"))
        assertEquals(11, deleted(face + family))
    }

    @Test fun a_skin_toned_emoji_goes_in_one_press() {
        assertEquals(4, deleted(thumbsUpMedium)); assertEquals(4, deleted(face + thumbsUpMedium))
    }

    @Test fun a_keycap_goes_in_one_press() {
        assertEquals(3, deleted(keycapOne)); assertEquals(3, deleted("12$keycapOne"))
    }

    @Test fun a_subdivision_flag_goes_in_one_press() {
        assertEquals(14, england.length); assertEquals(14, deleted(england)); assertEquals(14, deleted("a$england"))
    }

    @Test fun a_combining_dakuten_goes_with_its_kana() {
        assertEquals(2, deleted("か\u3099")); assertEquals(2, deleted("あか\u3099"))
    }

    @Test fun a_surrogate_pair_goes_whole() {
        assertEquals(2, deleted(face)); assertEquals(2, deleted("a$face")); assertEquals(2, deleted(family + face))
    }

    @Test fun plain_text_goes_one_character_at_a_time() {
        assertEquals(1, deleted("a")); assertEquals(1, deleted("ab")); assertEquals(1, deleted(japan + "a"))
        assertEquals(1, deleted("かな")); assertEquals(0, deleted(""))
    }
}
