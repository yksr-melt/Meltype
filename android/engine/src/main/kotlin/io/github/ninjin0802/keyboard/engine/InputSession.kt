// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

data class SessionConfig(
    val liveThreshold: Int = 4, val maxReading: Int = 100,
    val toggleEnabled: Boolean = false, val toggleWaitMs: Long = 1000,
    val direct: Boolean = false,          // 変換しない欄（Task 6）
    /** 変換しない欄で、最後に打った1文字を未確定で持つか。パスワードの欄では持たない。[direct] のときだけ意味がある。 */
    val holdLast: Boolean = true,
)

sealed interface Operation {
    data class Insert(val char: Char) : Operation
    data class Tap(val key: KanaKey, val timeMs: Long) : Operation
    data object Modify : Operation
    data object Delete : Operation
    data object Space : Operation
    data object Commit : Operation
    data object Right : Operation
    data class Pick(val index: Int) : Operation
    /** 戻すキー。切り替え中の文字を1つ前に戻す、または候補の選択を解く。 */
    data class Undo(val timeMs: Long) : Operation
    /** ローマ字配列で打った1文字（英字の小文字、- , . '）。 */
    data class Romaji(val char: Char) : Operation
    /** 最後の1文字が英字なら、大文字と小文字を入れ替える。 */
    data object ToggleCase : Operation
}

/** 候補の出どころ。空白キーが選ぶのは [PREDICTION] 以外（変換候補）だけ。 */
enum class CandidateKind { CONVERSION, PREDICTION, FIRST_CLAUSE, KANA, KATAKANA }

/** [consumed]: 読みの先頭から何文字に対応するか。 */
data class Candidate(val text: String, val consumed: Int, val kind: CandidateKind)

data class Update(
    val commit: String = "", val composing: String = "",
    val candidates: List<Candidate> = emptyList(), val selected: Int? = null,
    val request: ConversionRequest? = null,
    val learn: List<Clause>? = null,
)

class InputSession(var config: SessionConfig = SessionConfig()) {
    private val reading = StringBuilder()
    /** ローマ字配列で打った英字のうち、まだかなになっていないもの（spec 05「打ちかけの英字」）。ほかの配列では空。 */
    private val pending = StringBuilder()

    /** 打ちかけが n ひとつ。読みの末尾に ん があるものとして扱う（spec 05「仮の ん」）。次の字で、な行にも ん にもなる。 */
    private val provisionalN: Boolean get() = pending.length == 1 && pending[0] == 'n'

    /** 変換・表示・候補・確定・学習に使う読み。仮の ん を含む。 */
    private fun readingText(): String = if (provisionalN) "$reading$PROVISIONAL_N" else reading.toString()

    /** 読みのあとに、打ったまま出す英字。仮の ん は読みの側にあるので、ここには入らない。 */
    private fun tail(): String = if (provisionalN) "" else pending.toString()
    private var lastRequestId = 0L
    private var pendingId: Long? = null                    // 結果を待っている最新の依頼
    private class Received(val reading: String, val segments: List<Segment>?, val predictions: List<String>)
    private var received: Received? = null                 // 直前に受け取った結果
    private var selected: Int? = null                      // 空白で選んでいる候補の番号
    private var spacePending = false                       // 結果待ちのまま空白が押された

    private class TapRecord(val key: KanaKey, val timeMs: Long)
    private var lastTap: TapRecord? = null                 // 切り替えの途中にあるタップ

    val isComposing: Boolean get() = reading.isNotEmpty() || pending.isNotEmpty()

    /** 変換せず、最後の1文字も持たない欄（パスワード）。打った文字はすぐ確定し、トグル入力は効かない。 */
    private val holdsNothing: Boolean get() = config.direct && !config.holdLast

    fun handle(op: Operation): Update {
        val prevTap = lastTap
        if (op !is Operation.Tap) lastTap = null     // タップ以外は切り替えを終わらせる（「戻す」は undo が続けさせる）
        return dispatch(op, prevTap)
    }

    private fun dispatch(op: Operation, prevTap: TapRecord?): Update = when (op) {
        is Operation.Insert -> insert(op.char)
        is Operation.Tap -> tap(op, prevTap)
        Operation.Modify -> replaceLast(Kana::modify)
        Operation.ToggleCase -> replaceLast(Latin::toggleCase)
        Operation.Delete -> {
            if (selected != null) {
                selected = null
                current()
            } else if (pending.isNotEmpty()) {
                val before = readingText()
                pending.setLength(pending.length - 1)
                refreshed(before)
            } else if (reading.isNotEmpty()) {
                reading.setLength(reading.length - 1)
                changed()
            } else current()
        }
        Operation.Commit -> {
            val text = display()
            val learn = learnForCommit()
            clear()
            Update(commit = text, learn = learn)
        }
        Operation.Space -> if (config.direct) flushDirect() else space()
        is Operation.Pick -> pick(op.index)
        Operation.Right -> current()
        is Operation.Undo -> undo(op, prevTap)
        is Operation.Romaji -> romaji(op.char)
    }

    /** 最新の依頼に対する結果だけ受け取る。それ以外（古い・確定後）は null。 */
    fun onConversion(result: ConversionResult): Update? {
        if (pendingId != result.id) return null
        pendingId = null
        val segments = result.segments
        received = Received(readingText(), segments, result.predictions)
        if (spacePending) {
            spacePending = false
            selectNext()
        }
        return current()
    }

    fun reset() { clear() }

    private fun clear() {
        reading.setLength(0)
        pending.setLength(0)
        pendingId = null
        received = null
        selected = null
        spacePending = false
    }

    /** 読みの最後の1文字を、[replacement] が返す文字に置き換える。置き換えるものがなければ何もしない。 */
    private fun replaceLast(replacement: (Char) -> Char?): Update {
        settlePending()
        val last = reading.lastOrNull()?.let(replacement)
        if (last == null || last == reading.last()) return current()
        reading.setCharAt(reading.length - 1, last)
        return changed()
    }

    /** 打ちかけの英字を読みに移す（n ひとつは ん、ほかは打ったまま）。かなの操作が続くときに使う。 */
    private fun settlePending() {
        if (pending.isEmpty()) return
        reading.append(Romaji.finish(pending.toString()))
        pending.setLength(0)
    }

    private fun tap(op: Operation.Tap, prev: TapRecord?): Update {
        lastTap = TapRecord(op.key, op.timeMs)
        if (prev == null || prev.key != op.key || !isToggling(prev, op.timeMs)) return insert(op.key.cycle[0])
        reading.setCharAt(reading.length - 1, op.key.next(reading.last()))
        return changed()
    }

    /** 選択を解く。そうでなく切り替えの途中なら、1つ前の文字に戻して切り替えを続ける。どちらでもなければ何もしない。 */
    private fun undo(op: Operation.Undo, prev: TapRecord?): Update {
        if (selected != null || spacePending) {
            selected = null
            spacePending = false
            return current()
        }
        if (prev == null || !isToggling(prev, op.timeMs)) return current()
        lastTap = TapRecord(prev.key, op.timeMs)
        reading.setCharAt(reading.length - 1, prev.key.previous(reading.last()))
        return changed()
    }

    /** [prev] のタップで入れた文字を、[timeMs] の時点でまだ切り替えられるか。 */
    private fun isToggling(prev: TapRecord, timeMs: Long): Boolean =
        config.toggleEnabled && timeMs - prev.timeMs <= config.toggleWaitMs &&
            reading.isNotEmpty() && selected == null && !holdsNothing

    /** 変換しない欄: 空白は未確定の1文字を確定するだけ（空白を入れるのは app）。 */
    private fun flushDirect(): Update {
        val text = display()
        clear()
        return Update(commit = text)
    }

    private fun insert(c: Char): Update {
        if (holdsNothing) {
            // 何も持たない欄。すぐ確定する（持っていた文字があれば、それも先に確定する）。
            val held = display()
            clear()
            return Update(commit = held + c)
        }
        val mustCommit = selected != null || reading.length >= config.maxReading ||
            (config.direct && isComposing)
        val committed = if (mustCommit) display() else ""
        val learn = if (mustCommit) learnForCommit() else null
        if (committed.isNotEmpty()) clear()
        settlePending()
        reading.append(c)
        return changed().copy(commit = committed, learn = learn)
    }

    /**
     * ローマ字配列の1文字（spec 05「ローマ字入力」）。できたかなは読みに足し、まだかなにならない分は
     * 打ちかけの英字として持つ。変換を頼むのは、読みが変わったときだけ。
     */
    private fun romaji(c: Char): Update {
        if (holdsNothing) return insert(c)       // パスワードの欄では組み立てない。打った英字をそのまま、すぐ確定する。
        var committed = ""
        var learn: List<Clause>? = null
        if (selected != null) {
            committed = display()
            learn = learnForCommit()
            clear()
        }
        val step = Romaji.feed(pending.toString(), c)
        // 読みを先に確定するのは、変換しない欄で前のかなを持っているときと、足すと上限を超えるとき（仮の ん も数える）。
        // 確定するのは、いまの読みだけ。打ちかけの英字は、できたかなや次の打ちかけとして続く。
        val grows = step.kana.length + (if (step.pending == "n") 1 else 0)
        val full = if (config.direct) step.kana.isNotEmpty() else reading.length + grows > config.maxReading
        if (full && reading.isNotEmpty()) {
            pending.setLength(0)                 // 読みだけの表示と学習にする
            committed += display()
            learn = learnForCommit()
            clear()
        }
        val before = if (committed.isEmpty()) readingText() else ""
        reading.append(step.kana)
        pending.setLength(0)
        pending.append(step.pending)
        return refreshed(before).copy(commit = committed, learn = learn)
    }

    /** 読み（仮の ん を含む）が [before] から変わっていれば、変換を頼み直す。変わっていなければ、表示だけ返す。 */
    private fun refreshed(before: String): Update {
        if (readingText() != before) return changed()
        spacePending = false
        return current()
    }

    /** 読みが変わったとき。表示と、読み全体の変換の依頼を返す。 */
    private fun changed(): Update {
        selected = null
        spacePending = false
        val r = readingText()
        if (r.isEmpty() || config.direct) {
            pendingId = null
            return current()
        }
        val request = ConversionRequest(++lastRequestId, r)
        pendingId = request.id
        return current().copy(request = request)
    }

    private fun current(): Update = Update(composing = display(), candidates = candidates(), selected = selected)

    /** 今の読みに対応する結果だけを使って候補を作る（古い結果は使わない）。 */
    private fun candidates(): List<Candidate> {
        if (config.direct) return emptyList()
        val r = readingText()
        val rec = received?.takeIf { it.reading == r }
        return buildCandidates(r, rec?.segments, rec?.predictions ?: emptyList())
    }

    private fun space(): Update {
        if (readingText().isEmpty()) {
            // 打ちかけの英字だけ（または何もない）。選ぶ候補がないので、打ったまま確定する。
            val text = display()
            clear()
            return Update(commit = text)
        }
        if (selected == null && pendingId != null) {
            spacePending = true
            return current()
        }
        selectNext()
        return current()
    }

    /**
     * 空白で次の候補へ。回るのは変換候補だけで、予測候補は一覧のどこにあっても飛ばす
     * （打っていない長い文が、変換のためのキーで確定されないようにする）。[selected] は一覧全体での番号。
     */
    private fun selectNext() {
        val list = candidates()
        val stops = list.indices.filter { list[it].kind != CandidateKind.PREDICTION }
        if (stops.isEmpty()) return
        val now = selected
        selected = if (now == null) {
            val shown = display()
            stops.firstOrNull { list[it].text != shown } ?: stops.first()
        } else {
            stops.firstOrNull { it > now } ?: stops.first()
        }
    }

    private fun pick(index: Int): Update {
        val candidate = candidates().getOrNull(index) ?: return current()
        val learn = learnFor(candidate)
        // 仮の ん は、残りの読みの末尾にあれば仮のまま続き、確定した分に入っていれば ん に決まる。
        val rest = readingText().substring(candidate.consumed)
        val keepsN = provisionalN && rest.isNotEmpty()
        val tail = if (provisionalN && !keepsN) "" else pending.toString()
        clear()
        reading.append(if (keepsN) rest.dropLast(1) else rest)
        pending.append(tail)
        val update = if (rest.isEmpty()) current() else changed()
        return update.copy(commit = candidate.text, learn = learn)
    }

    private fun learnFor(candidate: Candidate): List<Clause> {
        val r = readingText()
        val head = if (candidate.consumed < r.length) received?.segments?.firstOrNull()?.reading ?: r else r
        return listOf(Clause(head, candidate.text))
    }

    /** 確定したときに Converter へ教える内容。読みのまま確定したときは null。 */
    private fun learnForCommit(): List<Clause>? {
        if (config.direct) return null
        selected?.let { i -> return candidates().getOrNull(i)?.let(::learnFor) }
        val r = readingText()
        if (r.length < config.liveThreshold) return null
        val rec = received ?: return null
        val segments = rec.segments ?: return null
        if (rec.reading != r) return null
        return segments.map { Clause(it.reading, it.candidates.firstOrNull() ?: it.reading) }
    }

    /** 未確定文字として出す内容。確定するときも、このまま確定する。読みの表示のあとに、打ちかけの英字を打ったまま続ける。 */
    private fun display(): String = readingDisplay() + tail()

    private fun readingDisplay(): String {
        val r = readingText()
        selected?.let { i -> candidates().getOrNull(i)?.let { return it.text + r.substring(it.consumed) } }
        if (r.length < config.liveThreshold) return r
        val rec = received ?: return r
        val readingR0 = rec.reading
        val segments = rec.segments ?: return r
        if (!r.startsWith(readingR0)) return r
        return segments.joinToString("") { it.candidates.firstOrNull() ?: it.reading } + r.substring(readingR0.length)
    }

    private companion object {
        const val PROVISIONAL_N = 'ん'
    }
}
