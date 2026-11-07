// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

data class Segment(val reading: String, val candidates: List<String>)   // candidates[0] が第一候補
data class Clause(val reading: String, val text: String)

interface Converter {
    fun convert(reading: String, context: String?): List<Segment>?   // 失敗は null。例外を投げない
    fun predict(reading: String): List<String>                         // 失敗は空
    fun learn(clauses: List<Clause>, context: String?)
}

data class ConversionRequest(val id: Long, val reading: String)
data class ConversionResult(val id: Long, val segments: List<Segment>?, val predictions: List<String>)

fun Converter.run(request: ConversionRequest, context: String?): ConversionResult {
    val segments = convert(request.reading, context)
        ?.takeIf { segs -> segs.joinToString("") { it.reading } == request.reading }
    return ConversionResult(request.id, segments, predict(request.reading))
}
