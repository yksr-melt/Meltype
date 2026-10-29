// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import io.github.ninjin0802.keyboard.engine.Converter

/** JNI calls these synchronously while processing one core key. */
internal class CoreMozcCallbacks(private val converter: Converter) {
    @Suppress("unused")
    fun clauses(readingBytes: ByteArray, contextBytes: ByteArray?): ByteArray? {
        val reading = readingBytes.decodeToString()
        val context = contextBytes?.decodeToString()
        val segments = converter.convert(reading, context) ?: return null
        if (segments.isEmpty() || segments.joinToString("") { it.reading } != reading) return null
        return segments.joinToString("\n") { s -> s.reading + "\t" + (s.candidates.firstOrNull() ?: s.reading) }
            .encodeToByteArray()
    }

    @Suppress("unused")
    fun candidates(readingBytes: ByteArray): ByteArray? {
        val reading = readingBytes.decodeToString()
        val list = converter.convert(reading, null)?.firstOrNull()?.candidates
            ?: converter.predict(reading)
        return list.joinToString("\n").encodeToByteArray()
    }
}
