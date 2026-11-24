// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package app.meltype.keyboard.mozc

/** Text units returned by the Android Mozc engine to the C# Core callback. */
/** [userWord] は、最初の候補がユーザー辞書の語を含むか (活用した形も)。 */
data class MozcSegment(val reading: String, val candidates: List<String>, val userWord: Boolean = false)
data class MozcClause(val reading: String, val text: String)
/** ユーザー辞書の 1 語。[pos] は Mozc の品詞名 (名詞・動詞ラ行五段…)。 */
data class MozcWord(val reading: String, val word: String, val pos: String)

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
    /** ユーザー辞書を [words] で入れ替える。入れ替えて変換に使えるようになったら true。 */
    fun setUserDictionary(words: List<MozcWord>): Boolean
}
