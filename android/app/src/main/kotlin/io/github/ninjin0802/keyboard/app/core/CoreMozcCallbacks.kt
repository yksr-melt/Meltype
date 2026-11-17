// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import io.github.ninjin0802.keyboard.app.PerfLog
import io.github.ninjin0802.keyboard.mozc.MozcBackend
import io.github.ninjin0802.keyboard.mozc.MozcClause

/**
 * Core の MozcConverter が送る変換ヘルパーの要求 (native/mozc/meltype_mozc_helper.cc の先頭) に、[mozc] で答える。
 * JNI が Core の変換のスレッド (入力のスレッドとは別) から呼ぶ。
 */
internal class CoreMozcCallbacks(
    private val mozc: () -> MozcBackend?,
    private val conversionReady: () -> Unit,
) {
    /** 要求 1 行に答える。null は失敗 (Mozc を開けなかった。Core は数回続くと Mozc を使うのをやめる)。 */
    @Suppress("unused")
    fun request(requestBytes: ByteArray): ByteArray? {
        val converter = mozc() ?: return null
        return answer(converter, requestBytes.decodeToString()).encodeToByteArray()
    }

    /** Core の変換の結果が届いた (Core の変換のスレッドから呼ばれる)。 */
    @Suppress("unused")
    fun ready() = conversionReady()

    private fun answer(converter: MozcBackend, request: String): String {
        val fields = request.split('\t')
        return when (fields[0]) {
            "C" -> {
                val (context, reading) = fields.drop(1)
                val start = System.nanoTime()
                val segments = converter.convert(reading, context.ifEmpty { null })
                PerfLog.event("convert", (System.nanoTime() - start) / 1000, reading.length)
                // 変換できない読みは空行 (Core はかなのまま見せ、同じ読みでは聞き直さない)
                segments
                    ?.takeIf { found -> found.joinToString("") { it.reading } == reading }
                    ?.joinToString(RECORD) { s -> (listOf(s.reading) + s.candidates).joinToString(UNIT) }
                    ?: ""
            }
            "L" -> {
                val (context, records) = fields.drop(1)
                val clauses = records.split(RECORD).map { record ->
                    val (reading, text) = record.split(UNIT)
                    MozcClause(reading, text)
                }
                if (converter.learn(clauses, context.ifEmpty { null })) "OK" else "NG"
            }
            "R" -> converter.reading(fields[1]) ?: ""
            // 覚えさせるたびに保存している (MozcConverter.learn)
            "S" -> "OK"
            else -> throw IllegalArgumentException("Unknown Mozc helper request: ${fields[0]}")
        }
    }

    private companion object {
        const val UNIT = "\u001f"
        const val RECORD = "\u001e"
    }
}
