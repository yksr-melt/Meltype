// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Reflection;
using System.Text.Json.Nodes;
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

internal static class SettingsSchemaTests
{
    [Test]
    public static void SettingsSchema_DescribesConfigKeysWithTitlesAndChoices()
    {
        var schema = JsonNode.Parse(SettingsSchema.Create())!.AsObject();
        var properties = schema["properties"]!.AsObject();
        var keys = JsonNode.Parse(new Settings().ToJson())!.AsObject().Select(p => p.Key).ToList();
        Assert.Equal(string.Join(",", keys), string.Join(",", properties.Select(p => p.Key)), "config.json のすべての項目 ($schema を含む) が schema にある");
        Assert.Equal(false, schema["additionalProperties"]?.GetValue<bool>(), "知らない項目 (書き間違い) を警告する");
        Assert.Equal("句読点", properties["Punctuation"]!["title"]?.GetValue<string>(), "設定画面の名前を title に出す");
        Assert.True(properties["AutoDetection"]!["description"]?.GetValue<string>().Length > 0, "設定画面の説明を description に出す");
        Assert.Equal("Japanese", properties["Punctuation"]!["default"]?.GetValue<string>(), "既定値を default に出す");
        Assert.Equal("3. 判定", properties["AutoDetection"]!["x-category"]?.GetValue<string>(), "設定画面の分類を x-category に出す");
        string Platforms(string key) => string.Join(",", properties[key]!["x-platforms"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("Windows", Platforms("CompositionTheme"), "一部の OS でしか使わない項目に、使う OS を x-platforms に出す");
        Assert.Equal("Windows,Mac", Platforms("UseZenzai"));
        Assert.True(properties["AutoDetection"]!["x-platforms"] is null, "どの OS でも使う項目には出さない");
        // 独自の JsonConverter を付けた列挙型も、選択肢が出る
        Assert.Equal("Keyboard,Tsf", string.Join(",", properties["Mode"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>())));
        Assert.Equal("Romaji,Kana", string.Join(",", properties["InputStyle"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>())));
        Assert.Equal("、。", properties["Punctuation"]!["enumDescriptions"]![0]?.GetValue<string>(), "選択肢の説明を出す");
        Assert.True(properties["CompositionFont"]!["description"]!.GetValue<string>().StartsWith("Windows のみ。", StringComparison.Ordinal), "一部の OS でしか使わない項目は、使う OS を説明の先頭に書く");
        Assert.True(properties["UseZenzai"]!["description"]!.GetValue<string>().StartsWith("Windows・Mac のみ。", StringComparison.Ordinal), "OS が複数なら・でつなぐ");
        Assert.True(!properties["Punctuation"]!["description"]!.GetValue<string>().Contains("のみ。"), "どの OS でも使う項目には書かない");
    }

    [Test]
    public static void Platforms_AreSomeButNotAllPlatforms()
    {
        var all = Enum.GetValues<Platform>().Aggregate((a, b) => a | b);
        foreach (var property in typeof(Settings).GetProperties())
        {
            if (property.GetCustomAttribute<PlatformsAttribute>() is not { } attribute) continue;
            Assert.True(attribute.Platforms != 0 && attribute.Platforms != all && (attribute.Platforms & ~all) == 0,
                $"{property.Name}: 使う OS は 1 つ以上で、すべての OS なら Platforms を付けない");
        }
    }

    [Test]
    public static void SettingsJson_KeepsSchemaReferenceAcrossSaveAndOutOfProfiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, "{ \"$schema\": \"../my.schema.json\", \"SettingsVersion\": 5 }");
            var loaded = Settings.Load(path);
            loaded.Save(path);
            Assert.True(File.ReadAllText(path).Contains("\"$schema\": \"../my.schema.json\""), "ユーザーが書いた $schema を保存で消さない");
            Assert.True(!loaded.ProfileValues().ContainsKey("$schema"), "$schema はプロファイルの値に入れない");
            Assert.True(new Settings().ToJson().Contains($"\"$schema\": \"./{SettingsSchema.FileName}\""), "新しく作る config.json は同じフォルダーの schema を指す");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void MeltypeSession_WritesSchemaNextToConfig()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-schema-" + Guid.NewGuid().ToString("N"));
        try
        {
            MeltypeSession.CreateDefault(directory, new CompositionTests.FakeConverter(), null, null);
            Assert.Equal(SettingsSchema.Create(), File.ReadAllText(Path.Combine(directory, SettingsSchema.FileName)), "Mac・Linux 版は config.json の隣に schema を書く");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
