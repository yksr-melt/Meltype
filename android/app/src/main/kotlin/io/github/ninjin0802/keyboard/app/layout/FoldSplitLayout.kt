// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app.layout

/**
 * Thumb-oriented layouts selected from the actual IME viewport in dp.
 * A narrow or short window keeps the standard full-width QWERTY. On wider
 * unfolded windows the two halves get larger key targets, and the center
 * numeric pad is present only if both width and height genuinely allow it.
 */
object FoldSplitLayout {
    const val COLUMNS = 18f // room for two hands and the optional numeric pad
    const val COMPACT_COLUMNS = 14f // enlarged halves, no numeric pad
    const val LEFT_END = 6f
    const val PAD_START = 7.2f
    const val PAD_END = 10.8f
    const val RIGHT_START = 12f
    const val COMPACT_RIGHT_START = 8f

    enum class Mode { FULL, SPLIT, SPLIT_WITH_NUMPAD }

    fun mode(widthDp: Float, heightDp: Float, smallestWidthDp: Int): Mode {
        if (!widthDp.isFinite() || !heightDp.isFinite() ||
            widthDp < 730f || heightDp < 340f || smallestWidthDp < 500) return Mode.FULL
        // Three numeric columns each occupy 1.2/18 of the width. Allow them
        // only when those targets can be at least 48dp wide and the display
        // has enough vertical space for a usable fourth row.
        val numpadKeyDp = ((widthDp - 24f) / COLUMNS) * 1.2f
        val spacious = widthDp >= 900f && heightDp >= 540f &&
            widthDp / heightDp <= 1.85f && numpadKeyDp >= 48f
        return if (spacious) Mode.SPLIT_WITH_NUMPAD else Mode.SPLIT
    }

    fun forWindow(
        base: KeyboardLayout, widthDp: Float, heightDp: Float, smallestWidthDp: Int,
    ): KeyboardLayout {
        // One-row emoji controls reuse the input-mode ID; never turn them into QWERTY.
        if (base.rows != 4 || (base.id != LayoutId.ROMAJI && base.id != LayoutId.QWERTY)) return base
        return when (mode(widthDp, heightDp, smallestWidthDp)) {
            Mode.FULL -> base
            Mode.SPLIT -> if (base.id == LayoutId.ROMAJI) romajiCompact else englishCompact
            Mode.SPLIT_WITH_NUMPAD -> if (base.id == LayoutId.ROMAJI) romaji else english
        }
    }

    /** Do not turn unused center space into accidental key taps. */
    fun isDeadZone(x: Float, columns: Float): Boolean = when (columns) {
        COMPACT_COLUMNS -> x < 0f || x >= COMPACT_COLUMNS ||
            (x >= LEFT_END && x < COMPACT_RIGHT_START)
        COLUMNS -> x < 0f || x >= COLUMNS ||
            (x >= LEFT_END && x < PAD_START) || (x >= PAD_END && x < RIGHT_START)
        else -> false
    }

    private fun k(row: Int, x: Float, w: Float, label: String, japanese: Boolean): Key =
        Key(if (japanese) KeyAction.Romaji(label.single()) else KeyAction.Text(label),
            label, label, x, row, w)

    private fun button(row: Int, x: Float, w: Float, action: KeyAction, name: String, label: String = "") =
        Key(action, label, name, x, row, w)

    private fun build(japanese: Boolean, withNumpad: Boolean): KeyboardLayout {
        val right = if (withNumpad) RIGHT_START else COMPACT_RIGHT_START
        val keys = ArrayList<Key>(if (withNumpad) 60 else 50)
        fun letters(row: Int, start: Float, text: String, width: Float) {
            for ((i, ch) in text.withIndex()) keys.add(k(row, start + i * width, width, ch.toString(), japanese))
        }
        // Boundary letters are intentionally available to either thumb.
        letters(0, 0f, "qwert", 1.2f)
        letters(0, right, "yuiop", 1.2f)
        letters(1, 0f, "asdfg", 1.2f)
        letters(1, right, if (japanese) "ghjkl-" else "ghjkl", if (japanese) 1f else 1.2f)
        keys.add(button(2, 0f, 1.2f, KeyAction.Shift, "シフト"))
        letters(2, 1.2f, "zxcv", 1.2f)
        letters(2, right, "vbnm", 1.2f)
        keys.add(button(2, right + 4.8f, 1.2f, KeyAction.Delete, "削除"))

        keys.add(button(3, 0f, 1.4f, KeyAction.Switch(LayoutId.SYMBOLS), "記号", "123"))
        keys.add(button(3, 1.4f, 1f, KeyAction.Text(if (japanese) "、" else ","), "読点",
            if (japanese) "、" else ","))
        keys.add(button(3, 2.4f, 3.6f, KeyAction.Space, "左スペース"))
        keys.add(button(3, right, 3.6f, KeyAction.Space, "右スペース"))
        keys.add(button(3, right + 3.6f, 1f, KeyAction.Text(if (japanese) "。" else "."), "句点",
            if (japanese) "。" else "."))
        keys.add(button(3, right + 4.6f, 1.4f, KeyAction.Enter, "確定"))

        if (withNumpad) {
            for ((row, digits) in listOf("789", "456", "123").withIndex()) {
                for ((col, digit) in digits.withIndex()) {
                    keys.add(Key(KeyAction.Text(digit.toString()), digit.toString(),
                        digit.toString(), PAD_START + col * 1.2f, row, 1.2f))
                }
            }
            keys.add(Key(KeyAction.Text("0"), "0", "0", PAD_START + 1.2f, 3, 1.2f))
        }
        return KeyboardLayout(
            if (japanese) LayoutId.ROMAJI else LayoutId.QWERTY,
            if (withNumpad) COLUMNS else COMPACT_COLUMNS, 4, keys,
        )
    }

    val romaji by lazy { build(japanese = true, withNumpad = true) }
    val english by lazy { build(japanese = false, withNumpad = true) }
    val romajiCompact by lazy { build(japanese = true, withNumpad = false) }
    val englishCompact by lazy { build(japanese = false, withNumpad = false) }
}
