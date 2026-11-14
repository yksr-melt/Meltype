// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Meltype.Config;

namespace Meltype.UI;

/// <summary>タスクトレイ (設計書 §28)。Ctrl+Alt+F12 で Meltype の一時停止/再開。</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly MeltypeEngine _engine;
    private readonly AppState _state;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    // Zenzai の状態 (「Zenzai を使う」が ON のときだけ出す。issue #1152)
    private readonly ToolStripMenuItem _zenzaiItem = new() { Enabled = false, Visible = false };
    private readonly ToolStripMenuItem _enabledItem;
    private readonly HotkeyWindow _hotkey;
    private readonly HotkeyWindow _registerHotkey;
    private readonly Control _invoker = new();
    private readonly Icon _onIcon = CreateIcon("あ", Color.FromArgb(0, 120, 212));
    private readonly Icon _directIcon = CreateIcon("A", Color.FromArgb(0, 120, 212));
    private readonly Icon _offIcon = CreateIcon("A", Color.FromArgb(120, 120, 120));
    private readonly ToolStripMenuItem _keyboardModeItem;
    private readonly ToolStripMenuItem _autoDetectionItem;
    private readonly ToolStripMenuItem _profileItem;
    private readonly Composition.CompositionService _composition;
    private readonly Tip.TipServer _tipServer;
    // 更新前の Meltype IME が残っているアプリ (TipServer.ClientOutdated)
    private readonly HashSet<string> _outdatedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Keyboard.KeyboardFront _keyboard;
    private readonly ToolStripMenuItem _tsfModeItem;
    private SettingsForm? _settingsForm;
    private LogForm? _logForm;
    private UserDictionaryForm? _dictionaryForm;
    private SnippetsForm? _snippetsForm;
    private ReportDialog? _reportDialog;
    private LearnedWordsForm? _learnedForm;
    private KeyCountForm? _keyCountForm;
    private WelcomeForm? _welcomeForm;
    private readonly Updater _updater;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _autoUpdateItem;
    private int _exclusionNotificationScheduled;
    private volatile bool _exiting;

    public TrayApplicationContext(MeltypeEngine engine, AppState state)
    {
        _engine = engine;
        _state = state;
        _invoker.CreateControl();
        // 入力・表示は、動かしている設定 (Meltype IME を使えなければ Meltype キーボード) を読む
        _composition = new Composition.CompositionService(_invoker, _engine.Runtime, () => _engine.Running);
        _keyboard = new Keyboard.KeyboardFront(_invoker, _composition, environment => new Ime.ImeHost(environment, () => _engine.Running));
        _keyboard.ModeChanged += OnKeyboardModeChanged;
        _engine.Keyboard = _keyboard;
        _engine.Focus = _composition.Focus;
        _composition.Focus.TextInputEntered += _engine.OnTextInputEntered;
        _tipServer = new Tip.TipServer(_invoker, _composition, environment => new Ime.ImeHost(environment, () => _engine.Running));
        _engine.ImeServer = _tipServer;
        _tipServer.ClientOutdated += process =>
        {
            // DLL は動作モードが Meltype キーボードのときも接続してくる。ログはアプリごとに 1 回
            if (_engine.Running.Mode != InputMode.Tsf || !_outdatedApps.Add(process)) return;
            Diagnostics.Log.Warn($"Meltype IME: {process} に、更新前の Meltype IME が残っています。{process} を再起動するまで、Meltype IME で入力できません。");
            // 更新前から開いているアプリはすべて同じなので、アプリごとではなく、PC の再起動を 1 回だけ求める
            // (設定「通知を表示」にかかわらず。入力を止めないように、ダイアログではなく通知にする)
            if (_outdatedApps.Count == 1)
                ShowBalloonTip(10000, "Meltype IME", $"更新前から開いているアプリ (「{process}」など) は、PC を再起動するまで Meltype IME で入力できません。PC を再起動してください。");
        };
        _tipServer.Start();
        _engine.SettingsChanged += OnEngineSettingsChanged;
        IntroduceTsf();

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem { Enabled = false };
        _enabledItem = new ToolStripMenuItem("Meltype を有効にする", null, (_, _) => ToggleEnabled()) { CheckOnClick = false };
        _keyboardModeItem = new ToolStripMenuItem("Meltype キーボード (変換ボックスで入力)", null, (_, _) => SetMode(InputMode.Keyboard));
        _tsfModeItem = new ToolStripMenuItem("Meltype IME (入力欄に直接入力)", null, (_, _) => SelectTsf());
        menu.Items.Add(_statusItem);
        menu.Items.Add(_zenzaiItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        if (!AppPaths.IsPortable) menu.Items.Add(_tsfModeItem);
        menu.Items.Add(_keyboardModeItem);
        // 英語 / 日本語の自動判定 (OFF なら提案だけ出し、Tab で英字にする)
        _autoDetectionItem = new ToolStripMenuItem("英語 / 日本語を自動で判定する", null, (_, _) => ToggleAutoDetection());
        menu.Items.Add(_autoDetectionItem);
        // プロファイル (仕事用・趣味用・SNS 用など)。一覧は開くたびに作る (設定画面で足したもの・名前を変えたものを出す)
        _profileItem = new ToolStripMenuItem("プロファイル");
        _profileItem.DropDownItems.Add("(読み込み中)");
        _profileItem.DropDownOpening += (_, _) => FillProfiles();
        menu.Items.Add(_profileItem);
        ToolStripMenuItem? startup = null;
        if (!AppPaths.IsPortable)
        {
            startup = new ToolStripMenuItem("Windows の起動時に起動", null, (_, _) => ToggleStartup());
            startup.ToolTipText = "サインインしたらすぐ起動するよう、タスク スケジューラに「Meltype」というタスクを登録します (OFF にすると消します)。";
            menu.Items.Add(startup);
        }
        menu.Items.Add("使い方...", null, (_, _) => ShowWelcome());
        menu.Items.Add("設定...", null, (_, _) => ShowSettings());
        menu.Items.Add("ユーザー辞書...", null, (_, _) => ShowUserDictionary());
        menu.Items.Add("定型文...", null, (_, _) => ShowSnippets());
        menu.Items.Add("ログ / 判定理由...", null, (_, _) => ShowLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("データフォルダを開く", null, (_, _) => OpenDataFolder());
        var backup = new ToolStripMenuItem("バックアップ");
        backup.DropDownItems.Add("バックアップを書き出す...", null, (_, _) => CreateBackup());
        backup.DropDownItems.Add("バックアップから戻す...", null, (_, _) => RestoreBackup());
        menu.Items.Add(backup);
        menu.Items.Add("不具合の報告・提案...", null, (_, _) => OpenReport());
        menu.Items.Add("直した誤判定を報告...", null, (_, _) => new CorrectionReportDialog(_composition.Corrections, _engine.Running).Show());
        menu.Items.Add("Meltype について...", null, (_, _) => MessageBox.Show(AppInfo.AboutText, "Meltype について", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add("学習した語...", null, (_, _) => ShowLearnedWords());
        menu.Items.Add("打鍵数...", null, (_, _) => ShowKeyCount());
        var forget = new ToolStripMenuItem("学習データを消す");
        forget.DropDownItems.Add("この 1 時間に覚えたもの...", null, (_, _) => ForgetRecentLearning(DateTime.UtcNow.AddHours(-1), "この 1 時間"));
        forget.DropDownItems.Add("今日覚えたもの...", null, (_, _) => ForgetRecentLearning(DateTime.Today.ToUniversalTime(), "今日"));
        forget.DropDownItems.Add("すべて (リセット)...", null, (_, _) => ResetLearning());
        menu.Items.Add(forget);
        if (!AppPaths.IsPortable) menu.Items.Add("アンインストール...", null, (_, _) => Uninstall());
        // 更新: 自動更新の ON/OFF、今すぐ確認、ダウンロード済みなら更新して再起動
        var updates = new ToolStripMenuItem("更新");
        _autoUpdateItem = new ToolStripMenuItem("自動で更新する", null, (_, _) => ToggleAutoUpdate());
        updates.DropDownItems.Add(_autoUpdateItem);
        updates.DropDownItems.Add("今すぐ更新を確認", null, (_, _) => CheckForUpdate());
        updates.DropDownItems.Add("リリースノート...", null, (_, _) => ReleaseNotesDialog.ShowFor(AppInfo.Version, updated: false));
        _updateItem = new ToolStripMenuItem("", null, (_, _) => ApplyUpdate()) { Visible = false };
        updates.DropDownItems.Add(_updateItem);
        updates.DropDownOpening += (_, _) =>
        {
            _autoUpdateItem.Checked = _engine.Settings.AutoUpdate;
            // プレリリース版の更新を OFF にしたら、ダウンロード済みのプレリリース版の「更新して再起動」は出さない
            ShowUpdateItem();
        };
        if (!AppPaths.IsPortable) menu.Items.Add(updates);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitThread());
        if (startup is not null) menu.Opening += (_, _) => startup.Checked = Startup.IsEnabled;
        menu.Opening += (_, _) => UpdateStatus();

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ToggleEnabled();
        _hotkey = new HotkeyWindow(ToggleEnabled, HotkeyWindow.WithCustom(_engine.Settings.PauseHotkey, HotkeyWindow.ToggleKeys));
        _registerHotkey = new HotkeyWindow(RegisterSelectedWord, HotkeyWindow.RegisterWordKeys, "単語の登録");
        _engine.ToggleRequested += OnToggleRequested;
        _enabledItem.Text = _hotkey.Name is { } hotkey ? $"Meltype を有効にする (Ctrl+半角/全角, {hotkey})" : "Meltype を有効にする (Ctrl+半角/全角)";
        _updater = new Updater(() => !AppPaths.IsPortable && _engine.Settings.AutoUpdate, () => Prerelease);
        _updater.Ready += OnUpdateReady;
        ShowUpdateItem();
        UpdateStatus();
        // 前回の更新に失敗していたら知らせる (設定「通知を表示」にかかわらず。入力を止めないように、ダイアログではなく通知にする)
        if (Updater.FailedVersion is { } failed)
            _invoker.BeginInvoke(() => _tray.ShowBalloonTip(10000, "Meltype - 更新失敗", UpdateFailedMessage(failed, _engine.Settings.AutoUpdate), ToolTipIcon.Warning));
        // 初めて起動したら使い方を見せる (見せたことは state.json に残す)
        var firstRun = !_state.WelcomeShown;
        if (firstRun)
        {
            _state.WelcomeShown = true;
            _state.Save();
            _invoker.BeginInvoke(ShowWelcome);
        }
        // 更新したあとの初めての起動なら、その版のリリースノートを一度だけ見せる (issue #1207)。
        // 初めて起動した人には見せず、今の版を見せたことにしておく
        if (_state.ReleaseNotesShown != AppInfo.Version)
        {
            var showNotes = ProjectInfo.ShouldShowReleaseNotes(_state.ReleaseNotesShown, AppInfo.Version, firstRun);
            _state.ReleaseNotesShown = AppInfo.Version;
            _state.Save();
            if (showNotes) _invoker.BeginInvoke(() => ReleaseNotesDialog.ShowFor(AppInfo.Version, updated: true));
        }
        // Meltype IME が入る前の版から更新した人に、一度だけ Meltype IME を入れるかを聞く
        // (初めて起動した人には、インストーラーが聞いている)
        if (!AppPaths.IsPortable && !_state.TsfOffered)
        {
            _state.TsfOffered = true;
            _state.Save();
            if (!firstRun && CanRegisterTsf && !_state.TsfIntroduced && Tip.TipServer.IsKnownUnregistered) _invoker.BeginInvoke(() => EnableTsf(
                "Meltype IME を入れますか？\n\n" +
                "Meltype IME は Windows の IME として、打った文字を入力欄に直接入れます。変換ボックスが出ないアプリでも入力できます。「いいえ」なら、今の動作モードのまま使えます。"));
        }
        // 動作モードが Meltype IME なのに、登録済みの impl がこの Meltype.exe と別ビルドで、Meltype キーボードで動いている
        // (登録した人が別の版の Meltype に更新した・同じ PC のほかのユーザーが別の版を入れた)。聞いたことは覚えず、起動のたびに聞く
        if (CanRegisterTsf && _engine.Settings.Mode == InputMode.Tsf && Tip.TipServer.IsOtherBuildRegistered) _invoker.BeginInvoke(OfferTsfUpdate);
        // 起動時の設定反映が終わってから、読み込み中に蓄積した除外をまとめて通知する。
        Diagnostics.ShortcutExclusions.Added += OnShortcutExcluded;
        OnShortcutExcluded();
    }

    /// <summary>
    /// Meltype IME (TSF) を入れたら、一度だけ動作モードを Meltype IME にする
    /// (PC に登録されていても、このユーザーのキーボードの一覧に無ければ Win + Space で選べないので切り替えない)
    /// (ARM64 の Windows には Meltype IME は対応していないので切り替えない)
    /// </summary>
    private void IntroduceTsf()
    {
        if (AppPaths.IsPortable || _state.TsfIntroduced || !Tip.TipServer.IsRegistered || !Tip.TipServer.IsInUserLanguageList ||
            RuntimeInformation.OSArchitecture == Architecture.Arm64) return;
        var wasTsf = _engine.Running.Mode == InputMode.Tsf;
        _state.TsfIntroduced = true;
        _state.Save();
        var next = _engine.Settings.Clone();
        next.Mode = InputMode.Tsf;
        _engine.ApplySettings(next);
        // ほかのモードからなら MeltypeEngine.OnSettingsChanged が切り替える。もう Meltype IME のモードだったときも、入力方式を Meltype IME にする
        if (wasTsf) Tip.TipServer.ActivateForSession();
        Diagnostics.Log.Info("Meltype IME が入っているので、動作モードを Meltype IME にしました。");
    }

    /// <summary>Meltype IME をインストール先の setup-ime.ps1 で登録できるか (ポータブル版・ARM64 の Windows・tip フォルダーの無い Meltype.exe では登録できない)。</summary>
    private static bool CanRegisterTsf => !AppPaths.IsPortable && RuntimeInformation.OSArchitecture != Architecture.Arm64 &&
        File.Exists(Path.Combine(AppContext.BaseDirectory, "setup-ime.ps1")) && Directory.Exists(Path.Combine(AppContext.BaseDirectory, "tip"));

    /// <summary>トレイで Meltype IME を選んだ。使えなければ、登録するかを聞いてから切り替える。</summary>
    private void SelectTsf()
    {
        if (Tip.TipServer.IsUsable) SetMode(InputMode.Tsf);
        else EnableTsf("Meltype IME を Windows に登録しますか？\n\n" +
            "Meltype IME は Windows の IME として、打った文字を入力欄に直接入れます。変換ボックスが出ないアプリでも入力できます。");
    }

    /// <summary>Meltype IME を登録するかを聞き、使えるようになったら動作モードを Meltype IME にする。</summary>
    private async void EnableTsf(string question)
    {
        if (!await OfferRegisterTsf(question)) return;
        // 次に起動したとき IntroduceTsf で切り替え直さないように、切り替えたことも残す (あとでユーザーが戻したら、そのまま)
        _state.TsfIntroduced = true;
        _state.Save();
        SetMode(InputMode.Tsf);
        UpdateStatus();
    }

    /// <summary>登録済みの Meltype IME を、この Meltype.exe に同梱のものに更新するかを聞き、更新できたら Meltype IME で動かし直す。</summary>
    private async void OfferTsfUpdate()
    {
        if (!await OfferRegisterTsf("Meltype IME を更新しますか？\n\n" +
            "Windows に登録されている Meltype IME が、ほかの版の Meltype のものです。更新するまでは、Meltype キーボード (変換ボックスで入力) で動きます。")) return;
        // 動かす動作モード (MeltypeEngine.Running) を決め直す
        _engine.ApplySettings(_engine.Settings);
        UpdateStatus();
    }

    /// <summary>聞いて「はい」なら、インストール先の setup-ime.ps1 で Meltype IME を登録する (UAC の確認が出る)。Meltype IME を使えるようになったら true。</summary>
    private static async Task<bool> OfferRegisterTsf(string question)
    {
        // 起動したときにも出すので、ほかのウィンドウの後ろに隠れないように最前面にする
        using var owner = new Form { TopMost = true };
        var answer = MessageBox.Show(owner, question + "\n\n次に「Windows PowerShell」の管理者権限の確認が出ます。",
            "Meltype IME", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return false;
        await RegisterTsf();
        // 登録できても、キーボードの一覧に足せなければ Win + Space で選べない
        if (Tip.TipServer.IsUsable) return true;
        MessageBox.Show(owner, $"Meltype IME を使えるようにできませんでした。今の動作モードのまま使えます。\n\n記録: {Path.Combine(Path.GetTempPath(), "Meltype-setup-ime.log")}",
            "Meltype IME", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    /// <summary>setup-ime.ps1 で Meltype IME を登録する (UAC の確認が出る)。</summary>
    private static async Task RegisterTsf()
    {
        var directory = AppContext.BaseDirectory;
        var start = new ProcessStartInfo(Updater.PowerShell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(directory, "setup-ime.ps1"), "-Source", Path.Combine(directory, "tip") })
        {
            start.ArgumentList.Add(arg);
        }
        try
        {
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"Meltype IME を登録できませんでした: {ex.Message}");
        }
    }

    private void ShowWelcome()
    {
        if (_welcomeForm is { IsDisposed: false })
        {
            _welcomeForm.Activate();
            return;
        }
        _welcomeForm = new WelcomeForm();
        _welcomeForm.Show();
    }

    /// <summary>新しい版をダウンロードし終えた: メニューに「更新して再起動」を出し、通知する (クリックで今すぐ更新)。</summary>
    private void OnUpdateReady(string version)
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return;
        _invoker.BeginInvoke(() =>
        {
            ShowUpdateItem();
            if (!_engine.Settings.ShowNotifications) return;
            _tray.BalloonTipClicked -= OnUpdateBalloonClicked;
            _tray.BalloonTipClicked += OnUpdateBalloonClicked;
            _tray.ShowBalloonTip(5000, "Meltype の更新", UpdateReadyMessage(version, _engine.Settings.AutoUpdate), ToolTipIcon.Info);
        });
    }

    /// <summary>自動更新が OFF なら、次のサインインでは更新しない (Updater.ApplyStagedAtStartup)。トレイのメニューから更新できると案内する。</summary>
    internal static string UpdateReadyMessage(string version, bool autoUpdate) => autoUpdate
        ? $"Meltype {version} を準備しました。次に Windows にサインインしたときに更新します。ここをクリックすると今すぐ更新します。"
        : $"Meltype {version} を準備しました。トレイの「更新」→「Meltype {version} に更新して再起動」で更新できます。ここをクリックすると今すぐ更新します。";

    /// <summary>
    /// 自動更新が ON なら、Updater がダウンロードし直し、次のサインインでもう一度更新する。
    /// Meltype IME を更新できなかったとき (UAC を断った) も、インストーラーが本体を入れ替えずに止めるので、この通知になる。
    /// </summary>
    internal static string UpdateFailedMessage(string version, bool autoUpdate) => autoUpdate
        ? $"Meltype {version} に更新できませんでした。今の版のまま動いています。次に Windows にサインインしたときに、もう一度更新します。記録: {Updater.InstallLog}"
        : $"Meltype {version} に更新できませんでした。今の版のまま動いています。トレイの「更新」→「今すぐ更新を確認」から、もう一度更新できます。記録: {Updater.InstallLog}";

    private void OnUpdateBalloonClicked(object? sender, EventArgs e)
    {
        _tray.BalloonTipClicked -= OnUpdateBalloonClicked;
        ApplyUpdate();
    }

    /// <summary>
    /// 更新以外の通知を出す。BalloonTipClicked はどの通知のクリックでも起きるので、更新の通知のクリック待ちは外す
    /// (外さないと、この通知をクリックしたときに更新が始まる)。
    /// </summary>
    private void ShowBalloonTip(int timeout, string title, string text)
    {
        _tray.BalloonTipClicked -= OnUpdateBalloonClicked;
        _tray.ShowBalloonTip(timeout, title, text, ToolTipIcon.Info);
    }

    /// <summary>除外情報の表示を UI スレッドに予約し、その処理までに追加された複数の登録を一つのトレイ通知にまとめる。</summary>
    private void OnShortcutExcluded()
    {
        if (_exiting || !_invoker.IsHandleCreated || _invoker.IsDisposed ||
            Interlocked.Exchange(ref _exclusionNotificationScheduled, 1) != 0) return;
        try
        {
            _invoker.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _exclusionNotificationScheduled, 0);
                if (_exiting) return;
                var message = TakeShortcutExclusionMessage(_engine.Settings.ShowNotifications);
                if (message is null) return;
                _tray.BalloonTipClicked -= OnUpdateBalloonClicked;
                _tray.ShowBalloonTip(5000, "入力モード切り替えキーを除外しました", message, ToolTipIcon.Warning);
            });
        }
        catch (InvalidOperationException) when (_exiting)
        {
            // 終了と別スレッドからの除外通知が重なった場合は、破棄済みの UI を呼ばない。
        }
    }

    /// <summary>未通知の除外情報を消費し、通知が有効で対象があるときだけ本文を返す (OFF 中の情報も消費する)。</summary>
    internal static string? TakeShortcutExclusionMessage(bool showNotifications)
    {
        var excluded = Diagnostics.ShortcutExclusions.Drain();
        return showNotifications && excluded.Length > 0 ? ShortcutExclusionMessage(excluded) : null;
    }

    /// <summary>除外した登録の先頭 3 件をキー名で示し、残り件数とログ画面への案内を付けた通知本文を作る。</summary>
    internal static string ShortcutExclusionMessage(IReadOnlyList<Diagnostics.ShortcutExclusion> excluded)
    {
        var names = excluded.Take(3).Select(e => e.Shortcut is { } shortcut ? ShortcutEditor.Describe(shortcut) : "不正な登録 (null)");
        var remaining = excluded.Count > 3 ? $"、ほか {excluded.Count - 3} 件" : "";
        return $"{string.Join("、", names)}{remaining}\n詳細は「ログ / 判定理由」で確認できます。";
    }

    /// <summary>
    /// プレリリース版も更新の対象にするか (設定の「プレリリース版も自動で更新する」)。
    /// ダウンロード (Updater) と更新 (Staged・Apply) のどちらも、そのときのこの値で決める。
    /// </summary>
    private bool Prerelease => _engine.Settings.UpdatePrerelease;

    /// <summary>ダウンロード済みの新しい版があれば「更新して再起動」を出し、無ければ隠す。</summary>
    private void ShowUpdateItem()
    {
        var staged = Updater.Staged(Prerelease);
        _updateItem.Visible = staged is not null;
        if (staged is { } shown) _updateItem.Text = $"Meltype {shown.Version} に更新して再起動";
    }

    private void ApplyUpdate()
    {
        // インストーラーが Meltype を終了させてから入れ替え、新しい版を起動する。
        if (!Updater.Apply(Prerelease)) MessageBox.Show("更新を始められませんでした。ログを確認してください。", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async void CheckForUpdate()
    {
        var result = await _updater.CheckNowAsync();
        ShowUpdateItem();
        if (Updater.Staged(Prerelease) is { } staged)
        {
            if (MessageBox.Show($"Meltype {staged.Version} に更新できます。今すぐ更新しますか？ (Meltype が再起動します)", "Meltype の更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) ApplyUpdate();
            return;
        }
        MessageBox.Show(result, "Meltype の更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnToggleRequested()
    {
        if (_invoker.IsHandleCreated && !_invoker.IsDisposed) _invoker.BeginInvoke(ToggleEnabled);
    }

    private void OnKeyboardModeChanged()
    {
        if (_invoker.IsHandleCreated && !_invoker.IsDisposed) _invoker.BeginInvoke(UpdateStatus);
    }

    // MeltypeRuntime のタイマーのスレッドからも呼ばれる
    private void OnEngineSettingsChanged(Settings previous, Settings next)
    {
        try
        {
            _invoker.BeginInvoke(() =>
            {
                _engine.OnSettingsChanged(previous, next);
                UpdateStatus();
            });
        }
        catch (InvalidOperationException)
        {
            // 終了してハンドルを破棄した
        }
    }

    private void ToggleEnabled()
    {
        _engine.Enabled = !_engine.Enabled;
        // 設定で通知を OFF にしていれば出さない (Windows の通知は音も鳴る)。
        if (_engine.Settings.ShowNotifications) ShowBalloonTip(1500, "Meltype", _engine.Enabled ? "Meltype を有効にしました" : "Meltype を一時停止しました");
    }

    /// <summary>
    /// 不具合の報告の画面 (GitHub のアカウントが無くても報告できる)。実行環境とログを見せ、ログはコピーボタンで、フォームはボタンで開く。
    /// ログは URL に入れない (履歴に残る・長さの上限がある)。
    /// </summary>
    private void OpenReport()
    {
        if (_reportDialog is { IsDisposed: false })
        {
            _reportDialog.Activate();
            return;
        }
        using var dialog = new ReportDialog(_engine.Running);
        _reportDialog = dialog;
        dialog.FormClosed += (_, _) => _reportDialog = null;
        dialog.ShowDialog();
    }

    private static void ToggleStartup()
    {
        try
        {
            Startup.Set(!Startup.IsEnabled);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"変えられませんでした。\n\n{ex.Message}", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ToggleAutoUpdate()
    {
        var next = _engine.Settings.Clone();
        next.AutoUpdate = !next.AutoUpdate;
        _engine.ApplySettings(next);
        _autoUpdateItem.Checked = next.AutoUpdate;
    }

    private void FillProfiles()
    {
        _profileItem.DropDownItems.Clear();
        var settings = _engine.Settings;
        foreach (var name in settings.ProfileNames)
        {
            _profileItem.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => SwitchProfile(name)) { Checked = name == settings.ActiveProfile });
        }
        _profileItem.DropDownItems.Add(new ToolStripSeparator());
        _profileItem.DropDownItems.Add("プロファイルを足す・名前を変える (設定)...", null, (_, _) => ShowSettings());
    }

    private void SwitchProfile(string name)
    {
        if (name == _engine.Settings.ActiveProfile) return;
        _engine.ApplySettings(_engine.Settings.SwitchProfile(name));
        UpdateStatus();
        if (ProfileSwitchMessage(_engine.Settings) is { } message) ShowBalloonTip(1500, "Meltype", message);
    }

    /// <summary>除外警告が待機中なら通常の切替通知を省略し、同じ操作でバルーンを連続して表示しない。</summary>
    internal static string? ProfileSwitchMessage(Settings settings) =>
        settings.ShowNotifications && !Diagnostics.ShortcutExclusions.HasPending
            ? $"プロファイルを「{settings.ActiveProfile}」にしました" : null;

    private void ToggleAutoDetection()
    {
        var next = _engine.Settings.Clone();
        next.AutoDetection = !next.AutoDetection;
        _engine.ApplySettings(next);
        UpdateStatus();
    }

    private void SetMode(InputMode mode)
    {
        if (AppPaths.IsPortable && mode != InputMode.Keyboard) return;
        var wasTsf = _engine.Running.Mode == InputMode.Tsf;
        var next = _engine.Settings.Clone();
        next.Mode = mode;
        next.Enabled = true;
        _engine.ApplySettings(next);
        // ほかのモードからなら MeltypeEngine.OnSettingsChanged が切り替える。もう Meltype IME のモードで選び直したとき (Win + Space でほかの IME にしていた) も、入力方式を Meltype IME に戻す
        if (wasTsf && mode == InputMode.Tsf) Tip.TipServer.ActivateForSession();
    }

    private void UpdateStatus()
    {
        var settings = _engine.Running;
        var enabled = settings.Enabled;
        var keyboard = settings.Mode == InputMode.Keyboard;
        var tsf = settings.Mode == InputMode.Tsf;
        _enabledItem.Checked = enabled;
        _keyboardModeItem.Checked = keyboard;
        _tsfModeItem.Checked = tsf;
        // Meltype IME を使えず (ARM64 の Windows・登録されていない・キーボードの一覧に無い)、登録もできないなら選べない
        // (使えないまま選ぶとキーボードフックが止まり、どこでも何も起きなくなる。登録できるなら、選んだときに登録する: SelectTsf)
        var registered = Tip.TipServer.IsRegistered;
        _tsfModeItem.Enabled = tsf || Tip.TipServer.IsUsable || CanRegisterTsf;
        _autoDetectionItem.Checked = settings.AutoDetection;
        _profileItem.Text = $"プロファイル: {settings.ActiveProfile}";

        string status;
        if (!enabled)
        {
            _tray.Icon = _offIcon;
            status = "一時停止中";
        }
        else if (tsf)
        {
            _tray.Icon = _onIcon;
            status = registered ? "Meltype IME (Win + Space で Meltype を選んで入力)" : "Meltype IME が登録されていません (トレイの「Meltype IME」を選ぶと登録できます)";
        }
        else
        {
            _tray.Icon = _keyboard.Japanese ? _onIcon : _directIcon;
            status = _keyboard.Japanese ? "キーボード: 日本語" : "キーボード: 直接入力 (半角/全角 で日本語)";
        }
        _statusItem.Text = status;
        var zenzai = _composition.Runtime.ZenzaiStatus();
        _zenzaiItem.Visible = zenzai is not null;
        if (zenzai is not null) _zenzaiItem.Text = $"Zenzai: {zenzai}";
        var tip = $"Meltype — {status}";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_engine, _state);
        _settingsForm.FormClosed += (_, _) => UpdateStatus();
        _settingsForm.Show();
    }

    private void ShowKeyCount()
    {
        if (_keyCountForm is { IsDisposed: false })
        {
            _keyCountForm.Activate();
            return;
        }
        _keyCountForm = new KeyCountForm(_engine.KeyCounter);
        _keyCountForm.Show();
    }

    private void ShowLearnedWords()
    {
        if (_learnedForm is { IsDisposed: false })
        {
            _learnedForm.Activate();
            return;
        }
        _learnedForm = new LearnedWordsForm(_composition.Languages, _composition.History);
        _learnedForm.Show();
    }

    private void ShowUserDictionary()
    {
        if (_dictionaryForm is { IsDisposed: false })
        {
            _dictionaryForm.Activate();
            return;
        }
        _dictionaryForm = new UserDictionaryForm(_composition);
        _dictionaryForm.Show();
    }

    private void ShowSnippets()
    {
        if (_snippetsForm is { IsDisposed: false })
        {
            _snippetsForm.Activate();
            return;
        }
        _snippetsForm = new SnippetsForm(_composition.Snippets, () => _engine.Settings.SnippetMark);
        _snippetsForm.Show();
    }

    /// <summary>
    /// Ctrl+F7: 前面のアプリで選んでいる語を、ユーザー辞書の登録画面に入れて開く (読みは Microsoft IME で推測)。
    /// 選んでいる語は、Ctrl+C を送ってクリップボードから読む (クリップボードの元の中身は戻す)。
    /// </summary>
    private async void RegisterSelectedWord()
    {
        string? word = null;
        DataObject? saved = null;
        try
        {
            saved = Composition.CompositionService.CopyClipboard();
            Clipboard.Clear();
            Input.KeyInjector.SendShortcut(0x11, 0x43); // Ctrl+C
            for (var i = 0; i < 10 && !Clipboard.ContainsText(); i++) await Task.Delay(30);
            if (Clipboard.ContainsText()) word = Clipboard.GetText().Trim();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"選んでいる語を読めませんでした: {ex.Message}");
        }
        finally
        {
            try { if (saved is not null) Clipboard.SetDataObject(saved, copy: true); } catch { }
        }
        ShowUserDictionary();
        // 1 行で短いものだけ (文章を選んでいたら語は入れない)
        if (word is { Length: > 0 and <= 30 } && !word.Contains('\n')) _dictionaryForm!.Prefill(word, _composition.GuessReading(word));
    }

    private void ShowLog()
    {
        if (_logForm is { IsDisposed: false })
        {
            _logForm.Activate();
            return;
        }
        _logForm = new LogForm(_engine);
        _logForm.Show();
    }

    /// <summary>設定・学習データ・ユーザー辞書を 1 つのファイルに書き出す (PC の買い替え・別の PC への移行)。</summary>
    private static void CreateBackup()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Meltype のバックアップを書き出す",
            Filter = "Meltype のバックアップ (*.meltype-backup)|*.meltype-backup",
            FileName = $"Meltype-{DateTime.Now:yyyyMMdd}.meltype-backup",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            var (content, files) = Config.Backup.Create(AppPaths.DataDirectory, AppInfo.Version);
            File.WriteAllBytes(dialog.FileName, content);
            MessageBox.Show($"設定・学習データ・ユーザー辞書を書き出しました ({files} 個のファイル)。\n別の PC の Meltype で「バックアップ」→「バックアップから戻す...」を選ぶと戻せます。", "Meltype のバックアップ",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"書き出せませんでした。\n\n{ex.Message}", "Meltype のバックアップ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>バックアップから戻す。Meltype を起動し直し、新しい Meltype が戻してから読み込む。</summary>
    private void RestoreBackup()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Meltype のバックアップから戻す",
            Filter = "Meltype のバックアップ (*.meltype-backup)|*.meltype-backup|すべてのファイル (*.*)|*.*",
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            var content = File.ReadAllBytes(dialog.FileName);
            var (created, app, files) = Config.Backup.Inspect(content);
            var answer = MessageBox.Show($"{created} に作ったバックアップ (Meltype {app}、{files.Count} 個のファイル) で、今の設定・学習データ・ユーザー辞書を置き換えます。\nMeltype を起動し直します。よろしいですか？",
                "Meltype のバックアップ", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;
            // 新しい Meltype に渡す (この Meltype が終わってから戻す)
            var copy = Path.Combine(Path.GetTempPath(), $"meltype-restore-{Environment.ProcessId}.meltype-backup");
            File.WriteAllBytes(copy, content);
            var start = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false };
            start.ArgumentList.Add("--restore");
            start.ArgumentList.Add(copy);
            Process.Start(start);
            ExitThread();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"戻せませんでした。\n\n{ex.Message}", "Meltype のバックアップ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void OpenDataFolder()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDirectory}\"") { UseShellExecute = true });
    }

    private void ResetLearning()
    {
        var answer = MessageBox.Show("学習データ (選び直した変換 conversions.json、英字 / かなに直した語 languages.json、予測変換の語句 phrases.txt、選んだ英訳 translations.json) をすべて削除します。よろしいですか？\n\nユーザー辞書 (userdict.txt) と設定は消えません。", "Meltype", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.OK) _composition.Runtime.ResetLearning();
    }

    /// <summary>最近覚えたものだけを忘れる (<see cref="Composition.MeltypeRuntime.ForgetLearningSince"/>)。</summary>
    private void ForgetRecentLearning(DateTime sinceUtc, string label)
    {
        var answer = MessageBox.Show($"{label}に覚えた学習データ (選び直した変換・英字 / かなに直した語・予測変換の語句) を削除します。よろしいですか？", "Meltype", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        var count = _composition.Runtime.ForgetLearningSince(sinceUtc);
        Diagnostics.Log.Info($"{label}に覚えた学習データを削除しました ({count} 件)。");
        MessageBox.Show(count > 0 ? $"{count} 件削除しました。" : "削除するものはありませんでした。", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>インストーラーのアンインストーラーでアンインストールする (確認もアンインストーラーが出す)。</summary>
    private static void Uninstall()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "unins000.exe")) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"アンインストールを始められませんでした: {ex.Message}\nWindows の「設定」→「アプリ」→「インストールされているアプリ」からアンインストールしてください。", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        Diagnostics.ShortcutExclusions.Added -= OnShortcutExcluded;
        _engine.SettingsChanged -= OnEngineSettingsChanged;
        _engine.ToggleRequested -= OnToggleRequested;
        _tipServer.Dispose();
        _engine.Keyboard = null;
        _keyboard.Dispose();
        Composition.LearningSaves.FlushAll();
        _composition.Dispose();
        _settingsForm?.Close();
        _logForm?.Close();
        _dictionaryForm?.Close();
        _reportDialog?.Close();
        _learnedForm?.Close();
        _welcomeForm?.Close();
        _updater.Dispose();
        _hotkey.Dispose();
        _registerHotkey.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _invoker.Dispose();
        base.ExitThreadCore();
    }

    private static Icon CreateIcon(string text, Color background)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(background);
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Yu Gothic UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, Brushes.White, (32 - size.Width) / 2, (32 - size.Height) / 2 + 1);
        }
        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312, Id = 1;
        private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000, VK_F12 = 0x7B;
        private readonly Action _pressed;

        // 一時停止 / 再開。他のアプリと衝突したら順に次の候補を試す。
        public static readonly (uint Modifiers, uint Key, string Name)[] ToggleKeys =
        [
            (MOD_CONTROL | MOD_ALT, VK_F12, "Ctrl+Alt+F12"),
            (MOD_CONTROL | MOD_ALT, 0x7A, "Ctrl+Alt+F11"),
            (MOD_CONTROL | MOD_ALT | MOD_SHIFT, 0x41, "Ctrl+Shift+Alt+A"),
            (MOD_CONTROL | MOD_ALT, 0x13, "Ctrl+Alt+Pause"),
        ];

        // 選んでいる語をユーザー辞書に登録する (Microsoft IME の単語の登録と同じ Ctrl+F7)
        public static readonly (uint Modifiers, uint Key, string Name)[] RegisterWordKeys =
        [
            (MOD_CONTROL, 0x76, "Ctrl+F7"),
            (MOD_CONTROL | MOD_ALT, 0x76, "Ctrl+Alt+F7"),
        ];

        /// <summary>設定「一時停止 / 再開のホットキー」(Ctrl+Alt+F10 など) を先頭の候補にする。読めなければ組み込みの候補だけ。</summary>
        public static (uint Modifiers, uint Key, string Name)[] WithCustom(string? setting, (uint Modifiers, uint Key, string Name)[] defaults)
        {
            var custom = Meltype.Input.KeyShortcut.ParseList(setting).Take(1)
                .Select(shortcut => (
                    (shortcut.Ctrl ? MOD_CONTROL : 0u) | (shortcut.Alt ? MOD_ALT : 0u) | (shortcut.Shift ? MOD_SHIFT : 0u) | (shortcut.Win ? 0x0008u : 0u),
                    (uint)shortcut.Vk,
                    setting!.Trim()))
                .ToArray();
            return [.. custom, .. defaults];
        }

        public string? Name { get; }

        public HotkeyWindow(Action pressed, (uint Modifiers, uint Key, string Name)[]? candidates = null, string purpose = "一時停止/再開")
        {
            _pressed = pressed;
            CreateHandle(new CreateParams { Caption = "Meltype Hotkey" });
            foreach (var (modifiers, key, name) in candidates ?? ToggleKeys)
            {
                if (!RegisterHotKey(Handle, Id, modifiers | MOD_NOREPEAT, key)) continue;
                Name = name;
                Diagnostics.Log.Info($"{purpose}のホットキー: {name}");
                return;
            }
            Diagnostics.Log.Warn($"{purpose}のホットキーを登録できませんでした (候補がすべて他のアプリで使用中)。");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == Id) _pressed();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterHotKey(Handle, Id);
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
