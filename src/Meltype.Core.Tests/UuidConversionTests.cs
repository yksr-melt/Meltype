// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

using Meltype.Input;
using Meltype.Learning;
using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

internal static class UuidConversionTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 11, 27, 12, 55, 0, DateTimeKind.Utc);

    [Test]
    public static void Uuid_BareKeywordAppendsUuidCandidatesAndKeepsNormalCandidate()
    {
        var k = new Keyboard(now: () => FixedUtcNow);
        k.Type("uuid");
        k.Press(VirtualKeys.Convert);
        var candidates = k.Host.View!.Candidates;
        Assert.Equal("uuid", candidates[0], "裸の uuid は通常の英字候補を先に残す");
        Assert.True(candidates.Any(c => Guid.TryParseExact(c, "D", out var g) && g.Version == 4), "UUID v4 が含まれる");
        Assert.True(candidates.Any(c => Guid.TryParseExact(c, "D", out var g) && g.Version == 7), "UUID v7 が含まれる");

        var kKana = new Keyboard(now: () => FixedUtcNow);
        kKana.Type("yu-yu-aidexi- ");
        Assert.True(kKana.Host.View!.Candidates.Any(c => Guid.TryParseExact(c, "D", out var g) && g.Version == 4), "ゆーゆーあいでぃー でも UUID v4 が出る");
    }

    [Test]
    public static void Uuid_FunctionGeneratesUuidsAndIsNotLearned()
    {
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history, now: () => FixedUtcNow);
        k.Type("uuid() ");
        var view = k.Host.View!;
        Assert.True(Guid.TryParseExact(view.Candidates[0], "D", out var v4) && v4.Version == 4, "第 1 候補は UUID v4");
        Assert.True(Guid.TryParseExact(view.Candidates[1], "D", out var v7) && v7.Version == 7, "第 2 候補は UUID v7");
        Assert.Equal(view.Candidates[0].ToUpperInvariant(), view.Candidates[2], "第 3 候補は大文字");
        Assert.Equal(view.Candidates[0].Replace("-", "", StringComparison.Ordinal), view.Candidates[3], "第 4 候補はハイフンなし");

        // Space で候補を切り替えても生成済みの値がブレない
        k.Press(VirtualKeys.Space);
        Assert.Equal(v7.ToString("D"), k.Host.View!.Text);
        k.Press(VirtualKeys.Return);
        Assert.Equal(v7.ToString("D"), k.Host.Document);
        Assert.True(history.Get("uuid()") is null, "ランダム生成の UUID は学習しない");
    }

    [Test]
    public static void Uuid_SupportsVersionArgumentsAndTabPrediction()
    {
        var k7 = new Keyboard(now: () => FixedUtcNow);
        k7.Type("UUID(v7) ");
        Assert.True(Guid.TryParseExact(k7.Host.View!.Text, "D", out var g7) && g7.Version == 7, "uuid(v7) は v7 を先頭にする");

        var kn = new Keyboard(now: () => FixedUtcNow);
        kn.Type("guid(n) ");
        Assert.True(Guid.TryParseExact(kn.Host.View!.Text, "N", out _), "guid(n) はハイフンなしを先頭にする");

        var kTab = new Keyboard(predictor: new Predictor(null, () => [], null), now: () => FixedUtcNow);
        kTab.Type("uuid()");
        kTab.Press(VirtualKeys.Tab);
        var predicted = kTab.Host.View!.Text;
        Assert.True(Guid.TryParseExact(predicted, "D", out var g4) && g4.Version == 4, "Tab 予測でも UUID を選べる");
        kTab.Press(VirtualKeys.Return);
        Assert.Equal(predicted, kTab.Host.Document);
    }
}
