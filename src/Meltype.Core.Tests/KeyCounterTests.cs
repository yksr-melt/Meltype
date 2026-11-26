// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
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
        var counter = new KeyCounter(null);
        var day = new DateTime(2026, 10, 19);
        counter.Count(new KeyEvent(0xF3, 0x29, false, false, false, 0), day, true);
        counter.Count(new KeyEvent(0xF4, 0x29, false, true, false, 0), day, true);
        counter.Count(new KeyEvent(0xF3, 0x29, false, false, false, 0), day, true);
        Assert.Equal(2L, counter.Total);
    }

    [Test]
    public static void Count_DoesNothingWhenNotAllowed()
    {
        // 一時停止中などは数えないが、離したことは覚えるので、次の打鍵は数える
        var counter = new KeyCounter(null);
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
        counter.Count(Down(0x42), DateTime.Today, true);
        counter.Import("a=5\nb=7\n");
        Assert.Equal(13L, counter.Total);
        Assert.Equal(8L, counter.TopKeys().First(p => p.Key == "B").Value);
    }

    [Test]
    public static void Import_SameTextTwice_CountsOnce()
    {
        var counter = new KeyCounter(null);
        const string text = "A,10\nB,20\n";
        Assert.True(!counter.Import(text).Duplicate, "最初の取り込みは足す");
        var again = counter.Import(text);
        Assert.True(again.Duplicate, "同じテキストは足さない");
        Assert.Equal(30L, counter.Total);
        Assert.Equal(10L, counter.TopKeys().First(p => p.Key == "A").Value);
    }

    [Test]
    public static void Reset_ClearsCounts()
    {
        var counter = new KeyCounter(null);
        counter.Count(Down(0x41), DateTime.Today, true);
        counter.Reset();
        Assert.Equal(0L, counter.Total);
    }

    [Test]
    public static void SaveAndLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-keycount-{Guid.NewGuid():N}.json");
        try
        {
            var counter = new KeyCounter(path);
            counter.Count(Down(VirtualKeys.Space), new DateTime(2026, 1, 2), true);
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

    [Test]
    public static void Count_NamesTypedCharactersLikeKeys()
    {
        // フックの無い OS: 文字を入力するキーは仮想キーコードが無い
        var counter = new KeyCounter(null);
        var day = new DateTime(2026, 10, 19);
        counter.Count(0, 'a', day);
        counter.Count(0, 'A', day);
        counter.Count(VirtualKeys.Space, ' ', day);
        counter.Count(0, 0, day);
        Assert.Equal(4L, counter.OnDay(day));
        Assert.Equal("A=2,Space=1", string.Join(",", counter.TopKeys().Select(p => $"{p.Key}={p.Value}")));
    }

    // typect の保存ファイルの形 (KeyCounter.TryReadTypect のコメント) を作る
    private static byte[] Typect(uint[] hours, Dictionary<int, uint>? vks)
    {
        var words = new uint[vks is null ? 29 : 285 + 8];
        (words[0], words[1], words[2], words[3]) = (203, 2026, 11, 23);
        words[4] = (uint)hours.Sum(h => (long)h);
        hours.CopyTo(words, 5);
        foreach (var (vk, n) in vks ?? []) words[29 + vk] = n;
        var bytes = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    [Test]
    public static void ImportFile_ReadsTypectBinary()
    {
        var hours = new uint[24];
        (hours[0], hours[23]) = (100, 60);
        var counter = new KeyCounter(null);
        var result = counter.ImportFile(Typect(hours, new() { [0x41] = 120, [VirtualKeys.LShift] = 25, [VirtualKeys.RShift] = 5, [0] = 10 }));
        Assert.Equal(160L, result.Total);
        Assert.Equal(160L, counter.Total);
        var keys = counter.TopKeys().ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal(120L, keys["A"]);
        Assert.Equal(30L, keys["Shift"]);
        Assert.Equal(2, keys.Count);
        Assert.Equal(0, counter.Analyze(new DateTime(2026, 11, 23)).PeakHour);
        Assert.True(counter.ImportFile(Typect(hours, new() { [0x41] = 120, [VirtualKeys.LShift] = 25, [VirtualKeys.RShift] = 5, [0] = 10 })).Duplicate, "同じファイルは二重に足さない");
    }

    [Test]
    public static void ImportFile_TypectWithoutKeyTableCountsTotalOnly()
    {
        // キーごとに数えない版はキーの表が無い・和が合わない
        var hours = new uint[24];
        hours[12] = 50;
        var counter = new KeyCounter(null);
        Assert.Equal(50L, counter.ImportFile(Typect(hours, null)).Total);
        Assert.Equal(0, counter.TopKeys().Count);
    }

    [Test]
    public static void ImportFile_ReadsTextOtherwise()
    {
        var counter = new KeyCounter(null);
        Assert.Equal(12L, counter.ImportFile(System.Text.Encoding.UTF8.GetBytes("a=5\nb=7\n")).Total);
    }
}
