// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package app.meltype.keyboard.core

import app.meltype.keyboard.PerfLog
import app.meltype.keyboard.mozc.MozcBackend
import app.meltype.keyboard.mozc.MozcWord

/**
 * Core の MozcConverter が送る変換ヘルパーの要求 (native/mozc/meltype_mozc_helper.cc の先頭) に、[mozc] で答える。
 * JNI が Core の変換のスレッド (入力のスレッドとは別) から呼ぶ。
 */
internal class CoreMozcCallbacks(
    private val mozc: Lazy<MozcBackend?>,
    private val conversionReady: () -> Unit,
) {
    /** 要求 1 行に答える。null は失敗 (Mozc を開けなかった・時間切れ。Core は答えを控えず、数回続くと Mozc を使うのをやめる)。 */
    @Suppress("unused")
    fun request(requestBytes: ByteArray): ByteArray? {
        val request = requestBytes.decodeToString()
        // 逆変換 (再変換の読み) は入力のスレッドから頼まれるので、Mozc を開き終わるのを待たずに「逆変換できない」と答える
        if (request.startsWith("R\t") && !mozc.isInitialized()) return ByteArray(0)
        val converter = mozc.value ?: return null
        return answer(converter, request)?.encodeToByteArray()
    }

    /** Core の変換の結果が届いた (Core の変換のスレッドから呼ばれる)。 */
    @Suppress("unused")
    fun ready() = conversionReady()

    private fun answer(converter: MozcBackend, request: String): String? {
        val fields = request.split('\t')
        return when (fields[0]) {
            "C" -> {
                val (context, reading) = fields.drop(1)
                val start = System.nanoTime()
                val segments = converter.convert(reading, context.ifEmpty { null })
                PerfLog.event("convert", (System.nanoTime() - start) / 1000, reading.length)
                // 変換できない読みは空行 (Core はかなのまま見せ、同じ読みでは聞き直さない)
                segments?.takeIf { found -> found.joinToString("") { it.reading } == reading }
                    ?.joinToString(RECORD) { s ->
                        // ユーザー辞書の語を含む候補は頭に GS を付ける (デスクトップの meltype_mozc_helper と同じ)
                        val candidates = s.candidates.mapIndexed { i, c -> if (i == 0 && s.userWord) USER_WORD + c else c }
                        (listOf(s.reading) + candidates).joinToString(UNIT)
                    }
                    ?: segments?.let { "" }
            }
            "R" -> converter.reading(fields[1]) ?: ""
            "D" -> {
                val words = fields[1].split(RECORD).filter { it.isNotEmpty() }.map { record ->
                    val (reading, word, pos) = record.split(UNIT)
                    MozcWord(reading, word, pos)
                }
                if (converter.setUserDictionary(words)) "OK" else "NG"
            }
            else -> throw IllegalArgumentException("Unknown Mozc helper request: ${fields[0]}")
        }
    }

    private companion object {
        const val UNIT = "\u001f"
        const val RECORD = "\u001e"
        const val USER_WORD = "\u001d"
    }
}
