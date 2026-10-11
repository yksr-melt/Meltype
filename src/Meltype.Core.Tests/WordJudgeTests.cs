// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>更新の前に「あなたの打ち方で変わる語」を見せる (issue #285)。</summary>
internal static class WordJudgeTests
{
    [Test]
    public static void JudgeAll_RoundTripsAndFindsChanges()
    {
        var detector = CompositionTests.Detector;
        var output = WordJudge.JudgeAll(detector, ["kyou", "github", "kyou", "x", "あ", "mata"]);
        var now = WordJudge.Parse(output);
        Assert.Equal("きょう", now["kyou"]);
        Assert.Equal("github", now["github"]);
        Assert.True(!now.ContainsKey("x") && !now.ContainsKey("あ"), "英字 2 文字以上だけ");
        Assert.Equal(3, now.Count, "同じ語は 1 回");

        // 新しい版で見え方が変わった語だけを出す。新しい版が判定しなかった語は出さない
        var next = new Dictionary<string, string>(now) { ["mata"] = "mata", ["github"] = "github" };
        next.Remove("kyou");
        var changes = WordJudge.Changes(now, next);
        Assert.Equal(1, changes.Count);
        Assert.Equal(new WordChange("mata", now["mata"], "mata"), changes[0]);
        Assert.True(!changes[0].NowEnglish, "今は かな");
        Assert.True(new WordChange("api", "api", "あぴ").NowEnglish, "今は英字");
    }
}
