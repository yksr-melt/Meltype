// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * What the Android front must get right around the native Core's edits: re-commits
 * (removeBefore), moving the caret back into a commit, a caret moved elsewhere, and leaving an
 * editor while composing. The events are the ones KeyboardService sends (ImeJson).
 */
class CoreEditingDeviceTest {
    private val core = DeviceTestCore.open()

    @After fun closeCore() = core.close()

    /** Types into [editor] through the Core, sending each key as KeyboardService does. */
    private inner class Typist(var editor: TestEditor = TestEditor()) {
        val adapter = CoreInputAdapter { editor }

        fun field() = adapter.field(emptyList(), app = "io.github.ninjin0802.test")

        fun send(event: String): Boolean = adapter.apply(core.handle(event) ?: error("The Core returned nothing for $event"))

        /** '\n' is Enter. A key the Core passes is typed by the "app". */
        fun type(keys: String) {
            for (key in keys) {
                if (key == '\n') enter()
                else if (!send(ImeJson.characterKey(key.code, false, field()))) editor.commitText(key.toString(), 1)
            }
        }

        fun enter() {
            send(ImeJson.virtualKey(0x0D, field()))
        }
    }

    @Test fun aRecommitReplacesThePreviousWordInTheEditor() {
        // i converted with Space and committed by the next key turns into I once want shows the
        // sentence is English (as on the desktop: CompositionTests.AutoCorrect_JapaneseToEnglishAfterCommit).
        val typist = Typist()
        typist.type("i want ")
        assertEquals("I want ", typist.editor.content)
        assertEquals(7, typist.editor.caret)
        assertEquals(-1, typist.editor.composingStart)
    }

    @Test fun aCommittedWordCanBeRecommittedWhenCaretHasNotMoved() {
        // Keep this in its own Core host. ImeHost's "android" instance is stateful across editors.
        val stays = Typist()
        stays.type("i \n")
        stays.type("want ")
        assertTrue("Not re-committed: " + stays.editor.content, stays.editor.content.startsWith("I"))
    }

    @Test fun aCaretMovedElsewhereKeepsThePreviousWord() {
        // A distinct host reproduces a fresh editor: the first scenario must not leak into this one.
        val moved = Typist()
        moved.type("i \n")
        val committed = moved.editor.content
        moved.send(ImeJson.caretMoved(moved.field()))
        moved.type("want ")
        assertEquals(committed + "want ", moved.editor.content)
    }

    @Test fun removeBeforeTakesAwayTheComposingTextFirst() {
        val editor = TestEditor()
        editor.commitText("い ", 1)
        val adapter = CoreInputAdapter { editor }
        adapter.apply(response("[]", """{"text":"want","converting":false,"clauses":[],"selectedClause":0,"hint":"","spellingSuggestion":null,"typedKeys":null,"candidates":null}"""))
        assertEquals("い want", editor.content)
        adapter.apply(response("""[{"text":"I want ","removeBefore":"い ","currentWord":null,"moveCaretBack":0,"insertByPaste":false}]""", "null"))
        assertEquals("I want ", editor.content)
        assertEquals(7, editor.caret)
        assertEquals(-1, editor.composingStart)
        assertTrue(!adapter.composing)
    }

    @Test fun moveCaretBackPutsTheCaretInsideTheCommit() {
        val editor = TestEditor()
        editor.commitText("abc", 1)
        val adapter = CoreInputAdapter { editor }
        adapter.apply(response("[]", """{"text":"[]","converting":false,"clauses":[],"selectedClause":0,"hint":"","spellingSuggestion":null,"typedKeys":null,"candidates":null}"""))
        adapter.apply(response("""[{"text":"「」","removeBefore":null,"currentWord":null,"moveCaretBack":1,"insertByPaste":false}]""", "null"))
        assertEquals("abc「」", editor.content)
        assertEquals(4, editor.caret)
    }

    @Test fun leavingAnEditorWhileComposingKeepsTheTextThereAndStartsAfresh() {
        val first = TestEditor()
        val typist = Typist(first)
        typist.type("ka")
        assertTrue(typist.adapter.composing)
        // The editor keeps the composing text when it loses focus (KeyboardService.leaveCore).
        first.finishComposingText()
        typist.adapter.clear()
        typist.send(ImeJson.committedByApp(null, focusLeft = true))
        assertEquals("か", first.content)

        val second = TestEditor()
        typist.editor = second
        typist.send(ImeJson.focusEntered(typist.field()))
        typist.type("ki")
        typist.enter()
        assertEquals("か", first.content)
        assertEquals("き", second.content)
    }

    @Test fun anEditorWithoutItsOwnTextSendsNoTextAndCannotRemove() {
        val editor = TestEditor()
        editor.commitText("abc", 1)
        val facts = CoreInputAdapter { editor }.field(emptyList(), readable = false)
        assertNull(facts.before)
        assertNull(facts.after)
        assertTrue(!facts.canRemove)
    }

    @Test fun theSelectedTextIsSentOnlyWhenTheEditorReportedASelection() {
        val editor = TestEditor()
        editor.commitText("abcd", 1)
        editor.setSelection(1, 3)
        val adapter = CoreInputAdapter { editor }
        assertEquals("bc", adapter.field(emptyList(), selected = true).selection)
        assertEquals("a", adapter.field(emptyList(), selected = true).before)
        assertNull(adapter.field(emptyList(), selected = false).selection)
    }

    /** A whole response as the Core writes it (ImeOutput). */
    private fun response(commits: String, view: String) =
        """{"version":1,"state":{},"result":{"consumed":true,"commits":$commits,"view":$view}}"""
}
