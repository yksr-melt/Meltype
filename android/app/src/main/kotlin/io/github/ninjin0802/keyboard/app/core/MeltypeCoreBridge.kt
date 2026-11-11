// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import java.io.File

/**
 * Production-path NativeAOT/JNI bridge to the shared Meltype.Core (ImeHost).
 * Android sends input events as ImeEvent JSON and applies the ImeOutput JSON
 * it gets back (ImeJson builds and reads both; CoreInputAdapter applies them).
 * Use on a single IME thread; ImeHost is not thread-safe.
 *
 * All text crosses JNI as UTF-8 byte arrays rather than Java Modified UTF-8.
 */
internal object NativeApi {
    init {
        System.loadLibrary("MeltypeNative")
        System.loadLibrary("meltype_jni")
    }

    external fun init(directoryUtf8: ByteArray, callbacks: CoreMozcCallbacks): Boolean
    /** meltype_ime_create */
    external fun create(): Long
    /** meltype_ime_destroy */
    external fun destroy(host: Long)
    /** meltype_ime_handle: one ImeEvent JSON in, one ImeOutput JSON out (null if the Core failed). */
    external fun handle(host: Long, eventUtf8: ByteArray): ByteArray?
}

/** One ImeHost for the whole keyboard. All Android editors share its single instance ("android"). */
internal class MeltypeCoreHost private constructor(private var host: Long) : AutoCloseable {
    companion object {
        /** Use an app-private directory (e.g. File(context.noBackupFilesDir, "meltype")). */
        fun open(directory: File, callbacks: CoreMozcCallbacks): MeltypeCoreHost {
            require(directory.isAbsolute) { "Expected a private absolute path" }
            check(directory.isDirectory || directory.mkdirs()) { "Cannot create Core data directory" }
            check(NativeApi.init(directory.absolutePath.encodeToByteArray(), callbacks)) {
                "Native Core initialization failed (or conflicting data directory)"
            }
            val ptr = NativeApi.create()
            check(ptr != 0L) { "Native Core host creation failed" }
            return MeltypeCoreHost(ptr)
        }
    }

    /** Sends one ImeEvent JSON (built by [ImeJson]) and returns the ImeOutput JSON. */
    fun handle(event: String): String? {
        check(host != 0L) { "Host is closed" }
        return NativeApi.handle(host, event.encodeToByteArray())?.decodeToString()
    }

    override fun close() {
        val ptr = host
        host = 0
        if (ptr != 0L) NativeApi.destroy(ptr)
    }
}
