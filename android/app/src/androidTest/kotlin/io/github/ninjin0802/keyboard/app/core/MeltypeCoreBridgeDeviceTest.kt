// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.json.JSONObject
import io.github.ninjin0802.keyboard.engine.Converter
import io.github.ninjin0802.keyboard.engine.Clause
import io.github.ninjin0802.keyboard.engine.Segment
import java.io.File

class MeltypeCoreBridgeDeviceTest {
    @Test fun coreAnswersEventsThroughTheImeProtocol() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        MeltypeCoreHost.open(File(context.noBackupFilesDir, "meltype"),
            CoreMozcCallbacks(object : Converter {
                override fun convert(reading: String, context: String?): List<Segment>? = null
                override fun predict(reading: String): List<String> = emptyList()
                override fun learn(clauses: List<Clause>, context: String?) = Unit
            })).use { core ->
            // Check the JSON contract and the JNI round-trip. Japanese conversion
            // also needs the Mozc callback and InputConnection integration.
            val field = ImeFieldFacts(before = "", after = "", composing = false)
            for ((index, key) in "konnichiwa".withIndex()) {
                val json = core.handle(ImeJson.characterKey(key.code, false, field.copy(composing = index > 0)))
                requireNotNull(json) { "Native Core did not return a response" }
                val output = JSONObject(json)
                assertEquals(1, output.getInt("version"))
                assertTrue("The Core must take Japanese input in an ordinary field",
                    output.getJSONObject("state").getBoolean("handlesInput"))
                assertTrue("Romaji must go into the Core's composition", ImeJson.parse(json).consumed)
            }
            val commit = ImeJson.parse(requireNotNull(core.handle(ImeJson.commitRequested(field.copy(composing = true)))))
            assertTrue("CommitRequested must commit the composition", commit.commits.isNotEmpty())
            assertEquals(null, commit.view)

            // A password field is not the Core's: it hands keys back to the keyboard.
            val password = ImeFieldFacts(before = "", after = "", composing = false, traits = listOf("Password"))
            val passed = requireNotNull(core.handle(ImeJson.characterKey('a'.code, false, password)))
            assertFalse(JSONObject(passed).getJSONObject("state").getBoolean("handlesInput"))
            assertFalse(ImeJson.parse(passed).consumed)
        }
    }
}
