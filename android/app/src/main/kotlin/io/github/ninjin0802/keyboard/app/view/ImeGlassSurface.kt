// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app.view

import android.content.Context
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.view.Window
import android.view.WindowManager

/**
 * Translucent rounded IME surface. Cross-window blur-behind is deliberately
 * forbidden because it blurs the app under the keyboard as well as the IME.
 * Real wallpaper-through transparency depends on OEM compositor behavior.
 */
internal object ImeGlassSurface {
    fun configureWindow(window: Window?, context: Context) {
        val ime = window ?: return
        val d = context.resources.displayMetrics.density
        val corner = 28f * d
        ime.setBackgroundDrawable(GradientDrawable().apply {
            setColor(0x66F4F4F4)
            cornerRadii = floatArrayOf(corner, corner, corner, corner, 0f, 0f, 0f, 0f)
        })
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            ime.setBackgroundBlurRadius((36f * d).toInt())
        }
        ime.clearFlags(
            WindowManager.LayoutParams.FLAG_BLUR_BEHIND or
                WindowManager.LayoutParams.FLAG_DIM_BEHIND,
        )
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            val params = ime.attributes
            params.blurBehindRadius = 0
            params.dimAmount = 0f
            ime.attributes = params
        }
    }

    fun keyboardBackground(context: Context): GradientDrawable {
        val corner = 28f * context.resources.displayMetrics.density
        return GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
            intArrayOf(0x55F4F4F4, 0x77F4F4F4)).apply {
            cornerRadii = floatArrayOf(corner, corner, corner, corner, 0f, 0f, 0f, 0f)
        }
    }
}
