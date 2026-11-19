// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

/**
 * Tells the keyboard's own edits for the Core from caret moves made by the user or the app
 * (a tap, the arrow keys, the app rewriting its text), so that only the latter become CaretMoved.
 *
 * Android reports the selection late (onUpdateSelection) and may merge several edits into one
 * report, so every position this keyboard's edits will leave behind is kept until the editor
 * reports it or a later one. A report that matches none of them came from outside.
 * Positions are UTF-16 offsets in the editor, like the ones onUpdateSelection reports.
 *
 * Only the edits made for the Core (its commits and composing text, and keys it passed) are
 * recorded. Anything else that moves the caret — the Kotlin input path, the cursor keys,
 * the space-bar cursor — is a move the Core did not make, and is reported as one.
 */
internal class CaretWatch {
    /** Where the edits so far leave the selection, or -1 while it is not known. */
    private var start = -1
    private var end = -1

    /** Positions ([start], [end]) left by own edits that the editor has not reported yet, oldest first. */
    private val pending = ArrayDeque<Pair<Int, Int>>()

    /** An own edit moved the caret by an amount this class cannot tell: take the next report as own. */
    private var lost = false

    /** The editor told where the selection is (a new field: EditorInfo.initialSelStart / End). */
    fun reset(selectionStart: Int, selectionEnd: Int) {
        pending.clear()
        lost = selectionStart < 0 || selectionEnd < 0
        start = if (lost) -1 else minOf(selectionStart, selectionEnd)
        end = if (lost) -1 else maxOf(selectionStart, selectionEnd)
    }

    /**
     * An own edit removed [removed] UTF-16 units right before the caret (the composing text, or
     * text the Core re-commits) or, when [removed] is 0, the selected text, and inserted [inserted]
     * units there. The caret ends up after the inserted text.
     */
    fun replaced(removed: Int, inserted: Int) {
        if (lost) return
        val from = if (removed > 0) (start - removed).coerceAtLeast(0) else start
        expect(from + inserted, from + inserted)
    }

    /** An own edit moved the caret [count] units to the left (moveCaretBack). */
    fun movedBack(count: Int) {
        if (lost || count <= 0) return
        val to = (start - count).coerceAtLeast(0)
        expect(to, to)
    }

    /** An own edit moved the caret by an unknown amount (a key the Core passed on to the app). */
    fun lose() {
        lost = true
        pending.clear()
    }

    /**
     * onUpdateSelection. True if the report comes from own edits (perhaps late, or several merged),
     * or nothing moved. False if the user or the app moved the caret; this class then continues
     * from the reported position.
     *
     * [composingEnd] is the end of the editor's composing region (-1 if none). While own composing
     * text is shown ([composing]), a caret right at its end is own as well: some editors report
     * positions this class cannot predict, and the composition must not break while typing.
     */
    fun isOwn(newStart: Int, newEnd: Int, composingEnd: Int = -1, composing: Boolean = false): Boolean {
        val reported = minOf(newStart, newEnd) to maxOf(newStart, newEnd)
        val index = pending.indexOf(reported)
        if (index >= 0) {
            repeat(index + 1) { pending.removeFirst() }
            return true
        }
        if (lost || reported == (start to end) || composing && composingEnd >= 0 && newStart == newEnd && newEnd == composingEnd) {
            if (lost || pending.isEmpty()) follow(reported)
            return true
        }
        follow(reported)
        return false
    }

    private fun follow(reported: Pair<Int, Int>) {
        pending.clear()
        lost = reported.first < 0
        start = reported.first
        end = reported.second
    }

    private fun expect(newStart: Int, newEnd: Int) {
        start = newStart
        end = newEnd
        pending.addLast(newStart to newEnd)
        // An editor that never reports (or one with a different idea of positions) must not grow this.
        while (pending.size > MAX_PENDING) pending.removeFirst()
    }

    private companion object {
        const val MAX_PENDING = 64
    }
}
