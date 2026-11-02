// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Konayukiw

using Meltype.Config;

namespace Meltype.Tests;

internal static class PastePolicyTests
{
    [Test]
    public static void IsQtWindowClass_SeesQtWindows()
    {
        Assert.True(PastePolicy.IsQtWindowClass("Qt663QWindowIcon"), "LINE のウィンドウ");
        Assert.True(PastePolicy.IsQtWindowClass("Qt683QWindowIcon"), "OBS のウィンドウ");
        Assert.True(PastePolicy.IsQtWindowClass("Qt6QWindowToolSaveBits"), "バージョンが 1 桁の形");
        Assert.True(PastePolicy.IsQtWindowClass("Qt5152QWindowIcon"), "Qt 5.15.2 の形");
    }

    [Test]
    public static void IsQtWindowClass_RejectsOthers()
    {
        Assert.True(!PastePolicy.IsQtWindowClass("Notepad"), "メモ帳");
        Assert.True(!PastePolicy.IsQtWindowClass("Chrome_WidgetWin_1"), "Chromium / Electron");
        Assert.True(!PastePolicy.IsQtWindowClass("Notepad++"), "Notepad++");
        Assert.True(!PastePolicy.IsQtWindowClass("Qt"), "短すぎる");
        Assert.True(!PastePolicy.IsQtWindowClass("QtQWindow"), "バージョン数字が無い");
        Assert.True(!PastePolicy.IsQtWindowClass("QT663QWindowIcon"), "大文字の T は Qt ではない");
        Assert.True(!PastePolicy.IsQtWindowClass(""), "空文字");
        Assert.True(!PastePolicy.IsQtWindowClass(null), "null");
    }

    [Test]
    public static void ShouldPaste_QtAppUsesPasteByDefault()
    {
        var settings = new Settings();
        Assert.True(PastePolicy.ShouldPaste(settings, "LINE.exe", isQtApp: true), "Qt アプリは貼り付け");
        Assert.True(PastePolicy.ShouldPaste(settings, "obs64.exe", isQtApp: true), "OBS も貼り付け");
        Assert.True(!PastePolicy.ShouldPaste(settings, "notepad.exe", isQtApp: false), "非 Qt は今までどおり 1 文字ずつ");
        Assert.True(PastePolicy.ShouldPaste(settings, "Resolve.exe", isQtApp: false), "既定のアプリ別設定で Resolve は貼り付け");
        Assert.True(PastePolicy.ShouldPaste(settings, "resolve.EXE", isQtApp: false), "大文字小文字は問わない");
    }

    [Test]
    public static void ShouldPaste_KeysWinsOverQt()
    {
        var settings = new Settings();
        settings.AppRules.Add(new AppRule { Process = "Custom-Qt.exe", Commit = CommitMethod.Keys });
        Assert.True(!PastePolicy.ShouldPaste(settings, "custom-qt.exe", isQtApp: true), "「1 文字ずつ送る」は Qt でも 1 文字ずつ (大文字小文字は無視する)");
        Assert.True(PastePolicy.ShouldPaste(settings, "LINE.exe", isQtApp: true), "アプリ別設定に無い Qt アプリは貼り付けのまま");
    }
}
