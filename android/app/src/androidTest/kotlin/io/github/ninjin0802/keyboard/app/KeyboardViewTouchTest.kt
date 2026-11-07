// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import android.app.Activity
import android.content.Intent
import android.os.SystemClock
import android.view.InputDevice
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import androidx.test.platform.app.InstrumentationRegistry
import io.github.ninjin0802.keyboard.app.input.Direction
import io.github.ninjin0802.keyboard.app.layout.Key
import io.github.ninjin0802.keyboard.app.layout.KeyAction
import io.github.ninjin0802.keyboard.app.layout.KeyboardLayout
import io.github.ninjin0802.keyboard.app.layout.Layouts
import io.github.ninjin0802.keyboard.app.view.KeyListener
import io.github.ninjin0802.keyboard.app.view.KeyboardView
import io.github.ninjin0802.keyboard.engine.KanaKeys
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * キーの View に、指2本ぶんのタッチを直接渡す（adb では2本の指を送れない）。
 * 指を移したときの乗り換えと、キーの動作の中で配列が替わったときに、残りの指の押下を始めないことを確かめる。
 */
class KeyboardViewTouchTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val fired = ArrayList<Pair<KeyAction, Direction>>()
    private var cursorModeAsked = 0
    private var menuOpenedAt: Pair<Float, Float>? = null
    private var menuReleasedAt: Pair<Float, Float>? = null
    private var menuMoveCount = 0
    private var menuCancelCount = 0
    private lateinit var view: KeyboardView
    private var activity: Activity? = null
    private var downTime = 0L

    /** あa が押されたら、本体と同じように、キーの動作の中で配列を入れ替える。 */
    private var languageSwitchesTo: KeyboardLayout? = null

    private val listener = object : KeyListener {
        override fun onKey(action: KeyAction, direction: Direction, timeMs: Long) {
            fired += action to direction
            if (action == KeyAction.Language) languageSwitchesTo?.let { view.layout = it }
        }
        override fun onSpaceLanguageMenu(x: Float, y: Float): Boolean {
            menuOpenedAt = x to y
            return true
        }
        override fun onSpaceLanguageMenuMove(x: Float, y: Float) { menuMoveCount++ }
        override fun onSpaceLanguageMenuRelease(x: Float, y: Float) { menuReleasedAt = x to y }
        override fun onSpaceLanguageMenuCancel() { menuCancelCount++ }
        override fun onCursorModeStart(): Boolean { cursorModeAsked++; return true }
        override fun onCursorMove(steps: Int) = Unit
    }

    @After fun finish() {
        activity?.let { a -> instrumentation.runOnMainSync { a.finish() } }
    }

    private fun onMain(block: () -> Unit) = instrumentation.runOnMainSync(block)

    /** 窓に付けない View。大きさだけ決める。 */
    private fun detachedView() = onMain {
        view = KeyboardView(instrumentation.targetContext).also { it.listener = listener }
        view.measure(
            View.MeasureSpec.makeMeasureSpec(1000, View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED),
        )
        view.layout(0, 0, view.measuredWidth, view.measuredHeight)
    }

    /** 画面に出ている View（押し続けの繰り返しと長押しは、見えているあいだだけ働く）。 */
    private fun shownView() {
        val intent = Intent(instrumentation.targetContext, LicensesActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        val started = instrumentation.startActivitySync(intent)
        activity = started
        onMain {
            view = KeyboardView(started).also { it.listener = listener }
            started.addContentView(view, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
        val deadline = SystemClock.uptimeMillis() + 5000
        var ready = false
        while (!ready && SystemClock.uptimeMillis() < deadline) {
            instrumentation.waitForIdleSync()
            onMain { ready = view.isShown && view.width > 0 && view.height > 0 }
            if (!ready) Thread.sleep(50)
        }
        assertTrue("the view is on screen", ready)
    }

    private fun key(action: KeyAction): Key = view.layout.keys.first { it.action == action }

    /** [action] のキーの中心（View の中の px）。 */
    private fun center(action: KeyAction): Pair<Float, Float> {
        val k = key(action)
        val cellW = view.width / view.layout.columns
        val cellH = view.height.toFloat() / view.layout.rows
        return (k.col + k.width / 2) * cellW to (k.row + k.rowSpan / 2f) * cellH
    }

    /** 指 [ids] が [points] にあるときの出来事を1つ渡す。[index] は、触れた・離れた指の順番。 */
    private fun send(action: Int, index: Int, ids: List<Int>, points: List<Pair<Float, Float>>) = onMain {
        val now = SystemClock.uptimeMillis()
        if (action == MotionEvent.ACTION_DOWN) downTime = now
        val properties = Array(ids.size) { i ->
            MotionEvent.PointerProperties().apply { id = ids[i]; toolType = MotionEvent.TOOL_TYPE_FINGER }
        }
        val coords = Array(ids.size) { i ->
            MotionEvent.PointerCoords().apply { x = points[i].first; y = points[i].second; pressure = 1f; size = 1f }
        }
        val masked = action or (index shl MotionEvent.ACTION_POINTER_INDEX_SHIFT)
        val event = MotionEvent.obtain(
            downTime, now, masked, ids.size, properties, coords, 0, 0, 1f, 1f, 0, 0, InputDevice.SOURCE_TOUCHSCREEN, 0,
        )
        view.dispatchTouchEvent(event)
        event.recycle()
    }

    private fun tap(point: Pair<Float, Float>) {
        send(MotionEvent.ACTION_DOWN, 0, listOf(0), listOf(point))
        send(MotionEvent.ACTION_UP, 0, listOf(0), listOf(point))
    }

    private fun snapshot(): List<Pair<KeyAction, Direction>> {
        var copy = emptyList<Pair<KeyAction, Direction>>()
        onMain { copy = fired.toList() }
        return copy
    }

    private fun shownLayout(): KeyboardLayout {
        var layout: KeyboardLayout? = null
        onMain { layout = view.layout }
        return layout!!
    }

    private fun count(action: KeyAction) = snapshot().count { it.first == action }

    private val a = KeyAction.Flick(KanaKeys.A)
    private val ka = KeyAction.Flick(KanaKeys.KA)

    /** あ を押したまま か に触れ、あ、か の順に離す。それぞれ1回ずつ入る。 */
    @Test fun rollover_between_two_flick_keys_fires_each_once() {
        detachedView()
        val first = center(a)
        val second = center(ka)
        send(MotionEvent.ACTION_DOWN, 0, listOf(0), listOf(first))
        assertEquals(emptyList<Any>(), snapshot())
        send(MotionEvent.ACTION_POINTER_DOWN, 1, listOf(0, 1), listOf(first, second))
        assertEquals(listOf(a to Direction.TAP), snapshot())                       // 前のキーは、後の指が触れたときに入る
        send(MotionEvent.ACTION_MOVE, 0, listOf(0, 1), listOf(first, second))
        send(MotionEvent.ACTION_POINTER_UP, 0, listOf(0, 1), listOf(first, second))
        assertEquals(listOf(a to Direction.TAP), snapshot())                       // 先の指が離れても、もう一度は入らない
        send(MotionEvent.ACTION_UP, 0, listOf(1), listOf(second))
        assertEquals(listOf(a to Direction.TAP, ka to Direction.TAP), snapshot())
        // 逆の順（後の指を先に離す）でも同じ。
        onMain { fired.clear() }
        send(MotionEvent.ACTION_DOWN, 0, listOf(0), listOf(first))
        send(MotionEvent.ACTION_POINTER_DOWN, 1, listOf(0, 1), listOf(first, second))
        send(MotionEvent.ACTION_POINTER_UP, 1, listOf(0, 1), listOf(first, second))
        send(MotionEvent.ACTION_UP, 0, listOf(0), listOf(first))
        assertEquals(listOf(a to Direction.TAP, ka to Direction.TAP), snapshot())
    }

    /**
     * あa を押したまま、2本目の指が削除キーに触れる。あa は、その動作の中で配列を入れ替える。
     * 削除は1回も起きず、繰り返しも始まらない。指を全部離して触れ直すと、ふつうに働く。
     */
    private fun holdLanguageThenTouchDelete() {
        languageSwitchesTo = Layouts.latinFlick
        val language = center(KeyAction.Language)
        val delete = center(KeyAction.Delete)
        send(MotionEvent.ACTION_DOWN, 0, listOf(0), listOf(language))
        send(MotionEvent.ACTION_POINTER_DOWN, 1, listOf(0, 1), listOf(language, delete))
        assertEquals(listOf(KeyAction.Language to Direction.TAP), snapshot())
        assertEquals(Layouts.latinFlick, shownLayout())
        assertEquals(delete, center(KeyAction.Delete))                             // 新しい配列でも、指の下は削除キー
        // 繰り返しは 400 ms 後から 50 ms ごと。指を置いたまま、それより長く待つ。
        Thread.sleep(300)
        send(MotionEvent.ACTION_MOVE, 0, listOf(0, 1), listOf(language, delete))
        assertEquals(0, count(KeyAction.Delete))
        Thread.sleep(400)
        assertEquals(listOf(KeyAction.Language to Direction.TAP), snapshot())
        send(MotionEvent.ACTION_POINTER_UP, 0, listOf(0, 1), listOf(language, delete))
        send(MotionEvent.ACTION_UP, 0, listOf(1), listOf(delete))
        Thread.sleep(500)
        assertEquals(listOf(KeyAction.Language to Direction.TAP), snapshot())
        // 触れ直すと、削除は1回。
        languageSwitchesTo = null
        tap(delete)
        assertEquals(1, count(KeyAction.Delete))
        Thread.sleep(500)
        assertEquals(1, count(KeyAction.Delete))
        tap(center(KeyAction.Flick(view.layout.keys.firstNotNullOf { (it.action as? KeyAction.Flick)?.key })))
        assertEquals(3, snapshot().size)
    }

    @Test fun a_layout_switch_inside_the_first_keys_action_never_starts_delete() {
        detachedView()
        holdLanguageThenTouchDelete()
    }

    /** 同じことを、画面に出ている View で。ここでは、止めそこねた繰り返しは本当に続く。 */
    @Test fun a_layout_switch_inside_the_first_keys_action_never_starts_delete_on_screen() {
        shownView()
        holdLanguageThenTouchDelete()
    }

    /** 3本目の指が触れても、最初の指が触れ直すまでは何も始めない。 */
    @Test fun after_the_switch_a_third_finger_starts_nothing_either() {
        shownView()
        languageSwitchesTo = Layouts.latinFlick
        val language = center(KeyAction.Language)
        val delete = center(KeyAction.Delete)
        val space = center(KeyAction.Space)
        send(MotionEvent.ACTION_DOWN, 0, listOf(0), listOf(language))
        send(MotionEvent.ACTION_POINTER_DOWN, 1, listOf(0, 1), listOf(language, space))
        send(MotionEvent.ACTION_POINTER_DOWN, 2, listOf(0, 1, 2), listOf(language, space, delete))
        Thread.sleep(600)                                                          // 長押し（300 ms）も、繰り返しも起きない
        send(MotionEvent.ACTION_POINTER_UP, 2, listOf(0, 1, 2), listOf(language, space, delete))
        send(MotionEvent.ACTION_POINTER_UP, 1, listOf(0, 1), listOf(language, space))
        send(MotionEvent.ACTION_UP, 0, listOf(0), listOf(language))
        assertEquals(listOf(KeyAction.Language to Direction.TAP), snapshot())
        var asked = -1
        onMain { asked = cursorModeAsked }
        assertEquals(0, asked)
    }

    /** 画面に出ている View で、削除キーの押し続けは繰り返し、指を離すと止まる（この確かめ方が、止まらない繰り返しを見つけられること）。 */
    @Test fun a_held_delete_repeats_and_stops_on_release() {
        shownView()
        val delete = center(KeyAction.Delete)
        send(MotionEvent.ACTION_DOWN, 0, listOf(0), listOf(delete))
        assertEquals(1, count(KeyAction.Delete))
        Thread.sleep(1000)                                                         // 1 + (1000 − 400) / 50 回くらい
        send(MotionEvent.ACTION_UP, 0, listOf(0), listOf(delete))
        val atRelease = count(KeyAction.Delete)
        assertTrue("repeated while held: $atRelease", atRelease in 6..20)
        Thread.sleep(400)
        assertEquals(atRelease, count(KeyAction.Delete))
    }

    /** 削除キーを押したまま、別の指が あa に触れる。削除の繰り返しは止まり、あa は、その指が離れたときに働く。 */
    @Test fun rolling_from_a_held_delete_onto_another_key_stops_the_repeat() {
        shownView()
        languageSwitchesTo = Layouts.latinFlick
        val delete = center(KeyAction.Delete)
        val language = center(KeyAction.Language)
        send(MotionEvent.ACTION_DOWN, 0, listOf(0), listOf(delete))
        Thread.sleep(600)
        send(MotionEvent.ACTION_POINTER_DOWN, 1, listOf(0, 1), listOf(delete, language))
        val deletes = count(KeyAction.Delete)
        assertTrue("repeated while held: $deletes", deletes >= 2)
        Thread.sleep(400)
        assertEquals(deletes, count(KeyAction.Delete))
        send(MotionEvent.ACTION_POINTER_UP, 1, listOf(0, 1), listOf(delete, language))
        assertEquals(1, count(KeyAction.Language))
        send(MotionEvent.ACTION_UP, 0, listOf(0), listOf(delete))                  // 置いたままだった指が離れても、何も起きない
        Thread.sleep(500)
        assertEquals(deletes, count(KeyAction.Delete)); assertEquals(1, count(KeyAction.Language))
    }

    /** The original touch stream (no extra DOWN) must reach the menu and never insert Space. */
    @Test fun held_space_drag_then_release_selects_without_second_tap() {
        shownView()
        onMain { view.layout = Layouts.romaji }
        val start = center(KeyAction.Space)
        val end = start.first to start.second - 80f
        send(MotionEvent.ACTION_DOWN, 0, listOf(0), listOf(start))
        Thread.sleep(450)
        instrumentation.waitForIdleSync()
        assertEquals(start, menuOpenedAt)
        send(MotionEvent.ACTION_MOVE, 0, listOf(0), listOf(end))
        assertTrue("hover MOVE delivered", menuMoveCount > 0)
        send(MotionEvent.ACTION_UP, 0, listOf(0), listOf(end))
        assertEquals(end, menuReleasedAt)
        assertEquals(0, count(KeyAction.Space))
        assertEquals(0, menuCancelCount)
    }

    /** Lifting without entering any menu item must not insert a space either. */
    @Test fun held_space_release_without_drag_consumes_the_press() {
        shownView()
        onMain { view.layout = Layouts.qwerty }
        val position = center(KeyAction.Space)
        send(MotionEvent.ACTION_DOWN, 0, listOf(0), listOf(position))
        Thread.sleep(450)
        instrumentation.waitForIdleSync()
        assertEquals(position, menuOpenedAt)
        send(MotionEvent.ACTION_UP, 0, listOf(0), listOf(position))
        assertEquals(position, menuReleasedAt)
        assertEquals(0, count(KeyAction.Space))
    }
}
