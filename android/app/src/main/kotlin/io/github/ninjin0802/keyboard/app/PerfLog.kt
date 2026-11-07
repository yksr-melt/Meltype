// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import android.util.Log
import io.github.ninjin0802.keyboard.app.input.ConversionRunner
import io.github.ninjin0802.keyboard.app.layout.KeyAction

/**
 * 性能を測るためのログ。出すのは出来事の種類・文字数・時間だけ（打った文字は出さない）。
 *
 * ふだんは何も出さない。測るときだけ、次で有効にする:
 *   adb shell setprop log.tag.KeyboardPerf DEBUG
 */
internal object PerfLog {
    private const val TAG = "KeyboardPerf"

    val enabled: Boolean get() = Log.isLoggable(TAG, Log.DEBUG)

    /**
     * [kind] は出来事の種類、[micros] はかかった時間。[length] は読みの文字数、[count] は候補の数
     * （どちらも、関係なければ負の数）。
     */
    fun event(kind: String, micros: Long, length: Int = -1, count: Int = -1) {
        if (!enabled) return
        val text = StringBuilder(kind)
        if (length >= 0) text.append(" len=").append(length)
        if (count >= 0) text.append(" n=").append(count)
        text.append(" ms=").append("%d.%03d".format(micros / 1000, micros % 1000))
        Log.d(TAG, text.toString())
    }
}

/**
 * ログに出すキーの種類。クラス名から作らない（release ビルドでは名前が書き換わる）。
 * 文字のキーは、どのキーでも同じ種類にする（どの文字を打ったかが分からないように）。
 */
internal fun perfKind(action: KeyAction): String = when (action) {
    is KeyAction.Flick -> "flick"
    is KeyAction.Romaji -> "romaji"
    is KeyAction.Text -> "text"
    KeyAction.Modify -> "modify"
    KeyAction.ToggleCase -> "case"
    KeyAction.Delete -> "delete"
    KeyAction.Space -> "space"
    KeyAction.Enter -> "enter"
    KeyAction.Left -> "left"
    KeyAction.Right -> "right"
    KeyAction.Shift -> "shift"
    KeyAction.Language -> "language"
    is KeyAction.Switch -> "switch"
    KeyAction.Back -> "back"
    KeyAction.Undo -> "undo"
    KeyAction.Emoji -> "emoji"
}

/**
 * 変換のスレッドで起きた思いがけない失敗を、ログに出す形にする。[step] が null なら Mozc を開く途中。
 * 出すのは、どの作業か、と大まかな種類だけ。例外の中身は打った文字を含みうるので出さない。
 * クラス名も出さない（release ビルドでは書き換わっていて、役に立たない）。
 */
internal fun workerFailureText(step: ConversionRunner.Step?, t: Throwable): String {
    val during = when (step) {
        ConversionRunner.Step.CONVERT -> "convert"
        ConversionRunner.Step.LEARN -> "learn"
        null -> "open"
    }
    return "$during failed (${if (t is Error) "error" else "exception"})"
}
