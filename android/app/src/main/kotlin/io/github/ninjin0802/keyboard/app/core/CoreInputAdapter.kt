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
internal class CoreInputAdapter(
    /** Records where these edits leave the caret, to tell them from moves by the user or the app. */
    private val caret: CaretWatch = CaretWatch(),
    private val connection: () -> InputConnection?,
) {
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

    /** The view of the last response (null when nothing is composing). */
    val view: ImeView? get() = latestView
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
     * Facts about the editor for the next event. [before] leaves out the composing text, which this
     * adapter keeps right before the cursor. An editor that cannot be read ([readable] false: the
     * editor has no text of its own) sends neither side and cannot remove text before the caret.
     * [selected] tells that the editor reported a selection, so that it is read only then.
     */
    fun field(
        traits: List<String>,
        app: String? = null,
        readable: Boolean = true,
        selected: Boolean = false,
    ): ImeFieldFacts {
        val input = connection()?.takeIf { readable }
        val text = input?.getTextBeforeCursor(BEFORE_LENGTH + shown.length, 0)?.toString()
        val before = text?.let { if (composing && it.endsWith(shown)) it.dropLast(shown.length) else if (composing) null else it }
        val after = input?.getTextAfterCursor(AFTER_LENGTH, 0)?.toString()
        val selection = if (selected && !composing) input?.getSelectedText(0)?.toString()?.takeIf { it.isNotEmpty() } else null
        return ImeFieldFacts(before, after, composing, traits, canRemove = readable, selection = selection, app = app)
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
                        if (composing) {
                            input.setComposingText("", 1)
                            caret.replaced(shown.length, 0)
                            shown = ""
                        }
                        input.deleteSurroundingText(remove, 0)
                        caret.replaced(remove, 0)
                    }
                    // While composing text is shown, commitText replaces it.
                    input.commitText(edit.text, 1)
                    caret.replaced(shown.length, edit.text.length)
                    shown = ""
                    if (edit.moveCaretBack > 0) {
                        moveCaretBack(input, edit.moveCaretBack)
                        caret.movedBack(edit.moveCaretBack)
                    }
                }
                val view = result.view
                if (view != null) {
                    input.setComposingText(view.text, 1)
                    caret.replaced(shown.length, view.text.length)
                    shown = view.text
                } else if (composing) {
                    input.setComposingText("", 1)
                    caret.replaced(shown.length, 0)
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
        /**
         * UTF-16 units before the cursor sent as field.before: the Core reads up to 4000 in a code
         * editor (ImeField.Before), and the Mac and IBus fronts send the same.
         */
        const val BEFORE_LENGTH = 4000

        /** UTF-16 units after the cursor sent as field.after (as the Mac and IBus fronts). */
        const val AFTER_LENGTH = 20
    }
}
