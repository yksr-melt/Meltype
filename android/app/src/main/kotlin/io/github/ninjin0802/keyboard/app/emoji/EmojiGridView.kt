// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.emoji

import android.content.Context
import android.content.res.Configuration
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.VelocityTracker
import android.view.View
import android.view.ViewConfiguration
import android.widget.OverScroller
import io.github.ninjin0802.keyboard.app.view.Palette

/**
 * 絵文字のます目。見えている行だけを描き、縦のスクロールと、はじいたあとの流れを自分で持つ
 * （数千個を1つの View で出す）。描画で割り当てをしない。位置の計算は [EmojiGrid]。
 */
class EmojiGridView(context: Context) : View(context) {
    /** 絵文字がタップされた。 */
    var onPick: ((String) -> Unit)? = null

    /** 絵文字が1つもないときに書く文。空なら、何も書かない。 */
    var emptyText: String = ""

    private val palette = Palette.of(context)
    private val density = resources.displayMetrics.density
    private val emojiPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER; textSize = EMOJI_TEXT_DP * density; color = palette.text
    }
    private val notePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER; textSize = 14 * density; color = palette.subText
    }
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val rect = RectF()
    private val radius = 10 * density
    private val thumbWidth = 3 * density

    private var items: Array<String> = emptyArray()
    private var grid = EmojiGrid(1f, 1f, 1, 1f, 0)
    private var scroll = 0f

    /** ますの中心から、文字の基準の線までの下がり。 */
    private val baseline = Paint.FontMetrics().let { emojiPaint.getFontMetrics(it); -(it.ascent + it.descent) / 2 }

    private val scroller = OverScroller(context)
    private val config = ViewConfiguration.get(context)
    private var tracker: VelocityTracker? = null

    /** タップかスクロールかの見分け。 */
    private val gesture = EmojiGridGesture(config.scaledTouchSlop.toFloat())

    /** 色を変えて描いているます。 */
    private var pressed = -1

    init {
        contentDescription = "絵文字の一覧"
    }

    /** [items] を先頭から出す。 */
    fun show(items: Array<String>) {
        cancelTouch()
        this.items = items
        scroll = 0f
        updateGrid()
        invalidate()
    }

    /** スクロールと押下を止める。何も入力しない。 */
    fun cancelTouch() {
        scroller.forceFinished(true)
        tracker?.recycle()
        tracker = null
        gesture.cancel()
        showPressed()
    }

    /** 押しているますが変わっていたら、描き直す。 */
    private fun showPressed() {
        if (pressed != gesture.pressed) { pressed = gesture.pressed; invalidate() }
    }

    private fun updateGrid() {
        if (width <= 0 || height <= 0) return
        grid = EmojiGrid(width.toFloat(), height.toFloat(), emojiColumns(width / density), EMOJI_CELL_HEIGHT_DP * density, items.size)
        scroll = grid.clampScroll(scroll)
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val widthPx = MeasureSpec.getSize(widthMeasureSpec)
        val widthDp = widthPx / density
        val displayHeightDp = EmojiPanelHeights.viewportHeightDp(resources, density)
        setMeasuredDimension(widthPx, EmojiPanelHeights.gridPx(widthDp, displayHeightDp, density))
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) = updateGrid()

    /** 画面が回ったら、作り直されなくても、その向きの高さで測り直す。 */
    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        cancelTouch()
        requestLayout()
    }

    override fun onDraw(canvas: Canvas) {
        val g = grid
        if (g.count == 0) {
            canvas.drawText(emptyText, g.width / 2, g.height / 2 + notePaint.textSize / 3, notePaint)
            return
        }
        val cellW = g.cellWidth
        val cellH = g.cellHeight
        val last = g.lastVisibleRow(scroll)
        var row = g.firstVisibleRow(scroll)
        while (row <= last) {
            val top = row * cellH - scroll
            var col = 0
            var i = row * g.columns
            while (col < g.columns && i < g.count) {
                val left = col * cellW
                if (i == pressed) {
                    rect.set(left + density, top + density, left + cellW - density, top + cellH - density)
                    fill.color = palette.keyPressed
                    canvas.drawRoundRect(rect, radius, radius, fill)
                }
                canvas.drawText(items[i], left + cellW / 2, top + cellH / 2 + baseline, emojiPaint)
                col++; i++
            }
            row++
        }
        val thumb = g.thumbHeight
        if (thumb > 0f) {
            val top = g.thumbTop(scroll)
            rect.set(g.width - thumbWidth - density, top + density, g.width - density, top + thumb - density)
            fill.color = palette.subText
            canvas.drawRoundRect(rect, thumbWidth / 2, thumbWidth / 2, fill)
        }
    }

    /** はじいたあとの流れ。描くたびに、次の位置へ進める。 */
    override fun computeScroll() {
        if (scroller.computeScrollOffset()) {
            scroll = grid.clampScroll(scroller.currY.toFloat())
            postInvalidateOnAnimation()
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                // 流れているあいだに触ったら、止めるだけ（その指では選ばない）。
                val wasMoving = !scroller.isFinished
                cancelTouch()
                tracker = VelocityTracker.obtain().also { it.addMovement(event) }
                gesture.down(event.getPointerId(0), event.y, grid.indexAt(event.x, event.y, scroll), flinging = wasMoving)
                showPressed()
            }
            MotionEvent.ACTION_MOVE -> {
                val i = event.findPointerIndex(gesture.pointer)
                if (i < 0) return true
                tracker?.addMovement(event)
                val wasDragging = gesture.dragging
                val dy = gesture.move(gesture.pointer, event.getY(i))
                if (!wasDragging && gesture.dragging) parent?.requestDisallowInterceptTouchEvent(true)
                showPressed()
                if (dy != 0f) {
                    scroll = grid.clampScroll(scroll - dy)
                    invalidate()
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                val i = event.actionIndex
                val id = event.getPointerId(i)
                if (id != gesture.pointer) return true
                val velocity = tracker?.let {
                    it.addMovement(event)
                    it.computeCurrentVelocity(1000, config.scaledMaximumFlingVelocity.toFloat())
                    it.getYVelocity(id)
                } ?: 0f
                val outcome = gesture.up(id, grid.indexAt(event.getX(i), event.getY(i), scroll))
                val index = gesture.picked
                cancelTouch()
                when (outcome) {
                    EmojiGridGesture.Up.SCROLLED -> if (Math.abs(velocity) > config.scaledMinimumFlingVelocity) {
                        scroller.fling(0, scroll.toInt(), 0, -velocity.toInt(), 0, 0, 0, grid.maxScroll.toInt())
                        postInvalidateOnAnimation()
                    }
                    EmojiGridGesture.Up.PICK -> if (index >= 0 && index < items.size) {
                        performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                        onPick?.invoke(items[index])
                    }
                    else -> Unit
                }
            }
            MotionEvent.ACTION_CANCEL -> cancelTouch()
        }
        return true
    }

    override fun onDetachedFromWindow() {
        cancelTouch()
        super.onDetachedFromWindow()
    }

    private companion object {
        /** 絵文字の大きさ。 */
        const val EMOJI_TEXT_DP = 28f
    }
}
