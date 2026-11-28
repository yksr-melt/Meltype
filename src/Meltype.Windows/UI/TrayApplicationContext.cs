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
    private readonly HotkeyWindow _hotkey;
    private readonly HotkeyWindow _registerHotkey;
    private readonly Control _invoker = new();
    private readonly Icon _onIcon = CreateIcon("あ", Color.FromArgb(0, 120, 212));
    private readonly Icon _directIcon = CreateIcon("A", Color.FromArgb(0, 120, 212));
    private readonly Icon _offIcon = CreateIcon("A", Color.FromArgb(120, 120, 120));
    private readonly Composition.CompositionService _composition;
    private readonly Tip.TipServer _tipServer;
    // 更新前の Meltype IME が残っているアプリ (TipServer.ClientOutdated)
    private readonly HashSet<string> _outdatedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Keyboard.KeyboardFront _keyboard;
    private LogForm? _logForm;
    private ReportDialog? _reportDialog;
    private CorrectionReportDialog? _correctionDialog;
    private KeyCountForm? _keyCountForm;
    private WelcomeForm? _welcomeForm;
    private UpdateCheckForm? _updateCheckForm;
    private Task<string>? _updateCheck;
    private readonly Updater _updater;
    private int _exclusionNotificationScheduled;
    private volatile bool _exiting;

    public TrayApplicationContext(MeltypeEngine engine, AppState state)
    {
        _engine = engine;
        _state = state;
        _invoker.CreateControl();
        // 入力・表示は、動かしている設定 (Meltype IME を使えなければ Meltype キーボード) を読む
        _composition = new Composition.CompositionService(_invoker, _engine.Runtime, () => _engine.Running);
        _keyboard = new Keyboard.KeyboardFront(_invoker, _composition, environment => _composition.CreateHost(environment, () => _engine.Running), () => _engine.OtherTipVersion);
        _keyboard.ModeChanged += OnKeyboardModeChanged;
        _engine.Keyboard = _keyboard;
        _engine.Focus = _composition.Focus;
        _composition.Focus.TextInputEntered += _engine.OnTextInputEntered;
        _tipServer = new Tip.TipServer(_invoker, _composition, environment => _composition.CreateHost(environment, () => _engine.Running));
        _engine.ImeServer = _tipServer;
        _tipServer.ClientOutdated += process =>
        {
            // DLL は動作モードが Meltype キーボードのときも接続してくる
            if (_engine.Running.Mode != InputMode.Tsf) return;
            // 通知は見逃すことがあるので、そのアプリで打っている間 (同じ接続から別ビルドの要求が届くたび、3 秒おき) は、打っている所にも出す。
            // 古い DLL が残っているかは届いた要求で分かるので、アプリを再起動して直れば、覚えておく状態なしに出なくなる。
            // エクスプローラーのように単体では再起動しにくいアプリもあるので、アプリではなく PC の再起動を求める (#1919)
            _composition.ShowNotice($"PC を再起動してください (「{process}」に前の版の Meltype IME が残っていて、入力できません)", 3000);
            // ログはアプリごとに 1 回
            if (!_outdatedApps.Add(process)) return;
            Diagnostics.Log.Warn($"Meltype IME: {process} に、更新前の Meltype IME が残っています。{process} を再起動するまで、Meltype IME で入力できません。");
            // 更新前から開いているアプリはすべて同じなので、アプリごとではなく、PC の再起動を 1 回だけ求める
            // (設定「通知を表示」にかかわらず。入力を止めないように、ダイアログではなく通知にする)
            if (_outdatedApps.Count == 1)
                ShowBalloonTip(10000, "Meltype IME", $"更新前から開いているアプリ (「{process}」など) は、PC を再起動するまで Meltype IME で入力できません。PC を再起動してください。");
        };
        _tipServer.Start();
        _engine.SettingsChanged += OnEngineSettingsChanged;
        IntroduceTsf();

        // メニューは出さず、左右どちらのクリックでも設定アプリを開く。一時停止は Ctrl + 半角/全角 とホットキー、終了は設定アプリで行う
        _tray = new NotifyIcon { Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right) SettingsApp.Show();
        };
        // 状態の表示 (Zenzai の状態は時間で変わる) は、アイコンに乗せたときに作り直す
        _tray.MouseMove += (_, _) => UpdateStatus();
        _hotkey = new HotkeyWindow(ToggleEnabled, HotkeyWindow.WithCustom(_engine.Settings.PauseHotkey, HotkeyWindow.ToggleKeys));
        _registerHotkey = new HotkeyWindow(RegisterSelectedWord, HotkeyWindow.RegisterWordKeys, "単語の登録");
        _engine.ToggleRequested += OnToggleRequested;
        _updater = new Updater(() => !AppPaths.IsPortable && _engine.Settings.AutoUpdate, () => Prerelease);
        _updater.Ready += OnUpdateReady;
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
        // 設定で自動表示を OFF にしていれば見せない
        if (TakeReleaseNotes(_state, AppInfo.Version, firstRun, _engine.Settings.ShowReleaseNotesAfterUpdate))
            _invoker.BeginInvoke(() => ReleaseNotesDialog.ShowFor(AppInfo.Version, updated: true, _engine));
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
        // 起動中の IntroduceTsf で Meltype IME を使えなかったときは、上で聞く。ここより前に受け取り始めると 2 回聞く
        _engine.TsfUnavailable += OnTsfUnavailable;
        // 起動時の設定反映が終わってから、読み込み中に蓄積した除外をまとめて通知する。
        Diagnostics.ShortcutExclusions.Added += OnShortcutExcluded;
        OnShortcutExcluded();
    }

    /// <summary>
    /// Meltype IME (TSF) を入れたら、一度だけ動作モードを Meltype IME にする
    /// (PC に登録されていても、このユーザーのキーボードの一覧に無ければ Win + Space で選べないので切り替えない)
    /// </summary>
    private void IntroduceTsf()
    {
        if (AppPaths.IsPortable || _state.TsfIntroduced || !Tip.TipServer.IsRegistered || !Tip.TipServer.IsInUserLanguageList) return;
        var wasTsf = _engine.Running.Mode == InputMode.Tsf;
        _state.TsfIntroduced = true;
        _state.Save();
        _engine.EditSettings(settings => settings.Mode = InputMode.Tsf);
        // ほかのモードからなら MeltypeEngine.OnSettingsChanged が切り替える。もう Meltype IME のモードだったときも、入力方式を Meltype IME にする
        if (wasTsf) Tip.TipServer.ActivateForSession();
        Diagnostics.Log.Info("Meltype IME が入っているので、動作モードを Meltype IME にしました。");
    }

    /// <summary>Meltype IME をインストール先の setup-ime.ps1 で登録できるか (ポータブル版・tip フォルダーの無い Meltype.exe では登録できない)。</summary>
    private static bool CanRegisterTsf => !AppPaths.IsPortable &&
        File.Exists(Path.Combine(AppContext.BaseDirectory, "setup-ime.ps1")) && Directory.Exists(Path.Combine(AppContext.BaseDirectory, "tip"));

    /// <summary>Meltype IME を登録するかを聞き、使えるようになったら動作モードを Meltype IME にする。</summary>
    private async void EnableTsf(string question)
    {
        if (!await OfferRegisterTsf(question)) return;
        // 次に起動したとき IntroduceTsf で切り替え直さないように、切り替えたことも残す (あとでユーザーが戻したら、そのまま)
        _state.TsfIntroduced = true;
        _state.Save();
        SwitchToTsf();
        UpdateStatus();
    }

    /// <summary>登録済みの Meltype IME を、この Meltype.exe に同梱のものに更新するかを聞き、更新できたら Meltype IME で動かし直す。</summary>
    private async void OfferTsfUpdate()
    {
        if (!await OfferRegisterTsf("Meltype IME を更新しますか？\n\n" +
            "Windows に登録されている Meltype IME が、ほかの版の Meltype のものです。更新するまでは、Meltype キーボード (変換ボックスで入力) で動きます。")) return;
        // 動かす動作モード (MeltypeEngine.Running) を決め直す
        _engine.EditSettings(_ => { });
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

    /// <summary>新しい版をダウンロードし終えた: 通知する (クリックで今すぐ更新)。</summary>
    private void OnUpdateReady(string version)
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return;
        _invoker.BeginInvoke(() =>
        {
            if (!_engine.Settings.ShowNotifications) return;
            _tray.BalloonTipClicked -= OnUpdateBalloonClicked;
            _tray.BalloonTipClicked += OnUpdateBalloonClicked;
            _tray.ShowBalloonTip(5000, "Meltype の更新", UpdateReadyMessage(version, _engine.Settings.AutoUpdate), ToolTipIcon.Info);
        });
    }

    /// <summary>自動更新が OFF なら、次のサインインでは更新しない (Updater.ApplyStagedAtStartup)。設定アプリから更新できると案内する。</summary>
    internal static string UpdateReadyMessage(string version, bool autoUpdate) => autoUpdate
        ? $"Meltype {version} を準備しました。次に Windows にサインインしたときに更新します。ここをクリックすると今すぐ更新します。"
        : $"Meltype {version} を準備しました。設定の「更新」→「今すぐ更新を確認」で更新できます。ここをクリックすると今すぐ更新します。";

    /// <summary>
    /// 自動更新が ON なら、Updater がダウンロードし直し、次のサインインでもう一度更新する。
    /// Meltype IME を更新できなかったとき (UAC を断った) も、インストーラーが本体を入れ替えずに止めるので、この通知になる。
    /// </summary>
    internal static string UpdateFailedMessage(string version, bool autoUpdate) => autoUpdate
        ? $"Meltype {version} に更新できませんでした。今の版のまま動いています。次に Windows にサインインしたときに、もう一度更新します。記録: {Updater.InstallLog}"
        : $"Meltype {version} に更新できませんでした。今の版のまま動いています。設定の「更新」→「今すぐ更新を確認」から、もう一度更新できます。記録: {Updater.InstallLog}";

    /// <summary>
    /// 更新したあとの初めての起動で、その版のリリースノートを出すか決めて、見せた版を state.json に残す (issue #1207)。
    /// 設定で自動表示を OFF にしていても、初めて起動した人でも、見せたことにしておく (あとで ON に戻しても、その版のは自動では出さない)。
    /// </summary>
    internal static bool TakeReleaseNotes(AppState state, string version, bool firstRun, bool autoShow)
    {
        if (state.ReleaseNotesShown == version) return false;
        var show = ProjectInfo.ShouldShowReleaseNotes(state.ReleaseNotesShown, version, firstRun, autoShow);
        state.ReleaseNotesShown = version;
        state.Save();
        return show;
    }

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
        var names = excluded.Take(3).Select(e => e.Shortcut is { } shortcut ? shortcut.Describe() : "不正な登録 (null)");
        var remaining = excluded.Count > 3 ? $"、ほか {excluded.Count - 3} 件" : "";
        return $"{string.Join("、", names)}{remaining}\n詳細は「Meltype」の「打鍵数・ログ」→「ログ / 判定理由」で確認できます。";
    }

    /// <summary>
    /// プレリリース版も更新の対象にするか (設定の「プレリリース版も自動で更新する」)。
    /// ダウンロード (Updater) と更新 (Staged・Apply) のどちらも、そのときのこの値で決める。
    /// </summary>
    private bool Prerelease => _engine.Settings.UpdatePrerelease;

    private void ApplyUpdate()
    {
        // インストーラーが Meltype を終了させてから入れ替え、新しい版を起動する。
        if (!Updater.Apply(Prerelease)) MessageBox.Show("更新を始められませんでした。ログを確認してください。", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async void CheckForUpdate()
    {
        // 確認している間にもう一度押されたら、確認し直さずに窓を前に出す
        if (_updateCheckForm is { IsDisposed: false })
        {
            _updateCheckForm.Activate();
            return;
        }
        _updateCheckForm = new UpdateCheckForm();
        _updateCheckForm.Show();
        // 窓を閉じてから押し直したときは、続いている確認を待つ (Updater は確認を 1 つずつしか動かさない。結果は最初に押した方で出す)
        if (_updateCheck is { IsCompleted: false }) return;
        _updateCheck = _updater.CheckNowAsync();
        string result;
        try
        {
            result = await _updateCheck;
        }
        finally
        {
            _updateCheckForm.Close();
        }
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

    // 設定アプリ・config.json の書き換えで動作モードを Meltype IME にしたが、Windows に登録されていない。MeltypeRuntime のタイマーのスレッドからも呼ばれる
    private void OnTsfUnavailable()
    {
        if (!CanRegisterTsf) return;
        try
        {
            _invoker.BeginInvoke(() => EnableTsf("Meltype IME を Windows に登録しますか？\n\n" +
                "Meltype IME は Windows の IME として、打った文字を入力欄に直接入れます。登録するまでは Meltype キーボード (変換ボックスで入力) で動きます。"));
        }
        catch (InvalidOperationException)
        {
            // 終了してハンドルを破棄した
        }
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

    /// <summary>設定アプリから頼まれた画面を出す (AppSignal。UI スレッド)。</summary>
    public void Open(AppWindow window)
    {
        // 合図の WndProc の中でダイアログを出すと、閉じるまで次の合図を受け取れないので、メッセージループに戻ってから出す
        _invoker.BeginInvoke(() =>
        {
            switch (window)
            {
                case AppWindow.Guide: ShowWelcome(); break;
                case AppWindow.Report: OpenReport(); break;
                case AppWindow.Corrections: ShowCorrections(); break;
                case AppWindow.Log: ShowLog(); break;
                case AppWindow.KeyCount: ShowKeyCount(); break;
                case AppWindow.ReleaseNotes: ReleaseNotesDialog.ShowFor(AppInfo.Version, updated: false, _engine); break;
                case AppWindow.CheckForUpdate: CheckForUpdate(); break;
                default: throw new ArgumentOutOfRangeException(nameof(window), window, null);
            }
        });
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

    private void ShowCorrections()
    {
        if (_correctionDialog is { IsDisposed: false })
        {
            _correctionDialog.Activate();
            return;
        }
        _correctionDialog = new CorrectionReportDialog(_composition.Corrections, _engine.Running);
        _correctionDialog.Show();
    }

    /// <summary>動作モードを Meltype IME にする。</summary>
    private void SwitchToTsf()
    {
        var wasTsf = _engine.Running.Mode == InputMode.Tsf;
        _engine.EditSettings(settings =>
        {
            settings.Mode = InputMode.Tsf;
            settings.Enabled = true;
        });
        // ほかのモードからなら MeltypeEngine.OnSettingsChanged が切り替える。もう Meltype IME のモードで選び直したとき (Win + Space でほかの IME にしていた) も、入力方式を Meltype IME に戻す
        if (wasTsf) Tip.TipServer.ActivateForSession();
    }

    private void UpdateStatus()
    {
        var settings = _engine.Running;
        var enabled = settings.Enabled;
        var tsf = settings.Mode == InputMode.Tsf;

        string status;
        if (!enabled)
        {
            _tray.Icon = _offIcon;
            status = "一時停止中";
        }
        else if (tsf)
        {
            _tray.Icon = _onIcon;
            status = Tip.TipServer.IsRegistered ? "Meltype IME (Win + Space で Meltype を選んで入力)" : "Meltype IME が登録されていません (「Meltype」の動作モードで Meltype IME を選び直すと登録できます)";
        }
        else
        {
            _tray.Icon = _keyboard.Japanese ? _onIcon : _directIcon;
            status = _keyboard.Japanese ? "キーボード: 日本語" : "キーボード: 直接入力 (半角/全角 で日本語)";
        }
        // Zenzai の状態は「Zenzai を使う」が ON のときだけ出す (issue #1152)
        var zenzai = _composition.Runtime.ZenzaiStatus() is { } running ? $"\nZenzai: {running}" : "";
        // NotifyIcon.Text は 127 文字まで
        var tip = $"Meltype — {status}{zenzai}";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    private void ShowKeyCount()
    {
        if (_keyCountForm is { IsDisposed: false })
        {
            _keyCountForm.Activate();
            return;
        }
        _keyCountForm = new KeyCountForm(_engine);
        _keyCountForm.Show();
    }

    /// <summary>
    /// Ctrl+F7: 前面のアプリで選んでいる語を、設定アプリのユーザー辞書の登録欄に入れて開く (読みは変換エンジンで推測)。
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
        // 1 行で短いものだけ (文章を選んでいたら語は入れない)
        if (word is { Length: > 0 and <= 30 } && !word.Contains('\n')) SettingsApp.Open("--add-word", word, _composition.Runtime.GuessReading(word));
        else SettingsApp.Open("--add-word");
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

    protected override void ExitThreadCore()
    {
        _exiting = true;
        Diagnostics.ShortcutExclusions.Added -= OnShortcutExcluded;
        _engine.SettingsChanged -= OnEngineSettingsChanged;
        _engine.TsfUnavailable -= OnTsfUnavailable;
        _engine.ToggleRequested -= OnToggleRequested;
        _tipServer.Dispose();
        _engine.Keyboard = null;
        _keyboard.Dispose();
        Config.LearningSaves.FlushAll();
        _composition.Dispose();
        _logForm?.Close();
        _reportDialog?.Close();
        _correctionDialog?.Close();
        _welcomeForm?.Close();
        _updateCheckForm?.Close();
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
