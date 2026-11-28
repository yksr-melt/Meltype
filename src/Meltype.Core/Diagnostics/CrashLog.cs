// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;

namespace Meltype.Diagnostics;

/// <summary>
/// 落ちたとき (起動できなかったとき) の例外を crash.log に書き足す。ファイルへのログ (設定) が OFF でも書く:
/// 起動直後に落ちる報告で、原因の手がかりが何も残っていなかった (Linux の設定アプリは #2415)。打った文字は含まない (例外の種類と場所だけ)。
/// </summary>
public static class CrashLog
{
    /// <summary>program は見出しに入れるプログラムの名前と版 (「Meltype 1.8.0」)。</summary>
    public static void Write(string program, object exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(AppPaths.CrashLogFile,
                $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {program} / {Environment.OSVersion} ====\n{exception}\n\n");
        }
        catch
        {
            // 書けなくても、元の例外の処理を続ける
        }
    }
}
