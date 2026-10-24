// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.layout

import io.github.ninjin0802.keyboard.engine.FlickKey

/**
 * 配列（spec 04「配列の種類」）。[KANA] と [ROMAJI] が日本語を打つ配列、[QWERTY] と [LATIN_FLICK] が英字を打つ配列。
 * それぞれ、設定で選んである方だけが出る。
 */
enum class LayoutId {
    KANA, ROMAJI, QWERTY, LATIN_FLICK, SYMBOLS;

    val isJapanese: Boolean get() = this == KANA || this == ROMAJI
    val isEnglish: Boolean get() = this == QWERTY || this == LATIN_FLICK
}

sealed interface KeyAction {
    /** 押し続けると繰り返すキーか。 */
    val repeats: Boolean get() = false

    /** タップとフリックで文字が決まる（かな配列、英字フリック配列）。 */
    data class Flick(val key: FlickKey) : KeyAction
    /** ローマ字配列の文字キー。[char] は英字の小文字か、- , . のどれか。 */
    data class Romaji(val char: Char) : KeyAction
    /** そのまま入力（英字・数字・記号）。 */
    data class Text(val text: String) : KeyAction
    data object Modify : KeyAction
    /** 直前の英字の、大文字と小文字を入れ替える（a⇔A）。 */
    data object ToggleCase : KeyAction
    data object Delete : KeyAction { override val repeats get() = true }
    data object Space : KeyAction
    data object Enter : KeyAction
    data object Left : KeyAction { override val repeats get() = true }
    data object Right : KeyAction { override val repeats get() = true }
    data object Shift : KeyAction
    /** 日本語の配列と英語の配列を入れ替える（あa）。 */
    data object Language : KeyAction
    /** 数字・記号の配列へ。絵文字の画面では、開く前の配列 [to] へ戻る。 */
    data class Switch(val to: LayoutId) : KeyAction
    /** 数字・記号から直前の配列へ。 */
    data object Back : KeyAction
    /** 切り替え中の文字を1つ前に戻す、または候補の選択を解く。 */
    data object Undo : KeyAction
    /** 絵文字の画面へ。 */
    data object Emoji : KeyAction
}

/**
 * [label]: キーに書く文字。絵で出すキーでは空（絵は view が [action] から決める）。
 * [name]: 読み上げに使う、キーごとに決まった日本語の名前。打った文字では変わらない。
 */
data class Key(
    val action: KeyAction,
    val label: String,
    val name: String,
    val col: Float,
    val row: Int,
    val width: Float = 1f,
    val rowSpan: Int = 1,
)

data class KeyboardLayout(val id: LayoutId, val columns: Float, val rows: Int, val keys: List<Key>) {
    /** x は 0..columns、y は 0..rows の座標。キーの範囲は左・上を含み、右・下を含まない。 */
    fun keyAt(x: Float, y: Float): Key? = keys.firstOrNull {
        x >= it.col && x < it.col + it.width && y >= it.row && y < it.row + it.rowSpan
    }
}

/**
 * keyAt が null でも、配列の内側なら同じ行でいちばん近いキーを返す
 * （QWERTY 2 段目の左右の半キー分の余白は「a」「l」の延長になる）。範囲の外は null。
 */
fun KeyboardLayout.nearestKeyAt(x: Float, y: Float): Key? {
    keyAt(x, y)?.let { return it }
    if (x < 0f || x > columns || y < 0f || y > rows) return null
    // 割り当てなしで探す（指が動くたびに呼ばれる）。下の端ちょうどは、いちばん下の行として扱う。
    val row = minOf(y.toInt(), rows - 1)
    var best: Key? = null
    var bestDistance = Float.MAX_VALUE
    for (k in keys) {
        if (row < k.row || row >= k.row + k.rowSpan) continue
        val d = maxOf(k.col - x, x - (k.col + k.width), 0f)
        if (d < bestDistance) { best = k; bestDistance = d }
    }
    return best
}
