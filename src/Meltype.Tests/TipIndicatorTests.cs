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
    public static void Indicator_SettingsAreIndependentForKeyboardAndTsf()
    {
        var settings = new Settings();
        foreach (var keyboard in new[] { false, true })
        {
            foreach (var tsf in new[] { false, true })
            {
                settings.ModeIndicator = keyboard ? ModeIndicatorTiming.OnSwitch : ModeIndicatorTiming.Never;
                settings.ShowTsfModeIndicator = tsf;
                settings.Mode = InputMode.Keyboard;
                Assert.Equal(keyboard, UI.TrayApplicationContext.ShouldShowModeIndicator(settings), "キーボード方式は既存設定だけを見る");
                settings.Mode = InputMode.Tsf;

            }
        }
        settings.Enabled = false;
        Assert.True(!UI.TrayApplicationContext.ShouldShowModeIndicator(settings), "一時停止中は表示しない");
        settings.Mode = InputMode.Keyboard;
        Assert.True(!UI.TrayApplicationContext.ShouldShowModeIndicator(settings), "キーボード方式も一時停止中は表示しない");
    }

    [Test]
    public static void Indicator_TsfPositionSupportsNegativeCoordinates()
    {
        static string Request(string position) => $"{{\"thread\":1{position},\"event\":{{\"v\":{Ime.ImeHost.Version},\"type\":\"toggle\"}}}}";
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
        using var composition = new CompositionService(invoker, CompositionDetector.CreateDefault(), new CompositionOptions
        {
            ModeIndicator = () => true,
            UserDictionary = new UserDictionary(null),
            History = new ConversionHistory(null),
            Languages = new LanguageMemory(null),
            TranslationHistory = new TranslationHistory(null),
            Engine = () => ConversionEngine.System,
        });
        var indicator = (ModeIndicatorWindow)typeof(CompositionService).GetField("_indicator", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(composition)!;
        var area = Screen.PrimaryScreen!.WorkingArea;
        var anchor = new Point(area.Left + 40, area.Top + 40);
        var foreground = Native.GetForegroundWindow();
        composition.ShowMode(true, inputAnchor: anchor);
        Application.DoEvents();
        Assert.True(indicator.Visible, "通知の受付だけでなく表示まで確認する");
        Assert.Equal(new Point(anchor.X + 2, anchor.Y + 2), indicator.Location, "入力欄の左上ではなく TSF の位置を使う");
        Assert.Equal(foreground, Native.GetForegroundWindow(), "入力先のフォーカスを奪わない");

        // ON/OFF の繰り返しでも、既に表示中の同じウィンドウを更新できる。
        composition.ShowMode(false, inputAnchor: new Point(anchor.X + 30, anchor.Y + 20));
        Application.DoEvents();
        Assert.True(indicator.Visible, "2 回目も表示する");
        Assert.Equal(new Point(anchor.X + 32, anchor.Y + 22), indicator.Location);
        composition.ShowMode(true, inputAnchor: anchor);
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
    public static void Indicator_IsShownOnlyForExplicitChangesWithTheSetting()
    {
        var settings = new Settings { Mode = InputMode.Tsf };
        var displayRequests = 0;
        using var invoker = new Control();
        invoker.CreateControl();
        using var composition = new CompositionService(invoker, CompositionDetector.CreateDefault(), new CompositionOptions
        {
            ModeIndicator = () => { displayRequests++; return false; },
            UserDictionary = new UserDictionary(null),
            History = new ConversionHistory(null),
            Languages = new LanguageMemory(null),
            TranslationHistory = new TranslationHistory(null),
            Engine = () => ConversionEngine.System,
        });
        using var server = new Tip.TipServer(invoker, composition, () => settings);
        var host = server.Connect("harness", 0);
        void Send(string type, uint thread = 1, string fields = "")
        {
            var request = Tip.TipRequest.Parse($"{{\"thread\":{thread},\"event\":{{\"v\":{Ime.ImeHost.Version},\"type\":\"{type}\",\"sid\":\"t1\",\"open\":true{fields}}}}}")!;
            server.Handle(host, request);
            Application.DoEvents();
        }

        // 実際の表示は設定で止め、UI への依頼だけ数える
        Send("toggle");
        Assert.Equal(0, displayRequests, "設定「Meltype IME でも入力モードを表示」が OFF なら表示しない");
        settings.ShowTsfModeIndicator = true;
        Send("toggle");
        Send("toggle");
        Assert.Equal(2, displayRequests, "明示的な切り替えのたびに表示する");
        Send("key", fields: ",\"key\":{\"vk\":65,\"ch\":97}");
        Send("sync");
        Assert.Equal(2, displayRequests, "切り替えでない出来事では表示しない");
        Send("toggle", thread: 0);
        Assert.Equal(2, displayRequests, "入力スレッドが分からなければ表示しない");
        settings.Mode = InputMode.Keyboard;
        Send("toggle");
        Assert.Equal(2, displayRequests, "キーボード方式では Meltype IME の切り替えを表示しない");
    }
}
