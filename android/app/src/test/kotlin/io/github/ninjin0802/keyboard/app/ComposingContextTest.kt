// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.ComposingContext
import io.github.ninjin0802.keyboard.engine.ConversionRequest
import io.github.ninjin0802.keyboard.engine.Update
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/** 未確定文字の直前の文章（変換の手がかり）を、いつ読むか（spec 06）。 */
class ComposingContextTest {
    private val context = ComposingContext(length = 20)

    /** 入力欄。[committed] のあとに、未確定文字 [composing] が出ている。読めない入力欄では [readable] が false。 */
    private var committed = "前の文"
    private var composing = ""
    private var readable = true
    private var reads = 0

    /** KeyboardService と同じ順: 読むかどうか決める → 入力欄に反映する → 後始末。 */
    private fun render(update: Update, converts: Boolean = true): String? {
        context.beforeApply(update, converts) { n -> reads++; if (readable) (committed + composing).takeLast(n) else null }
        committed += update.commit; composing = update.composing
        val used = context.text
        context.afterApply(update)
        return used
    }
    private fun request(reading: String) = ConversionRequest(1, reading)

    @Test fun twelve_key_kana_reads_once_when_the_first_kana_starts_the_composition() {
        assertEquals("前の文", render(Update(composing = "か", request = request("か"))))
        assertEquals("前の文", render(Update(composing = "かき", request = request("かき"))))
        assertEquals("前の文", render(Update(composing = "かき")))                         // 変換結果が返ったとき
        assertEquals(1, reads)
    }
    @Test fun romaji_reads_before_the_first_pending_letter_is_shown() {
        assertEquals("前の文", render(Update(composing = "k")))                             // 変換の依頼はまだない
        assertEquals("前の文", render(Update(composing = "か", request = request("か"))))    // ここで読むと「前の文k」になる
        assertEquals(1, reads); assertEquals("前の文", context.text)
    }
    @Test fun the_context_is_dropped_when_the_composition_ends_and_read_again_for_the_next_one() {
        render(Update(composing = "か", request = request("か")))
        assertEquals("前の文", render(Update(commit = "蚊")))                                // 学習には、確定の前の文脈を使う
        assertNull(context.text)
        assertEquals("前の文蚊", render(Update(composing = "き", request = request("き")))); assertEquals(2, reads)
    }
    @Test fun a_partial_pick_extends_the_context_by_the_committed_head() {
        render(Update(composing = "きょうはてんき", request = request("きょうはてんき")))
        render(Update(commit = "今日は", composing = "てんき", request = request("てんき")))
        assertEquals("前の文今日は", context.text); assertEquals(1, reads)
    }
    @Test fun the_context_is_cut_to_its_length() {
        committed = "あ".repeat(30)
        assertEquals("あ".repeat(20), render(Update(composing = "か")))
        render(Update(commit = "いろはにほ", composing = "へ"))
        assertEquals("あ".repeat(15) + "いろはにほ", context.text)
    }
    @Test fun nothing_is_read_when_nothing_starts_composing() {
        assertNull(render(Update(commit = "a"))); assertNull(render(Update())); assertEquals(0, reads)
    }
    @Test fun a_field_that_does_not_convert_is_never_read() {
        assertNull(render(Update(composing = "か"), converts = false))
        assertNull(render(Update(commit = "か", composing = "き"), converts = false))
        assertEquals(0, reads); assertNull(context.text)
    }
    @Test fun an_unreadable_field_is_asked_once_per_composition() {
        readable = false
        assertNull(render(Update(composing = "k"))); assertNull(render(Update(composing = "か", request = request("か"))))
        assertEquals(1, reads)
    }
    @Test fun reset_forgets_the_composition() {
        render(Update(composing = "か")); context.reset(); assertNull(context.text)
        composing = ""; assertEquals("前の文", render(Update(composing = "き"))); assertEquals(2, reads)
    }
}
