// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W
using System.Text.Json;
using Meltype.Config;
using Meltype.Input;
using Meltype.Tip;
using Meltype.UI;

namespace Meltype.Tests;

/// <summary>設定画面 UI でのショートカットバリデーションと、TipServer (IPC) プロトコルでの設定同期テスト。</summary>
internal static class ShortcutSettingsTests
{
    /// <summary>設定画面で重複・修飾キー単独・既存操作との衝突を拒否し、Ctrl+Space や Alt+Tab は登録できること。</summary>
    [Test]
    public static void Shortcut_EditorRejectsConflicts()
    {
        var settings = new Settings { ConvertKeysSwitchKeyboard = true, AltKeysSwitchKeyboard = true };
        foreach (var binding in new ToggleShortcut[]
        {
            new() { Key = 0x19, Modifiers = ShortcutModifiers.Control },
            new() { Key = 0x55, Modifiers = ShortcutModifiers.Control },
            new() { Key = 0x76 },
            new() { Key = 0x1C }, new() { Key = 0xA4 },
        }) Assert.True(ShortcutEditor.ValidationError(binding, settings, []) is not null, "既存操作との衝突");
        Assert.True(ShortcutEditor.ValidationError(new ToggleShortcut { Key = 0x09, Modifiers = ShortcutModifiers.Alt }, settings, []) is null, "Alt+Tab の登録を拒否しない");
        var valid = new ToggleShortcut { Key = 0x20, Modifiers = ShortcutModifiers.Control };
        Assert.True(ShortcutEditor.ValidationError(valid, settings, []) is null, "Ctrl+Space は登録可能");
        Assert.True(ShortcutEditor.ValidationError(valid, settings, [valid]) is not null, "重複を拒否");
        Assert.True(ShortcutEditor.ValidationError(new ToggleShortcut { Key = 0xA5 }, new Settings(), []) is not null, "左右 Alt 設定が OFF でも単独押しを拒否");
    }

    /// <summary>TipServer の hello 応答に有効な割り当て・設定変更・空の一覧を反映し、一時停止中も一覧を返すこと。</summary>
    [Test]
    public static void Shortcut_ProtocolSnapshotsFollowChangesAndEmptyList()
    {
        var settings = new Settings { Mode = InputMode.Tsf, ToggleShortcuts = [new() { Key = 0x20, Modifiers = ShortcutModifiers.Control }, new() { Key = 0xA4 }] }.Normalize();
        using var invoker = new Control();
        using var service = TipHarness.CreateService(invoker);
        using var server = new TipServer(invoker, service, () => settings);
        var sessions = new Dictionary<string, Meltype.Composition.MeltypeSession>();
        JsonDocument Hello() => JsonDocument.Parse(server.Handle("{\"op\":\"hello\",\"process\":\"harness\"}", sessions));
        using var first = Hello();
        Assert.True(first.RootElement.GetProperty("active").GetBoolean(), "IME モードでは有効");
        var bindings = first.RootElement.GetProperty("toggleShortcuts");
        Assert.Equal(1, bindings.GetArrayLength(), "単独押しを除外して IME に送る");
        Assert.Equal(0x20, bindings[0].GetProperty("key").GetInt32());
        Assert.Equal(2, bindings[0].GetProperty("mods").GetInt32());
        using var same = Hello();
        Assert.Equal(bindings.GetRawText(), same.RootElement.GetProperty("toggleShortcuts").GetRawText(), "同じ設定の配列を送る");
        settings = new Settings { Mode = InputMode.Tsf, ToggleShortcuts = [] }.Normalize();
        using var cleared = Hello();
        Assert.Equal(0, cleared.RootElement.GetProperty("toggleShortcuts").GetArrayLength(), "空も明示する");
        settings.Enabled = false;
        using var disabled = Hello();
        Assert.True(!disabled.RootElement.GetProperty("active").GetBoolean(), "一時停止");
        Assert.Equal(0, disabled.RootElement.GetProperty("toggleShortcuts").GetArrayLength(), "一時停止中も設定を送る");
    }
}
