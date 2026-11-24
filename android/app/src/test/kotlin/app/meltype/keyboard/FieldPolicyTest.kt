// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package app.meltype.keyboard

import android.text.InputType
import android.view.inputmethod.EditorInfo
import app.meltype.keyboard.input.FieldPolicy
import app.meltype.keyboard.input.enterAction
import app.meltype.keyboard.input.EnterKind
import app.meltype.keyboard.input.enterKind
import app.meltype.keyboard.input.fieldPolicy
import kotlin.test.Test
import kotlin.test.assertEquals

class FieldPolicyTest {
    private fun text(variation: Int) = InputType.TYPE_CLASS_TEXT or variation
    private fun traits(inputType: Int, imeOptions: Int = 0) = fieldPolicy(inputType, imeOptions).traits

    @Test fun normal_text_has_no_traits() {
        assertEquals(FieldPolicy(emptyList(), symbols = false), fieldPolicy(text(InputType.TYPE_TEXT_VARIATION_NORMAL), 0))
        assertEquals(FieldPolicy(emptyList(), symbols = false), fieldPolicy(0, 0))
        // URL 欄は検索にも使うので、ふつうの欄と同じ。
        assertEquals(emptyList(), traits(text(InputType.TYPE_TEXT_VARIATION_URI) or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS))
    }
    @Test fun passwords() {
        for (v in listOf(InputType.TYPE_TEXT_VARIATION_PASSWORD, InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD, InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD))
            assertEquals(listOf("Password"), traits(text(v)))
        assertEquals(listOf("Password", "Numeric"), traits(InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_VARIATION_PASSWORD))
    }
    @Test fun email() {
        for (v in listOf(InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS, InputType.TYPE_TEXT_VARIATION_WEB_EMAIL_ADDRESS))
            assertEquals(FieldPolicy(listOf("Email"), symbols = false), fieldPolicy(text(v), 0))
    }
    @Test fun numbers_start_with_symbols() {
        for (c in listOf(InputType.TYPE_CLASS_NUMBER, InputType.TYPE_CLASS_PHONE, InputType.TYPE_CLASS_DATETIME,
            InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_FLAG_DECIMAL or InputType.TYPE_NUMBER_FLAG_SIGNED))
            assertEquals(FieldPolicy(listOf("Numeric"), symbols = true), fieldPolicy(c, 0))
    }
    @Test fun phone_and_datetime_variations_are_not_mistaken_for_passwords() {
        // 種類ごとに同じ数値が別の意味になる（数字のパスワード 0x10 は、日付と時刻では「日付」）。
        assertEquals(listOf("Numeric"), traits(InputType.TYPE_CLASS_DATETIME or InputType.TYPE_DATETIME_VARIATION_DATE))
        assertEquals(listOf("Numeric"), traits(InputType.TYPE_CLASS_PHONE or 0x10))
    }
    @Test fun no_learning_flag() {
        val privateFlag = EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING
        for (variation in listOf(InputType.TYPE_TEXT_VARIATION_NORMAL, InputType.TYPE_TEXT_VARIATION_WEB_EDIT_TEXT, InputType.TYPE_TEXT_VARIATION_URI))
            assertEquals(listOf("Private"), traits(text(variation), privateFlag))
        assertEquals(listOf("Password", "Private"), traits(text(InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD), privateFlag))
    }

    @Test fun enter_action_follows_the_field() {
        assertEquals(EditorInfo.IME_ACTION_SEARCH, enterAction(EditorInfo.IME_ACTION_SEARCH or EditorInfo.IME_FLAG_NO_EXTRACT_UI))
        assertEquals(null, enterAction(EditorInfo.IME_ACTION_SEND or EditorInfo.IME_FLAG_NO_ENTER_ACTION))
        assertEquals(null, enterAction(EditorInfo.IME_ACTION_UNSPECIFIED))
        assertEquals(null, enterAction(EditorInfo.IME_ACTION_NONE))
    }
    /** 絵文字の「最近使ったもの」（spec 04・09）: 覚えないのはパスワードの欄と覚えないように求める欄。見せないのはパスワードの欄だけ。 */
    @Test fun emoji_picks_are_not_recorded_in_password_or_private_fields_and_recents_are_hidden_only_in_password_fields() {
        val text = InputType.TYPE_CLASS_TEXT
        val private = EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING
        // 入力欄, 覚えるか, 見せるか
        val rows = listOf(
            Triple(fieldPolicy(text, 0), true, true),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_FLAG_MULTI_LINE, 0), true, true),
            Triple(fieldPolicy(text, private), false, true),                                             // シークレットモードなど
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS, 0), true, true),
            Triple(fieldPolicy(InputType.TYPE_CLASS_NUMBER, 0), true, true),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_PASSWORD, 0), false, false),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD, private), false, false),
            Triple(fieldPolicy(InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_VARIATION_PASSWORD, 0), false, false),
        )
        for ((policy, records, shows) in rows) {
            assertEquals(records, policy.recordsEmoji, "$policy"); assertEquals(shows, policy.showsRecentEmoji, "$policy")
        }
    }
    @Test fun enter_kind_follows_the_action() {
        assertEquals(
            listOf(EnterKind.GO, EnterKind.SEARCH, EnterKind.SEND, EnterKind.NEXT, EnterKind.DONE, EnterKind.NEWLINE, EnterKind.NEWLINE, EnterKind.OTHER),
            listOf(
                EditorInfo.IME_ACTION_GO, EditorInfo.IME_ACTION_SEARCH, EditorInfo.IME_ACTION_SEND, EditorInfo.IME_ACTION_NEXT,
                EditorInfo.IME_ACTION_DONE, EditorInfo.IME_ACTION_NONE,
                EditorInfo.IME_ACTION_SEARCH or EditorInfo.IME_FLAG_NO_ENTER_ACTION, EditorInfo.IME_ACTION_PREVIOUS,
            ).map(::enterKind),
        )
    }
}
