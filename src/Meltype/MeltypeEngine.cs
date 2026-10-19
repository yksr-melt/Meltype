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
///   フックのスレッド   … KeyboardMonitor。打鍵を変換ボックス (CompositionService) に渡す。
///   タイマー           … IME 状態のポーリング。
/// </summary>
internal sealed class MeltypeEngine : IDisposable
{
    private readonly string? _configPath;
    private readonly ScoreEngine _scoreEngine;
    private readonly ForegroundTracker _foreground = new();
    private readonly Imm32ImeController _imm32 = new();
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

    /// <summary>UI スレッドで作った変換ボックスをつなぐ。</summary>
    public void AttachComposition(Composition.CompositionService composition)
    {
        _composition = composition;
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
        if (settings.Mode == InputMode.Tsf) return false;
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
        // 設定「日本語 / 英数を切り替えるキーを追加」: 半角/全角 と同じく Meltype キーボードの ON/OFF (issue #530)
        if (settings.Enabled && settings.Mode == InputMode.Keyboard && settings.ToggleKeyboardShortcuts.Length > 0 && !e.Injected && !composition.Gate.IsCaptured)
        {
            var ctrl = IsDown(VirtualKeys.Control);
            var alt = IsDown(VirtualKeys.Menu);
            var shift = IsDown(VirtualKeys.Shift);
            var win = IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin);
            if (ToggleShortcuts(settings.ToggleKeyboardShortcuts).Any(shortcut => shortcut.Matches(e.Vk, ctrl, alt, shift, win)))
            {
                if (e.IsDown)
                {
                    lock (_swallowedToggleUps) _swallowedToggleUps.Add(e.Vk);
                    MaskAltRelease();
                    KeyboardDirect = !_keyboardDirect;
                }
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
                    Log.Info(_codeJapanese ? "コードの行: 日本語で入力 (改行しても続ける。キャレットが別の場所に移るか、もう一度押すまで)" : "コードの行: 英数に戻す");
                    ShowCodeMode();
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

    // 設定の文字列を、打鍵のたびに読み直さない (変わったときだけ読み直す)
    private string _toggleShortcutsText = "";
    private IReadOnlyList<Meltype.Input.KeyShortcut> _toggleShortcuts = [];

    private IReadOnlyList<Meltype.Input.KeyShortcut> ToggleShortcuts(string text)
    {
        if (text != _toggleShortcutsText)
        {
            _toggleShortcuts = Meltype.Input.KeyShortcut.ParseList(text).ToList();
            _toggleShortcutsText = text;
        }
        return _toggleShortcuts;
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
        var mods = IsDown(VirtualKeys.Control) || IsDown(VirtualKeys.Menu) || IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin);
        // Space / Tab は変換ボックスが開いていても先に試す (issue #463: ;名前 が変換に取られて展開できない)。
        if (e.Vk is (VirtualKeys.Space or VirtualKeys.Tab) && !IsDown(VirtualKeys.Shift) && !mods && SnippetsEnabled(settings) && composition.Focus.CanCapture)
        {
            _snippet.Mark = settings.SnippetMark;
            if (_snippet.Expand(composition.Snippets) is { } expansion)
            {
                lock (_swallowedToggleUps) _swallowedToggleUps.Add(e.Vk);
                _lastSnippet = (expansion.Text, expansion.Typed);
                composition.Post(() => composition.InsertSnippetAfterCancelComposition(expansion.Delete, expansion.Text));
                _line.Invalidate();
                Log.Info($"定型文を入れました ({expansion.Text.Length} 文字)。");
                return true;
            }
        }
        if (composition.Gate.IsCaptured || mods) return false;
        if (e.Vk == VirtualKeys.Escape && undo is { } last)
        {
            lock (_swallowedToggleUps) _swallowedToggleUps.Add(e.Vk);
            composition.Post(() => composition.InsertSnippet(last.Text.ReplaceLineEndings("\n").Length, last.Typed));
            Log.Info("定型文を取り消して、打った名前に戻しました。");
            return true;
        }
        return false;
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
        var punctuation = e.Vk is >= 0x30 and <= 0x39 or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;
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
        // ;名前 を打っている途中は変換ボックスを開かない (issue #463)。
        if (!reconvert && SnippetsEnabled(settings) && _snippet.IsActive) return false;
        // 先頭か空白の直後の ;名前 (定型文) は、Space / Tab まで変換せずにそのままアプリへ渡す (#290)。
        if (!reconvert && SnippetsEnabled(settings) && KeyText.CharFromKey(e.Vk, e.Scan, false) is { } snippetChar)
        {
            _snippet.Mark = settings.SnippetMark;
            if (_snippet.PassesThrough(snippetChar, _line.Text)) return false;
        }
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
        if (_composition?.Focus.CanCaptureWaiting() != true &&
            !(RechecksStaleFocus(_foreground.Current.ProcessName) && _composition?.Focus.RecheckStale() == true)) return false;
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
        if (next.Mode == InputMode.Keyboard && (previous.Mode != InputMode.Keyboard || !previous.Enabled)) CloseSystemImeAsync();
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
        _foreground.Refresh(window);
        InvalidateLine();
        _sigil.Start();
        _snippet.Start();
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
