package io.github.ninjin0802.keyboard.app.core

import android.view.View
import android.view.inputmethod.BaseInputConnection
import androidx.test.platform.app.InstrumentationRegistry
import io.github.ninjin0802.keyboard.app.layout.withoutEmoji
import io.github.ninjin0802.keyboard.engine.Clause
import io.github.ninjin0802.keyboard.engine.Converter
import io.github.ninjin0802.keyboard.engine.Segment
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/**
 * Device-level integration: NativeAOT/JNI -> original Meltype.Core language
 * detector -> CoreInputAdapter -> Android InputConnection.
 * A successful build alone is NOT a successful bilingual regression test.
 */
class LanguageDetectionDeviceTest {
    private val converter = object : Converter {
        override fun convert(reading: String, context: String?): List<Segment>? = null
        override fun predict(reading: String): List<String> = emptyList()
        override fun learn(clauses: List<Clause>, context: String?) {}
    }

    private fun type(text: String): String {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val connection = BaseInputConnection(View(ctx), true)
        val adapter = CoreInputAdapter { connection }
        MeltypeCoreSession.open(File(ctx.noBackupFilesDir, "bilingual-device-test"), CoreMozcCallbacks(converter)).use { core ->
            core.setCanDelete(true)
            for (char in text) {
                val preceding = connection.getTextBeforeCursor(50, 0)?.toString()
                val response = core.handleKey(
                    char.uppercaseChar().code,
                    char.code,
                    before = preceding,
                ) ?: error("Native engine returned null on input '$text' after '$char'")
                val consumed = adapter.apply(response)
                if (!consumed) connection.commitText(char.toString(), 1)
                println("MELTYPE_BILINGUAL_TRACE input=$text char=$char consumed=$consumed response=$response")
            }
            val end = core.commit() ?: error("Native engine returned null on commit for '$text'")
            adapter.apply(end)
            connection.finishComposingText()
            val actual = connection.getEditable().toString()
            println("MELTYPE_BILINGUAL_RESULT input=$text actual=$actual")
            return actual
        }
    }

    @Test fun englishStaysLatin() {
        assertEquals("hello", type("hello"))
        assertEquals("meeting", type("meeting"))
    }

    @Test fun japaneseRomajiBecomesKana() {
        assertEquals("ありがとう", type("arigatou"))
        val greeting = type("konnichiwa")
        assertTrue("Expected Japanese kana for konnichiwa but got '$greeting'",
            greeting == "こんにちは" || greeting == "こんにちわ")
    }

    @Test fun mixedLanguagesWithoutManualToggle() {
        assertEquals("hello ありがとう", type("hello arigatou"))
    }

    @Test fun predictionActionsAreMappedToNativeIndices() {
        val view = org.json.JSONObject(
            """{"text":"hel","converting":false,"candidates":[],"predictions":["hello","help"],"selectedPrediction":-1}"""
        )
        val (choices, selected) = CoreCandidateChoices.fromView(view)
        assertEquals(null, selected)
        assertEquals(CoreChoiceKind.PREDICTION, choices[0].kind)
        assertEquals(0, choices[0].nativeIndex)
        assertEquals(CoreChoiceKind.PREDICTION, choices[1].kind)
        assertEquals(1, choices[1].nativeIndex)
        assertTrue("Correction row must not show operation buttons", choices.none {
            it.label in setOf("変換", "かなに直す", "英字に直す")
        })
        val converted = org.json.JSONObject(
            """{"text":"こんにちは","converting":true,"candidates":["今日は","こんにちは"],"selectedIndex":1}"""
        )
        val (convertedChoices, selection) = CoreCandidateChoices.fromView(converted)
        assertEquals(1, selection)
        assertEquals(CoreChoiceKind.CONVERSION, convertedChoices[1].kind)
        assertEquals(1, convertedChoices[1].nativeIndex)
    }

    @Test fun correctionsShowRealWordsAndNeverCommandButtons() {
        val view = org.json.JSONObject(
            """{"text":"日本語","raw":"nihongo","kana":"にほんご","converting":false,
            "candidates":[],"predictions":[],"selectedPrediction":-1}"""
        )
        val (choices, _) = CoreCandidateChoices.fromView(view)
        assertTrue(choices.any { it.label == "nihongo" && it.kind == CoreChoiceKind.LATIN_CORRECTION })
        assertTrue(choices.any { it.label == "にほんご" && it.kind == CoreChoiceKind.KANA_CORRECTION })
        assertTrue(choices.any { it.label == "ニホンゴ" && it.kind == CoreChoiceKind.KATAKANA_CORRECTION })
        assertTrue(choices.none { it.label in setOf("変換", "かなに直す", "英字に直す") })
    }

    @Test fun emojiPreferenceFiltersPredictions() {
        val view = org.json.JSONObject(
            """{"text":"ありがとう","raw":"arigatou","kana":"ありがとう",
            "predictions":["ありがとうございます","ありがとう😊"],"candidates":[],
            "converting":false,"selectedPrediction":-1}"""
        )
        val enabled = CoreCandidateChoices.fromView(view, allowEmoji = true).first
        val disabled = CoreCandidateChoices.fromView(view, allowEmoji = false).first
        assertTrue(enabled.any { it.label == "ありがとう😊" })
        assertTrue(disabled.none { it.label == "ありがとう😊" })
        assertTrue(disabled.any { it.label == "ありがとうございます" })
    }

    @Test fun settingsPersistJapaneseSpaceAndEmojiPreferences() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val settings = io.github.ninjin0802.keyboard.app.Settings(ctx)
        val oldWidth = settings.japaneseSpaceFullWidth
        val oldEmoji = settings.emojiEnabled
        try {
            settings.japaneseSpaceFullWidth = false
            settings.emojiEnabled = false
            val restored = io.github.ninjin0802.keyboard.app.Settings(ctx)
            assertEquals(false, restored.japaneseSpaceFullWidth)
            assertEquals(false, restored.emojiEnabled)
            settings.japaneseSpaceFullWidth = true
            settings.emojiEnabled = true
            assertEquals(true, restored.japaneseSpaceFullWidth)
            assertEquals(true, restored.emojiEnabled)
        } finally {
            settings.japaneseSpaceFullWidth = oldWidth
            settings.emojiEnabled = oldEmoji
        }
        val noEmoji = io.github.ninjin0802.keyboard.app.layout.Layouts.romaji.withoutEmoji()
        assertTrue(noEmoji.keys.none { it.action == io.github.ninjin0802.keyboard.app.layout.KeyAction.Emoji })
    }

    @Test fun originalLatinCorrectionCanBeSelectedAndCommitted() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val connection = BaseInputConnection(View(ctx), true)
        val adapter = CoreInputAdapter { connection }
        MeltypeCoreSession.open(File(ctx.noBackupFilesDir, "bilingual-device-test"),
            CoreMozcCallbacks(converter)).use { core ->
            core.setCanDelete(true)
            for (ch in "nihongo") {
                val response = core.handleKey(ch.uppercaseChar().code, ch.code,
                    before = connection.getTextBeforeCursor(50, 0)?.toString())
                    ?: error("No result for char $ch")
                if (!adapter.apply(response)) connection.commitText(ch.toString(), 1)
            }
            assertTrue(adapter.choices.any { it.label == "nihongo" &&
                it.kind == CoreChoiceKind.LATIN_CORRECTION })
            adapter.apply(core.handleKey(0x79, 0)) // F10: original Latin spelling
            adapter.apply(core.handleKey(0x0D, 0)) // Enter: commit
            assertEquals("nihongo", connection.getEditable().toString())
        }
    }

    @Test fun digitsStayInCompositionUntilExplicitEnter() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val connection = BaseInputConnection(View(ctx), true)
        val adapter = CoreInputAdapter { connection }
        MeltypeCoreSession.open(File(ctx.noBackupFilesDir, "bilingual-device-test"), CoreMozcCallbacks(converter)).use { core ->
            core.setCanDelete(true)
            for (ch in "arigatou") {
                val payload = core.handleKey(ch.uppercaseChar().code, ch.code,
                    before = connection.getTextBeforeCursor(50, 0)?.toString())
                    ?: error("Native Core did not respond to romaji")
                val consumed = adapter.apply(payload)
                assertTrue("Kana romaji must enter composition for $ch", consumed)
            }
            assertTrue("Japanese composition disappeared before keypad digits", adapter.composing)
            for (digit in "123") {
                val payload = core.handleKey(digit.code, digit.code,
                    before = connection.getTextBeforeCursor(50, 0)?.toString())
                    ?: error("Core returned null for numeric keypad $digit")
                assertTrue("Numpad digit $digit should be consumed as composition",
                    adapter.apply(payload))
                assertTrue("Numpad digit $digit unexpectedly committed text", adapter.composing)
            }
            assertTrue("Composition should be active before explicit Enter", adapter.composing)
            val enter = core.handleKey(0x0D, 0) ?: error("Core did not handle Enter")
            adapter.apply(enter)
            assertTrue("Enter should terminate composition", !adapter.composing)
        }
    }

    @Test fun candidateBarTextIsReadableAndHasBreathingRoom() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val density = ctx.resources.displayMetrics.density
        val scaled = ctx.resources.displayMetrics.scaledDensity
        val bar = io.github.ninjin0802.keyboard.app.view.CandidateBarView(ctx)
        val candidate = "これは少し長めの予測変換候補です"
        bar.show(listOf(io.github.ninjin0802.keyboard.engine.Candidate(
            candidate, 0, io.github.ninjin0802.keyboard.engine.CandidateKind.PREDICTION
        )), null)
        bar.measure(
            View.MeasureSpec.makeMeasureSpec((410f*density).toInt(), View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED)
        )
        assertTrue("Candidate row too short: " + bar.measuredHeight,
            bar.measuredHeight >= 68f*density)
        val row = bar.getChildAt(0) as android.widget.LinearLayout
        val text = row.getChildAt(0) as android.widget.TextView
        assertEquals(candidate, text.text.toString())
        assertTrue("Prediction too small: " + text.textSize / scaled,
            text.textSize / scaled >= 19.5f)
        assertTrue("First choice touch target is too narrow",
            text.minimumWidth >= 60f*density)
    }

    @Test fun exteriorMarginsStayUnclickableWhileLetterKeysWork() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val density = ctx.resources.displayMetrics.density
        val keyboard = io.github.ninjin0802.keyboard.app.view.KeyboardView(ctx)
        keyboard.layout = io.github.ninjin0802.keyboard.app.layout.Layouts.romaji
        val width = (400f*density).toInt()
        keyboard.measure(
            View.MeasureSpec.makeMeasureSpec(width, View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED))
        keyboard.layout(0, 0, width, keyboard.measuredHeight)
        val keyAt = keyboard.javaClass.getDeclaredMethod(
            "keyAtPoint", Float::class.javaPrimitiveType, Float::class.javaPrimitiveType)
        keyAt.isAccessible = true
        val y = 45f*density
        assertEquals(null, keyAt.invoke(keyboard, 0f, y))
        assertEquals(null, keyAt.invoke(keyboard, width.toFloat() - 1f, y))
        assertEquals(null, keyAt.invoke(keyboard, 30f*density, 0f))
        val first = keyAt.invoke(keyboard, 30f*density, y)
        assertTrue("Key under Q position was lost", first is io.github.ninjin0802.keyboard.app.layout.Key)
        val key = first as io.github.ninjin0802.keyboard.app.layout.Key
        assertEquals("q", key.label)
    }

    @Test fun englishPredictionsCanBeCommittedOnAndroid() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val connection = BaseInputConnection(View(ctx), true)
        val adapter = CoreInputAdapter { connection }
        MeltypeCoreSession.open(File(ctx.noBackupFilesDir, "bilingual-device-test"), CoreMozcCallbacks(converter)).use { core ->
            core.setCanDelete(true)
            for (char in "hell") {
                val response = core.handleKey(char.uppercaseChar().code, char.code,
                    before = connection.getTextBeforeCursor(50, 0)?.toString())
                    ?: error("Core returned null while collecting predictions")
                val consumed = adapter.apply(response)
                if (!consumed) connection.commitText(char.toString(), 1)
            }
            val option = adapter.choices.firstOrNull {
                it.kind == CoreChoiceKind.PREDICTION && it.label.startsWith("hell")
            }
            assertTrue("No English prediction from Native Core for hell: " + adapter.choices, option != null)
            val selected = core.selectPrediction(option!!.nativeIndex)
                ?: error("Native prediction-selection function missing")
            adapter.apply(selected)
            assertEquals(option.label, connection.getEditable().toString())
        }
    }

    @Test fun translucentKeyboardHasOnlyTopRoundedHairline() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val density = context.resources.displayMetrics.density
        val w = (360 * density).toInt()
        val h = (30 * density).toInt().coerceAtLeast(32)
        val decoration = io.github.ninjin0802.keyboard.app.view.KeyboardTopEdgeHighlight(context)
        decoration.measure(
            View.MeasureSpec.makeMeasureSpec(w, View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec(h, View.MeasureSpec.EXACTLY),
        )
        decoration.layout(0, 0, w, h)
        val bitmap = android.graphics.Bitmap.createBitmap(w, h, android.graphics.Bitmap.Config.ARGB_8888)
        decoration.draw(android.graphics.Canvas(bitmap))
        val atTop = bitmap.getPixel(w / 2, (density * 0.5f).toInt())
        val belowEdge = bitmap.getPixel(w / 2, (density * 10f).toInt())
        assertTrue("Top edge should have a translucent white hairline",
            android.graphics.Color.alpha(atTop) > 0)
        assertEquals("Hairline must not fill the keyboard surface",
            0, android.graphics.Color.alpha(belowEdge))
        assertTrue("Decoration cannot intercept keyboard input", !decoration.isClickable)
        assertTrue("Decoration must not capture focus", !decoration.isFocusable)
        bitmap.recycle()
    }
}
