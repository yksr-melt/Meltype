// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 cet-t

using System.Reflection;
using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

internal static class TipIndicatorTests
{
    [Test]
    public static void Indicator_TsfPositionSupportsNegativeCoordinates()
    {
        static string Request(string position) => $"{{\"thread\":1{position},\"event\":{{\"version\":{Ime.ImeHost.Version},\"type\":\"ModeToggleRequested\"}}}}";
        Assert.Equal<Point?>(new Point(-900, -120), Tip.TipRequest.Parse(Request(",\"x\":-900,\"y\":-120"))?.Anchor, "左・上のモニターの座標も保持する");
        foreach (var position in new[] { "", ",\"x\":100", ",\"x\":100,\"y\":null", ",\"x\":\"100\",\"y\":20", ",\"x\":2147483648,\"y\":20" })
        {
            var request = Tip.TipRequest.Parse(Request(position));
            Assert.True(request is not null && request.Anchor is null, "位置なし・不正な座標では、常駐側で位置を探す: " + position);
        }
        Assert.True(Tip.TipRequest.Parse("{\"op\":\"indicator\",\"thread\":1}") is null, "古い DLL の要求は読まない");
    }

    [Test]
    public static void Indicator_UsesTsfPositionAndExpiresWithoutTakingFocus()
    {
        using var invoker = new Control();
        invoker.CreateControl();
        using var runtime = TestRuntime.Create();
        using var composition = new Composition.CompositionService(invoker, runtime, () => new Settings());
        var indicator = (ModeIndicatorWindow)typeof(CompositionService).GetField("_indicator", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(composition)!;
        var area = Screen.PrimaryScreen!.WorkingArea;
        var anchor = new Point(area.Left + 40, area.Top + 40);
        var foreground = Native.GetForegroundWindow();
        composition.ShowMode(true, 1200, inputAnchor: anchor);
        Application.DoEvents();
        Assert.True(indicator.Visible, "通知の受付だけでなく表示まで確認する");
        Assert.Equal(new Point(anchor.X + 2, anchor.Y + 2), indicator.Location, "入力欄の左上ではなく TSF の位置を使う");
        Assert.Equal(foreground, Native.GetForegroundWindow(), "入力先のフォーカスを奪わない");

        // ON/OFF の繰り返しでも、既に表示中の同じウィンドウを更新できる。
        composition.ShowMode(false, 1200, inputAnchor: new Point(anchor.X + 30, anchor.Y + 20));
        Application.DoEvents();
        Assert.True(indicator.Visible, "2 回目も表示する");
        Assert.Equal(new Point(anchor.X + 32, anchor.Y + 22), indicator.Location);
        composition.ShowMode(true, 1200, inputAnchor: anchor);
        Application.DoEvents();
        Assert.True(indicator.Visible, "3 回目も表示する");

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (indicator.Visible && elapsed.ElapsedMilliseconds < 1800)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.True(!indicator.Visible, "約 1.2 秒で消える");
        Assert.True(elapsed.ElapsedMilliseconds >= 1000, "表示がすぐ消えない");
    }

    [Test]
    public static void Indicator_IsShownWhenTheCoreSaysSo()
    {
        // 出すかは本体が state.modeIndicatorMs で決める (設定「入力モードをカーソルの近くに表示」)。TipServer は、入力スレッドが分かるときに出すだけ。
        // 表示は、前面のウィンドウが入力スレッドのものなときだけ出るので、前面のスレッドから来た要求にする
        var settings = new Settings { Mode = InputMode.Tsf, ModeIndicator = ModeIndicatorTiming.Never };
        using var invoker = new Control();
        invoker.CreateControl();
        using var runtime = TestRuntime.Create();
        using var composition = new Composition.CompositionService(invoker, runtime, () => new Settings());
        var indicator = (ModeIndicatorWindow)typeof(CompositionService).GetField("_indicator", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(composition)!;
        using var server = new Tip.TipServer(invoker, composition, environment => new Ime.ImeHost(environment, () => settings));
        var host = server.Connect("harness", 0);
        var foreground = WindowsIme.ImeTarget.FromForeground()?.ThreadId ?? 0;
        Assert.True(foreground != 0, "前面の入力スレッドがある");
        bool Shown(string type, uint? thread = null, string fields = "")
        {
            indicator.Hide();
            var request = Tip.TipRequest.Parse($"{{\"thread\":{thread ?? foreground},\"event\":{{\"version\":{Ime.ImeHost.Version},\"type\":\"{type}\",\"instance\":\"t1\",\"imeOpen\":true{fields}}}}}")!;
            server.Handle(host, request);
            Application.DoEvents();
            return indicator.Visible;
        }

        Assert.True(!Shown("ModeToggleRequested"), "出さない設定");
        settings.ModeIndicator = ModeIndicatorTiming.OnSwitch;
        Assert.True(Shown("ModeToggleRequested") && Shown("ModeToggleRequested"), "明示的な切り替えのたびに出す");
        Assert.True(!Shown("KeyPressed", fields: ",\"key\":{\"vk\":65,\"ch\":97}") && !Shown("StateRequested"), "切り替えでない出来事では出さない");
        Assert.True(!Shown("ModeToggleRequested", thread: 0), "入力スレッドが分からなければ出さない");
        settings.Mode = InputMode.Keyboard;
        Assert.True(!Shown("ModeToggleRequested"), "動作モードが Meltype キーボードなら、Meltype IME の切り替えでは出さない");
    }
}
