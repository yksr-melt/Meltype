// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.view.languagePickerHitIndex
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class LanguagePickerHitTest {
    private fun hit(x: Float, y: Float, n: Int = 3) =
        languagePickerHitIndex(x, y, width = 236f, padding = 8f, rowHeight = 50f, itemCount = n)

    @Test fun sliding_up_and_down_selects_exactly_the_row_under_the_finger() {
        assertEquals(2, hit(118f, 140f))
        assertEquals(1, hit(118f, 90f))
        assertEquals(0, hit(118f, 32f))
        assertEquals(2, hit(118f, 120f))
    }

    @Test fun releasing_outside_a_choice_cancels_the_selection() {
        assertNull(hit(118f, 3f))       // top padding
        assertNull(hit(118f, 160f))     // bottom padding and triangle tail
        assertNull(hit(-1f, 85f))
        assertNull(hit(236f, 85f))
        assertNull(hit(118f, 120f, n = 2)) // emoji disabled
    }

    @Test fun rows_have_half_open_hit_boundaries() {
        assertEquals(0, hit(0f, 8f))
        assertEquals(1, hit(235.9f, 58f))
        assertEquals(2, hit(118f, 108f))
        assertNull(hit(118f, 158f))
    }
}
