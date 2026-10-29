// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.emoji.EmojiRecentsStore
import io.github.ninjin0802.keyboard.app.emoji.KeyValueStore
import kotlin.test.Test
import kotlin.test.assertEquals

/** 「最近使ったもの」の保存と読み直し。保存先は、メモリの上の代わり。 */
class EmojiRecentsStoreTest {
    private class MemoryStore : KeyValueStore {
        val values = HashMap<String, String>()
        var writes = 0
        override fun read(key: String): String? = values[key]
        override fun write(key: String, value: String) { values[key] = value; writes++ }
    }
    private val memory = MemoryStore()

    @Test fun starts_empty_when_nothing_was_saved() {
        assertEquals(emptyList(), EmojiRecentsStore(memory).list); assertEquals(0, memory.writes)
    }

    @Test fun recorded_picks_survive_reopening() {
        val store = EmojiRecentsStore(memory)
        store.used("😀", record = true); store.used("🇯🇵", record = true); store.used("👨‍👩‍👧", record = true); store.used("😀", record = true)
        assertEquals(listOf("😀", "👨‍👩‍👧", "🇯🇵"), store.list)
        assertEquals(4, memory.writes)
        // 開き直す（キーボードが止められたあと）。
        val reopened = EmojiRecentsStore(memory)
        assertEquals(listOf("😀", "👨‍👩‍👧", "🇯🇵"), reopened.list)
        reopened.used("🐶", record = true)
        assertEquals(listOf("🐶", "😀", "👨‍👩‍👧", "🇯🇵"), EmojiRecentsStore(memory).list)
    }

    /** 覚えない欄（パスワード、シークレットモードなど）で選んだ絵文字は、並びにも保存先にも残さない。 */
    @Test fun a_pick_that_must_not_be_recorded_changes_nothing_and_writes_nothing() {
        val store = EmojiRecentsStore(memory)
        store.used("😀", record = true)
        val saved = HashMap(memory.values)
        store.used("🍎", record = false); store.used("😀", record = false)
        assertEquals(listOf("😀"), store.list); assertEquals(saved, memory.values); assertEquals(1, memory.writes)
        assertEquals(listOf("😀"), EmojiRecentsStore(memory).list)
        // 何も覚えていないところから始めても同じ。
        val empty = MemoryStore()
        EmojiRecentsStore(empty).used("🍎", record = false)
        assertEquals(0, empty.writes); assertEquals(emptyList(), EmojiRecentsStore(empty).list)
    }

    @Test fun keeps_at_most_forty_across_reopening() {
        val many = (0 until 45).map { String(Character.toChars(0x1F600 + it)) }
        val store = EmojiRecentsStore(memory)
        for (e in many) store.used(e, record = true)
        val reopened = EmojiRecentsStore(memory).list
        assertEquals(40, reopened.size); assertEquals(many.last(), reopened.first()); assertEquals(many[5], reopened.last())
    }
}
