// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Detection;
using Meltype.Diagnostics;
using Meltype.Input;

namespace Meltype;

/// <summary>
/// Meltype.exe のアプリの部分: 設定、キーボードフック (登録画面のキーの取り込み・Ctrl + 半角/全角 の一時停止・打鍵数)。
/// 入力の判断はしない。フックのキーは、動作モードが Meltype キーボードのときだけ <see cref="Keyboard.KeyboardFront"/> に渡す
/// (Meltype IME は TSF の DLL が受け取る)。
/// </summary>
internal sealed class MeltypeEngine : IDisposable
{
    private readonly string? _configPath;
    private readonly ScoreEngine _scoreEngine;
    private readonly ForegroundTracker _foreground = new();
    private readonly KeyboardMonitor _monitor;
    // Chrome リモート デスクトップ・Parsec で操作されているか (#372)
    private readonly RemoteControl _remote = new();
    private volatile Settings _settings;
    private bool _disposed;

    public event Action? StatusChanged;

    /// <summary>Ctrl + 半角/全角 が押された (フックのスレッドから呼ばれる。受け取った側で UI スレッドに移して切り替える)。</summary>
    public event Action? ToggleRequested;

    private readonly HashSet<int> _toggleKeyDown = [];

    /// <summary>打鍵数のカウンター (トレイの「打鍵数...」)。</summary>
    public KeyCounter KeyCounter { get; }
    private readonly System.Threading.Timer _keyCountTimer;
    /// <summary>Discord のステータスに打鍵数を出す (打鍵数の「Discord に表示する」が ON の間だけ。入力とは別のスレッドで送る)。</summary>
    private readonly DiscordPresence _discordPresence;

    private Action<KeyEvent>? _shortcutCapture;
    private bool _captureHankakuTail;
    private readonly HashSet<int> _captureDown = [];
    // ほかのソフトが送ったキーを通したことを、1 回だけログに書いたか (#250)
    private bool _injectedHintLogged;

    public MeltypeEngine(Settings settings, string? configPath, string? userDictionaryDirectory)
    {
        _settings = settings.Clone().Normalize();
        _configPath = configPath;
        Log.SetFileOutput(_settings.FileLog ? AppPaths.LogFile : null);
        Log.RecordText = _settings.LogTypedText;

        _scoreEngine = ScoreEngine.CreateDefault(() => _settings, userDictionaryDirectory);
        _monitor = new KeyboardMonitor(OnKey, OnMouseButton, OnForegroundChanged, OnFocusChanged);
        KeyCounter = new KeyCounter(configPath is null ? null : AppPaths.KeyCountFile);
        _keyCountTimer = new System.Threading.Timer(_ => KeyCounter.Save(), null, 60_000, 60_000);
        _discordPresence = new DiscordPresence(
            () => KeyCounter.DiscordPresence && KeyCounter.Enabled,
            () => new DiscordPresence.Stats(KeyCounter.OnDay(DateTime.Today), KeyCounter.Total));
    }

    public Settings Settings => _settings;

    /// <summary>前面のアプリの独自の種類 (自動判定・ライブ変換) を反映した設定。</summary>
    public Settings AppSettings => _settings.ForApp(_foreground.Current.ProcessName);

    /// <summary>Meltype キーボード (UI スレッドで作ったもの)。</summary>
    public Keyboard.KeyboardFront? Keyboard { get; set; }

    /// <summary>Meltype IME (TSF)。フックで見た左右の Alt を DLL に知らせる (TipServer.OnKey)。</summary>
    public Tip.TipServer? ImeServer { get; set; }

    /// <summary>フォーカスのある要素 (打鍵数をパスワード欄で数えないため)。</summary>
    public Composition.FocusInspector? Focus { get; set; }

    public void Start()
    {
        _monitor.Start();
        Log.Info($"Meltype を開始しました (モード: {_settings.Mode}, 有効: {(_settings.Enabled ? "ON" : "OFF")})。");
    }

    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            var next = _settings.Clone();
            next.Enabled = value;
            ApplySettings(next);
        }
    }

    public void CaptureShortcut(Action<KeyEvent>? capture) => Volatile.Write(ref _shortcutCapture, capture);

    private bool OnKey(KeyEvent e)
    {
        // 登録画面の取り込みは入力より先。終了後も飲み込んだキー解放は対にする。
        // 取り込みを終えた F3 解放に続く F4 押下を、通常の切り替えとして実行しない。
        if (_captureHankakuTail)
        {
            _captureHankakuTail = false;
            if (!e.Injected && e.IsDown && e.Vk == VirtualKeys.OemEnlw && e.Scan == 0x29 && !e.Extended) return true;
        }
        var capture = Volatile.Read(ref _shortcutCapture);
        if (!e.Injected && capture is not null)
        {
            if (e.IsDown) _captureDown.Add(e.PhysicalId); else _captureDown.Remove(e.PhysicalId);
            _captureHankakuTail = e.IsUp && e.Vk == VirtualKeys.OemAuto && e.Scan == 0x29 && !e.Extended;
            capture(e);
            return true;
        }
        if (e.IsUp && _captureDown.Remove(e.PhysicalId))
        {
            _captureHankakuTail = e.Vk == VirtualKeys.OemAuto && e.Scan == 0x29 && !e.Extended;
            return true;
        }
        var settings = _settings;
        // 遠隔操作 (AnyDesk・VNC) のキーは、ほかのソフトが送ったキー (LLKHF_INJECTED) として届く。設定で許可していれば手で打ったキーとして扱う。
        // Meltype 自身が送り直したキーは、印 (InjectedMarker) でフックの入口で除いているので、ここには来ない (自分の出力を処理し直さない)。
        // Chrome リモート デスクトップ・Parsec で操作されている間も同じ (#372)。
        if (e.Injected && (settings.AllowInjectedInput || _remote.Active is not null)) e = e with { Injected = false };
        else if (e.Injected && e.IsDown && !_injectedHintLogged && settings.Mode == InputMode.Keyboard && KeyText.CharFromKey(e.Vk, e.Scan, false) is { } injected && injected > ' ')
        {
            // キー配列を変えるソフト (ULE4JIS: US 配列にする) も、押したキーを別のキーとして送り直すので、ほかのソフトが送ったキーとして届く (issue #250)
            _injectedHintLogged = true;
            Log.Info("ほかのソフトが送ったキーを、変換せずにそのままアプリに通しました。ULE4JIS などキー配列を変えるソフトや遠隔操作で打っているなら、設定「遠隔操作などの入力も処理する」を ON にしてください。");
        }
        if (e.Injected) return false;
        KeyCounter.Count(e, DateTime.Now, KeyCountAllowed(settings));
        if (e.IsUp && _toggleKeyDown.Remove(e.Vk)) return true;
        // Ctrl + 半角/全角: Meltype 全体の有効/無効 (どちらのモードでも)。変換ボックスの後始末が要るので切り替え自体は UI スレッドで行う。
        if (VirtualKeys.IsHankakuZenkaku(e.Vk) && e.IsDown && IsDown(VirtualKeys.Control))
        {
            _toggleKeyDown.Add(e.Vk);
            ToggleRequested?.Invoke();
            return true;
        }
        return settings.Mode == InputMode.Keyboard ? Keyboard?.OnKey(e) == true : ImeServer?.OnKey(e, _foreground.Current) == true;
    }

    private bool OnMouseButton(MouseButtonEvent e)
    {
        if (_settings.Mode == InputMode.Keyboard) return Keyboard?.OnMouseButton(e) == true;
        ImeServer?.OnMouseButton(e);
        return false;
    }

    // フォーカスのある要素は、どちらのフロントも入力欄の事実として使う (Meltype IME はコードの入力欄の判定に)
    private void OnForegroundChanged(IntPtr window)
    {
        _foreground.Refresh(window);
        Focus?.InvalidateForeground();
        if (_settings.Mode == InputMode.Keyboard) Keyboard?.OnForegroundChanged(window);
    }

    private void OnFocusChanged()
    {
        Focus?.Invalidate();
        if (_settings.Mode == InputMode.Keyboard) Keyboard?.OnFocusChanged();
    }

    /// <summary>入力欄に入った (FocusInspector のスレッドから呼ばれる)。</summary>
    public void OnTextInputEntered()
    {
        if (_settings.Mode == InputMode.Keyboard) Keyboard?.OnTextInputEntered();
    }

    /// <summary>
    /// 打鍵数に数えてよいか。Meltype を一時停止しているとき・アプリ別に OFF のとき・パスワード欄では数えない。
    /// フォーカスが移った先をまだ調べている間も数えない (前の入力欄の判定のままパスワード欄の打鍵を数えないように)。
    /// </summary>
    private bool KeyCountAllowed(Settings settings) =>
        settings.Enabled && settings.IsAppEnabled(_foreground.Current.ProcessName) && Focus is { Resolved: true, Current.IsPassword: false };

    /// <summary>英数状態で打ち始めた英字がローマ字 (日本語) かを調べる。UI スレッドから呼ばれる。</summary>
    public Verdict ClassifyDirect(string letters, bool final)
    {
        var keys = letters.Select(c => (int)char.ToUpperInvariant(c)).ToArray();
        var result = _scoreEngine.Evaluate(new DetectionInput(letters, keys, final), AppSettings);
        // 同梱の辞書にない英単語 (debate, potato) はローマ字としても読めるので、スペルチェッカーの語なら日本語にしない。
        if (result.Verdict == Verdict.Japanese && letters.Length >= 4 && Detection.WindowsSpellChecker.Shared.IsWord(letters.ToLowerInvariant()))
        {
            Log.Info($"英数状態: {Log.Text(letters)}は英単語 (スペルチェッカー) なので日本語にしない");
            return final ? Verdict.English : Verdict.Undecided;
        }
        if (result.Verdict == Verdict.Japanese) Log.Decision($"英数状態でローマ字を検知: {result.Describe()}");
        return result.Verdict;
    }

    public void ApplySettings(Settings settings)
    {
        var next = settings.Clone().Normalize();
        var previous = _settings;
        // 設定画面で Meltype IME を選んだ (「既定値に戻す」を含む) が、ARM64 の Windows か、登録されていないか、このユーザーのキーボードの一覧に無い:
        // どこでも何も起きなくなるので選ばせない
        if (next.Mode == InputMode.Tsf && previous.Mode != InputMode.Tsf && !Tip.TipServer.IsUsable)
        {
            next.Mode = previous.Mode;
            Log.Warn("Meltype IME を使えない (ARM64 の Windows・登録されていない・キーボードの一覧に無い) ので、動作モードは変えません。");
        }
        _settings = next;
        // Meltype キーボードを使わなくなった: 変換中の内容を確定する。どちらでも、本体の状態を受け取り直す
        if (previous is { Enabled: true, Mode: InputMode.Keyboard } && next is not { Enabled: true, Mode: InputMode.Keyboard }) Keyboard?.Stop();
        else Keyboard?.Refresh();
        // Meltype IME のモードにしたら (トレイでも設定画面でも)、いま使う入力方式も Meltype IME にする (Win + Space で選び直さなくてよいように)
        if (SwitchesToTsf(previous.Mode, next.Mode)) Tip.TipServer.ActivateForSession();
        Log.SetFileOutput(next.FileLog ? AppPaths.LogFile : null);
        Log.RecordText = next.LogTypedText;
        if (_configPath is not null)
        {
            try { next.Save(_configPath); }
            catch (Exception ex) { Log.Warn($"config.json を保存できませんでした: {ex.Message}"); }
        }
        Log.Info($"設定を反映しました (モード: {next.Mode}, 有効: {(next.Enabled ? "ON" : "OFF")}, 自動判定: {(next.AutoDetection ? "ON" : "OFF")}, 入力方式: {next.InputStyle})。");
        StatusChanged?.Invoke();
    }

    /// <summary>動作モードを Meltype IME に変えたか (使えずに元のモードに戻したときは、戻した後のモードで見る)。</summary>
    internal static bool SwitchesToTsf(InputMode previous, InputMode next) => previous != InputMode.Tsf && next == InputMode.Tsf;

    public DetectionResult Evaluate(string letters, Settings? settings = null) =>
        _scoreEngine.Evaluate(new DetectionInput(letters, letters.Select(c => (int)char.ToUpperInvariant(c)).ToArray(), IsFinal: false), settings ?? _settings);

    private static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor.Dispose();
        _remote.Dispose();
        _keyCountTimer.Dispose();
        _discordPresence.Dispose();
        KeyCounter.Save();
        Log.Info("Meltype を終了しました。");
        Log.FlushFile();
    }
}
