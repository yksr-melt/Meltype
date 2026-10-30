// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Input;
using Meltype.Composition;
using Meltype.IME;
using System.Diagnostics;

namespace Meltype.Tests;

/// <summary>Windows 版だけのテスト。</summary>
internal static class WindowsTests
{
    [Test]
    public static void SpellChecker_DisposeStopsTheWorkerAndClosesItsWaitHandle()
    {
        using var checker = new Detection.WindowsSpellChecker();
        _ = checker.IsAvailable;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var worker = (Thread?)typeof(Detection.WindowsSpellChecker).GetField("_comThread", flags)!.GetValue(checker);
        var wake = (AutoResetEvent)typeof(Detection.WindowsSpellChecker).GetField("_wake", flags)!.GetValue(checker)!;
        checker.Dispose();
        checker.Dispose();
        Assert.True(worker is null || !worker.IsAlive, "問い合わせのスレッドが終了している");
        Assert.True(wake.SafeWaitHandle.IsClosed, "待機ハンドルを閉じる");
        var disposed = false;
        try { checker.IsWord("test"); }
        catch (ObjectDisposedException) { disposed = true; }
        Assert.True(disposed, "破棄した後に問い合わせを再開しない");
    }

    [Test]
    public static void Main_RunsOnStaThread()
    {
        // クリップボード・UI は STA でないと動かない。Main の前にメソッドを足して、[STAThread] がそちらに付いていた (v1.2.0 の自己診断で失敗)
        var main = typeof(global::Meltype.Program).GetMethod("Main", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.True(main.IsDefined(typeof(STAThreadAttribute), false), "Main に [STAThread]");
    }

    [Test]
    public static void Games_AreDetectedFromInstallFolder()
    {
        Assert.True(ForegroundTracker.LooksLikeGame(@"D:\SteamLibrary\steamapps\common\Apex Legends\r5apex.exe"), "Steam のゲーム");
        Assert.True(ForegroundTracker.LooksLikeGame(@"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe"), "Epic のゲーム");
        Assert.True(ForegroundTracker.LooksLikeGame(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Rainbow Six Siege\RainbowSix.exe"), "Ubisoft のゲーム");
        Assert.True(ForegroundTracker.LooksLikeGame(@"C:\Program Files (x86)\SEGA\PHANTASYSTARONLINE2_JP\pso2_bin\pso2.exe"), "PSO2 NGS (専用のランチャー)");
        Assert.True(ForegroundTracker.LooksLikeGame(@"C:\Users\a\AppData\Roaming\.minecraft\runtime\java-runtime-delta\windows-x64\java-runtime-delta\bin\javaw.exe"), "Minecraft (Java 版) #1009");
        Assert.True(ForegroundTracker.LooksLikeGame(@"C:\Users\a\AppData\Local\Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime\java-runtime-delta\windows-x64\java-runtime-delta\bin\javaw.exe"), "Minecraft (ストアのランチャー)");
        Assert.True(ForegroundTracker.LooksLikeGame(@"C:\Program Files (x86)\Minecraft Launcher\runtime\jre-x64\bin\javaw.exe"), "Minecraft (旧ランチャー: \\Launcher\\ ではないので除かない)");
        Assert.True(!ForegroundTracker.LooksLikeGame(@"C:\Program Files\Java\jdk-21\bin\javaw.exe"), "ふつうの Java のアプリ");
        Assert.True(!ForegroundTracker.LooksLikeGame(@"C:\Program Files (x86)\Steam\steam.exe"), "Steam のクライアントは除く");
        Assert.True(!ForegroundTracker.LooksLikeGame(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"), "ランチャーは除く");
        Assert.True(!ForegroundTracker.LooksLikeGame(@"C:\Program Files\Microsoft VS Code\Code.exe"), "ふつうのアプリ");

        var settings = new Settings();
        Assert.True(settings.IsGame("r5apex.exe", looksLikeGame: true), "ゲームのフォルダーなら止める");
        settings.StopInGames = false;
        Assert.True(!settings.IsGame("r5apex.exe", looksLikeGame: true), "設定で OFF にしたら止めない");
        settings.AppRules.Add(new AppRule { Process = "minecraft.exe", Enabled = true, Profile = AppProfile.Game });
        Assert.True(settings.IsGame("minecraft.exe", looksLikeGame: false), "アプリ別設定で「ゲーム」にしたら止める");
    }

    [Test]
    public static void Foreground_RechecksFullscreenOnTheSameWindow()
    {
        // 枠ありで起動して、同じウィンドウのまま全画面に切り替えるゲーム。表示はしない
        var screen = Screen.PrimaryScreen!.Bounds;
        using var form = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(screen.X, screen.Y, 200, 100) };
        var tracker = new ForegroundTracker();
        Assert.True(!tracker.CurrentFor(form.Handle, 1000).IsFullscreen, "枠あり");
        form.Bounds = screen;
        Assert.True(!tracker.CurrentFor(form.Handle, 1100).IsFullscreen, "すぐには確かめ直さない");
        Assert.True(tracker.CurrentFor(form.Handle, 2000).IsFullscreen, "少し経つと全画面と分かる");
    }

    [Test]
    public static void AllowTyping_FollowsRecreatedHandles()
    {
        // ハンドルを作り直したら (ShowInTaskbar などを変えたとき) 新しいハンドルに替え、閉じたら外す。外すときに新しいハンドルを作らない。表示はしない
        var before = (ForegroundTracker.TypingAllowedWindows.Count, ForegroundTracker.DirectInputControls.Count);
        var form = new RecreatableForm();
        var name = new RecreatableTextBox();
        form.Controls.Add(name);
        ForegroundTracker.AllowTyping(form, name);
        var (oldForm, oldName) = (form.Handle, name.Handle);
        Assert.True(ForegroundTracker.TypingAllowedWindows.ContainsKey(oldForm), "窓を登録");
        Assert.True(ForegroundTracker.DirectInputControls.ContainsKey(oldName), "欄を登録");
        form.Recreate();
        name.Recreate();
        Assert.True(form.Handle != oldForm && name.Handle != oldName, "作り直すとハンドルが変わる");
        Assert.True(!ForegroundTracker.TypingAllowedWindows.ContainsKey(oldForm) && ForegroundTracker.TypingAllowedWindows.ContainsKey(form.Handle), "窓は新しいハンドルだけ");
        Assert.True(!ForegroundTracker.DirectInputControls.ContainsKey(oldName) && ForegroundTracker.DirectInputControls.ContainsKey(name.Handle), "欄は新しいハンドルだけ");
        form.Dispose();
        Assert.True((ForegroundTracker.TypingAllowedWindows.Count, ForegroundTracker.DirectInputControls.Count) == before, "閉じたら何も残らない");
    }

    private sealed class RecreatableForm : Form
    {
        public void Recreate() => RecreateHandle();
    }

    private sealed class RecreatableTextBox : TextBox
    {
        public void Recreate() => RecreateHandle();
    }

    [Test]
    public static void MsIme_LongReading_IsSplitBeforeTheEngineLimit()
    {
        // #221: Microsoft IME の変換エンジンは 100 文字を超える読みを受け付けない (E_LARGEINPUT)。区切って渡す
        var sentence = "きょうはいいてんきですね、あしたもはれるといいですね。";
        var text = string.Concat(Enumerable.Repeat(sentence, 6)); // 162 文字
        var parts = Composition.MsImeKanjiConverter.SplitForEngine(text, 90);
        Assert.Equal(text, string.Concat(parts));
        Assert.True(parts.All(p => p.Length <= 90), string.Join(" / ", parts.Select(p => p.Length)));
        Assert.True(parts[0].EndsWith('。'), "文の切れ目で区切る: " + parts[0]);
        // 区切りの文字が無ければ上限で切る
        Assert.Equal("100,50", string.Join(",", Composition.MsImeKanjiConverter.SplitForEngine(new string('ー', 150), 100).Select(p => p.Length)));

        using var ime = new Composition.MsImeKanjiConverter();
        if (ime.Convert("てすと") is null) return; // Microsoft IME が無い環境
        var clauses = ime.ConvertClauses(text, "わたしは");
        Assert.True(clauses is { Count: > 0 }, "長い読みも文節に区切って変換できる");
        Assert.Equal(text, string.Concat(clauses!.Select(c => c.Reading)));
        Assert.True(string.Concat(clauses!.Select(c => c.Text)).Contains("天気"), string.Concat(clauses!.Select(c => c.Text)));
        Assert.True(ime.Convert(text)?.Contains("天気") == true, "全体の変換も");
    }

    [Test]
    public static void Mozc_DoesNotRestartAfterDispose()
    {
        // 確定のあと裏で覚えさせる処理・起動直後の WarmUp が、終了 (Dispose) のあとで動くことがある。ヘルパーを起動し直さない
        var helper = Environment.GetEnvironmentVariable("MELTYPE_MOZC") is { Length: > 0 } path ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "native", "mozc", "bin", "meltype_mozc_helper.exe"));
        if (!File.Exists(helper)) return; // Mozc の変換ヘルパーが無い環境
        var profile = Path.Combine(Path.GetTempPath(), $"meltype-mozc-dispose-{Environment.ProcessId}");
        static int Helpers() => System.Diagnostics.Process.GetProcessesByName("meltype_mozc_helper").Length;
        var mozc = new MozcConverter(helper, profile);
        try
        {
            if (mozc.Convert("てすと") is null) return; // 起動できない環境
            mozc.Dispose();
            var before = Helpers();
            mozc.Learn("", [new ConversionClause("てすと", "テスト")]);
            Assert.Equal(before, Helpers(), "Dispose のあとで覚えさせても、ヘルパーを起動しない");
            Assert.True(mozc.Convert("てすと") is null, "Dispose のあとは変換しない");
            Assert.Equal(before, Helpers(), "Dispose のあとで変換しても、ヘルパーを起動しない");
        }
        finally
        {
            mozc.Dispose();
            try { Directory.Delete(profile, recursive: true); } catch { }
        }
    }

    [Test]
    public static void Line_RechecksStaleFocusOnKey()
    {
        // 起動直後の LINE は、入力欄の通知が来ず「入力欄ではない」のままキーが素通しになる。打った時点で調べ直す。
        Assert.True(MeltypeEngine.RechecksStaleFocus("LINE.exe"), "LINE は打鍵時に入力欄を調べ直す");
        Assert.True(MeltypeEngine.RechecksStaleFocus("line.EXE"), "大小文字は無視");
        Assert.True(MeltypeEngine.RechecksStaleFocus("firefox.exe"), "ブラウザーは今までどおり");
        Assert.True(!MeltypeEngine.RechecksStaleFocus("notepad.exe"), "関係ないアプリは調べ直さない");
        Assert.True(!MeltypeEngine.RechecksStaleFocus(null), "プロセス名が無いときは調べ直さない");
    }

    [Test]
    public static void ApplySettings_ActivatesTsfOnlyWhenSwitchingToIt()
    {
        // トレイでも設定画面でも、動作モードを Meltype IME に変えたら入力方式も Meltype IME にする。
        Assert.True(MeltypeEngine.SwitchesToTsf(InputMode.Keyboard, InputMode.Tsf), "Meltype キーボードから");
        Assert.True(!MeltypeEngine.SwitchesToTsf(InputMode.Tsf, InputMode.Tsf), "ほかの設定だけ変えたときは切り替えない");
        Assert.True(!MeltypeEngine.SwitchesToTsf(InputMode.Keyboard, InputMode.Keyboard), "Meltype IME を使えずに元のモードに戻したときは切り替えない");
        Assert.True(!MeltypeEngine.SwitchesToTsf(InputMode.Tsf, InputMode.Keyboard), "Meltype IME から外したときは切り替えない");
    }

    [Test]
    public static void AltTap_ReleaseIsSentAfterMaskKey()
    {
        // 右 Alt (拡張キー) の単独押しの解放: 0xE8 の押下・解放のあとに、解放をそのまま送り直す (先に解放が届くとメニューバーに移る)
        var release = new KeyEvent(VirtualKeys.RMenu, 0x38, Extended: true, IsUp: true, Injected: false, TimeMs: 0);
        var events = MeltypeEngine.MaskedAltRelease(release);
        Assert.Equal(3, events.Length);
        Assert.True(events[0] is { Vk: 0xE8, IsUp: false } && events[1] is { Vk: 0xE8, IsUp: true }, "先に 0xE8 を押して離す");
        Assert.True(events[2] is { Vk: VirtualKeys.RMenu, Scan: 0x38, Extended: true, IsUp: true }, "解放は右 Alt の拡張キーのまま");
    }

    [Test]
    public static void FirstLetter_AfterClickAndAutofill()
    {
        // クリックの直後 (1 秒以内) の打ち始めだけ、フォーカスを調べ直す
        Assert.True(Composition.FocusInspector.IsRightAfterClick(1500, 1000), "クリックの 0.5 秒後");
        Assert.True(!Composition.FocusInspector.IsRightAfterClick(2500, 1000), "クリックの 1.5 秒後は調べ直さない");
        Assert.True(!Composition.FocusInspector.IsRightAfterClick(1500, 0), "クリックしていない (1 回使った後) は調べ直さない");

        // Chrome・Edge・Vivaldi のフォームの自動入力の候補の行は、入力欄として扱う
        Assert.True(Composition.FocusInspector.IsSuggestionRow("PopupRowContentView"), "自動入力の候補");
        Assert.True(Composition.FocusInspector.IsSuggestionRow("OmniLinkItem"), "Vivaldi のアドレスバーの候補");
        Assert.True(!Composition.FocusInspector.IsSuggestionRow("PopupView"), "候補の行ではない");

        // 候補を閉じた直後の、種類も名前も無い要素: 同じウィンドウで直前 (2 秒以内) が候補の行なら、入力欄として扱う
        var row = new Composition.FocusInfo(true, false, null, "候補", "35000", "PopupRowContentView");
        Assert.True(Composition.FocusInspector.KeepsPreviousTextInput(0, "", "", row, sameForeground: true, sincePreviousMs: 660), "候補の行の 0.7 秒後");
        Assert.True(!Composition.FocusInspector.KeepsPreviousTextInput(0, "", "", row, sameForeground: true, sincePreviousMs: 2500), "候補の行から 2 秒より後");
        Assert.True(!Composition.FocusInspector.KeepsPreviousTextInput(0, "", "", row, sameForeground: false, sincePreviousMs: 100), "別のウィンドウ");
        Assert.True(!Composition.FocusInspector.KeepsPreviousTextInput(0, "", "", new Composition.FocusInfo(true, false, null, "Edit", "", "Edit"), sameForeground: true, sincePreviousMs: 100), "直前がふつうの入力欄 (候補を閉じた直後ではない)");
        Assert.True(!Composition.FocusInspector.KeepsPreviousTextInput(0, "", "", new Composition.FocusInfo(true, false, null, "直前の入力欄のまま"), sameForeground: true, sincePreviousMs: 100), "直前がこの判定で入力欄にした要素 (引き継ぎは続かない)");
        Assert.True(!Composition.FocusInspector.KeepsPreviousTextInput(0, "", "", new Composition.FocusInfo(true, true, null, "候補", "", "PopupRowContentView"), sameForeground: true, sincePreviousMs: 100), "直前がパスワード");
        Assert.True(!Composition.FocusInspector.KeepsPreviousTextInput(50000, "", "", row, sameForeground: true, sincePreviousMs: 100), "ボタン (種類のある要素)");
        Assert.True(!Composition.FocusInspector.KeepsPreviousTextInput(0, "メニュー", "", row, sameForeground: true, sincePreviousMs: 100), "名前のある要素");
        Assert.True(!Composition.FocusInspector.KeepsPreviousTextInput(0, "", "Chrome_WidgetWin_1", row, sameForeground: true, sincePreviousMs: 100), "クラス名のある要素");
    }

    [Test]
    public static void RecheckStale_CooldownIsPerForegroundWindow()
    {
        // ブラウザーで再確認した直後に LINE へ移っても、共有クールダウンで抑止されない
        var browser = new IntPtr(0x100);
        var line = new IntPtr(0x200);
        Assert.True(Composition.FocusInspector.RecheckCooldownActive(browser, browser, 500, 0, 1000), "同じウィンドウは 1 秒以内なら抑止");
        Assert.True(!Composition.FocusInspector.RecheckCooldownActive(line, browser, 500, 0, 1000), "別ウィンドウへ移ったらすぐ再確認してよい");
        Assert.True(!Composition.FocusInspector.RecheckCooldownActive(browser, browser, 1500, 0, 1000), "同じウィンドウでも 1 秒過ぎれば再確認してよい");
        Assert.True(!Composition.FocusInspector.RecheckCooldownActive(IntPtr.Zero, browser, 500, 0, 1000), "前面ウィンドウが無いときは抑止しない");
    }

    [Test]
    public static void WebInputClassNames_AreTextInputs()
    {
        // #70: Chatwork の検索欄は Group で、クラス名 (sc-kzAZJu ijnNh inputLong) でしか入力欄と分からない
        foreach (var name in new[] { "sc-kzAZJu ijnNh inputLong", "input", "searchInput", "search-input", "chat_input", "input-field" })
            Assert.True(Composition.FocusInspector.LooksLikeInputClass(name), name);
        foreach (var name in new[] { "", "inputs", "input-group", "inputWrapper", "searchInputButton", "sc-kzAZJu ijnNh", "inputtable" })
            Assert.True(!Composition.FocusInspector.LooksLikeInputClass(name), name);
    }

    [Test]
    public static void StartupTask_IsLogonTriggerWithoutDelayAndAdmin()
    {
        // #283: サインインしたらすぐ起動するタスク。& などを含むユーザー名・パスでも XML として読める
        var xml = Startup.TaskXml(@"PC\R&D <user>", @"C:\Users\R&D\AppData\Local\Programs\Meltype\Meltype.exe");
        var document = System.Xml.Linq.XDocument.Parse(xml);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal(@"PC\R&D <user>", document.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
        Assert.Equal("LeastPrivilege", document.Descendants(ns + "RunLevel").Single().Value, "管理者権限なし");
        Assert.True(!document.Descendants(ns + "Delay").Any(), "遅らせない");
        Assert.Equal(@"C:\Users\R&D\AppData\Local\Programs\Meltype\Meltype.exe", document.Descendants(ns + "Command").Single().Value);
        Assert.Equal(@"C:\Users\R&D\AppData\Local\Programs\Meltype", document.Descendants(ns + "WorkingDirectory").Single().Value);
    }

    [Test]
    public static void Startup_FallsBackToKeyboardWhenMeltypeImeIsUnusable()
    {
        // #366: 動作モードの初期値は Meltype IME。使えないときは、起動したときに Meltype キーボードに戻す
        var x64 = System.Runtime.InteropServices.Architecture.X64;
        var arm64 = System.Runtime.InteropServices.Architecture.Arm64;
        var fresh = new Settings();
        Assert.Equal(InputMode.Tsf, fresh.Mode, "初期値は Meltype IME");
        Assert.True(!global::Meltype.Program.ShouldFallBackToKeyboard(fresh, knownUnregistered: false, inUserLanguageList: true, x64), "登録済みで一覧にもある");
        Assert.True(global::Meltype.Program.ShouldFallBackToKeyboard(fresh, knownUnregistered: false, inUserLanguageList: true, arm64), "ARM64 は登録済みで一覧にあっても戻す");
        Assert.True(global::Meltype.Program.ShouldFallBackToKeyboard(fresh, knownUnregistered: true, inUserLanguageList: false, x64), "登録されていない");
        Assert.True(global::Meltype.Program.ShouldFallBackToKeyboard(fresh, knownUnregistered: false, inUserLanguageList: false, x64), "まだ切り替えたことがなく、一覧に無い");

        var introduced = new Settings { TsfIntroduced = true };
        Assert.True(!global::Meltype.Program.ShouldFallBackToKeyboard(introduced, knownUnregistered: false, inUserLanguageList: false, x64), "使っていた人は、一覧に無くても戻さない");
        Assert.True(global::Meltype.Program.ShouldFallBackToKeyboard(introduced, knownUnregistered: false, inUserLanguageList: true, arm64), "使っていた人も、ARM64 なら戻す");
        Assert.True(!global::Meltype.Program.ShouldFallBackToKeyboard(new Settings { Mode = InputMode.Keyboard }, knownUnregistered: true, inUserLanguageList: false, arm64), "Meltype キーボードはそのまま");
    }

    [Test]
    public static void Portable_UsesDataFolderBesideExecutable()
    {
        Assert.Equal(Path.Combine(@"C:\Apps\Meltype", "data"), AppPaths.ResolveDataDirectory(true, null, @"C:\Apps\Meltype", @"C:\Users\User\AppData\Local"));
        Assert.Equal(@"D:\MeltypeData", AppPaths.ResolveDataDirectory(true, @"D:\MeltypeData", @"C:\Apps\Meltype", @"C:\Users\User\AppData\Local"), "開発用の上書き先は portable より優先");
    }

    [Test]
    public static void Portable_StartupResetsModeToKeyboard()
    {
        var x64 = System.Runtime.InteropServices.Architecture.X64;
        Assert.True(global::Meltype.Program.ShouldUseKeyboardOnStartup(new Settings { Mode = InputMode.Tsf }, isPortable: true, knownUnregistered: false, inUserLanguageList: true, x64), "ポータブル版は Meltype IME から戻す");
        Assert.True(!global::Meltype.Program.ShouldUseKeyboardOnStartup(new Settings { Mode = InputMode.Keyboard }, isPortable: true, knownUnregistered: false, inUserLanguageList: true, x64), "キーボード方式は維持");
    }

    [Test]
    public static void AffinityCandidate_RejectsOtherProductsAndMenus()
    {
        Assert.True(FocusInspector.IsAffinityCanvasCandidate("AFFINITY.EXE", UiAutomation.ControlTypePane, "Static"), "大小文字と Pane");
        Assert.True(!FocusInspector.IsAffinityCanvasCandidate("Photo.exe", UiAutomation.ControlTypeText, "Static"), "旧製品は対象外");
        Assert.True(!FocusInspector.IsAffinityCanvasCandidate("Affinity.exe", 50009, "Static"), "メニューは対象外");
    }
    [Test]
    public static void AffinityIme_UsesOneDeadlineAndStopsAfterFailure()
    {
        double now = 0; var budgets = new List<uint>();
        var active = Imm32ImeController.HasInputContext(20, (_, remaining) => { budgets.Add(remaining); now += 9.75; return (true, 0); }, () => TimeSpan.FromMilliseconds(now));
        Assert.True(!active, "ゼロの応答は入力可能としない");
        Assert.True(string.Join(',', budgets) == "20,10", "残り 10.25ms は 10ms に切り捨て、0.5ms では問い合わせない");
        foreach (var success in new[] { false, true })
        {
            now = 0; var calls = 0;
            active = Imm32ImeController.HasInputContext(20, (_, _) => { calls++; now = success ? 21 : 0; return (success, 0x19); }, () => TimeSpan.FromMilliseconds(now));
            Assert.True(!active && calls == 1, "失敗や期限後の肯定結果を採用せず、追加の問い合わせをしない");
        }
    }

    [Test]
    public static void CopyClipboard_KeepsContentAfterClipboardChanges()
    {
        // ほかのアプリ (クリップボードの履歴など) が同時にクリップボードを開いたり読んだりすると、入れられなかったり、
        // テストで入れた Stream を先に読み進められて中身が 0 になったりするので、何回か試す
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                CheckCopyClipboard();
                return;
            }
            catch (Exception) when (attempt < 5)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void CheckCopyClipboard()
    {
        // 貼り付けで確定したあと、元のクリップボードに戻す。GetDataObject() のまま戻すと、そのときの中身 (貼った文字) を読んで元の中身が消えていた
        var before = CompositionService.CopyClipboard();
        try
        {
            var text = new DataObject();
            text.SetText("コピーしていた文字");
            text.SetText("<b>コピーしていた文字</b>", TextDataFormat.Html);
            text.SetData("Meltype.Test", autoConvert: false, new MemoryStream([1, 2, 3]));
            Clipboard.SetDataObject(text, copy: true);
            RestoreAfterPaste();
            Assert.Equal("コピーしていた文字", Read(() => Clipboard.GetText()), "文字");
            Assert.True(Read(() => Clipboard.GetText(TextDataFormat.Html)).Contains("<b>コピーしていた文字</b>"), "HTML");
            Assert.Equal("1,2,3", Read(() => Clipboard.GetDataObject()?.GetData("Meltype.Test") is MemoryStream stream ? string.Join(",", stream.ToArray()) : ""), "アプリ独自の形式");

            using (var bitmap = new System.Drawing.Bitmap(3, 2))
            {
                bitmap.SetPixel(1, 1, System.Drawing.Color.Red);
                Clipboard.SetImage(bitmap);
            }
            RestoreAfterPaste();
            Assert.Equal("3x2 FFFF0000", Read(() => Clipboard.GetImage() is System.Drawing.Bitmap image ? $"{image.Width}x{image.Height} {image.GetPixel(1, 1).ToArgb():X}" : ""), "画像");

            Clipboard.SetFileDropList([Environment.ProcessPath!]);
            RestoreAfterPaste();
            Assert.Equal(Environment.ProcessPath, Read(() => string.Join(";", Clipboard.GetFileDropList().Cast<string>())), "ファイル");
        }
        finally
        {
            if (before is not null) Clipboard.SetDataObject(before, copy: true);
            else Clipboard.Clear();
        }

        static void RestoreAfterPaste()
        {
            // 入れた直後は、ほかのアプリ (クリップボードの履歴など) が開いていて読めないことがある
            Thread.Sleep(200);
            var saved = CompositionService.CopyClipboard();
            Assert.True(saved is not null, "中身を写せた");
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, "貼り付けた文字"), copy: true);
            Clipboard.SetDataObject(saved!, copy: true);
        }

        static string Read(Func<string> read)
        {
            for (var i = 0; i < 20; i++)
            {
                if (read() is { Length: > 0 } value) return value;
                Thread.Sleep(50);
            }
            return "";
        }
    }

    [Test]
    public static void ClipboardSequence_ChangesOnlyWhenContentChanges()
    {
        // 貼り付けのあとに番号が変わっていたら戻さない。Ctrl+V を受けたアプリやクリップボードの履歴が読んだだけで変わると、戻らなくなる
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                CheckClipboardSequence();
                return;
            }
            catch (Exception) when (attempt < 5)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void CheckClipboardSequence()
    {
        var before = CompositionService.CopyClipboard();
        try
        {
            Clipboard.SetDataObject(CompositionService.PasteData("貼り付けた文字"), copy: true);
            var pasted = Native.GetClipboardSequenceNumber();
            Assert.True(pasted != 0, "番号を読めた");
            Assert.Equal("貼り付けた文字", Clipboard.GetText(), "文字");
            Clipboard.GetText(TextDataFormat.Text); // ANSI の文字 (Windows が作る形式)
            Clipboard.GetDataObject()?.GetData("CanIncludeInClipboardHistory");
            CompositionService.CopyClipboard();
            Assert.Equal(pasted, Native.GetClipboardSequenceNumber(), "読むだけでは変わらない");

            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, "ユーザーがコピーした文字"), copy: true);
            Assert.True(Native.GetClipboardSequenceNumber() != pasted, "ほかの中身を入れたら変わる");
        }
        finally
        {
            if (before is not null) Clipboard.SetDataObject(before, copy: true);
            else Clipboard.Clear();
        }
    }

    [Test]
    public static void SettingsToolTip_BreaksLinesForReading()
    {
        Assert.Equal("ひとつめ。\nふたつめ。", UI.SettingsForm.WrapToolTip("ひとつめ。ふたつめ。"), "文ごとに改行する");
        Assert.Equal($"{new string('あ', 39)}\nAnyDesk を使う", UI.SettingsForm.WrapToolTip($"{new string('あ', 39)}AnyDesk を使う"), "英単語の途中で折り返さない");
        Assert.Equal($"{new string('あ', 39)}\nあ、い", UI.SettingsForm.WrapToolTip($"{new string('あ', 40)}、い"), "読点を行頭に置かない");
        Assert.Equal($"{new string('あ', 39)}\n(OFF)", UI.SettingsForm.WrapToolTip($"{new string('あ', 39)}(OFF)"), "開き括弧を行末に置かない");

        // 設定画面に出す説明すべてで、文字を落とさず、長すぎる行・行頭の閉じ括弧・行末の開き括弧を作らない
        var descriptions = typeof(Settings).GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).OfType<System.ComponentModel.DescriptionAttribute>().FirstOrDefault()?.Description)
            .OfType<string>().ToList();
        Assert.True(descriptions.Count > 20, "説明を読めている");
        foreach (var description in descriptions)
        {
            var wrapped = UI.SettingsForm.WrapToolTip(description);
            Assert.Equal(description.Replace(" ", ""), wrapped.Replace("\n", "").Replace(" ", ""), description);
            foreach (var line in wrapped.Split('\n'))
            {
                Assert.True(line.Sum(c => c < 0x80 ? 1 : 2) <= 80, $"長すぎる行: {line}");
                Assert.True(line.Length > 0 && !")）」』]、。,.・".Contains(line[0]) && !"(（「『[".Contains(line[^1]), $"括弧・句読点の位置: {line}");
            }
        }
    }

    [Test]
    public static void SettingsForm_UsesToggleSwitchesForEveryOnOffSetting()
    {
        using var engine = new MeltypeEngine(new Settings(), null, null);
        using var form = new UI.SettingsForm(engine);
        var controls = Descendants(form).ToList();
        // 設定画面に出す bool の項目は、すべてスイッチ (ON/OFF だけのプルダウン・チェックボックスを残さない)
        var settings = typeof(Settings).GetProperties().Count(p => p.PropertyType == typeof(bool) && p.CanWrite &&
            p.GetCustomAttributes(typeof(System.ComponentModel.BrowsableAttribute), false).OfType<System.ComponentModel.BrowsableAttribute>().FirstOrDefault()?.Browsable != false);
        Assert.True(settings >= 30, $"ON/OFF の設定を読めている: {settings}");
        Assert.Equal(settings + 2, controls.OfType<UI.ToggleSwitch>().Count(), "ON/OFF の設定 + 上の表示オプション 2 つ");
        Assert.True(!controls.OfType<ComboBox>().Any(c => c.Items.Count == 2 && c.Items.Contains("ON")), "ON/OFF だけのプルダウンが残っていない");
        Assert.True(!controls.OfType<CheckBox>().Any(), "チェックボックスが残っていない");
        // 表の ON/OFF の列 (アプリ別設定の「有効」・独自の種類の「最初は英数」) もスイッチで、値は bool
        var columns = controls.OfType<DataGridView>().SelectMany(g => g.Columns.Cast<DataGridViewColumn>()).ToList();
        Assert.Equal(2, columns.OfType<UI.ToggleSwitchColumn>().Count(), "表のスイッチの列");
        Assert.True(!columns.OfType<DataGridViewComboBoxColumn>().Any(c => c.Items.Count == 2 && c.Items.Contains("ON")), "表に ON/OFF だけのプルダウンの列が残っていない");
        var rules = controls.OfType<DataGridView>().First(g => g.Columns[0].HeaderText.StartsWith("プロセス名", StringComparison.Ordinal));
        Assert.Equal(false, rules.Rows.Cast<DataGridViewRow>().First(r => Equals(r.Cells[0].Value, "mstsc.exe")).Cells[1].Value, "mstsc.exe は OFF");
        Assert.Equal(true, rules.Rows.Cast<DataGridViewRow>().First(r => Equals(r.Cells[0].Value, "Code.exe")).Cells[1].Value, "Code.exe は ON");
        // 画面の値を設定に戻す (Collect) ときも、スイッチと表の bool がそのまま設定になる
        var collect = form.GetType().GetMethod("Collect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var collected = (Settings)collect.Invoke(form, null)!;
        Assert.True(!collected.IsAppEnabled("mstsc.exe") && collected.IsAppEnabled("Code.exe"), "表の値がそのまま設定になる");
        Assert.Equal(engine.Settings.Clone().Normalize().ToJson(), collected.ToJson(), "何も変えなければ値は変わらない (閉じるときに保存しない)");
        var live = controls.OfType<UI.ToggleSwitch>().First(t => t.AccessibleName == "ライブ変換");
        Assert.True(live.Checked, "既定の ON が画面に出ている");
        live.Checked = false;
        Assert.True(!((Settings)collect.Invoke(form, null)!).LiveConversion, "スイッチを OFF にしたら設定も OFF");
    }

    [Test]
    public static void ToggleSwitch_TogglesByDefaultActionAndPaints()
    {
        using var toggle = new UI.ToggleSwitch { Font = new Font("Yu Gothic UI", 9.5F) };
        var changed = 0;
        toggle.CheckedChanged += (_, _) => changed++;
        toggle.Checked = true;
        toggle.Checked = true;
        Assert.Equal(1, changed, "同じ値では CheckedChanged を出さない");
        Assert.True((toggle.AccessibilityObject.State & AccessibleStates.Checked) != 0, "ナレーターに ON と伝わる");
        toggle.AccessibilityObject.DoDefaultAction();
        Assert.True(!toggle.Checked && changed == 2, "ナレーターの既定の操作で切り替わる");
        Assert.True((toggle.AccessibilityObject.State & AccessibleStates.Checked) == 0, "ナレーターに OFF と伝わる");
        var plain = toggle.Width;
        Assert.True(plain > 40 && toggle.Height >= 20, $"トラック (40×20) と文字のぶんの大きさ: {toggle.Size}");
        toggle.Text = "見出し";
        Assert.True(toggle.Width > plain, "見出しのぶん広がる");
        // 描ける (自己診断と --render-forms は設定画面ごと画像に描く)
        using var bitmap = new Bitmap(toggle.Width, toggle.Height);
        toggle.DrawToBitmap(bitmap, toggle.ClientRectangle);
        Assert.True(UI.ToggleSwitch.AccentColor().A == 255, "アクセント色を読める (読めなければ既定の青)");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    [Test]
    public static void Updater_StopsScriptThatDoesNotFinish()
    {
        static Process Start(string command)
        {
            var start = new ProcessStartInfo(Updater.PowerShell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "-NoProfile", "-Command", command }) start.ArgumentList.Add(arg);
            return Process.Start(start)!;
        }
        using (var process = Start("Write-Output 'ready 9.9.9'"))
            Assert.Equal("ready 9.9.9", Updater.ReadOutput(process, 30_000)?.Trim(), "終わったら出力を返す");

        // 出力の途中で止まった PowerShell を、ReadToEnd で待ち続けずに時間で終了させる
        using (var process = Start("Write-Output 'start'; Start-Sleep 30; Write-Output 'none'"))
        {
            var watch = Stopwatch.StartNew();
            Assert.Equal(null, Updater.ReadOutput(process, 2_000), "時間切れなら null");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"時間で区切る: {watch.Elapsed}");
            Assert.True(process.WaitForExit(5_000), "PowerShell を終了させる");
        }
    }

    [Test]
    public static void UpdateReadyMessage_FollowsAutoUpdateSetting()
    {
        // 自動更新が OFF だと次のサインインでは更新しない (Updater.ApplyStagedAtStartup) ので、そう案内しない
        Assert.True(UI.TrayApplicationContext.UpdateReadyMessage("1.2.3", autoUpdate: true).Contains("次に Windows にサインインしたときに更新します"), "ON はサインイン時に更新する");
        var off = UI.TrayApplicationContext.UpdateReadyMessage("1.2.3", autoUpdate: false);
        Assert.True(!off.Contains("サインイン"), "OFF はサインイン時に更新しない");
        Assert.True(off.Contains("「Meltype 1.2.3 に更新して再起動」"), "OFF はトレイのメニューの項目名で案内する");
    }
}
