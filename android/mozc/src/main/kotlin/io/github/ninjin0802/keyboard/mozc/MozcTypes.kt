// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.mozc

/** Text units returned by the Android Mozc engine to the C# Core callback. */
data class MozcSegment(val reading: String, val candidates: List<String>)
data class MozcClause(val reading: String, val text: String)

/**
 * The OS-specific Mozc adapter executes conversion requested by Meltype.Core.
 * It does not decide how keystrokes are composed, converted, or committed.
 */
interface MozcBackend {
    fun convert(reading: String, context: String?): List<MozcSegment>?
    fun predict(reading: String): List<String>
    fun learn(clauses: List<MozcClause>, context: String?)
}
