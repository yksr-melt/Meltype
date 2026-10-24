// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.input

/**
 * 1本の指だけを追う。後から触れた指が来たら、前の押下をその位置で完了させて乗り換える。割り当てなし。
 *
 * [cancel] のあとは、そのとき触れていた指も、あとから触れる指も無視する。全部の指が離れて、
 * 最初の指（fresh）が触れ直すまで。取り消しはキーの動作の中からも起きる（配列や画面の切り替え）。
 * そのとき指の下のキーは入れ替わっているので、どのキーのつもりだったかを当てにいかない。
 */
class TouchTracker {
    enum class Outcome { IGNORE, START, COMPLETE, COMPLETE_THEN_START }

    private var activeId = NONE
    private var lastX = 0f
    private var lastY = 0f
    private var cancelled = false

    /** COMPLETE / COMPLETE_THEN_START のとき、完了させる位置。 */
    var completeX = 0f; private set
    var completeY = 0f; private set

    /** 追っている指があるか。キーの動作を起こしたあとに false なら、その動作の中で取り消されている。 */
    val tracking: Boolean get() = activeId != NONE

    /** [fresh]: ほかに触れている指がない、最初の指（ACTION_DOWN）か。 */
    fun down(id: Int, x: Float, y: Float, fresh: Boolean): Outcome {
        if (fresh) { cancelled = false; activeId = NONE } else if (cancelled) return Outcome.IGNORE
        val had = activeId != NONE
        if (had) { completeX = lastX; completeY = lastY }
        activeId = id; lastX = x; lastY = y
        return if (had) Outcome.COMPLETE_THEN_START else Outcome.START
    }

    /** 追っている指なら位置を更新して true。 */
    fun move(id: Int, x: Float, y: Float): Boolean {
        if (id != activeId) return false
        lastX = x; lastY = y
        return true
    }

    fun up(id: Int, x: Float, y: Float): Outcome {
        if (id != activeId) return Outcome.IGNORE
        completeX = x; completeY = y
        activeId = NONE
        return Outcome.COMPLETE
    }

    fun cancel() { activeId = NONE; cancelled = true }

    private companion object { const val NONE = -1 }
}
