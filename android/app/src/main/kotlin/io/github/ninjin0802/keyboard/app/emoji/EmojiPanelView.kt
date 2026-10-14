// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.emoji

import android.content.Context
import android.content.res.Configuration
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.os.SystemClock
import android.view.View
import android.view.ViewTreeObserver
import android.widget.LinearLayout
import io.github.ninjin0802.keyboard.app.PerfLog
import io.github.ninjin0802.keyboard.app.input.EnterKind
import io.github.ninjin0802.keyboard.app.layout.LayoutId
import io.github.ninjin0802.keyboard.app.layout.Layouts
import io.github.ninjin0802.keyboard.app.view.KeyboardView
import io.github.ninjin0802.keyboard.app.view.Palette

/**
 * 絵文字の画面（spec 04）。上から、種類のタブ、絵文字のます目、操作キーの行。
 * 合わせた高さは、候補バーとキー4段を合わせた高さと同じ（[EmojiPanelHeights]）。
 */
class EmojiPanelView(context: Context) : LinearLayout(context) {
    /** 絵文字がタップされた。 */
    var onPick: ((String) -> Unit)?
        get() = grid.onPick
        set(value) { grid.onPick = value }

    /** いまの「最近使ったもの」。タブを選んだときに読む。 */
    var recents: () -> List<String> = { emptyList() }

    /** 操作キーの行（戻る、空白、削除、確定）。押されたキーは、キーボードのキーと同じ相手に知らせる。 */
    val keys = KeyboardView(context)

    private val palette = Palette.of(context)
    private val density = resources.displayMetrics.density
    private val grid = EmojiGridView(context)
    private val tabs = EmojiCategory.entries.map { category ->
        TabView(category).apply { setOnClickListener { select(category) } }
    }
    private var catalog: Map<EmojiCategory, Array<String>> = emptyMap()
    private var selected = EmojiCategory.SMILEYS

    init {
        orientation = VERTICAL
        val strip = object : LinearLayout(context) {
            /** 高さは候補バーと同じ。画面の向きで変わるので、測るたびに今の向きから決める。 */
            override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
                val landscape = resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE
                val height = (EmojiPanelHeights.tabsDp(landscape) * density).toInt()
                super.onMeasure(widthMeasureSpec, MeasureSpec.makeMeasureSpec(height, MeasureSpec.EXACTLY))
            }
        }
        for (tab in tabs) strip.addView(tab, LayoutParams(0, LayoutParams.MATCH_PARENT, 1f))
        addView(strip, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT))
        addView(grid, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT))
        addView(keys, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT))
    }

    /**
     * 開くときに呼ぶ。[origin] は開く前の配列（戻るキーの行き先）、[enterKind] は確定キーの絵。
     * [showRecents] が false の欄（パスワード）では、「最近使ったもの」のタブを出さず、覚えてある絵文字も読まない。
     * [pressedAtMs] は絵文字キーが押された時刻（性能のログ用）。
     */
    fun open(origin: LayoutId, enterKind: EnterKind, showRecents: Boolean, pressedAtMs: Long) {
        val bar = Layouts.emojiBar(origin)
        if (keys.layout !== bar) keys.layout = bar
        if (keys.enterKind != enterKind) keys.enterKind = enterKind
        val offered = EmojiTabs.offered(showRecents)
        for (tab in tabs) tab.visibility = if (tab.category in offered) VISIBLE else GONE
        select(EmojiTabs.initial(showRecents, hasRecents = showRecents && recents().isNotEmpty()))
        EmojiCatalog.load(context) { loaded ->
            if (catalog === loaded) return@load
            catalog = loaded
            for (tab in tabs) tab.glyph = tab.category.tabEmoji?.let { wanted ->
                val list = loaded[tab.category].orEmpty()
                // タブの絵に使う絵文字を端末が描けないときは、その種類の先頭の絵文字。
                if (wanted in list) wanted else list.firstOrNull() ?: wanted
            }
            if (selected != EmojiCategory.RECENT) select(selected)
        }
        if (PerfLog.enabled) logFirstDraw(pressedAtMs)
    }

    /** スクロールと、押しているキー（削除の繰り返しなど）を止める。何も入力しない。 */
    fun cancelTouch() {
        grid.cancelTouch()
        keys.cancelTouch()
    }

    private fun select(category: EmojiCategory) {
        selected = category
        for (tab in tabs) tab.isSelected = tab.category == category
        grid.emptyText = EmojiTabs.emptyNote(category)
        grid.show(if (category == EmojiCategory.RECENT) recents().toTypedArray() else catalog[category] ?: emptyArray())
    }

    /** 絵文字キーが押されてから、この画面が最初に描かれるまでの時間を出す。 */
    private fun logFirstDraw(pressedAtMs: Long) {
        viewTreeObserver.addOnPreDrawListener(object : ViewTreeObserver.OnPreDrawListener {
            override fun onPreDraw(): Boolean {
                viewTreeObserver.removeOnPreDrawListener(this)
                post { PerfLog.event("emoji-open", (SystemClock.uptimeMillis() - pressedAtMs) * 1000) }
                return true
            }
        })
    }

    /** タブ1つ。名前は読み上げ用で、画面には絵（[glyph]。null なら時計）を出す。 */
    private inner class TabView(val category: EmojiCategory) : View(context) {
        var glyph: String? = category.tabEmoji
            set(value) { if (field != value) { field = value; invalidate() } }

        private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            textAlign = Paint.Align.CENTER; textSize = TAB_TEXT_DP * density
            strokeWidth = 1.8f * density; strokeCap = Paint.Cap.ROUND
        }
        private val baseline = Paint.FontMetrics().let { paint.getFontMetrics(it); -(it.ascent + it.descent) / 2 }
        private val rect = RectF()

        init {
            contentDescription = category.title
        }

        override fun setSelected(selected: Boolean) {
            if (isSelected != selected) { super.setSelected(selected); invalidate() }
        }

        override fun onDraw(canvas: Canvas) {
            val cx = width / 2f
            val cy = height / 2f
            paint.style = Paint.Style.FILL
            if (isSelected) {
                val gap = 3 * density
                rect.set(gap, gap, width - gap, height - gap)
                paint.color = palette.key
                canvas.drawRoundRect(rect, 10 * density, 10 * density, paint)
                // 選んでいる印: 下に強調の色の短い線。
                rect.set(cx - 9 * density, height - gap - 3 * density, cx + 9 * density, height - gap)
                paint.color = palette.accent
                canvas.drawRoundRect(rect, 1.5f * density, 1.5f * density, paint)
            }
            paint.color = palette.text
            val text = glyph
            if (text != null) {
                canvas.drawText(text, cx, cy + baseline, paint)
            } else {
                // 時計: 円と、12 時と 3 時すぎを指す2本の針。
                val r = 9 * density
                canvas.drawLine(cx, cy, cx, cy - r * 0.6f, paint)
                canvas.drawLine(cx, cy, cx + r * 0.42f, cy + r * 0.25f, paint)
                paint.style = Paint.Style.STROKE
                canvas.drawCircle(cx, cy, r, paint)
            }
        }
    }

    private companion object {
        /** タブに出す絵文字の大きさ。 */
        const val TAB_TEXT_DP = 20f
    }
}
