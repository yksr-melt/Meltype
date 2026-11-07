// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app.layout

/**
 * When emoji is off, recover the emoji key space for the normal keyboard.
 * Nothing is left behind as a disabled/dead key.
 */
fun KeyboardLayout.withoutEmoji(): KeyboardLayout {
    val emojiKeys = keys.filter { it.action == KeyAction.Emoji }
    if (emojiKeys.isEmpty()) return this
    val newKeys = keys.filterNot { it.action == KeyAction.Emoji }.toMutableList()
    for (emoji in emojiKeys) {
        val row = newKeys.filter { it.row == emoji.row }.sortedBy { it.col }
        val toLeft = row.lastOrNull { it.col + it.width <= emoji.col + 0.0001f }
        val toRight = row.firstOrNull { it.col >= emoji.col + emoji.width - 0.0001f }
        // On 5-column flick layouts, the emoji key is half a column wide.
        // Enlarge the adjacent ?123 key to its right edge.
        if (emoji.width < 1f && toLeft != null) {
            val index = newKeys.indexOf(toLeft)
            newKeys[index] = toLeft.copy(width = toLeft.width + emoji.width)
            continue
        }
        // On QWERTY layouts, move punctuation into the emoji slot and
        // enlarge Space; keep the positions of Return and period unchanged.
        val space = row.firstOrNull { it.action == KeyAction.Space && it.col > emoji.col }
        if (toRight != null && space != null && toRight.col < space.col) {
            val punctuationIndex = newKeys.indexOf(toRight)
            newKeys[punctuationIndex] = toRight.copy(col = toRight.col - emoji.width)
            val spaceIndex = newKeys.indexOf(space)
            newKeys[spaceIndex] = space.copy(col = space.col - emoji.width, width = space.width + emoji.width)
        } else if (toLeft != null) {
            val index = newKeys.indexOf(toLeft)
            newKeys[index] = toLeft.copy(width = toLeft.width + emoji.width)
        }
    }
    return copy(keys = newKeys)
}
