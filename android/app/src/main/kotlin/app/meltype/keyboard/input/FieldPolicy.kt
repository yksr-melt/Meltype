// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
package app.meltype.keyboard.input

import android.text.InputType
import android.view.inputmethod.EditorInfo

/**
 * 入力欄の種類から分かる事実。[traits] は Core に送る ImeTrait の名前 (変換するか・英数で始めるか・覚えるかは Core が決める)、
 * [symbols] は数字・記号のページで始めるか。
 */
data class FieldPolicy(val traits: List<String>, val symbols: Boolean) {
    /** パスワードの欄（数字だけのパスワードを含む）。 */
    val isPassword: Boolean get() = PASSWORD in traits

    /**
     * 絵文字の画面で選んだ絵文字を「最近使ったもの」に足すか。パスワードの欄と、覚えないように求める欄では足さない（spec 04・09）。
     */
    val recordsEmoji: Boolean get() = !isPassword && PRIVATE !in traits

    /** 絵文字の画面に「最近使ったもの」を出すか。パスワードの欄では、覚えてある絵文字を見せない（spec 04・09）。 */
    val showsRecentEmoji: Boolean get() = !isPassword

    companion object {
        const val PASSWORD = "Password"
        const val PRIVATE = "Private"
        const val EMAIL = "Email"
        const val NUMERIC = "Numeric"
    }
}

/** 入力欄の種類（EditorInfo の inputType・imeOptions）から、Core に送る事実と最初のページを決める（spec 09）。 */
fun fieldPolicy(inputType: Int, imeOptions: Int): FieldPolicy {
    val cls = inputType and InputType.TYPE_MASK_CLASS
    val variation = inputType and InputType.TYPE_MASK_VARIATION
    val numeric = cls == InputType.TYPE_CLASS_NUMBER || cls == InputType.TYPE_CLASS_PHONE || cls == InputType.TYPE_CLASS_DATETIME
    val traits = buildList {
        // 種類ごとに同じ数値が別の意味になる（数字のパスワード 0x10 は、日付と時刻では「日付」）ので、種類と組で見る。
        if (cls == InputType.TYPE_CLASS_TEXT && variation in passwordVariations ||
            cls == InputType.TYPE_CLASS_NUMBER && variation == InputType.TYPE_NUMBER_VARIATION_PASSWORD) add(FieldPolicy.PASSWORD)
        if (cls == InputType.TYPE_CLASS_TEXT && variation in emailVariations) add(FieldPolicy.EMAIL)
        if (numeric) add(FieldPolicy.NUMERIC)
        if (imeOptions and EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING != 0) add(FieldPolicy.PRIVATE)
    }
    return FieldPolicy(traits, symbols = numeric)
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
