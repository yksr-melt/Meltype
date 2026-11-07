// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app

import android.app.Activity
import android.content.Intent
import android.graphics.Insets
import android.os.Build
import android.view.KeyEvent
import android.view.WindowInsets
import android.view.View
import android.view.ViewGroup
import android.widget.Switch
import android.widget.TextView
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** On-device checks for the settings Activity, not only for persisted values. */
class SettingsScreenDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()

    private fun open(): Activity {
        val context = instrumentation.targetContext
        val intent = Intent(context, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        return instrumentation.startActivitySync(intent)
    }

    private fun findText(view: View, expected: String): TextView? {
        if (view is TextView && view.text.toString() == expected) return view
        if (view is ViewGroup) {
            for (index in 0 until view.childCount) {
                findText(view.getChildAt(index), expected)?.let { return it }
            }
        }
        return null
    }

    private fun allSwitches(view: View): List<Switch> {
        if (view is Switch) return listOf(view)
        if (view !is ViewGroup) return emptyList()
        return (0 until view.childCount).flatMap { allSwitches(view.getChildAt(it)) }
    }

    @Test fun homeHasGboardStyleCategoriesWithoutDemoOrCompletedSteps() {
        val activity = open()
        try {
            instrumentation.runOnMainSync {
                val root = activity.window.decorView
                assertNotNull(findText(root, "言語"))
                assertNotNull(findText(root, "設定"))
                assertNotNull(findText(root, "修正と候補"))
                assertNotNull(findText(root, "絵文字"))
                assertFalse(findText(root, "試し書き") != null)
                assertFalse(findText(root, "済み") != null)
            }
        } finally {
            instrumentation.runOnMainSync { activity.finish() }
        }
    }

    @Test fun liveConversionSwitchChangesAndPersistsItsValue() {
        val settings = Settings(instrumentation.targetContext)
        val previous = settings.liveConversionEnabled
        val activity = open()
        try {
            instrumentation.runOnMainSync {
                val button = findText(activity.window.decorView, "修正と候補")
                assertNotNull(button)
                // The title sits inside a vertical labels column.
                val row = (button!!.parent as View).parent as View
                assertTrue(row.hasOnClickListeners())
                row.performClick()
            }
            instrumentation.waitForIdleSync()
            instrumentation.runOnMainSync {
                assertNotNull(findText(activity.window.decorView, "ライブ変換"))
                val toggles = allSwitches(activity.window.decorView)
                assertEquals(1, toggles.size)
                assertEquals(previous, toggles[0].isChecked)
                toggles[0].isChecked = !previous
                assertEquals(!previous, Settings(activity).liveConversionEnabled)
                toggles[0].isChecked = previous
                assertEquals(previous, settings.liveConversionEnabled)
            }
        } finally {
            settings.liveConversionEnabled = previous
            instrumentation.runOnMainSync { activity.finish() }
        }
    }

    @Test fun systemBarsAreRespectedOnHomeAndDetailScreens() {
        val activity = open()
        try {
            // Explicit synthetic insets make this regression reproducible on
            // pre-edge-to-edge API29 as well as Android 15+ devices.
            val insets = WindowInsets.Builder()
                .setSystemWindowInsets(Insets.of(0, 68, 0, 28))
                .build()
            fun assertProtected() {
                val root = activity.findViewById<ViewGroup>(android.R.id.content).getChildAt(0)
                if (Build.VERSION.SDK_INT >= 35) {
                    // Check the real Android 15+ inset as well: the screenshot
                    // regression occurred because the title was under the clock.
                    val statusTop = root.rootWindowInsets?.getInsets(
                        WindowInsets.Type.statusBars())?.top ?: 0
                    assertTrue("Emulator must expose a visible status bar", statusTop > 0)
                    assertTrue("Settings must start below the actual OS clock",
                        root.paddingTop >= statusTop)
                }
                root.dispatchApplyWindowInsets(insets)
                assertTrue("Settings must clear the status bar", root.paddingTop >= 68)
                assertTrue("Settings must clear the navigation bar", root.paddingBottom >= 28)
            }
            instrumentation.runOnMainSync {
                assertProtected()
                val languages = findText(activity.window.decorView, "言語")
                assertNotNull(languages)
                ((languages!!.parent as View).parent as View).performClick()
                assertProtected()
                assertNotNull(findText(activity.window.decorView, "入力レイアウト"))
            }
        } finally {
            instrumentation.runOnMainSync { activity.finish() }
        }
    }

    @Test fun toolbarBackAndSystemBackBothReturnToSettingsHome() {
        val activity = open()
        try {
            fun openLanguages() {
                instrumentation.runOnMainSync {
                    val languages = findText(activity.window.decorView, "言語")
                    assertNotNull(languages)
                    ((languages!!.parent as View).parent as View).performClick()
                }
                instrumentation.waitForIdleSync()
            }
            openLanguages()
            instrumentation.runOnMainSync {
                assertNotNull(findText(activity.window.decorView, "入力レイアウト"))
                val back = findText(activity.window.decorView, "‹")
                assertNotNull("Visible back control must exist on detail screen", back)
                back!!.performClick()
            }
            instrumentation.waitForIdleSync()
            instrumentation.runOnMainSync {
                assertNotNull("Toolbar Back must navigate home",
                    findText(activity.window.decorView, "Meltype の設定"))
            }
            openLanguages()
            instrumentation.sendKeyDownUpSync(KeyEvent.KEYCODE_BACK)
            instrumentation.waitForIdleSync()
            instrumentation.runOnMainSync {
                assertNotNull("Android system Back must navigate home",
                    findText(activity.window.decorView, "Meltype の設定"))
                assertFalse("Back from a detail page should not close the Activity",
                    activity.isFinishing)
            }
        } finally {
            instrumentation.runOnMainSync { activity.finish() }
        }
    }

    @Test fun unfoldedScreenKeepsCategoriesBesideActiveSettings() {
        val activity = open()
        try {
            instrumentation.runOnMainSync {
                val width = activity.resources.configuration.screenWidthDp
                val container = activity.findViewById<ViewGroup>(android.R.id.content)
                val displayed = container.getChildAt(0)
                if (width >= 700) {
                    val split = displayed as? android.widget.LinearLayout
                    assertNotNull("Expected an unfolded two-pane layout", split)
                    assertEquals(android.widget.LinearLayout.HORIZONTAL, split!!.orientation)
                    assertEquals(3, split.childCount) // Left pane, divider, right pane
                    assertNotNull(findText(split, "言語"))
                    assertNotNull(findText(split, "修正と候補"))
                    assertNotNull(findText(split, "日本語入力時のスペース"))
                } else {
                    assertTrue(displayed is android.widget.ScrollView)
                }
            }
        } finally {
            instrumentation.runOnMainSync { activity.finish() }
        }
    }
}
