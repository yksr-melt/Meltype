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
    /// <summary>Control の割り当てと競合するキーは登録・保存時の共通検証で設定名を示して拒否し、割り当てを外すと許可すること。</summary>
    [Test]
    public static void Shortcut_EditorExplainsControlKeyBindingConflicts()
    {
        var settings = new Settings { ControlKeyBindings = "T=F7" };
        var shortcut = new ToggleShortcut { Key = 'T', Modifiers = ShortcutModifiers.Control };
        var error = ShortcutEditor.ValidationError(shortcut, settings, []);
        Assert.True(error is not null && error.Contains("Control キーの割り当て"), "競合元を画面で説明する");
        settings.ControlKeyBindings = "";
        Assert.True(ShortcutEditor.ValidationError(shortcut, settings, []) is null, "競合解消後は登録できる");
        settings.ToggleShortcuts = [shortcut];
        settings.ControlKeyBindings = "T=F6";
        Assert.True(settings.EffectiveToggleShortcuts.All(b => ShortcutEditor.ValidationError(b, settings, []) is not null), "登録後に割り当てを変えても保存前検証で拒否する");
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
            values[nameof(Settings.ConvertKeysSwitchKeyboard)] = true;
            values[nameof(Settings.ToggleShortcuts)] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["Key"] = 28 });
            settings.Profiles.Add(new SettingsProfile { Name = "古い設定", Values = values });
            var next = settings.SwitchProfile("古い設定");
            Assert.True(TrayApplicationContext.ProfileSwitchMessage(next) is null, "除外した切替では Info を要求しない");
            var warning = TrayApplicationContext.TakeShortcutExclusionMessage(next.ShowNotifications);
            Assert.True(warning is not null && warning.Contains("変換"), "Info の判定で除外警告を消費しない");
            Assert.True(TrayApplicationContext.TakeShortcutExclusionMessage(true) is null, "Warning は一度だけ取り出す");
            next.ShowNotifications = false;
            Assert.True(TrayApplicationContext.ProfileSwitchMessage(next) is null, "通知 OFF では Info も省略");
        }
        finally { Meltype.Diagnostics.ShortcutExclusions.Drain(); }
    }

    /// <summary>判定テストに入力した状態で競合設定を ON にしても除外を報告せず、画面の登録と既存の保存前検証を維持すること。</summary>
    [Test]
    public static void Shortcut_SettingsPreviewDoesNotReportExclusions()
    {
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        try
        {
            using var engine = new MeltypeEngine(new Settings { ConvertKeysSwitchKeyboard = false, ToggleShortcuts = [new() { Key = 28 }, new() { Key = 29 }] }, null, null);
            using var form = new SettingsForm(engine);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var input = (TextBox)typeof(SettingsForm).GetField("_testInput", flags)!.GetValue(form)!;
            var collect = typeof(SettingsForm).GetMethod("Collect", flags)!;
            var controls = new List<Control>();
            var pending = new Queue<Control>();
            pending.Enqueue(form);
            while (pending.TryDequeue(out var parent))
                foreach (Control child in parent.Controls) { controls.Add(child); pending.Enqueue(child); }
            var toggle = controls.OfType<ToggleSwitch>().Single(t => t.AccessibleName == "無変換で英数 / 変換で日本語");
            var editor = controls.OfType<ShortcutEditor>().Single();
            input.Text = "nihongo";
            toggle.Checked = true;
            input.Text = "nihongo english";
            var edited = (Settings)collect.Invoke(form, null)!;
            Assert.Equal(0, edited.EffectiveToggleShortcuts.Count, "判定用の複製は既存どおり正規化する");
            Assert.Equal(2, editor.CollectBindings().Count, "画面の一覧を保持");
            var raw = (Settings)typeof(SettingsForm).GetMethod("CollectValues", flags)!.Invoke(form, null)!;
            Assert.True(raw.EffectiveToggleShortcuts.All(b => ShortcutEditor.ValidationError(b, raw, []) is not null), "保存前の生の設定には競合が残る");
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "編集中は除外情報を追加しない");
            Assert.True(!engine.Settings.ConvertKeysSwitchKeyboard, "未保存の変更をエンジンに反映しない");
            toggle.Checked = false;
            edited = (Settings)collect.Invoke(form, null)!;
            Assert.True(edited.EffectiveToggleShortcuts.All(b => ShortcutEditor.ValidationError(b, edited, []) is null), "競合を直すと保存前検証を通る");
            Assert.Equal(2, edited.EffectiveToggleShortcuts.Count, "修正後も登録を維持");
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
            new Settings { ConvertKeysSwitchKeyboard = true, ToggleShortcuts = [new() { Key = 28 }, new() { Key = 29 }] }.Normalize();
            var message = TrayApplicationContext.TakeShortcutExclusionMessage(true);
            Assert.True(message is not null && message.Contains("変換") && message.Contains("無変換"), "トレイ生成前の複数の除外をまとめる");
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
        var item = new Meltype.Diagnostics.ShortcutExclusion(new() { Key = 28 }, ShortcutValidationError.ConvertKeys, "仕事");
        var message = TrayApplicationContext.ShortcutExclusionMessage([item, item with { Shortcut = new() { Key = 29 } }, item with { Shortcut = null }, item]);
        Assert.True(message.Contains("変換") && message.Contains("無変換") && message.Contains("null"), "除外キーの名前を表示");
        Assert.True(message.Contains("ほか 1 件") && message.Contains("ログ / 判定理由"), "残り件数とログへの案内");
    }

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
        Assert.True(!first.RootElement.GetProperty("fullWidthSpace").GetBoolean(), "全角スペースの既定値も送る");
        var bindings = first.RootElement.GetProperty("toggleShortcuts");
        Assert.Equal(1, bindings.GetArrayLength(), "単独押しを除外して IME に送る");
        Assert.Equal(0x20, bindings[0].GetProperty("key").GetInt32());
        Assert.Equal(2, bindings[0].GetProperty("mods").GetInt32());
        settings.FullWidthSpaceByDefault = true;
        using var same = Hello();
        Assert.True(same.RootElement.GetProperty("fullWidthSpace").GetBoolean(), "キー一覧と全角スペースの設定を同時に反映");
        Assert.True(!same.RootElement.GetProperty("modeIndicator").GetBoolean(), "IME の表示は既定 OFF なので DLL は問い合わせない");
        settings.ShowTsfModeIndicator = true;
        using var indicator = Hello();
        Assert.True(indicator.RootElement.GetProperty("modeIndicator").GetBoolean(), "IME の表示を ON にしたら DLL に伝える");
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
