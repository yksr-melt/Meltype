// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app

import android.content.Intent
import android.os.SystemClock
import android.provider.Settings
import android.view.MotionEvent
import android.view.InputDevice
import android.view.View
import android.view.ViewGroup
import io.github.ninjin0802.keyboard.app.input.Direction
import android.view.inputmethod.InputMethodManager
import androidx.test.platform.app.InstrumentationRegistry
import io.github.ninjin0802.keyboard.app.layout.KeyAction
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * End-to-end IME gate, not just an isolated KeyboardView drawing test.
 *
 * The emulator runner first enables and SELECTS the exact test package's IME.
 * This test then focuses a real EditText, waits until the system has created
 * the real InputMethodService window, and injects actual screen touches into
 * three rendered keys. Success requires the EditText to contain "asd".
 */
class ImeLaunchDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val target = instrumentation.targetContext

    private fun waitUntil(
        label: String,
        millis: Long = 15_000,
        retryEveryMillis: Long = 0,
        retry: () -> Unit = {},
        describe: () -> String = { "" },
        check: () -> Boolean,
    ) {
        val deadline = SystemClock.elapsedRealtime() + millis
        var nextRetry = SystemClock.elapsedRealtime() + retryEveryMillis
        while (SystemClock.elapsedRealtime() < deadline) {
            instrumentation.waitForIdleSync()
            if (check()) return
            if (retryEveryMillis > 0 && SystemClock.elapsedRealtime() >= nextRetry) {
                retry()
                nextRetry = SystemClock.elapsedRealtime() + retryEveryMillis
            }
            Thread.sleep(200)
        }
        throw AssertionError("IME smoke timed out: $label ${describe()}".trim())
    }

    /**
     * Asks Android to show the keyboard for [editor].
     *
     * A single request is not enough. Android drops it when the window has
     * not gained focus yet, and starting instrumentation restarts the app
     * process that hosts the IME, so the first request can arrive while the
     * system is still reconnecting to the keyboard service. restartInput
     * makes the system bind the service again instead of waiting for it.
     */
    private fun requestKeyboard(activity: ImeSmokeActivity, editor: android.widget.EditText) {
        instrumentation.runOnMainSync {
            if (!activity.hasWindowFocus()) return@runOnMainSync
            val imm = activity.getSystemService(android.content.Context.INPUT_METHOD_SERVICE)
                as InputMethodManager
            editor.requestFocus()
            imm.restartInput(editor)
            imm.showSoftInput(editor, 0)
        }
    }

    private fun describeImeState(activity: ImeSmokeActivity): String {
        val service = KeyboardService.activeForSmokeTest?.get()
        val keys = service?.activeKeysForSmokeTest()
        val defaultIme = Settings.Secure.getString(
            target.contentResolver, Settings.Secure.DEFAULT_INPUT_METHOD)
        val size = if (keys == null) "none" else keys.width.toString() + "x" + keys.height
        return "(windowFocus=" + activity.hasWindowFocus() +
            ", editorFocus=" + activity.editor.hasFocus() +
            ", defaultIme=" + defaultIme +
            ", serviceCreated=" + (service != null) +
            ", inputViewShown=" + service?.isInputViewShown +
            ", keys=" + size + ")"
    }

    /** A drawn IME window is not enough: it can outlive the editor's input binding. */
    private fun keyboardReadyFor(
        activity: ImeSmokeActivity,
        editor: android.widget.EditText,
        layout: io.github.ninjin0802.keyboard.app.layout.LayoutId,
    ): Boolean {
        val service = KeyboardService.activeForSmokeTest?.get() ?: return false
        val keys = service.activeKeysForSmokeTest() ?: return false
        val imm = activity.getSystemService(android.content.Context.INPUT_METHOD_SERVICE)
            as InputMethodManager
        return activity.hasWindowFocus() && editor.hasFocus() && imm.isActive(editor) &&
            service.isInputViewShown && service.currentInputConnection != null &&
            (editor.id == View.NO_ID || service.currentInputEditorInfo?.fieldId == editor.id) &&
            keys.isAttachedToWindow && keys.isShown && keys.width > 100 && keys.height > 100 &&
            keys.layout.id == layout
    }

    /** Touches the on-screen key that [matches], as a finger would. */
    private fun tap(keyboard: io.github.ninjin0802.keyboard.app.view.KeyboardView, label: String, matches: (KeyAction) -> Boolean) {
        val key = keyboard.layout.keys.firstOrNull { matches(it.action) } ?: throw AssertionError("No on-screen key for $label")
        val location = IntArray(2)
        val coords = FloatArray(2)
        instrumentation.runOnMainSync {
            assertTrue("IME key view is not attached and visible", keyboard.isAttachedToWindow && keyboard.isShown)
            keyboard.getLocationOnScreen(location)
            val center = keyboard.keyCenterForSmokeTest(key)
            coords[0] = location[0] + center.first
            coords[1] = location[1] + center.second
        }
        val x = coords[0]
        val y = coords[1]
        val clock = SystemClock.uptimeMillis()
        val down = MotionEvent.obtain(clock, clock, MotionEvent.ACTION_DOWN, x, y, 0)
        val up = MotionEvent.obtain(clock, clock + 55, MotionEvent.ACTION_UP, x, y, 0)
        down.source = InputDevice.SOURCE_TOUCHSCREEN
        up.source = InputDevice.SOURCE_TOUCHSCREEN
        try {
            assertTrue("Touch DOWN not delivered for $label", instrumentation.uiAutomation.injectInputEvent(down, true))
            assertTrue("Touch UP not delivered for $label", instrumentation.uiAutomation.injectInputEvent(up, true))
        } finally {
            down.recycle()
            up.recycle()
        }
        instrumentation.waitForIdleSync()
        // The editor and the keyboard exchange the edit and the new selection asynchronously.
        Thread.sleep(150)
        instrumentation.waitForIdleSync()
    }

    /** Types [keys] on the Japanese QWERTY (romaji) keyboard: letters, ' ' for Space and '\n' for Enter. */
    private fun typeRomaji(keys: String) {
        val keyboard = KeyboardService.activeForSmokeTest?.get()?.activeKeysForSmokeTest()
            ?: throw AssertionError("Keys not shown")
        assertEquals(io.github.ninjin0802.keyboard.app.layout.LayoutId.ROMAJI, keyboard.layout.id)
        for (key in keys) when (key) {
            ' ' -> tap(keyboard, "Space") { it == KeyAction.Space }
            '\n' -> tap(keyboard, "Enter") { it == KeyAction.Enter }
            else -> tap(keyboard, key.toString()) { it is KeyAction.Romaji && it.char == key }
        }
    }

    private fun outsideCaretMoves(): Int {
        var count = -1
        instrumentation.runOnMainSync {
            count = KeyboardService.activeForSmokeTest?.get()?.outsideCaretMovesForSmokeTest() ?: -1
        }
        return count
    }

    /** Focuses [editor] and waits until the Japanese QWERTY keyboard is shown for it. */
    private fun focusJapanese(activity: ImeSmokeActivity, editor: android.widget.EditText) {
        requestKeyboard(activity, editor)
        waitUntil(
            "Japanese QWERTY keyboard for the editor",
            millis = 45_000,
            retryEveryMillis = 2_000,
            retry = { requestKeyboard(activity, editor) },
            describe = { describeImeState(activity) },
        ) {
            keyboardReadyFor(activity, editor,
                io.github.ninjin0802.keyboard.app.layout.LayoutId.ROMAJI)
        }
    }

    private fun findImePicker(view: View): View? {
        if (view.contentDescription == "別のキーボードに切り替える") return view
        if (view is ViewGroup) {
            for (i in 0 until view.childCount) {
                findImePicker(view.getChildAt(i))?.let { return it }
            }
        }
        return null
    }

    /** Normal typing shows no extra switch button; recovery offers direct system IME selection. */
    @Test fun keyboardProvidesImePickerOnlyInEmergency() = withSmokeActivity { activity ->
        focusJapanese(activity, activity.japaneseEditor)
        val service = KeyboardService.activeForSmokeTest?.get()
            ?: throw AssertionError("Keyboard service missing")
        instrumentation.runOnMainSync {
            val root = service.window?.window?.decorView
                ?: throw AssertionError("IME window not available")
            assertTrue("Normal keyboard must not add a custom IME switch button",
                findImePicker(root) == null)

            val emergency = service.emergencyKeyboardForSmokeTest()
            val picker = findImePicker(emergency)
            assertNotNull("Emergency view must offer direct system IME selection", picker)
            assertTrue("Emergency picker must be clickable", picker!!.isClickable)

            fun containsSettingsLink(v: View): Boolean =
                (v is android.widget.TextView && v.text.contains("キーボード設定")) ||
                    (v is ViewGroup && (0 until v.childCount).any { containsSettingsLink(v.getChildAt(it)) })
            assertTrue("Recovery must not send the user to a settings screen",
                !containsSettingsLink(emergency))
        }
    }

    /** The Core does not handle an email editor (Email trait): it shows English QWERTY even after あa, and keys go in as typed. */
    @Test fun emailFieldTypesKeysAsTheyAre() = withSmokeActivity { activity ->
        requestKeyboard(activity, activity.editor)
        waitUntil(
            "English QWERTY in email editor", millis = 45_000,
            retryEveryMillis = 2_000, retry = { requestKeyboard(activity, activity.editor) },
            describe = { describeImeState(activity) },
        ) {
            keyboardReadyFor(activity, activity.editor,
                io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY)
        }
        // あa sends ModeToggleRequested, which the Core does not take here. Avoid depending on long-press gesture timing.
        val service = KeyboardService.activeForSmokeTest?.get()
            ?: throw AssertionError("Keyboard service missing")
        instrumentation.runOnMainSync {
            service.onKey(KeyAction.Language, Direction.TAP, SystemClock.uptimeMillis())
        }
        instrumentation.waitForIdleSync()
        assertTrue("English QWERTY must stay after あa: " + describeImeState(activity),
            keyboardReadyFor(activity, activity.editor, io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY))
        val keyboard = service.activeKeysForSmokeTest() ?: throw AssertionError("Keys not shown")
        tap(keyboard, "a") { it == KeyAction.Text("a") }
        waitUntil("the key goes in as typed in the email editor",
            describe = { describeImeState(activity) + ", text=" + activity.editor.text }) {
            activity.editor.text.toString() == "a"
        }
        assertEquals("a", activity.editor.text.toString())
    }

    /** Runs [block] in the smoke-test activity. */
    private fun withSmokeActivity(block: (ImeSmokeActivity) -> Unit) {
        val activity = instrumentation.startActivitySync(
            Intent(target, ImeSmokeActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        ) as ImeSmokeActivity
        try {
            block(activity)
        } finally {
            instrumentation.runOnMainSync { activity.finish() }
        }
    }

    /** Empties [editor] (the app restarts the input) and waits until the keyboard has settled. */
    private fun clear(editor: android.widget.EditText) {
        instrumentation.runOnMainSync { editor.setText("") }
        instrumentation.waitForIdleSync()
        Thread.sleep(500)
        instrumentation.waitForIdleSync()
    }

    /** Types i, Space (convert) and Enter (commit): the Core may re-commit that word once the next one shows the language. */
    private fun commitI(editor: android.widget.EditText): String {
        typeRomaji("i \n")
        waitUntil("i is converted and committed", describe = { "text=" + editor.text }) {
            editor.text.isNotEmpty() && android.view.inputmethod.BaseInputConnection.getComposingSpanStart(editor.text) < 0
        }
        return editor.text.toString()
    }

    /**
     * The keyboard's own edits are not caret moves: i + want re-commits i as I. A caret move by
     * the app is one: the Core is told and leaves the word before the caret alone.
     */
    @Test fun ownEditorWritesDoNotLookLikeCaretMoves() = withSmokeActivity { activity ->
        val editor = activity.japaneseEditor
        focusJapanese(activity, editor)
        clear(editor)
        // setText can restart an editor. Check that the actual keyboard still has Japanese focus.
        focusJapanese(activity, editor)
        val start = outsideCaretMoves()
        val committed = commitI(editor)
        typeRomaji("want ")
        waitUntil("want is committed", describe = { "text=" + editor.text }) { editor.text.endsWith("want ") }
        assertTrue("The previous word was not re-committed: " + editor.text,
            editor.text.toString() != committed + "want " && editor.text.startsWith("I"))
        assertEquals("Own edits were taken as caret moves", start, outsideCaretMoves())
    }

    @Test fun caretMovesByTheAppStopRecommit() = withSmokeActivity { activity ->
        val editor = activity.japaneseEditor
        focusJapanese(activity, editor)
        clear(editor)
        focusJapanese(activity, editor)
        val committed = commitI(editor)
        val beforeMoves = outsideCaretMoves()
        // The app moves the caret away and back: the Core must not rewrite the word it can see again.
        instrumentation.runOnMainSync { editor.setSelection(0) }
        waitUntil("the move to the start is seen", describe = { "moves=" + outsideCaretMoves() }) {
            outsideCaretMoves() > beforeMoves
        }
        instrumentation.runOnMainSync { editor.setSelection(editor.text.length) }
        waitUntil("the move back to the end is seen", describe = { "moves=" + outsideCaretMoves() }) {
            outsideCaretMoves() > beforeMoves + 1
        }
        typeRomaji("want ")
        waitUntil("want is typed after the word", describe = { "text=" + editor.text }) {
            editor.text.toString() == committed + "want "
        }
    }

    /** Sending from a button outside the IME must not keep or reinsert unsent composition. */
    @Test fun externalSendClearsCompositionWithoutReinsertingOldText() = withSmokeActivity { activity ->
        val editor = activity.japaneseEditor
        focusJapanese(activity, editor)
        clear(editor)
        focusJapanese(activity, editor)
        typeRomaji("ka")
        waitUntil("か before external Send", describe = { "text=" + editor.text }) {
            editor.text.toString() == "か" &&
                android.view.inputmethod.BaseInputConnection.getComposingSpanStart(editor.text) >= 0
        }
        instrumentation.runOnMainSync { activity.sendButton.performClick() }
        waitUntil("external Send must clear the editor and the Core composition",
            describe = { "text=" + editor.text + ", core=" + coreView() }) {
            editor.hasFocus() && editor.text.isEmpty() && coreView() == null
        }
        assertEquals("か", activity.lastSentMessage)
        // Continue in the very same editor without restarting the keyboard.
        typeRomaji("a")
        waitUntil("new input must not resurrect the previous message",
            describe = { "text=" + editor.text }) {
            editor.text.toString() == "あ"
        }
        assertEquals("あ", editor.text.toString())
    }

    /** Leaving an editor keeps the composing text there; the next editor starts without it. */
    @Test fun leavingAnEditorCommitsTheCompositionThere() = withSmokeActivity { activity ->
        val japanese = activity.japaneseEditor
        focusJapanese(activity, japanese)
        instrumentation.runOnMainSync { japanese.setText("") }
        // setText can restart editor input asynchronously; wait for Japanese focus again.
        focusJapanese(activity, japanese)
        typeRomaji("ka")
        waitUntil("か is composing", describe = { "text=" + japanese.text }) {
            japanese.text.toString() == "か" && android.view.inputmethod.BaseInputConnection.getComposingSpanStart(japanese.text) >= 0
        }
        requestKeyboard(activity, activity.editor)
        waitUntil("the other editor has the keyboard", describe = { describeImeState(activity) }) {
            activity.editor.hasFocus() &&
                KeyboardService.activeForSmokeTest?.get()?.activeKeysForSmokeTest()?.layout?.id ==
                io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY
        }
        assertEquals("か", japanese.text.toString())
        assertEquals("The composing text was not committed", -1,
            android.view.inputmethod.BaseInputConnection.getComposingSpanStart(japanese.text))

        focusJapanese(activity, japanese)
        instrumentation.runOnMainSync { japanese.setSelection(japanese.text.length) }
        instrumentation.waitForIdleSync()
        typeRomaji("a\n")
        waitUntil("あ is typed after か", describe = { "text=" + japanese.text }) {
            japanese.text.toString() == "かあ"
        }
    }

    private fun coreView(): io.github.ninjin0802.keyboard.app.core.ImeView? {
        var view: io.github.ninjin0802.keyboard.app.core.ImeView? = null
        instrumentation.runOnMainSync { view = KeyboardService.activeForSmokeTest?.get()?.coreViewForSmokeTest() }
        return view
    }

    /** Taps a candidate on the candidate bar other than [current], as a finger would, and returns it. */
    private fun tapCandidateOtherThan(current: String): String {
        var tapped: String? = null
        instrumentation.runOnMainSync {
            val bar = KeyboardService.activeForSmokeTest?.get()?.candidateBarForSmokeTest() ?: return@runOnMainSync
            val row = bar.getChildAt(0) as android.view.ViewGroup
            val item = (0 until row.childCount).map { row.getChildAt(it) as android.widget.TextView }
                .firstOrNull { it.text.isNotBlank() && it.text.toString() != current } ?: return@runOnMainSync
            tapped = item.text.toString()
            item.performClick()
        }
        instrumentation.waitForIdleSync()
        return tapped ?: throw AssertionError("No candidate to tap other than $current; view=" + coreView())
    }

    /**
     * With the real converter: Space selects the last clause (as on the desktop), a candidate tap chooses it for that
     * clause, the Space-bar cursor gesture moves to the previous clause, and Enter commits the whole sentence.
     */
    @Test fun candidateTapChoosesForTheClauseAndEnterCommits() = withSmokeActivity { activity ->
        val editor = activity.japaneseEditor
        focusJapanese(activity, editor)
        clear(editor)
        focusJapanese(activity, editor)
        typeRomaji("watashinonamaehanakanodesu ")
        waitUntil("the sentence is converted", describe = { "view=" + coreView() }) { coreView()?.converting == true }
        val view = coreView()!!
        assertTrue("The sentence was not split into clauses: $view", view.clauses.size >= 2)
        assertEquals("The last clause is not selected: $view", view.clauses.size - 1, view.selectedClause)
        val label = tapCandidateOtherThan(view.clauses.last())
        val expected = view.clauses.dropLast(1).joinToString("") + label
        waitUntil("the candidate is chosen for the last clause", describe = { "view=" + coreView() }) {
            coreView()?.text == expected
        }
        val service = KeyboardService.activeForSmokeTest?.get() ?: throw AssertionError("Keyboard service missing")
        instrumentation.runOnMainSync {
            assertTrue(service.onCursorModeStart())
            service.onCursorMove(-1)
        }
        waitUntil("the cursor gesture selects the previous clause", describe = { "view=" + coreView() }) {
            coreView()?.selectedClause == view.clauses.size - 2
        }
        instrumentation.runOnMainSync { service.onKey(KeyAction.Enter, Direction.TAP, SystemClock.uptimeMillis()) }
        waitUntil("the sentence is committed as chosen", describe = { "text=" + editor.text + " expected=" + expected }) {
            editor.text.toString() == expected &&
                android.view.inputmethod.BaseInputConnection.getComposingSpanStart(editor.text) < 0
        }
    }

    @Test fun selectedKeyboardOpensAndTypesThroughOnscreenTouch() {
        val expectedIme = target.packageName + "/" + KeyboardService::class.java.name
        assertEquals("The actual keyboard must be the system-selected default",
            expectedIme, Settings.Secure.getString(target.contentResolver,
                Settings.Secure.DEFAULT_INPUT_METHOD))
        val activity = instrumentation.startActivitySync(
            Intent(target, ImeSmokeActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        ) as ImeSmokeActivity
        try {
            requestKeyboard(activity, activity.editor)
            waitUntil(
                "IME service and the input window shown",
                millis = 45_000,
                retryEveryMillis = 2_000,
                retry = { requestKeyboard(activity, activity.editor) },
                describe = { describeImeState(activity) },
            ) {
                // The previous IME window can still be drawn after the app's input
                // binding has ended; wait for the actual focused editor connection.
                keyboardReadyFor(activity, activity.editor,
                    io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY)
            }
            val service = KeyboardService.activeForSmokeTest?.get()
            assertNotNull("Meltype was not instantiated by the Android IME manager", service)
            val view = service!!.activeKeysForSmokeTest()
            assertNotNull("Meltype key surface was not displayed", view)
            val keyboard = view!!
            assertEquals("A real email editor should select the English QWERTY keyboard",
                io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY, keyboard.layout.id)

            for ((index, letter) in listOf("a", "s", "d").withIndex()) {
                // The system may recreate the input view after an asynchronous restartInput.
                // Do not inject against coordinates cached from a detached IME window.
                waitUntil(
                    "visible English keys before $letter", millis = 45_000,
                    retryEveryMillis = 2_000, retry = { requestKeyboard(activity, activity.editor) },
                    describe = { describeImeState(activity) },
                ) {
                    keyboardReadyFor(activity, activity.editor,
                        io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY)
                }
                val currentKeys = KeyboardService.activeForSmokeTest?.get()?.activeKeysForSmokeTest()
                    ?: throw AssertionError("English keyboard detached before $letter")
                tap(currentKeys, letter) { it is KeyAction.Text && it.text == letter }
                waitUntil("key $letter must enter the app's EditText",
                    describe = { describeImeState(activity) + ", text=" + activity.editor.text }) {
                    activity.editor.text.toString().length >= index + 1
                }
            }
            waitUntil("the three visible keys must generate text in the editor") {
                activity.editor.text.toString() == "asd"
            }
            assertEquals("asd", activity.editor.text.toString())

            // Also exercise the normal Japanese-input route. A working email
            // keyboard alone could conceal a failing Japanese/native path.
            instrumentation.runOnMainSync {
                activity.japaneseEditor.requestFocus()
                (activity.getSystemService(android.content.Context.INPUT_METHOD_SERVICE)
                    as InputMethodManager).showSoftInput(
                        activity.japaneseEditor, InputMethodManager.SHOW_IMPLICIT)
            }
            waitUntil(
                "Japanese text field has a bound romaji keyboard", millis = 45_000,
                retryEveryMillis = 2_000,
                retry = { requestKeyboard(activity, activity.japaneseEditor) },
                describe = { describeImeState(activity) },
            ) {
                keyboardReadyFor(activity, activity.japaneseEditor,
                    io.github.ninjin0802.keyboard.app.layout.LayoutId.ROMAJI)
            }
            val japanese = KeyboardService.activeForSmokeTest?.get()
                ?.activeKeysForSmokeTest() ?: throw AssertionError("Japanese keys not shown")
            val key = japanese.layout.keys.firstOrNull {
                when (val action = it.action) {
                    is KeyAction.Romaji -> action.char == 'a'
                    else -> false
                }
            } ?: throw AssertionError("No Japanese あ/A soft key")
            val location = IntArray(2)
            val coords = FloatArray(2)
            instrumentation.runOnMainSync {
                assertTrue("Japanese IME key view is not attached", japanese.isAttachedToWindow && japanese.isShown)
                japanese.getLocationOnScreen(location)
                val center = japanese.keyCenterForSmokeTest(key)
                coords[0] = location[0] + center.first
                coords[1] = location[1] + center.second
            }
            val x = coords[0]
            val y = coords[1]
            val clock = SystemClock.uptimeMillis()
            val down = MotionEvent.obtain(clock, clock, MotionEvent.ACTION_DOWN, x, y, 0)
            val up = MotionEvent.obtain(clock, clock + 55, MotionEvent.ACTION_UP, x, y, 0)
            down.source = InputDevice.SOURCE_TOUCHSCREEN
            up.source = InputDevice.SOURCE_TOUCHSCREEN
            try {
                assertTrue(instrumentation.uiAutomation.injectInputEvent(down, true))
                assertTrue(instrumentation.uiAutomation.injectInputEvent(up, true))
            } finally {
                down.recycle()
                up.recycle()
            }
            waitUntil("Japanese key must write composing text into the editor") {
                activity.japaneseEditor.text.isNotEmpty()
            }
            // The Kotlin romaji path would also write text. Require that the key
            // really went through the native Core and that it did not fail.
            var native = false
            instrumentation.runOnMainSync {
                native = KeyboardService.activeForSmokeTest?.get()?.nativeCoreActiveForSmokeTest() == true
            }
            assertTrue("Japanese romaji did not go through the native Core", native)
        } finally {
            instrumentation.runOnMainSync { activity.finish() }
        }
    }
}
