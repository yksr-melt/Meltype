// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;

namespace Meltype.Tests;

internal static class SettingsFileTests
{
    [Test]
    public static void Settings_OldNullAppRulesPreserveUserSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-null-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            foreach (var rules in new[] { "null", "[null]" })
            {
                File.WriteAllText(path, $$"""{"SettingsVersion": 11, "AppRules": {{rules}}, "Enabled": false}""");
                var settings = Settings.Load(path);
                Assert.True(!settings.Enabled, "保存されていた一時停止を維持する");
                Assert.True(settings.AppRules.Any(r => r.Process == "JumpClient.exe" && !r.Enabled), "旧版からの移行を実行する");
                Assert.True(!File.Exists(path + ".broken"), "読み込める設定として扱う");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void Settings_WithAValueOfTheWrongType_LoadsDefaults()
    {
        // 手で書いた値の型が違う (Process が数) と、古い版の設定の読み替えが InvalidOperationException を投げる。起動できなくせず、既定値で動く
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, """{"SettingsVersion":7,"TextInputApps":"a.exe","AppRules":[{"Process":1}]}""");
            var settings = Settings.Load(path);
            Assert.Equal(new Settings().LiveConversion, settings.LiveConversion, "既定値");
            Assert.True(File.Exists(path + ".broken"), "読めなかったファイルは .broken に残す");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void SettingsFile_ChecksWriteTimeOncePerInterval()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            void Write(string punctuation, int seconds)
            {
                File.WriteAllText(path, $"{{ \"SettingsVersion\": {Settings.CurrentVersion}, \"Punctuation\": \"{punctuation}\" }}");
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, seconds, DateTimeKind.Utc));
            }
            long now = 0;
            var loads = 0;
            var file = new SettingsFile(path, _ => loads++, () => now);
            Write("Japanese", 0);
            Assert.Equal(PunctuationStyle.Japanese, file.Current.Punctuation);

            Write("FullWidthCommaPeriod", 1);
            now = SettingsFile.CheckIntervalMs - 1;
            Assert.Equal(PunctuationStyle.Japanese, file.Current.Punctuation, "間隔の間は更新時刻を見ない");
            now = SettingsFile.CheckIntervalMs;
            Assert.Equal(PunctuationStyle.FullWidthCommaPeriod, file.Current.Punctuation, "間隔が経てば、書き換えた config.json を読む");

            now += SettingsFile.CheckIntervalMs;
            _ = file.Current;
            Assert.Equal(2, loads, "更新時刻が変わっていなければ読み直さない");

            // エディターが書いている途中の、壊れた JSON
            File.WriteAllText(path, "{ \"Punctuation\": ");
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc));
            now += SettingsFile.CheckIntervalMs;
            Assert.Equal(PunctuationStyle.FullWidthCommaPeriod, file.Current.Punctuation, "読み直せなければ、既定値に戻さず前の設定を使う");
            Assert.True(!File.Exists(path + ".broken"), "読み直しの失敗では .broken を作らない");
            // 更新時刻が変わらないまま読めるようになった (権限を直したときなど)
            Write("Japanese", 2);
            now += SettingsFile.CheckIntervalMs;
            Assert.Equal(PunctuationStyle.Japanese, file.Current.Punctuation, "読み直せなかった後は、更新時刻が同じでもまた読む");
            loads = 0;
            now += SettingsFile.CheckIntervalMs;
            _ = file.Current;
            Assert.Equal(0, loads, "読めた後は、更新時刻が変わるまで読み直さない");

            // 元のファイルを別名に移してから書くエディターの、書く前
            File.Delete(path);
            now += SettingsFile.CheckIntervalMs;
            Assert.Equal(PunctuationStyle.Japanese, file.Current.Punctuation, "読み直しで無ければ、既定値に戻さず前の設定を使う");
            Write("FullWidthCommaPeriod", 4);
            now += SettingsFile.CheckIntervalMs;
            Assert.Equal(PunctuationStyle.FullWidthCommaPeriod, file.Current.Punctuation, "書かれたら読み直す");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>設定アプリで 1 項目を変えて保存しても、直前にほかのプロセス (トレイ) が書いた値を、間隔の間の古い設定で戻さない。</summary>
    [Test]
    public static void SettingsFile_EditReadsLatestFileBeforeSaving()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-settings-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            var file = new SettingsFile(path, timeMs: () => 0);
            Assert.True(file.Current.Enabled, "最初は有効");
            // 間隔が経つ前に、ほかのプロセスが一時停止する
            var paused = new Settings { Enabled = false };
            paused.Save(path);
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc));
            var saved = file.Edit(settings =>
            {
                var next = settings.Clone();
                next.Punctuation = PunctuationStyle.FullWidthCommaPeriod;
                return next;
            })!;
            Assert.True(!saved.Enabled && saved.Punctuation == PunctuationStyle.FullWidthCommaPeriod, "一時停止のまま、変えた項目だけを重ねる");
            Assert.True(!Settings.Load(path).Enabled, "保存したファイルも一時停止のまま");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
