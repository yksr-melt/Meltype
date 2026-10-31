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

    private val palette = Palette.of(context)
    private val density = resources.displayMetrics.density

    /** 画面の向きで変わるので、測るたびに今の向きから決める。 */
    private val barHeight: Int
        get() = (KeyboardHeights.barDp(resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE) * density).toInt()
    private val row = LinearLayout(context).apply { orientation = LinearLayout.HORIZONTAL }
    private var shown: List<Candidate> = emptyList()
    private var shownSelected: Int? = null

    init {
        isHorizontalScrollBarEnabled = false
        overScrollMode = OVER_SCROLL_NEVER
        setBackgroundColor(palette.background)
        addView(row, LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.MATCH_PARENT))
    }

    fun show(candidates: List<Candidate>, selected: Int?) {
        if (candidates == shown && selected == shownSelected) return
        if (candidates != shown) {
            row.removeAllViews()
            val pad = (14 * density).toInt()
            candidates.forEachIndexed { i, c ->
                row.addView(TextView(context).apply {
                    text = c.text
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 17f)
                    gravity = Gravity.CENTER_VERTICAL
                    setPadding(pad, 0, pad, 0)
                    isSingleLine = true
                    typeface = Typeface.DEFAULT
                    setOnClickListener { onPick?.invoke(i) }
                }, LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.MATCH_PARENT))
            }
            // 並びが変わったら先頭から見せる（前の並びを横に送った位置のままだと、第一候補が隠れる）。
            scrollTo(0, 0)
        }
        shown = candidates
        shownSelected = selected
        for (i in 0 until row.childCount) {
            val tv = row.getChildAt(i) as TextView
            val on = i == selected
            tv.setBackgroundColor(if (on) palette.accent else Color.TRANSPARENT)
            tv.setTextColor(if (on) palette.onAccent else palette.text)
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
        requestLayout()
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        super.onMeasure(widthMeasureSpec, MeasureSpec.makeMeasureSpec(barHeight, MeasureSpec.EXACTLY))
    }
}
