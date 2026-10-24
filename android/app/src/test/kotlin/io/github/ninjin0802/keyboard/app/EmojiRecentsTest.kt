// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.emoji.RecentEmoji
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertSame

class EmojiRecentsTest {
    private fun use(vararg emoji: String, learn: Boolean = true) =
        emoji.fold(emptyList<String>()) { list, e -> RecentEmoji.used(list, e, learn) }

    @Test fun newest_first() = assertEquals(listOf("🍎", "🐶", "😀"), use("😀", "🐶", "🍎"))
    @Test fun a_repeated_emoji_moves_to_the_front_and_is_kept_once() {
        assertEquals(listOf("😀", "🍎", "🐶"), use("😀", "🐶", "🍎", "😀"))
        assertEquals(listOf("😀"), use("😀", "😀"))
    }
    @Test fun capped_at_forty_dropping_the_oldest() {
        val many = (0 until 45).map { String(Character.toChars(0x1F600 + it)) }
        val list = use(*many.toTypedArray())
        assertEquals(40, RecentEmoji.MAX); assertEquals(40, list.size)
        assertEquals(many.last(), list.first()); assertEquals(many[5], list.last())
    }
    @Test fun not_updated_when_the_field_does_not_learn() {
        val before = use("😀", "🐶")
        assertSame(before, RecentEmoji.used(before, "🍎", learn = false))
        assertSame(before, RecentEmoji.used(before, "😀", learn = false))
        assertEquals(emptyList(), use("😀", learn = false))
    }
    @Test fun stored_form_round_trips() {
        val list = use("😀", "👨‍👩‍👧", "🇯🇵", "☺️")
        assertEquals(list, RecentEmoji.decode(RecentEmoji.encode(list)))
        assertEquals(emptyList(), RecentEmoji.decode(RecentEmoji.encode(emptyList())))
    }
    @Test fun unreadable_or_oversized_stored_form() {
        assertEquals(emptyList(), RecentEmoji.decode(null)); assertEquals(emptyList(), RecentEmoji.decode(""))
        val many = (0 until 50).map { String(Character.toChars(0x1F600 + it)) }
        assertEquals(many.take(40), RecentEmoji.decode(RecentEmoji.encode(many)))
        assertEquals(listOf("😀", "🐶"), RecentEmoji.decode(RecentEmoji.encode(listOf("😀", "🐶", "😀"))))
    }
}
