// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Collections.Concurrent;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Diagnostics;
using Meltype.IME;
using Meltype.Input;
using Meltype.Learning;

namespace Meltype;

/// <summary>
/// 各モジュールをつなぐ (設計書 §4)。スレッドは 3 種類:
///   フックのスレッド   … KeyboardMonitor。InputSession に打鍵を渡すだけ。
///   再入力ワーカー     … IME 切替と保留分の再入力。待ちが発生する処理はすべてここ (STA)。
///   タイマー           … 無入力での出力、IME 状態のポーリング、学習データの保存。
/// </summary>
internal sealed class MeltypeEngine : ISessionEnvironment, IDisposable
{
    private const int ImeStateFreshMs = 1000;

    private readonly string? _configPath;
    private readonly UserModel _userModel;
    private readonly ScoreEngine _scoreEngine;
    private readonly InputSession _session;
    private readonly ForegroundTracker _foreground = new();
    private readonly Imm32ImeController _imm32 = new();
    private readonly TsfImeController _tsf = new();
    private readonly ImeController _ime;
    private readonly KeyInjector _injector = new();
    private readonly KeyboardMonitor _monitor;
    private readonly BlockingCollection<FlushRequest> _flushQueue = new();
    private readonly Thread _worker;
    private readonly System.Threading.Timer _sessionTimer;
    private readonly System.Threading.Timer _pollTimer;
    private readonly System.Threading.Timer _saveTimer;
    private volatile Settings _settings;
    private volatile ImeSnapshot? _imeSnapshot;
    private string? _lastDenyReason;
    private int _polling;
    private bool _disposed;

    private sealed record ImeSnapshot(IntPtr Window, ImeState State, long Time);

    public event Action? StatusChanged;

    /// <summary>Ctrl + 半角/全角 が押された (フックのスレッドから呼ばれる。受け取った側で UI スレッドに移して切り替える)。</summary>
    public event Action? ToggleRequested;

    /// <summary>判定の強さが「手動」で、IME 自動切替が日本語入力を提案した (ワーカースレッドから呼ばれる)。</summary>
    public event Action? ImeSuggested;

    private readonly HashSet<int> _toggleKeyDown = [];

    public MeltypeEngine(Settings settings, string? configPath, string? modelPath, string? userDictionaryDirectory)
    {
        _settings = settings.Clone().Normalize();
        _configPath = configPath;
        Log.SetFileOutput(_settings.FileLog ? AppPaths.LogFile : null);
        Log.RecordText = _settings.LogTypedText;

        _userModel = new UserModel(modelPath);
        _scoreEngine = ScoreEngine.CreateDefault(_userModel, () => _settings, userDictionaryDirectory);
        _session = new InputSession(_scoreEngine, () => _settings, this);
        _ime = new ImeController(_imm32, _tsf, () => _settings);
        _monitor = new KeyboardMonitor(OnKey, OnMouseButton, OnForegroundChanged, OnFocusChanged);
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Meltype reinjection" };
        _worker.SetApartmentState(ApartmentState.STA);

        _sessionTimer = new System.Threading.Timer(_ => SafeRun(() => _session.OnTimer(Environment.TickCount64)), null, Timeout.Infinite, Timeout.Infinite);
        _pollTimer = new System.Threading.Timer(_ => PollImeState(), null, Timeout.Infinite, Timeout.Infinite);
        _saveTimer = new System.Threading.Timer(_ => SafeRun(_userModel.Save), null, Timeout.Infinite, Timeout.Infinite);
    }

    public Settings Settings => _settings;

    /// <summary>前面のアプリの独自の種類 (判定の強さ・ライブ変換) を反映した設定。</summary>
    public Settings AppSettings => _settings.ForApp(_foreground.Current.ProcessName);

    public UserModel UserModel => _userModel;

    public DetectionResult? LastDecision => _session.LastDecision;

    public void Start()
    {
        _worker.Start();
        _monitor.Start();
        _sessionTimer.Change(50, 50);
        _pollTimer.Change(0, 250);
        _saveTimer.Change(30_000, 30_000);
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

    /// <summary>UI スレッドで作った変換ボックスをつなぐ。</summary>
    public void AttachComposition(Composition.CompositionService composition)
    {
        _composition = composition;
        composition.PasteCommit = () => PastePolicy.ShouldPaste(_settings, _foreground.Current.ProcessName, IsQt(_foreground.Current.Window));
        composition.InputAllowed = () => KeyboardLayoutPolicy.AllowsInput(_settings);
        composition.Focus.TreatsAsTextInput = () => _settings.TreatsAsTextInput(_foreground.Current.ProcessName);
        // 変換ボックスで確定した文字と、Meltype が送り直したキーも、今の行の追いかけに入れる (自分で送ったキーはフックに届かない)。
        composition.Controller.Committed += text =>
        {
            _line.Append(text);
            _sigil.Append(text);
        };
        composition.Controller.ReconversionCommitted += () => InvalidateLine();
        composition.KeyReplayed += e => TrackLine(e);
        composition.MouseReplayed += () => InvalidateLine();
        composition.Focus.Invalidate();
        if (IsKeyboardActive) CloseSystemImeAsync();
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
    /// <summary>入力欄にフォーカスが移っても UI Automation の通知が来ないことがあるブラウザー (#364)。打ったときに調べ直す。</summary>
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "firefox.exe", "chrome.exe", "msedge.exe", "vivaldi.exe", "brave.exe", "opera.exe", "floorp.exe", "zen.exe", "waterfox.exe", "librewolf.exe" };

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
            _composition?.ShowMode(!value);
            StatusChanged?.Invoke();
        }
    }

    private bool OnKey(KeyEvent e)
    {
        var settings = _settings;
        // 遠隔操作 (AnyDesk・VNC) のキーは、ほかのソフトが送ったキー (LLKHF_INJECTED) として届く。設定で許可していれば手で打ったキーとして扱う。
        // Meltype 自身が送り直したキーは、印 (InjectedMarker) でフックの入口で除いているので、ここには来ない (自分の出力を処理し直さない)。
        if (e.Injected && settings.AllowInjectedInput) e = e with { Injected = false };
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
            SuspendForInputLanguage(e.TimeMs);
            // 既に保留中なら順序を保って送り直す。それ以外の打鍵はそのまま通す。
            if (settings.Mode == InputMode.Keyboard)
                return _composition?.Gate.OnKey(e, _ => false) == true;
            return _session.State == SessionState.Flushing && _session.OnKey(e);
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
        if (settings.Mode == InputMode.Tsf) return false;
        if (settings.Mode != InputMode.Keyboard)
        {
            if (!e.Injected) TrackLine(e);
            return _session.OnKey(e);
        }
        // 変換ボックスの準備前・終了処理中は何もしない (素通し)。
        if (_composition is not { } composition) return false;

        // 左 Alt の単独押しで英数、右 Alt の単独押しで日本語。Alt の押下・解放はそのままアプリに通す。
        if (altTap != AltTap.None && settings.Enabled && settings.AltKeysSwitchKeyboard && !composition.Gate.IsCaptured)
        {
            // 単独押しでメニューバーに移らないよう、押している間に何もしないキーを送る (Alt + ` と同じ)。
            if (altTap == AltTap.Pressed) ThreadPool.QueueUserWorkItem(_ => KeyInjector.SendKey(0xE8));
            else SetKeyboardMode(composition, settings, japanese: e.Vk == VirtualKeys.RMenu);
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
        // 半角/全角 キーは Microsoft IME ではなく Meltype キーボードの ON/OFF に使う。
        if (settings.Enabled && settings.HankakuTogglesKeyboard && VirtualKeys.IsHankakuZenkaku(e.Vk) && !e.Injected && !composition.Gate.IsCaptured)
        {
            if (e.IsDown)
            {
                lock (_swallowedToggleUps) _swallowedToggleUps.Add(e.Vk);
                MaskAltRelease();
                // コードの行 (コメント・文字列の外) では、この行だけ日本語にする / 戻す。
                if (!_keyboardDirect && IsCodeApp(settings) && (_codeJapanese || InCode(settings)))
                {
                    _codeJapanese = !_codeJapanese;
                    Log.Info(_codeJapanese ? "コードの行: 日本語で入力 (エディターは改行まで、ターミナルは別の場所に移るまで)" : "コードの行: 英数に戻す");
                    composition.ShowMode(_codeJapanese);
                    return true;
                }
                KeyboardDirect = !_keyboardDirect;
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
                composition.ShowMode(false);
                return;
            }
            if (_keyboardDirect) composition.ShowMode(false);
            else KeyboardDirect = true;
            return;
        }
        // 英数状態からなら KeyboardDirect の切り替えで「あ」を出す。
        var wasDirect = _keyboardDirect;
        if (wasDirect) KeyboardDirect = false;
        if (InCode(settings))
        {
            _codeJapanese = true;
            Log.Info("コードの行: 日本語で入力 (エディターは改行まで、ターミナルは別の場所に移るまで)");
        }
        if (!wasDirect) composition.ShowMode(true);
    }

    /// <summary>
    /// Alt を押したまま打ったキー (US 配列の Alt + ` = 半角/全角) を飲み込むと、アプリには Alt だけを押して離したように見え、
    /// メニューバーに移ってしまう。何もしないキー (0xE8、割り当てなし) を送って、Alt の単独押しにしない。
    /// </summary>
    private static void MaskAltRelease()
    {
        if (IsDown(VirtualKeys.Menu)) ThreadPool.QueueUserWorkItem(_ => KeyInjector.SendKey(0xE8));
    }

    // ---- アプリの種類「コード」: コメント・文字列の中だけ日本語 ----

    private readonly LineTracker _line = new();
    // 先頭か空白の直後の /command・$skill・@ファイル名 (#193)。
    private readonly Composition.SigilWord _sigil = new();
    private long _lineVersion;
    private System.Threading.Timer? _lineTimer;
    private volatile bool _codeJapanese;
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
                return;
            }
            _line.NewLine();
            _sigil.Start();
            _codeJapanese = false;
            return;
        }
        if (e.Vk == VirtualKeys.Back)
        {
            _line.Backspace();
            _sigil.Backspace();
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
        }
    }

    /// <param name="resetJapanese">半角/全角 で日本語にしていた行の設定も戻すか (キャレットが別の場所に動いたとき)。</param>
    /// <param name="keepSigil">/ $ @ の名前の判定 (前の文字) をそのままにするか。</param>
    private void InvalidateLine(bool resetJapanese = true, int delayMs = 80, bool keepSigil = false)
    {
        _line.Invalidate();
        if (!keepSigil) _sigil.Lose();
        if (resetJapanese) _codeJapanese = false;
        Interlocked.Increment(ref _lineVersion);
        // キャレットの移動がアプリに届くのを少し待ってから読む。
        if (_settings.ProfileFor(_foreground.Current.ProcessName) == AppProfile.Code) RequestLine(delayMs);
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
        var punctuation = e.Vk is >= 0x30 and <= 0x39 or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;
        // かな入力 (JIS): かなのキー (数字・記号のキーも含む) はすべて入力を始める。
        if (settings.InputStyle == InputStyle.Kana && !_keyboardDirect && KanaDetector.IsKanaKey(e.Vk)) punctuation = true;
        // 日本語入力のときの Shift+Space は全角スペース (Microsoft IME と同じ: issue #24)。
        if (e.Vk == VirtualKeys.Space && !_keyboardDirect && IsDown(VirtualKeys.Shift)) punctuation = true;
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
        // 先頭か空白の直後の /command・$skill・@ファイル名 は、名前の終わり (空白) まで変換せずにそのままアプリへ渡す (#193)。
        // かな入力では / などのキーはかな (め) なので対象にしない。
        if (!reconvert && settings.SigilWordsDirect && (_keyboardDirect || settings.InputStyle != InputStyle.Kana) &&
            KeyText.CharFromKey(e.Vk, e.Scan, false) is { } typed && _sigil.PassesThrough(typed, _line.Text))
        {
            if (!_sigil.IsActive) Log.Info($"{typed} で始まる語: 空白までそのまま入力します");
            return false;
        }
        if (!_foreground.Check(settings).Allowed) return false;
        // 文字入力欄 (パスワード以外) にフォーカスがあるときだけ。ショートカットキーやゲームの操作を横取りしない。
        if (_composition?.Focus.CanCapture != true && _composition?.Focus.CanCaptureWaiting() != true &&
            !(Browsers.Contains(_foreground.Current.ProcessName) && _composition?.Focus.RecheckStale() == true)) return false;
        // コードエディター・ターミナル: コードの中は英数のまま通す (補完もそのまま効く)。コメント・文字列の中は日本語を判定する。
        if (!reconvert && !_keyboardDirect && IsCodeApp(settings))
        {
            var code = InCode(settings);
            var kind = code ? LineKind.Code : LineKind.Comment;
            if (letter && _lastLineKind is { } last && last != kind) _composition?.ShowMode(!code);
            if (letter) _lastLineKind = kind;
            if (code) return false;
        }
        return true;
    }

    /// <summary>英数状態で打ち始めた英字がローマ字 (日本語) かを、IME 自動切替と同じ判定器で調べる。UI スレッドから呼ばれる。</summary>
    public Verdict ClassifyDirect(string letters, bool final)
    {
        var settings = AppSettings.Clone();
        // かな入力なら打鍵をかな配列として、それ以外はローマ字として判定する。
        settings.InputStyle = settings.InputStyle == InputStyle.Kana ? InputStyle.Kana : InputStyle.Romaji;
        var keys = letters.Select(c => (int)char.ToUpperInvariant(c)).ToArray();
        var result = _scoreEngine.Evaluate(new DetectionInput(letters, keys, final), settings);
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

    private bool OnMouseButton(Composition.MouseButtonEvent e)
    {
        if (IsButtonDown(e.Message)) _altTap.Cancel();
        if (_settings.Mode == InputMode.Tsf) return false;
        if (_settings.Mode == InputMode.Keyboard && _composition is { } composition)
        {
            if (composition.Gate.OnMouseButton(e)) return true;
            if (IsButtonDown(e.Message))
            {
                composition.Focus.Invalidate();
                composition.ResetContext();
                InvalidateLine();
            }
            return false;
        }
        if (IsButtonDown(e.Message))
        {
            _session.OnContextChanged(Environment.TickCount64);
            InvalidateLine();
        }
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
        // 設定画面で Meltype IME を選んだが、登録されていない: どこでも何も起きなくなるので選ばせない
        if (next.Mode == InputMode.Tsf && previous.Mode != InputMode.Tsf && !Tip.TipServer.IsRegistered)
        {
            next.Mode = previous.Mode;
            Log.Warn("Meltype IME が登録されていないので、動作モードは変えません (Install.cmd か Install-Meltype.ps1 で Meltype IME を入れてください)。");
        }
        _settings = next;
        if (!next.Enabled || next.Mode != InputMode.Keyboard || !KeyboardLayoutPolicy.AllowsInput(next)) _composition?.Flush();
        if (next.Mode == InputMode.Keyboard && (previous.Mode != InputMode.Keyboard || !previous.Enabled)) CloseSystemImeAsync();
        if (!next.Enabled) FlushAbandoned(_session.Abort());
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

    public DetectionResult Evaluate(string letters, Settings? settings = null) =>
        _scoreEngine.Evaluate(new DetectionInput(letters, letters.Select(c => (int)char.ToUpperInvariant(c)).ToArray(), IsFinal: false), settings ?? _settings);

    // ---- ISessionEnvironment (InputSession のロック内から呼ばれる。重い処理はしない) ----

    bool ISessionEnvironment.IsModifierDown() =>
        IsDown(VirtualKeys.Control) || IsDown(VirtualKeys.Menu) || IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin) || IsDown(VirtualKeys.Shift);

    CollectPermission ISessionEnvironment.CanCollect()
    {
        if (!KeyboardLayoutPolicy.AllowsInput(_settings)) return CollectPermission.Deny("入力言語が日本語ではない");
        var permission = _foreground.Check(_settings);
        // コードエディター・ターミナルのコードの中 (コメント・文字列の外) では判定しない。
        if (permission.Allowed && InCode(_settings)) permission = CollectPermission.Deny("コードの中 (コメント・文字列の外)");
        if (permission.Allowed)
        {
            // 既に日本語入力になっているなら保留する意味がない (遅延を出さない)。
            var snapshot = _imeSnapshot;
            if (snapshot is not null && snapshot.Window == _foreground.Current.Window &&
                Environment.TickCount64 - snapshot.Time < ImeStateFreshMs && snapshot.State.IsJapaneseReady)
            {
                permission = CollectPermission.Deny("既に日本語入力");
            }
        }
        if (!permission.Allowed && permission.Reason != _lastDenyReason && permission.Reason != "既に日本語入力")
        {
            Log.Info($"保留しません: {permission.Reason}");
        }
        _lastDenyReason = permission.Reason;
        return permission;
    }

    void ISessionEnvironment.RequestFlush(FlushRequest request)
    {
        Log.Decision(request.Result.Describe());
        if (!_flushQueue.IsAddingCompleted) _flushQueue.Add(request);
    }

    void ISessionEnvironment.SessionEnded(SessionSummary summary)
    {
        if (summary.UserCorrected)
        {
            Log.Decision($"誤判定のフィードバック: {Log.Text(summary.Letters)} は {summary.Verdict} ではなかった → {summary.Outcome}");
        }
        if (_settings.LearningEnabled)
        {
            _userModel.Learn(summary.Letters, summary.Outcome, summary.Verdict == Verdict.English);
        }
    }

    // ---- 再入力ワーカー ----

    private void WorkerLoop()
    {
        foreach (var request in _flushQueue.GetConsumingEnumerable())
        {
            try
            {
                if (!KeyboardLayoutPolicy.AllowsInput(_settings)) continue;
                if (request.Result.Verdict == Verdict.Japanese && _settings.DetectionLevel == DetectionLevel.Manual)
                {
                    // 手動: 切り替えずに提案だけ出す。
                    Log.Decision($"日本語入力の提案 (手動のため切り替えない): {request.Result.Describe()}");
                    ImeSuggested?.Invoke();
                }
                else if (request.Result.Verdict == Verdict.Japanese)
                {
                    var target = ImeTarget.FromForeground();
                    var result = _ime.EnsureJapanese(target);
                    var message = $"IME 切替: {result.Outcome} ({result.Before} → {result.After}) {result.Detail}";
                    if (result.Success) Log.Decision(message);
                    else Log.Warn(message + " — 保留分は切り替えずにそのまま出力します。");
                    if (target is not null) _imeSnapshot = new ImeSnapshot(target.TopLevel, result.After, Environment.TickCount64);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"IME 切替で例外: {ex.Message}");
            }
            finally
            {
                // 何があっても保留分は必ず出力する (入力を失わない)。
                Drain();
            }
            StatusChanged?.Invoke();
        }
    }

    private void Drain()
    {
        try
        {
            while (_session.TakePendingForFlush() is { } events) _injector.Inject(events);
        }
        catch (Exception ex)
        {
            Log.Error($"再入力で例外: {ex.Message}");
        }
    }

    private void FlushAbandoned(List<KeyEvent> events)
    {
        if (events.Count > 0) _injector.Inject(events);
    }

    // ---- フック・タイマーからの通知 ----

    private void SuspendForInputLanguage(long now)
    {
        _session.OnContextChanged(now);
        _directEnglishWord = false;
        _codeJapanese = false;
        _line.Invalidate();
        _sigil.Lose();
        Interlocked.Increment(ref _lineVersion);
        _lastLineKind = null;
    }

    private void OnFocusChanged()
    {
        InvalidateLine(keepSigil: true);
        // 別の入力欄に移った。次に打つ文字は先頭とみなす (/ $ @ の名前の途中なら続ける)。
        _sigil.FocusMoved();
        _composition?.Focus.Invalidate();
        _directEnglishWord = false;
        _composition?.ResetContext();
        _session.OnContextChanged(Environment.TickCount64);
    }

    private void OnForegroundChanged(IntPtr window)
    {
        // 変換中に別のウィンドウに切り替わったら、変換中の内容は捨てる (切り替わった先のアプリに入らないように)。
        _composition?.Abandon("別のウィンドウに切り替わった");
        _foreground.Refresh(window);
        InvalidateLine();
        _sigil.Start();
        _lastLineKind = null;
        var app = _foreground.Current;
        if (_settings.ProfileFor(app.ProcessName) == AppProfile.Code)
        {
            Log.Info($"{app.ProcessName} は「コード」: コメントと文字列の中だけ日本語を判定します (半角/全角 でこの行だけ日本語)。");
        }
        _composition?.Focus.Invalidate();
        _directEnglishWord = false;
        _composition?.ResetContext();
        _session.OnContextChanged(Environment.TickCount64);
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
                SuspendForInputLanguage(Environment.TickCount64);
                return;
            }
            var state = _imm32.GetState(target);
            _imeSnapshot = new ImeSnapshot(target.TopLevel, state, Environment.TickCount64);
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
        var composition = _composition;
        // 変換ボックスで入力中は、確定の直前に閉じる (CompositionService) ので触らない。
        if (composition is null || composition.Gate.IsCaptured) return;
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

    public void ResetLearning()
    {
        _userModel.Reset();
        Log.Info("学習データをリセットしました。");
    }

    private static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void SafeRun(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Error(ex.Message); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor.Dispose();
        _sessionTimer.Dispose();
        _pollTimer.Dispose();
        _lineTimer?.Dispose();
        _saveTimer.Dispose();
        _flushQueue.CompleteAdding();
        if (_worker.IsAlive) _worker.Join(3000); // Start 前 (起動に失敗したとき) でも Dispose できるように
        FlushAbandoned(_session.Abort());
        _userModel.Save();
        Log.Info("Meltype を終了しました。");
        Log.FlushFile();
    }
}
