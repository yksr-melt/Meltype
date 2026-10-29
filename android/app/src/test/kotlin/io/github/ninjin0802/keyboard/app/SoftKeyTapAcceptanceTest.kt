// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.SoftKeyTapAcceptance
import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class SoftKeyTapAcceptanceTest {
    private fun accepted(
        textKey: Boolean,
        downX: Float,
        downY: Float,
        upX: Float,
        upY: Float,
        same: Boolean = false,
    ) = SoftKeyTapAcceptance.shouldCommit(
        sameKey = same, textKey = textKey,
        downX = downX, downY = downY, upX = upX, upY = upY,
        keyWidthPx = 60f, rowHeightPx = 72f,
    )

    @Test fun sameKeyAcceptedWithoutTolerance() {
        assertTrue(accepted(false, 10f, 20f, 16f, 24f, same = true))
    }

    @Test fun slightSidewaysReleaseDriftDoesNotDropLetter() {
        assertTrue(accepted(true, 45f, 30f, 65f, 37f))
    }

    @Test fun slightVerticalReleaseDriftDoesNotDropLetter() {
        assertTrue(accepted(true, 45f, 30f, 48f, 55f))
    }

    @Test fun longDragDoesNotTypeAccidentally() {
        assertFalse(accepted(true, 40f, 30f, 78f, 30f))
        assertFalse(accepted(true, 40f, 30f, 40f, 75f))
    }

    @Test fun controlKeysStillRequireOriginalHitbox() {
        assertFalse(accepted(false, 45f, 30f, 52f, 32f))
    }

    @Test fun invalidTouchCoordinatesRejected() {
        assertFalse(accepted(true, Float.NaN, 30f, 45f, 30f))
    }
}
