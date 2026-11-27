// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package app.meltype.keyboard

import app.meltype.keyboard.dictionary.TypedReading
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class TypedReadingTest {
    @Test fun romajiThatTheCoreCannotConvertIsNotKeptAsTheReading() {
        // shino のまま登録すると、しの で変換できない語が残る
        assertNull(TypedReading.toHiragana("shino") { null })
        assertNull(TypedReading.toHiragana("shino") { throw UnsatisfiedLinkError("libMeltypeNative") })
    }

    @Test fun convertsRomajiAndLeavesKanaAsTyped() {
        assertEquals("しの", TypedReading.toHiragana(" shino ") { if (it == "shino") "しの" else null })
        // 英字の無い読みは Core を呼ばない (呼べなくても登録できる)
        assertEquals("しの", TypedReading.toHiragana(" しの ") { throw UnsatisfiedLinkError("libMeltypeNative") })
    }
}
