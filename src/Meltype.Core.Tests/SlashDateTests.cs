// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;
using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>月/日 を変換したときの日付の候補 (issue #935)。</summary>
internal static class SlashDateTests
{
    [Test]
    public static void Issue935_ConvertKeyOffersDates()
    {
        var k = new Keyboard(now: () => new DateTime(2026, 10, 1));
        k.Type("10/20");
        k.Press(VirtualKeys.Convert);
        var candidates = k.Host.View!.Candidates;
        Assert.Equal("10/20", candidates[0], "打ったままを最初に (分数・割り算かもしれない)");
        foreach (var expected in new[] { "10月20日", "10/20(火)", "10月20日(火)" })
            Assert.True(candidates.Contains(expected), expected + ": " + string.Join(",", candidates));
    }

    [Test]
    public static void Issue935_SpaceAndCalculatorUnchanged()
    {
        // Space は今までどおり確定して空白
        var space = new Keyboard(now: () => new DateTime(2026, 10, 1));
        space.Type("1/2 ");
        Assert.Equal("1/2 ", space.Host.Document, "Space では日付にしない");
        // 計算式の結果の候補は、日付の候補より先に残る
        var calc = new Keyboard(now: () => new DateTime(2026, 10, 1)) { CalculatorCandidates = true };
        calc.Type("1/2");
        calc.Press(VirtualKeys.Convert);
        var candidates = calc.Host.View!.Candidates.ToList();
        Assert.True(candidates.IndexOf("0.5") is > 0 && candidates.IndexOf("0.5") < candidates.IndexOf("1月2日"), string.Join(",", candidates));
        // 日付にならない数字 (13/40) には日付の候補を出さない
        var other = new Keyboard();
        other.Type("13/40");
        other.Press(VirtualKeys.Convert);
        Assert.True(other.Host.View?.Candidates.Any(c => c.Contains('月')) != true, "日付でなければ出さない");
    }
}
