// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package app.meltype.keyboard

import app.meltype.keyboard.core.ImeView
import app.meltype.keyboard.core.selectedClauseRange
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class SelectedClauseRangeTest {
    private fun converting(clauses: List<String>, selected: Int) =
        ImeView(text = clauses.joinToString(""), converting = true, candidates = null, clauses = clauses, selectedClause = selected)

    @Test fun marksTheSelectedClauseSoTheResizedBoundaryIsVisible() {
        assertEquals(0 until 2, selectedClauseRange(converting(listOf("今日", "は", "いい天気"), 0)))
        assertEquals(3 until 7, selectedClauseRange(converting(listOf("今日", "は", "いい天気"), 2)))
        // サロゲートペアは UTF-16 で数える (入力欄の位置と同じ)
        assertEquals(3 until 4, selectedClauseRange(converting(listOf("𠮷野", "家"), 1)))
    }

    @Test fun marksNothingWhenNotConvertingOrTheClausesDoNotMakeUpTheText() {
        assertNull(selectedClauseRange(ImeView(text = "きょう", converting = false, candidates = null)))
        assertNull(selectedClauseRange(ImeView(text = "今日は", converting = true, candidates = null, clauses = listOf("今日", "が"), selectedClause = 0)))
        assertNull(selectedClauseRange(converting(listOf("今日", "は"), 5)))
    }
}
