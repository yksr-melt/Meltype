// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import android.content.Context
import org.json.JSONException
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.nio.file.Files
import java.nio.file.StandardCopyOption

/**
 * The Core's data directory on Android (MELTYPE_DATA_DIR): config.json, the learning data and the
 * user dictionaries. The native Core fixes it for the process on first use (meltype_jni.cpp).
 */
internal fun coreDataDirectory(context: Context): File = File(context.noBackupFilesDir, "meltype")

/**
 * The Core's settings (config.json, the same file and format as on Windows, macOS and Linux).
 * The Core reads it again when it changes (SettingsFile, within a second), so a value written here
 * takes effect from the next key without restarting the keyboard.
 *
 * Only the top-level items the Core knows may be written: the Core reads the file with every name
 * mapped, and a file it cannot read is moved aside (config.json.broken) and every setting goes back
 * to its default. So an item is changed the way the Linux settings screen does it: read the whole
 * file, change that item only, write a temporary file next to it, and rename it over config.json.
 * A file this class cannot read is left alone. Comments, which the Core allows, are not kept
 * (nothing writes them on Android: the file is in the app's private storage).
 */
internal class CoreConfig(private val directory: File) {
    val file = File(directory, "config.json")

    /** The top-level boolean [key], or null if config.json does not set it or cannot be read. */
    fun boolean(key: String): Boolean? = read()?.opt(key) as? Boolean

    /**
     * Sets the top-level [key] to [value] (a Boolean, number or String the Core expects there) and
     * keeps everything else. False if it was not written: config.json cannot be read, or writing failed.
     */
    @Synchronized
    fun set(key: String, value: Any): Boolean {
        val values = if (file.exists()) read() ?: return false else newConfig()
        if (values.opt(key) == value) return true
        values.put(key, value)
        return write(values.toString(2) + "\n")
    }

    private fun read(): JSONObject? = try {
        if (file.exists()) JSONObject(file.readText()) else null
    } catch (e: IOException) {
        null
    } catch (e: JSONException) {
        null
    }

    /**
     * A new config.json, as the Linux settings screen makes it: the schema the Core writes next to it,
     * and the current SettingsVersion (without it the Core would take the file for an old one and
     * migrate it). The version is the schema's default; before the Core has run there is no schema,
     * and the Core then migrates the file, which keeps the values written here.
     */
    private fun newConfig(): JSONObject = JSONObject().apply {
        put("\$schema", "./$SCHEMA")
        settingsVersion()?.let { put("SettingsVersion", it) }
    }

    private fun settingsVersion(): Int? = try {
        val schema = File(directory, SCHEMA)
        if (!schema.exists()) null
        else JSONObject(schema.readText()).optJSONObject("properties")?.optJSONObject("SettingsVersion")
            ?.opt("default") as? Int
    } catch (e: IOException) {
        null
    } catch (e: JSONException) {
        null
    }

    private fun write(text: String): Boolean {
        if (!directory.isDirectory && !directory.mkdirs()) return false
        val temp = try {
            File.createTempFile("config.json.", ".tmp", directory)
        } catch (e: IOException) {
            return false
        }
        return try {
            FileOutputStream(temp).use { out ->
                out.write(text.toByteArray(Charsets.UTF_8))
                out.fd.sync()
            }
            Files.move(temp.toPath(), file.toPath(), StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE)
            true
        } catch (e: IOException) {
            false
        } finally {
            temp.delete()
        }
    }

    companion object {
        /** The JSON Schema the Core writes next to config.json (SettingsSchema.FileName). */
        private const val SCHEMA = "config.schema.json"

        /** Settings.LiveConversion: convert to kanji while typing. */
        const val LIVE_CONVERSION = "LiveConversion"
    }
}
