// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.ArrowAction
import io.github.ninjin0802.keyboard.app.input.arrowAction
import kotlin.test.Test
import kotlin.test.assertEquals

/** ◀ ▶ が何をするか（spec 04「操作キー」の表）。 */
class ArrowActionTest {
    @Test fun with_nothing_composing_the_cursor_moves() {
        assertEquals(ArrowAction.MOVE_CURSOR, arrowAction(composing = false, direct = false))
        assertEquals(ArrowAction.MOVE_CURSOR, arrowAction(composing = false, direct = true))
    }
    /** 英字フリック配列と、変換しない欄。持っている1文字を確定するだけで、カーソルは動かさない（l ▶ l で ll）。 */
    @Test fun a_held_character_is_only_committed() =
        assertEquals(ArrowAction.COMMIT_ONLY, arrowAction(composing = true, direct = true))
    /** 読みがあるとき。カーソルは動かさず、セッションに任せる（▶ はトグル入力の文字を決める）。 */
    @Test fun a_reading_is_left_to_the_session() =
        assertEquals(ArrowAction.SESSION, arrowAction(composing = true, direct = false))
}
