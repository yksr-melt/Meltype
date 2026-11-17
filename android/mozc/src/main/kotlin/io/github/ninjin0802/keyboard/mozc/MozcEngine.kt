// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.mozc

import android.content.Context
import android.os.Build
import android.util.Log
import com.google.android.apps.inputmethod.libs.mozc.session.MozcJni
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.io.InputStream
import org.mozc.android.inputmethod.japanese.protobuf.ProtoCommands

/**
 * プロセスに1つだけある Mozc。辞書を読める場所に写し、Mozc を初期化し、命令を1つずつ渡す。
 *
 * Mozc の本体はプロセス全体で1つなので、[open] は何度呼んでも同じものを返す。
 */
class MozcEngine private constructor() {
    /** 命令を1つ送る。失敗は null。どのスレッドから呼んでもよい（1つずつ順に処理する）。 */
    @Synchronized
    fun eval(input: ProtoCommands.Input): ProtoCommands.Output? {
        return try {
            val command = ProtoCommands.Command.newBuilder().setInput(input).build()
            val output = ProtoCommands.Command.parseFrom(MozcJni.evalCommand(command.toByteArray())).output
            output.takeIf { it.errorCode == ProtoCommands.Output.ErrorCode.SESSION_SUCCESS }
        } catch (e: Exception) {
            Log.w(TAG, "eval: ${e.javaClass.simpleName}")
            null
        }
    }

    companion object {
        private const val TAG = "MozcEngine"
        private const val DATA_FILE = "mozc.data"
        private const val NO_DICTIONARY_VERSION = "0.0.0"
        private var opened: MozcEngine? = null

        /** 初期化に失敗したら null。辞書を写すので、最初の1回は時間がかかる。メインスレッドの外で呼ぶ。 */
        fun open(context: Context): MozcEngine? = synchronized(this) {
            opened?.let { return it }
            try {
                val dir = File(context.noBackupFilesDir, "mozc")
                val data = installDictionary(dir, versionKey(context)) { context.assets.open(DATA_FILE) }
                val profile = File(dir, "profile")
                if (!profile.isDirectory && !profile.mkdirs()) return null
                if (!MozcJni.initialize()) return null
                if (!MozcJni.onPostLoad(profile.path, data.path)) return null
                // 辞書を読めなかったとき、onPostLoad は true のまま、辞書なしの Mozc で動き出す。
                // そのときの辞書の版は "0.0.0" になるので、それで見分ける。
                val version = MozcJni.getDataVersion()
                if (version.isEmpty() || version == NO_DICTIONARY_VERSION) return null
                MozcEngine().also { opened = it }
            } catch (e: Exception) {
                Log.w(TAG, "open: ${e.javaClass.simpleName}")
                null
            } catch (e: LinkageError) {
                // ライブラリを読めないと UnsatisfiedLinkError、その後の呼び出しは NoClassDefFoundError になる。
                Log.w(TAG, "open: ${e.javaClass.simpleName}")
                null
            }
        }

        /** アプリが更新されると変わる値。辞書を写し直すかどうかを決めるのに使う。 */
        private fun versionKey(context: Context): String {
            val info = context.packageManager.getPackageInfo(context.packageName, 0)
            val versionCode = if (Build.VERSION.SDK_INT >= 28) {
                info.longVersionCode
            } else {
                @Suppress("DEPRECATION")
                info.versionCode.toLong()
            }
            return "$versionCode:${info.lastUpdateTime}"
        }

        /**
         * [open] で読める辞書を [dir] に写して、その場所を返す。
         * Mozc はファイルをメモリに対応づけて読むので、アセットのままでは渡せない。
         *
         * 写したときの [versionKey] を隣のファイルに書いておき、それが同じで大きさも同じなら写さない。
         */
        internal fun installDictionary(dir: File, versionKey: String, open: () -> InputStream): File {
            val target = File(dir, DATA_FILE)
            val record = File(dir, "$DATA_FILE.version")
            val size = open().use { it.available().toLong() }
            val recorded = if (record.isFile) record.readText() else null
            if (recorded == versionKey && target.isFile && target.length() == size) return target
            dir.mkdirs()
            // 記録は写し終えてから書く。先に消しておくので、途中で失敗したら次も写し直す。
            record.delete()
            // 途中で止まっても半端なものが残らないよう、別の名前で書いてから置き換える。
            val temporary = File(dir, "$DATA_FILE.tmp")
            try {
                open().use { input ->
                    FileOutputStream(temporary).use { output ->
                        input.copyTo(output)
                        output.fd.sync()
                    }
                }
                if (!temporary.renameTo(target)) throw IOException()
            } finally {
                temporary.delete()
            }
            record.writeText(versionKey)
            return target
        }
    }
}
