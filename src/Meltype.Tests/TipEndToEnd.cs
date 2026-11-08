// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai

using System.Globalization;
using System.Runtime.InteropServices;

namespace Meltype.Tests;

/// <summary>
/// 調査用: 登録した Meltype IME (TSF の DLL) を、このプロセスの入力欄で実際に動かして確かめる。
///   Meltype.Tests --tip-e2e
/// サーバー (TipServer) は別のスレッドで動かす (DLL は UI スレッドで同期的にサーバーを待つため)。
/// 打鍵は SendInput で送るので、数秒間このウィンドウが前面に出る。
/// </summary>
internal static class TipEndToEnd
{
    private static readonly Guid ClsidTextService = new("417D801B-A9BD-4C26-BD16-356A825A6998");
    private static readonly Guid ProfileGuid = new("21F643F4-72D5-4946-BF70-0A126AB52D05");
    private static readonly Guid ClsidProfiles = new("33C53A50-F456-4884-B049-85FD643ECFED");

    [ComImport, Guid("71c6e74c-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        [PreserveSig]
        int ActivateProfile(uint profileType, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
    }

    // Windows SDK の msctf.h の順序に従い、使わない COM メソッドのスロットを飛ばす。
    [ComImport, Guid("aa80e801-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfThreadMgr
    {
        void _VtblGap1_4();

        [PreserveSig]
        int GetFocus(out ITfDocumentMgr? document);
    }

    [ComImport, Guid("aa80e7f4-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfDocumentMgr
    {
        void _VtblGap1_3();

        [PreserveSig]
        int GetTop(out ITfContext? context);
    }

    [ComImport, Guid("aa80e7fd-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfContext
    {
        void _VtblGap1_8();

        [PreserveSig]
        int GetStatus(out TfStatus status);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TfStatus
    {
        public uint DynamicFlags;
        public uint StaticFlags;
    }

    private const uint TF_PROFILETYPE_INPUTPROCESSOR = 1;
    private const uint TF_IPPMF_FORPROCESS = 0x10000000;
    private const uint TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE = 0x00000004;
    private const uint TF_SS_TRANSITORY = 0x00000004;

    public static int Run()
    {
        // サーバー: 別のスレッドで
        var ready = new ManualResetEventSlim();
        Control? serverInvoker = null;
        uint serverThreadId = 0;
        var settings = new Config.Settings { Mode = Config.InputMode.Tsf, LiveConversion = false };
        var serverThread = new Thread(() =>
        {
            var invoker = new Control();
            invoker.CreateControl();
            serverInvoker = invoker;
            serverThreadId = GetCurrentThreadId();
            using var service = new Composition.CompositionService(invoker, Composition.CompositionDetector.CreateDefault(), new Composition.CompositionOptions
            {
                UserDictionary = new Composition.UserDictionary(null),
                History = new Composition.ConversionHistory(null),
                Languages = new Composition.LanguageMemory(null),
                TranslationHistory = new Composition.TranslationHistory(null),
                Engine = () => Config.ConversionEngine.System,
                CursorIntoBrackets = () => settings.CursorIntoBrackets,
            });
            using var server = new Tip.TipServer(invoker, service, () => settings);
            server.Start();
            ready.Set();
            Application.Run();
        });
        serverThread.SetApartmentState(ApartmentState.STA);
        serverThread.IsBackground = true;
        // MELTYPE_E2E_EXTERNAL_SERVER=1: このプロセスではサーバーを動かさず、常駐している Meltype.exe につなぐ
        if (Environment.GetEnvironmentVariable("MELTYPE_E2E_EXTERNAL_SERVER") != "1")
        {
            serverThread.Start();
            ready.Wait();
        }

        Application.EnableVisualStyles();
        var form = new Form { Text = "Meltype IME test", Width = 600, Height = 300, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
        var box = new RichTextBox { Dock = DockStyle.Fill, Font = new Font("Yu Gothic UI", 14) };
        form.Controls.Add(box);
        var controlEnters = 0;
        box.KeyDown += (_, e) =>
        {
            if (!e.Control || e.KeyCode != Keys.Enter) return;
            controlEnters++;
            e.SuppressKeyPress = true; // アプリの送信操作に相当。改行は入れず、届いた回数を調べる。
        };
        var results = new List<(string Name, string Expected, string Actual)>();
        var runFailed = false;
        form.Shown += async (_, _) =>
        {
            try
            {
                var manager = (ITfInputProcessorProfileMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidProfiles)!)!;
                var clsid = ClsidTextService;
                var profile = ProfileGuid;
                var hr = Environment.GetEnvironmentVariable("MELTYPE_E2E_NO_ACTIVATE") == "1" ? 0 :
                    manager.ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR, 0x0411, ref clsid, ref profile, IntPtr.Zero, TF_IPPMF_FORPROCESS | TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE);
                Console.WriteLine($"ActivateProfile: 0x{hr:X8} (入力欄のスレッド {GetCurrentThreadId()}, サーバーのスレッド {serverThreadId})");
                // 背景のプロセスからは前面に出られないので、Alt を 1 回押したことにしてから前面に出す
                Send(0x12, false);
                Send(0x12, true);
                SetForegroundWindow(form.Handle);
                form.Activate();
                box.Focus();
                Console.WriteLine($"前面: {GetForegroundWindow() == form.Handle}");
                await Task.Delay(800);

                async Task Case(string name, string keys, string expected, int? caret = null, string initial = "", int start = 0)
                {
                    box.Text = initial;
                    box.Select(start, 0);
                    await Task.Delay(200);
                    TypeKeys(keys);
                    await Task.Delay(1200);
                    results.Add((name, expected, box.Text));
                    if (caret is { } position)
                        results.Add((name + " (カーソル)", $"{position}:0", $"{box.SelectionStart}:{box.SelectionLength}"));
                }

                await Case("かな", "kyouha\n", "きょうは");
                await Case("漢字", "kyouhaiitenkidesune \n", "今日はいい天気ですね");
                await Case("英単語", "hello world", "hello world");
                await Case("混在", "kyouhagoogledekensaku \n", "今日はgoogleで検索");
                await Case("BackSpace", "kyouhaa\b\n", "きょうは");
                await Case("Esc", "kyouha\u001b", "");
                // 半角/全角 (§ で送る) で英数にすると、そのまま英字。もう一度押すと日本語に戻る
                await Case("半角/全角", "§kyouha§", "kyouha");
                await Case("日本語に戻る", "kyouha\n", "きょうは");
                var beforeControlEnter = controlEnters;
                await Case("通常文の Ctrl+Enter", "kyouha\r", "きょうは");
                results.Add(("通常文の Ctrl+Enter はアプリへ届く", "1", (controlEnters - beforeControlEnter).ToString(CultureInfo.InvariantCulture)));
                // 確定したあと ← でキャレットを動かしてから英単語を打っても、前の文字を消して確定し直さない
                await Case("動かしたあとは直さない", "i\n←want ", "want い");

                // 設定をこのサーバーで管理できるときだけ ON/OFF を比較する。外部の常駐サーバーの設定は変更しない。
                if (serverInvoker is not null)
                {
                    box.Clear();
                    await Task.Delay(200);
                    // 変換中に使われているコンテキストを読み、RichEdit の種類による差を区別する。
                    TypeKeys("[] ");
                    var transitory = IsFocusedContextTransitory();
                    TypeKeys("\u001b");
                    foreach (var enabled in new[] { false, true })
                    {
                        serverInvoker.Invoke(() => settings.CursorIntoBrackets = enabled);
                        var label = enabled ? "ON" : "OFF";
                        var continueInside = enabled && !transitory;
                        await Case("括弧 " + label, "[]\n", "「」", enabled ? 1 : 2);
                        await Case("括弧候補 " + label, "[] \n", "「」", enabled ? 1 : 2);
                        beforeControlEnter = controlEnters;
                        await Case("括弧 Ctrl+Enter " + label, "[]\r", "「」");
                        results.Add(("括弧 Ctrl+Enter はアプリへ届く " + label, "1", (controlEnters - beforeControlEnter).ToString(CultureInfo.InvariantCulture)));
                        await Case("括弧の次の入力 " + label, "[]\nka\n", enabled ? "「か」" : "「」か", enabled ? 2 : 3);
                        await Case("候補確定と次の入力 " + label, "[] k", continueInside ? "「k」" : "「」k", continueInside ? 2 : 3);
                        TypeKeys("\u001b"); // 打ちかけの k を取り消してから次のケースへ。
                        await Task.Delay(200);
                        await Case("候補確定から続けてかな入力 " + label, "[] ka\n", continueInside ? "「か」" : "「」か", continueInside ? 2 : 3);
                    }
                    serverInvoker.Invoke(() => settings.CursorIntoBrackets = true);
                    // 通常コンテキストは選択を広げずに移動する。一時コンテキストでは Shift+左キーになるので送らない。
                    box.Clear();
                    await Task.Delay(200);
                    TypeKeys("[]");
                    Send(0x10, false);
                    try
                    {
                        TypeKeys("\n");
                        await Task.Delay(1200);
                        results.Add(("Shift 押下中の括弧", "「」", box.Text));
                        results.Add(("Shift 押下中のカーソル", transitory ? "2:0" : "1:0", $"{box.SelectionStart}:{box.SelectionLength}"));
                        results.Add(("Shift の押下を保持", "True", ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0).ToString()));
                    }
                    finally
                    {
                        Send(0x10, true);
                    }
                    await Case("丸括弧", "()\n", "()", 1);
                    await Case("波括弧", "{}\n", "｛｝", 1);
                    await Case("片括弧", "[\n", "「", 1);
                    await Case("内容のある括弧", "[a]\n", "「a」", 3);
                    await Case("文書途中の括弧", "[]\n", "x「」y", 2, "xy", 1);
                    await Case("文書途中の括弧の次の入力", "[]\nka\n", "x「か」y", 3, "xy", 1);
                    await Case("文書途中の候補確定と次の入力", "[] ka\n", transitory ? "x「」かy" : "x「か」y", transitory ? 4 : 3, "xy", 1);
                    serverInvoker.Invoke(() => settings.CursorIntoBrackets = false);
                }

                if (ShotDirectory is { } directory)
                {
                    // 見た目の確認用: 打っている途中 / 変換中 (候補の一覧と意味) を画像にする
                    box.Clear();
                    box.AppendText("確定済みの文: ");
                    await Task.Delay(200);
                    TypeKeys("kyouhaiitenkidesune");
                    await Task.Delay(600);
                    Capture(form, Path.Combine(directory, "tip-typing.png"));
                    TypeKeys(" ");
                    await Task.Delay(500);
                    Capture(form, Path.Combine(directory, "tip-converting.png"));
                    await Task.Delay(2000);
                    Capture(form, Path.Combine(directory, "tip-meaning.png"));
                    TypeKeys("\u001b\u001b\u001b");
                    await Task.Delay(300);
                    box.Clear();
                    // よくある書き間違い: 「もしかして」の行
                    TypeKeys("buresureddo");
                    await Task.Delay(600);
                    Capture(form, Path.Combine(directory, "tip-suggestion.png"));
                    TypeKeys("\u001b\u001b\u001b");
                }
            }
            catch (Exception ex)
            {
                runFailed = true;
                Console.WriteLine($"失敗: {ex}");
            }
            finally
            {
                form.Close();
            }
        };
        Application.Run(form);
        serverInvoker?.BeginInvoke(Application.ExitThread);
        var failed = 0;
        foreach (var (name, expected, actual) in results)
        {
            var ok = actual == expected;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}: 期待「{expected}」 実際「{actual}」");
        }
        return !runFailed && failed == 0 && results.Count > 0 ? 0 : 1;
    }

    private static bool IsFocusedContextTransitory()
    {
        Marshal.ThrowExceptionForHR(TF_GetThreadMgr(out var threadMgr));
        if (threadMgr is null) throw new InvalidOperationException("TSF スレッドマネージャーを取得できません");
        Marshal.ThrowExceptionForHR(threadMgr.GetFocus(out var document));
        if (document is null) throw new InvalidOperationException("フォーカス中の TSF 文書を取得できません");
        Marshal.ThrowExceptionForHR(document.GetTop(out var context));
        if (context is null) throw new InvalidOperationException("入力欄の TSF コンテキストを取得できません");
        Marshal.ThrowExceptionForHR(context.GetStatus(out var status));
        var transitory = (status.StaticFlags & TF_SS_TRANSITORY) != 0;
        Console.WriteLine($"RichEdit TSF: static=0x{status.StaticFlags:X8} 一時={(transitory ? 1 : 0)}");
        return transitory;
    }

    [DllImport("msctf.dll", ExactSpelling = true)]
    private static extern int TF_GetThreadMgr(out ITfThreadMgr? threadMgr);

    public static string? ShotDirectory { get; set; }

    /// <summary>フォームとその下・右 (候補の一覧が出る所) を画面から切り取る。</summary>
    private static void Capture(Form form, string path)
    {
        var bounds = form.Bounds;
        var area = Rectangle.Intersect(new Rectangle(bounds.Left, bounds.Top, bounds.Width + 500, bounds.Height + 420), SystemInformation.VirtualScreen);
        using var bitmap = new Bitmap(area.Width, area.Height);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        bitmap.Save(path);
        Console.WriteLine($"画像: {path}");
    }

    // ' ' = Space, '\n' = Enter, '\r' = Ctrl+Enter, '\b' = BackSpace, \u001b = Esc
    private static void TypeKeys(string keys)
    {
        foreach (var c in keys)
        {
            var stroke = c switch
            {
                ' ' => 0x20,
                '\n' => 0x0D,
                '\r' => 0x020D,
                '\b' => 0x08,
                '\u001b' => 0x1B,
                '§' => 0xF3,
                '←' => 0x25,
                >= 'a' and <= 'z' => c - 'a' + 'A',
                _ => (int)VkKeyScan(c),
            };
            if (stroke < 0) throw new ArgumentException($"送れない文字: {c}");
            var vk = (ushort)(stroke & 0xFF);
            var shift = (stroke & 0x0100) != 0;
            var control = (stroke & 0x0200) != 0;
            var alt = (stroke & 0x0400) != 0;
            if (shift) Send(0x10, false);
            if (control) Send(0x11, false);
            if (alt) Send(0x12, false);
            Send(vk, false);
            Send(vk, true);
            if (alt) Send(0x12, true);
            if (control) Send(0x11, true);
            if (shift) Send(0x10, true);
            Application.DoEvents();
            Thread.Sleep(30);
        }
    }

    private static void Send(ushort vk, bool up)
    {
        var input = new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0) | (vk is >= 0x21 and <= 0x28 ? Native.KEYEVENTF_EXTENDEDKEY : 0) } },
        };
        Native.SendAll([input], "テストの打鍵");
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
