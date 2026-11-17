// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import android.app.Activity
import android.graphics.Typeface
import android.os.Bundle
import android.widget.ScrollView
import android.widget.TextView
import io.github.ninjin0802.keyboard.app.view.Palette

/** ライセンスの表示。長いので、読み込みは別のスレッドで行う。 */
class LicensesActivity : Activity() {
    private lateinit var text: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val palette = Palette.of(this)
        val pad = (16 * resources.displayMetrics.density).toInt()
        text = TextView(this).apply {
            typeface = Typeface.MONOSPACE
            textSize = 12f
            setTextColor(palette.text)
            setPadding(pad, pad, pad, pad)
            setTextIsSelectable(true)
            this.text = getString(R.string.licenses_loading)
        }
        setContentView(ScrollView(this).apply {
            setBackgroundColor(palette.background)
            addView(text)
            padForSystemBars()
        })
        title = getString(R.string.licenses_link)
        val assets = applicationContext.assets
        Thread {
            val all = FILES.joinToString("\n\n") { name ->
                "==== $name ====\n" + try {
                    assets.open(name).bufferedReader(Charsets.UTF_8).use { it.readText() }
                } catch (e: java.io.IOException) {
                    "(読み込めませんでした)"
                }
            }
            runOnUiThread { if (!isDestroyed) text.text = all }
        }.start()
    }

    companion object {
        private val FILES = listOf("THIRD-PARTY-NOTICES.md", "LICENSE", "MOZC_NOTICES.txt")
    }
}
