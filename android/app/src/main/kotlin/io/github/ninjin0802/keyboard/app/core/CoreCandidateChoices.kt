// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

/** An item of the Core's one list (view.candidates). A tap sends CandidateClicked with [nativeIndex]. */
internal data class CoreChoice(
    val label: String,
    /** Index in the Core's list. */
    val nativeIndex: Int,
)

/** Maps the Core's view to the Android candidate bar while preserving native indices. */
internal object CoreCandidateChoices {
    /** Screens out only candidates containing Unicode emoji, not ordinary punctuation. */
    fun hasEmoji(text: String): Boolean {
        var index = 0
        while (index < text.length) {
            val codePoint = Character.codePointAt(text, index)
            // Emoji blocks and commonly rendered symbol emoji (☀ / ♡ / ☺).
            if (codePoint in 0x1F000..0x1FAFF ||
                codePoint in 0x2600..0x27BF ||
                codePoint == 0xFE0F || codePoint == 0x20E3
            ) return true
            index += Character.charCount(codePoint)
        }
        return false
    }

    /** The choices to show (only the Core's list; nothing when the Core gives none) and which of them is selected. */
    fun fromView(view: ImeView?, allowEmoji: Boolean = true): Pair<List<CoreChoice>, Int?> {
        if (view == null || view.text.isEmpty()) return emptyList<CoreChoice>() to null
        val result = ArrayList<CoreChoice>()
        val list = view.candidates
        list?.items?.forEachIndexed { index, label ->
            if (label.isNotBlank() && (allowEmoji || !hasEmoji(label))) result.add(CoreChoice(label, index))
        }
        val selected = list?.let { l -> result.indexOfFirst { it.nativeIndex == l.selected }.takeIf { it >= 0 } }
        return result to selected
    }
}
