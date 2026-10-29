// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.view

import android.content.Context
import android.content.res.Configuration
import android.graphics.Color
import android.graphics.Typeface
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.TextView
import io.github.ninjin0802.keyboard.engine.Candidate

/** 変換候補を横に並べる。高さは候補が空でも変わらない（縦画面 44dp、横画面 36dp）。 */
class CandidateBarView(context: Context) : HorizontalScrollView(context) {
    var onPick: ((index: Int) -> Unit)? = null

    private var palette = Palette.of(context)
    private val density = resources.displayMetrics.density

    private val row = LinearLayout(context).apply {
        orientation = LinearLayout.HORIZONTAL
        // Also restores the 12dp leading/trailing inset visible on iOS.
        setPadding((7*density).toInt(), 0, (7*density).toInt(), 0)
    }
    private var shown: List<Candidate> = emptyList()
    private var shownSelected: Int? = null

    init {
        isHorizontalScrollBarEnabled = false
        overScrollMode = OVER_SCROLL_NEVER
        setBackgroundColor(Color.TRANSPARENT)
        setPadding((10*density).toInt(), (10*density).toInt(),
                   (10*density).toInt(), (8*density).toInt())
        clipToPadding = false
        addView(row, LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.MATCH_PARENT))
    }

    fun show(candidates: List<Candidate>, selected: Int?) {
        if (candidates == shown && selected == shownSelected) return
        if (candidates != shown) {
            row.removeAllViews()
            val pad = (17 * density).toInt()
            candidates.forEachIndexed { i, c ->
                val tv = TextView(context).apply {
                    text = c.text
                    contentDescription = "変換候補: " + c.text
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f)
                    gravity = Gravity.CENTER
                    setPadding(pad, 0, pad, 0)
                    minWidth = (64*density).toInt()
                    // TextView.minWidth constrains text, but View.minimumWidth
                    // establishes a touch target. Both are required.
                    minimumWidth = (64*density).toInt()
                    isSingleLine = true
                    includeFontPadding = true
                    typeface = Typeface.DEFAULT
                    setOnClickListener { onPick?.invoke(i) }
                }
                val lp = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.MATCH_PARENT)
                lp.marginStart = (3*density).toInt()
                lp.marginEnd = (3*density).toInt()
                row.addView(tv, lp)
            }
            // 並びが変わったら先頭から見せる（前の並びを横に送った位置のままだと、第一候補が隠れる）。
            scrollTo(0, 0)
        }
        shown = candidates
        shownSelected = selected
        for (i in 0 until row.childCount) {
            val tv = row.getChildAt(i) as TextView
            val on = i == selected
            // Suggestions are text directly on the translucent IME surface.
            // No white pills or selected-item background: touch targets remain
            // full-size TextViews, independent of their visual background.
            tv.background = null
            tv.typeface = if (on) Typeface.DEFAULT_BOLD else Typeface.DEFAULT
            tv.setTextColor(if (on) palette.pill else palette.text)
        }
        if (selected != null && selected in 0 until row.childCount) {
            val target = row.getChildAt(selected)
            post { scrollIntoView(target) }
        }
    }

    private fun scrollIntoView(v: View) {
        val left = v.left
        val right = v.right
        when {
            left < scrollX -> scrollTo(left, 0)
            right > scrollX + width -> scrollTo(right - width, 0)
        }
    }

    /** 画面が回ったら、作り直されなくても、その向きの高さで測り直す。 */
    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        palette = Palette.of(context)
        val currentSelection = shownSelected
        shownSelected = null
        show(shown, currentSelection)
        requestLayout()
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val widthDp = MeasureSpec.getSize(widthMeasureSpec) / density
        val heightDp = maxOf(
            resources.configuration.screenHeightDp.toFloat(),
            resources.displayMetrics.heightPixels / density,
        )
        val barHeight = (KeyboardHeights.forViewport(widthDp, heightDp).barDp * density).toInt()
        super.onMeasure(widthMeasureSpec, MeasureSpec.makeMeasureSpec(barHeight, MeasureSpec.EXACTLY))
    }
}
