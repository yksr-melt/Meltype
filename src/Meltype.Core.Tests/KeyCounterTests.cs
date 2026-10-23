// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.Tests;

/// <summary>打鍵数のカウンターと、typect (タイプ数カウンター) からの引き継ぎ。</summary>
internal static class KeyCounterTests
{
    private static KeyEvent Down(int vk) => new(vk, 0, false, false, false, 0);
    private static KeyEvent Up(int vk) => new(vk, 0, false, true, false, 0);

    [Test]
    public static void Count_IgnoresAutoRepeatAndMergesModifiers()
    {
        var counter = new KeyCounter(null);
        var day = new DateTime(2026, 10, 19);
        counter.Count(Down(0x41), day);
        counter.Count(Down(0x41), day); // 押しっぱなしの繰り返し
        counter.Count(Up(0x41), day);
        counter.Count(Down(0x41), day);
        counter.Count(Down(VirtualKeys.LShift), day);
        counter.Count(Down(VirtualKeys.RShift), day);
        Assert.Equal(4L, counter.Total);
        Assert.Equal(4L, counter.OnDay(day));
        var keys = counter.TopKeys().ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal(2L, keys["A"]);
        Assert.Equal(2L, keys["Shift"]);
    }

    [Test]
    public static void Import_ReadsKeysDaysAndTotal()
    {
        var counter = new KeyCounter(null);
        var result = counter.Import("キー,回数\r\n\"A\",\"1,234\"\r\nSpace\t500\r\nF1: 3回\r\nLShift 10\r\n2026/10/18,800\r\n合計 99,999\r\n");
        Assert.Equal(99999L, result.Total);
        Assert.Equal(99999L, counter.Total);
        var keys = counter.TopKeys().ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal(1234L, keys["A"]);
        Assert.Equal(500L, keys["Space"]);
        Assert.Equal(3L, keys["F1"]);
        Assert.Equal(10L, keys["Shift"]);
        Assert.Equal(800L, counter.OnDay(new DateTime(2026, 10, 18)));
    }

    [Test]
    public static void Import_WithoutTotalUsesSumOfKeys()
    {
        var counter = new KeyCounter(null);
        counter.Count(Down(0x42), DateTime.Today);
        counter.Import("a=5\nb=7\n");
        Assert.Equal(13L, counter.Total);
        Assert.Equal(8L, counter.TopKeys().First(p => p.Key == "B").Value);
    }

    [Test]
    public static void SaveAndLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-keycount-{Guid.NewGuid():N}.json");
        try
        {
            var counter = new KeyCounter(path);
            counter.Count(Down(VirtualKeys.Space), new DateTime(2026, 1, 2));
            counter.Save();
            var loaded = new KeyCounter(path);
            Assert.Equal(1L, loaded.Total);
            Assert.Equal(1L, loaded.OnDay(new DateTime(2026, 1, 2)));
            Assert.Equal("Space", loaded.TopKeys()[0].Key);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public static void Analyze_SummarizesDaysHoursAndHands()
    {
        var counter = new KeyCounter(null);
        var today = new DateTime(2026, 10, 19);
        void Press(int vk, DateTime at) { counter.Count(Down(vk), at); counter.Count(Up(vk), at); }
        Press(0x41, today.AddHours(9)); // A (左手)
        Press(0x41, today.AddHours(9));
        Press(0x4A, today.AddHours(21)); // J (右手)
        Press(VirtualKeys.Back, today.AddDays(-1).AddHours(9));
        Press(0x41, today.AddDays(-3).AddHours(9));
        var a = counter.Analyze(today);
        Assert.Equal(3, a.ActiveDays);
        Assert.Equal(2, a.Streak);
        Assert.Equal("2026-10-19", a.BestDay);
        Assert.Equal(9, a.PeakHour);
        Assert.Equal(5L, a.Last7Days);
        Assert.Equal(0.75, a.LeftHandShare);
        Assert.Equal(0.2, a.CorrectionShare);
    }
}
