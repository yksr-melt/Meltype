// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package app.meltype.keyboard

import app.meltype.keyboard.dictionary.UserDictionaryFile
import app.meltype.keyboard.dictionary.UserWord
import java.io.File
import java.nio.file.Files
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class UserDictionaryFileTest {
    @Test fun readsTheCoreFormatIncludingThePartOfSpeech() {
        assertEquals(
            listOf(
                UserWord("めるたいぷ", "Meltype"),
                UserWord("はしる", "走る", "動詞五段"),
                UserWord("きれい", "綺麗", "名詞"),
            ),
            UserDictionaryFile.parse(
                "﻿# コメント\nめるたいぷ\tMeltype\nはしる\t走る\t動詞五段\r\nきれい\t綺麗\t \n\n読みだけ\n"
            ),
        )
    }

    @Test fun skipsLinesTheCoreDoesNotRead() {
        // 読み・単語が空のもの、制御文字 (ESC) が入ったものは、Core も読まない
        assertEquals(emptyList(), UserDictionaryFile.parse("\tMeltype\nよみ\t\nえすけーぷ\t\u001B\n"))
    }

    @Test fun keepsThePartOfSpeechWhenWritingBack() {
        val words = listOf(UserWord("めるたいぷ", "Meltype"), UserWord("はしる", "走る", "動詞五段"))
        val text = UserDictionaryFile.format(words)
        assertTrue(text.contains("めるたいぷ\tMeltype\n"))
        assertTrue(text.contains("はしる\t走る\t動詞五段\n"))
        assertEquals(words, UserDictionaryFile.parse(text))
    }

    @Test fun editingAWordKeepsItsPartOfSpeechAndTheOtherWords() = withFile("めるたいぷ\tMeltype\nはしる\t走る\t動詞五段\n") { dictionary ->
        assertTrue(dictionary.edit(UserWord("はしる", "走る", "動詞五段"), "はしる", "奔る"))
        assertEquals(listOf(UserWord("めるたいぷ", "Meltype"), UserWord("はしる", "奔る", "動詞五段")), dictionary.list())
    }

    @Test fun addDeleteAndDuplicates() = withFile(null) { dictionary ->
        assertTrue(dictionary.add(" めるたいぷ ", "Meltype"))
        assertTrue(dictionary.add("めるたいぷ", "Meltype"))
        assertTrue(dictionary.add("めるたいぷ", "メルタイプ"))
        assertFalse(dictionary.add("", "空"))
        assertEquals(listOf(UserWord("めるたいぷ", "Meltype"), UserWord("めるたいぷ", "メルタイプ")), dictionary.list())
        assertFalse(dictionary.edit(UserWord("めるたいぷ", "メルタイプ"), "めるたいぷ", "Meltype"))
        dictionary.delete(UserWord("めるたいぷ", "Meltype"))
        assertEquals(listOf(UserWord("めるたいぷ", "メルタイプ")), dictionary.list())
    }

    private fun withFile(text: String?, block: (UserDictionaryFile) -> Unit) {
        val directory = Files.createTempDirectory("userdict").toFile()
        try {
            val file = File(directory, "userdict.txt")
            if (text != null) file.writeText(text)
            block(UserDictionaryFile(file))
        } finally {
            directory.deleteRecursively()
        }
    }
}
