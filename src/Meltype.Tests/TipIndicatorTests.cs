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
    public static void Indicator_TsfPositionSupportsNegativeCoordinatesAndOldDlls()
    {
        using var negative = JsonDocument.Parse("{\"x\":-900,\"y\":-120}");
        Assert.Equal<Point?>(new Point(-900, -120), Tip.TipServer.IndicatorAnchor(negative.RootElement), "左・上のモニターの座標も保持する");
        foreach (var json in new[] { "{}", "{\"x\":100}", "{\"x\":100,\"y\":null}", "{\"x\":\"100\",\"y\":20}", "{\"x\":2147483648,\"y\":20}" })
        {
            using var document = JsonDocument.Parse(json);
            Assert.Equal<Point?>(null, Tip.TipServer.IndicatorAnchor(document.RootElement), "位置なし・不正な座標では従来の位置取得へ戻る");
        }
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
    public static void Indicator_DoesNotCreateSessionsAndRejectsInvalidNotifications()
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
        var sessions = new Dictionary<string, MeltypeSession>();
        var handle = typeof(Tip.TipServer).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Instance)!;
        void Send(string json)
        {
            handle.Invoke(server, [json, sessions]);
            Application.DoEvents();
        }

        // 実際の表示は設定で止め、UI への依頼だけ数える。IME OFF でもセッションは作らない。
        Send("{\"op\":\"indicator\",\"process\":\"harness\",\"thread\":1,\"japanese\":true}");
        Send("{\"op\":\"indicator\",\"process\":\"harness\",\"thread\":1,\"japanese\":false}");
        Assert.Equal(2, displayRequests, "ON/OFF の両方を表示へ渡す");
        Assert.Equal(0, sessions.Count, "表示のために入力状態を作らない");

        Send("{\"op\":\"indicator\",\"thread\":0,\"japanese\":true}");
        Send("{\"op\":\"indicator\",\"thread\":-1,\"japanese\":true}");
        Send("{\"op\":\"indicator\",\"thread\":\"1\",\"japanese\":true}");
        Send("{\"op\":\"indicator\",\"thread\":null,\"japanese\":true}");
        Send("{\"op\":\"indicator\",\"thread\":1,\"japanese\":\"false\"}");
        Send("{\"op\":\"indicator\",\"thread\":1}");
        Assert.Equal(2, displayRequests, "不完全な通知は表示しない");

        settings.Enabled = false;
        Send("{\"op\":\"indicator\",\"thread\":1,\"japanese\":true}");
        settings.Enabled = true;
        settings.Mode = InputMode.Keyboard;
        Send("{\"op\":\"indicator\",\"thread\":1,\"japanese\":true}");
        Assert.Equal(2, displayRequests, "一時停止中とキーボード方式では TSF の通知を表示しない");
        Assert.Equal(0, sessions.Count);
    }
}
