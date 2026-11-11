// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 MedeiaBeliar

using Meltype.Config;

namespace Meltype.Tests;

internal static class InputSuspensionTests
{
    [Test]
    public static void JapaneseOnly_PersistsAsSharedSetting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-keyboard-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}");
            Assert.True(!Settings.Load(path).JapaneseKeyboardOnly, "既存の設定ファイルでは OFF");
            var settings = new Settings { JapaneseKeyboardOnly = true };
            settings.Clone().Normalize().Save(path);
            Assert.True(Settings.Load(path).JapaneseKeyboardOnly, "保存・複製・読み込みで設定が残る");
            var profile = settings.Normalize().AddProfile("仕事用")!;
            profile.JapaneseKeyboardOnly = false;
            Assert.True(!profile.SwitchProfile(Settings.DefaultProfileName).JapaneseKeyboardOnly, "全プロファイル共通の設定");
        }
        finally { File.Delete(path); }
    }
}
