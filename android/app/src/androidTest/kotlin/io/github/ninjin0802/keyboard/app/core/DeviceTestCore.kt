// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

import android.text.Selection
import android.view.View
import android.view.inputmethod.BaseInputConnection
import android.view.inputmethod.ExtractedText
import android.view.inputmethod.ExtractedTextRequest
import androidx.test.platform.app.InstrumentationRegistry
import io.github.ninjin0802.keyboard.mozc.MozcClause
import io.github.ninjin0802.keyboard.mozc.MozcBackend
import io.github.ninjin0802.keyboard.mozc.MozcSegment
import java.io.File

/**
 * The native Core of the device tests that run without the keyboard service.
 * The Core fixes its data directory and conversion callbacks for the whole process on first use,
 * so every such test class opens it here, with the same directory and converter.
 */
internal object DeviceTestCore {
    val directory: File
        get() = File(InstrumentationRegistry.getInstrumentation().targetContext.noBackupFilesDir, "bilingual-device-test")

    /**
     * Converts only きょうは (to 今日は), so that live conversion can be seen, and はしをかみにかく in
     * three clauses (each with its own candidates), so that a conversion of several clauses can be
     * seen; everything else stays kana.
     */
    private val converter = object : MozcBackend {
        private val byClause = mapOf(
            "はしを" to listOf("箸を", "橋を", "端を"),
            "かみに" to listOf("紙に", "神に", "髪に"),
            "かく" to listOf("書く", "描く", "欠く"),
        )

        override fun convert(reading: String, context: String?): List<MozcSegment>? = when (reading) {
            "きょうは" -> listOf(MozcSegment("きょうは", listOf("今日は")))
            "はしをかみにかく" -> byClause.map { (clause, candidates) -> MozcSegment(clause, candidates) }
            in byClause -> listOf(MozcSegment(reading, byClause.getValue(reading)))
            else -> null
        }
        override fun predict(reading: String): List<String> = emptyList()
        override fun learn(clauses: List<MozcClause>, context: String?) {}
    }

    fun open(): MeltypeCoreHost = MeltypeCoreHost.open(directory, CoreMozcCallbacks(converter))
}

/** An editor of its own text, which also tells where the caret is (as TextView does; BaseInputConnection alone does not). */
internal class TestEditor : BaseInputConnection(View(InstrumentationRegistry.getInstrumentation().targetContext), true) {
    val content: String get() = editable!!.toString()
    val caret: Int get() = Selection.getSelectionEnd(editable!!)
    val composingStart: Int get() = BaseInputConnection.getComposingSpanStart(editable!!)

    override fun getExtractedText(request: ExtractedTextRequest?, flags: Int): ExtractedText {
        val all = editable!!
        return ExtractedText().also {
            it.text = all.toString()
            it.startOffset = 0
            it.selectionStart = Selection.getSelectionStart(all)
            it.selectionEnd = Selection.getSelectionEnd(all)
        }
    }
}
