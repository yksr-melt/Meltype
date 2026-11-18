// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
package io.github.ninjin0802.keyboard.app.core

import io.github.ninjin0802.keyboard.mozc.MozcBackend
import io.github.ninjin0802.keyboard.mozc.MozcClause
import io.github.ninjin0802.keyboard.mozc.MozcSegment
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** CoreMozcCallbacks answers the Core's MozcConverter in the line protocol of native/mozc/meltype_mozc_helper.cc. */
class CoreMozcCallbacksDeviceTest {
    private val learned = mutableListOf<Pair<List<MozcClause>, String?>>()

    private val backend = object : MozcBackend {
        override fun convert(reading: String, context: String?): List<MozcSegment>? = when (reading) {
            "あめがふる" -> listOf(MozcSegment("あめが", listOf("雨が", "飴が")), MozcSegment("ふる", listOf("降る", "振る")))
            "たいむあうと" -> null
            else -> emptyList()
        }
        override fun learn(clauses: List<MozcClause>, context: String?): Boolean {
            learned += clauses to context
            return clauses.first().text == "雨が"
        }
        override fun reading(text: String): String? = if (text == "雨") "あめ" else null
    }

    private fun ask(request: String, mozc: Lazy<MozcBackend?> = lazyOf(backend)): String? =
        CoreMozcCallbacks(mozc) {}.request(request.encodeToByteArray())?.decodeToString()

    @Test fun conversionListsEachClauseWithItsCandidates() =
        assertEquals("あめが\u001f雨が\u001f飴が\u001eふる\u001f降る\u001f振る", ask("C\tきょうは\tあめがふる"))

    // The Core keeps an empty answer for the reading, and does not keep a failure.
    @Test fun anUnconvertibleReadingIsAnEmptyLineAndAFailureIsNoAnswer() {
        assertEquals("", ask("C\t\tほげ"))
        assertNull(ask("C\t\tたいむあうと"))
    }

    @Test fun learningPassesTheClausesAndTheContext() {
        assertEquals("OK", ask("L\tきょうは\tあめが\u001f雨が\u001eふる\u001f降る"))
        assertEquals(listOf(MozcClause("あめが", "雨が"), MozcClause("ふる", "降る")) to "きょうは", learned.single())
        assertEquals("NG", ask("L\t\tあめが\u001f飴が"))
        assertNull(learned.last().second)
    }

    @Test fun reverseConversion() {
        assertEquals("あめ", ask("R\t雨"))
        assertEquals("", ask("R\t晴"))
    }

    // A reconversion asks on the input thread: it must not wait for Mozc to open.
    @Test fun reverseConversionDoesNotOpenMozc() {
        val unopened = lazy<MozcBackend?> { throw AssertionError("Mozc was opened") }
        assertEquals("", ask("R\t雨", unopened))
    }

    @Test fun mozcThatCannotOpenIsNoAnswer() = assertNull(ask("C\t\tあめがふる", lazyOf(null)))
}
