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
    private readonly ForegroundTracker _foreground = new();
    private readonly KeyboardMonitor _monitor;
    // Chrome リモート デスクトップ・Parsec で操作されているか (#372)
    private readonly RemoteControl _remote = new();
    // config.json の設定と、動かしている設定 (Running) の写し。フックの中でファイル・レジストリを見ないように、土台が読み直したときに差し替える
    private volatile Settings _settings = null!;
    private volatile Settings _running = null!;
    private bool _disposed;

    /// <summary>動かしている設定 (<see cref="Running"/>) が変わった (前と後)。どのスレッドからも呼ばれる。受け取った側で UI スレッドに移して <see cref="OnSettingsChanged"/> を呼ぶ。</summary>
    public event Action<Settings, Settings>? SettingsChanged;

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

    public MeltypeEngine(Composition.MeltypeRuntime runtime)
    {
        Runtime = runtime;
        // 先に受け取り始める (読んでから受け取り始めるまでに読み直した分を逃さないように。その間に届いた分は、下で読む)
        runtime.SettingsChanged += next =>
        {
            if (_running is not { } previous) return;
            _running = RunningSettings(_settings, next);
            _settings = next;
            SettingsChanged?.Invoke(previous, _running);
        };
        var settings = runtime.Settings.Current;
        _running = RunningSettings(null, settings);
        _settings = settings;
        _monitor = new KeyboardMonitor(OnKey, OnMouseButton, OnForegroundChanged, OnFocusChanged);
        KeyCounter = new KeyCounter(Path.Combine(runtime.DataDirectory, "keycount.json"));
        _keyCountTimer = new System.Threading.Timer(_ => KeyCounter.Save(), null, 60_000, 60_000);
        _discordPresence = new DiscordPresence(
            () => KeyCounter.DiscordPresence && KeyCounter.Enabled,
            () => new DiscordPresence.Stats(KeyCounter.OnDay(DateTime.Today), KeyCounter.Total));
    }

    /// <summary>設定・辞書・学習データ (どの OS も同じもの)。</summary>
    public Composition.MeltypeRuntime Runtime { get; }

    /// <summary>config.json の設定。設定画面・トレイはこれを写して変える (<see cref="Running"/> を写すと、Meltype キーボードを保存してしまう)。</summary>
    public Settings Settings => _settings;

    /// <summary>動かしている設定。入力・表示・報告はこれを読む。動作モードが Meltype IME でも、使えなければ Meltype キーボード。</summary>
    public Settings Running => _running;

    // 動作モードが Meltype IME なのに使えなければ (ポータブル版・ARM64 の Windows・登録されていない・登録済みの Meltype IME がこの Meltype.exe と別ビルド・
    // キーボードの一覧に無い)、Meltype キーボードで動かす。
    // config.json は書き換えないので、Meltype IME を入れ直してから読み直すと Meltype IME で動く
    private static Settings RunningSettings(Settings? previous, Settings next)
    {
        if (next.Mode != InputMode.Tsf || (!AppPaths.IsPortable && Tip.TipServer.IsUsable)) return next;
        if (previous?.Mode != next.Mode) Log.Warn("Meltype IME を使えない (ポータブル版・ARM64 の Windows・登録されていない・登録済みの Meltype IME がこの Meltype.exe と別ビルド・キーボードの一覧に無い) ので、Meltype キーボードで動かします。");
        var running = next.Clone();
        running.Mode = InputMode.Keyboard;
        return running;
    }

    /// <summary>前面のアプリの独自の種類 (自動判定・ライブ変換) を反映した設定。</summary>
    public Settings AppSettings => _running.ForApp(_foreground.Current.ProcessName);

    /// <summary>Meltype キーボード (UI スレッドで作ったもの)。</summary>
    public Keyboard.KeyboardFront? Keyboard { get; set; }

    /// <summary>Meltype IME (TSF)。フックで見た左右の Alt を DLL に知らせる (TipServer.OnKey)。</summary>
    public Tip.TipServer? ImeServer { get; set; }

    /// <summary>フォーカスのある要素 (打鍵数をパスワード欄で数えないため)。</summary>
    public Composition.FocusInspector? Focus { get; set; }

    public void Start()
    {
        _monitor.Start();
        Log.Info($"Meltype を開始しました (モード: {_running.Mode}, 有効: {(_running.Enabled ? "ON" : "OFF")})。");
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
        var settings = _running;
        // 遠隔操作 (AnyDesk・VNC) のキーは、ほかのソフトが送ったキー (LLKHF_INJECTED) として届く。設定で許可していれば手で打ったキーとして扱う。
        // Meltype 自身が送り直したキーは、印 (InjectedMarker) でフックの入口で除いているので、ここには来ない (自分の出力を処理し直さない)。
        // Chrome リモート デスクトップ・Parsec で操作されている間も同じ (#372)。
        if (e.Injected && (settings.AllowInjectedInput || _remote.Active is not null)) e = e with { Injected = false };
        else if (e.Injected && e.IsDown && !_injectedHintLogged && settings.Mode == InputMode.Keyboard && KeyText.CharFromKey(e.Vk, e.Scan, false) is { } injected && injected > ' ')
        {
            // キー配列を変えるソフト (ULE4JIS: US 配列にする) も、押したキーを別のキーとして送り直すので、ほかのソフトが送ったキーとして届く (issue #250)。
            // Windows のタッチキーボードの打鍵も同じ (issue #729)
            _injectedHintLogged = true;
            Log.Info("ほかのソフトが送ったキーを、変換せずにそのままアプリに通しました。Windows のタッチキーボード・ULE4JIS などキー配列を変えるソフト・遠隔操作で打っているなら、設定「遠隔操作などの入力も処理する」を ON にしてください。");
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
        if (_running.Mode == InputMode.Keyboard) return Keyboard?.OnMouseButton(e) == true;
        ImeServer?.OnMouseButton(e);
        return false;
    }

    // フォーカスのある要素は、どちらのフロントも入力欄の事実として使う (Meltype IME はコードの入力欄の判定に)
    private void OnForegroundChanged(IntPtr window)
    {
        _foreground.Refresh(window);
        Focus?.InvalidateForeground();
        if (_running.Mode == InputMode.Keyboard) Keyboard?.OnForegroundChanged(window);
    }

    private void OnFocusChanged()
    {
        Focus?.Invalidate();
        if (_running.Mode == InputMode.Keyboard) Keyboard?.OnFocusChanged();
    }

    /// <summary>入力欄に入った (FocusInspector のスレッドから呼ばれる)。</summary>
    public void OnTextInputEntered()
    {
        if (_running.Mode == InputMode.Keyboard) Keyboard?.OnTextInputEntered();
    }

    /// <summary>
    /// 打鍵数に数えてよいか。Meltype を一時停止しているとき・アプリ別に OFF のとき・パスワード欄では数えない。
    /// フォーカスが移った先をまだ調べている間も数えない (前の入力欄の判定のままパスワード欄の打鍵を数えないように)。
    /// </summary>
    private bool KeyCountAllowed(Settings settings) =>
        settings.Enabled && settings.IsAppEnabled(_foreground.Current.ProcessName) && Focus is { Resolved: true, Current.IsPassword: false };

    public void ApplySettings(Settings settings) => Runtime.Settings.Apply(settings);

    /// <summary>設定が変わった後の処理 (UI スレッド)。設定画面で変えたときも、config.json を外で書き換えたときも呼ぶ。</summary>
    public void OnSettingsChanged(Settings previous, Settings next)
    {
        // Meltype キーボードを使わなくなった: 変換中の内容を確定する。どちらでも、本体の状態を受け取り直す
        if (previous is { Enabled: true, Mode: InputMode.Keyboard } && next is not { Enabled: true, Mode: InputMode.Keyboard }) Keyboard?.Stop();
        else Keyboard?.Refresh();
        // Meltype IME のモードにしたら (トレイでも設定画面でも)、いま使う入力方式も Meltype IME にする (Win + Space で選び直さなくてよいように)
        if (SwitchesToTsf(previous.Mode, next.Mode)) Tip.TipServer.ActivateForSession();
        Log.Info($"設定を反映しました (モード: {next.Mode}, 有効: {(next.Enabled ? "ON" : "OFF")}, 自動判定: {(next.AutoDetection ? "ON" : "OFF")}, 入力方式: {next.InputStyle})。");
    }

    /// <summary>動かす動作モードを Meltype IME に変えたか (選んでも使えなければ Meltype キーボードのまま)。</summary>
    internal static bool SwitchesToTsf(InputMode previous, InputMode next) => previous != InputMode.Tsf && next == InputMode.Tsf;

    public DetectionResult Evaluate(string letters, Settings? settings = null) => Runtime.Evaluate(letters, final: false, settings ?? _settings);

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
        Runtime.Dispose();
        Log.Info("Meltype を終了しました。");
        Log.FlushFile();
    }
}
