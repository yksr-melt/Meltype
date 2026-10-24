// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Detection;
using Meltype.Diagnostics;
using Meltype.IME;
using Meltype.Input;

namespace Meltype;

/// <summary>
/// 各モジュールをつなぐ (設計書 §4)。
///   フックのスレッド   … KeyboardMonitor。打鍵を変換ボックス (CompositionService) に渡す (例外: Alt の単独押しの解放の送り直し、MaskAltTap)。
///   タイマー           … IME 状態のポーリング。
/// </summary>
internal sealed class MeltypeEngine : IDisposable
{
    private readonly string? _configPath;
    private readonly ScoreEngine _scoreEngine;
    private readonly ForegroundTracker _foreground = new();
    private readonly Imm32ImeController _imm32 = new();
    private readonly KeyInjector _injector = new();
    private readonly KeyboardMonitor _monitor;
    private readonly System.Threading.Timer _pollTimer;
    // Chrome リモート デスクトップ・Parsec で操作されているか (#372)
    private readonly RemoteControl _remote = new();
    private volatile Settings _settings;
    private volatile ImeSnapshot? _imeSnapshot;
    private int _polling;
    private bool _disposed;

    private sealed record ImeSnapshot(ImeState State);

    public event Action? StatusChanged;

    /// <summary>Ctrl + 半角/全角 が押された (フックのスレッドから呼ばれる。受け取った側で UI スレッドに移して切り替える)。</summary>
    public event Action? ToggleRequested;

    private readonly HashSet<int> _toggleKeyDown = [];

    public MeltypeEngine(Settings settings, string? configPath, string? userDictionaryDirectory)
    {
        _settings = settings.Clone().Normalize();
        _configPath = configPath;
        Log.SetFileOutput(_settings.FileLog ? AppPaths.LogFile : null);
        Log.RecordText = _settings.LogTypedText;

        _scoreEngine = ScoreEngine.CreateDefault(() => _settings, userDictionaryDirectory);
        _monitor = new KeyboardMonitor(OnKey, OnMouseButton, OnForegroundChanged, OnFocusChanged);
        _pollTimer = new System.Threading.Timer(_ => PollImeState(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public Settings Settings => _settings;

    /// <summary>前面のアプリの独自の種類 (判定の強さ・ライブ変換) を反映した設定。</summary>
    public Settings AppSettings => _settings.ForApp(_foreground.Current.ProcessName);

    public void Start()
    {
        _monitor.Start();
        _pollTimer.Change(0, 250);
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

    // ---- Meltype キーボード (変換ボックス) ----

    private Composition.CompositionService? _composition;
    private volatile bool _keyboardDirect;
    private volatile bool _directEnglishWord;
    private readonly HashSet<int> _swallowedToggleUps = [];
    private readonly AltTapDetector _altTap = new();
    private readonly ToggleShortcutDetector _shortcutDetector = new();
    private Action<KeyEvent>? _shortcutCapture;
    private bool _captureHankakuTail;
    private readonly HashSet<int> _captureDown = [];

    public void CaptureShortcut(Action<KeyEvent>? capture) => Volatile.Write(ref _shortcutCapture, capture);

    /// <summary>UI スレッドで作った変換ボックスをつなぐ。</summary>
    public void AttachComposition(Composition.CompositionService composition)
    {
        // 起動の途中から Windows の IME で語を打っている (Enter で確定する前か、確定した直後) なら、閉じずにそのまま打たせる (PollImeState で引き継ぐ)。
        // 語の途中で Meltype に替わると、前半と後半が別の IME に分かれる。OnKey が _composition を見る前に決めておく
        Volatile.Write(ref _takeoverPending, IsKeyboardActive && StillTypingInSystemIme() ? 1 : 0);
        composition.Controller.ToggleModeRequested += target =>
        {
            if (_settings.Mode != InputMode.Keyboard || !_settings.Enabled ||
                Native.GetForegroundWindow() != target || !KeyboardLayoutPolicy.AllowsInput(_settings)) return;
            // 前の打鍵を処理した後の未確定状態で判断する。未確定文字があれば文字だけを切り替える。
            if (composition.Controller.TogglePendingInput()) return;
            // コードの行 (コメント・文字列の外) では、この行だけ日本語にする / 戻す。
            if (!_keyboardDirect && IsCodeApp(_settings) && (_codeJapanese || InCode(_settings)))
            {
                _codeJapanese = !_codeJapanese;
                Log.Info(_codeJapanese ? "コードの行: 日本語で入力 (改行しても続ける。キャレットが別の場所に移るか、もう一度押すまで)" : "コードの行: 英数に戻す");
                ShowCodeMode();
            }
            else KeyboardDirect = !_keyboardDirect;
        };
        composition.PasteCommit = () => PastePolicy.ShouldPaste(_settings, _foreground.Current.ProcessName, IsQt(_foreground.Current.Window));
        composition.InputAllowed = () => KeyboardLayoutPolicy.AllowsInput(_settings);
        composition.Focus.TreatsAsTextInput = () => _settings.TreatsAsTextInput(_foreground.Current.ProcessName)
            || LineContext.TerminalProcesses.Contains(_foreground.Current.ProcessName);
        composition.Focus.TreatsWindowAsTextInput = () => WindowInputApps.Contains(_foreground.Current.ProcessName);
        composition.ShowModeOnFocus = ShowModeOnFocus;
        composition.Focus.ForegroundProcessName = () => _foreground.Current.ProcessName;
        // 変換ボックスで確定した文字と、Meltype が送り直したキーも、今の行の追いかけに入れる (自分で送ったキーはフックに届かない)。
        composition.Controller.Committed += text =>
        {
            _line.Append(text);
            _sigil.Append(text);
            _snippet.Append(text);
        };
        composition.Controller.ReconversionCommitted += () => InvalidateLine();
        composition.KeyReplayed += e => TrackLine(e);
        composition.MouseReplayed += () => InvalidateLine();
        composition.Focus.Invalidate();
        Volatile.Write(ref _composition, composition);
        if (IsKeyboardActive && !TakeoverPending) CloseSystemImeAsync();
    }

    /// <summary>起動の途中から Windows の IME で語を打っていて、まだ Meltype キーボードが入力を受け持っていない (1)。</summary>
    private int _takeoverPending;
    private bool TakeoverPending => Volatile.Read(ref _takeoverPending) != 0;
    /// <summary>Meltype キーボードが入力を受け持つ前に、Windows の IME で語を打っている途中か (文字のキーを押してから Enter を押していない)。</summary>
    private volatile bool _imeMaybeComposing;
    /// <summary>Meltype キーボードが入力を受け持つ前に、最後にキーを押した時刻。</summary>
    private long _lastKeyDownMs;
    /// <summary>最後にキーを押してから、この時間たつまで閉じない (Enter で確定した直後に閉じると、アプリがまだ受け取っていない Enter が改行になる)。</summary>
    private const int TakeoverQuietMs = 200;

    /// <summary>Windows の IME で語を打っている途中か、打ってすぐか (閉じると、前半と後半が別の IME に分かれる)。</summary>
    private bool StillTypingInSystemIme() =>
        _imeMaybeComposing || Environment.TickCount64 - Volatile.Read(ref _lastKeyDownMs) < TakeoverQuietMs;

    /// <summary>
    /// Meltype キーボードが入力を受け持つ前のキー。Windows の IME で語を打っている途中かを、打ったキーから推す
    /// (未確定の文字があるかは、ほかのアプリの IME に問い合わせられない)。文字のキーで打ち始め、Enter で確定したとみなす。
    /// </summary>
    private void NoteKeyBeforeTakeover(KeyEvent e)
    {
        if (!e.IsDown) return;
        Volatile.Write(ref _lastKeyDownMs, e.TimeMs);
        if (e.Vk == VirtualKeys.Return) _imeMaybeComposing = false;
        else if (StartsWordInSystemIme(e)) _imeMaybeComposing = true;
    }

    /// <summary>Windows の IME が ON なら、変換中の文字を打ち始めるキーか (英字・数字・記号。Ctrl などとの組み合わせは除く)。</summary>
    private static bool StartsWordInSystemIme(KeyEvent e) =>
        (VirtualKeys.IsLetter(e.Vk) || IsDigitOrPunctuationKey(e.Vk)) &&
        !IsDown(VirtualKeys.Control) && !IsDown(VirtualKeys.Menu) && !IsDown(VirtualKeys.LWin) && !IsDown(VirtualKeys.RWin);

    /// <summary>数字・句読点・かぎかっこ・長音・記号のキー (Shift を押して打つ ＃＄％（）＠ なども)。</summary>
    private static bool IsDigitOrPunctuationKey(int vk) => vk is >= 0x30 and <= 0x39 or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;

    /// <summary>
    /// 起動の途中から Windows の IME で打っていたのをやめて、Meltype キーボードが入力を受け持つ。
    /// 待っていなければ false。Windows の IME を閉じるのは呼び出し元。
    /// </summary>
    private bool EndTakeover(string reason)
    {
        if (Interlocked.Exchange(ref _takeoverPending, 0) == 0) return false;
        InvalidateLine();
        if (IsKeyboardActive) Log.Info($"起動の途中から Windows の IME で打っていたのをやめて、Meltype キーボードが入力を受け持ちます ({reason})。");
        return true;
    }

    /// <summary>終了時。未確定の内容を確定してから切り離す (UI スレッドで呼ぶ)。</summary>
    public void DetachComposition()
    {
        var composition = _composition;
        _composition = null;
        composition?.Flush();
    }

    /// <summary>Meltype キーボードが入力を受け付ける状態か (半角/全角 で直接入力にしていない)。</summary>
    public bool IsKeyboardActive => _settings.Enabled && _settings.Mode == InputMode.Keyboard && !_keyboardDirect && KeyboardLayoutPolicy.AllowsInput(_settings);

    /// <summary>
    /// #95の対応として、Qtアプリでは確定した文字を貼り付けで入れる。
    /// </summary>
    /// <summary>フォーカスがウィンドウそのものでも入力欄とみなすアプリ (#373)。</summary>
    private static readonly HashSet<string> WindowInputApps = new(StringComparer.OrdinalIgnoreCase) { "LINE.exe" };

    /// <summary>入力欄にフォーカスが移っても UI Automation の通知が来ないことがあるブラウザー (#364)。打ったときに調べ直す。</summary>
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "firefox.exe", "chrome.exe", "msedge.exe", "vivaldi.exe", "brave.exe", "opera.exe", "floorp.exe", "zen.exe", "waterfox.exe", "librewolf.exe" };

    /// <summary>
    /// 入力欄と判定できていないとき、打った時点でフォーカスを調べ直すアプリか。
    /// ブラウザーはタブ切替後に通知が来ないことがある (#364)。LINE は起動直後、入力欄の通知が来ず「入力欄ではない」のまま残ることがある。
    /// </summary>
    internal static bool RechecksStaleFocus(string? processName) =>
        processName is not null && (Browsers.Contains(processName) || WindowInputApps.Contains(processName));

    private static bool IsQt(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        var className = new System.Text.StringBuilder(64);
        Native.GetClassName(window, className, className.Capacity);
        return PastePolicy.IsQtWindowClass(className.ToString());
    }

    public bool KeyboardDirect
    {
        get => _keyboardDirect;
        set
        {
            _keyboardDirect = value;
            _directEnglishWord = false;
            Log.Info(value ? "Meltype キーボード: 直接入力 (英数)" : "Meltype キーボード: 日本語入力");
            if (!value) CloseSystemImeAsync();
            // 「コード」のアプリのコードの行では、日本語入力にしても次のキーは英数で入る。
            if (value || !IsCodeApp(_settings)) _composition?.ShowMode(!value);
            else ShowCodeMode();
            StatusChanged?.Invoke();
        }
    }

    private bool OnKey(KeyEvent e)
    {
        // 登録画面の取り込みは両モードの入力処理より先。終了後も飲み込んだキー解放は対にする。
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
            if (e.IsUp) _shortcutDetector.ReleaseConsumed(e);
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
        if (_shortcutDetector.ReleaseConsumed(e)) return true;
        // 飲み込んだ切替キーの解放は、入力言語が変わっても対にして処理する。
        if (e.IsUp && !e.Injected)
        {
            if (_toggleKeyDown.Remove(e.Vk)) return true;
            lock (_swallowedToggleUps) if (_swallowedToggleUps.Remove(e.Vk)) return true;
        }
        // 左右の Alt の単独押し (#85)。どの打鍵も見て、Alt を押している間に他のキーを押したら単独押しにしない。
        var altTap = _altTap.OnKey(e, e.Vk is VirtualKeys.LMenu or VirtualKeys.RMenu && e.IsDown &&
            (IsDown(VirtualKeys.Control) || IsDown(VirtualKeys.Shift) || IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin)));
        if (!KeyboardLayoutPolicy.AllowsInput(settings))
        {
            SuspendForInputLanguage();
            // 既に保留中なら順序を保って送り直す。それ以外の打鍵はそのまま通す。
            return settings.Mode == InputMode.Keyboard && _composition?.Gate.OnKey(e, _ => false) == true;
        }
        // Ctrl + 半角/全角: Meltype 全体の有効/無効 (どちらのモードでも)。変換ボックスの後始末が要るので切り替え自体は UI スレッドで行う。
        if (VirtualKeys.IsHankakuZenkaku(e.Vk) && !e.Injected && e.IsDown && IsDown(VirtualKeys.Control))
        {
            if (e.IsDown)
            {
                _toggleKeyDown.Add(e.Vk);
                ToggleRequested?.Invoke();
            }
            return true;
        }
        // Meltype IME (TSF): 入力は IME の側で受け持つので、フックでは何もしない。
        // 左右の Alt の単独押しだけは、Meltype キーボードと同じく英数 / 日本語の切り替えに使う (Meltype IME が受け持つアプリで。TipServer と同じ判定)。
        if (settings.Mode == InputMode.Tsf)
        {
            var app = _foreground.Current;
            if (altTap != AltTap.None && settings.Enabled && settings.AltKeysSwitchKeyboard &&
                settings.IsAppEnabled(app.ProcessName) && !settings.IsGame(app.ProcessName, looksLikeGame: false))
            {
                var masked = MaskAltTap(altTap, e);
                if (altTap == AltTap.Tapped) SetImeOpenAsync(japanese: e.Vk == VirtualKeys.RMenu);
                return masked;
            }
            return false;
        }
        // 変換ボックスの準備前・終了処理中は何もしない (素通し)。
        // 起動の途中から Windows の IME で語を打っている間も同じ (確定したら PollImeState で引き継ぐ)
        if (Volatile.Read(ref _composition) is not { } composition || TakeoverPending)
        {
            NoteKeyBeforeTakeover(e);
            return false;
        }
        // アプリ別設定で OFF にしたアプリ・ゲーム・全画面のアプリなどでは、切り替えのキーも定型文も扱わず、そのままアプリに通す (Windows の IME で打てるように)。
        // CloseSystemImeAsync と同じ判定。一時停止中は下で行を追いかけるので、ここでは返さない。
        // 追いかけない間に打った行は読み直す (同じウィンドウのまま全画面を出たときに、古い行で判定しないように)
        if (settings.Enabled && !composition.Gate.IsCaptured && !_foreground.Check(settings).Allowed)
        {
            _line.Invalidate();
            return false;
        }

        var mods = (IsDown(VirtualKeys.Shift) ? ShortcutModifiers.Shift : 0) |
            (IsDown(VirtualKeys.Control) ? ShortcutModifiers.Control : 0) |
            (IsDown(VirtualKeys.Menu) ? ShortcutModifiers.Alt : 0) |
            (IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin) ? ShortcutModifiers.Win : 0);
        var shortcut = _shortcutDetector.OnKey(e, mods,
            settings.Enabled && settings.IsAppEnabled(_foreground.Current.ProcessName) &&
                !settings.IsGame(_foreground.Current.ProcessName, _foreground.Current.LooksLikeGame) ? settings.EffectiveToggleShortcuts : []);
        if (shortcut.Toggle)
        {
            MaskAltRelease();
            composition.Gate.OnToggleMode(Native.GetForegroundWindow());
        }
        if (shortcut.Consume) return true;

        // 左 Alt の単独押しで英数、右 Alt の単独押しで日本語。Alt の押下はそのままアプリに通し、解放はメニューバーに移らないよう送り直す (MaskAltTap)。
        if (altTap != AltTap.None && settings.Enabled && settings.AltKeysSwitchKeyboard && !composition.Gate.IsCaptured)
        {
            var masked = MaskAltTap(altTap, e);
            if (altTap == AltTap.Tapped) SetKeyboardMode(composition, settings, japanese: e.Vk == VirtualKeys.RMenu);
            if (masked) return true;
        }
        // 無変換で英数、変換で日本語 (Mac の 英数 / かな と同じく、トグルではなく決まったモードにする)。
        if (settings.Enabled && settings.ConvertKeysSwitchKeyboard && e.Vk is VirtualKeys.NonConvert or VirtualKeys.Convert && e.IsDown && !e.Injected && !composition.Gate.IsCaptured)
        {
            // 押しっぱなしの繰り返し。
            lock (_swallowedToggleUps) if (_swallowedToggleUps.Contains(e.Vk)) return true;
            var japanese = e.Vk == VirtualKeys.Convert;
            // もう日本語入力なら、変換キーは今までどおり (選択した文字の再変換)。
            if (!japanese || _keyboardDirect || InCode(settings))
            {
                lock (_swallowedToggleUps) _swallowedToggleUps.Add(e.Vk);
                MaskAltRelease();
                SetKeyboardMode(composition, settings, japanese);
                return true;
            }
        }
        // 変換キーは Microsoft IME に渡さない。渡すと IME が ON になって選択した文字の再変換を始め、
        // Meltype キーボードが IME を OFF に戻すときに、再変換中の文字 (選択していた文字) が消える (#19)。
        if (settings.Enabled && e.Vk == VirtualKeys.Convert && !e.Injected && !composition.Gate.IsCaptured)
        {
            // 変換キーは Meltype に渡す。選択文字の取得・再変換は UI スレッドで行う。
            if (composition.Gate.OnKey(e, StartsComposition)) return true;
            if (e.IsDown)
            {
                lock (_swallowedToggleUps) _swallowedToggleUps.Add(e.Vk);
                MaskAltRelease();
                Log.Info("変換キー: この入力先では Meltype の再変換を開始できません。");
            }
            return true;
        }
        // 英数状態で英語と判定した単語は、区切りのキー (Space・記号など) が来たら終わり。次の単語はまた判定する。
        // @ と _ の後ろはユーザー名 (@kuraido、upah_setu) なので判定しない (ローマ字として読めても日本語にしない)。
        if (_keyboardDirect && e.IsDown && !VirtualKeys.IsLetter(e.Vk) && !VirtualKeys.IsModifier(e.Vk))
            _directEnglishWord = !e.Injected && KeyText.CharFromKey(e.Vk, e.Scan, false) is '@' or '_';
        if (HandleSnippetKey(composition, settings, e)) return true;
        var swallowed = composition.Gate.OnKey(e, StartsComposition);
        // Meltype を通らずにアプリへ届いたキーはキャレットを動かすかもしれない。直前の語を確定し直さないようにする。
        if (!swallowed && e.IsDown && !VirtualKeys.IsModifier(e.Vk)) composition.ForgetLastCommit();
        if (!swallowed && !e.Injected) TrackLine(e);
        return swallowed;
    }

    /// <summary>
    /// 英数 / 日本語 に決める (無変換・変換、左右の Alt。半角/全角 のトグルと違い、押す前のモードによらない)。
    /// 「コード」のアプリのコードの行では、半角/全角 と同じくこの行だけ日本語にする / 戻す。
    /// </summary>
    private void SetKeyboardMode(Composition.CompositionService composition, Settings settings, bool japanese)
    {
        if (!japanese)
        {
            if (!_keyboardDirect && IsCodeApp(settings) && (_codeJapanese || InCode(settings)))
            {
                if (_codeJapanese)
                {
                    _codeJapanese = false;
                    Log.Info("コードの行: 英数に戻す");
                }
                ShowCodeMode();
                return;
            }
            if (_keyboardDirect) composition.ShowMode(false);
            else KeyboardDirect = true;
            return;
        }
        // コードの行を日本語にしてから、英数状態なら KeyboardDirect の切り替えで「あ」を出す。
        var wasDirect = _keyboardDirect;
        if (InCode(settings))
        {
            _codeJapanese = true;
            Log.Info("コードの行: 日本語で入力 (改行しても続ける。キャレットが別の場所に移るか、もう一度押すまで)");
        }
        if (wasDirect) KeyboardDirect = false;
        else if (IsCodeApp(settings)) ShowCodeMode();
        else composition.ShowMode(true);
    }

    /// <summary>
    /// Alt/Win を押したまま打ったキー (US 配列では Alt + ` が半角/全角) を飲み込むと、アプリには修飾キーだけを押したように見える。
    /// 割り当てのないキー (0xE8) を送り、Alt の単独押しによるメニュー移動を防ぐ。
    /// </summary>
    private static void MaskAltRelease()
    {
        if (IsDown(VirtualKeys.Menu) || IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin))
            ThreadPool.QueueUserWorkItem(_ => KeyInjector.SendKey(0xE8));
    }

    /// <summary>
    /// 左右の Alt の単独押しでメニューバーに移らないよう、Alt の解放を止めて、何もしないキー (0xE8、割り当てなし) のあとに送り直す。
    /// まとめて SendInput で送り、次の打鍵より先に届くように、ここだけはフックのスレッドで送る。
    /// 押している間にスレッドプールで 0xE8 を送るやり方では、すぐに離すと Alt の解放のほうが先に並んでいて、メニューバーに移った。
    /// 管理者として動くアプリには送り直した Alt の解放が届かず (UIPI)、Alt が押されたままになるので止めない。
    /// 戻り値: このキー (Alt の解放) を止めたか。
    /// </summary>
    private bool MaskAltTap(AltTap altTap, KeyEvent e) =>
        altTap == AltTap.Tapped && ForegroundTracker.CanSendInputTo(_foreground.Current) && _injector.Inject(MaskedAltRelease(e));

    /// <summary>Alt の単独押しの解放 release の代わりに送るキー (0xE8 の押下・解放のあとに、release をそのまま。右 Alt の拡張キーの印も残す)。</summary>
    internal static KeyEvent[] MaskedAltRelease(KeyEvent release) =>
        [new KeyEvent(0xE8, 0, false, false, false, 0), new KeyEvent(0xE8, 0, false, true, false, 0), release];

    /// <summary>
    /// Meltype IME のモードで、前面のアプリの IME を日本語入力 / 英数にする (左右の Alt の単独押し)。
    /// Meltype IME は開閉の変化を受けて表示を切り替え、英数にするときは変換中の文字を確定する。
    /// 「コード」のアプリのコードの行だけを日本語にする働き (半角/全角 と同じ) はない。フックのスレッドでは待たない。
    /// </summary>
    private void SetImeOpenAsync(bool japanese)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                // 日本語のキーボードでなければ IME は無い
                if (ImeTarget.FromForeground() is not { } target || (Native.GetKeyboardLayout(target.ThreadId).ToInt64() & 0xFFFF) != 0x0411) return;
                if (_imm32.TrySetOpen(target, japanese, null, _settings.ImeTimeoutMs))
                    Log.Info(japanese ? "左右の Alt: 日本語入力にしました (右 Alt)" : "左右の Alt: 英数にしました (左 Alt)");
                else
                    Log.Warn($"左右の Alt: {(japanese ? "日本語入力" : "英数")}にできませんでした。");
            }
            catch (Exception ex)
            {
                Log.Warn($"左右の Alt: IME を切り替えられませんでした: {ex.Message}");
            }
        });
    }

    // ---- アプリの種類「コード」: コメント・文字列の中だけ日本語 ----

    private readonly LineTracker _line = new();
    // 先頭か空白の直後の /command・$skill・@ファイル名 (#193)。
    private readonly Composition.SigilWord _sigil = new();
    // 先頭か空白の直後の ;名前 (定型文: #290)。
    private readonly Composition.SnippetTrigger _snippet = new();
    // 直前に入れた定型文 (すぐに Esc を押したら、打った ;名前 に戻す)。
    private (string Text, string Typed)? _lastSnippet;

    /// <summary>定型文を使うか: 記号が 1 文字で、定型文を 1 つ以上登録している (登録していなければ ; は今までどおり変換ボックスに入れる)。</summary>
    private bool SnippetsEnabled(Settings settings) =>
        settings.SnippetMark is [_] && _composition?.Snippets.Count > 0 && settings.InputStyle != InputStyle.Kana;

    /// <summary>
    /// ;名前 の後の Space / Tab で定型文を入れる。入れた直後の Esc で、打った ;名前 に戻す (issue #290)。
    /// 変換ボックスが空のときだけ (名前は打つたびにアプリへ渡しているので、変換ボックスには入っていない)。処理したら true。
    /// </summary>
    private bool HandleSnippetKey(Composition.CompositionService composition, Settings settings, KeyEvent e)
    {
        if (!e.IsDown || e.Injected || VirtualKeys.IsModifier(e.Vk)) return false;
        var undo = _lastSnippet;
        _lastSnippet = null;
        if (composition.Gate.IsCaptured || IsDown(VirtualKeys.Control) || IsDown(VirtualKeys.Menu) || IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin)) return false;
        if (e.Vk == VirtualKeys.Escape && undo is { } last)
        {
            lock (_swallowedToggleUps) _swallowedToggleUps.Add(e.Vk);
            composition.Post(() => composition.InsertSnippet(last.Text.ReplaceLineEndings("\n").Length, last.Typed));
            Log.Info("定型文を取り消して、打った名前に戻しました。");
            return true;
        }
        // 入力欄にフォーカスがないとき (エクスプローラーのファイル一覧など) は展開しない。名前を消す Backspace が「前のフォルダーに戻る」になってしまう。
        // Meltype の設定画面などでも展開しない。プロファイルの名前などの画面を閉じて戻った直後は、フォーカスの判定が閉じた画面の入力欄のまま残っている。
        if (e.Vk is not (VirtualKeys.Space or VirtualKeys.Tab) || IsDown(VirtualKeys.Shift) || !SnippetsEnabled(settings) || !composition.Focus.CanCapture ||
            ForegroundTracker.IsOwnWindow(Native.GetForegroundWindow())) return false;
        _snippet.Mark = settings.SnippetMark;
        if (_snippet.Expand(composition.Snippets) is not { } expansion) return false;
        lock (_swallowedToggleUps) _swallowedToggleUps.Add(e.Vk);
        _lastSnippet = (expansion.Text, expansion.Typed);
        composition.Post(() => composition.InsertSnippet(expansion.Delete, expansion.Text));
        _line.Invalidate();
        Log.Info($"定型文を入れました ({expansion.Text.Length} 文字)。");
        return true;
    }
    private long _lineVersion;
    private System.Threading.Timer? _lineTimer;
    private volatile bool _codeJapanese;
    // ほかのソフトが送ったキーを通したことを、1 回だけログに書いたか (#250)
    private bool _injectedHintLogged;
    private LineKind? _lastLineKind;

    /// <summary>
    /// 前面のアプリが「コード」(コードエディター・ターミナル) なら、フォーカスのある入力欄の種類。
    /// 文章のファイル (README.md など) を開いているときや、チャット・AI への入力欄 (Copilot Chat・Claude Code の画面) なら None。
    /// </summary>
    private CodeFocus CurrentCodeFocus(Settings settings)
    {
        var app = _foreground.Current;
        if (settings.ProfileFor(app.ProcessName) != AppProfile.Code || LineContext.IsDocumentTitle(KeyText.WindowTitle(app.Window))) return CodeFocus.None;
        var focus = _composition?.Focus.Current;
        return LineContext.ClassifyFocus(app.ProcessName, focus?.Name ?? "", focus?.ClassName ?? "");
    }

    private bool IsCodeApp(Settings settings) => CurrentCodeFocus(settings) != CodeFocus.None;

    /// <summary>
    /// 前面のアプリが「コード」で、キャレットがコード (コメント・文字列・AI の入力行の外) にあるか。フックのスレッドで呼ばれる。
    /// 今の行が分からないときはコードとみなし、UI Automation で読みに行く。
    /// </summary>
    private bool InCode(Settings settings)
    {
        if (_codeJapanese || !IsCodeApp(settings)) return false;
        var line = _line.Text;
        if (line is null)
        {
            RequestLine(0);
            return true;
        }
        return LineContext.ClassifyText(line) == LineKind.Code;
    }

    /// <summary>
    /// 打ち始めの / を、・ にせずそのままアプリへ渡すか。前面のアプリが「コード」で、キャレットがコードの中か、
    /// ターミナルの AI の入力行 (Claude Code・Codex の /command を打つ所) にあるとき。コメント・文字列の中は ・ にする。
    /// </summary>
    private bool PassesSlash(Settings settings) =>
        InCode(settings) || !_codeJapanese && IsCodeApp(settings) && _line.Text is { } line && LineContext.ClassifyText(line) == LineKind.Prompt;

    /// <summary>
    /// 入力欄にフォーカスが移ったとき (フォーカスを調べるスレッドから呼ばれる)。次に打つキーが 日本語 / 英数 のどちらで入るかを出す。
    /// 「コード」のアプリのコードの行では、日本語入力のままでも英数で入るので「A」を出す。
    /// </summary>
    private void ShowModeOnFocus()
    {
        if (_composition is not { } composition) return;
        if (_keyboardDirect || !IsCodeApp(_settings))
        {
            composition.ShowMode(!_keyboardDirect);
            return;
        }
        if (_line.IsKnown)
        {
            ShowCodeMode();
            return;
        }
        // 今の行が分からないので、読んでから出す (読めなければコードとみなして「A」)。
        var version = Interlocked.Read(ref _lineVersion);
        composition.Focus.RequestTextBeforeCaret(before =>
        {
            // 読んでいる間にキャレットが動いたなら出さない。
            if (version != Interlocked.Read(ref _lineVersion)) return;
            if (before is not null && !_line.IsKnown) _line.SetFromText(before);
            ShowCodeMode();
        });
    }

    /// <summary>
    /// 「コード」のアプリで、次に打つキーが 日本語 / 英数 のどちらで入るかを出す。
    /// changedOnly なら、最後に出したものから変わったときだけ出す (改行・キャレットの移動の後)。
    /// </summary>
    private void ShowCodeMode(bool changedOnly = false)
    {
        if (_composition is not { } composition) return;
        if (changedOnly && (_lastLineKind is null || !_line.IsKnown && !_codeJapanese || !IsCodeApp(_settings))) return;
        var kind = InCode(_settings) ? LineKind.Code : LineKind.Comment;
        if (changedOnly && kind == _lastLineKind) return;
        // 打ち始めの英字で同じ表示を出し直さないように、出した種類を覚えておく。
        _lastLineKind = kind;
        composition.ShowMode(kind != LineKind.Code);
    }

    /// <summary>アプリへ届いたキーで、今の行を追いかける。</summary>
    private void TrackLine(KeyEvent e)
    {
        if (!e.IsDown || VirtualKeys.IsModifier(e.Vk)) return;
        var terminal = CurrentCodeFocus(_settings) == CodeFocus.Terminal;
        if (e.Vk == VirtualKeys.Return)
        {
            if (terminal)
            {
                // ターミナルの次の行のプロンプト (Claude Code の「> 」など) は出力なので、出てから読む。
                // AI の入力で 半角/全角 を押して日本語にしていたら、続けて日本語のまま。
                InvalidateLine(resetJapanese: false, delayMs: 300);
                _sigil.Start();
                _snippet.Start();
                return;
            }
            // 半角/全角 で日本語にしていたら、改行しても日本語のまま (コメント・文書を続けて書く。改行のたびに戻っていた: issue #214)。
            // クリック・矢印でキャレットを別の場所に動かすか、もう一度 半角/全角 を押すと戻る
            _line.NewLine();
            _sigil.Start();
            _snippet.Start();
            ShowCodeMode(changedOnly: true);
            return;
        }
        if (e.Vk == VirtualKeys.Back)
        {
            _line.Backspace();
            _sigil.Backspace();
            _snippet.Backspace();
            return;
        }
        if (e.Vk == VirtualKeys.Escape) return;
        // Ctrl・Alt・Win の操作 (貼り付け・元に戻す …)、Tab (補完)、キャレットを動かすキーの後は分からない。
        if (IsDown(VirtualKeys.Control) || IsDown(VirtualKeys.Menu) || IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin) ||
            e.Vk is VirtualKeys.Tab or (>= 0x21 and <= 0x28) or 0x2E)
        {
            InvalidateLine(resetJapanese: !terminal);
            return;
        }
        if (KeyText.CharFromKey(e.Vk, e.Scan, false) is { } c)
        {
            _line.Append(c.ToString());
            _sigil.Append(c);
            _snippet.Append(c.ToString());
        }
    }

    /// <param name="resetJapanese">半角/全角 で日本語にしていた行の設定も戻すか (キャレットが別の場所に動いたとき)。</param>
    /// <param name="keepSigil">/ $ @ の名前の判定 (前の文字) をそのままにするか。</param>
    private void InvalidateLine(bool resetJapanese = true, int delayMs = 80, bool keepSigil = false)
    {
        _line.Invalidate();
        if (!keepSigil)
        {
            _sigil.Lose();
            _snippet.Lose();
        }
        if (resetJapanese) _codeJapanese = false;
        Interlocked.Increment(ref _lineVersion);
        // キャレットの移動がアプリに届くのを少し待ってから読む。
        // 定型文を使うときは、コード以外のアプリでも読む (クリックした後の行頭の ;名前 を変換ボックスに入れないように)。
        if (_settings.ProfileFor(_foreground.Current.ProcessName) == AppProfile.Code || SnippetsEnabled(_settings)) RequestLine(delayMs);
    }

    /// <summary>UI Automation で、今の行のキャレットより前を読む (delayMs 後)。</summary>
    private void RequestLine(int delayMs)
    {
        if (_composition is not { } composition) return;
        var version = Interlocked.Read(ref _lineVersion);
        void Read() => composition.Focus.RequestTextBeforeCaret(before =>
        {
            // 読んでいる間にキャレットが動いた・打鍵で分かったなら使わない。
            if (before is null || version != Interlocked.Read(ref _lineVersion) || _line.IsKnown) return;
            _line.SetFromText(before);
            ShowCodeMode(changedOnly: true);
        });
        if (delayMs <= 0)
        {
            Read();
            return;
        }
        var timer = new System.Threading.Timer(_ => Read(), null, delayMs, Timeout.Infinite);
        Interlocked.Exchange(ref _lineTimer, timer)?.Dispose();
    }

    /// <summary>フックのスレッドで呼ばれる。この打鍵で変換ボックスを開くか。</summary>
    private bool StartsComposition(KeyEvent e)
    {
        if (!e.IsDown || e.Injected) return false;
        var settings = _settings;
        if (!settings.Enabled || settings.Mode != InputMode.Keyboard || !KeyboardLayoutPolicy.AllowsInput(settings)) return false;
        var letter = VirtualKeys.IsLetter(e.Vk);
        var reconvert = e.Vk == VirtualKeys.Convert;
        // 句読点・かぎかっこ・長音・数字・記号のキー (Shift を押して打つ ＃＄％（）＠ なども) でも変換ボックスを開く。
        // 日本語の中では全角、英語の中では半角になる。
        var punctuation = IsDigitOrPunctuationKey(e.Vk);
        // かな入力 (JIS): かなのキー (数字・記号のキーも含む) はすべて入力を始める。
        if (settings.InputStyle == InputStyle.Kana && !_keyboardDirect && KanaDetector.IsKanaKey(e.Vk)) punctuation = true;
        // 日本語入力のときの Shift+Space は全角スペース (Microsoft IME と同じ: issue #24)。
        if (e.Vk == VirtualKeys.Space && !_keyboardDirect && (IsDown(VirtualKeys.Shift) || settings.FullWidthSpaceByDefault)) punctuation = true;
        if (_keyboardDirect && !reconvert)
        {
            // 英数状態: ローマ字かどうかを判定するために、単語の打ち始めの英字だけを受け取る。
            // 英語と分かった単語の続きは、区切り (Space など) まで素通しする。
            if (!letter || !settings.DirectModeAutoDetect || settings.ForApp(_foreground.Current.ProcessName).DetectionLevel == DetectionLevel.Manual || _directEnglishWord) return false;
        }
        else if (!reconvert && !letter && !punctuation)
        {
            return false;
        }
        if (IsDown(VirtualKeys.Control) || IsDown(VirtualKeys.Menu) || IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin)) return false;
        // 先頭か空白の直後の ;名前 (定型文) は、Space / Tab まで変換せずにそのままアプリへ渡す (#290)。
        if (!reconvert && SnippetsEnabled(settings) && KeyText.CharFromKey(e.Vk, e.Scan, false) is { } snippetChar)
        {
            _snippet.Mark = settings.SnippetMark;
            if (_snippet.PassesThrough(snippetChar, _line.Text)) return false;
        }
        // かな入力では / などのキーはかな (め) なので対象にしない。
        // 文章の入力欄 (コードエディターのコード以外) の / や「/ キーで中黒」設定時は、変換ボックスに ・ として入れる。Space で / にすると、続く名前はそのまま渡す。
        var slashAtStart = false;
        if (!reconvert && settings.SigilWordsDirect && (_keyboardDirect || settings.InputStyle != InputStyle.Kana) &&
            KeyText.CharFromKey(e.Vk, e.Scan, false) is { } typed &&
            _sigil.PassesThrough(typed, _line.Text))
        {
            slashAtStart = typed == '/' && !_keyboardDirect && !_sigil.IsActive &&
                           (settings.SlashAsMiddleDot || !PassesSlash(settings));
            if (!slashAtStart)
            {
                if (!_sigil.IsActive) Log.Info($"{typed} で始まる語: 空白までそのまま入力します");
                return false;
            }
        }
        if (!_foreground.Check(settings).Allowed) return false;
        // 文字入力欄 (パスワード以外) にフォーカスがあるときだけ。ショートカットキーやゲームの操作を横取りしない。
        if (_composition?.Focus.CanCaptureWaiting() != true &&
            !(RechecksStaleFocus(_foreground.Current.ProcessName) && _composition?.Focus.RecheckStale() == true) &&
            !RecheckAfterClick()) return false;
        // コードエディター・ターミナル: コードの中は英数のまま通す (補完もそのまま効く)。コメント・文字列の中は日本語を判定する。
        if (!reconvert && !_keyboardDirect && IsCodeApp(settings))
        {
            var code = InCode(settings);
            var kind = code ? LineKind.Code : LineKind.Comment;
            if (letter && _lastLineKind is { } last && last != kind) _composition?.ShowMode(!code);
            if (letter) _lastLineKind = kind;
            if (code) return false;
        }
        if (slashAtStart) _composition?.Controller.MarkSlashAtStart();
        return true;
    }

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

    /// <summary>英数状態での判定結果 (UI スレッドから呼ばれる)。</summary>
    public void OnDirectDecided(bool japanese)
    {
        if (japanese) KeyboardDirect = false;
        else _directEnglishWord = true;
    }

    /// <summary>
    /// クリックの直後に、入力欄ではない所で打ち始めた: クリックで移るはずのフォーカスが、まだ前の場所のまま調べ終わっていることがある
    /// (入力欄をクリックしてすぐ打つと、最初の文字が変換ボックスを通らずに消えていた)。今のフォーカスを調べ直させて、最大 100 ms 待つ。
    /// </summary>
    private bool RecheckAfterClick()
    {
        if (_composition is not { } composition) return false;
        var capture = composition.Focus.RecheckAfterClick(out var waited);
        if (waited >= 0) Log.Info($"クリック直後の打ち始め: フォーカスを調べ直した ({waited} ms) → {(capture ? "入力欄なので変換ボックスに入れる" : "入力欄ではない")}");
        return capture;
    }

    private bool OnMouseButton(Composition.MouseButtonEvent e)
    {
        if (IsButtonDown(e.Message)) _altTap.Cancel();
        if (_settings.Mode == InputMode.Tsf) return false;
        if (_settings.Mode == InputMode.Keyboard && _composition is { } composition)
        {
            if (composition.Gate.OnMouseButton(e)) return true;
            if (IsButtonDown(e.Message))
            {
                composition.Focus.NoteClick();
                composition.Focus.Invalidate();
                composition.ResetContext();
                InvalidateLine();
            }
            return false;
        }
        if (IsButtonDown(e.Message)) InvalidateLine();
        return false;
    }

    private static bool IsButtonDown(int message) =>
        message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN;

    /// <summary>Meltype キーボード使用中は Microsoft IME を閉じておく (二重に変換されないように)。</summary>
    private void CloseSystemImeAsync()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (!IsKeyboardActive || ForegroundTracker.IsOwnWindow(Native.GetForegroundWindow()) || ImeTarget.FromForeground() is not { } target ||
                    !KeyboardLayoutPolicy.AllowsInput(_settings, target)) return;
                // アプリ別設定で OFF にしたアプリ (・ゲーム) では、Microsoft IME をそのまま使ってもらう (OFF に戻さない: issue #233)
                if (!_foreground.Check(_settings).Allowed) return;
                var state = _imm32.GetState(target);
                if (!KeyboardLayoutPolicy.AllowsInput(_settings, state.KeyboardLayout)) return;
                if (state.Mode == IME.ImeMode.Open && _imm32.TrySetOpen(target, false, null, _settings.ImeTimeoutMs))
                {
                    Log.Info("Meltype キーボードを使うため Microsoft IME を OFF にしました。");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Microsoft IME を OFF にできませんでした: {ex.Message}");
            }
        });
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
        if (!next.Enabled || next.Mode != InputMode.Keyboard || !KeyboardLayoutPolicy.AllowsInput(next)) _composition?.Flush();
        if (next.Mode == InputMode.Keyboard && (previous.Mode != InputMode.Keyboard || !previous.Enabled))
        {
            EndTakeover("動作モードか有効・無効を変えた");
            CloseSystemImeAsync();
        }
        // Meltype IME のモードにしたら (トレイでも設定画面でも)、いま使う入力方式も Meltype IME にする (Win + Space で選び直さなくてよいように)
        if (SwitchesToTsf(previous.Mode, next.Mode)) Tip.TipServer.ActivateForSession();
        // 「入力欄とみなすアプリ」を変えたら、今のフォーカスを調べ直す (前面のアプリに戻ったときを待たずに効かせる)。
        if (previous.TextInputApps != next.TextInputApps) _composition?.Focus.Invalidate();
        Log.SetFileOutput(next.FileLog ? AppPaths.LogFile : null);
        Log.RecordText = next.LogTypedText;
        if (_configPath is not null)
        {
            try { next.Save(_configPath); }
            catch (Exception ex) { Log.Warn($"config.json を保存できませんでした: {ex.Message}"); }
        }
        Log.Info($"設定を反映しました (モード: {next.Mode}, 有効: {(next.Enabled ? "ON" : "OFF")}, 判定の強さ: {next.DetectionLevel}, 閾値: {next.EffectiveJapaneseThreshold}, 入力方式: {next.InputStyle})。");
        StatusChanged?.Invoke();
    }

    /// <summary>動作モードを Meltype IME に変えたか (使えずに元のモードに戻したときは、戻した後のモードで見る)。</summary>
    internal static bool SwitchesToTsf(InputMode previous, InputMode next) => previous != InputMode.Tsf && next == InputMode.Tsf;

    public DetectionResult Evaluate(string letters, Settings? settings = null) =>
        _scoreEngine.Evaluate(new DetectionInput(letters, letters.Select(c => (int)char.ToUpperInvariant(c)).ToArray(), IsFinal: false), settings ?? _settings);

    // ---- フック・タイマーからの通知 ----

    private void SuspendForInputLanguage()
    {
        _directEnglishWord = false;
        _codeJapanese = false;
        _line.Invalidate();
        _sigil.Lose();
        _snippet.Lose();
        Interlocked.Increment(ref _lineVersion);
        _lastLineKind = null;
    }

    private void OnFocusChanged()
    {
        InvalidateLine(keepSigil: true);
        // 移った先の表示は、入力欄と分かったときに出す (ShowModeOnFocus)。
        _lastLineKind = null;
        // 別の入力欄に移った。次に打つ文字は先頭とみなす (/ $ @ の名前の途中なら続ける)。
        _sigil.FocusMoved();
        _snippet.FocusMoved();
        _composition?.Focus.Invalidate();
        _directEnglishWord = false;
        _composition?.ResetContext();
    }

    private void OnForegroundChanged(IntPtr window)
    {
        // 変換中に別のウィンドウに切り替わったら、変換中の内容は捨てる (切り替わった先のアプリに入らないように)。
        _composition?.Abandon("別のウィンドウに切り替わった");
        // 起動の途中から Windows の IME で打っていた語も、ここで終わる (Meltype キーボードを使うなら下で閉じる)
        _imeMaybeComposing = false;
        EndTakeover("別のウィンドウに切り替わった");
        _foreground.Refresh(window);
        InvalidateLine();
        _sigil.Start();
        _snippet.Start();
        // 置き換えた直後の Esc で戻すのは、置き換えたウィンドウの中だけ
        _lastSnippet = null;
        _lastLineKind = null;
        var app = _foreground.Current;
        if (_settings.ProfileFor(app.ProcessName) == AppProfile.Code)
        {
            Log.Info($"{app.ProcessName} は「コード」: コメントと文字列の中だけ日本語を判定します (半角/全角 でこの行だけ日本語)。");
        }
        _composition?.Focus.InvalidateForeground();
        _directEnglishWord = false;
        _composition?.ResetContext();
        // 独自の種類で「最初は英数」にしたアプリに切り替えたら英数から始める。
        if (_settings is { Enabled: true, Mode: InputMode.Keyboard } && !_keyboardDirect && _settings.KindFor(app.ProcessName) is { StartInEnglish: true } kind)
        {
            Log.Info($"{app.ProcessName} は「{kind.Name}」: 英数から始めます。");
            KeyboardDirect = true;
        }
        // ゲームでは Windows の IME に触らない (ゲームのチャットを Windows の IME で打てるように)
        if (_settings.IsGame(app.ProcessName, app.LooksLikeGame))
        {
            Log.Info($"{app.ProcessName} はゲーム: Meltype は何もしません (Windows の IME で打てます)。");
            return;
        }
        if (IsKeyboardActive) CloseSystemImeAsync();
    }

    private void PollImeState()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            if (!_settings.Enabled || _settings.Mode == InputMode.Tsf) return;
            // Meltype 自身の画面には IME の問い合わせを送らない (設定のドロップダウンが閉じてしまう)。
            if (ForegroundTracker.IsOwnWindow(Native.GetForegroundWindow())) return;
            var target = ImeTarget.FromForeground();
            if (target is null) return;
            if (!KeyboardLayoutPolicy.AllowsInput(_settings, target))
            {
                _imeSnapshot = null;
                SuspendForInputLanguage();
                return;
            }
            var state = _imm32.GetState(target);
            _imeSnapshot = new ImeSnapshot(state);
            if (TakeoverPending)
            {
                // 起動の途中から Windows の IME で語を打っている間は閉じない。IME が OFF と分かれば、変換中の文字は無い (分からないときは待つ)
                var closed = state.Mode == IME.ImeMode.Closed;
                if (!closed && StillTypingInSystemIme()) return;
                if (EndTakeover(closed ? "IME が OFF" : "Enter で確定した") && IsKeyboardActive) CloseSystemImeAsync();
                return;
            }
            if (_settings.Mode == InputMode.Keyboard) KeepSystemImeClosed(target, state);
        }
        catch (Exception ex)
        {
            Log.Warn($"IME 状態の取得に失敗: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private IntPtr _lastLayout;
    private long _lastForcedClose;
    private int _forcedCloses;

    /// <summary>
    /// Meltype キーボードを使っている間は、Windows の IME (Microsoft IME・Google 日本語入力など) を OFF に保つ。
    /// Win+Space などで IME を切り替えると、新しい IME が ON の状態で始まることがあり、そのままだと
    /// Meltype が通したキーを IME が変換してしまう (Meltype の変換ボックスが出たり IME の変換が出たりする)。
    /// </summary>
    private void KeepSystemImeClosed(ImeTarget target, ImeState state)
    {
        if (!KeyboardLayoutPolicy.AllowsInput(_settings, target) || !KeyboardLayoutPolicy.AllowsInput(_settings, state.KeyboardLayout)) return;
        var layout = Native.GetKeyboardLayout(target.ThreadId);
        if (layout != _lastLayout)
        {
            if (_lastLayout != IntPtr.Zero)
            {
                Log.Info($"キーボード/IME が切り替わりました (HKL {_lastLayout:X} → {layout:X}, IME: {state})。");
                _composition?.Focus.Invalidate();
            }
            _lastLayout = layout;
        }
        if (state.Mode != IME.ImeMode.Open) return;
        // アプリ別設定で OFF にしたアプリ (・ゲーム) では、Microsoft IME をそのまま使ってもらう (CloseSystemImeAsync と同じ)
        if (!_foreground.Check(_settings).Allowed) return;
        var composition = Volatile.Read(ref _composition);
        // 変換ボックスで入力中は、確定の直前に閉じる (CompositionService) ので触らない。起動の途中から Windows の IME で打っている間も触らない
        if (composition is null || composition.Gate.IsCaptured || TakeoverPending) return;
        var now = Environment.TickCount64;
        // ユーザーが IME を ON にし続ける (閉じてもすぐ開く) なら、10 秒に 3 回までにして奪い合わない。
        if (now - _lastForcedClose > 10000) _forcedCloses = 0;
        if (_forcedCloses >= 3) return;
        _forcedCloses++;
        _lastForcedClose = now;
        var closed = _imm32.TrySetOpen(target, false, null, _settings.ImeTimeoutMs);
        Log.Warn($"Meltype キーボードの使用中に Windows の IME が ON になっていました ({state}) → {(closed ? "OFF にしました" : "OFF にできませんでした")}。" +
                 (_forcedCloses == 3 ? " (続けて ON になるため、しばらく自動で OFF にしません。IME を使うときは Ctrl+半角/全角 で Meltype を一時停止してください)" : ""));
    }

    public ImeState? CurrentImeState => _imeSnapshot?.State;

    private static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor.Dispose();
        _pollTimer.Dispose();
        _remote.Dispose();
        _lineTimer?.Dispose();
        Log.Info("Meltype を終了しました。");
        Log.FlushFile();
    }
}
