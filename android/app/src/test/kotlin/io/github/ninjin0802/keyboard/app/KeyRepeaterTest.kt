// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.KeyRepeater
import io.github.ninjin0802.keyboard.app.input.TouchTracker
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class KeyRepeaterTest {
    private val delays = mutableListOf<Long>(); private var pending: (() -> Unit)? = null; private var count = 0
    private val r = KeyRepeater({ d, t -> delays += d; pending = t }, { pending = null })
    private fun fire() { val t = pending!!; pending = null; t() }

    @Test fun fires_immediately_then_400_then_every_50() {
        r.start { count++ }; assertEquals(1, count); fire(); fire(); fire()
        assertEquals(4, count); assertEquals(listOf(400L, 50L, 50L, 50L), delays)
    }
    @Test fun stop_cancels() { r.start { count++ }; r.stop(); assertNull(pending); assertEquals(1, count) }
    @Test fun restart_replaces_previous() { r.start { count++ }; r.start { count += 10 }; fire(); assertEquals(21, count) }

    @Test fun while_alive_the_timing_is_unchanged() {
        r.start({ true }) { count++ }; fire(); fire(); fire()
        assertEquals(4, count); assertEquals(listOf(400L, 50L, 50L, 50L), delays)
    }

    /** 押下がもう無いのに時刻が来たら、動作を起こさず、次も予約しない。 */
    @Test fun a_tick_that_finds_the_press_gone_does_not_fire_and_schedules_nothing() {
        var alive = true
        r.start({ alive }) { count++ }; fire()
        assertEquals(2, count)
        alive = false; fire()
        assertEquals(2, count); assertNull(pending); assertEquals(listOf(400L, 50L), delays)
    }

    @Test fun the_first_tick_also_checks() {
        var alive = true
        r.start({ alive }) { count++ }
        alive = false; fire()
        assertEquals(1, count); assertNull(pending)
    }

    /** 動作の中から止められたら（動作が配列を切り替えたときなど）、次を予約しない。 */
    @Test fun stop_from_inside_the_action_leaves_nothing_scheduled() {
        r.start { count++; if (count == 2) r.stop() }; fire()
        assertEquals(2, count); assertNull(pending)
    }

    @Test fun stop_from_inside_the_first_action_leaves_nothing_scheduled() {
        r.start { count++; r.stop() }
        assertEquals(1, count); assertNull(pending); assertEquals(emptyList(), delays)
    }

    /**
     * 削除キーを押し続けているあいだに、タッチの外から配列が替わった（設定で選び直した知らせ）。繰り返しは止まり、
     * 置いたままの指を離しても、別の指が削除キーの場所に触れても、最初の指が触れ直すまで何も起きない。
     */
    @Test fun a_layout_change_from_outside_the_touch_stops_the_repeat_until_a_fresh_touch() {
        val t = TouchTracker()
        fun press(fresh: Boolean, id: Int) {   // KeyboardView が削除キーの上の指を受けたときにすること
            val outcome = t.down(id, 1f, 1f, fresh)
            if (outcome != TouchTracker.Outcome.IGNORE && t.tracking) r.start({ t.tracking }) { count++ }
        }
        press(fresh = true, id = 0); fire(); fire()
        assertEquals(3, count)
        t.cancel(); r.stop()                    // 配列の入れ替え（KeyboardView.cancelTouch）
        assertNull(pending)
        press(fresh = false, id = 1)            // 2本目の指が、新しい配列の削除キーの上に触れた
        assertEquals(3, count); assertNull(pending)
        assertEquals(TouchTracker.Outcome.IGNORE, t.up(1, 1f, 1f)); assertEquals(TouchTracker.Outcome.IGNORE, t.up(0, 1f, 1f))
        press(fresh = true, id = 0)             // 触れ直すと、ふつうに繰り返す
        assertEquals(4, count); fire()
        assertEquals(5, count); assertEquals(50L, delays.last())
    }

    /**
     * 繰り返すキーを押しているあいだに、タッチが取り消された（止める呼び出しは、わざと抜かしてある）。
     * 次の時刻には、押下が追われていないのを見て、自分で止まる。
     */
    @Test fun a_repeating_press_whose_touch_was_cancelled_stops_itself() {
        val t = TouchTracker()
        t.down(0, 1f, 1f, fresh = true)
        r.start({ t.tracking }) { count++ }; fire()
        assertEquals(2, count)
        t.down(1, 9f, 9f, fresh = false); t.cancel()
        fire()
        assertEquals(2, count); assertNull(pending)
        assertEquals(TouchTracker.Outcome.IGNORE, t.up(1, 9f, 9f))
    }
}
