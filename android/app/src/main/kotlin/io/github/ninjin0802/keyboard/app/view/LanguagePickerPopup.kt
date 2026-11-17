// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app.view

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import android.widget.PopupWindow
import android.widget.TextView
import io.github.ninjin0802.keyboard.app.layout.LayoutId

/**
 * A one-touch language picker: hold Space, slide into a row, then release.
 *
 * The popup never handles touches. KeyboardView owns the original pointer stream,
 * so lifting the finger over the popup confirms a choice without a second tap.
 */
internal class LanguagePickerPopup(private val context: Context) {
    private data class Item(val label: String, val language: LayoutId? = null, val emoji: Boolean = false)

    private var showing: PopupWindow? = null
    private var items = emptyList<Item>()
    private var rows = emptyList<TextView>()
    private var hovered: Int? = null
    private var selected: LayoutId = LayoutId.ROMAJI
    private var onSelectLanguage: ((LayoutId) -> Unit)? = null
    private var onEmoji: (() -> Unit)? = null
    private val density get() = context.resources.displayMetrics.density
    private val menuWidth get() = (236f * density).toInt()
    private val pad get() = (8f * density).toInt()
    private val rowHeight get() = (50f * density).toInt()

    fun dismiss() {
        val popup = showing
        showing = null
        items = emptyList()
        rows = emptyList()
        hovered = null
        onSelectLanguage = null
        onEmoji = null
        popup?.dismiss()
    }

    fun show(
        anchor: View,
        touchX: Float,
        touchY: Float,
        selected: LayoutId,
        allowEmoji: Boolean,
        onSelectLanguage: (LayoutId) -> Unit,
        onEmoji: () -> Unit,
    ): Boolean {
        if (!anchor.isAttachedToWindow) return false
        dismiss()
        this.selected = selected
        this.onSelectLanguage = onSelectLanguage
        this.onEmoji = onEmoji
        items = listOf(
            Item("日本語（自動判別）", LayoutId.ROMAJI),
            Item("English", LayoutId.QWERTY),
        ) + if (allowEmoji) listOf(Item("絵文字", emoji = true)) else emptyList()

        val palette = Palette.of(context)
        val rowViews = ArrayList<TextView>()
        val column = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(pad, pad, pad, pad)
            background = GradientDrawable().apply {
                setColor(palette.key)
                cornerRadius = 17f * density
            }
        }
        items.forEach { item ->
            val row = TextView(context).apply {
                text = (if (item.language == selected) "✓  " else "     ") + item.label
                textSize = 17f
                gravity = Gravity.CENTER_VERTICAL
                setPadding((14f * density).toInt(), 0, (14f * density).toInt(), 0)
            }
            rowViews.add(row)
            column.addView(row, LinearLayout.LayoutParams(-1, rowHeight))
        }
        rows = rowViews

        val pos = IntArray(2)
        anchor.getLocationOnScreen(pos)
        val fingerX = pos[0] + touchX
        val fingerY = pos[1] + touchY
        val maxLeft = (context.resources.displayMetrics.widthPixels - menuWidth).coerceAtLeast(0)
        val left = (fingerX - menuWidth / 2f).toInt().coerceIn(0, maxLeft)
        val tailHeight = (9f * density).toInt()
        val bodyHeight = items.size * rowHeight + pad * 2
        // The tip ends just above the touched point, including on split/folded layouts.
        val top = (fingerY - bodyHeight - tailHeight - 7f * density).toInt().coerceAtLeast(0)
        val tipX = (fingerX - left).coerceIn(15f * density, menuWidth - 15f * density)

        val tail = object : View(context) {
            private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.key }
            private val triangle = Path().apply {
                moveTo(tipX - 10f * density, 0f)
                lineTo(tipX + 10f * density, 0f)
                lineTo(tipX, tailHeight.toFloat())
                close()
            }
            override fun onDraw(canvas: Canvas) {
                super.onDraw(canvas)
                canvas.drawPath(triangle, paint)
            }
        }
        val balloon = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            addView(column, LinearLayout.LayoutParams(menuWidth, bodyHeight))
            addView(tail, LinearLayout.LayoutParams(menuWidth, tailHeight))
        }
        val popup = PopupWindow(balloon, menuWidth, bodyHeight + tailHeight, false).apply {
            setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
            elevation = 12f * density
            isTouchable = false // Critical: MOVE and UP stay with KeyboardView.
            isOutsideTouchable = false
        }
        showing = popup
        popup.setOnDismissListener {
            if (showing === popup) dismiss()
        }
        popup.showAtLocation(anchor, Gravity.TOP or Gravity.START, left, top)
        updateHighlight()
        return true
    }

    fun move(anchor: View, x: Float, y: Float) {
        val content = showing?.contentView ?: return
        val anchorPosition = IntArray(2)
        val popupPosition = IntArray(2)
        anchor.getLocationOnScreen(anchorPosition)
        content.getLocationOnScreen(popupPosition)
        val localX = anchorPosition[0] + x - popupPosition[0]
        val localY = anchorPosition[1] + y - popupPosition[1]
        val next = languagePickerHitIndex(
            localX, localY, menuWidth.toFloat(), pad.toFloat(), rowHeight.toFloat(), items.size,
        )
        if (hovered != next) {
            hovered = next
            updateHighlight()
        }
    }

    fun release(anchor: View, x: Float, y: Float) {
        move(anchor, x, y)
        val choice = hovered?.let(items::get)
        val select = onSelectLanguage
        val emojiAction = onEmoji
        dismiss()
        when {
            choice?.language != null -> select?.invoke(choice.language)
            choice?.emoji == true -> emojiAction?.invoke()
        }
    }

    private fun updateHighlight() {
        val palette = Palette.of(context)
        rows.forEachIndexed { i, row ->
            val isHovered = hovered == i
            val isCurrent = items[i].language == selected
            row.background = GradientDrawable().apply {
                setColor(when {
                    isHovered -> palette.accent
                    isCurrent -> palette.keyPressed
                    else -> Color.TRANSPARENT
                })
                cornerRadius = 11f * density
            }
            row.setTextColor(if (isHovered) palette.onAccent else palette.text)
        }
    }
}

/** The triangular tail and padding are not selectable; only visible option rows are. */
internal fun languagePickerHitIndex(
    x: Float,
    y: Float,
    width: Float,
    padding: Float,
    rowHeight: Float,
    itemCount: Int,
): Int? {
    if (x < 0f || x >= width || y < padding || rowHeight <= 0f) return null
    val index = ((y - padding) / rowHeight).toInt()
    return index.takeIf { it in 0 until itemCount && y < padding + itemCount * rowHeight }
}
