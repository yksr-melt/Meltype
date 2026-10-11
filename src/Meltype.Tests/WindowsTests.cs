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
    public static void MsIme_LongReading_IsSplitBeforeTheEngineLimit()
    {
        // #221: Microsoft IME の変換エンジンは 100 文字を超える読みを受け付けない (E_LARGEINPUT)。区切って渡す
        var sentence = "きょうはいいてんきですね、あしたもはれるといいですね。";
        var text = string.Concat(Enumerable.Repeat(sentence, 6)); // 162 文字
        var parts = Composition.MsImeKanjiConverter.SplitForEngine(text, 90);
        Assert.Equal(text, string.Concat(parts));
        Assert.True(parts.All(p => p.Length <= 90), string.Join(" / ", parts.Select(p => p.Length)));
        Assert.True(parts[0].EndsWith('。'), "文の切れ目で区切る: " + parts[0]);
        // 区切りの文字が無ければ上限で切る
        Assert.Equal("100,50", string.Join(",", Composition.MsImeKanjiConverter.SplitForEngine(new string('ー', 150), 100).Select(p => p.Length)));

        using var ime = new Composition.MsImeKanjiConverter();
        if (ime.Convert("てすと") is null) return; // Microsoft IME が無い環境
        var clauses = ime.ConvertClauses(text, "わたしは");
        Assert.True(clauses is { Count: > 0 }, "長い読みも文節に区切って変換できる");
        Assert.Equal(text, string.Concat(clauses!.Select(c => c.Reading)));
        Assert.True(string.Concat(clauses.Select(c => c.Text)).Contains("天気"), string.Concat(clauses.Select(c => c.Text)));
        Assert.True(ime.Convert(text)?.Contains("天気") == true, "全体の変換も");
    }
}
