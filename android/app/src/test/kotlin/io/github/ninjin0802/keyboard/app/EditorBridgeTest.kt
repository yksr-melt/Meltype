// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.EditorBridge
import io.github.ninjin0802.keyboard.engine.Latin
import io.github.ninjin0802.keyboard.engine.Update
import android.view.inputmethod.InputConnection
import java.lang.reflect.Proxy
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class EditorBridgeTest {
    @Test fun deleteLength() {
        assertEquals(1, EditorBridge.deleteLength("ab"))
        assertEquals(2, EditorBridge.deleteLength("a😀"))
        assertEquals(1, EditorBridge.deleteLength("😀a"))
        assertEquals(1, EditorBridge.deleteLength("a"))
        assertEquals(1, EditorBridge.deleteLength("\uDE00"))
        assertEquals(0, EditorBridge.deleteLength(""))
        assertEquals(0, EditorBridge.deleteLength(null))
    }
    @Test fun delete_removes_a_combined_character_in_one_press() {
        assertEquals(2, EditorBridge.deleteLength("aか\u3099"))        // か + 結合する濁点
        assertEquals(2, EditorBridge.deleteLength("a☺️"))               // 字形を選ぶ符号つき
        assertEquals(3, EditorBridge.deleteLength("a😀\uFE0F"))
        // 囲み数字の絵文字（数字 + 字形を選ぶ符号 + 囲みの符号）。結合する符号なので、PC の Java でもまとまる。
        assertEquals(3, EditorBridge.deleteLength("a1️⃣")); assertEquals(3, EditorBridge.deleteLength("1️⃣#️⃣"))
        assertEquals(2, EditorBridge.deleteLength("a1\u20E3"))
        // 国旗（符号2つ）や、ZWJ でつないだ絵文字は、端末の ICU の規則でまとめられる。PC の Java 17 は
        // その規則を持たないので、ここでは確かめられない（端末の上のテスト DeleteLengthDeviceTest で確かめる）。
        assertEquals(16, EditorBridge.DELETE_READ_LENGTH)
    }


    @Test fun null_connection_is_ignored() {
        val b = EditorBridge { null }
        b.apply(Update(commit = "x", composing = "y"))
        b.deleteBeforeCursor()
    }

    @Test fun selection_at_composing_end_keeps_session() {
        val b = EditorBridge { null }
        b.apply(Update(composing = "かな"))
        assertFalse(b.onSelectionChanged(5, 5, 3, 5))
    }

    @Test fun moved_cursor_or_lost_composition_resets() {
        val b = EditorBridge { null }
        b.apply(Update(composing = "かな"))
        assertTrue(b.onSelectionChanged(1, 1, 3, 5))
        assertTrue(b.onSelectionChanged(5, 5, -1, -1))
        assertTrue(b.onSelectionChanged(3, 5, 3, 5))
    }

    @Test fun without_composition_never_resets() {
        val b = EditorBridge { null }
        b.apply(Update(commit = "x"))
        assertFalse(b.onSelectionChanged(0, 0, -1, -1))
    }

    @Test fun composition_that_ended_no_longer_resets() {
        val b = EditorBridge { null }
        b.apply(Update(composing = "かな"))
        b.apply(Update(commit = "仮名"))
        assertFalse(b.onSelectionChanged(2, 2, -1, -1))
    }

    @Test fun forget_drops_the_composing_state() {
        val b = EditorBridge { null }
        b.apply(Update(composing = "かな"))
        b.forget()
        assertFalse(b.onSelectionChanged(0, 0, -1, -1))
    }

    @Test fun late_callback_from_before_the_composition_is_ignored() {
        // 確定のすぐあとに次の文字を打つと、確定の知らせ（未確定の範囲なし）が、次の未確定文字を出したあとに届く。
        val b = EditorBridge { null }
        b.apply(Update(commit = "漢字"))
        b.apply(Update(composing = "か"))
        assertFalse(b.onSelectionChanged(2, 2, -1, -1))
        assertFalse(b.onSelectionChanged(3, 3, 2, 3))
    }

    @Test fun composition_lost_after_the_field_showed_it_resets() {
        val b = EditorBridge { null }
        b.apply(Update(composing = "か"))
        assertFalse(b.onSelectionChanged(1, 1, 0, 1))
        assertTrue(b.onSelectionChanged(1, 1, -1, -1))
    }

    @Test fun each_new_composition_waits_for_the_field_to_show_it() {
        val b = EditorBridge { null }
        b.apply(Update(composing = "か"))
        assertFalse(b.onSelectionChanged(1, 1, 0, 1))
        b.apply(Update(commit = "蚊"))
        b.apply(Update(composing = "き"))
        assertFalse(b.onSelectionChanged(1, 1, -1, -1))
    }

    @Test fun composition_continued_across_a_commit_stays_known() {
        // 先頭だけ確定して残りが続くとき（1回のまとまった操作）は、同じ未確定文字の続きとして扱う。
        val b = EditorBridge { null }
        b.apply(Update(composing = "かんじ"))
        assertFalse(b.onSelectionChanged(3, 3, 0, 3))
        b.apply(Update(commit = "漢", composing = "じ"))
        assertTrue(b.onSelectionChanged(2, 2, -1, -1))
    }

    @Test fun edits_for_an_update() {
        // (直前の未確定文字, 確定, 未確定) → 入力欄への操作
        assertEquals(EditorBridge.Edits(commit = null, composing = "か"), EditorBridge.edits("", Update(composing = "か")))
        assertEquals(EditorBridge.Edits(commit = "漢字", composing = null), EditorBridge.edits("かんじ", Update(commit = "漢字")))
        assertEquals(EditorBridge.Edits(commit = "漢", composing = "じ"), EditorBridge.edits("かんじ", Update(commit = "漢", composing = "じ")))
        assertEquals(EditorBridge.Edits(commit = "x", composing = null), EditorBridge.edits("", Update(commit = "x")))
    }

    @Test fun deleting_the_last_composing_character_removes_it_from_the_field() {
        // 未確定文字が消えたときは、空の文字で置き換える（finishComposingText だと入力欄に残ってしまう）。
        assertEquals(EditorBridge.Edits(commit = "", composing = null), EditorBridge.edits("か", Update()))
    }

    @Test fun unchanged_update_needs_no_edit() {
        assertEquals(null, EditorBridge.edits("", Update()))
        assertEquals(null, EditorBridge.edits("かな", Update(composing = "かな")))
    }

    // パスワードの欄の ゛゜小: 何も持っていないので、カーソルの前の1文字を読んで置き換える（spec 09）。
    @Test fun case_toggle_replacement_for_the_character_before_the_cursor() {
        assertEquals("A", EditorBridge.modified("a", Latin::toggleCase)); assertEquals("a", EditorBridge.modified("xyA", Latin::toggleCase))
        for (text in listOf("1", "@", "か", "😀", "", null)) assertEquals(null, EditorBridge.modified(text, Latin::toggleCase), "$text")
    }
    @Test fun modified_replacement_for_the_character_before_the_cursor() {
        assertEquals("が", EditorBridge.modified("か"))
        assertEquals("か", EditorBridge.modified("が"))
        assertEquals("っ", EditorBridge.modified("つ"))
        assertEquals("ぱ", EditorBridge.modified("ば"))
        assertEquals("が", EditorBridge.modified("abか"))       // 見るのは最後の1文字だけ
        assertEquals(null, EditorBridge.modified("ん"))          // 切り替えられない文字
        assertEquals(null, EditorBridge.modified("a"))
        assertEquals(null, EditorBridge.modified("😀"))
        assertEquals(null, EditorBridge.modified(""))
        assertEquals(null, EditorBridge.modified(null))          // 読めなかった
    }

    /** 呼ばれた操作を記録する入力欄。[before] がカーソルの前の文章（null なら読めない入力欄）。 */
    private class RecordingConnection(private val before: String?) {
        val calls = mutableListOf<String>()
        val connection: InputConnection = Proxy.newProxyInstance(
            InputConnection::class.java.classLoader, arrayOf(InputConnection::class.java),
        ) { _, method, args ->
            val a = args.orEmpty()
            when (method.name) {
                "beginBatchEdit", "endBatchEdit" -> true
                "getTextBeforeCursor" -> { calls += "read(${a[0]})"; before?.takeLast(a[0] as Int) }
                else -> { calls += method.name + a.joinToString(",", "(", ")"); if (method.returnType == Boolean::class.javaPrimitiveType) true else null }
            }
        } as InputConnection
    }

    @Test fun modify_before_cursor_replaces_exactly_one_character() {
        val field = RecordingConnection("あか")
        EditorBridge { field.connection }.modifyBeforeCursor()
        assertEquals(listOf("read(1)", "deleteSurroundingText(1,0)", "commitText(が,1)"), field.calls)
    }

    @Test fun modify_before_cursor_does_nothing_when_the_character_cannot_change_or_cannot_be_read() {
        for (before in listOf("かん", "abc", "", null)) {
            val field = RecordingConnection(before)
            EditorBridge { field.connection }.modifyBeforeCursor()
            assertEquals(listOf("read(1)"), field.calls, "before=$before")
        }
        EditorBridge { null }.modifyBeforeCursor()
    }

    @Test fun case_toggle_before_cursor_replaces_one_letter_and_leaves_everything_else_alone() {
        val field = RecordingConnection("xb")
        EditorBridge { field.connection }.modifyBeforeCursor(Latin::toggleCase)
        assertEquals(listOf("read(1)", "deleteSurroundingText(1,0)", "commitText(B,1)"), field.calls)
        for (before in listOf("a1", "か", "", null)) {
            val other = RecordingConnection(before)
            EditorBridge { other.connection }.modifyBeforeCursor(Latin::toggleCase)
            assertEquals(listOf("read(1)"), other.calls, "before=$before")
        }
    }
}
