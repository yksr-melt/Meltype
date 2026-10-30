// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Tests;

/// <summary>自動更新で版を比べる (プレリリース版の自動更新、issue #1206)。packaging/update.ps1 と同じ順にする。</summary>
internal static class UpdateVersionTests
{
    [Test]
    public static void CompareVersions_PutsPrereleasesBeforeTheRelease()
    {
        // 古い順
        string[] ordered = ["1.2.9", "1.3.0", "1.3.1-pre.1", "1.3.1-pre.2", "1.3.1-pre.10", "1.3.1", "1.3.2-pre.1", "1.10.0"];
        for (var i = 0; i < ordered.Length; i++)
        {
            for (var j = 0; j < ordered.Length; j++)
                Assert.Equal(Math.Sign(i.CompareTo(j)), ProjectInfo.CompareVersions(ordered[i], ordered[j]), $"{ordered[i]} と {ordered[j]}");
        }
        Assert.Equal(1, ProjectInfo.CompareVersions("1.3.1-pre.3+abc", "1.3.1-pre.2"), "ビルドの情報 (+ のあと) は見ない");
        Assert.Equal(1, ProjectInfo.CompareVersions("1.3.1-rc.1", "1.3.1-pre.9"), "数字でない部分は文字の順");
        Assert.Equal(1, ProjectInfo.CompareVersions("1.3.1-pre.1.1", "1.3.1-pre.1"), "同じところまで同じなら長いほうが新しい");
        Assert.Equal(null, ProjectInfo.CompareVersions("?", "1.3.0"));
    }

    [Test]
    public static void PrereleaseVersions_AreRecognized()
    {
        Assert.True(ProjectInfo.IsPrerelease("1.3.1-pre.3"), "1.3.1-pre.3");
        Assert.True(!ProjectInfo.IsPrerelease("1.3.1"), "1.3.1");
        // プレリリース版を使っていても、更新を確かめる (前は版を読めず、テスト版として扱っていた)
        Assert.True(ProjectInfo.IsPublicRelease("1.3.1-pre.3"), "1.3.1-pre.3");
        Assert.True(!ProjectInfo.IsPublicRelease("0.9.0-pre.1"), "0.9.0-pre.1");
    }

    [Test]
    public static void UpdatePrerelease_IsOffByDefaultAndSharedByProfiles()
    {
        var settings = new Settings();
        Assert.True(!settings.UpdatePrerelease, "標準は正式版だけ");
        Assert.True(!settings.ProfileValues().ContainsKey(nameof(Settings.UpdatePrerelease)), "プロファイルでは変えない");
    }
}
