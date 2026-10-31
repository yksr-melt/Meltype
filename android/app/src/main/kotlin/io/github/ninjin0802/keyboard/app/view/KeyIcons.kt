// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.view

import android.graphics.Path
import android.graphics.RectF
import io.github.ninjin0802.keyboard.app.input.EnterKind
import io.github.ninjin0802.keyboard.app.input.ShiftState
import io.github.ninjin0802.keyboard.app.layout.KeyAction

/** キーに描く絵の種類（spec 04「キーの表示と名前」）。 */
enum class KeyIcon { DELETE, SPACE, ENTER, SEARCH, SEND, ARROW, CHECK, LEFT, RIGHT, UNDO, EMOJI, SHIFT, SHIFT_ONCE, SHIFT_LOCKED }

/** [action] のキーに描く絵。文字を書くキーなら null。 */
fun keyIcon(action: KeyAction, shift: ShiftState, enter: EnterKind): KeyIcon? = when (action) {
    KeyAction.Delete -> KeyIcon.DELETE
    KeyAction.Space -> KeyIcon.SPACE
    KeyAction.Enter -> when (enter) {
        EnterKind.NEWLINE -> KeyIcon.ENTER
        EnterKind.SEARCH -> KeyIcon.SEARCH
        EnterKind.SEND -> KeyIcon.SEND
        EnterKind.DONE -> KeyIcon.CHECK
        EnterKind.GO, EnterKind.NEXT, EnterKind.OTHER -> KeyIcon.ARROW
    }
    KeyAction.Left -> KeyIcon.LEFT
    KeyAction.Right -> KeyIcon.RIGHT
    KeyAction.Undo -> KeyIcon.UNDO
    KeyAction.Emoji -> KeyIcon.EMOJI
    KeyAction.Shift -> when (shift) {
        ShiftState.OFF -> KeyIcon.SHIFT
        ShiftState.ONCE -> KeyIcon.SHIFT_ONCE
        ShiftState.LOCKED -> KeyIcon.SHIFT_LOCKED
    }
    is KeyAction.Flick, is KeyAction.Romaji, is KeyAction.Text, KeyAction.Modify, KeyAction.ToggleCase,
    KeyAction.Language, is KeyAction.Switch, KeyAction.Back -> null
}

/**
 * 絵を、線で描く形（stroke）と塗る形（fill）の2つの Path に作る。形はどれも、よくある一般的な記号を
 * 自分で引いたもの。描くたびには呼ばない（大きさや配列が変わったときに作っておく）。
 */
object KeyIcons {
    private val oval = RectF()

    /** 中心 ([cx], [cy])、半分の大きさ [s]（px）。絵は、およそ -1〜1 の正方形に収まるように決めてある。 */
    fun build(icon: KeyIcon, cx: Float, cy: Float, s: Float, stroke: Path, fill: Path) {
        stroke.rewind(); fill.rewind()
        fun Path.move(x: Float, y: Float) = moveTo(cx + x * s, cy + y * s)
        fun Path.line(x: Float, y: Float) = lineTo(cx + x * s, cy + y * s)
        fun Path.circle(x: Float, y: Float, r: Float) = addCircle(cx + x * s, cy + y * s, r * s, Path.Direction.CW)
        fun box(x: Float, y: Float, r: Float) = oval.apply { set(cx + (x - r) * s, cy + (y - r) * s, cx + (x + r) * s, cy + (y + r) * s) }
        when (icon) {
            KeyIcon.DELETE -> {
                // 左向きの札と ×。
                stroke.move(-1.1f, 0f); stroke.line(-0.4f, -0.72f); stroke.line(1.05f, -0.72f); stroke.line(1.05f, 0.72f)
                stroke.line(-0.4f, 0.72f); stroke.close()
                stroke.move(0f, -0.3f); stroke.line(0.6f, 0.3f); stroke.move(0.6f, -0.3f); stroke.line(0f, 0.3f)
            }
            KeyIcon.SPACE -> { stroke.move(-1.2f, -0.15f); stroke.line(-1.2f, 0.4f); stroke.line(1.2f, 0.4f); stroke.line(1.2f, -0.15f) }
            KeyIcon.ENTER -> {
                stroke.move(0.85f, -0.7f); stroke.line(0.85f, 0.2f); stroke.line(-0.85f, 0.2f)
                stroke.move(-0.4f, -0.25f); stroke.line(-0.85f, 0.2f); stroke.line(-0.4f, 0.65f)
            }
            KeyIcon.SEARCH -> { stroke.circle(-0.2f, -0.2f, 0.62f); stroke.move(0.26f, 0.26f); stroke.line(0.9f, 0.9f) }
            KeyIcon.SEND -> {
                // 紙ひこうき。
                fill.move(-0.95f, -0.85f); fill.line(1.05f, 0f); fill.line(-0.95f, 0.85f); fill.line(-0.6f, 0.12f)
                fill.line(0.2f, 0f); fill.line(-0.6f, -0.12f); fill.close()
            }
            KeyIcon.ARROW -> {
                stroke.move(-0.9f, 0f); stroke.line(0.9f, 0f)
                stroke.move(0.35f, -0.55f); stroke.line(0.9f, 0f); stroke.line(0.35f, 0.55f)
            }
            KeyIcon.CHECK -> { stroke.move(-0.85f, 0.05f); stroke.line(-0.3f, 0.6f); stroke.line(0.85f, -0.55f) }
            KeyIcon.LEFT -> { fill.move(-0.6f, 0f); fill.line(0.45f, -0.7f); fill.line(0.45f, 0.7f); fill.close() }
            KeyIcon.RIGHT -> { fill.move(0.6f, 0f); fill.line(-0.45f, -0.7f); fill.line(-0.45f, 0.7f); fill.close() }
            KeyIcon.UNDO -> {
                // 左回りの矢印: 左下から上を回って右下へ下りる弧と、弧の始まりに付けた矢じり。
                stroke.arcTo(box(0.1f, 0.25f, 0.8f), 200f, 195f, true)
                val ax = 0.1f - 0.8f * 0.9397f
                val ay = 0.25f - 0.8f * 0.342f
                stroke.move(ax - 0.12f, ay - 0.62f); stroke.line(ax, ay); stroke.line(ax + 0.6f, ay - 0.1f)
            }
            KeyIcon.EMOJI -> {
                stroke.circle(0f, 0f, 0.95f)
                fill.circle(-0.33f, -0.25f, 0.13f); fill.circle(0.33f, -0.25f, 0.13f)
                stroke.arcTo(box(0f, 0f, 0.55f), 25f, 130f, true)
            }
            KeyIcon.SHIFT, KeyIcon.SHIFT_ONCE, KeyIcon.SHIFT_LOCKED -> {
                // 上向きの矢印。ふだんは線だけ。効いているあいだは塗り、固定のときは下線を足す。
                val locked = icon == KeyIcon.SHIFT_LOCKED
                val up = if (locked) -0.18f else 0f
                for (p in if (icon == KeyIcon.SHIFT) arrayOf(stroke) else arrayOf(stroke, fill)) {
                    p.move(0f, -0.95f + up); p.line(0.95f, 0f + up); p.line(0.42f, 0f + up); p.line(0.42f, 0.8f + up)
                    p.line(-0.42f, 0.8f + up); p.line(-0.42f, 0f + up); p.line(-0.95f, 0f + up); p.close()
                }
                if (locked) { stroke.move(-0.42f, 1.02f); stroke.line(0.42f, 1.02f) }
            }
        }
    }
}
