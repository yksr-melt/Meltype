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
    private const string Instance = "keyboard";

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
    // ワークアラウンド (ImeEventType.KeyReleased): 離したことも本体に送る押下。_gate で守る
    private readonly HashSet<int> _releaseFollows = [];
    // 並んでいるキー・クリックの数と、UI スレッドで処理しているか (どちらかなら、後のキー・クリックも並べる)。_gate で守る。
    // アプリに通した入力の知らせ (Caret) は順番どおりに本体に送るが、後のキーを並ばせない
    private int _waiting;
    private bool _busy;
    // UI スレッドが _queue から取り出して処理している入力 (処理を終えるまで、アプリに送り直していない)。_gate で守る
    private Input? _processing;
    private volatile bool _composing;
    private volatile ImeState _state;
    // 押下を本体に送った左右の Alt (ImeCapture.AltKeys) と、押している間にほかのキー・マウスのボタンを押したか。フックのスレッドだけが使う
    private int _alt;
    private bool _altInterrupted;
    // フォーカスが移ってから、本体の状態をまだ受け取っていない (その間は、入力欄にフォーカスがあればキーを本体に聞く)
    private volatile bool _stale = true;
    private int _pumpScheduled;
    private int _polling;
    private IntPtr _lastLayout;
    private long _lastForcedClose;
    private int _forcedCloses;

    /// <summary>
    /// 並べる入力。Key だけなら離したキー、Event はキーの出来事 (Key が解放なら、本体に答えを聞いて送り直す左右の Alt の解放)、Caret はアプリに通した入力の知らせ。
    /// Notice は、アプリに通した左右の Alt の押下の知らせ (Caret と同じく、後のキーを並ばせない)。
    /// </summary>
    private readonly record struct Input(KeyEvent? Key = null, MouseButtonEvent? Mouse = null, ImeKey? Event = null, ImeCaret? Caret = null, bool Notice = false)
    {
        public bool Waits => Caret is null && !Notice;
    }

    /// <param name="createHost">入力の本体を作る (設定は本体に渡す。フロントは読まない)。</param>
    public KeyboardFront(Control invoker, CompositionService composition, Func<IImeEnvironment, ImeHost> createHost)
    {
        _invoker = invoker;
        _composition = composition;
        _host = createHost(this);
        _state = new ImeState { Capture = new ImeCapture() };
        composition.Focus.TreatsWindowAsTextInput = () => WindowInputApps.Contains(_foreground.Current.ProcessName);
        composition.Focus.ForegroundProcessName = () => _foreground.Current.ProcessName;
        // 入力欄でない所・パスワード欄に移った: 本体が受け持たなくなったら、変換中の文字は入れる先が無いので捨てる
        composition.Focus.CaptureLost += _ => Post(() =>
        {
            // 変換を始めたときに読んだ入力欄でなく、移った先の入力欄で判断する
            _fieldAtComposition = null;
            if (_composing && !Send(ImeEventType.StateRequested, apply: false).State.HandlesInput) Abandon();
        });
        // 変換の答えで作り直すのは、このフロントの変換中の表示だけ (Meltype IME で打っているときの答えでは何もしない)
        composition.ConversionReady += () => Post(() =>
        {
            if (_composing) Send(ImeEventType.ConversionFinished);
        });
        _pollTimer = new System.Threading.Timer(_ => PollSystemIme(), null, 250, 250);
    }

    /// <summary>入力モード (トレイのアイコン)。</summary>
    public bool Japanese => _state.LanguageMode == ImeLanguageMode.Japanese;

    /// <summary>入力モードが変わった (UI スレッドで呼ぶ)。</summary>
    public event Action? ModeChanged;

    /// <summary>前面のアプリの Windows の IME の状態 (ログの画面に出す)。</summary>
    public WindowsIme.ImeState? SystemImeState { get; private set; }

    // ---- IImeEnvironment ----

    public InputMode? InputMethod => InputMode.Keyboard;

    public MeltypeSession CreateSession(Func<Settings> settings) => _composition.CreateSession(settings, directModeDetection: true);

    public SnippetStore Snippets => _composition.Snippets;

    public string? ClipboardText => CompositionService.ClipboardText;


    // ---- フックのスレッド ----

    /// <summary>キーを受け取る (true ならアプリに渡さない)。</summary>
    public bool OnKey(KeyEvent e)
    {
        var repeat = false;
        if (e.IsDown)
        {
            repeat = !_held.Add(e.PhysicalId);
            if (_alt != 0 && e.Vk != _alt) _altInterrupted = true;
        }
        else _held.Remove(e.PhysicalId);
        var input = new Input(e, Event: e.IsDown ? KeyOf(e, repeat) : null);
        // 左右の Alt: 単独押しかは本体が決める。押下は知らせてからアプリに通し、解放は止めて本体の答えで送り直す (ImeCapture.AltKeys)。
        // 管理者として動くアプリには送り直した解放が届かない (UIPI) ので、知らせずにそのまま通す
        var alt = e.Vk is VirtualKeys.LMenu or VirtualKeys.RMenu;
        var state = _state;
        var announce = alt && e.IsDown && !repeat && state.HandlesInput && !_stale && state.Capture.AltKeys && ForegroundTracker.CanSendInputTo(_foreground.Current);
        if (announce)
        {
            _alt = e.Vk;
            _altInterrupted = false;
        }
        var altRelease = alt && e.IsUp && _alt == e.Vk;
        if (altRelease) _alt = 0;
        lock (_gate)
        {
            if (altRelease) return Enqueue(input with { Event = new ImeKey { Vk = e.Vk, Interrupted = _altInterrupted } });
            if (_waiting > 0 || _busy || _composing || e.IsUp && _releaseFollows.Contains(e.PhysicalId)) return Enqueue(input);
            if (e.IsUp) return UpOutside(e);
            if (announce)
            {
                Enqueue(input with { Key = null, Notice = true });
                return false;
            }
            if (Captures(e)) return Enqueue(input);
            // アプリに通したキーはキャレットを動かすかもしれない (後のキーより先に本体に知らせる)
            if (!VirtualKeys.IsModifier(e.Vk)) Enqueue(new Input(Caret: new ImeCaret { Moved = true, PassedKey = e.Vk, Ctrl = IsDown(VirtualKeys.Control) }));
        }
        return false;
    }

    /// <summary>マウスのボタン (true ならアプリに渡さない)。変換中なら、確定してからクリックを送り直す。</summary>
    public bool OnMouseButton(MouseButtonEvent e)
    {
        var down = e.Message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN;
        if (down && _alt != 0) _altInterrupted = true;
        lock (_gate)
        {
            if (_waiting > 0 || _busy || _composing) return Enqueue(new Input(Mouse: e));
            if (!down) return false;
            _composition.Focus.NoteClick();
            _composition.Focus.Invalidate();
            Enqueue(new Input(Caret: new ImeCaret { Moved = true, PassedKey = 0 }));
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
    /// <summary>入力欄に入った (FocusInspector のスレッドから呼ばれる)。</summary>
    public void OnTextInputEntered() => Post(() => Send(ImeEventType.CaretMoved, caret: new ImeCaret { Moved = true, PassedKey = 0, FocusEntered = true }));

    public void OnFocusChanged()
    {
        _stale = true;
        lock (_gate) Enqueue(new Input(Caret: new ImeCaret { Moved = true, PassedKey = 0, FocusLeft = true }));
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
        if (state.HandlesInput && !_stale)
        {
            if (state.Capture.Shortcuts.Any(key => ToggleShortcut.NormalizeHankakuKey(key.Vk) == ToggleShortcut.NormalizeHankakuKey(e.ShortcutKey) && key.Modifiers == mods)) return true;
            return (mods & ~1) == 0 && KeyText.CharFromKey(e.Vk, e.Scan, (mods & 1) != 0) is { } c && c is > ' ' and <= '~' &&
                state.Capture.PrintableAscii && !state.Capture.PrintableAsciiExcept.Contains(c);
        }
        if ((mods & ~1) != 0) return false;
        return _stale || TextFocusRechecked();
    }

    // 並んでいないときに離したキー。本体が使った押下なら、離したこともアプリに通さない
    private bool UpOutside(KeyEvent e) => _consumed.Remove(e.PhysicalId);

    /// <summary>Alt の単独押しの解放 release の代わりに送るキー (0xE8 の押下・解放のあとに、release をそのまま。右 Alt の拡張キーの印も残す)。</summary>
    internal static KeyEvent[] MaskedAltRelease(KeyEvent release) =>
        [new KeyEvent(0xE8, 0, false, false, false, 0), new KeyEvent(0xE8, 0, false, true, false, 0), release];

    private bool Enqueue(Input input)
    {
        _queue.Enqueue(input);
        if (input.Waits) _waiting++;
        if (_invoker.IsHandleCreated && !_invoker.IsDisposed && Interlocked.Exchange(ref _pumpScheduled, 1) == 0)
            _invoker.BeginInvoke(Pump);
        return true;
    }

    private ImeKey KeyOf(KeyEvent e, bool repeat)
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
            bool caretFollows;
            lock (_gate)
            {
                if (!_queue.TryDequeue(out input))
                {
                    _busy = false;
                    return;
                }
                _busy = input.Waits;
                if (_busy) _waiting--;
                caretFollows = _queue.TryPeek(out var next) && next.Caret is not null;
                _processing = input;
            }
            try
            {
                Process(input, caretFollows);
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
            finally
            {
                lock (_gate) _processing = null;
            }
        }
    }

    /// <param name="caretFollows">次もキャレットの動き。入力欄を読むのは、続いた動きの最後の 1 回だけでよい (途中はキーの情報だけ送る)。</param>
    private void Process(Input input, bool caretFollows)
    {
        if (input.Caret is { } caret)
        {
            Send(ImeEventType.CaretMoved, caret: caret, readField: !caretFollows);
            return;
        }
        if (input.Notice && input.Event is { } pressed)
        {
            Send(ImeEventType.KeyPressed, pressed, readField: false);
            return;
        }
        if (input.Key is { IsUp: true } release && input.Event is { } alt)
        {
            // 単独押しで切り替えたら、メニューバーに移らないよう、何もしないキー (0xE8、割り当てなし) のあとに送り直す
            if (Send(ImeEventType.KeyReleased, alt).Result.Consumed && _injector.Inject(MaskedAltRelease(release))) return;
            _composition.Replay(release);
            return;
        }
        if (input.Mouse is { } mouse)
        {
            if (mouse.Message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN)
            {
                _composition.Focus.NoteClick();
                _composition.Focus.Invalidate();
                Send(ImeEventType.CommitRequested, caret: new ImeCaret { Moved = true, PassedKey = 0 });
            }
            CompositionService.Replay(mouse);
            return;
        }
        if (input.Key is not { } e) return;
        if (input.Event is not { } key)
        {
            // 離したキー: 本体が使った押下なら通さない
            bool consumed, follows;
            lock (_gate)
            {
                consumed = _consumed.Remove(e.PhysicalId);
                follows = _releaseFollows.Remove(e.PhysicalId);
            }
            // 変換していないときは入力欄を読まない (離すたびに UI Automation で読むと打鍵が遅れる)
            if (follows) Send(ImeEventType.KeyReleased, new ImeKey { Vk = e.ShortcutKey }, readField: _composing);
            if (!consumed && e.IsUp) _composition.Replay(e);
            return;
        }
        if (_state.Capture.KeyReleases && !VirtualKeys.IsModifier(e.Vk))
        {
            key = key with { ReleaseFollows = true };
            lock (_gate) _releaseFollows.Add(e.PhysicalId);
        }
        var output = Send(ImeEventType.KeyPressed, key);
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
    private ImeOutput Send(ImeEventType type, ImeKey? key = null, ImeCaret? caret = null, bool apply = true, bool readField = true)
    {
        var output = _host.Handle(new ImeEvent { Version = ImeHost.Version, Type = type, Instance = Instance, Key = key, Caret = caret, Field = readField ? Field() : null });
        var wasMode = _state.LanguageMode;
        _state = output.State;
        _stale = false;
        if (apply)
        {
            foreach (var edit in output.Result.Commits) Commit(edit);
            if (output.Result.View is { } view) _composition.Show(view);
            else _composition.Hide();
            _composing = output.Result.View is not null;
        }
        if (output.State.ModeIndicatorMs > 0) _composition.ShowMode(output.State.LanguageMode == ImeLanguageMode.Japanese, output.State.ModeIndicatorMs);
        if (output.State.LanguageMode != wasMode) ModeChanged?.Invoke();
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
        // 変換中の文字は Meltype の変換ボックスにあり、アプリの入力欄の文字は変わらないので、変換を始めたときに読んだものを使う
        if (_composing && _fieldAtComposition is { } atComposition) return atComposition with { Composing = true };
        _fieldAtComposition = ReadFacts();
        return _fieldAtComposition;
    }

    private ImeField? _fieldAtComposition;

    private ImeField ReadFacts()
    {
        var app = _foreground.Current;
        var text = _composition.Focus.CanCaptureWaiting();
        var focus = _composition.Focus.Current;
        var (before, after, selection) = _composition.Focus.ReadField(50);
        var traits = new List<ImeTrait>();
        if (focus.IsPassword) traits.Add(ImeTrait.Password);
        if (!text) traits.Add(ImeTrait.NonText);
        if (app.Window == IntPtr.Zero || app.IsOwnProcess || !ForegroundTracker.CanSendInputTo(app)) traits.Add(ImeTrait.Unreachable);
        if (WindowsIme.ImeTarget.FromForeground() is not { ThreadId: not 0 } target || !IsJapaneseLayout(Native.GetKeyboardLayout(target.ThreadId))) traits.Add(ImeTrait.OtherLanguage);
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
        if (_composing) Send(ImeEventType.CommitRequested, caret: new ImeCaret { Moved = true, PassedKey = 0, FocusLeft = true }, apply: false);
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

    // 押している修飾キーか。変換中はフックが修飾キーの押下も止めて並べ、本体に聞いてから送り直すので、
    // 送り直すまでは GetAsyncKeyState に表れない (止めたキーは反映されない)。そうしないと、変換中に Shift + / を打つと
    // Shift が離れて見え、? が / (・) になる。GetAsyncKeyState に表れないのは、並べてまだ送り直していない押下 (処理中のものと _queue) だけなので、
    // それも見る (同じ物理キーの解放が後ろに並んでいれば、離している)。並びが空なら GetAsyncKeyState だけで決まる
    private bool IsDown(int vk)
    {
        if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        var down = new HashSet<int>();
        lock (_gate)
        {
            foreach (var input in _processing is { } current ? _queue.Prepend(current) : _queue)
            {
                if (input.Key is not { } key || key.Vk != vk && GenericModifier(key.Vk) != vk) continue;
                if (key.IsDown) down.Add(key.PhysicalId);
                else down.Remove(key.PhysicalId);
            }
        }
        return down.Count > 0;
    }

    /// <summary>左右を区別する修飾キーの、区別しない仮想キー (修飾キーでなければ 0)。</summary>
    private static int GenericModifier(int vk) => vk switch
    {
        VirtualKeys.LShift or VirtualKeys.RShift => VirtualKeys.Shift,
        VirtualKeys.LControl or VirtualKeys.RControl => VirtualKeys.Control,
        VirtualKeys.LMenu or VirtualKeys.RMenu => VirtualKeys.Menu,
        _ => 0,
    };

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
            if (ForegroundTracker.IsOwnWindow(Native.GetForegroundWindow()) || WindowsIme.ImeTarget.FromForeground() is not { } target) return;
            var state = WindowsIme.Imm32ImeController.GetState(target);
            SystemImeState = state;
            var layout = Native.GetKeyboardLayout(target.ThreadId);
            if (layout != _lastLayout)
            {
                if (_lastLayout != IntPtr.Zero) Log.Info($"キーボード/IME が切り替わりました (HKL {_lastLayout:X} → {layout:X}, IME: {state})。");
                _lastLayout = layout;
            }
            if (state.Mode != WindowsIme.ImeMode.Open || !_state.HandlesInput) return;
            // ユーザーが IME を ON にし続ける (閉じてもすぐ開く) なら、10 秒に 3 回までにして奪い合わない
            var now = Environment.TickCount64;
            if (now - _lastForcedClose > 10000) _forcedCloses = 0;
            if (_forcedCloses >= 3) return;
            _forcedCloses++;
            _lastForcedClose = now;
            var closed = WindowsIme.Imm32ImeController.TrySetOpen(target, false, null, ImeTimeoutMs);
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
        Send(ImeEventType.StateRequested, apply: false);
    }

    /// <summary>Meltype キーボードを使わなくなった・終了するとき (UI スレッド)。未確定の内容を確定し、並んでいた入力をそのままアプリに通す。</summary>
    public void Stop()
    {
        if (_composing) Send(ImeEventType.CommitRequested);
        _composing = false;
        _composition.Hide();
        ReplayQueued();
        Send(ImeEventType.StateRequested, apply: false);
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        Stop();
    }
}
