// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package app.meltype.keyboard.dictionary

import android.content.Context
import app.meltype.keyboard.core.NativeApi
import app.meltype.keyboard.core.coreDataDirectory

/**
 * ユーザー辞書の登録で打った読みを、ひらがなにする (shino → しの)。Windows・Mac・Linux の設定アプリ (UserDictionaryPage) と
 * 同じく、Core の TypedReading (ユーザーのローマ字の表 romaji.txt も使う) で変える。キーボードが英語と判定して
 * 読みが英字のまま入っても、変換の読み (ひらがな) と食い違わないようにする。
 */
internal object TypedReading {
    /** 英字が無ければ、前後の空白を除いただけで返す。Core を読み込めないときも、打ったまま返す。 */
    fun toHiragana(context: Context, input: String): String {
        val text = input.trim()
        if (text.none { it in 'a'..'z' || it in 'A'..'Z' }) return text
        return try {
            NativeApi.typedReading(
                coreDataDirectory(context.applicationContext).absolutePath.encodeToByteArray(),
                text.encodeToByteArray(),
            )?.decodeToString() ?: text
        } catch (_: LinkageError) {
            text
        }
    }
}
