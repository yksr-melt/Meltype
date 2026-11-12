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
    /// <summary>v6/v7 移行時の解析失敗ログと競合除外のログ・通知は各1回で、保存後の再読み込みでは繰り返さないこと。</summary>
    [Test]
    public static void Shortcut_LoadReportsLegacyMigrationProblemsExactlyOnce()
    {
        // v5 はプロファイルの初期化も通り、v6 は旧追加キーだけを移行する。
        foreach (var version in new[] { 5, 6 })
        {
            var path = Path.Combine(Path.GetTempPath(), "Meltype-migration-reports-" + Guid.NewGuid() + ".json");
            var profile = "移行通知-" + Guid.NewGuid();
            var notifications = 0;
            void Added() => notifications++;
            Meltype.Diagnostics.ShortcutExclusions.Drain();
            Meltype.Diagnostics.ShortcutExclusions.Added += Added;
            try
            {
                var values = new JsonObject
                {
                    ["ToggleShortcuts"] = new JsonArray(),
                    ["ToggleKeyboardShortcuts"] = "UnknownKey; Ctrl+U; Ctrl+Space",
                };
                File.WriteAllText(path, new JsonObject
                {
                    ["SettingsVersion"] = version,
                    ["ActiveProfile"] = profile,
                    ["ToggleShortcuts"] = new JsonArray(),
                    ["ToggleKeyboardShortcuts"] = "UnknownKey; Ctrl+U; Ctrl+Space",
                    ["Profiles"] = new JsonArray(new JsonObject { ["Name"] = profile, ["Values"] = values }),
                }.ToJsonString());

                var settings = Settings.Load(path);
                Assert.Equal(new ToggleShortcut { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.Control },
                    settings.EffectiveToggleShortcuts.Single(), "有効な登録は保存前に残す");
                var exclusion = Meltype.Diagnostics.ShortcutExclusions.Drain().Single();
                Assert.Equal(ShortcutValidationError.Conversion, exclusion.Reason);
                Assert.Equal(new ToggleShortcut { Key = 'U', Modifiers = ShortcutModifiers.Control }, exclusion.Shortcut);
                Assert.Equal(profile, exclusion.Profile);
                Assert.Equal(1, notifications, "除外イベントは1回だけ発火する");

                // 同じ旧値がトップレベルとプロファイルにあるため、解析失敗は出所ごとに1件ずつ記録する。
                var warnings = Meltype.Diagnostics.Log.Snapshot()
                    .Where(e => e.Level == Meltype.Diagnostics.LogLevel.Warn && e.Message.Contains(profile)).ToArray();
                Assert.Equal(2, warnings.Count(e => e.Message.Contains("UnknownKey") && e.Message.Contains("書式")),
                    "旧値の各出所を1回だけ報告する");
                Assert.Equal(1, warnings.Count(e => e.Message.Contains("Conversion")), "除外ログは1回だけ記録する");
                Assert.Equal(3, warnings.Length, "予期しない警告がない");

                var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                Assert.Equal(Settings.CurrentVersion, saved["SettingsVersion"]!.GetValue<int>());
                Assert.True(!File.ReadAllText(path).Contains("ToggleKeyboardShortcuts"), "各出所の旧設定を保存しない");
                Assert.Equal(settings.EffectiveToggleShortcuts.Single(), Settings.Load(path).EffectiveToggleShortcuts.Single());
                Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "保存後は除外を再通知しない");
                Assert.Equal(1, notifications, "再読み込みでイベントを発火しない");
                Assert.Equal(warnings.Length, Meltype.Diagnostics.Log.Snapshot()
                    .Count(e => e.Level == Meltype.Diagnostics.LogLevel.Warn && e.Message.Contains(profile)),
                    "再読み込みで警告ログを追加しない");
            }
            finally
            {
                Meltype.Diagnostics.ShortcutExclusions.Added -= Added;
                Meltype.Diagnostics.ShortcutExclusions.Drain();
                File.Delete(path);
            }
        }
    }

    /// <summary>Control の割り当てを入力処理と同じ書式で解釈し、有効な英字の Ctrl および Ctrl+Shift だけを競合とすること。</summary>
    [Test]
    public static void Shortcut_ControlKeyBindingsUseSharedParsingRules()
    {
        var parsed = ControlKeyBindingParser.Parse(" t = f6; T=F7, y=left\nz=Tab; T=unknown; AA=F8; 1=F9; Q=unknown; broken");
        Assert.Equal(3, parsed.Count);
        Assert.Equal(VirtualKeys.F7, parsed['T'], "最後の有効な割り当てを優先");
        Assert.Equal(VirtualKeys.Left, parsed['Y']);
        Assert.Equal(VirtualKeys.Tab, parsed['Z']);
        var settings = new Settings { ControlKeyBindings = "t=F7; Q=unknown" };
        foreach (var mods in new[] { ShortcutModifiers.Control, ShortcutModifiers.Control | ShortcutModifiers.Shift })
            Assert.Equal(ShortcutValidationError.ControlKeyBinding, ToggleShortcutValidator.Validate(new() { Key = 'T', Modifiers = mods }, settings, []));
        foreach (var mods in new[] { ShortcutModifiers.Alt, ShortcutModifiers.Control | ShortcutModifiers.Alt, ShortcutModifiers.Control | ShortcutModifiers.Win })
            Assert.Equal(ShortcutValidationError.None, ToggleShortcutValidator.Validate(new() { Key = 'T', Modifiers = mods }, settings, []));
        Assert.Equal(ShortcutValidationError.None, ToggleShortcutValidator.Validate(new() { Key = 'Q', Modifiers = ShortcutModifiers.Control }, settings, []), "不正な割り当ては競合にしない");
        settings.ControlKeyBindings = "";
        Assert.Equal(ShortcutValidationError.None, ToggleShortcutValidator.Validate(new() { Key = 'T', Modifiers = ShortcutModifiers.Control }, settings, []), "割り当てを消すと登録できる");
    }

    /// <summary>設定読み込み・プロファイル切り替え・インポートで Control の競合を除外して理由を報告し、編集中は報告しないこと。</summary>
    [Test]
    public static void Shortcut_ControlKeyBindingsConflictsAcrossSettingsAndProfiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meltype-control-bindings-" + Guid.NewGuid() + ".json");
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        try
        {
            File.WriteAllText(path, """
                {"SettingsVersion":6,"ControlKeyBindings":"T=F7","ToggleShortcuts":[],"ToggleKeyboardShortcuts":"Ctrl+T; Ctrl+Space"}
                """);
            var settings = Settings.Load(path);
            Assert.Equal(1, settings.EffectiveToggleShortcuts.Count);
            Assert.Equal(ShortcutValidationError.ControlKeyBinding, Meltype.Diagnostics.ShortcutExclusions.Drain().Single().Reason);
            Assert.True(Meltype.Diagnostics.Log.Snapshot().Any(e => e.Message.Contains("Control キーの割り当て") && e.Message.Contains("ControlKeyBinding")), "ログに競合元を示す");
            Settings.Load(path);
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "移行保存後は再通知しない");
            var values = JsonNode.Parse("""{"ControlKeyBindings":"T=F7","ToggleShortcuts":[{"Key":84,"Modifiers":2},{"Key":32,"Modifiers":2}]}""")!.AsObject();
            settings.Profiles.Add(new SettingsProfile { Name = "競合", Values = values });
            Assert.Equal(1, settings.SwitchProfile("競合").EffectiveToggleShortcuts.Count);
            Assert.Equal("競合", Meltype.Diagnostics.ShortcutExclusions.Drain().Single().Profile);
            var imported = settings.ImportProfile(new JsonObject { ["format"] = "meltype-profile", ["name"] = "取り込み", ["values"] = values.DeepClone() }.ToJsonString());
            Assert.True(imported is not null, "取り込み成功");
            Assert.Equal(1, imported!.EffectiveToggleShortcuts.Count);
            Assert.Equal(ShortcutValidationError.ControlKeyBinding, Meltype.Diagnostics.ShortcutExclusions.Drain().Single().Reason);
            var edited = new Settings { ControlKeyBindings = "T=F7", ToggleShortcuts = [new() { Key = 'T', Modifiers = ShortcutModifiers.Control }] };
            Assert.Equal(0, edited.Normalize(reportShortcutExclusions: false).EffectiveToggleShortcuts.Count);
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "編集中は通知しない");
        }
        finally
        {
            File.Delete(path);
            Meltype.Diagnostics.ShortcutExclusions.Drain();
        }
    }

    /// <summary>旧設定から移行した競合キーは保存前に除外し、再読み込みで同じ除外を通知しないこと。</summary>
    [Test]
    public static void Shortcut_MigrationSavesNormalizedKeysBeforeReload()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meltype-migration-conflicts-" + Guid.NewGuid() + ".json");
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        try
        {
            File.WriteAllText(path, """
                {"SettingsVersion":6,"ToggleShortcuts":[],
                 "ToggleKeyboardShortcuts":"Ctrl+Space; Ctrl+U; Space; Henkan"}
                """);
            var settings = Settings.Load(path);
            Assert.Equal(1, settings.EffectiveToggleShortcuts.Count, "有効な Ctrl+Space だけ残す");
            var exclusions = Meltype.Diagnostics.ShortcutExclusions.Drain();
            Assert.Equal(3, exclusions.Length, "初回だけ競合を通知する");
            Assert.True(exclusions.Any(e => e.Reason == ShortcutValidationError.Conversion)
                && exclusions.Any(e => e.Reason == ShortcutValidationError.UnmodifiedKey)
                && exclusions.Any(e => e.Reason == ShortcutValidationError.ConvertKeys), "各除外理由を保持する");
            var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(Settings.CurrentVersion, saved["SettingsVersion"]!.GetValue<int>());
            Assert.Equal(1, saved["ToggleShortcuts"]!.AsArray().Count, "ファイルにも除外済みの一覧を保存する");
            Assert.True(!saved.ContainsKey("ToggleKeyboardShortcuts"), "旧項目を保存しない");
            Assert.Equal(1, Settings.Load(path).EffectiveToggleShortcuts.Count);
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "再読み込みで再通知しない");
        }
        finally
        {
            File.Delete(path);
            Meltype.Diagnostics.ShortcutExclusions.Drain();
        }
    }

    /// <summary>切り替え先プロファイルの保存用一覧も正規化結果に同期し、保存後の再読み込みと切り替えで再通知しないこと。</summary>
    [Test]
    public static void Shortcut_ProfileSwitchSavesNormalizedProfileValues()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meltype-profile-conflicts-" + Guid.NewGuid() + ".json");
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        try
        {
            var settings = new Settings { ActiveProfile = "元", ToggleShortcuts = [] }.Normalize();
            settings.Profiles.Add(new SettingsProfile
            {
                Name = "移行先",
                Values = JsonNode.Parse("""{"ToggleShortcuts":[],"ToggleKeyboardShortcuts":"Ctrl+Space; Ctrl+U"}""")!.AsObject(),
            });
            var next = settings.SwitchProfile("移行先");
            Assert.Equal(1, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "切り替え時に除外を通知する");
            var values = next.Profiles.Single(p => p.Name == "移行先").Values!;
            Assert.True(JsonNode.DeepEquals(next.ProfileValues(), values), "保存用のプロファイル全体を現在値と同期する");
            Assert.Equal(1, values["ToggleShortcuts"]!.AsArray().Count, "保存用一覧にも競合キーを残さない");
            next.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal(1, loaded.EffectiveToggleShortcuts.Count);
            var switched = loaded.SwitchProfile("元").SwitchProfile("移行先");
            Assert.Equal(1, switched.EffectiveToggleShortcuts.Count);
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "保存後の読み込みと切り替えで再通知しない");
        }
        finally
        {
            File.Delete(path);
            Meltype.Diagnostics.ShortcutExclusions.Drain();
        }
    }

    /// <summary>旧追加キーを既存一覧へ重複なく移行し、旧項目を保存せず、再読み込みでも登録を増やさないこと。</summary>
    [Test]
    public static void Shortcut_LegacyAdditionalKeysMigrateOnLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meltype-legacy-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """
                {"SettingsVersion":6,"ToggleShortcuts":[{"Key":32,"Modifiers":2}],
                 "ToggleKeyboardShortcuts":"Ctrl+Space; Alt+Backquote; Ctrl+Shift+Win+K"}
                """);
            var settings = Settings.Load(path);
            Assert.Equal(3, settings.EffectiveToggleShortcuts.Count);
            Assert.True(settings.EffectiveToggleShortcuts.Any(s => s.Key == 'K' && s.Modifiers == (ShortcutModifiers.Control | ShortcutModifiers.Shift | ShortcutModifiers.Win)), "全修飾キーを移行");
            Assert.True(!File.ReadAllText(path).Contains("ToggleKeyboardShortcuts"), "移行時の保存先も新一覧のみ");
            Assert.Equal(3, Settings.Load(path).EffectiveToggleShortcuts.Count, "再読み込みは冪等");
            File.WriteAllText(path, """{"SettingsVersion":7,"ToggleShortcuts":[],"ToggleKeyboardShortcuts":"Ctrl+Space"}""");
            settings = Settings.Load(path);
            Assert.Equal(1, settings.EffectiveToggleShortcuts.Count, "現行バージョンでも旧項目を読み込む");
            Assert.True(!settings.ToJson().Contains("ToggleKeyboardShortcuts"), "旧項目は出力しない");
            Assert.Equal(0, new Settings { ToggleShortcuts = [], LegacyToggleKeyboardShortcuts = "" }.Normalize().EffectiveToggleShortcuts.Count, "明示的な空一覧を維持");
            Assert.Equal(3, new Settings { LegacyToggleKeyboardShortcuts = "Ctrl+Space" }.Normalize().EffectiveToggleShortcuts.Count, "未設定では既定キーに追加");
        }
        finally { File.Delete(path); }
    }

    /// <summary>保存済みプロファイルとインポートでも旧追加キーを移行し、不正な書式・競合の理由を記録すること。</summary>
    [Test]
    public static void Shortcut_LegacyAdditionalKeysMigrateProfilesAndReportInvalid()
    {
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        try
        {
            var settings = new Settings { ToggleShortcuts = [] }.Normalize();
            settings.Profiles.Add(new SettingsProfile { Name = "旧設定", Values = JsonNode.Parse("""{"ToggleShortcuts":[],"ToggleKeyboardShortcuts":"Ctrl+Space; Ctrl+U; UnknownKey"}""")!.AsObject() });
            settings = settings.SwitchProfile("旧設定");
            Assert.Equal(1, settings.EffectiveToggleShortcuts.Count, "有効なキーだけを移行");
            Assert.True(!settings.ToJson().Contains("ToggleKeyboardShortcuts"), "保存済みプロファイルからも旧項目を削除");
            Assert.True(Meltype.Diagnostics.Log.Snapshot().Any(e => e.Message.Contains("UnknownKey") && e.Message.Contains("書式") && e.Message.Contains("旧設定")), "解析できないキーの理由と出所を記録");
            Assert.True(Meltype.Diagnostics.ShortcutExclusions.Drain().Any(e => e.Reason == ShortcutValidationError.Conversion), "解析できた競合は通常の除外通知に統合");
            var imported = settings.ImportProfile("""{"format":"meltype-profile","name":"取り込み","values":{"ToggleShortcuts":[],"ToggleKeyboardShortcuts":"Ctrl+Space; Ctrl+Space"}}""");
            Assert.True(imported is not null, "旧形式を取り込める");
            Assert.Equal(1, imported!.EffectiveToggleShortcuts.Count, "取り込み時も重複を統合");
            Assert.True(!imported.ToJson().Contains("ToggleKeyboardShortcuts"), "エクスポートにも旧項目を残さない");
        }
        finally { Meltype.Diagnostics.ShortcutExclusions.Drain(); }
    }

    /// <summary>編集中も既存どおりキーや数値を正規化するが除外は報告せず、通常の読み込みでは報告すること。</summary>
    [Test]
    public static void Shortcut_EditNormalizationSuppressesOnlyExclusionReports()
    {
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        try
        {
            var settings = new Settings { ToggleShortcuts = [new() { Key = 28 }, new() { Key = 29 }, new() { Key = 32, Modifiers = ShortcutModifiers.Control }] };
            var edited = settings.Clone().Normalize(reportShortcutExclusions: false);
            Assert.Equal(1, edited.EffectiveToggleShortcuts.Count, "キーの正規化は既存どおり");
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "編集時は通知しない");
            settings.Normalize();
            Assert.Equal(1, settings.EffectiveToggleShortcuts.Count, "通常の読み込みでは除外する");
            Assert.Equal(2, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "通常の除外は報告する");
        }
        finally { Meltype.Diagnostics.ShortcutExclusions.Drain(); }
    }

    /// <summary>設定の読み込み・プロファイル切り替え・インポートで除外したキーと理由を保持し、正規化済みの設定では再通知しないこと。</summary>
    [Test]
    public static void Shortcut_ExclusionsSurviveLoadAndProfileImport()
    {
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        var directory = Path.Combine(Path.GetTempPath(), "Meltype-exclusions-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, "{\"SettingsVersion\":6,\"ActiveProfile\":\"仕事\",\"ToggleShortcuts\":[{\"Key\":28},{\"Key\":29},{\"Key\":32,\"Modifiers\":2}]}");
            var settings = Settings.Load(path);
            var exclusions = Meltype.Diagnostics.ShortcutExclusions.Drain();
            Assert.Equal(2, exclusions.Length);
            Assert.True(exclusions.All(e => e.Reason == ShortcutValidationError.ConvertKeys && e.Profile == "仕事"), "理由とプロファイルを保持");
            Assert.True(Meltype.Diagnostics.Log.Snapshot().Any(e => e.Level == Meltype.Diagnostics.LogLevel.Warn && e.Message.Contains("Key=28") && e.Message.Contains("ConvertKeys")), "キーと理由を警告ログへ記録");
            settings.Clone().Normalize();
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "再正規化は通知しない");
            var values = settings.ProfileValues();
            values["ToggleShortcuts"] = new JsonArray(new JsonObject { ["Key"] = 28 });
            settings.Profiles.Add(new SettingsProfile { Name = "古い設定", Values = values });
            settings.SwitchProfile("古い設定");
            Assert.Equal("古い設定", Meltype.Diagnostics.ShortcutExclusions.Drain().Single().Profile);
            var file = new JsonObject { ["format"] = "meltype-profile", ["name"] = "取り込み", ["values"] = values.DeepClone() };
            Assert.True(settings.ImportProfile(file.ToJsonString()) is not null, "取り込み成功");
            Assert.Equal("取り込み", Meltype.Diagnostics.ShortcutExclusions.Drain().Single().Profile, "取り込み途中の除外も保持");
        }
        finally
        {
            Meltype.Diagnostics.ShortcutExclusions.Drain();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>不正・重複・既存操作との衝突を理由ごとに記録し、通知情報は一度だけ取り出せ、有効な登録や空の一覧では警告しないこと。</summary>
    [Test]
    public static void Shortcut_ExclusionsReportReasonsAndConsumeOnce()
    {
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        var signals = 0;
        // 除外した登録ごとに追加イベントが発生した回数を数える。
        void Added() => signals++;
        Meltype.Diagnostics.ShortcutExclusions.Added += Added;
        try
        {
            new Settings { ToggleShortcuts = [null!, new() { Key = 32, Modifiers = ShortcutModifiers.Control }, new() { Key = 32, Modifiers = ShortcutModifiers.Control }, new() { Key = 65 }, new() { Key = 85, Modifiers = ShortcutModifiers.Control }] }.Clone().Normalize();
            var exclusions = Meltype.Diagnostics.ShortcutExclusions.Drain();
            Assert.Equal(4, exclusions.Length);
            Assert.Equal(4, signals);
            Assert.Equal(ShortcutValidationError.Invalid, exclusions[0].Reason);
            Assert.Equal(ShortcutValidationError.Duplicate, exclusions[1].Reason);
            Assert.Equal(ShortcutValidationError.UnmodifiedKey, exclusions[2].Reason);
            Assert.Equal(ShortcutValidationError.Conversion, exclusions[3].Reason);
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length);
            new Settings().Normalize();
            new Settings { ToggleShortcuts = [] }.Normalize();
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length);
        }
        finally
        {
            Meltype.Diagnostics.ShortcutExclusions.Added -= Added;
            Meltype.Diagnostics.ShortcutExclusions.Drain();
        }
    }

    private static KeyEvent Key(int vk, bool up = false, bool injected = false, int scan = 0) => new(vk, scan, false, up, injected, 1000);

    /// <summary>IME が登録キーを文字切り替えとして送った場合、セッションは確定せず日本語／英字を往復すること。</summary>
    [Test]
    public static void Shortcut_ImeTextToggleKeepsCompositionUncommitted()
    {
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionServices(), () => new Settings { LiveConversion = false })
        { Direct = false };
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
        Assert.Equal(0, settings.SwitchProfile("旧").EffectiveToggleShortcuts.Count, "旧プロファイルの OFF を維持");
        var directory = Path.Combine(Path.GetTempPath(), "meltype-shortcuts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "config.json");
            settings.Save(path);
            Assert.Equal(settings.EffectiveToggleShortcuts[0], Settings.Load(path).EffectiveToggleShortcuts[0], "保存・読み込み");
            empty.Save(path);
            Assert.Equal(0, Settings.Load(path).EffectiveToggleShortcuts.Count, "空の保存・読み込み");
            File.WriteAllText(path, "{\"SettingsVersion\":5,\"HankakuTogglesKeyboard\":false}");
            Assert.Equal(0, Settings.Load(path).EffectiveToggleShortcuts.Count, "旧ファイルの OFF を維持");
            Assert.Equal(0, Settings.Load(path).EffectiveToggleShortcuts.Count, "移行保存後も OFF を維持");
            File.WriteAllText(path, "{\"SettingsVersion\":5,\"HankakuTogglesKeyboard\":true}");
            Assert.Equal(2, Settings.Load(path).EffectiveToggleShortcuts.Count, "旧ファイルの ON は初期登録へ移行");
            File.WriteAllText(path, "{\"SettingsVersion\":5,\"HankakuTogglesKeyboard\":true,\"ToggleShortcuts\":[]}");
            Assert.Equal(0, Settings.Load(path).EffectiveToggleShortcuts.Count, "保存済みの空の一覧を優先");
            File.WriteAllText(path, "{\"SettingsVersion\":5,\"HankakuTogglesKeyboard\":false,\"ToggleShortcuts\":[{\"Key\":32,\"Modifiers\":2}]}");
            Assert.Equal(0x20, Settings.Load(path).EffectiveToggleShortcuts[0].Key, "保存済みの登録を優先");
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
        var settings = new Settings
        {
            ToggleShortcuts =
            [
                new() { Key = 0x75 }, new() { Key = 0x79, Modifiers = ShortcutModifiers.Shift },
                new() { Key = 0x55, Modifiers = ShortcutModifiers.Control },
                new() { Key = 0x1C }, valid, valid,
            ],
        }.Normalize();
        Assert.Equal(1, settings.EffectiveToggleShortcuts.Count, "Core で衝突と重複を除外");
        Assert.Equal(valid, settings.EffectiveToggleShortcuts[0]);
        var values = settings.ProfileValues();
        values["ToggleShortcuts"] = new JsonArray(new JsonObject { ["Key"] = 0x76 });
        settings.Profiles.Add(new SettingsProfile { Name = "衝突", Values = values });
        Assert.Equal(0, settings.SwitchProfile("衝突").EffectiveToggleShortcuts.Count, "保存済みプロファイルでも除外");
        var file = new JsonObject { ["format"] = "meltype-profile", ["name"] = "衝突", ["values"] = values.DeepClone() };
        Assert.Equal(0, settings.ImportProfile(file.ToJsonString())!.EffectiveToggleShortcuts.Count, "インポートでも除外");
    }

    /// <summary>文字入力・編集用の単独キーと Shift のみの組み合わせを拒否し、IME 用キーと Ctrl+Space は維持する。</summary>
    [Test]
    public static void Shortcut_RejectsUnmodifiedTextAndEditingKeys()
    {
        foreach (var mods in new[] { ShortcutModifiers.None, ShortcutModifiers.Shift })
            foreach (var key in new[] { 0x20, 0x0D, 0x1B, 0x41, 0x30, 0x09, 0x08, 0x25, 0x2E, 0xC0, 0x70 })
            {
                // Shift+Space だけは #1033 で切り替えキーにできる (Shortcut_ShiftSpaceIsAllowed... を参照)。
                if (mods == ShortcutModifiers.Shift && key == 0x20) continue;
                var binding = new ToggleShortcut { Key = key, Modifiers = mods };
                Assert.Equal(ShortcutValidationError.UnmodifiedKey, ToggleShortcutValidator.Validate(binding, new Settings(), []));
                Assert.Equal(0, new Settings { ToggleShortcuts = [binding] }.Normalize().EffectiveToggleShortcuts.Count, "読み込み時も通常入力を奪わない");
            }
        foreach (var key in new[] { 0x19, 0xF3, 0xF4 })
            Assert.Equal(ShortcutValidationError.None, ToggleShortcutValidator.Validate(new() { Key = key }, new Settings(), []));
        // 無変換・変換だけは英数・日本語にするキー
        foreach (var key in new[] { 0x1C, 0x1D })
            Assert.Equal(ShortcutValidationError.ConvertKeys, ToggleShortcutValidator.Validate(new() { Key = key }, new Settings(), []));
        Assert.Equal(ShortcutValidationError.None, ToggleShortcutValidator.Validate(
            new() { Key = 0x20, Modifiers = ShortcutModifiers.Control }, new Settings(), []));
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

    /// <summary>旧設定の OFF は空、ON は 2 つの初期登録へ移行し、通常設定・プロファイル・インポートで同じ規則を使い、明示された空の一覧は維持すること。</summary>
    [Test]
    public static void Shortcut_LegacyMigrationPreservesDisabledAndExplicitBindings()
    {
        foreach (var enabled in new[] { false, true })
        {
            var settings = new Settings { SettingsVersion = 5, LegacyHankaku = enabled };
            settings.Profiles.Add(new SettingsProfile { Name = "旧", Values = new JsonObject { ["HankakuTogglesKeyboard"] = enabled } });
            Assert.True(settings.Migrate(), "v6 に移行");
            Assert.Equal(enabled ? 2 : 0, settings.Normalize().EffectiveToggleShortcuts.Count, "通常設定の ON/OFF を維持");
            Assert.True(settings.LegacyHankaku is null, "移行後は旧値を破棄");
            Assert.True(!settings.ToJson().Contains("HankakuTogglesKeyboard"), "旧項目は保存しない");
            Assert.Equal(enabled ? 2 : 0, settings.SwitchProfile("旧").EffectiveToggleShortcuts.Count, "旧設定の ON/OFF を維持");
            Assert.True(!settings.Profiles.Single(p => p.Name == "旧").Values!.ContainsKey("HankakuTogglesKeyboard"), "旧項目を削除");
            Assert.True(!settings.Migrate(), "移行は一度だけ");
            var file = new JsonObject { ["format"] = "meltype-profile", ["name"] = "旧", ["values"] = new JsonObject { ["HankakuTogglesKeyboard"] = enabled } };
            Assert.Equal(enabled ? 2 : 0, settings.ImportProfile(file.ToJsonString())!.EffectiveToggleShortcuts.Count, "旧インポートの ON/OFF を維持");
        }
        var empty = new Settings { SettingsVersion = 5, ToggleShortcuts = [] };
        empty.Profiles.Add(new SettingsProfile { Name = "空", Values = new JsonObject { ["ToggleShortcuts"] = new JsonArray() } });
        empty.Migrate();
        Assert.Equal(0, empty.Normalize().EffectiveToggleShortcuts.Count, "空を維持");
        Assert.Equal(0, empty.SwitchProfile("空").EffectiveToggleShortcuts.Count, "プロファイルの空を維持");
    }

    /// <summary>
    /// #1033: Shift+Space は入力モード切り替えキーにでき、ほかの Shift だけのキーは今までどおり外すこと。
    /// 設定「Shift + Space で半角スペース」が ON のときは、無変換 / 変換 と同じく競合として外す。
    /// </summary>
    [Test]
    public static void Shortcut_ShiftSpaceIsAllowedAndConflictsWithTheHalfWidthSetting()
    {
        var settings = new Settings();
        var shiftSpace = new ToggleShortcut { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.Shift };

        Assert.Equal(ShortcutValidationError.None, ToggleShortcutValidator.Validate(shiftSpace, settings, []),
            "Shift+Space は切り替えキーにできる");
        Assert.Equal(ShortcutValidationError.UnmodifiedKey,
            ToggleShortcutValidator.Validate(new() { Key = 'A', Modifiers = ShortcutModifiers.Shift }, settings, []),
            "Shift+英字は文字の入力に使うので外す");
        Assert.Equal(ShortcutValidationError.UnmodifiedKey,
            ToggleShortcutValidator.Validate(new() { Key = '1', Modifiers = ShortcutModifiers.Shift }, settings, []),
            "Shift+数字も外す");
        Assert.Equal(ShortcutValidationError.UnmodifiedKey,
            ToggleShortcutValidator.Validate(new() { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.None }, settings, []),
            "Space だけは切り替えにしない");
        Assert.Equal(ShortcutValidationError.None,
            ToggleShortcutValidator.Validate(new() { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.Control }, settings, []),
            "Ctrl+Space は今までどおり");

        settings.SpaceWidth = SpaceWidth.HalfWidth;
        Assert.Equal(ShortcutValidationError.ShiftSpaceHalfWidth, ToggleShortcutValidator.Validate(shiftSpace, settings, []),
            "設定「スペースの幅」が「半角」なら競合として外す");
        Assert.Equal(ShortcutValidationError.None,
            ToggleShortcutValidator.Validate(new() { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.Control }, settings, []),
            "Shift+Space 以外は半角スペースの設定と競合しない");
    }

    /// <summary>#1033: 旧設定の "Shift+Space" が読み込み時の移行で残り、除外されないこと。</summary>
    [Test]
    public static void Shortcut_LegacyShiftSpaceSurvivesMigration()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meltype-shift-space-" + Guid.NewGuid() + ".json");
        Meltype.Diagnostics.ShortcutExclusions.Drain();
        try
        {
            File.WriteAllText(path, """
                {"SettingsVersion":6,"ToggleShortcuts":[],"ToggleKeyboardShortcuts":"Shift+Space"}
                """);
            var settings = Settings.Load(path);
            Assert.Equal(1, settings.EffectiveToggleShortcuts.Count, "旧設定の Shift+Space を残す");
            Assert.Equal(VirtualKeys.Space, settings.EffectiveToggleShortcuts[0].Key);
            Assert.Equal(ShortcutModifiers.Shift, settings.EffectiveToggleShortcuts[0].Modifiers);
            Assert.Equal(0, Meltype.Diagnostics.ShortcutExclusions.Drain().Length, "除外しない");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
