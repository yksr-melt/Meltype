// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.layout.Layouts
import io.github.ninjin0802.keyboard.app.layout.nearestKeyAt
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class NearestKeyAtTest {
    private val q = Layouts.qwerty

    @Test fun left_margin_goes_to_a() = assertEquals("a", q.nearestKeyAt(0.2f, 1.5f)!!.label)
    @Test fun right_margin_goes_to_l() = assertEquals("l", q.nearestKeyAt(9.8f, 1.5f)!!.label)
    @Test fun normal_point_matches_keyAt() = assertEquals(q.keyAt(3.5f, 0.5f), q.nearestKeyAt(3.5f, 0.5f))
    @Test fun exact_right_and_bottom_edges_still_hit_a_key() {
        assertEquals("確定", Layouts.kana.nearestKeyAt(5f, 3.5f)!!.name)
        assertEquals("p", q.nearestKeyAt(10f, 0.5f)!!.label)
    }
    @Test fun outside_is_null() {
        assertNull(q.nearestKeyAt(-0.1f, 0.5f)); assertNull(q.nearestKeyAt(10.5f, 0.5f))
        assertNull(q.nearestKeyAt(1f, -0.1f)); assertNull(q.nearestKeyAt(1f, 4.5f))
    }
}
