// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import org.json.JSONObject

/** Every visible suggestion is exactly the string that a tap will commit. */
internal enum class CoreChoiceKind {
    CONVERSION, PREDICTION, KANA_CORRECTION, KATAKANA_CORRECTION, LATIN_CORRECTION
}

internal data class CoreChoice(
    val label: String,
    val kind: CoreChoiceKind,
    val nativeIndex: Int = -1,
)

/** Maps Core JSON to Android candidate strings while preserving native indices. */
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

    fun fromView(view: JSONObject?, allowEmoji: Boolean = true): Pair<List<CoreChoice>, Int?> {
        if (view == null || view.optString("text").isEmpty()) return emptyList<CoreChoice>() to null
        val composing = view.optString("text")
        val converting = view.optBoolean("converting", false)
        val result = ArrayList<CoreChoice>()
        val labels = HashSet<String>()
        fun add(label: String, kind: CoreChoiceKind, nativeIndex: Int = -1) {
            if (label.isNotBlank() && labels.add(label) && (allowEmoji || !hasEmoji(label))) {
                result.add(CoreChoice(label, kind, nativeIndex))
            }
        }
        val ordinary = view.optJSONArray("candidates")
        val predicted = view.optJSONArray("predictions")

        if (converting) {
            if (ordinary != null) for (i in 0 until ordinary.length()) {
                add(ordinary.optString(i), CoreChoiceKind.CONVERSION, i)
            }
            val nativeSelected = view.optInt("selectedIndex", -1)
            val selected = result.indexOfFirst {
                it.kind == CoreChoiceKind.CONVERSION && it.nativeIndex == nativeSelected
            }.takeIf { it >= 0 }
            return result to selected
        }

        if (predicted != null) for (i in 0 until predicted.length()) {
            add(predicted.optString(i), CoreChoiceKind.PREDICTION, i)
        }
        if (ordinary != null) for (i in 0 until ordinary.length()) {
            add(ordinary.optString(i), CoreChoiceKind.CONVERSION, i)
        }

        // Core always retains the original keystrokes and their kana reading.
        // Surface corrected text itself, not 'switch language' command buttons.
        val original = view.optString("raw")
        val kana = view.optString("kana")
        if (original.isNotBlank() && original != composing) {
            add(original, CoreChoiceKind.LATIN_CORRECTION)
        }
        if (kana.isNotBlank() && kana != composing && kana != original &&
            kana.any { it in 'ぁ'..'ゖ' || it == 'ー' }) {
            add(kana, CoreChoiceKind.KANA_CORRECTION)
            val katakana = buildString {
                kana.forEach { ch ->
                    append(if (ch in 'ぁ'..'ゖ') (ch.code + 0x60).toChar() else ch)
                }
            }
            if (katakana != kana) add(katakana, CoreChoiceKind.KATAKANA_CORRECTION)
        }

        val nativeSelected = view.optInt("selectedPrediction", -1)
        val selected = result.indexOfFirst {
            it.kind == CoreChoiceKind.PREDICTION && it.nativeIndex == nativeSelected
        }.takeIf { it >= 0 }
        return result to selected
    }
}
