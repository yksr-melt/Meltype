// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

/** ◀ ▶ が何をするか（spec 04「操作キー」の表）。 */
enum class ArrowAction {
    /** 入力欄のカーソルを1文字動かす。 */
    MOVE_CURSOR,

    /** 持っている1文字を確定するだけ。カーソルは動かさない。 */
    COMMIT_ONLY,

    /** カーソルは動かさず、入力セッションに任せる（▶ は、トグル入力で切り替えている文字を決める）。 */
    SESSION,
}

/**
 * [composing]: 未確定文字があるか。[direct]: 最後の1文字だけを持つ配列・欄（英字フリック配列、変換しない欄）か。
 *
 * 持っている1文字を確定するときに、カーソルまで動かさない。▶ は同じ字を続けて打つためのキーでもあり（l ▶ l で ll）、
 * 動かすと次の字を飛びこえたり、ほかの入力欄へ移ったりする。
 */
fun arrowAction(composing: Boolean, direct: Boolean): ArrowAction = when {
    !composing -> ArrowAction.MOVE_CURSOR
    direct -> ArrowAction.COMMIT_ONLY
    else -> ArrowAction.SESSION
}
