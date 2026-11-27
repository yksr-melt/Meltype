// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
package app.meltype.keyboard.core

/** An item of the Core's one list (view.candidates). A tap sends CandidateClicked with [nativeIndex]. */
internal data class CoreChoice(
    val label: String,
    /** Index in the Core's list. */
    val nativeIndex: Int,
)

/** Maps the Core's view to the Android candidate bar while preserving native indices. */
internal object CoreCandidateChoices {
    /** The choices to show (only the Core's list; nothing when the Core gives none) and which of them is selected. */
    fun fromView(view: ImeView?): Pair<List<CoreChoice>, Int?> {
        if (view == null || view.text.isEmpty()) return emptyList<CoreChoice>() to null
        val result = ArrayList<CoreChoice>()
        val list = view.candidates
        list?.items?.forEachIndexed { index, label ->
            if (label.isNotBlank()) result.add(CoreChoice(label, index))
        }
        val selected = list?.let { l -> result.indexOfFirst { it.nativeIndex == l.selected }.takeIf { it >= 0 } }
        return result to selected
    }
}
