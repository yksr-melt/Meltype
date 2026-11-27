// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Composition.Utility;
using Meltype.Input;
using Meltype.Learning;
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
