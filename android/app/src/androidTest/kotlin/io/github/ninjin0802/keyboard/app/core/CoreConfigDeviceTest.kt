// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import org.json.JSONObject
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.nio.file.Files

/**
 * The Core's settings are written to its config.json (CoreConfig) and take effect from the next
 * key, without restarting the keyboard; writing one item keeps the others and a file the Core can read.
 */
class CoreConfigDeviceTest {
    private val core = DeviceTestCore.open()
    private val config = CoreConfig(DeviceTestCore.directory)
    private val original: ByteArray? = config.file.takeIf { it.exists() }?.readBytes()
    private val broken = File(DeviceTestCore.directory, "config.json.broken")

    /** The Core's default of each item a test sets (Meltype.Core Settings). */
    private val defaults = mapOf(CoreConfig.LIVE_CONVERSION to true, "CursorIntoBrackets" to false)
    private val changed = mutableSetOf<String>()

    @After fun restore() {
        core.close()
        if (changed.isEmpty()) return
        val saved = original
        // Without a file the Core would keep what it last read, so put the defaults back instead.
        if (saved != null) config.file.writeBytes(saved) else changed.forEach { config.set(it, defaults.getValue(it)) }
        // Let the Core read the restored file before the next test.
        Thread.sleep(SETTINGS_RELOAD_MS)
    }

    /** Types [keys] through the Core into an editor ('\n' is Enter). */
    private fun type(editor: TestEditor, adapter: CoreInputAdapter, keys: String) {
        for (key in keys) {
            val event = if (key == '\n') ImeJson.virtualKey(0x0D, adapter.field(emptyList()))
            else ImeJson.characterKey(key.code, false, adapter.field(emptyList()))
            if (!adapter.apply(core.handle(event)) && key != '\n') editor.commitText(key.toString(), 1)
        }
    }

    /** Sets a Core setting and waits until the Core reads config.json again (it checks once a second). */
    private fun set(key: String, value: Any) {
        broken.delete()
        changed += key
        assertTrue("config.json was not written", config.set(key, value))
        Thread.sleep(SETTINGS_RELOAD_MS)
    }

    @Test fun liveConversionTakesEffectFromTheNextKey() {
        val editor = TestEditor()
        val adapter = CoreInputAdapter { editor }
        set(CoreConfig.LIVE_CONVERSION, false)
        type(editor, adapter, "kyouha")
        assertEquals("Live conversion OFF shows the reading", "きょうは", adapter.shown)
        adapter.apply(core.handle(ImeJson.virtualKey(0x1B, adapter.field(emptyList())))) // Esc

        set(CoreConfig.LIVE_CONVERSION, true)
        type(editor, adapter, "kyouha")
        assertEquals("Live conversion ON converts while typing", "今日は", adapter.shown)
        assertEquals(true, config.boolean(CoreConfig.LIVE_CONVERSION))
        assertFalse("The Core could not read config.json", broken.exists())
    }

    @Test fun bracketsPutTheCaretInsideWhenTheSettingIsOn() {
        set("CursorIntoBrackets", true)
        val editor = TestEditor()
        val adapter = CoreInputAdapter { editor }
        type(editor, adapter, "[]\n")
        assertEquals("「」", editor.content)
        assertEquals("The caret goes back inside the brackets (moveCaretBack)", 1, editor.caret)
        assertFalse("The Core could not read config.json", broken.exists())
    }

    @Test fun settingOneItemKeepsTheRestOfTheFile() {
        val directory = Files.createTempDirectory(DeviceTestCore.directory.parentFile!!.toPath(), "config-test").toFile()
        try {
            val file = File(directory, "config.json")
            val before = """{
              "${'$'}schema": "./config.schema.json",
              "SettingsVersion": 10,
              "LiveConversion": true,
              "SnippetMark": ";",
              "AppRules": [{"Process": "com.example.app/Main", "Enabled": false, "Profile": "Code"}],
              "ClockTimeZone": "Asia/Tokyo",
              "ZenzaiDevice": "日本語   \"quoted\""
            }"""
            file.writeText(before)
            val written = CoreConfig(directory)
            assertTrue(written.set(CoreConfig.LIVE_CONVERSION, false))
            val expected = JSONObject(before).put(CoreConfig.LIVE_CONVERSION, false)
            val after = JSONObject(file.readText())
            assertEquals(expected.keys().asSequence().toList(), after.keys().asSequence().toList())
            for (key in expected.keys()) assertEquals(key, expected.get(key).toString(), after.get(key).toString())
            assertEquals(listOf("config.json"), directory.list()!!.toList())
        } finally {
            directory.deleteRecursively()
        }
    }

    @Test fun aFileThatCannotBeReadIsLeftAlone() {
        val directory = Files.createTempDirectory(DeviceTestCore.directory.parentFile!!.toPath(), "config-test").toFile()
        try {
            val file = File(directory, "config.json")
            file.writeText("{ \"LiveConversion\": ")
            assertFalse(CoreConfig(directory).set(CoreConfig.LIVE_CONVERSION, false))
            assertEquals("{ \"LiveConversion\": ", file.readText())
            assertEquals(null, CoreConfig(directory).boolean(CoreConfig.LIVE_CONVERSION))
        } finally {
            directory.deleteRecursively()
        }
    }

    @Test fun aNewFileNamesTheSchemaAndTheVersion() {
        val directory = Files.createTempDirectory(DeviceTestCore.directory.parentFile!!.toPath(), "config-test").toFile()
        try {
            File(directory, "config.schema.json").writeText("""{"properties":{"SettingsVersion":{"type":"integer","default":10}}}""")
            assertTrue(CoreConfig(directory).set(CoreConfig.LIVE_CONVERSION, false))
            val written = JSONObject(File(directory, "config.json").readText())
            assertEquals("./config.schema.json", written.getString("\$schema"))
            assertEquals(10, written.getInt("SettingsVersion"))
            assertEquals(false, written.getBoolean(CoreConfig.LIVE_CONVERSION))
        } finally {
            directory.deleteRecursively()
        }
    }

    private companion object {
        /** SettingsFile.CheckIntervalMs (1000) and a margin. */
        const val SETTINGS_RELOAD_MS = 1300L
    }
}
