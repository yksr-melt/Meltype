// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package app.meltype.keyboard.mozc

import android.os.SystemClock
import android.util.Log
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCandidateWindow.CandidateAttribute
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.CompositionMode
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.Context
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.Input
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.KeyEvent
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.Output
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.SessionCommand
import org.mozc.android.inputmethod.japanese.protobuf.ProtoUserDictionaryStorage.UserDictionary
import org.mozc.android.inputmethod.japanese.protobuf.ProtoUserDictionaryStorage.UserDictionaryStorage
import java.io.File
import java.io.FileOutputStream
import java.io.IOException

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
                val words = output.allCandidateWords.candidatesList
                val userWord = words.any { it.value == values[index] && CandidateAttribute.USER_DICTIONARY in it.attributesList }
                MozcSegment(clauseReading, (listOf(values[index]) + words.map { it.value }).filter { it.isNotEmpty() }.distinct(), userWord)
            }
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
     * Mozc の辞書の取り込み (IMPORT_USER_DICTIONARY) は終わるのを待てない (非同期) ので、ユーザー辞書のファイルを書いてから、
     * 待てる読み直し (RELOAD_AND_WAIT) で読ませる。ファイルには Meltype の語だけを書く (毎回 Core の語から作る)。
     */
    override fun setUserDictionary(words: List<MozcWord>): Boolean {
        val engine = engine ?: return false
        val dictionary = UserDictionary.newBuilder().setId(1).setName("Meltype")
        for (word in words) {
            dictionary.addEntries(UserDictionary.Entry.newBuilder().setKey(word.reading).setValue(word.word).setPos(POS[word.pos] ?: UserDictionary.PosType.NOUN))
        }
        val storage = UserDictionaryStorage.newBuilder().addDictionaries(dictionary).build()
        // 変換の命令の列と混ざらないよう、ほかの操作と同じく 1 つずつ順に行う
        return synchronized(engine) {
            write(engine.userDictionaryFile, storage.toByteArray()) &&
                eval(Input.newBuilder().setType(Input.CommandType.RELOAD_AND_WAIT).build()) != null
        }
    }

    // 途中で止まっても半端なものが残らないよう、別の名前で書いてから置き換える
    private fun write(file: File, bytes: ByteArray): Boolean {
        val temporary = File(file.path + ".tmp")
        return try {
            FileOutputStream(temporary).use { it.write(bytes); it.fd.sync() }
            temporary.renameTo(file)
        } catch (e: IOException) {
            false
        } finally {
            temporary.delete()
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

        private fun command(withContext: Boolean, build: SessionCommand.Builder.() -> Unit) = send {
            setType(Input.CommandType.SEND_COMMAND).setCommand(SessionCommand.newBuilder().apply(build))
            if (withContext) addContext()
        }

        private fun key(key: KeyEvent.SpecialKey, withContext: Boolean) = send {
            setType(Input.CommandType.SEND_KEY).setKey(KeyEvent.newBuilder().setSpecialKey(key))
            if (withContext) addContext()
        }

        private fun Input.Builder.addContext() {
            if (!precedingText.isNullOrEmpty()) setContext(Context.newBuilder().setPrecedingText(precedingText))
        }

        private fun send(build: Input.Builder.() -> Unit): Output {
            if (now() > deadline) throw TimedOut()
            val output = eval(Input.newBuilder().setId(id).apply(build).build()) ?: throw Failed()
            // 確定を頼んでいないのに確定された文字が返ったら、手順がずれている (Mozc に学習させない)。
            if (output.hasResult()) throw Failed()
            return output
        }
    }

    private companion object {
        const val TAG = "MozcConverter"
        const val MAX_READING_LENGTH = 100
        const val TIMEOUT_MS = 2000L
    }
}

// Mozc の品詞名 (user_dictionary_storage.proto の PosType の注釈) → 品詞。ほかの日本語入力の品詞名 (ら行五段など) は知らないので名詞にする
private val POS = mapOf(
    "名詞" to UserDictionary.PosType.NOUN,
    "短縮よみ" to UserDictionary.PosType.ABBREVIATION,
    "サジェストのみ" to UserDictionary.PosType.SUGGESTION_ONLY,
    "固有名詞" to UserDictionary.PosType.PROPER_NOUN,
    "人名" to UserDictionary.PosType.PERSONAL_NAME,
    "姓" to UserDictionary.PosType.FAMILY_NAME,
    "名" to UserDictionary.PosType.FIRST_NAME,
    "組織" to UserDictionary.PosType.ORGANIZATION_NAME,
    "地名" to UserDictionary.PosType.PLACE_NAME,
    "名詞サ変" to UserDictionary.PosType.SA_IRREGULAR_CONJUGATION_NOUN,
    "名詞形動" to UserDictionary.PosType.ADJECTIVE_VERBAL_NOUN,
    "数" to UserDictionary.PosType.NUMBER,
    "アルファベット" to UserDictionary.PosType.ALPHABET,
    "記号" to UserDictionary.PosType.SYMBOL,
    "顔文字" to UserDictionary.PosType.EMOTICON,
    "副詞" to UserDictionary.PosType.ADVERB,
    "連体詞" to UserDictionary.PosType.PRENOUN_ADJECTIVAL,
    "接続詞" to UserDictionary.PosType.CONJUNCTION,
    "感動詞" to UserDictionary.PosType.INTERJECTION,
    "接頭語" to UserDictionary.PosType.PREFIX,
    "助数詞" to UserDictionary.PosType.COUNTER_SUFFIX,
    "接尾一般" to UserDictionary.PosType.GENERIC_SUFFIX,
    "接尾人名" to UserDictionary.PosType.PERSON_NAME_SUFFIX,
    "接尾地名" to UserDictionary.PosType.PLACE_NAME_SUFFIX,
    "動詞ワ行五段" to UserDictionary.PosType.WA_GROUP1_VERB,
    "動詞カ行五段" to UserDictionary.PosType.KA_GROUP1_VERB,
    "動詞サ行五段" to UserDictionary.PosType.SA_GROUP1_VERB,
    "動詞タ行五段" to UserDictionary.PosType.TA_GROUP1_VERB,
    "動詞ナ行五段" to UserDictionary.PosType.NA_GROUP1_VERB,
    "動詞マ行五段" to UserDictionary.PosType.MA_GROUP1_VERB,
    "動詞ラ行五段" to UserDictionary.PosType.RA_GROUP1_VERB,
    "動詞ガ行五段" to UserDictionary.PosType.GA_GROUP1_VERB,
    "動詞バ行五段" to UserDictionary.PosType.BA_GROUP1_VERB,
    "動詞ハ行四段" to UserDictionary.PosType.HA_GROUP1_VERB,
    "動詞一段" to UserDictionary.PosType.GROUP2_VERB,
    "動詞カ変" to UserDictionary.PosType.KURU_GROUP3_VERB,
    "動詞サ変" to UserDictionary.PosType.SURU_GROUP3_VERB,
    "動詞ザ変" to UserDictionary.PosType.ZURU_GROUP3_VERB,
    "動詞ラ変" to UserDictionary.PosType.RU_GROUP3_VERB,
    "形容詞" to UserDictionary.PosType.ADJECTIVE,
    "終助詞" to UserDictionary.PosType.SENTENCE_ENDING_PARTICLE,
    "句読点" to UserDictionary.PosType.PUNCTUATION,
    "独立語" to UserDictionary.PosType.FREE_STANDING_WORD,
    "抑制単語" to UserDictionary.PosType.SUPPRESSION_WORD,
)
