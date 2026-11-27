// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
package app.meltype.keyboard

import android.content.Context
import android.content.SharedPreferences
import app.meltype.keyboard.core.CoreConfig
import app.meltype.keyboard.core.coreDataDirectory

/**
 * 設定の保存先。値はアプリ専用の領域に残る。
 * キーボードの画面の設定（振動・絵文字）はここ（SharedPreferences）に、Core（Meltype.Core）の設定は
 * ほかの OS と同じ config.json に持つ（[CoreConfig]）。
 */
class Settings(private val context: Context) {
    private val prefs = context.getSharedPreferences("settings", Context.MODE_PRIVATE)
    private val core by lazy { CoreConfig(coreDataDirectory(context)) }

    /** 日本語入力時の Space を全角にするか（Core の設定 SpaceWidth。config.json に書く）。次に打つキーから効く。 */
    var japaneseSpaceFullWidth: Boolean
        get() = core.string(CoreConfig.SPACE_WIDTH) == CoreConfig.SPACE_WIDTH_FULL
        set(value) { core.set(CoreConfig.SPACE_WIDTH, if (value) CoreConfig.SPACE_WIDTH_FULL else CoreConfig.SPACE_WIDTH_STANDARD) }

    /** Hides emoji keys/panel when disabled. */
    var emojiEnabled: Boolean
        get() = prefs.getBoolean(KEY_EMOJI_ENABLED, false)
        set(value) { prefs.edit().putBoolean(KEY_EMOJI_ENABLED, value).apply() }

    var hapticEnabled: Boolean
        get() = prefs.getBoolean(KEY_HAPTIC_ENABLED, true)
        set(value) { prefs.edit().putBoolean(KEY_HAPTIC_ENABLED, value).apply() }

    /** ライブ変換（Core の設定 LiveConversion。config.json に書く）。次に打つキーから効く。 */
    var liveConversionEnabled: Boolean
        get() = core.boolean(CoreConfig.LIVE_CONVERSION) ?: LIVE_CONVERSION_DEFAULT
        set(value) { core.set(CoreConfig.LIVE_CONVERSION, value) }

    /** 値が変わったときの通知を受ける。呼び出し側が listener を強い参照で持つこと（保存先は弱い参照しか持たない）。 */
    fun registerChangeListener(listener: SharedPreferences.OnSharedPreferenceChangeListener) =
        prefs.registerOnSharedPreferenceChangeListener(listener)

    fun unregisterChangeListener(listener: SharedPreferences.OnSharedPreferenceChangeListener) =
        prefs.unregisterOnSharedPreferenceChangeListener(listener)

    companion object {
        const val KEY_EMOJI_ENABLED = "emoji_enabled"
        const val KEY_HAPTIC_ENABLED = "haptic_enabled"

        /** config.json に LiveConversion が無いときの値（Core の Settings.LiveConversion の既定）。 */
        private const val LIVE_CONVERSION_DEFAULT = true
    }
}
