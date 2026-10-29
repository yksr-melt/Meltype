// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import android.view.inputmethod.InputConnection
import io.github.ninjin0802.keyboard.engine.Candidate
import io.github.ninjin0802.keyboard.engine.CandidateKind
import org.json.JSONObject

/**
 * Interprets the original MeltypeSession result contract on Android. This is
 * intentionally separate from the existing flick-input EditorBridge.
 * A malformed result must not erase existing editor text.
 */
internal class CoreInputAdapter(private val connection: () -> InputConnection?) {
    var composing: Boolean = false
        private set
    var emojiEnabled: Boolean = true
        set(value) {
            field = value
            refreshChoices()
        }
    private var latestView: JSONObject? = null
    var choices: List<CoreChoice> = emptyList()
        private set
    var candidates: List<Candidate> = emptyList()
        private set
    var selected: Int? = null
        private set

    fun clear() {
        composing = false
        latestView = null
        choices = emptyList()
        candidates = emptyList()
        selected = null
    }

    private fun refreshChoices() {
        val (nextChoices, nextSelected) = CoreCandidateChoices.fromView(latestView, emojiEnabled)
        choices = nextChoices
        candidates = nextChoices.map {
            Candidate(it.label, 0, if (it.kind == CoreChoiceKind.PREDICTION)
                CandidateKind.PREDICTION else CandidateKind.CONVERSION)
        }
        selected = nextSelected
    }

    /** Whether the core consumed the key. Throws for corrupted JSON. */
    fun apply(payload: String?): Boolean {
        if (payload == null) return false
        val result = JSONObject(payload)
        val commits = result.getJSONArray("commits")
        val view = result.optJSONObject("view")
        val input = connection()
        if (input != null) {
            input.beginBatchEdit()
            try {
                for (index in 0 until commits.length()) {
                    val edit = commits.getJSONObject(index)
                    val deleteCount = edit.getInt("deleteBefore")
                    val inserted = edit.getString("text")
                    if (deleteCount > 0 && !edit.isNull("expect")) {
                        val expected = edit.getString("expect")
                        val previous = input.getTextBeforeCursor(deleteCount, 0)?.toString()
                        if (expected.length == deleteCount && previous == expected) {
                            input.deleteSurroundingText(deleteCount, 0)
                        }
                    }
                    input.commitText(inserted, 1)
                    composing = false
                }
                if (view != null) {
                    val content = view.getString("text")
                    input.setComposingText(content, 1)
                    composing = content.isNotEmpty()
                } else if (composing) {
                    input.setComposingText("", 1)
                    composing = false
                }
            } finally {
                input.endBatchEdit()
            }
        }
        latestView = view
        refreshChoices()
        return result.getBoolean("consumed")
    }
}
