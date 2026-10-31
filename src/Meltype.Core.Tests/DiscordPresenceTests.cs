// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using System.Text.Json;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>Discord のステータス (Rich Presence) に出す内容と、IPC の形。</summary>
internal static class DiscordPresenceTests
{
    [Test]
    public static void Frame_IsOpcodeLengthAndUtf8Json()
    {
        var frame = DiscordPresence.Frame(1, "{\"a\":\"打\"}");
        Assert.Equal(1, BitConverter.ToInt32(frame, 0));
        Assert.Equal(frame.Length - 8, BitConverter.ToInt32(frame, 4));
        Assert.Equal("{\"a\":\"打\"}", Encoding.UTF8.GetString(frame, 8, frame.Length - 8));
    }

    [Test]
    public static void Activity_ShowsOnlyCountsWithDigitGrouping()
    {
        var started = new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
        using var doc = JsonDocument.Parse(DiscordPresence.ActivityJson(new(12345, 1234567), 250, started));
        var root = doc.RootElement;
        Assert.Equal("今日 12,345 打", root.GetProperty("details").GetString());
        Assert.Equal("合計 1,234,567 打 · 250 打/分", root.GetProperty("state").GetString());
        Assert.Equal(started.ToUnixTimeSeconds(), root.GetProperty("timestamps").GetProperty("start").GetInt64());
        // 入力内容・アプリ名などは入れない (表示する欄だけ)
        var names = root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal("buttons,details,state,timestamps", string.Join(",", names));
    }

    [Test]
    public static void Activity_OmitsKpmBeforeSecondUpdate()
    {
        using var doc = JsonDocument.Parse(DiscordPresence.ActivityJson(new(0, 0), null, DateTimeOffset.UnixEpoch));
        Assert.Equal("合計 0 打", doc.RootElement.GetProperty("state").GetString());
    }

    [Test]
    public static void SetActivity_NullClearsPresence()
    {
        using var doc = JsonDocument.Parse(DiscordPresence.SetActivityJson(null));
        Assert.Equal("SET_ACTIVITY", doc.RootElement.GetProperty("cmd").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("args").GetProperty("activity").ValueKind);
    }

    [Test]
    public static void KeysPerMinute_ScalesToOneMinuteAndNeverNegative()
    {
        Assert.Equal(120L, DiscordPresence.KeysPerMinute(40, TimeSpan.FromSeconds(20)));
        Assert.Equal(0L, DiscordPresence.KeysPerMinute(-5, TimeSpan.FromSeconds(20))); // リセットした直後
        Assert.Equal(0L, DiscordPresence.KeysPerMinute(10, TimeSpan.Zero));
    }

    [Test]
    public static void NoDiscord_DoesNotThrowAndDisposesQuickly()
    {
        // Discord が起動していなくても例外を外に出さず、すぐ止まる
        var presence = new DiscordPresence(() => true, () => new(1, 1), "0");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        presence.Dispose();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "すぐ止まる");
    }
}
