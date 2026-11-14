// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.emoji.EmojiCategory
import io.github.ninjin0802.keyboard.app.emoji.EmojiTabs
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse

/** 絵文字の画面のタブ（spec 04「種類」「最近使ったもの」）。 */
class EmojiTabsTest {
    @Test fun every_category_is_offered_where_recents_may_be_shown() =
        assertEquals(EmojiCategory.entries.toList(), EmojiTabs.offered(showRecents = true))

    @Test fun a_password_field_is_offered_no_recents_tab() {
        val offered = EmojiTabs.offered(showRecents = false)
        assertFalse(EmojiCategory.RECENT in offered)
        assertEquals(EmojiCategory.entries.filter { it != EmojiCategory.RECENT }, offered)
        assertEquals(9, offered.size); assertEquals(EmojiCategory.SMILEYS, offered.first())
    }

    @Test fun the_panel_opens_on_recents_when_there_are_some_and_on_the_first_category_otherwise() {
        assertEquals(EmojiCategory.RECENT, EmojiTabs.initial(showRecents = true, hasRecents = true))
        assertEquals(EmojiCategory.SMILEYS, EmojiTabs.initial(showRecents = true, hasRecents = false))
    }

    @Test fun in_a_password_field_the_panel_opens_on_the_first_category_even_when_there_are_recents() {
        assertEquals(EmojiCategory.SMILEYS, EmojiTabs.initial(showRecents = false, hasRecents = true))
        assertEquals(EmojiCategory.SMILEYS, EmojiTabs.initial(showRecents = false, hasRecents = false))
        for (show in listOf(true, false)) for (has in listOf(true, false)) {
            assertEquals(true, EmojiTabs.initial(show, has) in EmojiTabs.offered(show))
        }
    }

    /** 一覧を調べ終わる前や、一覧が読めなかったときに、ほかの種類の下に「まだありません」と書かない。 */
    @Test fun only_the_recents_tab_says_that_it_is_empty() {
        assertEquals("最近使った絵文字は、まだありません", EmojiTabs.emptyNote(EmojiCategory.RECENT))
        for (c in EmojiCategory.entries) if (c != EmojiCategory.RECENT) assertEquals("", EmojiTabs.emptyNote(c), c.name)
    }
}
