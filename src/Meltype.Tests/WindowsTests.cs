// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>Windows 版だけのテスト。</summary>
internal static class WindowsTests
{
    [Test]
    public static void Games_AreDetectedFromInstallFolder()
    {
        Assert.True(ForegroundTracker.LooksLikeGame(@"D:\SteamLibrary\steamapps\common\Apex Legends\r5apex.exe"), "Steam のゲーム");
        Assert.True(ForegroundTracker.LooksLikeGame(@"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe"), "Epic のゲーム");
        Assert.True(ForegroundTracker.LooksLikeGame(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Rainbow Six Siege\RainbowSix.exe"), "Ubisoft のゲーム");
        Assert.True(ForegroundTracker.LooksLikeGame(@"C:\Program Files (x86)\SEGA\PHANTASYSTARONLINE2_JP\pso2_bin\pso2.exe"), "PSO2 NGS (専用のランチャー)");
        Assert.True(!ForegroundTracker.LooksLikeGame(@"C:\Program Files (x86)\Steam\steam.exe"), "Steam のクライアントは除く");
        Assert.True(!ForegroundTracker.LooksLikeGame(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"), "ランチャーは除く");
        Assert.True(!ForegroundTracker.LooksLikeGame(@"C:\Program Files\Microsoft VS Code\Code.exe"), "ふつうのアプリ");

        var settings = new Settings();
        Assert.True(settings.IsGame("r5apex.exe", looksLikeGame: true), "ゲームのフォルダーなら止める");
        settings.StopInGames = false;
        Assert.True(!settings.IsGame("r5apex.exe", looksLikeGame: true), "設定で OFF にしたら止めない");
        settings.AppRules.Add(new AppRule { Process = "minecraft.exe", Enabled = true, Profile = AppProfile.Game });
        Assert.True(settings.IsGame("minecraft.exe", looksLikeGame: false), "アプリ別設定で「ゲーム」にしたら止める");
    }

    [Test]
    public static void StartupTask_IsLogonTriggerWithoutDelayAndAdmin()
    {
        // #283: サインインしたらすぐ起動するタスク。& などを含むユーザー名・パスでも XML として読める
        var xml = Startup.TaskXml(@"PC\R&D <user>", @"C:\Users\R&D\AppData\Local\Programs\Meltype\Meltype.exe");
        var document = System.Xml.Linq.XDocument.Parse(xml);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal(@"PC\R&D <user>", document.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
        Assert.Equal("LeastPrivilege", document.Descendants(ns + "RunLevel").Single().Value, "管理者権限なし");
        Assert.True(!document.Descendants(ns + "Delay").Any(), "遅らせない");
        Assert.Equal(@"C:\Users\R&D\AppData\Local\Programs\Meltype\Meltype.exe", document.Descendants(ns + "Command").Single().Value);
        Assert.Equal(@"C:\Users\R&D\AppData\Local\Programs\Meltype", document.Descendants(ns + "WorkingDirectory").Single().Value);
    }
}
