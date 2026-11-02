// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.Tests;

/// <summary>打鍵数のカウンターと、typect (タイプ数カウンター) からの引き継ぎ。</summary>
internal static class KeyCounterTests
{
    private static KeyEvent Down(int vk) => new(vk, 0, false, false, false, 0);
    private static KeyEvent Up(int vk) => new(vk, 0, false, true, false, 0);

    private static KeyCounter OnCounter(string? path = null) => new(path) { Enabled = true };

    [Test]
    public static void Count_IgnoresAutoRepeatAndMergesModifiers()
    {
        var counter = OnCounter();
        var day = new DateTime(2026, 10, 19);
        counter.Count(Down(0x41), day, true);
        counter.Count(Down(0x41), day, true); // 押しっぱなしの繰り返し
        counter.Count(Up(0x41), day, true);
        counter.Count(Down(0x41), day, true);
        counter.Count(Down(VirtualKeys.LShift), day, true);
        counter.Count(Down(VirtualKeys.RShift), day, true);
        Assert.Equal(4L, counter.Total);
        Assert.Equal(4L, counter.OnDay(day));
        var keys = counter.TopKeys().ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal(2L, keys["A"]);
        Assert.Equal(2L, keys["Shift"]);
    }

    [Test]
    public static void Count_PairsDownAndUpByPhysicalKey()
    {
        // 押下と解放で仮想キーコードが変わっても (同じスキャンコード)、離したことにして次の打鍵を数える
        var counter = OnCounter();
        var day = new DateTime(2026, 10, 19);
        counter.Count(new KeyEvent(0xF3, 0x29, false, false, false, 0), day, true);
        counter.Count(new KeyEvent(0xF4, 0x29, false, true, false, 0), day, true);
        counter.Count(new KeyEvent(0xF3, 0x29, false, false, false, 0), day, true);
        Assert.Equal(2L, counter.Total);
    }

    [Test]
    public static void Count_DoesNothingWhenOffOrNotAllowed()
    {
        var off = new KeyCounter(null);
        off.Count(Down(0x41), DateTime.Today, true);
        Assert.Equal(0L, off.Total);

        // 一時停止中などは数えないが、離したことは覚えるので、次の打鍵は数える
        var counter = OnCounter();
        var day = new DateTime(2026, 10, 19);
        counter.Count(Down(0x41), day, false);
        counter.Count(Up(0x41), day, false);
        Assert.Equal(0L, counter.Total);
        counter.Count(Down(0x41), day, true);
        Assert.Equal(1L, counter.Total);
    }

    [Test]
    public static void Import_ReadsKeysDaysAndTotal()
    {
        var counter = OnCounter();
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
        var counter = OnCounter();
        counter.Count(Down(0x42), DateTime.Today, true);
        counter.Import("a=5\nb=7\n");
        Assert.Equal(13L, counter.Total);
        Assert.Equal(8L, counter.TopKeys().First(p => p.Key == "B").Value);
    }

    [Test]
    public static void Import_SameTextTwice_CountsOnce()
    {
        var counter = OnCounter();
        const string text = "A,10\nB,20\n";
        Assert.True(!counter.Import(text).Duplicate, "最初の取り込みは足す");
        var again = counter.Import(text);
        Assert.True(again.Duplicate, "同じテキストは足さない");
        Assert.Equal(30L, counter.Total);
        Assert.Equal(10L, counter.TopKeys().First(p => p.Key == "A").Value);
    }

    [Test]
    public static void Reset_ClearsCountsButKeepsOnSetting()
    {
        var counter = OnCounter();
        counter.Count(Down(0x41), DateTime.Today, true);
        counter.Reset();
        Assert.Equal(0L, counter.Total);
        Assert.True(counter.Enabled, "数えるかどうかの設定は残す");
    }

    [Test]
    public static void SaveAndLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-keycount-{Guid.NewGuid():N}.json");
        try
        {
            var counter = OnCounter(path);
            counter.Count(Down(VirtualKeys.Space), new DateTime(2026, 1, 2), true);
            counter.Save();
            var loaded = new KeyCounter(path);
            Assert.True(loaded.Enabled, "設定も保存される");
            Assert.Equal(1L, loaded.Total);
            Assert.Equal(1L, loaded.OnDay(new DateTime(2026, 1, 2)));
            Assert.Equal("Space", loaded.TopKeys()[0].Key);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public static void Analyze_SummarizesDaysHoursAndHands()
    {
        var counter = OnCounter();
        var today = new DateTime(2026, 10, 19);
        void Press(int vk, DateTime at) { counter.Count(Down(vk), at, true); counter.Count(Up(vk), at, true); }
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
