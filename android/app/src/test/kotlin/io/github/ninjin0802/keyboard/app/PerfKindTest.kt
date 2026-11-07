// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import io.github.ninjin0802.keyboard.app.input.ConversionRunner
import io.github.ninjin0802.keyboard.app.layout.KeyAction
import io.github.ninjin0802.keyboard.app.layout.LatinKeys
import io.github.ninjin0802.keyboard.app.layout.LayoutId
import io.github.ninjin0802.keyboard.app.layout.Layouts
import io.github.ninjin0802.keyboard.engine.KanaKeys
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** ログに出す「種類」は、クラス名から作らない（release ビルドでは名前が書き換わる）。打った文字も分からないようにする。 */
class PerfKindTest {
    @Test fun every_flick_key_is_one_kind() =
        assertEquals(setOf("flick"), (KanaKeys.all + LatinKeys.all).map { perfKind(KeyAction.Flick(it)) }.toSet())
    @Test fun every_romaji_key_is_one_kind() =
        assertEquals(setOf("romaji"), "aq-,.".map { perfKind(KeyAction.Romaji(it)) }.toSet())
    @Test fun every_text_key_is_one_kind() =
        assertEquals(setOf("text"), "aZ1@「".map { perfKind(KeyAction.Text(it.toString())) }.toSet())
    @Test fun kinds_of_all_keys_in_all_layouts() {
        val kinds = LayoutId.entries.flatMap { Layouts.of(it).keys }.map { perfKind(it.action) }.toSet()
        assertEquals(setOf("flick", "romaji", "case", "language", "text", "modify", "delete", "space", "enter", "left", "right", "shift", "switch", "back", "undo", "emoji"), kinds)
        assertTrue(kinds.all { k -> k.all { it in 'a'..'z' } })
    }
    @Test fun worker_failure_names_only_the_step_and_a_category() {
        assertEquals("convert failed (exception)", workerFailureText(ConversionRunner.Step.CONVERT, IllegalStateException("かな")))
        assertEquals("learn failed (error)", workerFailureText(ConversionRunner.Step.LEARN, NoClassDefFoundError("x")))
        assertEquals("open failed (exception)", workerFailureText(null, RuntimeException()))
    }
}
