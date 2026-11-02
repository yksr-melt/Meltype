// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

using System.Text.Json;
using Meltype.Config;

namespace Meltype.Tests;

internal static class SettingsJsonTests
{
    [Test]
    public static void SettingsJson_ShowGuideDefaultsOnAndRoundTrips()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-guide-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            foreach (var (json, expected) in new[] { ("{}", true), ("{\"ShowGuide\":true}", true), ("{\"ShowGuide\":false}", false) })
            {
                File.WriteAllText(path, json);
                var settings = Settings.Load(path);
                using var saved = JsonDocument.Parse(settings.ToJson());
                Assert.Equal(expected, saved.RootElement.GetProperty("ShowGuide").GetBoolean(), "省略時はガイドを表示し、明示した値を読む");
                settings.Clone().Save(path);
                using var loaded = JsonDocument.Parse(Settings.Load(path).ToJson());
                Assert.Equal(expected, loaded.RootElement.GetProperty("ShowGuide").GetBoolean(), "複製・保存・再読込で表示設定を保つ");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void SettingsJson_TsfIndicatorIsOptInAndRoundTrips()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-indicator-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, $"{{\"SettingsVersion\":{Settings.CurrentVersion},\"Mode\":\"Tsf\",\"ModeIndicator\":\"OnSwitchAndFocus\"}}");
            var settings = Settings.Load(path);
            Assert.True(!new Settings().ShowTsfModeIndicator && !settings.ShowTsfModeIndicator, "新規・既存の設定とも IME 表示は既定 OFF");
            Assert.Equal(ModeIndicatorTiming.OnSwitchAndFocus, settings.ModeIndicator, "既存のキーボード方式の設定を維持する");
            settings.ShowTsfModeIndicator = true;
            settings.ModeIndicator = ModeIndicatorTiming.Never;
            settings.Save(path);
            var loaded = Settings.Load(path);
            Assert.True(loaded.ShowTsfModeIndicator && loaded.ModeIndicator == ModeIndicatorTiming.Never, "方式ごとの設定を独立して保存・復元する");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void SettingsJson_PreservesEnumsAndSpacingAcrossSaveLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            var original = new Settings
            {
                SpaceAroundEnglish = true,
                InputStyle = InputStyle.Kana,
                AutoDetection = false,
                Mode = InputMode.Keyboard,
            }.Normalize();
            original.Save(path);
            Assert.True(original.ToJson().Contains("\"Keyboard\"") && original.ToJson().Contains("\"Kana\""), "enum の文字列表現を維持する");
            var loaded = Settings.Load(path);
            Assert.True(loaded.SpaceAroundEnglish, "空白設定を読み込む");
            Assert.Equal(InputStyle.Kana, loaded.InputStyle);
            Assert.True(!loaded.AutoDetection, "自動判定 OFF を読み込む");
            Assert.Equal(InputMode.Keyboard, loaded.Mode);
            Assert.True(!File.Exists(path + ".broken"), "有効な設定を壊れたファイルとして扱わない");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void SettingsJson_StillAcceptsCommentsTrailingCommasAndNumericEnums()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, "{ /* synthetic */ \"SettingsVersion\": 5, \"SpaceAroundEnglish\": true, \"InputStyle\": 1, }");
            var loaded = Settings.Load(path);
            Assert.True(loaded.SpaceAroundEnglish, "コメント・末尾カンマを受け入れる");
            Assert.Equal(InputStyle.Kana, loaded.InputStyle, "旧形式の数値 enum を受け入れる");
            Assert.True(!File.Exists(path + ".broken"), "有効な旧形式を壊れたファイルとして扱わない");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void SettingsJson_KeepsMigratedSettingsWhenSaveFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 4, \"Mode\": \"Keyboard\", \"SpaceAroundEnglish\": true }");
            // 移行のあとの保存を失敗させる。Windows は読み取り専用のファイルに置き換えられない。
            // Mac・Linux は読み取り専用でも置き換えられるので、.tmp の場所にフォルダーを置いて書けなくする
            if (OperatingSystem.IsWindows()) File.SetAttributes(path, FileAttributes.ReadOnly);
            else Directory.CreateDirectory(path + ".tmp");
            var loaded = Settings.Load(path);
            Assert.Equal(InputMode.Keyboard, loaded.Mode, "保存に失敗しても読み込んだ値を使う");
            Assert.True(loaded.SpaceAroundEnglish, "保存に失敗しても既定値に戻さない");
            Assert.Equal(Settings.CurrentVersion, loaded.SettingsVersion, "移行は済ませる");
            Assert.True(loaded.AppRules.Any(r => r.Process == "maya.exe"), "移行で足したアプリの設定を残す");
            Assert.True(!File.Exists(path + ".broken"), "保存の失敗を壊れたファイルとして扱わない");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void SettingsJson_ReadsRemovedChoicesAsCurrentOnes()
    {
        // 消した動作モード「IME 自動切替」は Meltype IME、消した入力方式「両方を判定」はローマ字入力として読む。
        // ほかの設定は既定値に戻さない (知らない名前で読めないと、設定がすべて既定値に戻る)
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"Mode\": \"AutoSwitch\", \"TsfIntroduced\": true, \"InputStyle\": \"Both\", \"SpaceAroundEnglish\": true, \"IdleFlushMs\": 900, " +
                                    "\"ActiveProfile\": \"仕事\", \"Profiles\": [ { \"Name\": \"仕事\" }, { \"Name\": \"趣味\", \"Values\": { \"InputStyle\": \"Both\", \"LiveConversion\": false } } ] }");
            var state = AppState.Load(Path.Combine(directory, "state.json"), path);
            Assert.True(!state.TsfIntroduced, "初めて起動したときと同じく、Meltype IME を使えなければ Meltype キーボードに戻す (Program.ShouldFallBackToKeyboard)");
            var loaded = Settings.Load(path);
            Assert.Equal(InputMode.Tsf, loaded.Mode);
            Assert.Equal(InputStyle.Romaji, loaded.InputStyle);
            Assert.True(loaded.SpaceAroundEnglish, "ほかの設定はそのまま");
            Assert.True(!File.Exists(path + ".broken"), "壊れたファイルとして扱わない");
            Assert.True(File.ReadAllText(path).Contains("\"Mode\": \"Tsf\""), "読んだときに移した内容を保存する");
            Assert.True(Settings.Load(path).Mode == InputMode.Tsf && !AppState.Load(Path.Combine(directory, "state.json"), path).TsfIntroduced, "次に読んだときも同じ");
            var hobby = loaded.SwitchProfile("趣味");
            Assert.Equal(InputStyle.Romaji, hobby.InputStyle, "プロファイルに残っている「両方を判定」も読める");
            Assert.True(!hobby.LiveConversion, "プロファイルのほかの値はそのまま");
            Assert.True(loaded.ToJson().Contains("\"Mode\": \"Tsf\"") && loaded.ToJson().Contains("\"InputStyle\": \"Romaji\""), "保存するときは今の名前で書く");

            var imported = new Settings().ImportProfile("{\"format\":\"meltype-profile\",\"name\":\"前の版\",\"values\":{\"InputStyle\":\"Both\",\"SpaceAroundEnglish\":true}}");
            Assert.True(imported is { InputStyle: InputStyle.Romaji, SpaceAroundEnglish: true }, "前の版で書き出したプロファイルも読み込める");

            // ほかのモードの人と、プロファイルの中に前の版の "Mode" が残っているだけの人は、TsfIntroduced を変えない
            File.WriteAllText(path, "{ \"Mode\": \"Keyboard\", \"TsfIntroduced\": true, \"Profiles\": [ { \"Name\": \"標準\", \"Values\": { \"Mode\": \"AutoSwitch\" } } ] }");
            File.Delete(Path.Combine(directory, "state.json"));
            Assert.True(AppState.Load(Path.Combine(directory, "state.json"), path).TsfIntroduced, "TsfIntroduced はそのまま");
            Assert.Equal(InputMode.Keyboard, Settings.Load(path).Mode, "動作モードは Meltype キーボードのまま");

            // 数値で書いたもの: 1 は消した IME 自動切替、入力方式の 2 は消した「両方を判定」
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"Mode\": 1, \"InputStyle\": 2 }");
            Assert.True(Settings.Load(path) is { Mode: InputMode.Tsf, InputStyle: InputStyle.Romaji }, "初期値にする");
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"Mode\": 2 }");
            Assert.Equal(InputMode.Tsf, Settings.Load(path).Mode, "Meltype IME の番号は前の版のまま");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void ModeIndicatorColors_AreParsed()
    {
        // #359: 入力モードの表示の色は #RRGGBB (# は無くても・#RGB でも)。空・読めない値は既定の色
        Assert.True(Settings.ParseColor("#D32F2F") == (0xD3, 0x2F, 0x2F), "#RRGGBB");
        Assert.True(Settings.ParseColor("007acc") == (0x00, 0x7A, 0xCC), "# なし・小文字");
        Assert.True(Settings.ParseColor("#fff") == (255, 255, 255), "#RGB");
        Assert.True(Settings.ParseColor("") is null && Settings.ParseColor("red") is null && Settings.ParseColor("#12345") is null, "読めない値");
        Assert.Equal("", new Settings().ModeIndicatorDirectColor, "既定は空 (今までの色)");
    }

    [Test]
    public static void SettingsJson_V7ValuesMoveToV8Choices()
    {
        // v8 で 1 つの選択肢にまとめた項目・アプリ別設定に移したアプリの一覧は、一番上とプロファイルの両方で移す。消した項目は保存で消える
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-v8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, """
                {
                  "SettingsVersion": 7,
                  "SlashAsMiddleDot": true, "FullWidthSlash": true,
                  "ShiftSpaceHalfWidth": true, "FullWidthSpaceByDefault": false,
                  "CompatibleShortcuts": true, "MacControlKeys": false,
                  "ShowModeIndicator": true, "ShowModeIndicatorOnFocus": false,
                  "PasteApps": "Resolve.exe, foo.exe", "NoPasteApps": "LINE.exe", "TextInputApps": "Adobe Premiere Pro.exe;a.exe b.exe",
                  "AppRules": [ { "Process": "line.exe", "Enabled": false, "Profile": "General" } ],
                  "JapaneseThreshold": 12, "TypoEnabled": false, "MaxPendingKeys": 3, "ImeTimeoutMs": 500, "LogKeyIntervals": true,
                  "DetectionLevel": "Conservative",
                  "AppKinds": [ { "Name": "チャット", "DetectionLevel": "Manual" }, { "Name": "仕事", "DetectionLevel": "Aggressive" }, { "Name": "ゲーム" } ],
                  "ActiveProfile": "標準",
                  "Profiles": [ { "Name": "標準" }, { "Name": "趣味", "Values": { "FullWidthSlash": true, "MacControlKeys": true, "CompatibleShortcuts": true, "ShowModeIndicator": false, "FullWidthSpaceByDefault": true, "ShiftSpaceHalfWidth": true, "DetectionLevel": 3 } } ]
                }
                """);
            var loaded = Settings.Load(path);
            Assert.Equal(SlashStyle.MiddleDot, loaded.Slash, "中黒が ON なら中黒 (前も中黒が優先)");
            Assert.Equal(SpaceWidth.HalfWidth, loaded.SpaceWidth, "Shift + Space で半角 → どちらも半角");
            Assert.Equal(ControlKeyLayout.Atok, loaded.ControlKeys, "Ctrl + K / L → ATOK 風");
            Assert.Equal(ModeIndicatorTiming.OnSwitch, loaded.ModeIndicator, "入力欄に入ったときは出さない → 切り替えたとき");
            Assert.Equal(CommitMethod.Paste, loaded.CommitMethodFor("foo.exe"), "貼り付けで入力するアプリ → 貼り付け");
            Assert.Equal(CommitMethod.Paste, loaded.CommitMethodFor("Resolve.exe"));
            Assert.Equal(CommitMethod.Keys, loaded.CommitMethodFor("LINE.exe"), "貼り付けを使わないアプリ → 1 文字ずつ");
            Assert.True(!loaded.IsAppEnabled("LINE.exe"), "前からあったアプリ別設定 (大文字小文字の違う名前) に足し、OFF のまま");
            Assert.True(loaded.TreatsAsTextInput("Adobe Premiere Pro.exe") && loaded.TreatsAsTextInput("a.exe") && loaded.TreatsAsTextInput("b.exe"), "入力欄とみなすアプリ (空白区切りの .exe の並びも分ける)");
            Assert.True(!loaded.TreatsAsTextInput("foo.exe"), "書いていないアプリは入力欄とみなさない");
            Assert.True(loaded.AutoDetection, "慎重 → 自動判定 ON (#1232)");
            Assert.Equal("False,True,", string.Join(",", loaded.AppKinds.Select(k => k.AutoDetection)), "独自の種類の 手動 → OFF・積極的 → ON・指定なし → 全体と同じ");

            var hobby = loaded.SwitchProfile("趣味");
            Assert.Equal(SlashStyle.FullWidth, hobby.Slash, "プロファイルの中も移す");
            Assert.Equal(ControlKeyLayout.Mac, hobby.ControlKeys, "macOS 風と ATOK 風の両方なら macOS 風");
            Assert.Equal(ModeIndicatorTiming.Never, hobby.ModeIndicator);
            Assert.Equal(SpaceWidth.FullWidth, hobby.SpaceWidth, "スペースを全角で入力が優先 (前も Shift + Space で半角だった)");
            Assert.True(!hobby.AutoDetection, "数値で書いた 手動 (3) → 自動判定 OFF");

            var saved = File.ReadAllText(path);
            foreach (var key in new[] { "SlashAsMiddleDot", "FullWidthSlash", "ShiftSpaceHalfWidth", "FullWidthSpaceByDefault", "CompatibleShortcuts", "MacControlKeys",
                         "ShowModeIndicator\"", "ShowModeIndicatorOnFocus", "PasteApps", "NoPasteApps", "TextInputApps", "JapaneseThreshold", "TypoEnabled", "MaxPendingKeys", "ImeTimeoutMs", "LogKeyIntervals", "DetectionLevel" })
                Assert.True(!saved.Contains(key), $"移した・消した項目は保存しない: {key}");
            Assert.True(saved.Contains($"\"SettingsVersion\": {Settings.CurrentVersion}"), "版を上げて保存する");
            Assert.Equal(loaded.ToJson(), Settings.Load(path).ToJson(), "保存したものを読み直しても同じ");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void SettingsJson_V8DoesNotReadOldNames()
    {
        // v8 の config.json に前の項目名を手で書いても、移さない (移すのは版を上げる前の 1 回だけ)
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-v8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, $"{{ \"SettingsVersion\": {Settings.CurrentVersion}, \"SlashAsMiddleDot\": true, \"Slash\": \"FullWidth\" }}");
            Assert.Equal(SlashStyle.FullWidth, Settings.Load(path).Slash);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void AppState_MovesOutOfConfigJson()
    {
        // v8 より前は Meltype の状態も config.json に入っていた。state.json が無ければ config.json から読んで state.json に書き、config.json からは消す
        var directory = Path.Combine(Path.GetTempPath(), "meltype-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            var statePath = Path.Combine(directory, "state.json");
            File.WriteAllText(config, "{ \"SettingsVersion\": 7, \"WelcomeShown\": true, \"TsfIntroduced\": true, \"TsfOffered\": true, \"ReleaseNotesShown\": \"1.4.0\", \"SettingsShowHelp\": false, \"SettingsWheelChangesValue\": false }");
            var state = AppState.Load(statePath, config);
            Assert.True(state is { WelcomeShown: true, TsfIntroduced: true, TsfOffered: true, ReleaseNotesShown: "1.4.0", SettingsShowHelp: false, SettingsWheelChangesValue: false }, "config.json の値を引き継ぐ");
            Assert.True(File.Exists(statePath), "すぐに state.json に書く");

            Settings.Load(config);
            Assert.True(!File.ReadAllText(config).Contains("TsfIntroduced"), "config.json の移行で消える");
            Assert.True(AppState.Load(statePath, config) is { TsfIntroduced: true, ReleaseNotesShown: "1.4.0" }, "次からは state.json を読む");

            state.ReleaseNotesShown = "1.5.0";
            state.Save();
            Assert.Equal("1.5.0", AppState.Load(statePath, config).ReleaseNotesShown, "変えた値を保存する");

            File.Delete(statePath);
            File.Delete(config);
            Assert.True(AppState.Load(statePath, config) is { WelcomeShown: false, TsfIntroduced: false, SettingsShowHelp: true }, "初めて起動したときは既定値");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
