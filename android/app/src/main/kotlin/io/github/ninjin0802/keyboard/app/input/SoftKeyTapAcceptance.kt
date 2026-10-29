// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app.input

import kotlin.math.abs

/**
 * A soft-key press starts on ACTION_DOWN. On small accidental drift, keep the
 * original text key instead of silently dropping the character on ACTION_UP.
 *
 * This changes tap acceptance, not the hitboxes: a nearby touch still maps to
 * exactly one key and adjacent keys are not enlarged into each other.
 */
object SoftKeyTapAcceptance {
    /**
     * @param sameKey Whether the release location resolves to the DOWN key.
     * @param textKey Controls (Delete, Enter, language, etc.) deliberately do
     *        not receive the larger text-key release tolerance.
     */
    fun shouldCommit(
        sameKey: Boolean,
        textKey: Boolean,
        downX: Float,
        downY: Float,
        upX: Float,
        upY: Float,
        keyWidthPx: Float,
        rowHeightPx: Float,
    ): Boolean {
        if (sameKey) return true
        if (!textKey) return false
        if (!downX.isFinite() || !downY.isFinite() || !upX.isFinite() || !upY.isFinite()) return false
        if (keyWidthPx <= 0f || rowHeightPx <= 0f) return false
        // Keep limited tap movement; an intentional long drag does not type.
        return abs(upX - downX) <= keyWidthPx * 0.47f &&
               abs(upY - downY) <= rowHeightPx * 0.42f
    }
}
