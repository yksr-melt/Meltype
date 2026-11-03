// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;
using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>「なう」の再生中の曲 (issue #1400) と、「とけい」「じこく」のタイムゾーンの時刻 (issue #1401)。</summary>
internal static class NowAndClockTests
{
    [Test]
    public static void Issue1400_NowPlayingIsOfferedFirst()
    {
        var k = new Keyboard { NowPlayingCandidates = true };
        k.Host.NowPlaying = ("夜に駆ける", "YOASOBI");
        k.Type("nau ");
        var candidates = k.Host.View!.Candidates.ToList();
        var song = candidates.IndexOf("夜に駆ける / YOASOBI");
        var time = candidates.FindIndex(c => c.Contains(':'));
        Assert.True(song >= 0 && time > song, "曲名 / アーティストを時刻より先に: " + string.Join(",", candidates));
        Assert.Equal("なう", candidates[0], "ふつうの変換の候補はそのまま先頭");
        Assert.True(candidates.Contains("夜に駆ける"), "曲名だけも出す");
    }

    [Test]
    public static void Issue1400_NoSongOrOffKeepsTimes()
    {
        var none = new Keyboard { NowPlayingCandidates = true };
        none.Type("nau ");
        Assert.True(none.Host.View!.Candidates.Any(c => c.Contains(':')), "再生中の曲が無ければ今までどおり時刻");
        var off = new Keyboard();
        off.Host.NowPlaying = ("曲", "人");
        off.Type("nau ");
        Assert.True(!off.Host.View!.Candidates.Any(c => c.Contains('曲')), "OFF なら曲を読まない");
        var ima = new Keyboard { NowPlayingCandidates = true };
        ima.Host.NowPlaying = ("曲", "人");
        ima.Type("ima ");
        Assert.True(!ima.Host.View!.Candidates.Any(c => c.Contains("曲 /")), "いま には曲を出さない");
    }

    [Test]
    public static void Issue1400_ArtistUnknownShowsTitle()
    {
        var k = new Keyboard { NowPlayingCandidates = true };
        k.Host.NowPlaying = ("ラジオ番組", "");
        k.Type("nau ");
        Assert.True(k.Host.View!.Candidates.Contains("ラジオ番組") && !k.Host.View.Candidates.Any(c => c.StartsWith("ラジオ番組 /", StringComparison.Ordinal)), "アーティストが無ければ曲名だけ (/ を付けない)");
    }

    [Test]
    public static void Issue1401_ClockUsesConfiguredZone()
    {
        // 2026-07-01 23:30 UTC は ニューヨーク (夏時間 UTC-4) で 19:30、東京で 翌 8:30
        var utc = new DateTime(2026, 7, 1, 23, 30, 0, DateTimeKind.Utc);
        var ny = new Keyboard { ClockTimeZone = "America/New_York", UtcNow = utc };
        ny.Type("tokei ");
        Assert.True(ny.Host.View!.Candidates.Contains("19:30"), "夏時間を反映: " + string.Join(",", ny.Host.View.Candidates));
        Assert.True(ny.Host.View.Candidates.Contains("午後7時30分"), "午前・午後の表記");
        var tokyo = new Keyboard { ClockTimeZone = "Tokyo Standard Time", UtcNow = utc };
        tokyo.Type("jikoku ");
        Assert.True(tokyo.Host.View!.Candidates.Contains("08:30"), "Windows の ID も読む: " + string.Join(",", tokyo.Host.View.Candidates));
        Assert.True(tokyo.Host.View.Candidates.Any(c => c.StartsWith("7/2(木) 08:30", StringComparison.Ordinal)), "日付をまたいだ日付を付ける");
    }

    [Test]
    public static void Issue1401_WinterTimeAndUnknownZone()
    {
        // 1 月はニューヨークが標準時 (UTC-5)
        var k = new Keyboard { ClockTimeZone = "America/New_York", UtcNow = new DateTime(2026, 1, 15, 12, 5, 0, DateTimeKind.Utc) };
        k.Type("tokei ");
        Assert.True(k.Host.View!.Candidates.Contains("07:05"), string.Join(",", k.Host.View.Candidates));
        Assert.True(CompositionController.ClockZone("Not/AZone") is null && CompositionController.ClockZone(" ") is null, "見つからない・空はこの PC の時刻");
    }

    [Test]
    public static void Issue1401_ImaUsesConfiguredZone()
    {
        // いま も、とけい・じこく と同じく設定のタイムゾーンの時刻 (2026-07-01 23:30 UTC はニューヨークで 19:30)
        var k = new Keyboard { ClockTimeZone = "America/New_York", UtcNow = new DateTime(2026, 7, 1, 23, 30, 0, DateTimeKind.Utc) };
        k.Type("ima ");
        Assert.True(k.Host.View!.Candidates.Contains("19:30"), string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void Issue1401_SameZoneAsThisPcHasNoZoneName()
    {
        // この PC と同じタイムゾーンを書いても、タイムゾーンの名前を付けた候補は出さない (設定の説明どおり)
        var k = new Keyboard { ClockTimeZone = "Asia/Tokyo", LocalTimeZone = CompositionController.ClockZone("Tokyo Standard Time")!, UtcNow = new DateTime(2026, 7, 1, 23, 30, 0, DateTimeKind.Utc) };
        k.Type("tokei ");
        Assert.True(!k.Host.View!.Candidates.Any(c => c.Contains("Asia/Tokyo", StringComparison.Ordinal)), string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void Issue1400_ChosenSongIsNotLearned()
    {
        // 選んだ曲は学習しない (次の「なう」で前の曲が最初に出ないように)
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history) { NowPlayingCandidates = true };
        k.Host.NowPlaying = ("夜に駆ける", "YOASOBI");
        k.Type("nau ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("夜に駆ける");
        Assert.True(index > 0, string.Join(",", k.Host.View.Candidates));
        for (var i = 0; i < index; i++) k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Return);
        Assert.Equal("夜に駆ける", k.Host.Document);
        Assert.Equal(null, history.Get("なう"), "学習しない");
    }

    [Test]
    public static void Issue1401_DefaultUsesLocalNow()
    {
        var k = new Keyboard(now: () => new DateTime(2026, 11, 2, 14, 7, 0));
        k.Type("tokei ");
        Assert.True(k.Host.View!.Candidates.Contains("14:07"), "既定はこの PC の時刻");
        Assert.True(!k.Host.View.Candidates.Any(c => c.Contains('(')), "この PC のタイムゾーンならタイムゾーンの名前を付けない");
    }
}
