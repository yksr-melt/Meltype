// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.view

import android.content.Context
import android.content.res.Configuration
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import io.github.ninjin0802.keyboard.app.input.Direction
import io.github.ninjin0802.keyboard.app.input.EnterKind
import io.github.ninjin0802.keyboard.app.input.FlickDetector
import io.github.ninjin0802.keyboard.app.input.KeyRepeater
import io.github.ninjin0802.keyboard.app.input.ShiftState
import io.github.ninjin0802.keyboard.app.input.SoftKeyTapAcceptance
import io.github.ninjin0802.keyboard.app.input.SpaceCursorGesture
import io.github.ninjin0802.keyboard.app.input.TouchTracker
import io.github.ninjin0802.keyboard.app.layout.Key
import io.github.ninjin0802.keyboard.app.layout.KeyAction
import io.github.ninjin0802.keyboard.app.layout.KeyboardLayout
import io.github.ninjin0802.keyboard.app.layout.LayoutId
import io.github.ninjin0802.keyboard.app.layout.Layouts
import io.github.ninjin0802.keyboard.app.layout.FoldSplitLayout
import io.github.ninjin0802.keyboard.app.layout.nearestKeyAt

interface KeyListener {
    fun onKey(action: KeyAction, direction: Direction, timeMs: Long)

    /** 空白キーが長押しされた。カーソルを動かす状態に入ってよければ true（未確定文字があるあいだは false）。 */
    fun onSpaceLanguageMenu(x: Float, y: Float): Boolean = false

    /** A space long-press retains the original pointer stream; hover is tracked here. */
    fun onSpaceLanguageMenuMove(x: Float, y: Float) = Unit
    fun onSpaceLanguageMenuRelease(x: Float, y: Float) = Unit
    fun onSpaceLanguageMenuCancel() = Unit

    fun onCursorModeStart(): Boolean

    /** カーソルを [steps] 文字動かす（右が正）。 */
    fun onCursorMove(steps: Int)
}

/**
 * 配列を描き、タッチをキーの入力（フリックの向き付き）にする。描画で割り当てをしない。
 * キーの絵の形は [KeyIcons]、フリックの案内は [FlickPopupView] が受け持つ。
 */
class KeyboardView(context: Context) : View(context) {
    /** The mode selected by the IME is not overwritten by fold/unfold reflow. */
    private var sourceLayout: KeyboardLayout = Layouts.kana
    private var displayedLayout: KeyboardLayout = Layouts.kana
    var layout: KeyboardLayout
        get() = displayedLayout
        set(value) {
            sourceLayout = value
            reflow(width)
            cancelTouch()
            requestLayout()
            invalidate()
        }
    var shift: ShiftState = ShiftState.OFF
        set(value) { field = value; rebuildFaces(); invalidate() }

    /** 確定キーが何をするか。キーの絵を決める。 */
    var enterKind: EnterKind = EnterKind.NEWLINE
        set(value) { field = value; rebuildFaces(); invalidate() }

    /** 数字・記号の配列の戻るキーの行き先。キーに書く文字を決める。 */
    var backTarget: LayoutId = LayoutId.KANA
        set(value) { field = value; rebuildFaces(); invalidate() }
    var listener: KeyListener? = null

    /** フリックの案内を描く View。この View と候補バーの両方に重なっている。 */
    var popup: FlickPopupView? = null

    private var palette = Palette.of(context)
    private val density = resources.displayMetrics.density

    private fun displayHeightDp(): Float = maxOf(
        resources.configuration.screenHeightDp.toFloat(),
        resources.displayMetrics.heightPixels / density,
    )
    private var metrics = KeyboardHeights.forViewport(
        resources.configuration.screenWidthDp.toFloat(), displayHeightDp(),
    )
    private val rowHeightPx: Float get() = metrics.rowDp * density
    private val gapX: Float get() = metrics.gapXDp * density
    private val gapY: Float get() = metrics.gapYDp * density
    private val radius = 12 * density
    private val outerSidePx: Float get() = metrics.sideDp * density
    private val outerTopPx: Float get() = metrics.topDp * density
    private val outerBottomPx: Float get() = metrics.bottomDp * density

    /** Update layout and hit geometry from the same measured viewport. */
    private fun reflow(widthPx: Int) {
        val widthDp = if (widthPx > 0) widthPx / density
                      else resources.configuration.screenWidthDp.toFloat()
        val heightDp = displayHeightDp()
        val nextMetrics = KeyboardHeights.forViewport(widthDp, heightDp)
        val nextLayout = FoldSplitLayout.forWindow(
            sourceLayout, widthDp, heightDp,
            resources.configuration.smallestScreenWidthDp,
        )
        if (nextMetrics != metrics || nextLayout !== displayedLayout) {
            metrics = nextMetrics
            displayedLayout = nextLayout
            updateCells()
            rebuildFaces()
            invalidate()
        }
    }
    private val handler = Handler(Looper.getMainLooper())
    private val repeater = KeyRepeater({ d, t -> handler.postDelayed(t, d) }, { handler.removeCallbacksAndMessages(null) })

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { textAlign = Paint.Align.CENTER }
    private val iconStroke = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE; strokeWidth = 1.8f * density
        strokeCap = Paint.Cap.ROUND; strokeJoin = Paint.Join.ROUND
    }
    private val iconFill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val fm = Paint.FontMetrics()
    private val rect = RectF()

    /** キーごとの、書く文字と、絵の形（絵のないキーは null）。配列・大きさ・⇧・確定キーの種類が変わったときに作り直す。 */
    private var labels: Array<String> = emptyArray()
    private var strokes: Array<Path?> = emptyArray()
    private var fills: Array<Path?> = emptyArray()

    private var cellW = 1f
    private var cellH = 1f

    var hapticEnabled: Boolean = true
        set(value) {
            field = value
            // The setting is independent of the system's dark/light preference.
            isHapticFeedbackEnabled = value
        }
    private var spaceMenuOpened = false
    private var pressed: Key? = null
    private var downX = 0f
    private var downY = 0f
    private var direction = Direction.TAP
    private val location = IntArray(2)

    private val cursorGesture = SpaceCursorGesture(SpaceCursorGesture.STEP_DP * density)
    private val longPress = Runnable {
        val k = pressed
        if (k == null || k.action != KeyAction.Space || !holds(k)) return@Runnable
        if (!cursorGesture.longPress(SystemClock.uptimeMillis())) return@Runnable
        if (layout.id == LayoutId.ROMAJI || layout.id == LayoutId.QWERTY) {
            if (listener?.onSpaceLanguageMenu(downX, downY) == true) {
                spaceMenuOpened = true
                KeyFeedback.play(this, hapticEnabled, KeyFeedback.Kind.LONG_PRESS)
            }
            cursorGesture.cancel()
            return@Runnable
        }
        if (listener?.onCursorModeStart() == true) {
            KeyFeedback.play(this, hapticEnabled, KeyFeedback.Kind.LONG_PRESS)
        } else {
            cursorGesture.cancel()   // 未確定文字がある。離したときに、ふつうの空白キーとして働く。
        }
    }

    init { rebuildFaces() }

    private fun rebuildFaces() {
        val keys = layout.keys
        if (labels.size != keys.size) {
            labels = Array(keys.size) { "" }
            strokes = arrayOfNulls(keys.size)
            fills = arrayOfNulls(keys.size)
        }
        for (i in keys.indices) {
            val k = keys[i]
            labels[i] = when (k.action) {
                is KeyAction.Text, is KeyAction.Romaji -> shift.apply(k.label)
                KeyAction.Back -> Layouts.backLabel(backTarget)
                else -> k.label
            }
            val icon = keyIcon(k.action, shift, enterKind)
            if (icon == null) {
                strokes[i] = null; fills[i] = null
                continue
            }
            val stroke = strokes[i] ?: Path().also { strokes[i] = it }
            val fillPath = fills[i] ?: Path().also { fills[i] = it }
            keyRect(k, rect)
            val size = minOf(ICON_DP * density, rect.height() * 0.3f, rect.width() * 0.3f)
            KeyIcons.build(icon, rect.centerX(), rect.centerY(), size, stroke, fillPath)
        }
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val availableWidth = MeasureSpec.getSize(widthMeasureSpec)
        reflow(availableWidth)
        setMeasuredDimension(
            availableWidth,
            (rowHeightPx * layout.rows + outerTopPx + outerBottomPx).toInt(),
        )
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        reflow(w)
        updateCells()
        rebuildFaces()
    }

    /** 画面が回ったら、作り直されなくても、その向きの高さで測り直す。 */
    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        palette = Palette.of(context)
        hapticEnabled = io.github.ninjin0802.keyboard.app.Settings(context).hapticEnabled
        invalidate()
        cancelTouch()
        layout = sourceLayout
        requestLayout()
    }

    /** 列数・行数が変わると、View の大きさが同じでもセルの大きさが変わる。 */
    private fun updateCells() {
        if (width <= 0 || height <= 0) return
        cellW = (width - 2f * outerSidePx).coerceAtLeast(1f) / layout.columns
        cellH = (height - outerTopPx - outerBottomPx).coerceAtLeast(1f) / layout.rows
    }

    override fun onDraw(canvas: Canvas) {
        // IME container owns the translucent background; do not paint an opaque slab.
        val keys = layout.keys
        for (i in keys.indices) {
            val k = keys[i]
            keyRect(k, rect)
            val isPressed = k === pressed
            val action = k.action
            val isPill = action == KeyAction.Enter
            val isChar = action is KeyAction.Flick || action is KeyAction.Text || action is KeyAction.Romaji || action == KeyAction.Space
            fill.color = when {
                isPill -> if (isPressed) palette.pillPressed else palette.pill
                isChar -> if (isPressed) palette.keyPressed else palette.key
                else -> if (isPressed) palette.functionKeyPressed else palette.functionKey
            }
            // あa と確定キーは、両端を半円にする。
            // Same 12dp corner geometry for Return and letter keys.
            // Color—not a distinct capsule silhouette—marks the commit key.
            val r = radius
            canvas.drawRoundRect(rect, r, r, fill)

            val foreground = if (isPill) palette.onPill else palette.text
            val stroke = strokes[i]
            val fillPath = fills[i]
            if (action == KeyAction.Space && (layout.id == LayoutId.ROMAJI || layout.id == LayoutId.QWERTY || layout.id == LayoutId.SYMBOLS || layout.id == LayoutId.SYMBOLS2)) {
                if (layout.id == LayoutId.ROMAJI) {
                    textPaint.color = palette.subText
                    textPaint.textAlign = Paint.Align.RIGHT
                    textPaint.textSize = 11f * density
                    canvas.drawText("あ", rect.right - 10f * density, rect.bottom - 7f * density, textPaint)
                    textPaint.textAlign = Paint.Align.CENTER
                }
                continue
            }
            if (stroke != null && fillPath != null) {
                iconStroke.color = foreground
                iconFill.color = foreground
                canvas.drawPath(stroke, iconStroke)
                canvas.drawPath(fillPath, iconFill)
            } else {
                val label = labels[i]
                textPaint.color = foreground
                textPaint.textSize = density * when {
                    label.length > 1 -> if (rect.width() < NARROW_KEY_DP * density) 12.5f else 15f
                    action is KeyAction.Flick -> KANA_TEXT_DP
                    else -> 23f
                }
                textPaint.getFontMetrics(fm)
                canvas.drawText(label, rect.centerX(), rect.centerY() - (fm.ascent + fm.descent) / 2, textPaint)
            }
        }
    }

    private fun keyRect(k: Key, out: RectF) {
        out.set(
            outerSidePx + k.col * cellW + gapX / 2,
            outerTopPx + k.row * cellH + gapY / 2,
            outerSidePx + (k.col + k.width) * cellW - gapX / 2,
            outerTopPx + (k.row + k.rowSpan) * cellH - gapY / 2,
        )
    }

    private fun keyAtPoint(x: Float, y: Float): Key? {
        val unitX = (x - outerSidePx) / cellW
        val unitY = (y - outerTopPx) / cellH
        if (x < outerSidePx || x >= width - outerSidePx ||
            y < outerTopPx || y >= height - outerBottomPx) return null
        if ((layout.id == LayoutId.ROMAJI || layout.id == LayoutId.QWERTY) &&
            displayedLayout !== sourceLayout &&
            FoldSplitLayout.isDeadZone(unitX, layout.columns)) return null
        return layout.nearestKeyAt(unitX, unitY)
    }

    private fun fire(action: KeyAction, direction: Direction, time: Long) {
        listener?.onKey(action, direction, time)
    }

    private val tracker = TouchTracker()

    /** [k] の押下が、いまも続いているか。繰り返しと長押しは、時刻が来るたびにこれを確かめる。 */
    private fun holds(k: Key): Boolean = pressed === k && tracker.tracking && isAttachedToWindow && isShown

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                releaseTouch()
                tracker.down(event.getPointerId(0), event.x, event.y, fresh = true)
                if (!startPress(event.x, event.y, event.eventTime)) { tracker.cancel(); return false }
            }
            MotionEvent.ACTION_POINTER_DOWN -> {
                // A second finger cancels this one-finger selection gesture.
                if (spaceMenuOpened) { cancelTouch(); return true }
                val i = event.actionIndex
                val outcome = tracker.down(event.getPointerId(i), event.getX(i), event.getY(i), fresh = false)
                if (outcome == TouchTracker.Outcome.COMPLETE_THEN_START) {
                    finishPress(tracker.completeX, tracker.completeY, event.eventTime)
                }
                // 完了させたキーの動作が配列や画面を切り替えると、その中でタッチが取り消される。そのときは押下を始めない。
                if (tracker.tracking) startPress(event.getX(i), event.getY(i), event.eventTime)
            }
            MotionEvent.ACTION_MOVE -> {
                for (i in 0 until event.pointerCount) {
                    if (tracker.move(event.getPointerId(i), event.getX(i), event.getY(i))) movePress(event.getX(i), event.getY(i))
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                val i = event.actionIndex
                if (tracker.up(event.getPointerId(i), event.getX(i), event.getY(i)) == TouchTracker.Outcome.COMPLETE) {
                    finishPress(tracker.completeX, tracker.completeY, event.eventTime)
                }
            }
            MotionEvent.ACTION_CANCEL -> cancelTouch()
        }
        return true
    }

    private fun startPress(x: Float, y: Float, time: Long): Boolean {
        val k = keyAtPoint(x, y)
        pressed = k
        if (k == null) return false
        downX = x; downY = y
        KeyFeedback.play(this, hapticEnabled, KeyFeedback.Kind.TAP)
        val action = k.action
        when {
            action.repeats -> repeater.start({ holds(k) }) { fire(action, Direction.TAP, SystemClock.uptimeMillis()) }
            action is KeyAction.Flick -> showPopup(k, action)
            action == KeyAction.Space -> {
                cursorGesture.down(x, y, slopPx = cellW * k.width / 4, timeMs = time)
                postDelayed(longPress, SpaceCursorGesture.LONG_PRESS_MS)
            }
        }
        invalidate()
        return true
    }

    private fun showPopup(k: Key, action: KeyAction.Flick) {
        val overlay = popup ?: return
        direction = Direction.TAP
        // この View のいちばん上の段が、案内の View の中でどの高さにあるか。
        getLocationInWindow(location)
        val top = location[1]
        overlay.getLocationInWindow(location)
        overlay.show(k, action.key, cellW, cellH, (top - location[1]).toFloat())
    }

    /** 押している指が動いた。案内の向きが変わったときだけ、案内を描き直す（この View は描き直さない）。 */
    private fun movePress(x: Float, y: Float) {
        val k = pressed ?: return
        if (k.action == KeyAction.Space && spaceMenuOpened) {
            listener?.onSpaceLanguageMenuMove(x, y)
            return
        }
        when (k.action) {
            is KeyAction.Flick -> {
                val d = FlickDetector.direction(x - downX, y - downY, cellW * k.width)
                if (d != direction) { direction = d; popup?.setDirection(d) }
            }
            KeyAction.Space -> {
                val steps = cursorGesture.move(x, y)
                if (steps != 0) listener?.onCursorMove(steps)
            }
            else -> Unit
        }
    }

    /** 押しているキーを (x, y) で指を離したものとして完了させる。 */
    private fun finishPress(x: Float, y: Float, time: Long) {
        val k = pressed
        if (k != null) {
            val action = k.action
            when {
                action.repeats -> repeater.stop()
                action is KeyAction.Flick ->
                    fire(action, FlickDetector.direction(x - downX, y - downY, cellW * k.width), time)
                // カーソルを動かしたあとは、空白を入れない。
                action == KeyAction.Space && spaceMenuOpened -> {
                    // Clear before the callback: switching layouts cancels the active touch.
                    spaceMenuOpened = false
                    listener?.onSpaceLanguageMenuRelease(x, y)
                }
                action == KeyAction.Space && cursorGesture.end() -> Unit
                SoftKeyTapAcceptance.shouldCommit(
                    sameKey = keyAtPoint(x, y) === k,
                    textKey = action is KeyAction.Romaji || action is KeyAction.Text || action == KeyAction.Space,
                    downX = downX,
                    downY = downY,
                    upX = x,
                    upY = y,
                    keyWidthPx = cellW * k.width,
                    rowHeightPx = cellH * k.rowSpan,
                ) -> fire(action, Direction.TAP, time)
            }
        }
        releaseTouch()
    }

    /** 押し続け・案内・カーソルの移動を止める。何も入力しない。 */
    fun cancelTouch() { tracker.cancel(); releaseTouch() }

    private fun releaseTouch() {
        if (spaceMenuOpened) listener?.onSpaceLanguageMenuCancel()
        spaceMenuOpened = false
        repeater.stop()
        removeCallbacks(longPress)
        cursorGesture.cancel()
        popup?.hide()
        if (pressed != null) { pressed = null; invalidate() }
    }

    /** 見えなくなったら（絵文字の画面との入れ替え、キーボードを閉じる）、押下を続けない。 */
    override fun onVisibilityChanged(changedView: View, visibility: Int) {
        super.onVisibilityChanged(changedView, visibility)
        if (visibility != VISIBLE) cancelTouch()
    }

    override fun onWindowVisibilityChanged(visibility: Int) {
        super.onWindowVisibilityChanged(visibility)
        if (visibility != VISIBLE) cancelTouch()
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        hapticEnabled = io.github.ninjin0802.keyboard.app.Settings(context).hapticEnabled
        palette = Palette.of(context)
        invalidate()
    }

    override fun onDetachedFromWindow() {
        cancelTouch()
        super.onDetachedFromWindow()
    }

    companion object {
        /** かなのキーの文字の大きさ。フリックの案内も、選んでいない向きはこの大きさで書く。 */
        const val KANA_TEXT_DP = 22f

        /** 絵の大きさ（中心から端まで）。 */
        private const val ICON_DP = 10.5f

        /** これより狭いキー（かな配列の、幅が半分のキー）には、文字を小さく書く。 */
        private const val NARROW_KEY_DP = 56f
    }
}
