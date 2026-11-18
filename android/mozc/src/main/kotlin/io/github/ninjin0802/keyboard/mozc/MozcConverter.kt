// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.mozc

import android.os.SystemClock
import android.util.Log
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.CompositionMode
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.Context
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.Input
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.KeyEvent
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.Output
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.SessionCommand

/**
 * Mozc でかなを漢字に変える。[engine] が null（初期化に失敗）なら、何も変換しない。
 *
 * 1回の操作ごとに Mozc のセッションを作って捨てる。操作は1つずつ順に行う（同時に呼ばれたら待つ）。
 * 時間がかかるので、メインスレッドの外で呼ぶ。
 */
class MozcConverter internal constructor(
    private val engine: MozcEngine?,
    private val now: () -> Long,
    private val eval: (Input) -> Output? = { engine?.eval(it) },
) : MozcBackend {

    constructor(engine: MozcEngine?) : this(engine, SystemClock::elapsedRealtime)

    override fun convert(reading: String, context: String?): List<MozcSegment>? =
        if (!isConvertible(reading)) emptyList() else withSession(reading, context) { session ->
            session.compose()
            var output = session.startConversion()
            val values = output.preedit.segmentList.map { it.value }
            if (values.isEmpty()) return@withSession emptyList()
            clauseReadings(output, reading).mapIndexed { index, clauseReading ->
                if (index > 0) output = session.focusNextClause()
                val alternatives = output.allCandidateWords.candidatesList.map { it.value }
                MozcSegment(clauseReading, (listOf(values[index]) + alternatives).filter { it.isNotEmpty() }.distinct())
            }
        }

    override fun learn(clauses: List<MozcClause>, context: String?): Boolean {
        if (clauses.isEmpty()) return false
        val reading = clauses.joinToString("") { it.reading }
        return withSession(reading, context) { session ->
            session.compose()
            var output = session.startConversion()
            if (clauseReadings(output, reading) != clauses.map { it.reading }) return@withSession false
            clauses.forEachIndexed { index, clause ->
                if (index > 0) output = session.focusNextClause()
                val chosen = output.allCandidateWords.candidatesList.firstOrNull { it.hasId() && it.value == clause.text }
                    ?: return@withSession false
                output = session.highlight(chosen.id)
            }
            // 選んだ結果が頼まれた表記と違うなら、違うものを覚えさせないよう、確定しない。
            if (output.preedit.segmentList.joinToString("") { it.value } != clauses.joinToString("") { it.text }) {
                return@withSession false
            }
            session.submit()
            session.save()
            true
        } ?: false
    }

    /**
     * 半角の数字は 1 字ずつ読む (2026年 → にぜろにろくねん) ので、全角にして渡し (２０２６年 → ２０２６ねん)、読みの数字は半角に戻す
     * (デスクトップの meltype_mozc_helper と同じ)。
     */
    override fun reading(text: String): String? =
        withSession(text.map { if (it in '0'..'9') '０' + (it - '0') else it }.joinToString(""), null) { session ->
            session.convertReverse().preedit.segmentList.joinToString("") { it.key }
                .map { if (it in '０'..'９') '0' + (it - '０') else it }.joinToString("")
                .takeIf { it.isNotEmpty() }
        }

    /**
     * 文節ごとの読み。Mozc は読みの中の一部の記号を半角にして返す（「（」→「(」、「？」→「?」など）ので、
     * 長さが変わっていなければ、渡した読みを同じ区切りで切って返す。
     */
    private fun clauseReadings(output: Output, reading: String): List<String> {
        val keys = output.preedit.segmentList.map { it.key }
        if (keys.sumOf { it.length } != reading.length) return keys
        var start = 0
        return keys.map { key -> reading.substring(start, start + key.length).also { start += key.length } }
    }

    /** Mozc に渡せる読みか。渡せない読みは変換できないものとして扱う (失敗ではない)。 */
    private fun isConvertible(reading: String): Boolean =
        reading.isNotEmpty() && reading.length <= MAX_READING_LENGTH && reading.none { it.isISOControl() }

    /** セッションを作り、[block] を実行し、必ずセッションを捨てる。できなければ (失敗・時間切れ・渡せない読み) null。例外は外に出さない。 */
    private fun <T> withSession(reading: String, context: String?, block: (Session) -> T): T? {
        val engine = engine ?: return null
        if (!isConvertible(reading)) return null
        // 命令の列が他の操作と混ざらないよう、操作の全体を1つずつ順に行う。
        synchronized(engine) {
            // 締め切りは順番が来てから数える（他の操作を待った時間は入れない）。
            val deadline = now() + TIMEOUT_MS
            val id = eval(Input.newBuilder().setType(Input.CommandType.CREATE_SESSION).build())?.id
            if (id == null || id == 0L) return null
            try {
                val session = Session(eval, now, id, reading, context, deadline)
                session.turnOn()
                val result = block(session)
                // 最後の命令が締め切りを過ぎて終わったら、その結果は使わない。
                if (now() > deadline) throw TimedOut()
                return result
            } catch (e: Exception) {
                // 例外のメッセージには読みが入りうるので、型の名前だけを出す。
                Log.w(TAG, "failed: ${e.javaClass.simpleName}")
                return null
            } finally {
                eval(Input.newBuilder().setType(Input.CommandType.DELETE_SESSION).setId(id).build())
            }
        }
    }

    private class Failed : Exception()
    private class TimedOut : Exception()

    /** 1つのセッションに送る命令。うまくいかなければ例外を投げる（[withSession] が受ける）。 */
    private class Session(
        private val eval: (Input) -> Output?,
        private val now: () -> Long,
        private val id: Long,
        private val reading: String,
        private val precedingText: String?,
        private val deadline: Long,
    ) {
        fun turnOn() = command(withContext = false) {
            setType(SessionCommand.CommandType.TURN_ON_IME).setCompositionMode(CompositionMode.HIRAGANA)
        }

        /** 読みを未確定文字として置く。返るものに予測の候補が入っている。 */
        fun compose() = command(withContext = true) {
            setType(SessionCommand.CommandType.UPDATE_COMPOSITION)
                .addCompositionEvents(SessionCommand.CompositionEvent.newBuilder().setCompositionString(reading))
        }

        /** 変換を始める。返るものに文節の並びと、最初の文節の候補が入っている。 */
        fun startConversion() = key(KeyEvent.SpecialKey.SPACE, withContext = true)

        /** [reading] (漢字かな混じりの文字列) を逆変換する。返るものの文節の key が読み。 */
        fun convertReverse() = command(withContext = false) {
            setType(SessionCommand.CommandType.CONVERT_REVERSE).setText(reading)
        }

        /** 次の文節に移る。返るものに、その文節の候補が入っている。 */
        fun focusNextClause() = key(KeyEvent.SpecialKey.RIGHT, withContext = false)

        fun highlight(candidateId: Int) = command(withContext = false) {
            setType(SessionCommand.CommandType.HIGHLIGHT_CANDIDATE).setId(candidateId)
        }

        fun submit() = command(withContext = false, commits = true) { setType(SessionCommand.CommandType.SUBMIT) }

        /** 学習したことをファイルに書く。 */
        fun save() = send(commits = false) { setType(Input.CommandType.SYNC_DATA) }

        private fun command(
            withContext: Boolean,
            commits: Boolean = false,
            build: SessionCommand.Builder.() -> Unit,
        ) = send(commits) {
            setType(Input.CommandType.SEND_COMMAND).setCommand(SessionCommand.newBuilder().apply(build))
            if (withContext) addContext()
        }

        private fun key(key: KeyEvent.SpecialKey, withContext: Boolean) = send(commits = false) {
            setType(Input.CommandType.SEND_KEY).setKey(KeyEvent.newBuilder().setSpecialKey(key))
            if (withContext) addContext()
        }

        private fun Input.Builder.addContext() {
            if (!precedingText.isNullOrEmpty()) setContext(Context.newBuilder().setPrecedingText(precedingText))
        }

        private fun send(commits: Boolean, build: Input.Builder.() -> Unit): Output {
            if (now() > deadline) throw TimedOut()
            val output = eval(Input.newBuilder().setId(id).apply(build).build()) ?: throw Failed()
            // 確定を頼んでいないのに確定された文字が返ったら、手順がずれている。
            if (output.hasResult() != commits) throw Failed()
            return output
        }
    }

    private companion object {
        const val TAG = "MozcConverter"
        const val MAX_READING_LENGTH = 100
        const val TIMEOUT_MS = 2000L
    }
}
