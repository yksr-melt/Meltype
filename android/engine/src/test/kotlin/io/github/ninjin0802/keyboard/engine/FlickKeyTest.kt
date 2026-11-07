// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import kotlin.test.*

/** 文字のない向きを持つキー（英字フリック配列。spec 04）。 */
class FlickKeyTest {
    private val abc = FlickKey("abc${FlickKey.NONE}2")
    private val pqrs = FlickKey("pqrs7")

    @Test fun a_direction_without_a_character_has_none() {
        assertEquals(listOf('a', 'b', 'c', null, '2'), (0..4).map { abc.charAt(it) })
        assertEquals(listOf('p', 'q', 'r', 's', '7'), (0..4).map { pqrs.charAt(it) })
        assertNull(FlickKey("ab").charAt(2)); assertNull(abc.charAt(5))
    }
    @Test fun the_cycle_is_the_characters_in_direction_order_without_the_gaps() {
        assertEquals("abc2", abc.cycle); assertEquals("pqrs7", pqrs.cycle); assertEquals("あいうえお", KanaKeys.A.cycle)
    }
    @Test fun next_and_previous_skip_the_gap_and_wrap() {
        assertEquals(listOf('b', 'c', '2', 'a'), "abc2".map { abc.next(it) })
        assertEquals(listOf('2', 'a', 'b', 'c'), "abc2".map { abc.previous(it) })
        assertEquals('a', abc.next('x')); assertEquals('a', abc.previous(FlickKey.NONE)); assertEquals('a', abc.next(FlickKey.NONE))
    }
    @Test fun kana_keys_are_flick_keys_with_five_characters() {
        for (key in KanaKeys.all) { assertEquals(5, key.cycle.length); assertEquals(key.chars, key.cycle) }
    }
    @Test fun toggle_input_cycles_through_the_digit_and_undo_steps_back() {
        val s = InputSession(SessionConfig(direct = true, toggleEnabled = true))
        val seen = (0L..4L).map { s.handle(Operation.Tap(abc, it * 100)).composing }
        assertEquals(listOf("a", "b", "c", "2", "a"), seen)
        assertEquals("2", s.handle(Operation.Undo(500)).composing); assertEquals("c", s.handle(Operation.Undo(600)).composing)
    }
    @Test fun toggle_case_swaps_the_held_letter_and_ignores_digits_and_symbols() {
        val s = InputSession(SessionConfig(direct = true, toggleEnabled = true))
        s.handle(Operation.Insert('b'))
        assertEquals("B", s.handle(Operation.ToggleCase).composing); assertEquals("b", s.handle(Operation.ToggleCase).composing)
        val digit = s.handle(Operation.Insert('2')); assertEquals("b" to "2", digit.commit to digit.composing)
        assertEquals("" to "2", s.handle(Operation.ToggleCase).let { it.commit to it.composing })
        val symbol = s.handle(Operation.Insert('@')); assertEquals("2" to "@", symbol.commit to symbol.composing)
        assertEquals("@", s.handle(Operation.ToggleCase).composing)
        s.handle(Operation.Commit); assertEquals(Update(), s.handle(Operation.ToggleCase))
    }
    @Test fun toggle_case_fixes_the_character_being_cycled() {
        val s = InputSession(SessionConfig(direct = true, toggleEnabled = true))
        s.handle(Operation.Tap(abc, 0)); s.handle(Operation.Tap(abc, 100))                        // b
        assertEquals("B", s.handle(Operation.ToggleCase).composing)
        val next = s.handle(Operation.Tap(abc, 200)); assertEquals("B" to "a", next.commit to next.composing)
    }
    @Test fun toggle_case_has_nothing_to_act_on_in_a_password_field() {
        val s = InputSession(SessionConfig(direct = true, holdLast = false))
        assertEquals("a", s.handle(Operation.Tap(abc, 0)).commit); assertEquals(Update(), s.handle(Operation.ToggleCase))
    }
    @Test fun latin_case() {
        assertEquals('A', Latin.toggleCase('a')); assertEquals('z', Latin.toggleCase('Z'))
        for (c in "1@.あア ー") assertNull(Latin.toggleCase(c), "$c")
    }
}
