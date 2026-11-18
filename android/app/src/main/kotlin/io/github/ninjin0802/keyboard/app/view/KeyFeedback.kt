// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app.view

import android.content.Context
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.provider.Settings
import android.view.HapticFeedbackConstants
import android.view.View

/**
 * All themes use one feedback implementation. Neither dark/light palette nor
 * the current keyboard layout participates in the haptic decision.
 *
 * Native vibration is used for an IME because View.performHapticFeedback may
 * silently do nothing when a keyboard view is replaced during theme changes.
 */
internal object KeyFeedback {
    enum class Kind { TAP, LONG_PRESS }

    fun shouldPlay(userEnabled: Boolean, systemEnabled: Boolean): Boolean =
        userEnabled && systemEnabled

    fun play(view: View, userEnabled: Boolean, kind: Kind = Kind.TAP) {
        val systemEnabled = try {
            Settings.System.getInt(
                view.context.contentResolver,
                Settings.System.HAPTIC_FEEDBACK_ENABLED,
                1,
            ) != 0
        } catch (_: SecurityException) {
            true
        }
        if (!shouldPlay(userEnabled, systemEnabled)) return
        val vibrator = view.context.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
        if (vibrator?.hasVibrator() == true) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                val effect = if (kind == Kind.TAP)
                    VibrationEffect.EFFECT_TICK else VibrationEffect.EFFECT_CLICK
                vibrator.vibrate(VibrationEffect.createPredefined(effect))
            } else {
                @Suppress("DEPRECATION")
                vibrator.vibrate(if (kind == Kind.TAP) 8L else 14L)
            }
        } else {
            view.performHapticFeedback(
                if (kind == Kind.TAP) HapticFeedbackConstants.KEYBOARD_TAP
                else HapticFeedbackConstants.LONG_PRESS,
            )
        }
    }
}
