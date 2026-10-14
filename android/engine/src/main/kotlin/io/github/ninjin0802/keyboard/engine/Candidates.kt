// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

/**
 * 候補バーに並べる一覧。同じ表記は先に出たものだけ残す。読みが空なら空。
 *
 * 予測候補がほかの種類の候補と同じ表記のときは、予測候補のほうを落とす
 * （空白キーは予測候補を飛ばすので、残すと空白で選べる候補が減ってしまう）。
 */
fun buildCandidates(reading: String, segments: List<Segment>?, predictions: List<String>): List<Candidate> {
    if (reading.isEmpty()) return emptyList()
    val all = mutableListOf<Candidate>()
    if (segments != null) {
        val whole = segments.joinToString("") { it.candidates.firstOrNull() ?: it.reading }
        all += Candidate(whole, reading.length, CandidateKind.CONVERSION)
    }
    predictions.forEach { all += Candidate(it, reading.length, CandidateKind.PREDICTION) }
    segments?.firstOrNull()?.let { head ->
        head.candidates.drop(1).forEach { all += Candidate(it, head.reading.length, CandidateKind.FIRST_CLAUSE) }
    }
    all += Candidate(reading, reading.length, CandidateKind.KANA)
    all += Candidate(Kana.toKatakana(reading), reading.length, CandidateKind.KATAKANA)
    val convertible = all.filter { it.kind != CandidateKind.PREDICTION }.mapTo(HashSet()) { it.text }
    return all.filter { it.kind != CandidateKind.PREDICTION || it.text !in convertible }.distinctBy { it.text }
}
