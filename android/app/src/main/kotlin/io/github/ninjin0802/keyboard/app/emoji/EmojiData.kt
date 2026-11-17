// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.emoji

/**
 * 絵文字の種類。並びはタブの順（spec 04「絵文字の画面」）。
 * [title]: 読み上げに使う名前。[unicodeGroup]: Unicode の一覧でのグループ名。[tabEmoji]: タブに出す絵文字。
 */
enum class EmojiCategory(val title: String, val unicodeGroup: String?, val tabEmoji: String?) {
    RECENT("最近使ったもの", null, null),
    SMILEYS("顔と気持ち", "Smileys & Emotion", "😀"),
    PEOPLE("人", "People & Body", "🧑"),
    ANIMALS("動物と自然", "Animals & Nature", "🐶"),
    FOOD("食べ物", "Food & Drink", "🍎"),
    ACTIVITIES("活動", "Activities", "⚽"),
    TRAVEL("旅行と場所", "Travel & Places", "🚗"),
    OBJECTS("物", "Objects", "💡"),
    SYMBOLS("記号", "Symbols", "🔣"),
    FLAGS("旗", "Flags", "🏁");

    companion object {
        /** Unicode のグループが入る種類。使わないグループ（Component）や知らないグループは null。 */
        fun ofUnicodeGroup(name: String): EmojiCategory? = entries.firstOrNull { it.unicodeGroup == name }
    }
}

/** アプリに入れてある絵文字の一覧（assets/emoji.txt。tools/make-emoji-asset.py が作る）の読み取り。 */
object EmojiData {
    /**
     * 一覧を種類ごとに分ける。種類の中の並びは、一覧の順のまま。肌の色の違いは外す。
     * 形: 「#」で始まる行は説明、「@」で始まる行は Unicode のグループ名、そのほかは絵文字を空白で区切った行。
     */
    fun parse(lines: Sequence<String>): Map<EmojiCategory, List<String>> {
        val result = LinkedHashMap<EmojiCategory, MutableList<String>>()
        for (c in EmojiCategory.entries) if (c.unicodeGroup != null) result[c] = ArrayList()
        var current: MutableList<String>? = null
        for (line in lines) {
            when {
                line.isEmpty() || line.startsWith("#") -> Unit
                line.startsWith("@") -> current = EmojiCategory.ofUnicodeGroup(line.substring(1).trim())?.let(result::get)
                else -> current?.let { into -> line.split(' ').filterTo(into) { it.isNotEmpty() && !hasSkinTone(it) } }
            }
        }
        return result
    }

    /**
     * [read] で一覧を作る。どんな例外で失敗しても、[onFailure] を呼んで空の一覧を返す
     * （別のスレッドで作るので、ここで止めないとキーボードごと落ちる）。
     */
    inline fun readOrEmpty(
        read: () -> Map<EmojiCategory, Array<String>>, onFailure: () -> Unit,
    ): Map<EmojiCategory, Array<String>> = try {
        read()
    } catch (e: Exception) {
        onFailure()
        emptyMap()
    }

    /** 肌の色の符号（U+1F3FB〜U+1F3FF）を含むか。 */
    fun hasSkinTone(emoji: String): Boolean {
        // どれも同じ上位サロゲート（U+D83C）に、下位サロゲート U+DFFB〜U+DFFF が続く。
        for (i in 1 until emoji.length) {
            if (emoji[i - 1] == '\uD83C' && emoji[i] in '\uDFFB'..'\uDFFF') return true
        }
        return false
    }
}
