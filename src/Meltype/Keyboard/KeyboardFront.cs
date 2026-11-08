// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Composition;
using Meltype.Config;
using Meltype.Diagnostics;
using Meltype.Ime;
using Meltype.Input;

namespace Meltype.Keyboard;

/// <summary>
/// Meltype キーボード: キーボードフックのフロント。TSF の DLL と同じく、事実を <see cref="ImeHost"/> に送り、返ってきた確定・表示・受け取るキーに従う。
///   フックのスレッド: 受け取るキー (state.capture。変換中はすべて) を握りつぶして順番どおりに並べ、UI スレッドで ImeHost に送る。
///   並んでいる間・処理している間・変換中は、後のキーとクリックも並べる (確定した文字を追い越さない)。本体が使わなかった入力は送り直す。
///   アプリに通したキー・クリック・フォーカスの移動は caret で知らせる。入力欄の文字は UI Automation で読めたときだけ送る。
///   確定は SendInput (アプリ別設定によっては貼り付け)、変換中の文字は変換ボックス (CompositionWindow) に出す。
/// Windows の IME (Microsoft IME など) は、二重に変換しないように閉じておく。
/// </summary>
internal sealed class KeyboardFront : IImeEnvironment, IDisposable
{
    private const string Sid = "keyboard";

    /// <summary>Windows の IME の ON/OFF を切り替えるときに、応答を待つ時間。</summary>
    private const int ImeTimeoutMs = 300;

    private readonly Control _invoker;
    private readonly CompositionService _composition;
    private readonly ForegroundTracker _foreground = new();
    private readonly KeyInjector _injector = new();
    private readonly ImeHost _host;
    private readonly System.Threading.Timer _pollTimer;
    private readonly object _gate = new();
    private readonly Queue<Input> _queue = new();
    // 押している物理キー (自動の繰り返しを見分ける)。フックのスレッドだけが使う
    private readonly HashSet<int> _held = [];
    // 本体が使った押下 (離したこともアプリに通さない)。_gate で守る
    private readonly HashSet<int> _consumed = [];
    // 並んでいるキー・クリックの数と、UI スレッドで処理しているか (どちらかなら、後のキー・クリックも並べる)。_gate で守る。
    // アプリに通した入力の知らせ (Caret) は順番どおりに本体に送るが、後のキーを並ばせない
    private int _waiting;
    private bool _busy;
    private volatile bool _composing;
    private volatile ImeState _state;
    // 単独で押している左右の Alt。フックのスレッドだけが使う
    private int _tap;
    // フォーカスが移ってから、本体の状態をまだ受け取っていない (その間は、入力欄にフォーカスがあればキーを本体に聞く)
    private volatile bool _stale = true;
    private int _pumpScheduled;
    private int _polling;
    private IntPtr _lastLayout;
    private long _lastForcedClose;
    private int _forcedCloses;

    /// <summary>並べる入力。Key だけなら離したキー、Event はキーの出来事 (左右の Alt の単独押しは Key なし)、Caret はアプリに通した入力の知らせ。</summary>
    private readonly record struct Input(KeyEvent? Key = null, MouseButtonEvent? Mouse = null, ImeKey? Event = null, ImeCaret? Caret = null);

    /// <param name="createHost">入力の本体を作る (設定は本体に渡す。フロントは読まない)。</param>
    public KeyboardFront(Control invoker, CompositionService composition, Func<IImeEnvironment, ImeHost> createHost)
    {
        _invoker = invoker;
        _composition = composition;
        _host = createHost(this);
        _state = new ImeState { Capture = new ImeCapture() };
        composition.Focus.TreatsWindowAsTextInput = () => WindowInputApps.Contains(_foreground.Current.ProcessName);
        composition.Focus.ForegroundProcessName = () => _foreground.Current.ProcessName;
        composition.Focus.TextInputEntered += () => Post(() => Send(ImeEventType.Focus));
        // 入力欄でない所・パスワード欄に移った: 本体が受け持たなくなったら、変換中の文字は入れる先が無いので捨てる
        composition.Focus.CaptureLost += _ => Post(() =>
        {
            if (_composing && !Send(ImeEventType.Sync, apply: false).State.Active) Abandon();
        });
        composition.ConversionReady += () => Post(() => Send(ImeEventType.Refresh));
        _pollTimer = new System.Threading.Timer(_ => PollSystemIme(), null, 250, 250);
    }

    /// <summary>入力モード (トレイのアイコン)。</summary>
    public bool Japanese => _state.Japanese;

    /// <summary>入力モードが変わった (UI スレッドで呼ぶ)。</summary>
    public event Action? ModeChanged;

    /// <summary>前面のアプリの Windows の IME の状態 (ログの画面に出す)。</summary>
    public IME.ImeState? SystemImeState { get; private set; }

    // ---- IImeEnvironment ----

    public InputMode? Mode => InputMode.Keyboard;

    public MeltypeSession CreateSession(Func<Settings> settings) => _composition.CreateSession(settings, directModeDetection: true);

    public string? ReadingOf(string text) => _composition.GuessReading(text) is { Length: > 0 } reading ? reading : null;

    public SnippetStore Snippets => _composition.Snippets;

    public string? ClipboardText => CompositionService.ClipboardText;

    public (string Title, string Artist)? NowPlaying => _composition.NowPlaying;

    // ---- フックのスレッド ----

    /// <summary>キーを受け取る (true ならアプリに渡さない)。</summary>
    public bool OnKey(KeyEvent e)
    {
        var repeat = false;
        if (e.IsDown)
        {
            repeat = !_held.Add(e.PhysicalId);
            _tap = e.Vk is VirtualKeys.LMenu or VirtualKeys.RMenu && (_tap == e.Vk || _tap == 0 && !OtherModifiersDown()) ? e.Vk : 0;
        }
        else _held.Remove(e.PhysicalId);
        var input = new Input(e, Event: e.IsDown ? KeyOf(e, repeat) : null);
        lock (_gate)
        {
            if (_waiting > 0 || _busy || _composing) return Enqueue(input);
            if (e.IsUp) return UpOutside(e);
            if (Captures(e)) return Enqueue(input);
            // アプリに通したキーはキャレットを動かすかもしれない (後のキーより先に本体に知らせる)
            if (!VirtualKeys.IsModifier(e.Vk)) Enqueue(new Input(Caret: new ImeCaret { Moved = true, Passed = e.Vk, Ctrl = IsDown(VirtualKeys.Control) }));
        }
        return false;
    }

    /// <summary>マウスのボタン (true ならアプリに渡さない)。変換中なら、確定してからクリックを送り直す。</summary>
    public bool OnMouseButton(MouseButtonEvent e)
    {
        var down = e.Message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN;
        if (down) _tap = 0;
        lock (_gate)
        {
            if (_waiting > 0 || _busy || _composing) return Enqueue(new Input(Mouse: e));
            if (!down) return false;
            _composition.Focus.NoteClick();
            _composition.Focus.Invalidate();
            Enqueue(new Input(Caret: new ImeCaret { Moved = true, Passed = 0 }));
        }
        return false;
    }

    /// <summary>
    /// 前面のウィンドウが変わった。変換中の文字は入れる先が無くなったので捨て、並んでいた入力はそのままアプリに通す
    /// (切り替わった先のアプリに、前のアプリの変換中の文字を入れない)。
    /// </summary>
    public void OnForegroundChanged(IntPtr window)
    {
        _stale = true;
        _foreground.Refresh(window);
        Abandon();
    }

    /// <summary>同じウィンドウの中で、フォーカスが別の要素に移った。</summary>
    public void OnFocusChanged()
    {
        _stale = true;
        lock (_gate) Enqueue(new Input(Caret: new ImeCaret { Moved = true, Passed = 0, Left = true }));
    }

    // 受け取るキーか。本体の状態 (capture) どおりにする。ただし、フォーカスが移ってから本体の状態をまだ受け取っていないときと、
    // 本体が受け持っていないのに入力欄にフォーカスがある (UI Automation の通知が遅れた・来なかった) ときは、
    // Ctrl・Alt・Win と一緒でないキーを本体に聞く (本体が使わなければ送り直す)
    private bool Captures(KeyEvent e)
    {
        if (VirtualKeys.IsModifier(e.Vk)) return false;
        var state = _state;
        var mods = (IsDown(VirtualKeys.Shift) ? 1 : 0) | (IsDown(VirtualKeys.Control) ? 2 : 0) | (IsDown(VirtualKeys.Menu) ? 4 : 0) |
            (IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin) ? 8 : 0);
        if (state.Active && !_stale)
        {
            if (state.Capture.Keys.Any(key => ToggleShortcut.NormalizeHankakuKey(key.Key) == ToggleShortcut.NormalizeHankakuKey(e.ShortcutKey) && key.Mods == mods)) return true;
            return (mods & ~1) == 0 && KeyText.CharFromKey(e.Vk, e.Scan, (mods & 1) != 0) is { } c && c is > ' ' and <= '~' &&
                state.Capture.Text && !state.Capture.Except.Contains(c);
        }
        if ((mods & ~1) != 0) return false;
        return _stale || TextFocusRechecked();
    }

    // 並んでいないときに離したキー。本体が使った押下なら、離したこともアプリに通さない。左右の Alt の単独押しは本体に送る
    private bool UpOutside(KeyEvent e)
    {
        if (_consumed.Remove(e.PhysicalId)) return true;
        if (e.Vk is not (VirtualKeys.LMenu or VirtualKeys.RMenu) || _tap != e.Vk) return false;
        _tap = 0;
        var state = _state;
        if (!state.Active || !state.Capture.Keys.Any(key => key.Key == e.Vk && key.Mods == 0) || !ForegroundTracker.CanSendInputTo(_foreground.Current)) return false;
        // メニューバーに移らないよう、Alt の解放を止めて、何もしないキー (0xE8、割り当てなし) のあとに送り直す。
        // 次の打鍵より先に届くように、ここだけはフックのスレッドで送る (押している間に送ると、すぐに離したときに Alt の解放が先に並ぶ)。
        // 管理者として動くアプリには送り直した Alt の解放が届かない (UIPI) ので止めない
        if (!_injector.Inject(MaskedAltRelease(e))) return false;
        Enqueue(new Input(Event: new ImeKey { Vk = e.Vk, Tap = true }));
        return true;
    }

    /// <summary>Alt の単独押しの解放 release の代わりに送るキー (0xE8 の押下・解放のあとに、release をそのまま。右 Alt の拡張キーの印も残す)。</summary>
    internal static KeyEvent[] MaskedAltRelease(KeyEvent release) =>
        [new KeyEvent(0xE8, 0, false, false, false, 0), new KeyEvent(0xE8, 0, false, true, false, 0), release];

    private bool Enqueue(Input input)
    {
        _queue.Enqueue(input);
        if (input.Caret is null) _waiting++;
        if (_invoker.IsHandleCreated && !_invoker.IsDisposed && Interlocked.Exchange(ref _pumpScheduled, 1) == 0)
            _invoker.BeginInvoke(Pump);
        return true;
    }

    private static ImeKey KeyOf(KeyEvent e, bool repeat)
    {
        bool shift = IsDown(VirtualKeys.Shift), ctrl = IsDown(VirtualKeys.Control), alt = IsDown(VirtualKeys.Menu), command = IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin);
        var ch = !ctrl && !alt && !command && KeyText.CharFromKey(e.Vk, e.Scan, shift) is { } c && !char.IsControl(c) ? c : 0;
        return new ImeKey { Vk = e.ShortcutKey, Ch = ch, Shift = shift, Ctrl = ctrl, Alt = alt, Command = command, Repeat = repeat };
    }

    // ---- UI スレッド ----

    private void Pump()
    {
        Volatile.Write(ref _pumpScheduled, 0);
        while (true)
        {
            Input input;
            lock (_gate)
            {
                if (!_queue.TryDequeue(out input))
                {
                    _busy = false;
                    return;
                }
                _busy = input.Caret is null;
                if (_busy) _waiting--;
            }
            try
            {
                Process(input);
            }
            catch (Exception ex)
            {
                // 状態を捨てないと、次の打鍵でも同じ例外が起きて英字しか入力できなくなる
                Log.Error($"Meltype キーボードで例外が起きたので入力をリセットしました: {ex}");
                _host.CloseAll();
                _composing = false;
                _composition.Hide();
                ReplayQueued();
            }
        }
    }

    private void Process(Input input)
    {
        if (input.Caret is { } caret)
        {
            Send(ImeEventType.Caret, caret: caret);
            return;
        }
        if (input.Event is { Tap: true } tap)
        {
            Send(ImeEventType.Key, tap);
            return;
        }
        if (input.Mouse is { } mouse)
        {
            if (mouse.Message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN)
            {
                _composition.Focus.NoteClick();
                _composition.Focus.Invalidate();
                Send(ImeEventType.Commit, caret: new ImeCaret { Moved = true, Passed = 0 });
            }
            CompositionService.Replay(mouse);
            return;
        }
        if (input.Key is not { } e) return;
        if (input.Event is not { } key)
        {
            // 離したキー: 本体が使った押下なら通さない
            bool consumed;
            lock (_gate) consumed = _consumed.Remove(e.PhysicalId);
            if (!consumed && e.IsUp) _composition.Replay(e);
            return;
        }
        var output = Send(ImeEventType.Key, key);
        if (!output.Result.Consumed)
        {
            _composition.Replay(e);
            return;
        }
        lock (_gate) _consumed.Add(e.PhysicalId);
        // Alt・Win と一緒に押したキー (US 配列の Alt + ` など) を使うと、アプリには修飾キーだけを押したように見えてメニューに移るので、何もしないキーを送る
        if (key.Alt || key.Command) KeyInjector.SendKey(0xE8);
    }

    /// <summary>出来事を送り、返ってきた確定・表示・入力モードを反映する。</summary>
    private ImeOutput Send(ImeEventType type, ImeKey? key = null, ImeCaret? caret = null, bool apply = true)
    {
        var output = _host.Handle(new ImeEvent { V = ImeHost.Version, Type = type, Sid = Sid, Key = key, Caret = caret, Field = Field() });
        var wasJapanese = _state.Japanese;
        _state = output.State;
        _stale = false;
        if (apply)
        {
            foreach (var edit in output.Result.Commits) Commit(edit);
            if (output.Result.View is { } view) _composition.Show(view);
            else _composition.Hide();
            _composing = output.Result.View is not null;
        }
        if (output.State.Indicator) _composition.ShowMode(output.State.Japanese);
        if (output.State.Japanese != wasJapanese) ModeChanged?.Invoke();
        return output;
    }

    private void Commit(TextEdit edit)
    {
        _composition.Insert(edit.DeleteBefore, edit.Text, edit.Paste);
        _composition.MoveCaretBackward(edit.CaretBack);
    }

    /// <summary>
    /// 入力欄の事実。フォーカスを調べている最中なら、調べ終わるのを少し待つ。入力欄の文字は、UI Automation で読めたときだけ。
    /// 管理者として動くアプリ (UIPI) には SendInput が届かず、Meltype 自身の画面 (横取りしない所) には送らないので、文字を送れない所とする。
    /// </summary>
    private ImeField Field()
    {
        var app = _foreground.Current;
        var text = _composition.Focus.CanCaptureWaiting();
        var focus = _composition.Focus.Current;
        var (before, after, selection) = _composition.Focus.ReadField(50);
        var traits = new List<ImeTrait>();
        if (focus.IsPassword) traits.Add(ImeTrait.Password);
        if (!text) traits.Add(ImeTrait.NonText);
        if (app.Window == IntPtr.Zero || app.IsOwnProcess || !ForegroundTracker.CanSendInputTo(app)) traits.Add(ImeTrait.Unreachable);
        if (IME.ImeTarget.FromForeground() is not { ThreadId: not 0 } target || !IsJapaneseLayout(Native.GetKeyboardLayout(target.ThreadId))) traits.Add(ImeTrait.OtherLanguage);
        if (app.LooksLikeGame) traits.Add(ImeTrait.Game);
        if (app.IsFullscreen) traits.Add(ImeTrait.Fullscreen);
        return new ImeField
        {
            Before = before,
            After = after,
            Selection = selection is { Length: > 0 } ? selection : null,
            Composing = _composing,
            App = app.ProcessName.Length > 0 ? app.ProcessName : null,
            Title = KeyText.WindowTitle(app.Window),
            WindowClass = WindowClass(app.Window),
            Element = new ImeElement(focus.Name, focus.ClassName),
            Traits = traits,
        };
    }

    /// <summary>入れる先が無くなった: 変換中の文字を捨て、並んでいた入力をそのままアプリに通す。</summary>
    private void Abandon() => Post(() =>
    {
        if (_composing) Send(ImeEventType.Commit, caret: new ImeCaret { Moved = true, Passed = 0, Left = true }, apply: false);
        _composing = false;
        _composition.Hide();
        ReplayQueued();
    });

    private void ReplayQueued()
    {
        List<Input> inputs;
        lock (_gate)
        {
            inputs = [.. _queue];
            _queue.Clear();
            _waiting = 0;
            _consumed.Clear();
        }
        foreach (var input in inputs)
        {
            if (input.Key is { } key) _composition.Replay(key);
            else if (input.Mouse is { } mouse) CompositionService.Replay(mouse);
        }
    }

    private void Post(Action action)
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return;
        _invoker.BeginInvoke(() =>
        {
            try { action(); }
            catch (Exception ex) { Log.Error($"Meltype キーボードで例外: {ex}"); }
        });
    }

    // ---- 入力欄にフォーカスがあるか (フックのスレッド) ----

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

    /// <summary>
    /// 入力欄でないと分かっている所で打ったが、調べ直したら入力欄 (パスワード以外) だった: 通知が来ないまま入力欄に移っていた (ブラウザーのタブの切り替え: #364)。
    /// クリックの直後に打ち始めたときも、クリックで移るはずのフォーカスがまだ前の場所のまま調べ終わっていることがあるので、調べ直して最大 100 ms 待つ。
    /// </summary>
    private bool TextFocusRechecked()
    {
        var focus = _composition.Focus;
        if (RechecksStaleFocus(_foreground.Current.ProcessName) && focus.RecheckStale()) return true;
        var capture = focus.RecheckAfterClick(out var waited);
        if (waited >= 0) Log.Info($"クリック直後の打ち始め: フォーカスを調べ直した ({waited} ms) → {(capture ? "入力欄なので本体に聞く" : "入力欄ではない")}");
        return capture;
    }

    private static bool OtherModifiersDown() =>
        IsDown(VirtualKeys.Control) || IsDown(VirtualKeys.Shift) || IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin);

    private static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>入力先のスレッドで選んでいる入力言語が日本語か (物理配列ではない。HKL の下位ワードが言語 ID で、上位ワード (配列・IME の識別子) と開閉状態には依存しない)。</summary>
    internal static bool IsJapaneseLayout(IntPtr layout) => (layout.ToInt64() & 0xFFFF) == 0x0411;

    private static string? WindowClass(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        var className = new char[256];
        var length = Native.GetClassName(window, className, className.Length);
        return length > 0 ? new string(className, 0, length) : null;
    }

    // ---- Windows の IME ----

    /// <summary>
    /// Meltype キーボードを使っている間は、Windows の IME (Microsoft IME・Google 日本語入力など) を OFF に保つ。
    /// Win+Space などで IME を切り替えると、新しい IME が ON の状態で始まることがあり、そのままだと
    /// Meltype が通したキーを IME が変換してしまう。アプリ別設定で OFF にしたアプリ・ゲームでは、Windows の IME をそのまま使ってもらう。
    /// </summary>
    private void PollSystemIme()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            if (ForegroundTracker.IsOwnWindow(Native.GetForegroundWindow()) || IME.ImeTarget.FromForeground() is not { } target) return;
            var state = IME.Imm32ImeController.GetState(target);
            SystemImeState = state;
            var layout = Native.GetKeyboardLayout(target.ThreadId);
            if (layout != _lastLayout)
            {
                if (_lastLayout != IntPtr.Zero) Log.Info($"キーボード/IME が切り替わりました (HKL {_lastLayout:X} → {layout:X}, IME: {state})。");
                _lastLayout = layout;
            }
            if (state.Mode != IME.ImeMode.Open || !_state.Active) return;
            // ユーザーが IME を ON にし続ける (閉じてもすぐ開く) なら、10 秒に 3 回までにして奪い合わない
            var now = Environment.TickCount64;
            if (now - _lastForcedClose > 10000) _forcedCloses = 0;
            if (_forcedCloses >= 3) return;
            _forcedCloses++;
            _lastForcedClose = now;
            var closed = IME.Imm32ImeController.TrySetOpen(target, false, null, ImeTimeoutMs);
            Log.Warn($"Meltype キーボードの使用中に Windows の IME が ON になっていました ({state}) → {(closed ? "OFF にしました" : "OFF にできませんでした")}。" +
                     (_forcedCloses == 3 ? " (続けて ON になるため、しばらく自動で OFF にしません。IME を使うときは Ctrl+半角/全角 で Meltype を一時停止してください)" : ""));
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

    /// <summary>設定が変わった (UI スレッド)。本体の状態を受け取り直す (受け持つか・受け取るキーが変わる)。</summary>
    public void Refresh()
    {
        _stale = true;
        Send(ImeEventType.Sync, apply: false);
    }

    /// <summary>Meltype キーボードを使わなくなった・終了するとき (UI スレッド)。未確定の内容を確定し、並んでいた入力をそのままアプリに通す。</summary>
    public void Stop()
    {
        if (_composing) Send(ImeEventType.Commit);
        _composing = false;
        _composition.Hide();
        ReplayQueued();
        Send(ImeEventType.Sync, apply: false);
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        Stop();
    }
}
