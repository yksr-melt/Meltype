// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W
using Meltype.Config;
using Meltype.Input;
using Meltype.UI;

namespace Meltype.Tests;

/// <summary>入力モード切り替えキーの検証と、除外の通知のテスト。</summary>
internal static class ShortcutSettingsTests
{
    /// <summary>Control の割り当てと競合するキーは、競合の種類を示して拒否し、割り当てを外すと許可すること。</summary>
    [Test]
    public static void Shortcut_ValidatorReportsControlKeyBindingConflicts()
    {
        var settings = new Settings { ControlKeyBindings = "T=F7" };
        var shortcut = new ToggleShortcut { Key = 'T', Modifiers = ShortcutModifiers.Control };
        Assert.Equal(ShortcutValidationError.ControlKeyBinding, ToggleShortcutValidator.Validate(shortcut, settings, []), "競合元を示す");
        settings.ControlKeyBindings = "";
        Assert.True(ToggleShortcutValidator.Validate(shortcut, settings, []) == ShortcutValidationError.None, "競合解消後は登録できる");
        settings.ToggleShortcuts = [shortcut];
        settings.ControlKeyBindings = "T=F6";
        Assert.True(settings.EffectiveToggleShortcuts.All(b => ToggleShortcutValidator.Validate(b, settings, []) != ShortcutValidationError.None), "登録後に割り当てを変えても保存前検証で拒否する");
    }

    /// <summary>トレイから競合を含むプロファイルへ切り替えたときは、通常の切替通知を省略して除外警告を優先すること。</summary>
    [Test]
    public static void Shortcut_ProfileSwitchPrioritizesExclusionWarning()
    {
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        try
        {
            var settings = new Settings { ShowNotifications = true }.Normalize();
            Assert.True(TrayApplicationContext.ProfileSwitchMessage(settings) is not null, "通常の切替では Info 通知");
            var values = settings.ProfileValues();
            values[nameof(Settings.ToggleShortcuts)] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["Key"] = 0x70 });
            settings.Profiles.Add(new SettingsProfile { Name = "古い設定", Values = values });
            var next = settings.SwitchProfile("古い設定");
            Assert.True(TrayApplicationContext.ProfileSwitchMessage(next) is null, "除外した切替では Info を要求しない");
            var warning = TrayApplicationContext.TakeShortcutExclusionMessage(next.ShowNotifications);
            Assert.True(warning is not null && warning.Contains("F1"), "Info の判定で除外警告を消費しない");
            Assert.True(TrayApplicationContext.TakeShortcutExclusionMessage(true) is null, "Warning は一度だけ取り出す");
            next.ShowNotifications = false;
            Assert.True(TrayApplicationContext.ProfileSwitchMessage(next) is null, "通知 OFF では Info も省略");
        }
        finally { Meltype.Diagnostics.ShortcutExclusions.Drain(); }
    }

    /// <summary>トレイ生成前の除外をまとめて通知し、通知 OFF で消費した情報は ON に戻しても再通知しないこと。</summary>
    [Test]
    public static void Shortcut_ExclusionNotificationsConsumeStartupAndDisabledBatches()
    {
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        try
        {
            new Settings { ToggleShortcuts = [new() { Key = 0x70 }, new() { Key = 0x71 }] }.Normalize();
            var message = TrayApplicationContext.TakeShortcutExclusionMessage(true);
            Assert.True(message is not null && message.Contains("F1") && message.Contains("F2"), "トレイ生成前の複数の除外をまとめる");
            Assert.True(TrayApplicationContext.TakeShortcutExclusionMessage(true) is null, "消費後は再通知しない");
            new Settings { ToggleShortcuts = [new() { Key = -1 }] }.Normalize();
            Assert.True(TrayApplicationContext.TakeShortcutExclusionMessage(false) is null, "通知 OFF では表示しない");
            Assert.True(TrayApplicationContext.TakeShortcutExclusionMessage(true) is null, "通知を ON にしても過去分を表示しない");
        }
        finally { Meltype.Diagnostics.ShortcutExclusions.Drain(); }
    }

    /// <summary>除外通知にキー名と不正な登録を表示し、表示しきれない件数とログ画面への案内を添えること。</summary>
    [Test]
    public static void Shortcut_ExclusionNotificationShowsKeysAndRemainingCount()
    {
        var item = new Meltype.Diagnostics.ShortcutExclusion(new() { Key = 28 }, ShortcutValidationError.UnmodifiedKey, "仕事");
        var message = TrayApplicationContext.ShortcutExclusionMessage([item, item with { Shortcut = new() { Key = 29 } }, item with { Shortcut = null }, item]);
        Assert.True(message.Contains("変換") && message.Contains("無変換") && message.Contains("null"), "除外キーの名前を表示");
        Assert.True(message.Contains("ほか 1 件") && message.Contains("ログ / 判定理由"), "残り件数とログへの案内");
    }

    /// <summary>重複・修飾キー単独・既存操作との衝突を拒否し、Ctrl+Space や Alt+Tab は登録できること。</summary>
    [Test]
    public static void Shortcut_ValidatorRejectsConflicts()
    {
        var settings = new Settings { AltKeysSwitchKeyboard = true };
        foreach (var binding in new ToggleShortcut[]
        {
            new() { Key = 0x19, Modifiers = ShortcutModifiers.Control },
            new() { Key = 0x55, Modifiers = ShortcutModifiers.Control },
            new() { Key = 0x76 },
            new() { Key = 0x41 }, new() { Key = 0xA4 },
        }) Assert.True(ToggleShortcutValidator.Validate(binding, settings, []) != ShortcutValidationError.None, "既存操作との衝突");
        Assert.True(ToggleShortcutValidator.Validate(new ToggleShortcut { Key = 0x09, Modifiers = ShortcutModifiers.Alt }, settings, []) == ShortcutValidationError.None, "Alt+Tab の登録を拒否しない");
        var valid = new ToggleShortcut { Key = 0x20, Modifiers = ShortcutModifiers.Control };
        Assert.True(ToggleShortcutValidator.Validate(valid, settings, []) == ShortcutValidationError.None, "Ctrl+Space は登録可能");
        Assert.True(ToggleShortcutValidator.Validate(valid, settings, [valid]) != ShortcutValidationError.None, "重複を拒否");
        Assert.True(ToggleShortcutValidator.Validate(new ToggleShortcut { Key = 0xA5 }, new Settings(), []) != ShortcutValidationError.None, "左右 Alt 設定が OFF でも単独押しを拒否");
    }
}
