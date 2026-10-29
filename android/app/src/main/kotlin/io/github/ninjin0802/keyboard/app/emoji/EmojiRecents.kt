// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.emoji

import android.content.Context

/** 「最近使ったもの」の並びの決め方（spec 04）。 */
object RecentEmoji {
    const val MAX = 40

    /**
     * [emoji] を選んだあとの並び。新しい順で、同じものは1つ、[MAX] 個まで。
     * 学習しない欄（[learn] が false）では、何も変えない。
     */
    fun used(list: List<String>, emoji: String, learn: Boolean): List<String> {
        if (!learn) return list
        val next = ArrayList<String>(minOf(list.size + 1, MAX))
        next.add(emoji)
        for (e in list) {
            if (next.size == MAX) break
            if (e != emoji) next.add(e)
        }
        return next
    }

    /** 保存する形。絵文字は空白を含まないので、空白で区切る。 */
    fun encode(list: List<String>): String = list.joinToString(" ")

    fun decode(stored: String?): List<String> =
        stored.orEmpty().split(' ').filter { it.isNotEmpty() }.distinct().take(MAX)
}

/** 名前で引く、文字の保存先。 */
interface KeyValueStore {
    fun read(key: String): String?
    fun write(key: String, value: String)
}

/** 「最近使ったもの」を覚えておく。[store] に残すので、キーボードが止められても、開き直すと同じ並びが出る。 */
class EmojiRecentsStore(private val store: KeyValueStore) {
    var list: List<String> = RecentEmoji.decode(store.read(KEY_RECENT))
        private set

    /** [emoji] を選んだ。[record] は、いまの入力欄で覚えてよいか（FieldPolicy.recordsEmoji）。覚えないときは、何も書き込まない。 */
    fun used(emoji: String, record: Boolean) {
        val next = RecentEmoji.used(list, emoji, record)
        if (next === list) return
        list = next
        store.write(KEY_RECENT, RecentEmoji.encode(next))
    }

    private companion object {
        const val KEY_RECENT = "recent"
    }
}

/** アプリ専用の領域にある保存先（SharedPreferences）。バックアップにも、端末の引き継ぎにも入らない（spec 09）。 */
class PrefsStore(context: Context, name: String) : KeyValueStore {
    private val prefs = context.getSharedPreferences(name, Context.MODE_PRIVATE)

    override fun read(key: String): String? = prefs.getString(key, null)
    override fun write(key: String, value: String) { prefs.edit().putString(key, value).apply() }
}
