// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Diagnostics;

namespace Meltype.UI;

/// <summary>
/// 設定アプリ (src/Meltype.Settings を NativeAOT でビルドした MeltypeSettings.exe)。Build-Package.ps1 が Meltype.exe と同じフォルダーに置く
/// (ポータブル版の印 Meltype.portable を同じフォルダーから見て、同じデータフォルダーを使う)。
/// </summary>
internal static class SettingsApp
{
    public static void Open(params string[] arguments)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MeltypeSettings.exe");
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // Meltype.Windows だけをビルドして動かしているとき (設定アプリは別のプロジェクト)
            MessageBox.Show($"設定アプリを起動できませんでした: {ex.Message}\n\n{path}", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
