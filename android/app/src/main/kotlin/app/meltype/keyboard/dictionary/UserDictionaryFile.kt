// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package app.meltype.keyboard.dictionary

import android.content.Context
import app.meltype.keyboard.core.coreDataDirectory
import java.io.File
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.Files
import java.nio.file.StandardCopyOption

/** ユーザー辞書の 1 語。[pos] は Core の UserWord と同じく、品詞を書いていない行では名詞。 */
data class UserWord(val reading: String, val word: String, val pos: String = NOUN) {
    companion object {
        const val NOUN = "名詞"
    }
}

/**
 * Core のユーザー辞書 (AppPaths.UserDictionaryFile = データフォルダの userdict.txt) を、設定画面から読み書きする。
 * 読み方と書き方は src/Meltype.Core/Dictionary/UserDictionary.cs (Parse・Save) に合わせる。別の保存先は作らない。
 *
 * 書いた語は、キーボードを開き直さなくても変換に効く。Core が入力欄に入るときと、変換していないキーの処理で 1 秒ごとに
 * userdict.txt の更新時刻を見て読み直し (UserDictionary.Refresh)、変わった語を Mozc のユーザー辞書に渡すため。
 *
 * 読めない・書けないときの IOException は、呼び出し側 (設定画面) で利用者に知らせる。
 */
class UserDictionaryFile(private val path: File) {
    constructor(context: Context) : this(File(coreDataDirectory(context.applicationContext), "userdict.txt"))

    fun list(): List<UserWord> = if (path.isFile) parse(path.readText(Charsets.UTF_8)) else emptyList()

    /** 登録する。同じ読み・同じ単語が既にあれば何もしない (Core の Add と同じ)。読み・単語が空なら false。 */
    fun add(reading: String, word: String): Boolean {
        val added = UserWord(reading.trim(), word.trim())
        if (!valid(added)) return false
        val words = list()
        if (words.any { it.reading == added.reading && it.word == added.word }) return true
        write(words + added)
        return true
    }

    /** [previous] の読みと単語を変える。品詞は前のまま。ほかの語と同じ読み・単語になるなら false。 */
    fun edit(previous: UserWord, reading: String, word: String): Boolean {
        val changed = previous.copy(reading = reading.trim(), word = word.trim())
        if (!valid(changed)) return false
        val words = list()
        val index = words.indexOf(previous)
        if (index < 0) return false
        if (words.withIndex().any { (i, w) -> i != index && w.reading == changed.reading && w.word == changed.word }) return false
        write(words.toMutableList().also { it[index] = changed })
        return true
    }

    /** 消す。同じ語が複数あれば、Core の Remove と同じく最初の 1 つだけ。 */
    fun delete(word: UserWord) {
        val words = list().toMutableList()
        if (words.remove(word)) write(words)
    }

    // Core の Config.DataFile.Write と同じく、別のファイルに書いてから置き換える (書いている途中の中身を Core が読まないように)。
    private fun write(words: List<UserWord>) {
        val directory = path.parentFile!!
        if (!directory.isDirectory && !directory.mkdirs()) throw java.io.IOException("Cannot create " + directory)
        val temp = File.createTempFile("userdict.", ".tmp", directory)
        try {
            temp.writeText(format(words), Charsets.UTF_8)
            try {
                Files.move(temp.toPath(), path.toPath(), StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE)
            } catch (_: AtomicMoveNotSupportedException) {
                Files.move(temp.toPath(), path.toPath(), StandardCopyOption.REPLACE_EXISTING)
            }
        } finally {
            temp.delete()
        }
    }

    companion object {
        /** UserDictionary.Parse と同じ。読めない行は飛ばす。 */
        fun parse(text: String): List<UserWord> = text.removePrefix("﻿").lines().mapNotNull { line ->
            if (line.startsWith("#")) return@mapNotNull null
            val parts = line.trimEnd('\r').split('\t')
            if (parts.size < 2) return@mapNotNull null
            // 制御文字 (ESC など) の入った語は、確定するとアプリにキー操作として届くので Core は読まない
            if (parts[0].any(Char::isISOControl) || parts[1].any(Char::isISOControl)) return@mapNotNull null
            val pos = parts.getOrNull(2)?.trim()?.takeIf { it.isNotEmpty() } ?: UserWord.NOUN
            UserWord(parts[0].trim(), parts[1].trim(), pos).takeIf(::valid)
        }

        /**
         * UserDictionary.Save と同じ形。名詞は品詞を書かない 2 列、それ以外は品詞まで 3 列。
         * Core は BOM 付きの UTF-8 で書くので合わせる (Core は BOM の有無どちらも読む)。
         */
        fun format(words: List<UserWord>): String =
            "﻿# Meltype ユーザー辞書: 1 行に「読み<Tab>単語」か「読み<Tab>単語<Tab>品詞」(品詞が無ければ名詞)\n" +
                words.joinToString("") {
                    if (it.pos == UserWord.NOUN) "${it.reading}\t${it.word}\n" else "${it.reading}\t${it.word}\t${it.pos}\n"
                }

        private fun valid(word: UserWord): Boolean =
            word.reading.isNotEmpty() && word.word.isNotEmpty() &&
                word.reading.none(Char::isISOControl) && word.word.none(Char::isISOControl)
    }
}
