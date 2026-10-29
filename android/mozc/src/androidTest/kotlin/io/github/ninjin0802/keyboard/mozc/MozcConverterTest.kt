// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.mozc

import androidx.test.platform.app.InstrumentationRegistry
import io.github.ninjin0802.keyboard.engine.Clause
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.io.RandomAccessFile
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.Input
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands.SessionCommand

class MozcConverterTest {
    private val ctx get() = InstrumentationRegistry.getInstrumentation().targetContext
    private val converter by lazy { MozcConverter(MozcEngine.open(ctx)) }
    private fun text(reading: String) = converter.convert(reading, null)?.joinToString("") { it.candidates[0] }

    @Test fun engine_opens() = assertNotNull(MozcEngine.open(ctx))

    @Test fun converts_sentence() = assertEquals("今日は天気がいい", text("きょうはてんきがいい"))

    @Test fun segment_readings_join_to_input() {
        val r = "わたしのなまえはなかのです"
        assertEquals(r, converter.convert(r, null)!!.joinToString("") { it.reading })
    }

    // Mozc は記号を半角にして返すが、文節の読みは渡したものと同じでなければならない。
    @Test fun segment_readings_keep_symbols_as_given() {
        for (r in listOf("（あめ）", "？！…〜ー", "きょうは、あめ。")) {
            assertEquals(r, converter.convert(r, null)!!.joinToString("") { it.reading })
        }
    }

    @Test fun each_segment_has_alternatives() =
        assertTrue(converter.convert("あめがふる", null)!![0].candidates.size > 1)

    @Test fun candidates_have_no_duplicates() {
        for (segment in converter.convert("あめがふる", null)!!) {
            assertEquals(segment.candidates.size, segment.candidates.toSet().size)
        }
    }

    @Test fun context_is_accepted() = assertNotNull(converter.convert("あめがふる", "きょうは"))

    @Test fun empty_and_too_long_return_null() {
        assertNull(converter.convert("", null))
        assertNull(converter.convert("あ".repeat(101), null))
    }

    @Test fun control_characters_return_null() = assertNull(converter.convert("あ\nめ", null))

    @Test fun hundred_chars_convert() {
        val r = "あ".repeat(100)
        assertEquals(r, converter.convert(r, null)!!.joinToString("") { it.reading })
    }

    // 記号は辞書しだいで変換できないことがある。できたなら、文節の読みは渡したものと同じでなければならない。
    @Test fun symbols_give_null_or_matching_readings() {
        for (r in listOf("、。", "（あめ）", "？！…〜ー", "きょうは、あめ。")) {
            val segments = converter.convert(r, null) ?: continue
            assertEquals(r, segments.joinToString("") { it.reading })
        }
    }

    // 中身は辞書しだい。空の文字列と重複がないことを確かめる。
    @Test fun predict_has_no_empty_or_duplicate_entries() {
        val predictions = converter.predict("あり")
        assertTrue(predictions.none { it.isEmpty() })
        assertEquals(predictions.size, predictions.toSet().size)
    }

    /** 呼ばれるたびに [step] ミリ秒進む時計と、送った命令の記録をつけた変換器。 */
    private class Timed(engine: MozcEngine, step: Long) {
        val sent = mutableListOf<Input>()
        var sessionId = 0L
        private var time = -step
        val converter = MozcConverter(engine, now = { time += step; time }) { input ->
            sent += input
            engine.eval(input).also { if (input.type == Input.CommandType.CREATE_SESSION) sessionId = it!!.id }
        }

        fun assertSessionDeleted() {
            assertTrue(sessionId != 0L)
            assertEquals(Input.CommandType.DELETE_SESSION, sent.last().type)
            assertEquals(sessionId, sent.last().id)
        }
    }

    // 時計は「締め切りの計算、IME オン、読み、スペース、最後の確かめ」の順に読まれる。
    // 600 ms ずつ進めると、命令はどれも 2 秒以内に送られ、最後の確かめだけが 2 秒を超える。
    @Test fun convert_finishing_after_the_limit_returns_null_and_deletes_the_session() {
        val timed = Timed(MozcEngine.open(ctx)!!, step = 600)
        assertNull(timed.converter.convert("こうえん", null))
        assertEquals(Input.CommandType.SEND_KEY, timed.sent[timed.sent.size - 2].type)
        timed.assertSessionDeleted()
    }

    // 予測は命令が1つ少ないので、1000 ms ずつ進める。
    @Test fun predict_finishing_after_the_limit_returns_empty_and_deletes_the_session() {
        val timed = Timed(MozcEngine.open(ctx)!!, step = 1000)
        assertTrue(timed.converter.predict("こうえん").isEmpty())
        assertEquals(SessionCommand.CommandType.UPDATE_COMPOSITION, timed.sent[timed.sent.size - 2].command.type)
        timed.assertSessionDeleted()
    }

    @Test fun limit_reached_between_commands_returns_null_and_deletes_the_session() {
        val timed = Timed(MozcEngine.open(ctx)!!, step = 1500)
        assertNull(timed.converter.convert("こうえん", null))
        timed.assertSessionDeleted()
    }

    @Test fun within_the_limit_the_injected_clock_changes_nothing() {
        val timed = Timed(MozcEngine.open(ctx)!!, step = 1)
        assertEquals(converter.convert("あめがふる", null), timed.converter.convert("あめがふる", null))
        timed.assertSessionDeleted()
    }

    @Test fun learned_candidate_becomes_first() {
        val second = converter.convert("こうえん", null)!!.single().candidates[1]
        repeat(3) { converter.learn(listOf(Clause("こうえん", second)), null) }
        assertEquals(second, text("こうえん"))
    }

    @Test fun learn_with_unknown_text_changes_nothing() {
        val before = text("こうえん")
        converter.learn(listOf(Clause("こうえん", "存在しない表記")), null)
        assertEquals(before, text("こうえん"))
    }

    @Test fun learns_every_clause_of_a_sentence() {
        val reading = "あめがふる"
        val chosen = converter.convert(reading, null)!!.map { Clause(it.reading, it.candidates[1]) }
        assertTrue(chosen.size > 1)
        repeat(3) { converter.learn(chosen, null) }
        assertEquals(chosen.joinToString("") { it.text }, text(reading))
    }

    @Test fun learn_with_other_clause_boundaries_changes_nothing() {
        val before = converter.convert("あめがふる", null)
        converter.learn(listOf(Clause("あめ", "飴"), Clause("がふる", "がふる")), null)
        assertEquals(before, converter.convert("あめがふる", null))
    }

    @Test fun init_fails_returns_null() {
        val c = MozcConverter(null)
        assertNull(c.convert("あめ", null))
        assertTrue(c.predict("あめ").isEmpty())
        c.learn(listOf(Clause("あめ", "雨")), null)
    }

    // 動いている Mozc が使っている辞書を壊さないよう、別の場所で確かめる。
    @Test fun dictionary_is_copied_only_when_needed() {
        val dir = File(ctx.cacheDir, "mozc-copy-test").apply { deleteRecursively() }
        val asset = { ctx.assets.open("mozc.data") }
        fun firstByte(file: File) = file.inputStream().use { it.read() }
        try {
            val data = MozcEngine.installDictionary(dir, "1:100", asset)
            val fullSize = data.length()
            assertTrue(fullSize > 0)
            val original = firstByte(data)
            // 写し直したかどうかが分かるよう、1バイト書き換えておく（大きさは変わらない）。
            fun mark() = RandomAccessFile(data, "rw").use { it.write(original xor 0xFF) }

            // 版も大きさも同じなら写し直さない。
            mark()
            MozcEngine.installDictionary(dir, "1:100", asset)
            assertNotEquals(original, firstByte(data))

            // アプリが更新されていたら、大きさが同じでも写し直す。
            MozcEngine.installDictionary(dir, "2:200", asset)
            assertEquals(original, firstByte(data))
            mark()
            MozcEngine.installDictionary(dir, "2:200", asset)
            assertNotEquals(original, firstByte(data))

            // 途中で切れていたら写し直す。
            RandomAccessFile(data, "rw").use { it.setLength(fullSize / 2) }
            MozcEngine.installDictionary(dir, "2:200", asset)
            assertEquals(fullSize, data.length())
            assertEquals(original, firstByte(data))
        } finally {
            dir.deleteRecursively()
        }
    }

    @Test fun failed_dictionary_copy_leaves_no_temporary_file_and_no_version_record() {
        val dir = File(ctx.cacheDir, "mozc-copy-fail-test").apply { deleteRecursively() }
        // 大きさは答えるが、読もうとすると失敗する入力。
        val broken = {
            object : InputStream() {
                override fun available() = 1000
                override fun read(): Int = throw IOException()
            }
        }
        try {
            MozcEngine.installDictionary(dir, "1:100") { ctx.assets.open("mozc.data") }
            assertEquals(setOf("mozc.data", "mozc.data.version"), dir.list()!!.toSet())

            assertTrue(runCatching { MozcEngine.installDictionary(dir, "2:200", broken) }.exceptionOrNull() is IOException)
            // 古い辞書は残ってよいが、書きかけと版の記録は残らない（次に開いたときに写し直す）。
            assertEquals(setOf("mozc.data"), dir.list()!!.toSet())
        } finally {
            dir.deleteRecursively()
        }
    }
}
