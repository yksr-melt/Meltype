// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.view

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.view.MotionEvent
import android.view.View
import io.github.ninjin0802.keyboard.app.input.Direction
import io.github.ninjin0802.keyboard.app.input.charAt
import io.github.ninjin0802.keyboard.app.layout.Key
import io.github.ninjin0802.keyboard.engine.FlickKey

/**
 * フリックの案内（spec 04「フリックの案内」）。候補バーとキーの両方に重ねる透明な View で、描くだけ。
 * タッチは受け取らず、下の View にそのまま届く。補助機能には何も知らせない（spec 09）。
 * 描くたびの割り当てはない。形は [show] のときに作っておく。
 */
class FlickPopupView(context: Context) : View(context) {
    /** 選んでいる文字を、十字の左上に大きく出すか。パスワードの欄では出さない（spec 09）。 */
    var showPreview = true

    private val palette = Palette.of(context)
    private val density = resources.displayMetrics.density
    private val inset = 1.5f * density
    private val radius = 10 * density

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val shadowed = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        setShadowLayer(4 * density, 0f, 1.5f * density, 0x50000000)
    }
    private val outline = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE; strokeWidth = 1.5f * density; color = palette.popupOutline
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { textAlign = Paint.Align.CENTER }
    private val fm = Paint.FontMetrics()
    private val rect = RectF()
    private val oneChar = CharArray(1)
    private val cells = FloatArray(FlickPopupGeometry.SIZE)
    private val cross = Path()
    private val bar = Path()

    private var kana: FlickKey? = null
    private var direction = Direction.TAP

    init {
        importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
        isClickable = false
        isFocusable = false
    }

    /**
     * 自分では高さを求めない。候補バーとキー（と下の余白）で決まった親の高さに、あとから合わせる
     * （高さを求めると、キーボードが画面いっぱいに広がってしまう）。
     */
    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val exact = MeasureSpec.getMode(heightMeasureSpec) == MeasureSpec.EXACTLY
        setMeasuredDimension(MeasureSpec.getSize(widthMeasureSpec), if (exact) MeasureSpec.getSize(heightMeasureSpec) else 0)
    }

    /** [key] を中心に案内を出す。[keyboardTop] は、この View の中での、キーのいちばん上の段の上の端。 */
    fun show(key: Key, chars: FlickKey, cellW: Float, cellH: Float, keyboardTop: Float) {
        FlickPopupGeometry.layout(key, cellW, cellH, keyboardTop, width.toFloat(), height.toFloat(), cells)
        kana = chars
        buildCross()
        direction = Direction.TAP
        invalidate()
    }

    fun setDirection(d: Direction) {
        if (kana == null || d == direction) return
        direction = d
        invalidate()
    }

    fun hide() {
        if (kana == null) return
        kana = null
        invalidate()
    }

    /** 十字全体を、ひとつながりの形にする（縦の帯と横の帯を合わせる）。 */
    private fun buildCross() {
        val up = if (present(Direction.UP)) Direction.UP else Direction.TAP
        val down = if (present(Direction.DOWN)) Direction.DOWN else Direction.TAP
        val left = if (present(Direction.LEFT)) Direction.LEFT else Direction.TAP
        val right = if (present(Direction.RIGHT)) Direction.RIGHT else Direction.TAP
        cross.rewind()
        rect.set(l(Direction.TAP) + inset, t(up) + inset, r(Direction.TAP) - inset, b(down) - inset)
        cross.addRoundRect(rect, radius, radius, Path.Direction.CW)
        bar.rewind()
        rect.set(l(left) + inset, t(Direction.TAP) + inset, r(right) - inset, b(Direction.TAP) - inset)
        bar.addRoundRect(rect, radius, radius, Path.Direction.CW)
        cross.op(bar, Path.Op.UNION)
    }

    private fun l(d: Direction) = cells[d.ordinal * 4]
    private fun t(d: Direction) = cells[d.ordinal * 4 + 1]
    private fun r(d: Direction) = cells[d.ordinal * 4 + 2]
    private fun b(d: Direction) = cells[d.ordinal * 4 + 3]
    /** [d] のますを出すか。描ける大きさがあり、その向きに文字があるとき。 */
    private fun present(d: Direction) = FlickPopupGeometry.isPresent(cells, d.ordinal) && kana?.charAt(d) != null

    override fun onDraw(canvas: Canvas) {
        val chars = kana ?: return
        shadowed.color = palette.popup
        canvas.drawPath(cross, shadowed)
        canvas.drawPath(cross, outline)

        val dirs = Direction.entries
        for (i in dirs.indices) {
            val d = dirs[i]
            if (!present(d)) continue
            val on = d == direction
            rect.set(l(d) + inset * 3, t(d) + inset * 2, r(d) - inset * 3, b(d) - inset * 2)
            if (on) {
                fill.color = palette.accent
                canvas.drawRoundRect(rect, radius - inset * 2, radius - inset * 2, fill)
            }
            // 選んでいる向きは少し大きく書く。大きな表示を出さない欄では、どの向きもキーの文字と同じ大きさ。
            val size = if (on && showPreview) SELECTED_TEXT_DP else TEXT_DP
            drawChar(canvas, chars.charAt(d) ?: continue, rect, size, if (on) palette.onAccent else palette.text)
        }

        // 文字のない向きに指があるあいだは、選んでいる文字がない。
        val chosen = chars.charAt(direction) ?: return
        if (showPreview && FlickPopupGeometry.isPresent(cells, FlickPopupGeometry.PREVIEW)) {
            val i = FlickPopupGeometry.PREVIEW * 4
            rect.set(cells[i] + inset, cells[i + 1] + inset * 2, cells[i + 2] - inset * 3, cells[i + 3] - inset * 3)
            shadowed.color = palette.accent
            canvas.drawRoundRect(rect, radius, radius, shadowed)
            drawChar(canvas, chosen, rect, PREVIEW_TEXT_DP, palette.onAccent)
        }
    }

    /** [box] の中央に1文字。ますが低いとき（横画面、候補バーの上）は、収まる大きさに下げる。 */
    private fun drawChar(canvas: Canvas, c: Char, box: RectF, sizeDp: Float, color: Int) {
        textPaint.color = color
        textPaint.textSize = minOf(sizeDp * density, box.height() * MAX_TEXT_FRACTION)
        textPaint.getFontMetrics(fm)
        oneChar[0] = c
        canvas.drawText(oneChar, 0, 1, box.centerX(), box.centerY() - (fm.ascent + fm.descent) / 2, textPaint)
    }

    /** 描くだけ。タッチは下のキーや候補に届ける。 */
    @Suppress("ClickableViewAccessibility")
    override fun onTouchEvent(event: MotionEvent): Boolean = false

    private companion object {
        /** キーの文字と同じ大きさ。 */
        const val TEXT_DP = KeyboardView.KANA_TEXT_DP
        const val SELECTED_TEXT_DP = 26f
        const val PREVIEW_TEXT_DP = 30f
        const val MAX_TEXT_FRACTION = 0.74f
    }
}
