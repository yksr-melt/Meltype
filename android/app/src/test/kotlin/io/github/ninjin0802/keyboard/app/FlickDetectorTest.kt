// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.Direction
import io.github.ninjin0802.keyboard.app.input.FlickDetector
import io.github.ninjin0802.keyboard.app.input.charAt
import io.github.ninjin0802.keyboard.app.layout.LatinKeys
import io.github.ninjin0802.keyboard.engine.KanaKeys
import kotlin.test.Test
import kotlin.test.assertEquals

class FlickDetectorTest {
    @Test fun below_quarter_width_is_tap() = assertEquals(Direction.TAP, FlickDetector.direction(24f, 0f, 100f))
    @Test fun at_quarter_width_is_flick() = assertEquals(Direction.RIGHT, FlickDetector.direction(25f, 0f, 100f))
    @Test fun four_directions() {
        assertEquals(Direction.LEFT, FlickDetector.direction(-40f, 10f, 100f)); assertEquals(Direction.UP, FlickDetector.direction(10f, -40f, 100f))
        assertEquals(Direction.DOWN, FlickDetector.direction(-10f, 40f, 100f))
    }
    @Test fun a_direction_without_a_character_gives_none() {
        val abc = LatinKeys.all[1]
        assertEquals(listOf('a', 'b', 'c', null, '2'), Direction.entries.map { abc.charAt(it) })
    }
    @Test fun chars_follow_direction() { assertEquals('う', KanaKeys.A.charAt(Direction.UP)); assertEquals('）', KanaKeys.YA.charAt(Direction.RIGHT)) }
}
