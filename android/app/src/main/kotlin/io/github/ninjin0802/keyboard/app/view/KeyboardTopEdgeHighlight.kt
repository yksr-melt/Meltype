// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app.view

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.view.View

/**
 * A restrained iOS-inspired hairline at the upper edge of the translucent IME.
 *
 * Only the top edge and both rounded shoulders are highlighted. Drawing a
 * full border would frame the keyboard like a card and interfere with the
 * bottom system navigation area. This decorative View is never a touch target.
 */
class KeyboardTopEdgeHighlight(context: Context) : View(context) {
    private val density = resources.displayMetrics.density
    private val cornerRadius = 28f * density
    private val stroke = (1f * density).coerceAtLeast(1f)
    private val inset = stroke / 2f
    private val path = Path()
    private val leftArc = RectF()
    private val rightArc = RectF()
    private val highlight = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = 0xBFFFFFFF.toInt()
        style = Paint.Style.STROKE
        strokeWidth = stroke
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }

    init {
        setWillNotDraw(false)
        isClickable = false
        isFocusable = false
        importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        path.reset()
        val radius = cornerRadius.coerceAtMost(w / 2f).coerceAtMost(h.toFloat() - inset)
        if (radius <= inset || w <= 0) return
        // The inset keeps the full stroke visible without clipping at the
        // screen edge. Radius matches the keyboard's 28dp background corners.
        val left = inset
        val top = inset
        val right = w.toFloat() - inset
        leftArc.set(left, top, left + radius * 2f, top + radius * 2f)
        rightArc.set(right - radius * 2f, top, right, top + radius * 2f)
        path.moveTo(left, top + radius)
        path.arcTo(leftArc, 180f, 90f, false)
        path.lineTo(right - radius, top)
        path.arcTo(rightArc, 270f, 90f, false)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawPath(path, highlight)
    }
}
