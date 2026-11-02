// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Meltype.Diagnostics;

/// <summary>
/// 不具合報告に自動で入れる実行環境とログ。入力した文字は入れない (ログは設定どおり。既定では文字数だけ)。
/// </summary>
internal static class ReportInfo
{
    /// <summary>報告に入れるログの行数と長さの上限。</summary>
    private const int MaxLogLines = 80, MaxLogChars = 8000;

    [DllImport("user32.dll")] private static extern int GetKeyboardType(int typeFlag);

    /// <summary>OS の名前 (フォームの OS の選択肢と同じ形)。</summary>
    public static string OsName => System.Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10";

    /// <summary>実行環境 (1 行ずつ「項目: 値」)。</summary>
    public static string Environment(Config.Settings settings)
    {
        var lines = new List<string>
        {
            $"Meltype: {AppInfo.Version}",
            $"OS: {OsName} {WindowsVersion()} ({RuntimeInformation.OSArchitecture})",
            $".NET: {System.Environment.Version}",
            $"管理者として実行: {(IsAdministrator() ? "はい" : "いいえ")}",
            $"モード: {settings.Mode}",
            $"変換エンジン: {settings.ConversionEngine} (Mozc: {(File.Exists(Path.Combine(AppContext.BaseDirectory, "mozc", "meltype_mozc_helper.exe")) ? "あり" : "なし")})",
            $"Zenzai: {(settings.UseZenzai ? "ON" : "OFF")}",
            $"自動判定の強さ: {settings.DetectionLevel}",
            $"入力方式: {settings.InputStyle}",
            $"ライブ変換: {(settings.LiveConversion ? "ON" : "OFF")}",
            $"変換ボックス: {settings.CompositionPlacement} / {settings.CompositionSize} / {settings.CompositionTheme} / {settings.CompositionOpacity}",
            $"キーボード: {KeyboardKind()}",
            $"画面: {Screen.AllScreens.Length} 枚、拡大率 {Scaling()}%",
            $"言語: {CultureInfo.CurrentUICulture.Name}",
        };
        return string.Join("\n", lines);
    }

    /// <summary>最近のログ (新しいものほど後ろ)。</summary>
    public static string RecentLog()
    {
        var entries = Log.Snapshot();
        var text = string.Join("\n", entries.Skip(Math.Max(0, entries.Length - MaxLogLines)).Select(e => e.ToString()));
        return text.Length > MaxLogChars ? text[^MaxLogChars..] : text;
    }

    private static string WindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var display = key?.GetValue("DisplayVersion") as string;
            var edition = key?.GetValue("EditionID") as string;
            var ubr = key?.GetValue("UBR") is int u ? $".{u}" : "";
            var version = System.Environment.OSVersion.Version;
            return $"{edition} {display} (ビルド {version.Build}{ubr})".Trim();
        }
        catch
        {
            return System.Environment.OSVersion.VersionString;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>JIS (日本語 106/109) か US (101/104) か。</summary>
    private static string KeyboardKind()
    {
        var type = GetKeyboardType(0);
        var subtype = GetKeyboardType(1);
        return type == 7 ? $"日本語 (JIS、種類 {subtype})" : $"日本語以外 (US など、種類 {type}/{subtype})";
    }

    private static int Scaling()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return (int)Math.Round(g.DpiX / 96f * 100);
    }
}
