// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.emoji

import android.content.Context
import android.content.res.AssetManager
import android.graphics.Paint
import android.os.Handler
import android.os.Looper
import android.os.Process
import android.util.Log
import io.github.ninjin0802.keyboard.app.PerfLog

/**
 * この端末で出せる絵文字の一覧。アセットを読み、端末が1つの絵として描けないものを外す。
 * 時間がかかるので別のスレッドで作り、アプリが動いているあいだ覚えておく。[load] は画面のスレッドから呼ぶ。
 */
object EmojiCatalog {
    private const val ASSET = "emoji.txt"
    private const val TAG = "EmojiCatalog"

    private val main = Handler(Looper.getMainLooper())
    private var catalog: Map<EmojiCategory, Array<String>>? = null
    private var loading = false
    private val waiting = ArrayList<(Map<EmojiCategory, Array<String>>) -> Unit>()

    /** 一覧を用意する。[onReady] は画面のスレッドで呼ぶ（用意ができていれば、すぐ）。 */
    fun load(context: Context, onReady: ((Map<EmojiCategory, Array<String>>) -> Unit)? = null) {
        catalog?.let { onReady?.invoke(it); return }
        if (onReady != null) waiting += onReady
        if (loading) return
        loading = true
        val assets = context.applicationContext.assets
        Thread({
            // キーボードを開く処理や変換のじゃまをしない。
            Process.setThreadPriority(Process.THREAD_PRIORITY_BACKGROUND)
            val start = System.nanoTime()
            // 出すのは決まった文だけ。失敗の中身（読んだ文字を含みうる）は出さない。
            val result = EmojiData.readOrEmpty({ read(assets) }) { Log.w(TAG, "emoji-catalog: failed") }
            PerfLog.event("emoji-catalog", (System.nanoTime() - start) / 1000, count = result.values.sumOf { it.size })
            main.post {
                catalog = result
                loading = false
                val callbacks = ArrayList(waiting)
                waiting.clear()
                callbacks.forEach { it(result) }
            }
        }, "emoji-catalog").start()
    }

    private fun read(assets: AssetManager): Map<EmojiCategory, Array<String>> {
        val all = assets.open(ASSET).bufferedReader(Charsets.UTF_8).useLines { EmojiData.parse(it) }
        val paint = Paint()
        return all.mapValues { (_, list) -> list.filter(paint::hasGlyph).toTypedArray() }
    }
}
