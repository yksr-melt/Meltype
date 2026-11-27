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
    /**
     * 英字が無ければ、前後の空白を除いただけで返す。英字があって Core でひらがなにできなかったら null
     * (打った英字のまま登録すると、読みがひらがなの変換と一致しない。呼び出し側は登録を止めて理由を出す)。
     */
    fun toHiragana(context: Context, input: String): String? = toHiragana(input) { text ->
        NativeApi.typedReading(
            coreDataDirectory(context.applicationContext).absolutePath.encodeToByteArray(),
            text.encodeToByteArray(),
        )?.decodeToString()
    }

    /** [convert] は Core でひらがなにする (できなければ null)。Core を読み込めない (LinkageError) ときも null。 */
    internal fun toHiragana(input: String, convert: (String) -> String?): String? {
        val text = input.trim()
        if (text.none { it in 'a'..'z' || it in 'A'..'Z' }) return text
        return try {
            convert(text)
        } catch (_: LinkageError) {
            null
        }
    }
}
