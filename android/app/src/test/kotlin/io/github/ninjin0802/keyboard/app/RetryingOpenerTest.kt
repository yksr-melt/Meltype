// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.RetryingOpener
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class RetryingOpenerTest {
    private var now = 1_000_000L
    private var attempts = 0
    private var result: String? = null
    private val opener = RetryingOpener({ attempts++; result }, { now }, maxRetries = 3, minIntervalMs = 30_000)

    @Test fun success_is_kept_and_never_opened_again() {
        result = "mozc"
        assertEquals("mozc", opener.get()); assertEquals("mozc", opener.get())
        assertEquals(1, attempts)
    }
    @Test fun a_failure_is_not_retried_before_the_interval() {
        assertNull(opener.get())
        repeat(100) { assertNull(opener.get()) }       // 変換が続けて頼まれても、開き直しを繰り返さない
        now += 29_999; assertNull(opener.get())
        assertEquals(1, attempts)
    }
    @Test fun a_failure_is_retried_after_the_interval_and_can_succeed() {
        assertNull(opener.get())
        now += 30_000; result = "mozc"
        assertEquals("mozc", opener.get()); assertEquals(2, attempts)
        now += 60_000; assertEquals("mozc", opener.get()); assertEquals(2, attempts)
    }
    @Test fun the_interval_counts_from_the_end_of_the_failed_attempt() {
        val slow = RetryingOpener<String>({ attempts++; now += 20_000; null }, { now }, maxRetries = 3, minIntervalMs = 30_000)
        assertNull(slow.get())                          // 20 秒かかって失敗
        now += 29_999; assertNull(slow.get()); assertEquals(1, attempts)
        now += 1; assertNull(slow.get()); assertEquals(2, attempts)
    }
    @Test fun retries_stop_at_the_cap_and_stay_stopped() {
        repeat(20) { assertNull(opener.get()); now += 30_000 }
        assertEquals(1 + 3, attempts)                   // 最初の1回 + やり直し3回
        result = "mozc"; now += 10_000_000
        assertNull(opener.get()); assertEquals(4, attempts)
    }
    @Test fun an_opener_that_throws_counts_as_a_failure() {
        val throwing = RetryingOpener<String>({ attempts++; throw IllegalStateException("x") }, { now }, maxRetries = 3, minIntervalMs = 30_000)
        assertNull(throwing.get()); assertNull(throwing.get()); assertEquals(1, attempts)
    }
    @Test fun defaults_are_thirty_seconds_and_five_retries() {
        val d = RetryingOpener<String>({ attempts++; null }, { now })
        repeat(20) { d.get(); now += 29_999; d.get(); now += 1 }
        assertEquals(1 + 5, attempts)
    }
}
