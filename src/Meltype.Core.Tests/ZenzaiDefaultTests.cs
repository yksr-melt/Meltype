// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>Zenzai を同梱して既定 ON にしたこと (issue #1120・#1130)。</summary>
internal static class ZenzaiDefaultTests
{
    [Test]
    public static void ZenzaiDefault_NewSettingsUseZenzai()
    {
        Assert.True(new Settings().UseZenzai, "新しい設定では Zenzai を使う");
        Assert.Equal("", new Settings().ZenzaiServerPath, "場所は空 (同梱のものを使う)");
    }

    [Test]
    public static void ZenzaiDefault_MigratesOnlyUntouchedOff()
    {
        foreach (var (json, expected, why) in new[]
        {
            ("{ \"SettingsVersion\": 8, \"UseZenzai\": false }", true, "前の既定 (OFF・場所が空) は ON にする"),
            ("{ \"SettingsVersion\": 8, \"UseZenzai\": false, \"ZenzaiModelPath\": \"C:\\\\m.gguf\" }", false, "自分で場所を書いて OFF にした人はそのまま"),
            ("{ \"SettingsVersion\": 8, \"UseZenzai\": true, \"ZenzaiServerPath\": \"C:\\\\s.exe\" }", true, "ON はそのまま"),
            ($"{{ \"SettingsVersion\": {Settings.CurrentVersion}, \"UseZenzai\": false }}", false, "移行の後に OFF にしたものは戻さない"),
        })
        {
            var directory = Path.Combine(Path.GetTempPath(), "meltype-zenzai-default-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "config.json");
                File.WriteAllText(path, json);
                Assert.Equal(expected, Settings.Load(path).UseZenzai, why);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    [Test]
    public static void ZenzaiDefault_MigratesProfileValues()
    {
        // プロファイルに保存された前の既定 (UseZenzai: false) も ON にする (切り替えて OFF に戻らないように)。
        // 自分で場所を書いて OFF にしたプロファイルはそのまま
        var directory = Path.Combine(Path.GetTempPath(), "meltype-zenzai-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, """
                { "SettingsVersion": 8, "UseZenzai": false, "ActiveProfile": "仕事",
                  "Profiles": [
                    { "Name": "仕事", "Values": { "UseZenzai": false } },
                    { "Name": "趣味", "Values": { "UseZenzai": false } },
                    { "Name": "自前", "Values": { "UseZenzai": false, "ZenzaiModelPath": "C:\\m.gguf" } } ] }
                """);
            var loaded = Settings.Load(path);
            Assert.True(loaded.UseZenzai, "今のプロファイル");
            Assert.True(loaded.SwitchProfile("趣味").UseZenzai, "切り替えた先のプロファイルも ON");
            Assert.True(!loaded.SwitchProfile("自前").UseZenzai, "自分で場所を書いて OFF にしたプロファイルはそのまま");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void ZenzaiBundle_EmptyPathUsesBundledFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "meltype-zenzai-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "zenzai"));
        try
        {
            Assert.Equal(Path.Combine(root, "zenzai", ZenzaiBundle.ServerFileName), ZenzaiBundle.ServerPath("  ", root), "空白だけなら同梱の llama-server");
            Assert.True(!File.Exists(ZenzaiBundle.ModelPath("", root)), "同梱のモデルが無ければ、無い場所 (見つからないと答える)");
            var model = Path.Combine(root, "zenzai", "zenz-test.gguf");
            File.WriteAllText(model, "");
            Assert.Equal(model, ZenzaiBundle.ModelPath("", root), "名前によらず、同梱の .gguf を使う");
            Assert.Equal("D:\\my.gguf", ZenzaiBundle.ModelPath(" D:\\my.gguf ", root), "書いた場所を優先する");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
