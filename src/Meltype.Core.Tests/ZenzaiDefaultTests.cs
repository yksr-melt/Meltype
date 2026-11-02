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
    public static void ZenzaiBundle_EmptyPathUsesBundledFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "app");
        Assert.Equal(Path.Combine(root, "zenzai", ZenzaiBundle.ModelFileName), ZenzaiBundle.ModelPath("", root), "空なら同梱のモデル");
        Assert.Equal(Path.Combine(root, "zenzai", ZenzaiBundle.ServerFileName), ZenzaiBundle.ServerPath("  ", root), "空白だけも同梱のもの");
        Assert.Equal("D:\\my.gguf", ZenzaiBundle.ModelPath(" D:\\my.gguf ", root), "書いた場所を優先する");
    }
}
