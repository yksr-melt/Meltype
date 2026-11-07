// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertTrue
import org.junit.Test
import org.json.JSONObject
import io.github.ninjin0802.keyboard.engine.Converter
import io.github.ninjin0802.keyboard.engine.Clause
import io.github.ninjin0802.keyboard.engine.Segment
import java.io.File

class MeltypeCoreBridgeDeviceTest {
    @Test fun coreRespondsToRomajiWithoutKotlinPort() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        MeltypeCoreSession.open(File(context.noBackupFilesDir, "meltype"),
            CoreMozcCallbacks(object : Converter {
                override fun convert(reading: String, context: String?): List<Segment>? = null
                override fun predict(reading: String): List<String> = emptyList()
                override fun learn(clauses: List<Clause>, context: String?) = Unit
            })).use { core ->
            // Check JSON contract and JNI round-trip first. Japanese conversion still
            // needs the Mozc callback and InputConnection integration.
            for (key in "konnichiwa") {
                val json = core.handleKey(key.uppercaseChar().code, key.code)
                requireNotNull(json) { "Native Core did not return a session result" }
                val result = JSONObject(json)
                assertTrue(result.has("consumed"))
                assertTrue(result.has("commits"))
                assertTrue(result.has("view"))
            }
            val commit = core.commit()
            requireNotNull(commit)
            assertTrue(JSONObject(commit).has("commits"))
        }
    }
}
