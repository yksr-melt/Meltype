// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app

import android.content.Intent
import android.os.SystemClock
import android.provider.Settings
import android.view.MotionEvent
import android.view.InputDevice
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
                val service = KeyboardService.activeForSmokeTest?.get()
                service?.activeKeysForSmokeTest()?.let {
                    it.isAttachedToWindow && it.isShown && it.width > 100 && it.height > 100
                } == true
            }
            val service = KeyboardService.activeForSmokeTest?.get()
            assertNotNull("Meltype was not instantiated by the Android IME manager", service)
            val view = service!!.activeKeysForSmokeTest()
            assertNotNull("Meltype key surface was not displayed", view)
            val keyboard = view!!
            assertEquals("A real email editor should select the English QWERTY keyboard",
                io.github.ninjin0802.keyboard.app.layout.LayoutId.QWERTY, keyboard.layout.id)

            for (letter in listOf("a", "s", "d")) {
                val key = keyboard.layout.keys.singleOrNull {
                    (it.action as? KeyAction.Text)?.text == letter
                }
                assertNotNull("No on-screen key for $letter", key)
                val location = IntArray(2)
                instrumentation.runOnMainSync { keyboard.getLocationOnScreen(location) }
                val chosen = key!!
                val x = location[0] +
                    keyboard.width * ((chosen.col + chosen.width / 2f) / keyboard.layout.columns)
                val y = location[1] +
                    keyboard.height * ((chosen.row + 0.5f) / keyboard.layout.rows)
                val clock = SystemClock.uptimeMillis()
                val down = MotionEvent.obtain(clock, clock, MotionEvent.ACTION_DOWN, x, y, 0)
                val up = MotionEvent.obtain(clock, clock + 55, MotionEvent.ACTION_UP, x, y, 0)
                down.source = InputDevice.SOURCE_TOUCHSCREEN
                up.source = InputDevice.SOURCE_TOUCHSCREEN
                try {
                    assertTrue("Touch DOWN not delivered to IME", instrumentation.uiAutomation
                        .injectInputEvent(down, true))
                    assertTrue("Touch UP not delivered to IME", instrumentation.uiAutomation
                        .injectInputEvent(up, true))
                } finally {
                    down.recycle()
                    up.recycle()
                }
                waitUntil("key $letter must enter the app's EditText") {
                    activity.editor.text.toString().length >= letter.length +
                        listOf("a", "s", "d").indexOf(letter)
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
            waitUntil("Japanese text field opens the Japanese keyboard") {
                val id = KeyboardService.activeForSmokeTest?.get()
                    ?.activeKeysForSmokeTest()?.layout?.id
                id == io.github.ninjin0802.keyboard.app.layout.LayoutId.ROMAJI ||
                    id == io.github.ninjin0802.keyboard.app.layout.LayoutId.KANA
            }
            val japanese = KeyboardService.activeForSmokeTest?.get()
                ?.activeKeysForSmokeTest() ?: throw AssertionError("Japanese keys not shown")
            val key = japanese.layout.keys.firstOrNull {
                when (val action = it.action) {
                    is KeyAction.Romaji -> action.char == 'a'
                    is KeyAction.Flick -> action.key.chars.firstOrNull() == 'あ'
                    else -> false
                }
            } ?: throw AssertionError("No Japanese あ/A soft key")
            val location = IntArray(2)
            instrumentation.runOnMainSync { japanese.getLocationOnScreen(location) }
            val x = location[0] +
                japanese.width * ((key.col + key.width / 2f) / japanese.layout.columns)
            val y = location[1] +
                japanese.height * ((key.row + 0.5f) / japanese.layout.rows)
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
        } finally {
            instrumentation.runOnMainSync { activity.finish() }
        }
    }
}
