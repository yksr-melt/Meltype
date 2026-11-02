// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Tests;

/// <summary>更新したあとの初めての起動で、リリースノートを一度だけ見せる (issue #1207)。</summary>
internal static class ReleaseNotesTests
{
    [Test]
    public static void ReleaseNotes_AreShownOnceAfterAnUpdate()
    {
        Assert.True(ProjectInfo.ShouldShowReleaseNotes("1.3.0", "1.3.1", firstRun: false), "更新したら見せる");
        Assert.True(ProjectInfo.ShouldShowReleaseNotes(null, "1.3.1", firstRun: false), "この仕組みが無い版からの更新でも見せる");
        Assert.True(!ProjectInfo.ShouldShowReleaseNotes("1.3.1", "1.3.1", firstRun: false), "見せた版では、次からは見せない");
        Assert.True(!ProjectInfo.ShouldShowReleaseNotes(null, "1.3.1", firstRun: true), "新しく入れたときは見せない");
        Assert.True(!ProjectInfo.ShouldShowReleaseNotes(null, "?", firstRun: false), "版が分からなければ見せない");
    }

    [Test]
    public static void ReleaseUrls_PointToTheTagAndTheList()
    {
        Assert.Equal("https://github.com/yksr-melt/Meltype/releases/tag/v1.3.1-pre.3", ProjectInfo.ReleaseUrl("1.3.1-pre.3"));
        Assert.Equal("https://github.com/yksr-melt/Meltype/releases", ProjectInfo.ReleasesUrl);
        Assert.True(!new Settings().ProfileValues().ContainsKey(nameof(Settings.ReleaseNotesShown)), "プロファイルでは変えない");
    }
}
