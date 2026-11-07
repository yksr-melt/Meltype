// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.mozc

import android.os.SystemClock
import android.util.Log
import io.github.ninjin0802.keyboard.engine.Clause
import io.github.ninjin0802.keyboard.engine.Converter
import io.github.ninjin0802.keyboard.engine.Segment
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
) : Converter {

    constructor(engine: MozcEngine?) : this(engine, SystemClock::elapsedRealtime)

    override fun convert(reading: String, context: String?): List<Segment>? =
        withSession(reading, context) { session ->
            session.compose()
            var output = session.startConversion()
            val values = output.preedit.segmentList.map { it.value }
            if (values.isEmpty()) return@withSession null
            clauseReadings(output, reading).mapIndexed { index, clauseReading ->
                if (index > 0) output = session.focusNextClause()
                val alternatives = output.allCandidateWords.candidatesList.map { it.value }
                Segment(clauseReading, (listOf(values[index]) + alternatives).filter { it.isNotEmpty() }.distinct())
            }
        }

    override fun predict(reading: String): List<String> =
        withSession(reading, null) { session ->
            session.compose().allCandidateWords.candidatesList.map { it.value }.filter { it.isNotEmpty() }.distinct()
        } ?: emptyList()

    override fun learn(clauses: List<Clause>, context: String?) {
        if (clauses.isEmpty()) return
        val reading = clauses.joinToString("") { it.reading }
        withSession(reading, context) { session ->
            session.compose()
            var output = session.startConversion()
            if (clauseReadings(output, reading) != clauses.map { it.reading }) return@withSession
            clauses.forEachIndexed { index, clause ->
                if (index > 0) output = session.focusNextClause()
                val chosen = output.allCandidateWords.candidatesList.firstOrNull { it.hasId() && it.value == clause.text }
                    ?: return@withSession
                output = session.highlight(chosen.id)
            }
            // 選んだ結果が頼まれた表記と違うなら、違うものを覚えさせないよう、確定しない。
            if (output.preedit.segmentList.joinToString("") { it.value } != clauses.joinToString("") { it.text }) {
                return@withSession
            }
            session.submit()
            session.save()
        }
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

    /** セッションを作り、[block] を実行し、必ずセッションを捨てる。できなければ null。例外は外に出さない。 */
    private fun <T> withSession(reading: String, context: String?, block: (Session) -> T): T? {
        val engine = engine ?: return null
        if (reading.isEmpty() || reading.length > MAX_READING_LENGTH || reading.any { it.isISOControl() }) return null
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
