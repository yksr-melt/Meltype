// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W
using System.Text.Json.Nodes;
using Meltype.Config;
using Meltype.Input;
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>入力モード切り替えショートカットの取り込み (Recorder)、キー判定 (Detector)、設定移行とバリデーション。</summary>
internal static class ToggleShortcutTests
{
    private static KeyEvent Key(int vk, bool up = false, bool injected = false, int scan = 0) => new(vk, scan, false, up, injected, 1000);

    /// <summary>直接入力でアプリへ渡した判定中の英字は未確定文字とみなさず、文字を変換せず入力モードを切り替えること。</summary>
    [Test]
    public static void Shortcut_DirectDetectionDoesNotCountAsPendingText()
    {
        var keyboard = new CompositionTests.Keyboard(direct: true);
        keyboard.Type("q");
        var output = string.Concat(keyboard.Host.Output);
        var modeChanges = 0;
        keyboard.Controller.ToggleModeRequested += _ =>
        {
            if (!keyboard.Controller.TogglePendingInput()) modeChanges++;
        };
        keyboard.Gate.OnToggleMode(1);
        keyboard.Controller.Pump();
        Assert.Equal(1, modeChanges, "アプリへ渡した文字は変換しない");
        Assert.Equal(output, string.Concat(keyboard.Host.Output));
        Assert.True(!keyboard.Controller.IsComposing, "古い自動判定を残さない");
    }

    /// <summary>未確定文字は確定せず日本語／英字へ切り替え、確定後の要求は入力モード切り替えとして扱うこと。</summary>
    [Test]
    public static void Shortcut_TogglesPendingTextBeforeInputMode()
    {
        var keyboard = new CompositionTests.Keyboard();
        var modeChanges = 0;
        keyboard.Controller.ToggleModeRequested += _ =>
        {
            if (!keyboard.Controller.TogglePendingInput()) modeChanges++;
        };
        keyboard.Type("nihongo");
        var japanese = keyboard.Host.View!.Text;
        keyboard.Gate.OnToggleMode(1);
        keyboard.Controller.Pump();
        Assert.Equal("nihongo", keyboard.Host.View!.Text, "未確定文字を英字にする");
        Assert.Equal(0, keyboard.Host.Output.Count, "確定しない");
        Assert.Equal(0, modeChanges, "入力モードは変えない");
        keyboard.Gate.OnToggleMode(1);
        keyboard.Controller.Pump();
        Assert.Equal(japanese, keyboard.Host.View!.Text, "日本語に戻す");
        keyboard.Press(VirtualKeys.Return);
        keyboard.Gate.OnToggleMode(1);
        keyboard.Controller.Pump();
        Assert.Equal(1, modeChanges, "確定後は入力モードを変える");
    }

    /// <summary>キューに残る打鍵を処理してから未確定状態を判断し、中断時には切り替え要求だけを破棄すること。</summary>
    [Test]
    public static void Shortcut_QueuedTogglePreservesInputOrderAndAbort()
    {
        var keyboard = new CompositionTests.Keyboard();
        var modeChanges = 0;
        keyboard.Controller.ToggleModeRequested += _ =>
        {
            if (!keyboard.Controller.TogglePendingInput()) modeChanges++;
        };
        keyboard.Gate.OnKey(Key(0x4E), _ => true);
        keyboard.Gate.OnKey(Key(0x49), _ => true);
        keyboard.Gate.OnToggleMode(1);
        keyboard.Controller.Pump();
        Assert.Equal("ni", keyboard.Host.View!.Text, "先行入力を英字にする");
        Assert.Equal(0, modeChanges, "処理前に空でもモードを変えない");
        keyboard.Gate.OnToggleMode(1);
        keyboard.Gate.OnKey(Key(0x41), _ => true);
        var rest = keyboard.Gate.Abort();
        Assert.Equal(1, rest.Count, "切り替え要求を破棄してキーだけ返す");
        Assert.Equal(0x41, rest[0].Key!.Value.Vk);
    }

    /// <summary>IME が登録キーを文字切り替えとして送った場合、セッションは確定せず日本語／英字を往復すること。</summary>
    [Test]
    public static void Shortcut_ImeTextToggleKeepsCompositionUncommitted()
    {
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions(), () => new Settings()) { Direct = false };
        SessionResult? last = null;
        foreach (var c in "nihongo") last = session.HandleKey(char.ToUpperInvariant(c), c, false, false, false, false);
        var japanese = last!.View!.Text;
        var english = session.HandleKey(VirtualKeys.Kanji, null, false, false, false, false);
        Assert.Equal("nihongo", english.View!.Text);
        Assert.Equal(0, english.Commits.Count, "確定しない");
        var restored = session.HandleKey(VirtualKeys.Kanji, null, false, false, false, false);
        Assert.Equal(japanese, restored.View!.Text);
        Assert.True(session.IsComposing && !session.Direct, "未確定状態と入力モードを維持");
    }

    /// <summary>US の Alt+バッククォートが VK_KANJI として届いても配列情報で識別でき、JIS と配列情報がない場合は半角/全角として扱うこと。</summary>
    [Test]
    public static void Shortcut_UsAltGraveUsesLayoutInsteadOfImeVirtualKey()
    {
        foreach (var layoutKey in new[] { 0xC0, 0xF3, 0 })
        {
            var down = Key(0x19, scan: 0x29) with { UnmodifiedKey = layoutKey };
            var up = Key(0x19, up: true, scan: 0x29) with { UnmodifiedKey = layoutKey };
            var expected = layoutKey == 0xC0 ? 0xC0 : 0x19;
            var recorder = new ShortcutRecorder();
            recorder.OnKey(Key(0xA4, scan: 0x38));
            recorder.OnKey(down);
            recorder.OnKey(up);
            var recorded = recorder.OnKey(Key(0xA4, up: true, scan: 0x38))!;
            Assert.Equal(expected, recorded.Key, "US はバッククォート、JIS と取得不能時は半角/全角");
            Assert.Equal(ShortcutModifiers.Alt, recorded.Modifiers);
            var detector = new ToggleShortcutDetector();
            ToggleShortcut[] bindings = [new() { Key = 0xC0, Modifiers = ShortcutModifiers.Alt }];
            Assert.Equal(layoutKey == 0xC0, detector.OnKey(down, ShortcutModifiers.Alt, bindings).Toggle, "US の場合だけ既定キーと一致");
            detector.ReleaseConsumed(up);
            Assert.Equal(layoutKey == 0xC0, detector.OnKey(down, ShortcutModifiers.Alt, bindings).Toggle, "次の操作も一致");
        }
    }

    /// <summary>JIS の Alt+半角/全角を Alt+バッククォートと区別し、主キーの解放通知がなくても Alt を離せば次の操作が発動し、長押しでは繰り返し切り替わらないこと。</summary>
    [Test]
    public static void Shortcut_JisAltToggleWorksWithoutMainKeyRelease()
    {
        var detector = new ToggleShortcutDetector();
        ToggleShortcut[] bindings = [new() { Key = 0x19, Modifiers = ShortcutModifiers.Alt }];
        Assert.True(!detector.OnKey(Key(0x19, scan: 0x29), ShortcutModifiers.Alt, ToggleShortcut.Defaults()).Toggle, "Alt+バッククォートとは一致しない");
        detector.ReleaseConsumed(Key(0xA4, up: true, scan: 0x38));
        for (var i = 0; i < 3; i++)
        {
            detector.OnKey(Key(0xA4, scan: 0x38), ShortcutModifiers.Alt, bindings);
            Assert.True(detector.OnKey(Key(0x19, scan: 0x29), ShortcutModifiers.Alt, bindings).Toggle, "毎回切り替え");
            Assert.True(!detector.OnKey(Key(0x19, scan: 0x29), ShortcutModifiers.Alt, bindings).Toggle, "長押しは切り替えない");
            detector.ReleaseConsumed(Key(0xA4, up: true, scan: 0x38));
        }
    }

    /// <summary>JIS の半角/全角の特殊な通知を 1 回で取り込み、Alt との組み合わせも主キーの解放を待たずに完了し、バッククォートとは別のキーとして記録すること。</summary>
    [Test]
    public static void Shortcut_RecorderHandlesJisToggleNotifications()
    {
        var half = new ShortcutRecorder();
        Assert.Equal(new ToggleShortcut { Key = 0x19 }, half.OnKey(Key(0xF3, up: true, scan: 0x29))!);
        Assert.True(half.Complete, "最初の F3 解放だけで取り込む");
        Assert.True(half.OnKey(Key(0xF4, scan: 0x29)) is null, "後続通知で書き換えない");
        foreach (var vk in new[] { 0x19, 0xF3 })
        {
            var alt = new ShortcutRecorder();
            alt.OnKey(Key(0xA4, scan: 0x38));
            alt.OnKey(Key(vk, scan: 0x29));
            Assert.Equal(new ToggleShortcut { Key = 0x19, Modifiers = ShortcutModifiers.Alt }, alt.OnKey(Key(0xA4, up: true, scan: 0x38))!);
            Assert.True(alt.Complete, "主キーの解放通知なしでも Alt 解放で取り込む");
        }
        var grave = new ShortcutRecorder();
        grave.OnKey(Key(0xA4, scan: 0x38));
        grave.OnKey(Key(0xC0, scan: 0x29));
        grave.OnKey(Key(0xC0, up: true, scan: 0x29));
        Assert.Equal(new ToggleShortcut { Key = 0xC0, Modifiers = ShortcutModifiers.Alt }, grave.OnKey(Key(0xA4, up: true, scan: 0x38))!);
    }

    /// <summary>修飾キーと通常キーを離す順番がどちらであっても、すべてのキーが離された時点で正しく取り込みが完了すること。</summary>
    [Test]
    public static void Shortcut_RecorderRecognizesAltNumberAndEitherReleaseOrder()
    {
        foreach (var altFirst in new[] { false, true })
        {
            var recorder = new ShortcutRecorder();
            recorder.OnKey(Key(0xA4, scan: 0x38));
            recorder.OnKey(Key(0x31, scan: 0x02));
            var first = altFirst ? Key(0x12, up: true, scan: 0x38) : Key(0x31, up: true, scan: 0x02);
            var second = altFirst ? Key(0x31, up: true, scan: 0x02) : Key(0x12, up: true, scan: 0x38);
            Assert.True(recorder.OnKey(first) is null, "全キーの解放を待つ");
            var recorded = recorder.OnKey(second)!;
            Assert.Equal(0x31, recorded.Key);
            Assert.Equal(ShortcutModifiers.Alt, recorded.Modifiers);
            Assert.True(recorder.OnKey(Key(0x41)) is null, "完了後の打鍵で結果を変えない");
        }
    }

    /// <summary>押下時と解放時で仮想キーコードが変化しても、消費フラグが残り続けず次の打鍵が正常に動くこと。</summary>
    [Test]
    public static void Shortcut_ChangedVirtualCodeDoesNotLeaveRepeatLatch()
    {
        var detector = new ToggleShortcutDetector();
        ToggleShortcut[] bindings = [new() { Key = 0x19, Modifiers = ShortcutModifiers.Alt }];
        Assert.True(detector.OnKey(Key(0xF3, scan: 0x29), ShortcutModifiers.Alt, bindings).Toggle, "初回");
        Assert.True(detector.ReleaseConsumed(Key(0xC0, up: true, scan: 0x29)), "違うコードの解放も対にする");
        Assert.True(detector.OnKey(Key(0xF4, scan: 0x29), ShortcutModifiers.Alt, bindings).Toggle, "次の打鍵も発動");
    }

    /// <summary>半角/全角の複数のキーコードは同じ割り当てに一致し、バッククォートや余分な修飾キーを含む操作とは一致しないこと。</summary>
    [Test]
    public static void Shortcut_MatchesNormalizedKeysAndExactModifiers()
    {
        var half = new ToggleShortcut { Key = 0x19 };
        foreach (var vk in new[] { 0x19, 0xF3, 0xF4 }) Assert.True(half.Matches(vk, 0), "半角/全角の別コード");
        Assert.True(!half.Matches(0xF3, ShortcutModifiers.Control), "余分な修飾キー");
        var grave = new ToggleShortcut { Key = 0xC0, Modifiers = ShortcutModifiers.Alt };
        Assert.True(grave.Matches(0xC0, ShortcutModifiers.Alt), "仮想キー");
        foreach (var vk in new[] { 0x19, 0xF3, 0xF4 })
            Assert.True(!grave.Matches(vk, ShortcutModifiers.Alt), "JIS 半角/全角は別の割り当て");
        Assert.True(!grave.Matches(0xC0, ShortcutModifiers.Alt | ShortcutModifiers.Control), "AltGr を除く");
    }

    /// <summary>キーリピートや設定変更を挟んでも、キー押下と解放の消費が常に対になってアプリへ漏れないこと。</summary>
    [Test]
    public static void Shortcut_RepeatsAndReleaseRemainPairedAfterSettingsChange()
    {
        var detector = new ToggleShortcutDetector();
        ToggleShortcut[] bindings = [new() { Key = 0x20, Modifiers = ShortcutModifiers.Control }];
        Assert.True(detector.OnKey(Key(0x20), ShortcutModifiers.Control, bindings).Toggle, "初回で切り替え");
        var repeat = detector.OnKey(Key(0x20), 0, []);
        Assert.True(repeat.Consume && !repeat.Toggle, "設定・修飾キーを変えても繰り返しは飲み込む");
        Assert.True(!detector.ReleaseConsumed(Key(0x20, up: true, injected: true)), "外部入力の解放で物理入力の対を消さない");
        Assert.True(detector.ReleaseConsumed(Key(0x20, up: true)), "方式変更後も解放を飲み込む");
        Assert.True(!detector.ReleaseConsumed(Key(0x20, up: true)), "二重の解放は通す");
        Assert.True(!detector.OnKey(Key(0x20, injected: true), ShortcutModifiers.Control, bindings).Consume, "外部入力を通す");
    }

    /// <summary>Shift や Ctrl などの修飾キー単独の登録は拒否され、レコーダーが通常キーを待ち続けること。</summary>
    [Test]
    public static void Shortcut_ModifierKeysAreRejectedAndRecorderKeepsWaiting()
    {
        foreach (var vk in new[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
        {
            var binding = new ToggleShortcut { Key = vk };
            Assert.True(!binding.IsValid && !binding.Matches(vk, 0), "修飾キーは登録・一致不可");
            var detector = new ToggleShortcutDetector();
            foreach (var e in new[] { Key(vk), Key(vk), Key(vk, up: true) })
            {
                var result = detector.OnKey(e, 0, [binding]);
                Assert.True(!result.Toggle && !result.Consume, "押下・長押し・解放を通す");
            }
            var recorder = new ShortcutRecorder();
            recorder.OnKey(Key(vk));
            recorder.OnKey(Key(vk));
            Assert.True(recorder.OnKey(Key(vk, up: true)) is null && !recorder.Complete, "修飾キーだけでは完了しない");
            recorder.OnKey(Key(0xA2));
            recorder.OnKey(Key(0x20));
            recorder.OnKey(Key(0x20, up: true));
            Assert.Equal(new ToggleShortcut { Key = 0x20, Modifiers = ShortcutModifiers.Control }, recorder.OnKey(Key(0xA2, up: true))!, "続けて組み合わせを取り込む");
            var settings = new Settings { ToggleShortcuts = [binding] }.Normalize();
            Assert.Equal(0, settings.EffectiveToggleShortcuts.Count, "除外後も空を維持");
            Assert.Equal(0, settings.Clone().Normalize().EffectiveToggleShortcuts.Count, "複製でも空を維持");
        }
    }

    /// <summary>設定の保存・読み込み、プロファイル切り替え、空リスト設定が正しく保持されること。</summary>
    [Test]
    public static void Shortcut_SettingsPreserveLegacyEmptyAndProfiles()
    {
        var legacy = new Settings().Normalize();
        Assert.True(legacy.EffectiveToggleShortcuts.Any(b => b.Key == 0x19), "未設定なら初期登録を使用");
        var empty = new Settings { ToggleShortcuts = [] }.Normalize();
        Assert.Equal(0, empty.EffectiveToggleShortcuts.Count, "削除した初期登録を復活させない");
        var settings = new Settings { ToggleShortcuts = [new() { Key = 0x20, Modifiers = ShortcutModifiers.Control }] }.Normalize();
        var clone = settings.Clone(); clone.ToggleShortcuts!.Clear();
        Assert.Equal(1, settings.EffectiveToggleShortcuts.Count, "独立した複製");
        var added = settings.AddProfile("別")!;
        added.ToggleShortcuts = [];
        var original = added.SwitchProfile(Settings.DefaultProfileName);
        Assert.Equal(0x20, original.EffectiveToggleShortcuts[0].Key, "元のプロファイル");
        Assert.Equal(0, original.SwitchProfile("別").EffectiveToggleShortcuts.Count, "空のプロファイル");
        var imported = empty.ImportProfile(settings.ExportProfile())!;
        Assert.Equal(0x20, imported.EffectiveToggleShortcuts[0].Key, "インポート");
        settings.Profiles.Add(new SettingsProfile { Name = "旧", Values = new JsonObject { ["HankakuTogglesKeyboard"] = false } });
        Assert.True(settings.SwitchProfile("旧").EffectiveToggleShortcuts.Any(b => b.Key == 0x19), "旧プロファイルから復元");
        var directory = Path.Combine(Path.GetTempPath(), "meltype-shortcuts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "config.json");
            settings.Save(path);
            Assert.Equal(settings.EffectiveToggleShortcuts[0], Settings.Load(path).EffectiveToggleShortcuts[0], "保存・読み込み");
            empty.Save(path);
            Assert.Equal(0, Settings.Load(path).EffectiveToggleShortcuts.Count, "空の保存・読み込み");
            File.WriteAllText(path, "{\"SettingsVersion\":5,\"HankakuTogglesKeyboard\":false}");
            Assert.True(Settings.Load(path).EffectiveToggleShortcuts.Any(b => b.Key == 0x19), "旧ファイル");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>config.json に負の値や重複、不正な修飾キーが含まれていても、Normalize で安全に除外・標準化されること。</summary>
    [Test]
    public static void Shortcut_NormalizesMalformedBindings()
    {
        var settings = new Settings { ToggleShortcuts = [new() { Key = -1 }, new() { Key = 0xF3 }, new() { Key = 0xF4 }, new() { Key = 0x19, Modifiers = ShortcutModifiers.Control }, new() { Key = 0xA4 }, new() { Key = 0xA4, Modifiers = ShortcutModifiers.Shift }] }.Normalize();
        Assert.Equal(1, settings.EffectiveToggleShortcuts.Count, "無効・重複を除く");
        Assert.Equal(0x19, settings.EffectiveToggleShortcuts[0].Key, "標準化");
    }

    /// <summary>既存の変換操作 (F6〜F10, Ctrl+U, 変換キー等) と衝突するキーが、設定およびプロファイル読み込み時に除外されること。</summary>
    [Test]
    public static void Shortcut_CoreRejectsConflictsAcrossSettingsAndProfiles()
    {
        var valid = new ToggleShortcut { Key = 0x20, Modifiers = ShortcutModifiers.Control };
        var settings = new Settings { ConvertKeysSwitchKeyboard = true, ToggleShortcuts =
        [
            new() { Key = 0x75 }, new() { Key = 0x79, Modifiers = ShortcutModifiers.Shift },
            new() { Key = 0x55, Modifiers = ShortcutModifiers.Control },
            new() { Key = 0x1C }, valid, valid,
        ] }.Normalize();
        Assert.Equal(1, settings.EffectiveToggleShortcuts.Count, "Core で衝突と重複を除外");
        Assert.Equal(valid, settings.EffectiveToggleShortcuts[0]);
        var values = settings.ProfileValues();
        values["ToggleShortcuts"] = new JsonArray(new JsonObject { ["Key"] = 0x76 });
        settings.Profiles.Add(new SettingsProfile { Name = "衝突", Values = values });
        Assert.Equal(0, settings.SwitchProfile("衝突").EffectiveToggleShortcuts.Count, "保存済みプロファイルでも除外");
        var file = new JsonObject { ["format"] = "meltype-profile", ["name"] = "衝突", ["values"] = values.DeepClone() };
        Assert.Equal(0, settings.ImportProfile(file.ToJsonString())!.EffectiveToggleShortcuts.Count, "インポートでも除外");
    }

    /// <summary>Windows の操作に使うキー (Alt+Tab、Win+L 等) を理由に登録を拒否せず、設定・プロファイル・インポートで割り当てを維持すること。</summary>
    [Test]
    public static void Shortcut_OsShortcutsAreAllowedAcrossSettingsAndProfiles()
    {
        var bindings = new List<ToggleShortcut>
        {
            new() { Key = 0x09, Modifiers = ShortcutModifiers.Alt },
            new() { Key = 0x09, Modifiers = ShortcutModifiers.Win },
            new() { Key = 0x2E, Modifiers = ShortcutModifiers.Control | ShortcutModifiers.Alt },
            new() { Key = 0x4C, Modifiers = ShortcutModifiers.Win },
        };
        var settings = new Settings { ToggleShortcuts = bindings }.Normalize();
        Assert.Equal(bindings.Count, settings.EffectiveToggleShortcuts.Count, "OS 操作を理由に除外しない");
        foreach (var binding in bindings)
            Assert.Equal(ShortcutValidationError.None, ToggleShortcutValidator.Validate(binding, settings, []));
        settings.Profiles.Add(new SettingsProfile { Name = "OS", Values = settings.ProfileValues() });
        Assert.Equal(bindings.Count, settings.SwitchProfile("OS").EffectiveToggleShortcuts.Count, "保存済みプロファイルでも維持");
        Assert.Equal(bindings.Count, new Settings().ImportProfile(settings.ExportProfile())!.EffectiveToggleShortcuts.Count, "インポートでも維持");
    }

    /// <summary>ショートカットの編集を保存しても、画面を開いている間にトレイで変えた有効状態・動作モード・プロファイルは維持され、編集は元のプロファイルに保存されること。</summary>
    [Test]
    public static void Shortcut_SettingsEditsKeepConcurrentTrayAndProfileChanges()
    {
        var opened = new Settings().Normalize().AddProfile("仕事用")!.SwitchProfile(Settings.DefaultProfileName);
        var edited = opened.Clone();
        var binding = new ToggleShortcut { Key = 0x20, Modifiers = ShortcutModifiers.Control };
        edited.ToggleShortcuts = [binding];
        var current = opened.SwitchProfile("仕事用");
        current.Enabled = false;
        current.Mode = InputMode.Keyboard;
        var merged = Settings.MergeEdits(opened, edited, current);
        Assert.True(!merged.Enabled && merged.Mode == InputMode.Keyboard, "トレイで変えた状態を維持");
        Assert.Equal("仕事用", merged.ActiveProfile, "トレイのプロファイルを維持");
        Assert.Equal(2, merged.EffectiveToggleShortcuts.Count, "仕事用の登録は変えない");
        var standard = merged.SwitchProfile(Settings.DefaultProfileName);
        Assert.Equal(1, standard.EffectiveToggleShortcuts.Count, "画面で編集したプロファイルに保存");
        Assert.Equal(binding, standard.EffectiveToggleShortcuts[0]);
    }

    /// <summary>旧設定の ON/OFF によらず未設定の一覧を 2 つの初期登録へ移行し、通常設定・プロファイル・インポートで同じ規則を使い、明示された空の一覧は維持すること。</summary>
    [Test]
    public static void Shortcut_LegacyMigrationUsesTwoDefaultsAndPreservesEmpty()
    {
        foreach (var enabled in new[] { false, true })
        {
            var settings = new Settings { SettingsVersion = 5 };
            settings.Profiles.Add(new SettingsProfile { Name = "旧", Values = new JsonObject { ["HankakuTogglesKeyboard"] = enabled } });
            Assert.True(settings.Migrate(), "v6 に移行");
            Assert.Equal(2, settings.Normalize().EffectiveToggleShortcuts.Count, "通常設定の初期値");
            Assert.Equal(2, settings.SwitchProfile("旧").EffectiveToggleShortcuts.Count, "旧設定の値によらず 2 つ登録");
            Assert.True(!settings.Profiles.Single(p => p.Name == "旧").Values!.ContainsKey("HankakuTogglesKeyboard"), "旧項目を削除");
            Assert.True(!settings.Migrate(), "移行は一度だけ");
            var file = new JsonObject { ["format"] = "meltype-profile", ["name"] = "旧", ["values"] = new JsonObject { ["HankakuTogglesKeyboard"] = enabled } };
            Assert.Equal(2, settings.ImportProfile(file.ToJsonString())!.EffectiveToggleShortcuts.Count, "旧インポートの初期値");
        }
        var empty = new Settings { SettingsVersion = 5, ToggleShortcuts = [] };
        empty.Profiles.Add(new SettingsProfile { Name = "空", Values = new JsonObject { ["ToggleShortcuts"] = new JsonArray() } });
        empty.Migrate();
        Assert.Equal(0, empty.Normalize().EffectiveToggleShortcuts.Count, "空を維持");
        Assert.Equal(0, empty.SwitchProfile("空").EffectiveToggleShortcuts.Count, "プロファイルの空を維持");
    }
}
