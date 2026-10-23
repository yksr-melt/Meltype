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
    private static readonly Meltype.Composition.GitHubIssueTitles GitHubTitlesShared = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly HotkeyWindow _hotkey;
    private readonly HotkeyWindow _registerHotkey;
    private readonly Control _invoker = new();
    private readonly Icon _onIcon = CreateIcon("あ", Color.FromArgb(0, 120, 212));
    private readonly Icon _directIcon = CreateIcon("A", Color.FromArgb(0, 120, 212));
    private readonly Icon _offIcon = CreateIcon("A", Color.FromArgb(120, 120, 120));
    private readonly ToolStripMenuItem _keyboardModeItem;
    private readonly ToolStripMenuItem _levelItem;
    private readonly ToolStripMenuItem _profileItem;
    private readonly Composition.CompositionService _composition;
    private readonly Tip.TipServer _tipServer;
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

    public TrayApplicationContext(MeltypeEngine engine)
    {
        _engine = engine;
        _invoker.CreateControl();
        var detector = Composition.CompositionDetector.CreateDefault(AppPaths.UserDictionaryDirectory);
        detector.SpellChecker = Detection.WindowsSpellChecker.Shared;
        detector.UseScoredSegmentation = () => _engine.AppSettings.ScoredSegmentation;
        detector.UseJapanesePunctuationAfterEnglish = () => _engine.Settings.JapanesePunctuationAfterEnglish;
        _composition = new Composition.CompositionService(_invoker, detector, new Composition.CompositionOptions
        {
            LiveConversion = () => _engine.AppSettings.LiveConversion,
            DirectMode = () => _engine.KeyboardDirect,
            ClassifyDirect = _engine.ClassifyDirect,
            DirectDecided = _engine.OnDirectDecided,
            AutoCorrect = () => _engine.Settings.AutoCorrectAfterCommit && _engine.AppSettings.DetectionLevel != DetectionLevel.Manual,
            Level = () => _engine.AppSettings.DetectionLevel,
            Engine = () => _engine.Settings.ConversionEngine,
            UseZenzai = () => _engine.Settings.UseZenzai,
            ZenzaiServerPath = () => _engine.Settings.ZenzaiServerPath,
            ZenzaiModelPath = () => _engine.Settings.ZenzaiModelPath,
            TranslationCandidates = () => _engine.Settings.TranslationCandidates,
            CandidateMeanings = () => _engine.Settings.ShowCandidateMeanings,
            ShowTypedKeys = () => _engine.Settings.ShowTypedKeys,
            TabConversion = () => _engine.Settings.TabConversion,
            ContinueAfterConversion = () => _engine.Settings.ContinueAfterConversion,
            Learning = () => _engine.Settings.LearningEnabled,
            SeparateKeyRoles = () => _engine.Settings.SeparateKeyRoles,
            EnglishFirstInput = () => _engine.Settings.EnglishFirstInput,
            ConsonantAbbreviations = () => _engine.Settings.ConsonantAbbreviations,
            CorrectTypos = () => _engine.Settings.CorrectTypos,
            SlashAsMiddleDot = () => _engine.Settings.SlashAsMiddleDot,
            SpaceAroundEnglish = () => _engine.Settings.SpaceAroundEnglish,
            ShiftSpaceHalfWidth = () => _engine.Settings.ShiftSpaceHalfWidth,
            EnterCommitsAsShown = () => _engine.Settings.EnterCommitsAsShown,
            AlphanumericKeyStartsAsTyped = () => _engine.Settings.AlphanumericKeyStartsAsTyped,
            FullWidthParentheses = () => _engine.Settings.FullWidthParentheses,
            FullWidthSlash = () => _engine.Settings.FullWidthSlash,
            SquareBrackets = () => _engine.Settings.SquareBrackets,
            EscStepsBack = () => _engine.Settings.EscStepsBack,
            EscRevertsSelectedClause = () => _engine.Settings.EscRevertsSelectedClause,
            FunctionKeysAffectSelectedClause = () => _engine.Settings.FunctionKeysAffectSelectedClause,
            CompatibleShortcuts = () => _engine.Settings.CompatibleShortcuts,
            PartialCommit = () => _engine.Settings.PartialCommit,
            MacControlKeys = () => _engine.Settings.MacControlKeys,
            CursorIntoBrackets = () => _engine.Settings.CursorIntoBrackets,
            ControlKeyBindings = () => _engine.Settings.ControlKeyBindings,
            CalculatorCandidates = () => _engine.Settings.CalculatorCandidates,
            LogKeyIntervals = () => _engine.Settings.LogKeyIntervals,
            LaughterAfterKana = () => _engine.Settings.LaughterAfterKana,
            ContextualLearning = () => _engine.Settings.ContextualLearning,
            FullWidthSpaceByDefault = () => _engine.Settings.FullWidthSpaceByDefault,
            ShowCandidatesWhileTyping = () => _engine.Settings.ShowCandidatesWhileTyping,
            GitHubIssueCandidates = () => _engine.Settings.GitHubIssueCandidates,
            GitHubRepository = () => _engine.Settings.GitHubRepository,
            GitHubTitles = () => GitHubTitlesShared,
            ClipboardCandidates = () => _engine.Settings.ClipboardCandidates,
            Punctuation = () => _engine.Settings.Punctuation,
            KanaInput = () => _engine.Settings.InputStyle == InputStyle.Kana,
            ModeIndicator = () => _engine.Settings is { Enabled: true, Mode: InputMode.Keyboard, ShowModeIndicator: true },
            ModeIndicatorOnFocus = () => _engine.Settings.ShowModeIndicatorOnFocus,
            ModeIndicatorColors = () => (_engine.Settings.ModeIndicatorJapaneseColor, _engine.Settings.ModeIndicatorDirectColor, _engine.Settings.ModeIndicatorTextColor),
            Placement = () => _engine.Settings.CompositionPlacement,
            Size = () => _engine.Settings.CompositionSize,
            Predictions = () => _engine.Settings.PredictiveCandidates,
            EnglishNamePredictions = () => _engine.Settings.EnglishNamePredictions,
            Font = () => _engine.Settings.CompositionFont,
            LightTheme = () => _engine.Settings.CompositionIsLight(Meltype.Composition.CompositionWindow.WindowsUsesLightTheme()),
            Opacity = () => _engine.Settings.CompositionOpacityValue,
        });
        _engine.AttachComposition(_composition);
        _tipServer = new Tip.TipServer(_invoker, _composition, () => _engine.Settings);
        _tipServer.Start();
        IntroduceTsf();

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem { Enabled = false };
        _enabledItem = new ToolStripMenuItem("Meltype を有効にする", null, (_, _) => ToggleEnabled()) { CheckOnClick = false };
        _keyboardModeItem = new ToolStripMenuItem("Meltype キーボード (変換ボックスで入力)", null, (_, _) => SetMode(InputMode.Keyboard));
        _tsfModeItem = new ToolStripMenuItem("Meltype IME (入力欄に直接入力)", null, (_, _) => SetMode(InputMode.Tsf));
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        if (!AppPaths.IsPortable) menu.Items.Add(_tsfModeItem);
        menu.Items.Add(_keyboardModeItem);
        // 自動判定の強さ (積極的 / 標準 / 慎重 / 手動)
        _levelItem = new ToolStripMenuItem("自動判定の強さ");
        foreach (var level in Enum.GetValues<DetectionLevel>())
        {
            _levelItem.DropDownItems.Add(new ToolStripMenuItem(LevelName(level), null, (_, _) => SetLevel(level)) { Tag = level });
        }
        menu.Items.Add(_levelItem);
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
        menu.Items.Add("直した誤判定を報告...", null, (_, _) => new CorrectionReportDialog(_composition.Corrections, _engine.Settings).Show());
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
        _updateItem = new ToolStripMenuItem("", null, (_, _) => ApplyUpdate()) { Visible = false };
        updates.DropDownItems.Add(_updateItem);
        updates.DropDownOpening += (_, _) => _autoUpdateItem.Checked = _engine.Settings.AutoUpdate;
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
        _engine.StatusChanged += OnEngineStatusChanged;
        _updater = new Updater(() => !AppPaths.IsPortable && _engine.Settings.AutoUpdate);
        _updater.Ready += OnUpdateReady;
        ShowUpdateItem();
        UpdateStatus();
        // 初めて起動したら使い方を見せる (見せたことは設定に残す)
        var firstRun = !_engine.Settings.WelcomeShown;
        if (firstRun)
        {
            var next = _engine.Settings.Clone();
            next.WelcomeShown = true;
            _engine.ApplySettings(next);
            _invoker.BeginInvoke(ShowWelcome);
        }
        // Meltype IME が入る前の版から更新した人に、一度だけ Meltype IME を入れるかを聞く
        // (初めて起動した人には、インストーラーが聞いている)
        if (!AppPaths.IsPortable && !_engine.Settings.TsfOffered)
        {
            var next = _engine.Settings.Clone();
            next.TsfOffered = true;
            _engine.ApplySettings(next);
            if (!firstRun && CanOfferTsf()) _invoker.BeginInvoke(OfferTsf);
        }
    }

    /// <summary>
    /// Meltype IME (TSF) を入れたら、一度だけ動作モードを Meltype IME にする
    /// (PC に登録されていても、このユーザーのキーボードの一覧に無ければ Win + Space で選べないので切り替えない)
    /// (ARM64 の Windows には Meltype IME は対応していないので切り替えない)
    /// </summary>
    private void IntroduceTsf()
    {
        if (AppPaths.IsPortable || _engine.Settings.TsfIntroduced || !Tip.TipServer.IsRegistered || !Tip.TipServer.IsInUserLanguageList ||
            RuntimeInformation.OSArchitecture == Architecture.Arm64) return;
        var wasTsf = _engine.Settings.Mode == InputMode.Tsf;
        var next = _engine.Settings.Clone();
        next.TsfIntroduced = true;
        next.Mode = InputMode.Tsf;
        _engine.ApplySettings(next);
        // ほかのモードからなら ApplySettings が切り替える。もう Meltype IME のモードだったときも、入力方式を Meltype IME にする
        if (wasTsf) Tip.TipServer.ActivateForSession();
        Diagnostics.Log.Info("Meltype IME が入っているので、動作モードを Meltype IME にしました。");
    }

    /// <summary>
    /// Meltype IME を入れるかを聞けるか。Meltype IME を使ったことがない・登録されていない・インストール先に meltype-ime.ps1 と DLL がある・
    /// 前に断っていない (UAC で断ったときと、インストーラーでチェックを外したときに HKCU\Software\Meltype の ImeDeclined に残す) とき。
    /// </summary>
    private bool CanOfferTsf()
    {
        if (_engine.Settings.TsfIntroduced || !Tip.TipServer.IsKnownUnregistered || RuntimeInformation.OSArchitecture == Architecture.Arm64) return false;
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "meltype-ime.ps1")) || !Directory.Exists(Path.Combine(AppContext.BaseDirectory, "tip"))) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Meltype");
            return key?.GetValue("ImeDeclined") is not 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Meltype IME を入れるかを聞き、「はい」なら meltype-ime.ps1 で登録して、動作モードを Meltype IME にする。</summary>
    private async void OfferTsf()
    {
        // 起動したときに出すので、ほかのウィンドウの後ろに隠れないように最前面にする
        using var owner = new Form { TopMost = true };
        var answer = MessageBox.Show(owner,
            "Meltype IME を入れますか？\n\n" +
            "Meltype IME は Windows の IME として、打った文字を入力欄に直接入れます。変換ボックスが出ないアプリでも入力できます。\n\n" +
            "入れるときに管理者権限の確認が出ます。「いいえ」なら、今の動作モードのまま使えます。",
            "Meltype IME", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        var directory = AppContext.BaseDirectory;
        var quote = (string text) => "'" + text.Replace("'", "''") + "'";
        var start = new ProcessStartInfo(Updater.PowerShell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
            $". {quote(Path.Combine(directory, "meltype-ime.ps1"))}; Install-MeltypeIme -Source {quote(Path.Combine(directory, "tip"))} -Ask | Out-Null" })
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
        // 登録できても、キーボードの一覧に足せなければ Win + Space で選べない
        if (!Tip.TipServer.IsRegistered || !Tip.TipServer.IsInUserLanguageList)
        {
            MessageBox.Show("Meltype IME を使えるようにできませんでした。今の動作モードのまま使えます。", "Meltype IME", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        // 次に起動したとき IntroduceTsf で切り替え直さないように、切り替えたことも残す (あとでユーザーが戻したら、そのまま)
        var next = _engine.Settings.Clone();
        next.TsfIntroduced = true;
        _engine.ApplySettings(next);
        SetMode(InputMode.Tsf);
        UpdateStatus();
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

    private void ShowUpdateItem()
    {
        if (Updater.Staged() is not { } staged) return;
        _updateItem.Text = $"Meltype {staged.Version} に更新して再起動";
        _updateItem.Visible = true;
    }

    private bool _applyingUpdate;

    private async void ApplyUpdate()
    {
        // 新しい版の判定を待っている間に、メニューや通知からもう一度押されても始めない
        if (_applyingUpdate) return;
        _applyingUpdate = true;
        try
        {
            // 更新の前に、あなたの打ち方で変わる語があれば見せる (issue #285)。
            // 判定は新しい版の Meltype.exe を起動して待つので、UI スレッドの外で行う (待つ間も Meltype IME に応答するため)
            if (Updater.Staged() is { } staged)
            {
                var words = _composition.Languages.Entries().Select(e => e.Word).Distinct().ToList();
                var preview = await Task.Run(() => Updater.PreviewChanges(words));
                if (!UpdateChangesDialog.Confirm(_composition.Languages, staged.Version, _engine.Settings, preview)) return;
            }
            // install.ps1 が Meltype を終了させてから入れ替え、新しい版を起動する。
            if (!Updater.Apply()) MessageBox.Show("更新を始められませんでした。ログを確認してください。", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _applyingUpdate = false;
        }
    }

    private async void CheckForUpdate()
    {
        var result = await _updater.CheckNowAsync();
        ShowUpdateItem();
        if (Updater.Staged() is { } staged)
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

    private void OnEngineStatusChanged()
    {
        if (_invoker.IsHandleCreated && !_invoker.IsDisposed) _invoker.BeginInvoke(UpdateStatus);
    }

    private void ToggleEnabled()
    {
        _engine.Enabled = !_engine.Enabled;
        // 設定で通知を OFF にしていれば出さない (Windows の通知は音も鳴る)。
        if (_engine.Settings.ShowNotifications) ShowBalloonTip(1500, "Meltype", _engine.Enabled ? "Meltype を有効にしました" : "Meltype を一時停止しました");
    }

    private static string LevelName(DetectionLevel level) => level switch
    {
        DetectionLevel.Aggressive => "積極的 (英語らしければすぐ英字)",
        DetectionLevel.Balanced => "標準",
        DetectionLevel.Conservative => "慎重 (確信度が高いときだけ英字)",
        _ => "手動 (提案のみ・Tab で英字)",
    };

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
        using var dialog = new ReportDialog(_engine.Settings);
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
        if (_engine.Settings.ShowNotifications) ShowBalloonTip(1500, "Meltype", $"プロファイルを「{name}」にしました");
    }

    private void SetLevel(DetectionLevel level)
    {
        var next = _engine.Settings.Clone();
        next.DetectionLevel = level;
        _engine.ApplySettings(next);
        UpdateStatus();
    }

    private void SetMode(InputMode mode)
    {
        if (AppPaths.IsPortable && mode != InputMode.Keyboard) return;
        var wasTsf = _engine.Settings.Mode == InputMode.Tsf;
        var next = _engine.Settings.Clone();
        next.Mode = mode;
        next.Enabled = true;
        _engine.ApplySettings(next);
        // ほかのモードからなら ApplySettings が切り替える。もう Meltype IME のモードで選び直したとき (Win + Space でほかの IME にしていた) も、入力方式を Meltype IME に戻す
        if (wasTsf && mode == InputMode.Tsf) Tip.TipServer.ActivateForSession();
    }

    private void UpdateStatus()
    {
        var settings = _engine.Settings;
        var enabled = settings.Enabled;
        var keyboard = settings.Mode == InputMode.Keyboard;
        var tsf = settings.Mode == InputMode.Tsf;
        _enabledItem.Checked = enabled;
        _keyboardModeItem.Checked = keyboard;
        _tsfModeItem.Checked = tsf;
        // Meltype IME を使えない (ARM64 の Windows・登録されていない・キーボードの一覧に無い) なら選べない
        // (選ぶとキーボードフックが止まり、どこでも何も起きなくなる)
        var registered = Tip.TipServer.IsRegistered;
        _tsfModeItem.Enabled = tsf || Tip.TipServer.IsUsable;
        foreach (ToolStripMenuItem item in _levelItem.DropDownItems) item.Checked = item.Tag is DetectionLevel level && level == settings.DetectionLevel;
        _levelItem.Text = $"自動判定の強さ: {LevelName(settings.DetectionLevel).Split(' ')[0]}";
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
            status = registered ? "Meltype IME (Win + Space で Meltype を選んで入力)" : "Meltype IME が登録されていません (Install.cmd か Install-Meltype.ps1 で入れるか、別の動作モードを選んでください)";
        }
        else
        {
            _tray.Icon = _engine.KeyboardDirect ? _directIcon : _onIcon;
            status = _engine.KeyboardDirect ? "キーボード: 直接入力 (半角/全角 で日本語)" : "キーボード: 日本語";
        }
        _statusItem.Text = status;
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
        _settingsForm = new SettingsForm(_engine);
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
    private void CreateBackup()
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
        if (answer == DialogResult.OK)
        {
            _composition.History.Clear();
            _composition.Languages.Clear();
            _composition.Phrases?.Clear();
            _composition.TranslationHistory?.Clear();
            Diagnostics.Log.Info("学習データをリセットしました。");
        }
    }

    /// <summary>
    /// 最近覚えたものだけを忘れる (一時的な入力・間違えて覚えたものを、全体をリセットせずに消す: issue #277)。
    /// 選び直した変換・英字 / かなに直した語・予測変換の語句が対象。英訳の記録は覚えた時刻を持たないので、「すべて」で消す。
    /// </summary>
    private void ForgetRecentLearning(DateTime sinceUtc, string label)
    {
        var answer = MessageBox.Show($"{label}に覚えた学習データ (選び直した変換・英字 / かなに直した語・予測変換の語句) を削除します。よろしいですか？", "Meltype", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        var count = _composition.History.ForgetSince(sinceUtc) + _composition.Languages.ForgetSince(sinceUtc) + (_composition.Phrases?.ForgetSince(sinceUtc) ?? 0);
        Diagnostics.Log.Info($"{label}に覚えた学習データを削除しました ({count} 件)。");
        MessageBox.Show(count > 0 ? $"{count} 件削除しました。" : "削除するものはありませんでした。", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// インストール先の uninstall.ps1 (install.ps1 がコピーしたもの) でアンインストールする。
    /// zip の Uninstall.cmd を消してしまうと消す方法が無かった (テスターの報告)。
    /// </summary>
    private void Uninstall()
    {
        // インストーラー (Meltype-<版>-setup.exe) で入れたときは、そのアンインストーラーを使う (確認もアンインストーラーが出す)
        var installer = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (File.Exists(installer))
        {
            try
            {
                Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アンインストールを始められませんでした: {ex.Message}", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }
        var script = Path.Combine(AppContext.BaseDirectory, "uninstall.ps1");
        if (!File.Exists(script))
        {
            MessageBox.Show("アンインストール用のファイルが見つかりません。Windows の「設定」→「アプリ」→「インストールされているアプリ」か、zip の中の Uninstall.cmd でアンインストールしてください。\nソースから Install-Meltype.ps1 で入れた場合は、ソースのフォルダーの Uninstall-Meltype.ps1 を実行してください (Meltype IME の登録も外します)。", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var answer = MessageBox.Show("Meltype をアンインストールします。設定・学習データ・ユーザー辞書も削除します。よろしいですか？\n(残したいときは、先に「バックアップ」→「バックアップを書き出す...」で書き出してください)", "Meltype のアンインストール", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK) return;
        try
        {
            // スクリプトが Meltype の終了を待ってからファイルを消す。インストール先を消せるように、作業フォルダーは別の場所にする。
            var start = new ProcessStartInfo(Updater.PowerShell) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script, "-FromSettings" }) start.ArgumentList.Add(arg);
            Process.Start(start);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"アンインストールを始められませんでした: {ex.Message}", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _engine.StatusChanged -= OnEngineStatusChanged;
        _engine.ToggleRequested -= OnToggleRequested;
        _tipServer.Dispose();
        _engine.DetachComposition();
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
