// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import android.text.InputType
import android.view.inputmethod.EditorInfo
import io.github.ninjin0802.keyboard.app.input.FieldPolicy
import io.github.ninjin0802.keyboard.app.input.enterAction
import io.github.ninjin0802.keyboard.app.input.EnterKind
import io.github.ninjin0802.keyboard.app.input.enterKind
import io.github.ninjin0802.keyboard.app.input.enterLabel
import io.github.ninjin0802.keyboard.app.input.fieldPolicy
import io.github.ninjin0802.keyboard.app.layout.LayoutId
import kotlin.test.Test
import kotlin.test.assertEquals

class FieldPolicyTest {
    private fun text(variation: Int) = InputType.TYPE_CLASS_TEXT or variation
    @Test fun normal_text() = assertEquals(FieldPolicy(LayoutId.KANA, true, true), fieldPolicy(text(InputType.TYPE_TEXT_VARIATION_NORMAL), 0))
    @Test fun passwords_hold_nothing() { for (v in listOf(InputType.TYPE_TEXT_VARIATION_PASSWORD, InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD, InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD))
        assertEquals(FieldPolicy(LayoutId.QWERTY, convert = false, learn = false, holdLast = false), fieldPolicy(text(v), 0)) }
    @Test fun email_does_not_convert_and_holds_the_last_character() { for (v in listOf(InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS, InputType.TYPE_TEXT_VARIATION_WEB_EMAIL_ADDRESS))
        assertEquals(FieldPolicy(LayoutId.QWERTY, convert = false, learn = false, holdLast = true), fieldPolicy(text(v), 0)) }
    @Test fun url_starts_in_qwerty_converts_and_never_learns() {
        val expected = FieldPolicy(LayoutId.QWERTY, convert = true, learn = false)
        assertEquals(expected, fieldPolicy(text(InputType.TYPE_TEXT_VARIATION_URI), 0))
        assertEquals(expected, fieldPolicy(text(InputType.TYPE_TEXT_VARIATION_URI), EditorInfo.IME_ACTION_GO))
        assertEquals(expected, fieldPolicy(text(InputType.TYPE_TEXT_VARIATION_URI), EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING))
        // フラグが付いていても URL は URL（Chrome のアドレスバーは、候補を出さない指定を付けてくる）。
        assertEquals(expected, fieldPolicy(text(InputType.TYPE_TEXT_VARIATION_URI) or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS, 0))
    }
    @Test fun numbers() { for (c in listOf(InputType.TYPE_CLASS_NUMBER, InputType.TYPE_CLASS_PHONE, InputType.TYPE_CLASS_DATETIME,
            InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_FLAG_DECIMAL or InputType.TYPE_NUMBER_FLAG_SIGNED))
        assertEquals(FieldPolicy(LayoutId.SYMBOLS, convert = false, learn = false, holdLast = true), fieldPolicy(c, 0)) }
    @Test fun number_password_holds_nothing() = assertEquals(
        FieldPolicy(LayoutId.SYMBOLS, convert = false, learn = false, holdLast = false),
        fieldPolicy(InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_VARIATION_PASSWORD, 0),
    )
    @Test fun phone_and_datetime_variations_are_not_mistaken_for_passwords() {
        // 種類ごとに同じ数値が別の意味になる（数字のパスワード 0x10 は、日付と時刻では「日付」）。
        assertEquals(true, fieldPolicy(InputType.TYPE_CLASS_DATETIME or InputType.TYPE_DATETIME_VARIATION_DATE, 0).holdLast)
        assertEquals(true, fieldPolicy(InputType.TYPE_CLASS_PHONE or 0x10, 0).holdLast)
    }
    @Test fun fields_that_convert_always_hold() {
        assertEquals(true, fieldPolicy(text(InputType.TYPE_TEXT_VARIATION_NORMAL), 0).holdLast)
        assertEquals(true, fieldPolicy(text(InputType.TYPE_TEXT_VARIATION_URI), 0).holdLast)
    }
    @Test fun no_learning_flag() = assertEquals(FieldPolicy(LayoutId.KANA, true, false),
        fieldPolicy(text(0), EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING))
    @Test fun unknown_type_is_normal() = assertEquals(FieldPolicy(LayoutId.KANA, true, true), fieldPolicy(0, 0))

    @Test fun enter_action_follows_the_field() {
        assertEquals(EditorInfo.IME_ACTION_SEARCH, enterAction(EditorInfo.IME_ACTION_SEARCH or EditorInfo.IME_FLAG_NO_EXTRACT_UI))
        assertEquals(null, enterAction(EditorInfo.IME_ACTION_SEND or EditorInfo.IME_FLAG_NO_ENTER_ACTION))
        assertEquals(null, enterAction(EditorInfo.IME_ACTION_UNSPECIFIED))
        assertEquals(null, enterAction(EditorInfo.IME_ACTION_NONE))
    }
    @Test fun enter_label_follows_the_action() {
        assertEquals(
            listOf("開く", "検索", "送信", "次へ", "完了", "改行", "改行", "改行"),
            listOf(
                EditorInfo.IME_ACTION_GO, EditorInfo.IME_ACTION_SEARCH, EditorInfo.IME_ACTION_SEND, EditorInfo.IME_ACTION_NEXT,
                EditorInfo.IME_ACTION_DONE, EditorInfo.IME_ACTION_NONE, EditorInfo.IME_ACTION_UNSPECIFIED,
                EditorInfo.IME_ACTION_SEND or EditorInfo.IME_FLAG_NO_ENTER_ACTION,
            ).map(::enterLabel),
        )
    }
    @Test fun the_large_flick_preview_is_hidden_only_in_password_fields() {
        val text = InputType.TYPE_CLASS_TEXT
        val passwords = listOf(
            text or InputType.TYPE_TEXT_VARIATION_PASSWORD, text or InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD,
            text or InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD,
            InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_VARIATION_PASSWORD,
        )
        val others = listOf(
            text, text or InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS, text or InputType.TYPE_TEXT_VARIATION_URI,
            text or InputType.TYPE_TEXT_FLAG_MULTI_LINE, InputType.TYPE_CLASS_NUMBER, InputType.TYPE_CLASS_PHONE,
            InputType.TYPE_CLASS_DATETIME, 0,
        )
        for (type in passwords) {
            assertEquals(true, fieldPolicy(type, 0).isPassword, "$type"); assertEquals(false, fieldPolicy(type, 0).flickPreview, "$type")
        }
        for (type in others) {
            assertEquals(false, fieldPolicy(type, 0).isPassword, "$type"); assertEquals(true, fieldPolicy(type, 0).flickPreview, "$type")
        }
        assertEquals(true, fieldPolicy(text, EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING).flickPreview)
    }
    /** 絵文字の「最近使ったもの」（spec 04・09）: 覚えるのは学習する欄だけ。見せないのはパスワードの欄だけ。 */
    @Test fun emoji_picks_are_recorded_only_where_the_field_learns_and_recents_are_hidden_only_in_password_fields() {
        val text = InputType.TYPE_CLASS_TEXT
        val private = EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING
        // 入力欄, 覚えるか, 見せるか
        val rows = listOf(
            Triple(fieldPolicy(text, 0), true, true),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_FLAG_MULTI_LINE, 0), true, true),
            Triple(fieldPolicy(0, 0), true, true),
            Triple(fieldPolicy(text, private), false, true),                                             // シークレットモードなど
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_URI, 0), false, true),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS, 0), false, true),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_WEB_EMAIL_ADDRESS, 0), false, true),
            Triple(fieldPolicy(InputType.TYPE_CLASS_NUMBER, 0), false, true),
            Triple(fieldPolicy(InputType.TYPE_CLASS_PHONE, 0), false, true),
            Triple(fieldPolicy(InputType.TYPE_CLASS_DATETIME, 0), false, true),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_PASSWORD, 0), false, false),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD, 0), false, false),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD, 0), false, false),
            Triple(fieldPolicy(text or InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD, private), false, false),
            Triple(fieldPolicy(InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_VARIATION_PASSWORD, 0), false, false),
        )
        for ((policy, records, shows) in rows) {
            assertEquals(records, policy.recordsEmoji, "$policy"); assertEquals(shows, policy.showsRecentEmoji, "$policy")
        }
        // 見分け方は、パスワードの欄のほかの扱いと同じ。学習する指定が付いていても、パスワードの欄では覚えない。
        assertEquals(false, FieldPolicy(LayoutId.QWERTY, convert = false, learn = true, holdLast = false).recordsEmoji)
        for ((policy, _, shows) in rows) assertEquals(!policy.isPassword, shows)
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
    @Test fun enter_label_is_the_name_of_the_kind() {
        assertEquals(listOf("改行", "検索", "送信", "開く", "次へ", "完了", "実行"), EnterKind.entries.map { it.label })
        for (options in listOf(EditorInfo.IME_ACTION_SEND, EditorInfo.IME_ACTION_NONE, 0x40)) {
            assertEquals(enterKind(options).label, enterLabel(options))
        }
    }
    @Test fun other_actions_that_will_be_performed_are_not_labelled_as_newline() {
        assertEquals("実行", enterLabel(EditorInfo.IME_ACTION_PREVIOUS))
        assertEquals("実行", enterLabel(0x40))   // 名前のない動作
        assertEquals("改行", enterLabel(EditorInfo.IME_ACTION_PREVIOUS or EditorInfo.IME_FLAG_NO_ENTER_ACTION))
    }
}
