// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import org.json.JSONArray
import org.json.JSONObject

// Core (src/Meltype.Core/Ime) とのやり取りの JSON。出来事 (ImeEvent) を作り、応答 (ImeOutput) を読むのはこのファイルだけ。
// FrontProtocolTests (src/Meltype.Core.Tests) がこのファイルから読む項目の名前を取り出して、Core が出す項目と突き合わせる。
// Core は知らない項目のある出来事を受け持たないので、ImeProtocol.cs に無い項目を入れない。

/**
 * 入力欄の事実 (ImeField)。[before] は確定した文字のうち、キャレット (変換中なら変換中の文字) より前。
 * [selection] は選択している文字 (無ければ null)。[app] は入力欄のアプリのパッケージ名 (EditorInfo.packageName。アプリ別設定の名前)。
 */
internal data class ImeFieldFacts(
    val before: String?,
    val after: String?,
    val composing: Boolean,
    val traits: List<String> = emptyList(),
    val canRemove: Boolean = true,
    val selection: String? = null,
    val app: String? = null,
)

/** 確定する文字。[removeBefore] があれば、キャレットの前のその長さ (UTF-16) を消してから [text] を入れる (照合は Core が済ませている)。 */
internal data class ImeCommit(val text: String, val removeBefore: String?, val moveCaretBack: Int)

/** 一覧は 1 つだけ (変換の候補か予測変換の候補か)。CandidateClicked の番号はこの [items] の番号。 */
internal data class ImeCandidates(val items: List<String>, val selected: Int)

/**
 * 変換中の表示。[clauses] は変換中の文節 (変換していなければ空)、[selectedClause] は候補の一覧を出している文節の番号。
 */
internal data class ImeView(
    val text: String,
    val converting: Boolean,
    val candidates: ImeCandidates?,
    val clauses: List<String> = emptyList(),
    val selectedClause: Int = 0,
)

internal data class ImeResult(val consumed: Boolean, val commits: List<ImeCommit>, val view: ImeView?)

internal object ImeJson {
    /** ImeHost.Version と同じ。 */
    private const val VERSION = 1

    /** Android の入力欄はすべて 1 つの ImeHost の、この 1 つのインスタンスで受け持つ。 */
    private const val INSTANCE = "android"

    /** 文字を入れるキー。vk は付けない (Core が文字から決める)。 */
    fun characterKey(ch: Int, shift: Boolean, field: ImeFieldFacts): String =
        event("KeyPressed", field) { put("key", JSONObject().put("ch", ch).put("shift", shift)) }

    /** Windows の仮想キーコード (Core の VirtualKeys と同じ値)。 */
    const val VK_RETURN = 0x0D
    const val VK_RIGHT = 0x27

    /** 文字の無いキー (Back 0x08・Enter 0x0D・→ 0x27 など)。vk は Windows の仮想キーコード。 */
    fun virtualKey(vk: Int, field: ImeFieldFacts): String =
        event("KeyPressed", field) { put("key", JSONObject().put("vk", vk)) }

    /** 未確定の内容を確定させる。確定する文字は応答で返る。 */
    fun commitRequested(field: ImeFieldFacts?, focusLeft: Boolean = false): String =
        event("CommitRequested", field) { put("caret", caret(focusLeft = focusLeft)) }

    /** 変換中の文字を、入力欄が表示のまま確定した (入力欄が替わった・閉じた)。応答では確定しない。 */
    fun committedByApp(field: ImeFieldFacts?, focusLeft: Boolean = false): String =
        event("CommittedByApp", field) { put("caret", caret(focusLeft = focusLeft)) }

    /** 入力欄に入った。 */
    fun focusEntered(field: ImeFieldFacts?): String =
        event("CaretMoved", field) { put("caret", caret(focusEntered = true)) }

    /** キーボードが書き換えていないのに、キャレットが動いた (タップ・矢印キー・アプリの編集)。 */
    fun caretMoved(field: ImeFieldFacts?): String =
        event("CaretMoved", field) { put("caret", caret()) }

    /** 前の応答の一覧の [index] 番目をタップした。 */
    fun candidateClicked(index: Int, field: ImeFieldFacts?): String =
        event("CandidateClicked", field) { put("index", index) }

    private fun caret(focusEntered: Boolean = false, focusLeft: Boolean = false): JSONObject =
        JSONObject().put("moved", true).put("passedKey", 0).apply {
            if (focusEntered) put("focusEntered", true)
            if (focusLeft) put("focusLeft", true)
        }

    private fun event(type: String, field: ImeFieldFacts?, body: JSONObject.() -> Unit): String =
        JSONObject().apply {
            put("version", VERSION)
            put("type", type)
            put("instance", INSTANCE)
            if (field != null) put("field", fieldJson(field))
            body()
        }.toString()

    private fun fieldJson(field: ImeFieldFacts): JSONObject = JSONObject().apply {
        // null は「読めない」。JSONObject は null の項目を書かないので、Core は省いたものとして null を読む。
        field.before?.let { put("before", it) }
        field.after?.let { put("after", it) }
        field.selection?.let { put("selection", it) }
        put("composing", field.composing)
        field.app?.let { put("app", it) }
        put("traits", JSONArray(field.traits))
        put("canRemove", field.canRemove)
    }

    /** 応答 ({"version","state","result"}) の result を読む。形が違えば例外を投げる (入力欄を書き換える前に)。 */
    fun parse(json: String): ImeResult {
        val result = JSONObject(json).getJSONObject("result")
        val commits = result.getJSONArray("commits")
        return ImeResult(
            consumed = result.getBoolean("consumed"),
            commits = (0 until commits.length()).map { i ->
                val edit = commits.getJSONObject(i)
                ImeCommit(
                    text = edit.getString("text"),
                    removeBefore = if (edit.isNull("removeBefore")) null else edit.getString("removeBefore"),
                    moveCaretBack = edit.getInt("moveCaretBack"),
                )
            },
            view = if (result.isNull("view")) null else view(result.getJSONObject("view")),
        )
    }

    private fun view(view: JSONObject): ImeView = ImeView(
        text = view.getString("text"),
        converting = view.getBoolean("converting"),
        candidates = if (view.isNull("candidates")) null else view.getJSONObject("candidates").let { list ->
            val items = list.getJSONArray("items")
            ImeCandidates((0 until items.length()).map { items.getString(it) }, list.getInt("selected"))
        },
        clauses = view.getJSONArray("clauses").let { clauses -> (0 until clauses.length()).map { clauses.getString(it) } },
        selectedClause = view.getInt("selectedClause"),
    )
}
