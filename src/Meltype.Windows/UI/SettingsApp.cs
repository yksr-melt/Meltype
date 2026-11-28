// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meltype.UI;

/// <summary>
/// 設定アプリ (src/Meltype.Settings を NativeAOT でビルドした MeltypeSettings.exe)。Build-Package.ps1 が Meltype.exe と同じフォルダーに置く
/// (ポータブル版の印 Meltype.portable を同じフォルダーから見て、同じデータフォルダーを使う)。
/// </summary>
internal static class SettingsApp
{
    /// <summary>
    /// トレイのアイコンを押した: 開いている設定アプリがあれば前に出し、無ければ起動する (押すたびに窓を増やさない)。
    /// 設定アプリは多重起動を防がないので、ここで探す。
    /// </summary>
    public static void Show()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var processes = Process.GetProcessesByName("MeltypeSettings");
        try
        {
            if (processes.FirstOrDefault(p => p.SessionId == session && p.MainWindowHandle != IntPtr.Zero)?.MainWindowHandle is not { } window)
            {
                Open();
                return;
            }
            if (IsIconic(window)) ShowWindow(window, SW_RESTORE);
            SetForegroundWindow(window);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private const int SW_RESTORE = 9;
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

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
