package io.github.ninjin0802.keyboard.app.core

import android.content.Context
import android.view.View
import android.view.inputmethod.BaseInputConnection
import androidx.test.platform.app.InstrumentationRegistry
import io.github.ninjin0802.keyboard.app.layout.withoutEmoji
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Device-level integration: NativeAOT/JNI -> original Meltype.Core language
 * detector -> CoreInputAdapter -> Android InputConnection.
 * A successful build alone is NOT a successful bilingual regression test.
 */
class LanguageDetectionDeviceTest {
    @Test fun freshSettingsDefaultToRomajiHalfWidthAndNoEmoji() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val prefs = ctx.getSharedPreferences("settings", Context.MODE_PRIVATE)
        val keys = listOf("japanese_layout", "japanese_space_full_width", "emoji_enabled")
        val original = prefs.all.toMap()
        try {
            val clear = prefs.edit()
            keys.forEach(clear::remove)
            check(clear.commit())
            val settings = io.github.ninjin0802.keyboard.app.Settings(ctx)
            assertEquals(io.github.ninjin0802.keyboard.app.layout.LayoutId.ROMAJI, settings.japaneseLayout)
            assertEquals(false, settings.japaneseSpaceFullWidth)
            assertEquals(false, settings.emojiEnabled)

            settings.japaneseLayout = io.github.ninjin0802.keyboard.app.layout.LayoutId.KANA
            settings.japaneseSpaceFullWidth = true
            settings.emojiEnabled = true
            val saved = io.github.ninjin0802.keyboard.app.Settings(ctx)
            assertEquals(io.github.ninjin0802.keyboard.app.layout.LayoutId.KANA, saved.japaneseLayout)
            assertEquals(true, saved.japaneseSpaceFullWidth)
            assertEquals(true, saved.emojiEnabled)
        } finally {
            val restore = prefs.edit()
            keys.forEach { key ->
                when (val value = original[key]) {
                    null -> restore.remove(key)
                    is String -> restore.putString(key, value)
                    is Boolean -> restore.putBoolean(key, value)
                    else -> error("Unexpected setting type for $key")
                }
            }
            check(restore.commit())
        }
    }

    /**
     * Sends events to the native Core the way KeyboardService does and applies the
     * responses to a real InputConnection: a key the Core passes is typed by the "app".
     */
    private inner class CoreTypist : AutoCloseable {
        private val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val connection = BaseInputConnection(View(ctx), true)
        val adapter = CoreInputAdapter { connection }
        private val core = DeviceTestCore.open()

        fun send(event: String): Boolean =
            adapter.apply(core.handle(event) ?: error("Native Core returned null for $event"))

        fun field() = adapter.field(emptyList())

        /** Types [text]; characters the Core passes reach the editor as the app would insert them. */
        fun type(text: String) {
            for (char in text) {
                val consumed = send(ImeJson.characterKey(char.code, false, field()))
                if (!consumed) connection.commitText(char.toString(), 1)
            }
        }

        fun key(vk: Int): Boolean = send(ImeJson.virtualKey(vk, field()))

        fun commit() {
            send(ImeJson.commitRequested(field()))
            connection.finishComposingText()
        }

        val text: String get() = connection.getEditable().toString()

        override fun close() = core.close()
    }

    private fun type(text: String): String = CoreTypist().use { typist ->
        typist.type(text)
        typist.commit()
        println("MELTYPE_BILINGUAL_RESULT input=$text actual=${typist.text}")
        typist.text
    }

    @Test fun englishStaysLatin() {
        assertEquals("hello", type("hello"))
        assertEquals("meeting", type("meeting"))
    }

    @Test fun japaneseRomajiBecomesKana() {
        assertEquals("ありがとう", type("arigatou"))
        // Core reads "nn" as ん, as on the desktop (CompositionTests: tanni → たんい,
        // konnichiwq → こんいちwq).
        assertEquals("こんいちわ", type("konnichiwa"))
    }

    @Test fun mixedLanguagesWithoutManualToggle() {
        assertEquals("hello ありがとう", type("hello arigatou"))
    }

    /** A whole response as the Core writes it (ImeOutput), with only the view varying. */
    private fun response(view: String) =
        """{"version":1,"state":{},"result":{"consumed":true,"commits":[],"view":$view}}"""

    private fun viewOf(view: String) = ImeJson.parse(response(view)).view

    @Test fun choicesKeepTheCoreIndicesForClicks() {
        val typing = viewOf(
            """{"text":"hel","converting":false,"clauses":[],"selectedClause":0,"hint":"","spellingSuggestion":null,
            "typedKeys":null,"candidates":{"items":["hello","help"],"notes":[null,null],"numbered":false,"selected":-1,
            "selectedMeaning":null,"meaningDelayMs":1500}}"""
        )
        val (choices, selected) = CoreCandidateChoices.fromView(typing)
        assertEquals(null, selected)
        assertEquals(listOf("hello", "help"), choices.filter { it.kind == CoreChoiceKind.CORE }.map { it.label })
        assertEquals(listOf(0, 1), choices.filter { it.kind == CoreChoiceKind.CORE }.map { it.nativeIndex })
        val converted = viewOf(
            """{"text":"こんにちは","converting":true,"clauses":["こんにちは"],"selectedClause":0,"hint":"",
            "spellingSuggestion":null,"typedKeys":null,"candidates":{"items":["今日は","こんにちは"],"notes":[null,null],
            "numbered":true,"selected":1,"selectedMeaning":null,"meaningDelayMs":1500}}"""
        )
        val (convertedChoices, selection) = CoreCandidateChoices.fromView(converted)
        assertEquals(1, selection)
        assertEquals("こんにちは", convertedChoices[1].label)
        assertEquals(1, convertedChoices[1].nativeIndex)
        assertTrue("No fixed choices while converting", convertedChoices.all { it.kind == CoreChoiceKind.CORE })
    }

    @Test fun fixedCorrectionsAreOfferedWhileTyping() {
        val typing = viewOf(
            """{"text":"日本語","converting":false,"clauses":[],"selectedClause":0,"hint":"","spellingSuggestion":null,
            "typedKeys":null,"candidates":null}"""
        )
        val (choices, _) = CoreCandidateChoices.fromView(typing)
        assertEquals(
            listOf(CoreChoiceKind.TO_HIRAGANA to 0x75, CoreChoiceKind.TO_KATAKANA to 0x76, CoreChoiceKind.TO_LATIN to 0x79),
            choices.map { it.kind to it.kind.vk },
        )
        assertEquals(null, CoreCandidateChoices.fromView(null).second)
        assertTrue(CoreCandidateChoices.fromView(null).first.isEmpty())
    }

    @Test fun emojiPreferenceFiltersPredictionsWithoutShiftingIndices() {
        val typing = viewOf(
            """{"text":"ありがとう","converting":false,"clauses":[],"selectedClause":0,"hint":"","spellingSuggestion":null,
            "typedKeys":null,"candidates":{"items":["ありがとう😊","ありがとうございます"],"notes":[null,null],
            "numbered":false,"selected":-1,"selectedMeaning":null,"meaningDelayMs":1500}}"""
        )
        val enabled = CoreCandidateChoices.fromView(typing, allowEmoji = true).first
        val disabled = CoreCandidateChoices.fromView(typing, allowEmoji = false).first
        assertTrue(enabled.any { it.label == "ありがとう😊" })
        assertTrue(disabled.none { it.label == "ありがとう😊" })
        // The Core still numbers the hidden emoji as 0, so the click must send 1.
        assertEquals(1, disabled.single { it.label == "ありがとうございます" }.nativeIndex)
    }

    @Test fun malformedResponseLeavesTheEditorAlone() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val connection = BaseInputConnection(View(ctx), true)
        connection.commitText("keep", 1)
        val adapter = CoreInputAdapter { connection }
        val broken = """{"version":1,"state":{},"result":{"consumed":true,"commits":[{"text":"x"}],"view":null}}"""
        assertTrue(runCatching { adapter.apply(broken) }.isFailure)
        assertEquals("keep", connection.getEditable().toString())
    }

    @Test fun removeBeforeReplacesTheTextBeforeTheCaret() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val connection = BaseInputConnection(View(ctx), true)
        connection.commitText("今日は", 1)
        val adapter = CoreInputAdapter { connection }
        adapter.apply(
            """{"version":1,"state":{},"result":{"consumed":true,"commits":[{"text":"京は","removeBefore":"今日は",
            "currentWord":null,"moveCaretBack":0,"insertByPaste":false}],"view":null}}"""
        )
        assertEquals("京は", connection.getEditable().toString())
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
        CoreTypist().use { typist ->
            typist.type("nihongo")
            assertTrue("Japanese romaji should be composing", typist.adapter.composing)
            val latin = typist.adapter.choices.single { it.kind == CoreChoiceKind.TO_LATIN }
            typist.key(latin.kind.vk) // F10: original Latin spelling
            typist.key(0x0D) // Enter: commit
            assertEquals("nihongo", typist.text)
        }
    }

    @Test fun composingTextIsNotSentAsTextBeforeTheCaret() {
        CoreTypist().use { typist ->
            typist.connection.commitText("abc ", 1)
            typist.type("ari")
            assertTrue(typist.adapter.composing)
            assertEquals("abc ", typist.field().before)
        }
    }

    @Test fun digitsStayInCompositionUntilExplicitEnter() {
        CoreTypist().use { typist ->
            for (ch in "arigatou") {
                assertTrue("Kana romaji must enter composition for $ch",
                    typist.send(ImeJson.characterKey(ch.code, false, typist.field())))
            }
            assertTrue("Japanese composition disappeared before keypad digits", typist.adapter.composing)
            for (digit in "123") {
                assertTrue("Numpad digit $digit should be consumed as composition",
                    typist.send(ImeJson.characterKey(digit.code, false, typist.field())))
                assertTrue("Numpad digit $digit unexpectedly committed text", typist.adapter.composing)
            }
            typist.key(0x0D)
            assertTrue("Enter should terminate composition", !typist.adapter.composing)
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
        CoreTypist().use { typist ->
            typist.type("hell")
            val option = typist.adapter.choices.firstOrNull {
                it.kind == CoreChoiceKind.CORE && it.label.startsWith("hell")
            }
            assertTrue("No English prediction from Native Core for hell: " + typist.adapter.choices, option != null)
            // A prediction is committed by the click itself (no Enter).
            typist.send(ImeJson.candidateClicked(option!!.nativeIndex, typist.field()))
            assertEquals(option.label, typist.text)
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
