// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
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

    [Test]
    public static void Issue935_SameFormatsAsTypedDate()
    {
        // 10/20 と 10がつ20にち で、同じ日付の書き方を出す (片方だけに足して食い違わないように)。
        // 10 月に打つ 1/2 は、今日にいちばん近い来年の 1 月 2 日 (土)
        var slash = new Keyboard(now: () => new DateTime(2026, 10, 1));
        slash.Type("1/2");
        slash.Press(VirtualKeys.Convert);
        var typed = new Keyboard(now: () => new DateTime(2026, 10, 1));
        typed.Type("1gatu2niti ");
        foreach (var format in new[] { "1/2", "01/02", "1月2日", "1/2(土)", "1月2日(土)", "2027/01/02", "2027/01/02(土)", "2027年1月2日", "１／２" })
        {
            Assert.True(slash.Host.View!.Candidates.Contains(format), "1/2: " + format + ": " + string.Join(",", slash.Host.View.Candidates));
            if (format != "１／２") Assert.True(typed.Host.View!.Candidates.Contains(format), "1がつ2にち: " + format + ": " + string.Join(",", typed.Host.View.Candidates));
        }
    }

    [Test]
    public static void Issue935_YearPrefersFuture()
    {
        // 先の日付を優先する。過ぎてから 30 日以内だけ今年 (6 月 15 日に打つ 12/20 は今年、6/1 は今年、1/5 は来年)
        foreach (var (now, typed, expected) in new[]
        {
            (new DateTime(2026, 2, 10), "12/25", "2026/12/25"),
            (new DateTime(2026, 6, 15), "12/20", "2026/12/20"),
            (new DateTime(2026, 6, 15), "5/1", "2027/05/01"),
            (new DateTime(2026, 3, 1), "2/29", "2028/02/29"),
            (new DateTime(2026, 1, 5), "12/28", "2025/12/28"),
            (new DateTime(2026, 1, 5), "12/1", "2026/12/01"),
            (new DateTime(2026, 6, 15), "6/1", "2026/06/01"),
            (new DateTime(2026, 12, 20), "1/5", "2027/01/05"),
        })
        {
            var k = new Keyboard(now: () => now);
            k.Type(typed);
            k.Press(VirtualKeys.Convert);
            Assert.True(k.Host.View!.Candidates.Contains(expected), $"{now:M/d} に {typed}: " + string.Join(",", k.Host.View.Candidates));
        }
    }

    [Test]
    public static void Issue935_ChosenDateIsNotLearned()
    {
        // 日付の候補 (2026年1月2日 など、書き方によらず) を選んでも学習しない (来年に打つと古い年が最初に出るので)
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history, now: () => new DateTime(2026, 1, 1));
        k.Type("1gatu2niti ");
        var view = k.Host.View!;
        var index = view.Candidates.ToList().IndexOf("2026年1月2日");
        Assert.True(index > 0, string.Join(",", view.Candidates));
        for (var i = 0; i < index; i++) k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Return);
        Assert.Equal("2026年1月2日", k.Host.Document, "選んだ候補で確定する");
        Assert.Equal(null, history.Get("1がつ2にち"), "学習しない");
    }
}
