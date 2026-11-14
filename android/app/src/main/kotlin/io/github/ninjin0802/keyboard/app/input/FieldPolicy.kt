// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

import android.text.InputType
import android.view.inputmethod.EditorInfo
import io.github.ninjin0802.keyboard.app.layout.LayoutId

/**
 * [layout]: 入力欄の種類に応じて表示する最初の配列。日本語QWERTYまたは英語QWERTY。
 * [holdLast]: パスワード欄かどうかを判断するための互換プロパティ。パスワード欄では偽となる。
 */
data class FieldPolicy(val layout: LayoutId, val convert: Boolean, val learn: Boolean, val holdLast: Boolean = true) {
    /** パスワードの欄（数字だけのパスワードを含む）。何も持たない欄が、ちょうどそれに当たる。 */
    val isPassword: Boolean get() = !holdLast

    /**
     * 絵文字の画面で選んだ絵文字を「最近使ったもの」に足すか。学習する欄だけ（spec 04・09）。
     * パスワードの欄では、学習の指定がどうであっても足さない。
     */
    val recordsEmoji: Boolean get() = learn && !isPassword

    /** 絵文字の画面に「最近使ったもの」を出すか。パスワードの欄では、覚えてある絵文字を見せない（spec 04・09）。 */
    val showsRecentEmoji: Boolean get() = !isPassword
}

/** 入力欄の種類から、最初の配列・変換・学習を決める（spec 09）。 */
fun fieldPolicy(inputType: Int, imeOptions: Int): FieldPolicy {
    val cls = inputType and InputType.TYPE_MASK_CLASS
    val variation = inputType and InputType.TYPE_MASK_VARIATION
    return when {
        cls == InputType.TYPE_CLASS_NUMBER ->
            FieldPolicy(
                LayoutId.SYMBOLS, convert = false, learn = false,
                holdLast = variation != InputType.TYPE_NUMBER_VARIATION_PASSWORD,
            )
        cls == InputType.TYPE_CLASS_PHONE || cls == InputType.TYPE_CLASS_DATETIME ->
            FieldPolicy(LayoutId.SYMBOLS, convert = false, learn = false)
        cls == InputType.TYPE_CLASS_TEXT && variation in passwordVariations ->
            FieldPolicy(LayoutId.QWERTY, convert = false, learn = false, holdLast = false)
        cls == InputType.TYPE_CLASS_TEXT && variation in emailVariations ->
            FieldPolicy(LayoutId.QWERTY, convert = false, learn = false)
        // URL 欄は検索にも使うため日本語で始める。URL の直接入力は英字配列に切り替えられる。学習はしない。
        cls == InputType.TYPE_CLASS_TEXT && variation == InputType.TYPE_TEXT_VARIATION_URI ->
            FieldPolicy(LayoutId.ROMAJI, convert = true, learn = false)
        imeOptions and EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING != 0 ->
            FieldPolicy(LayoutId.ROMAJI, convert = true, learn = false)
        else -> FieldPolicy(LayoutId.ROMAJI, convert = true, learn = true)
    }
}

private val passwordVariations = setOf(
    InputType.TYPE_TEXT_VARIATION_PASSWORD,
    InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD,
    InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD,
)

private val emailVariations = setOf(
    InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS,
    InputType.TYPE_TEXT_VARIATION_WEB_EMAIL_ADDRESS,
)

/** 確定キーで入力欄に頼む動作（EditorInfo.IME_ACTION_*）。改行を入れる欄なら null。 */
fun enterAction(imeOptions: Int): Int? {
    if (imeOptions and EditorInfo.IME_FLAG_NO_ENTER_ACTION != 0) return null
    return when (val action = imeOptions and EditorInfo.IME_MASK_ACTION) {
        EditorInfo.IME_ACTION_UNSPECIFIED, EditorInfo.IME_ACTION_NONE -> null
        else -> action
    }
}

/** 確定キーが何をするか。キーの絵と名前（[label]）を決める。 */
enum class EnterKind(val label: String) {
    NEWLINE("改行"), SEARCH("検索"), SEND("送信"), GO("開く"), NEXT("次へ"), DONE("完了"), OTHER("実行")
}

/** [NEWLINE][EnterKind.NEWLINE] は、改行のキーを送るときだけ。名前を決めていない動作は [OTHER][EnterKind.OTHER]。 */
fun enterKind(imeOptions: Int): EnterKind = when (enterAction(imeOptions)) {
    null -> EnterKind.NEWLINE
    EditorInfo.IME_ACTION_GO -> EnterKind.GO
    EditorInfo.IME_ACTION_SEARCH -> EnterKind.SEARCH
    EditorInfo.IME_ACTION_SEND -> EnterKind.SEND
    EditorInfo.IME_ACTION_NEXT -> EnterKind.NEXT
    EditorInfo.IME_ACTION_DONE -> EnterKind.DONE
    else -> EnterKind.OTHER
}

/** 確定キーの名前。 */
fun enterLabel(imeOptions: Int): String = enterKind(imeOptions).label
