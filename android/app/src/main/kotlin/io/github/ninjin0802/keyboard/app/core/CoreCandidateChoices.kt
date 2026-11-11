// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

/**
 * What a tap on a candidate does. [CORE] is an item of the Core's one list (view.candidates);
 * the others are fixed choices that send F6 / F7 / F10 and then Enter.
 */
internal enum class CoreChoiceKind(val vk: Int) {
    CORE(0), TO_HIRAGANA(0x75), TO_KATAKANA(0x76), TO_LATIN(0x79)
}

internal data class CoreChoice(
    val label: String,
    val kind: CoreChoiceKind,
    /** Index in the Core's list for [CoreChoiceKind.CORE] (CandidateClicked), otherwise -1. */
    val nativeIndex: Int = -1,
)

/** Maps the Core's view to the Android candidate bar while preserving native indices. */
internal object CoreCandidateChoices {
    /** Labels of the fixed choices shown while typing (not converting). */
    val fixedChoices = listOf(
        CoreChoice("ひらがな", CoreChoiceKind.TO_HIRAGANA),
        CoreChoice("カタカナ", CoreChoiceKind.TO_KATAKANA),
        CoreChoice("英字", CoreChoiceKind.TO_LATIN),
    )

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

    /** The choices to show and which of them is selected. */
    fun fromView(view: ImeView?, allowEmoji: Boolean = true): Pair<List<CoreChoice>, Int?> {
        if (view == null || view.text.isEmpty()) return emptyList<CoreChoice>() to null
        val result = ArrayList<CoreChoice>()
        val list = view.candidates
        list?.items?.forEachIndexed { index, label ->
            if (label.isNotBlank() && (allowEmoji || !hasEmoji(label))) result.add(CoreChoice(label, CoreChoiceKind.CORE, index))
        }
        val selected = list?.let { l -> result.indexOfFirst { it.nativeIndex == l.selected }.takeIf { it >= 0 } }
        if (!view.converting) result.addAll(fixedChoices)
        return result to selected
    }
}
