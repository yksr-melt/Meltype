// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>自分で直した誤判定を、1 件の報告にする (issue #284)。</summary>
internal static class CorrectionTests
{
    [Test]
    public static void FunctionKeyCorrection_IsRecorded()
    {
        // sushi と打つと すし → F10 で sushi にして確定: 打ったもの sushi / 出たもの すし / 期待 sushi
        var k = new CompositionTests.Keyboard();
        var log = new CorrectionLog();
        k.Controller.Corrected += log.Add;
        k.Host.PrecedingText = "これは";
        k.Type("sushi");
        Assert.Equal("すし", k.Showing);
        k.Press(VirtualKeys.F10);
        k.Press(VirtualKeys.Return);
        var correction = log.Recent().Single();
        Assert.Equal("sushi", correction.Typed);
        Assert.Equal("すし", correction.Shown);
        Assert.Equal("sushi", correction.Corrected);
        Assert.Equal("Enter (確定)", correction.LastKey);
        Assert.Equal("これは", correction.Before);
        Assert.Equal("英語のつもりがかなになった (みいてぃんg)", correction.Kind);
    }

    [Test]
    public static void AutomaticCommit_IsNotRecorded()
    {
        // 直さずに確定したもの・漢字を選び直しただけのものは記録しない
        var k = new CompositionTests.Keyboard();
        var log = new CorrectionLog();
        k.Controller.Corrected += log.Add;
        k.Type("kyouha\n");
        k.Type("hashiwo ");
        k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Return);
        Assert.Equal(0, log.Count, string.Join(" / ", log.Recent().Select(c => c.ToReportText())));
    }

    [Test]
    public static void ReportUrl_OpensTheBugTemplateWithEnvironment()
    {
        // ひな形を選ぶ画面 (issues/new/choose) では、選んだあとに値が引き継がれない (issue #877)
        var url = Config.ProjectInfo.ReportUrl("Windows 11", "1.2.2", "Meltype: 1.2.2\nOS: Windows 11");
        if (Config.ProjectInfo.HasReportForm) return;
        Assert.True(!url.Contains("/issues/new/choose"), url);
        foreach (var part in new[] { "/issues/new?template=1-bug.yml", $"os={Uri.EscapeDataString("Windows 11")}", "version=1.2.2", $"environment={Uri.EscapeDataString("Meltype: 1.2.2\nOS: Windows 11")}" })
            Assert.True(url.Contains(part), part + ": " + url);
    }

    [Test]
    public static void ReportUrl_FillsTheMisdetectionTemplate()
    {
        var correction = new Correction("mata", "また", "mata", "Enter (確定)", "hello ", DateTime.Now);
        var url = Config.ProjectInfo.MisdetectionReportUrl(correction, "Windows", "1.2.0", "env");
        foreach (var part in new[] { "template=2-misdetection.yml", "typed=mata", $"actual={Uri.EscapeDataString("また")}", "expected=mata", $"context={Uri.EscapeDataString("hello ")}" })
            Assert.True(url.Contains(part), part + ": " + url);
        Assert.True(correction.ToReportText().Contains("打ったもの (キーをそのまま): mata"), correction.ToReportText());
        // 直したときに Zenzai を使っていたかも入れる。分からなければ欄を空のままにする (issue #1197)
        Assert.True(!url.Contains("zenzai="), url);
        foreach (var (on, text) in new[] { (true, "使っている"), (false, "使っていない") })
        {
            var withZenzai = Config.ProjectInfo.MisdetectionReportUrl(correction with { Zenzai = on }, "Windows", "1.2.0", "env");
            Assert.True(withZenzai.Contains($"zenzai={Uri.EscapeDataString(text)}"), withZenzai);
        }

        // 記録は新しい順・最大 20 件
        var log = new CorrectionLog();
        for (var i = 0; i < 25; i++) log.Add(correction with { Typed = "w" + i });
        Assert.Equal(20, log.Count);
        Assert.Equal("w24", log.Recent()[0].Typed);
    }
}
