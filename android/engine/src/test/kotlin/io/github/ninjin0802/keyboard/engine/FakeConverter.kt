// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

class FakeConverter(
    val table: Map<String, List<Segment>> = emptyMap(),
    val predictions: Map<String, List<String>> = emptyMap(),
) : Converter {
    val learned: MutableList<List<Clause>> = mutableListOf()
    override fun convert(reading: String, context: String?): List<Segment>? = table[reading]
    override fun predict(reading: String): List<String> = predictions[reading] ?: emptyList()
    override fun learn(clauses: List<Clause>, context: String?) { learned.add(clauses) }
}
