// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;

namespace Meltype.Tests;

internal static class SettingsFileTests
{
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
            Write("Japanese", 3);
            now += SettingsFile.CheckIntervalMs;
            Assert.Equal(PunctuationStyle.Japanese, file.Current.Punctuation, "書き終わったら読み直す");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
