// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.emoji.EmojiCategory
import io.github.ninjin0802.keyboard.app.emoji.EmojiData
import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class EmojiDataTest {
    /** 一覧を作る途中の思いがけない失敗で、キーボードごと落ちない。空の一覧で続ける。 */
    @Test fun any_exception_while_reading_gives_an_empty_catalog() {
        val ok = mapOf(EmojiCategory.FOOD to arrayOf("🍎"))
        var failures = 0
        assertEquals(ok, EmojiData.readOrEmpty({ ok }) { failures++ }); assertEquals(0, failures)
        val thrown = listOf(java.io.IOException("x"), IllegalStateException("x"), NullPointerException(), IndexOutOfBoundsException(), SecurityException())
        for (e in thrown) assertEquals(emptyMap(), EmojiData.readOrEmpty({ throw e }) { failures++ }, e.javaClass.simpleName)
        assertEquals(thrown.size, failures)
    }

    private val family = "👨‍👩‍👧"
    private val japan = "🇯🇵"
    private val wavingDark = "👋🏿"
    private val sample = """
        # 説明の行
        @Smileys & Emotion
        😀 😃
        ☺️
        @People & Body
        👋 👋🏻 $wavingDark
        $family 🧑🏽‍🚀 🧑‍🚀
        @Component
        🦰
        @Travel & Places
        🚗
        @Activities
        ⚽
        @Flags
        $japan
    """.trimIndent()

    @Test fun categories_are_in_the_order_of_the_tabs_with_their_names() {
        assertEquals(
            listOf("最近使ったもの", "顔と気持ち", "人", "動物と自然", "食べ物", "活動", "旅行と場所", "物", "記号", "旗"),
            EmojiCategory.entries.map { it.title },
        )
    }
    @Test fun unicode_groups_map_onto_the_nine_categories() {
        val expected = mapOf(
            "Smileys & Emotion" to EmojiCategory.SMILEYS, "People & Body" to EmojiCategory.PEOPLE,
            "Animals & Nature" to EmojiCategory.ANIMALS, "Food & Drink" to EmojiCategory.FOOD,
            "Activities" to EmojiCategory.ACTIVITIES, "Travel & Places" to EmojiCategory.TRAVEL,
            "Objects" to EmojiCategory.OBJECTS, "Symbols" to EmojiCategory.SYMBOLS, "Flags" to EmojiCategory.FLAGS,
        )
        for ((group, category) in expected) assertEquals(category, EmojiCategory.ofUnicodeGroup(group), group)
        assertEquals(EmojiCategory.entries.toSet() - EmojiCategory.RECENT, expected.values.toSet())
        assertNull(EmojiCategory.ofUnicodeGroup("Component"))
        assertNull(EmojiCategory.ofUnicodeGroup("Something New"))
    }
    @Test fun every_category_but_recent_has_a_tab_emoji() {
        assertNull(EmojiCategory.RECENT.tabEmoji)
        assertTrue((EmojiCategory.entries - EmojiCategory.RECENT).all { !it.tabEmoji.isNullOrEmpty() })
    }
    @Test fun parses_groups_in_file_order_and_skips_comments() {
        val parsed = EmojiData.parse(sample.lineSequence())
        assertEquals(listOf("😀", "😃", "☺️"), parsed[EmojiCategory.SMILEYS])
        assertEquals(listOf("🚗"), parsed[EmojiCategory.TRAVEL])
        assertEquals(listOf("⚽"), parsed[EmojiCategory.ACTIVITIES])
        assertEquals(listOf(japan), parsed[EmojiCategory.FLAGS])
    }
    @Test fun skin_tone_variants_are_dropped_and_other_sequences_kept() {
        assertEquals(listOf("👋", family, "🧑‍🚀"), EmojiData.parse(sample.lineSequence())[EmojiCategory.PEOPLE])
    }
    @Test fun component_group_and_unknown_groups_are_skipped() {
        val parsed = EmojiData.parse((sample + "\n@Something New\n🆕").lineSequence())
        assertFalse(parsed.values.flatten().any { it == "🦰" || it == "🆕" })
        assertFalse(EmojiCategory.RECENT in parsed)
    }
    @Test fun emoji_before_any_group_line_are_ignored() {
        assertTrue(EmojiData.parse(sequenceOf("😀 😃")).values.all { it.isEmpty() })
    }
    @Test fun has_skin_tone() {
        assertTrue(EmojiData.hasSkinTone(wavingDark)); assertTrue(EmojiData.hasSkinTone("🧑🏽‍🚀"))
        assertFalse(EmojiData.hasSkinTone("👋")); assertFalse(EmojiData.hasSkinTone(family)); assertFalse(EmojiData.hasSkinTone(japan))
    }

    /** アプリに入れてある一覧そのもの。 */
    @Test fun bundled_asset() {
        val file = File("src/main/assets/emoji.txt")
        assertTrue(file.readLines().first().startsWith("# Unicode Emoji "))
        val parsed = file.useLines { EmojiData.parse(it) }
        for (c in EmojiCategory.entries - EmojiCategory.RECENT) assertTrue(parsed.getValue(c).size >= 80, "$c ${parsed[c]?.size}")
        val all = parsed.values.flatten()
        assertTrue(all.size in 1800..2600, "${all.size}")
        assertEquals(all.size, all.toSet().size)
        assertFalse(all.any(EmojiData::hasSkinTone))
        assertTrue(all.none { e -> e.isEmpty() || e.any { it == ' ' || it == '\r' } })
        assertEquals("😀", parsed.getValue(EmojiCategory.SMILEYS).first())
        assertTrue(family in parsed.getValue(EmojiCategory.PEOPLE)); assertTrue(japan in parsed.getValue(EmojiCategory.FLAGS))
        // タブの絵に使う絵文字は、その種類の中にある。
        for (c in EmojiCategory.entries - EmojiCategory.RECENT) assertTrue(c.tabEmoji in parsed.getValue(c), "$c")
    }
}
