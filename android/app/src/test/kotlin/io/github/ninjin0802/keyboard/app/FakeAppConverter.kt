// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.engine.Clause
import io.github.ninjin0802.keyboard.engine.Converter
import io.github.ninjin0802.keyboard.engine.Segment

/** 頼まれた読みと学習の内容を記録する。変換は読みをそのまま1文節で返す。 */
class FakeAppConverter : Converter {
    val converted: MutableList<String> = mutableListOf()
    val contexts: MutableList<String?> = mutableListOf()
    val learned: MutableList<List<Clause>> = mutableListOf()

    override fun convert(reading: String, context: String?): List<Segment>? {
        converted += reading
        contexts += context
        return listOf(Segment(reading, listOf(reading)))
    }

    override fun predict(reading: String): List<String> = emptyList()

    override fun learn(clauses: List<Clause>, context: String?) { learned += clauses }
}
