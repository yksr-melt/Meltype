// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import android.view.KeyEvent
import android.view.inputmethod.ExtractedTextRequest
import android.view.inputmethod.InputConnection
import io.github.ninjin0802.keyboard.engine.Candidate
import io.github.ninjin0802.keyboard.engine.CandidateKind

/**
 * Applies the Core's response (ImeOutput) to the Android editor. This is
 * intentionally separate from the existing flick-input EditorBridge.
 * A malformed response must not erase existing editor text: it is parsed
 * completely before anything is written.
 */
internal class CoreInputAdapter(private val connection: () -> InputConnection?) {
    /** The composing text this adapter shows in the editor ("" if none). */
    var shown: String = ""
        private set
    val composing: Boolean get() = shown.isNotEmpty()
    var emojiEnabled: Boolean = true
        set(value) {
            field = value
            refreshChoices()
        }
    private var latestView: ImeView? = null
    var choices: List<CoreChoice> = emptyList()
        private set
    var candidates: List<Candidate> = emptyList()
        private set
    var selected: Int? = null
        private set

    fun clear() {
        shown = ""
        latestView = null
        choices = emptyList()
        candidates = emptyList()
        selected = null
    }

    private fun refreshChoices() {
        val (nextChoices, nextSelected) = CoreCandidateChoices.fromView(latestView, emojiEnabled)
        choices = nextChoices
        val kind = if (latestView?.converting == true) CandidateKind.CONVERSION else CandidateKind.PREDICTION
        candidates = nextChoices.map { Candidate(it.label, 0, kind) }
        selected = nextSelected
    }

    /**
     * Facts about the editor for the next event. [before] leaves out the composing text,
     * which this adapter keeps right before the cursor.
     */
    fun field(traits: List<String>, beforeLength: Int = FIELD_CONTEXT, afterLength: Int = FIELD_CONTEXT): ImeFieldFacts {
        val input = connection()
        val text = input?.getTextBeforeCursor(beforeLength + shown.length, 0)?.toString()
        val before = text?.let { if (composing && it.endsWith(shown)) it.dropLast(shown.length) else if (composing) null else it }
        val after = input?.getTextAfterCursor(afterLength, 0)?.toString()
        return ImeFieldFacts(before, after, composing, traits)
    }

    /** Applies a response. Returns whether the Core consumed the key. Throws for a corrupted response. */
    fun apply(payload: String?): Boolean {
        if (payload == null) return false
        val result = ImeJson.parse(payload)
        val input = connection()
        if (input != null) {
            input.beginBatchEdit()
            try {
                for (edit in result.commits) {
                    val remove = edit.removeBefore?.length ?: 0
                    if (remove > 0) {
                        // removeBefore is text before the composing text: take the composing text away first.
                        if (composing) input.setComposingText("", 1)
                        input.deleteSurroundingText(remove, 0)
                    }
                    // While composing text is shown, commitText replaces it.
                    input.commitText(edit.text, 1)
                    shown = ""
                    if (edit.moveCaretBack > 0) moveCaretBack(input, edit.moveCaretBack)
                }
                val view = result.view
                if (view != null) {
                    input.setComposingText(view.text, 1)
                    shown = view.text
                } else if (composing) {
                    input.setComposingText("", 1)
                    shown = ""
                }
            } finally {
                input.endBatchEdit()
            }
        } else {
            shown = result.view?.text ?: ""
        }
        latestView = result.view
        refreshChoices()
        return result.consumed
    }

    /** Moves the cursor [count] UTF-16 units to the left of where the last commit left it. */
    private fun moveCaretBack(input: InputConnection, count: Int) {
        val extracted = input.getExtractedText(ExtractedTextRequest(), 0)
        if (extracted != null && extracted.selectionStart >= 0) {
            val position = (extracted.startOffset + extracted.selectionStart - count).coerceAtLeast(0)
            input.setSelection(position, position)
        } else {
            repeat(count) {
                input.sendKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_DPAD_LEFT))
                input.sendKeyEvent(KeyEvent(KeyEvent.ACTION_UP, KeyEvent.KEYCODE_DPAD_LEFT))
            }
        }
    }

    companion object {
        /** Characters around the cursor sent as field.before / field.after. */
        const val FIELD_CONTEXT = 200
    }
}
