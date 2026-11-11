// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import android.content.Context
import android.content.SharedPreferences
import io.github.ninjin0802.keyboard.app.core.CoreConfig
import io.github.ninjin0802.keyboard.app.core.coreDataDirectory
import io.github.ninjin0802.keyboard.app.layout.LayoutId

/**
 * 設定の保存先。値はアプリ専用の領域に残る。
 * キーボードの画面の設定（配列・振動・絵文字など）はここ（SharedPreferences）に、Core（Meltype.Core）の設定は
 * ほかの OS と同じ config.json に持つ（[CoreConfig]）。
 */
class Settings(private val context: Context) {
    private val prefs = context.getSharedPreferences("settings", Context.MODE_PRIVATE)
    private val core by lazy { CoreConfig(coreDataDirectory(context)) }

    var toggleEnabled: Boolean
        get() = prefs.getBoolean(KEY_TOGGLE_ENABLED, false)
        set(value) { prefs.edit().putBoolean(KEY_TOGGLE_ENABLED, value).apply() }

    var toggleWaitMs: Int
        get() = clampWait(prefs.getInt(KEY_TOGGLE_WAIT_MS, DEFAULT_WAIT_MS))
        set(value) { prefs.edit().putInt(KEY_TOGGLE_WAIT_MS, clampWait(value)).apply() }

    /** 日本語を打つ配列（spec 04「設定」の「日本語の入力」）。かな配列かローマ字配列。 */
    var japaneseLayout: LayoutId
        get() = japaneseOf(prefs.getString(KEY_JAPANESE_LAYOUT, null))
        set(value) { prefs.edit().putString(KEY_JAPANESE_LAYOUT, savedName(value)).apply() }

    /** 英字を打つ配列（「英字の入力」）。英字配列か英字フリック配列。 */
    var englishLayout: LayoutId
        get() = englishOf(prefs.getString(KEY_ENGLISH_LAYOUT, null))
        set(value) { prefs.edit().putString(KEY_ENGLISH_LAYOUT, savedName(value)).apply() }

    /** Japanese layout only. The default is a half-width space. */
    var japaneseSpaceFullWidth: Boolean
        get() = prefs.getBoolean(KEY_JAPANESE_SPACE_FULL_WIDTH, false)
        set(value) { prefs.edit().putBoolean(KEY_JAPANESE_SPACE_FULL_WIDTH, value).apply() }

    /** Hides emoji keys/panel and suppresses emoji suggestions when disabled. */
    var emojiEnabled: Boolean
        get() = prefs.getBoolean(KEY_EMOJI_ENABLED, false)
        set(value) { prefs.edit().putBoolean(KEY_EMOJI_ENABLED, value).apply() }

    var hapticEnabled: Boolean
        get() = prefs.getBoolean(KEY_HAPTIC_ENABLED, true)
        set(value) { prefs.edit().putBoolean(KEY_HAPTIC_ENABLED, value).apply() }

    /**
     * ライブ変換（Core の設定 LiveConversion。config.json に書く）。次に打つキーから効く。
     * 前の版で SharedPreferences に残した値は、config.json に移す。
     */
    var liveConversionEnabled: Boolean
        get() = core.boolean(CoreConfig.LIVE_CONVERSION) ?: movedLiveConversion() ?: LIVE_CONVERSION_DEFAULT
        set(value) {
            if (core.set(CoreConfig.LIVE_CONVERSION, value)) prefs.edit().remove(KEY_LIVE_CONVERSION_ENABLED).apply()
        }

    /** 前の版が SharedPreferences に保存したライブ変換の値を config.json に移して返す。保存がなければ null。 */
    private fun movedLiveConversion(): Boolean? {
        if (!prefs.contains(KEY_LIVE_CONVERSION_ENABLED)) return null
        val saved = prefs.getBoolean(KEY_LIVE_CONVERSION_ENABLED, LIVE_CONVERSION_DEFAULT)
        liveConversionEnabled = saved
        return saved
    }

    /** 値が変わったときの通知を受ける。呼び出し側が listener を強い参照で持つこと（保存先は弱い参照しか持たない）。 */
    fun registerChangeListener(listener: SharedPreferences.OnSharedPreferenceChangeListener) =
        prefs.registerOnSharedPreferenceChangeListener(listener)

    fun unregisterChangeListener(listener: SharedPreferences.OnSharedPreferenceChangeListener) =
        prefs.unregisterOnSharedPreferenceChangeListener(listener)

    companion object {
        const val KEY_JAPANESE_LAYOUT = "japanese_layout"
        const val KEY_JAPANESE_SPACE_FULL_WIDTH = "japanese_space_full_width"
        const val KEY_EMOJI_ENABLED = "emoji_enabled"
        const val KEY_HAPTIC_ENABLED = "haptic_enabled"
        /** 前の版がライブ変換を保存していた名前（今は config.json の LiveConversion）。 */
        private const val KEY_LIVE_CONVERSION_ENABLED = "live_conversion_enabled"

        /** config.json に LiveConversion が無いときの値（Core の Settings.LiveConversion の既定）。 */
        private const val LIVE_CONVERSION_DEFAULT = true
        const val KEY_ENGLISH_LAYOUT = "english_layout"

        /** 保存してある値から日本語の配列を決める。未設定・不明な値はローマ字配列。 */
        fun japaneseOf(saved: String?): LayoutId = if (saved == savedName(LayoutId.KANA)) LayoutId.KANA else LayoutId.ROMAJI

        /** 保存してある値から、英語の配列を決める。保存がない・知らない値のときは、最初の値（英字配列）。 */
        fun englishOf(saved: String?): LayoutId = if (saved == savedName(LayoutId.LATIN_FLICK)) LayoutId.LATIN_FLICK else LayoutId.QWERTY

        /** 配列を保存するときの名前。選べない配列（数字・記号）は空で、読むと最初の値になる。 */
        fun savedName(layout: LayoutId): String = when (layout) {
            LayoutId.KANA -> "kana"
            LayoutId.ROMAJI -> "romaji"
            LayoutId.QWERTY -> "qwerty"
            LayoutId.LATIN_FLICK -> "flick"
            LayoutId.SYMBOLS, LayoutId.SYMBOLS2 -> ""
        }

        private const val KEY_TOGGLE_ENABLED = "toggle_enabled"
        private const val KEY_TOGGLE_WAIT_MS = "toggle_wait_ms"
        private const val DEFAULT_WAIT_MS = 1000
        private const val MIN_WAIT_MS = 300
        private const val MAX_WAIT_MS = 2000
        private const val STEP_MS = 100

        /** 300〜2000 の範囲に収め、100 の倍数に丸める（半分は切り上げ）。 */
        fun clampWait(ms: Int): Int {
            val bounded = ms.coerceIn(MIN_WAIT_MS, MAX_WAIT_MS)
            return (bounded + STEP_MS / 2) / STEP_MS * STEP_MS
        }
    }
}
