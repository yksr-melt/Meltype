// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import java.io.File

/**
 * Production-path NativeAOT/JNI bridge for Japanese QWERTY bilingual detection.
 * KeyboardService and CoreInputAdapter connect its results to InputConnection.
 * Use on a single IME thread; the Core session
 * is not documented as thread-safe.
 *
 * All text crosses JNI as UTF-8 byte arrays rather than Java Modified UTF-8.
 */
internal object NativeApi {
    init {
        System.loadLibrary("MeltypeNative")
        System.loadLibrary("meltype_jni")
    }

    external fun init(directoryUtf8: ByteArray, callbacks: CoreMozcCallbacks): Boolean
    external fun create(): Long
    external fun destroy(handle: Long)
    external fun handleKey(
        handle: Long, vk: Int, scalar: Int, modifiers: Int,
        beforeUtf8: ByteArray?, afterUtf8: ByteArray?,
    ): ByteArray?
    external fun commit(handle: Long): ByteArray?
    external fun selectCandidate(handle: Long, index: Int): ByteArray?
    external fun selectPrediction(handle: Long, index: Int): ByteArray?
    external fun setLiveConversion(handle: Long, enabled: Boolean)
    external fun setDirect(handle: Long, direct: Boolean)
    external fun setCanDelete(handle: Long, canDelete: Boolean)
}

/** Native Core lifecycle. Always close before a field change or IME shutdown. */
internal class MeltypeCoreSession private constructor(private var handle: Long) : AutoCloseable {
    companion object {
        /** Use an app-private directory (e.g. File(context.noBackupFilesDir, "meltype")). */
        fun open(directory: File, callbacks: CoreMozcCallbacks): MeltypeCoreSession {
            require(directory.isAbsolute) { "Expected a private absolute path" }
            check(directory.isDirectory || directory.mkdirs()) { "Cannot create Core data directory" }
            check(NativeApi.init(directory.absolutePath.encodeToByteArray(), callbacks)) {
                "Native Core initialization failed (or conflicting data directory)"
            }
            val ptr = NativeApi.create()
            check(ptr != 0L) { "Native Core session creation failed" }
            return MeltypeCoreSession(ptr)
        }
    }

    private fun currentHandle(): Long = handle.also { check(it != 0L) { "Session is closed" } }

    /** vk: Windows virtual key (A=0x41, space=0x20), scalar: Unicode scalar. */
    fun handleKey(vk: Int, scalar: Int, modifiers: Int = 0, before: String? = null, after: String? = null): String? =
        NativeApi.handleKey(currentHandle(), vk, scalar, modifiers,
            before?.encodeToByteArray(), after?.encodeToByteArray())?.decodeToString()

    fun commit(): String? = NativeApi.commit(currentHandle())?.decodeToString()
    fun selectCandidate(index: Int): String? =
        NativeApi.selectCandidate(currentHandle(), index)?.decodeToString()
    fun selectPrediction(index: Int): String? =
        NativeApi.selectPrediction(currentHandle(), index)?.decodeToString()
    fun setLiveConversion(enabled: Boolean) = NativeApi.setLiveConversion(currentHandle(), enabled)
    fun setDirect(direct: Boolean) = NativeApi.setDirect(currentHandle(), direct)
    fun setCanDelete(canDelete: Boolean) = NativeApi.setCanDelete(currentHandle(), canDelete)

    override fun close() {
        val ptr = handle
        handle = 0
        if (ptr != 0L) NativeApi.destroy(ptr)
    }
}
