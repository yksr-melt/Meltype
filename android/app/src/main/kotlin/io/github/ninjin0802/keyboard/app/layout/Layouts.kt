// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.layout

object Layouts {
    private class Spec(val action: KeyAction, val label: String, val name: String, val width: Float)

    /** 文字を書くキー。[name] を省くと、書いてある文字が名前になる。 */
    private fun key(action: KeyAction, label: String, name: String = label, width: Float = 1f) = Spec(action, label, name, width)

    /** 絵で出すキー。書く文字はない。 */
    private fun picture(action: KeyAction, name: String, width: Float = 1f) = Spec(action, "", name, width)

    private fun texts(chars: String) = chars.map { key(KeyAction.Text(it.toString()), it.toString()) }

    /** 行ごとのキーを左から並べて Key にする。Pair の first は、その行の左端の列。 */
    private fun build(rows: List<Pair<Float, List<Spec>>>): List<Key> =
        rows.flatMapIndexed { r, (start, row) ->
            var col = start
            row.map { s -> Key(s.action, s.label, s.name, col, r, s.width).also { col += s.width } }
        }

    private fun delete(width: Float = 1f) = picture(KeyAction.Delete, "削除", width)
    private fun space(width: Float = 1f) = picture(KeyAction.Space, "空白", width)
    private fun enter(width: Float = 1f) = picture(KeyAction.Enter, "確定", width)
    private fun emoji(width: Float = 1f) = picture(KeyAction.Emoji, "絵文字", width)
    private fun symbols(width: Float) = key(KeyAction.Switch(LayoutId.SYMBOLS), "?123", "記号", width)
    private fun language(width: Float = 1f) = key(KeyAction.Language, "あa", "かな・英字切り替え", width)
    private fun comma() = key(KeyAction.Text(","), ",")
    private fun period() = key(KeyAction.Text("."), ".")
    private fun punct(label: String, width: Float = 1f) = key(KeyAction.Text(label), label, width = width)
    private fun moreSymbols() = key(KeyAction.Switch(LayoutId.SYMBOLS2), "#+=", "記号2ページ", 1.5f)
    private fun firstSymbols() = key(KeyAction.Switch(LayoutId.SYMBOLS), "123", "記号1ページ", 1.5f)

    val qwerty: KeyboardLayout by lazy {
        KeyboardLayout(LayoutId.QWERTY, 10f, 4, build(listOf(
            0f to texts("qwertyuiop"),
            0.5f to texts("asdfghjkl"),
            0f to listOf(picture(KeyAction.Shift, "シフト", 1.5f)) + texts("zxcvbnm") + delete(1.5f),
            0f to listOf(symbols(1.65f), comma(), space(4.45f), period(), enter(1.9f)),
        )))
    }

    /** QWERTY でローマ字を打つ配列。英字配列と同じ大きさのキーで、2行目の右の端に長音のキーがある。 */
    val romaji: KeyboardLayout by lazy {
        fun letters(chars: String) = chars.map { key(KeyAction.Romaji(it), it.toString()) }
        fun mark(char: Char, label: String) = key(KeyAction.Romaji(char), label)
        KeyboardLayout(LayoutId.ROMAJI, 10f, 4, build(listOf(
            0f to letters("qwertyuiop"),
            0f to letters("asdfghjkl") + mark('-', "ー"),
            0f to listOf(picture(KeyAction.Shift, "シフト", 1.5f)) + letters("zxcvbnm") + delete(1.5f),
            0f to listOf(symbols(1.65f), punct("、"), space(4.45f), punct("。"), enter(1.9f)),
        )))
    }

    val symbols: KeyboardLayout by lazy {
        KeyboardLayout(LayoutId.SYMBOLS, 10f, 4, build(listOf(
            0f to texts("1234567890"),
            0f to texts("-/:@()「」¥&"),
            0f to listOf(moreSymbols(), punct("。", 1.3f), punct("、", 1.3f),
                punct("?", 1.3f), punct("!", 1.3f), punct("^^", 1.5f), delete(1.8f)),
            0f to listOf(key(KeyAction.Back, "あいう", "前の配列に戻る", 2.2f),
                space(5.5f), enter(2.3f)),
        )))
    }

    val symbols2: KeyboardLayout by lazy {
        KeyboardLayout(LayoutId.SYMBOLS2, 10f, 4, build(listOf(
            0f to texts("[]{}#%^*+="),
            0f to texts("_\\;|<>\"'$€"),
            0f to listOf(firstSymbols(), punct(".", 1.3f), punct(",", 1.3f),
                punct("?", 1.3f), punct("!", 1.3f), punct("•", 1.5f), delete(1.8f)),
            0f to listOf(key(KeyAction.Back, "あいう", "前の配列に戻る", 2.2f),
                space(5.5f), enter(2.3f)),
        )))
    }

    /** 数字・記号の配列の戻るキーに書く文字。行き先（日本語の配列 または 英語の配列）を示す。 */
    fun backLabel(target: LayoutId): String = if (target.isEnglish) "abc" else "あa"

    /**
     * 絵文字の画面の、いちばん下の操作キーの行。[origin] は絵文字の画面を開く前の配列で、左端のキーでそこへ戻る。
     * 空白・削除・確定は、[origin] の配列にあるときと同じ動作をする。
     */
    fun emojiBar(origin: LayoutId): KeyboardLayout = emojiBars.getValue(origin)

    private val emojiBars: Map<LayoutId, KeyboardLayout> by lazy {
        LayoutId.entries.associateWith { origin ->
            val label = if (origin == LayoutId.SYMBOLS || origin == LayoutId.SYMBOLS2) "?123" else backLabel(origin)
            KeyboardLayout(origin, 10f, 1, build(listOf(
                0f to listOf(key(KeyAction.Switch(origin), label, "キーボードに戻る", 1.5f), space(5.5f), delete(1.5f), enter(1.5f)),
            )))
        }
    }

    fun of(id: LayoutId): KeyboardLayout = when (id) {
        LayoutId.ROMAJI -> romaji
        LayoutId.QWERTY -> qwerty
        LayoutId.SYMBOLS -> symbols
        LayoutId.SYMBOLS2 -> symbols2
    }
}
