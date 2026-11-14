// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.view.KeyboardHeights
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class KeyboardHeightsTest {
    @Test fun wideFoldGetsTallEnoughRowsEvenWhenWindowIsShort() {
        val g = KeyboardHeights.forViewport(1050f, 470f)
        assertTrue(g.rowDp >= 46)
        assertTrue(g.rowDp - g.gapYDp >= 40)
        assertTrue(g.barDp >= 32)
        assertTrue(g.rowDp > 33) // Regression: fixed 33dp made typing impractical
    }

    @Test fun phoneLandscapeStillHasTouchableRows() {
        val g = KeyboardHeights.forViewport(800f, 360f)
        assertEquals(46, g.rowDp)
        assertEquals(6, g.gapYDp)
        assertTrue(g.rowDp * 4 + g.barDp + g.topDp + g.bottomDp < 250)
    }

    @Test fun tallAndSquareWindowsGetFullSizeKeys() {
        val portrait = KeyboardHeights.forViewport(410f, 900f)
        val square = KeyboardHeights.forViewport(820f, 840f)
        assertTrue(portrait.rowDp >= 54)
        assertTrue(square.rowDp >= 54)
        assertTrue(square.barDp >= 48)
    }

    @Test fun logicalDpNotRawScreenPixelsControlsSizing() {
        assertEquals(
            KeyboardHeights.forViewport(1000f, 500f),
            KeyboardHeights.forViewport(1000f, 500f),
        )
        assertTrue(KeyboardHeights.forViewport(1000f, 400f).rowDp <
            KeyboardHeights.forViewport(1000f, 550f).rowDp)
    }
}
