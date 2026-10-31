// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

object Kana {
    private val next: Map<Char, Char> = buildMap {
        // 2要素の組は行き来する（か→が→か）
        for (w in listOf("かが", "きぎ", "くぐ", "けげ", "こご", "さざ", "しじ", "すず", "せぜ", "そぞ", "ただ", "ちぢ", "てで", "とど",
                "あぁ", "いぃ", "えぇ", "おぉ", "やゃ", "ゆゅ", "よょ", "わゎ")) {
            put(w[0], w[1]); put(w[1], w[0])
        }
        // 3要素以上は順に回る（つ→っ→づ→つ、は→ば→ぱ→は、う→ぅ→ゔ→う）
        for (w in listOf("つっづ", "はばぱ", "ひびぴ", "ふぶぷ", "へべぺ", "ほぼぽ", "うぅゔ")) {
            for (i in w.indices) put(w[i], w[(i + 1) % w.length])
        }
    }

    fun modify(c: Char): Char? = next[c]

    fun toKatakana(s: String): String = buildString {
        for (c in s) append(if (c in 'ぁ'..'ゖ') c + 0x60 else c)
    }
}

/**
 * タップとフリックで文字が決まるキー。[chars] は「タップ、左、上、右、下」の順。
 * 文字のない向きには [NONE] を置く（末尾の文字のない向きは省いてよい）。
 */
data class FlickKey(val chars: String) {
    /** 文字のある向きだけを、向きの順に並べたもの。トグル入力はこの順に回る。 */
    val cycle: String = chars.filter { it != NONE }

    /** [index] 番目の向き（0 がタップ）の文字。その向きに文字がなければ null。 */
    fun charAt(index: Int): Char? = chars.getOrNull(index)?.takeIf { it != NONE }

    fun next(c: Char): Char {
        val i = cycle.indexOf(c)
        return if (i < 0) cycle[0] else cycle[(i + 1) % cycle.length]
    }

    /** [next] の逆。最初の文字からは最後の文字に回る。このキーの文字でなければ最初の文字。 */
    fun previous(c: Char): Char {
        val i = cycle.indexOf(c)
        return if (i < 0) cycle[0] else cycle[(i + cycle.length - 1) % cycle.length]
    }

    companion object {
        /** 文字のない向き。 */
        const val NONE = '\u0000'
    }
}

/** かなのキー。5つの向きすべてに文字がある [FlickKey]。 */
typealias KanaKey = FlickKey

object KanaKeys {
    val A = KanaKey("あいうえお")
    val KA = KanaKey("かきくけこ")
    val SA = KanaKey("さしすせそ")
    val TA = KanaKey("たちつてと")
    val NA = KanaKey("なにぬねの")
    val HA = KanaKey("はひふへほ")
    val MA = KanaKey("まみむめも")
    val YA = KanaKey("や（ゆ）よ")
    val RA = KanaKey("らりるれろ")
    val WA = KanaKey("わをんー〜")
    val PUNCT = KanaKey("、。？！…")
    val all: List<KanaKey> = listOf(A, KA, SA, TA, NA, HA, MA, YA, RA, WA, PUNCT)
}
