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
    /** 文節に区切った変換。変換できない読みは空のリスト、失敗 (時間切れ・Mozc を使えない) は null。 */
    fun convert(reading: String, context: String?): List<MozcSegment>?
    /** 確定した文節を覚えさせる。覚えたら true。 */
    fun learn(clauses: List<MozcClause>, context: String?): Boolean
    /** 漢字かな混じりの [text] の読み (逆変換)。できなければ null。 */
    fun reading(text: String): String?
}
