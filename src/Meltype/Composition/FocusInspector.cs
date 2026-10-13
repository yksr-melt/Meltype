// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Collections.Concurrent;

namespace Meltype.Composition;

/// <summary>フォーカスのある要素の情報。</summary>
public sealed record FocusInfo(bool IsTextInput, bool IsPassword, Rectangle? Bounds, string Description, string Name = "", string ClassName = "");

/// <summary>
/// フォーカスのある要素を UI Automation で調べる。
///   ・文字を入力する欄かどうか: 変換ボックスは、文字入力欄以外 (Gmail の j/k、エクスプローラーの頭文字検索、ゲーム) で
///     キーを横取りしてはいけないし、パスワード欄の文字を画面に表示してもいけない。
///   ・キャレットの直前の確定済みの文字: 英語とも日本語とも読める語 (sushi) を前の文字に合わせるため。
/// UIA は他プロセスへの問い合わせで時間がかかることがあるため専用スレッドで行い、フックからは
/// 結果のキャッシュを見る (Affinity は短い Win32 の読み取りで追加確認)。フォーカスが変わってから調べ終わるまでの間は「入力欄ではない」扱い
/// (= 横取りしない) にするので、調べ損ねても安全側に倒れる。
/// </summary>
public sealed class FocusInspector : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private long _focusSequence;
    private long _resolvedSequence = -1;
    private sealed record FocusState(FocusInfo Info, IntPtr AffinityWindow = default);
    private volatile FocusState _state = new(new FocusInfo(false, false, null, "未確認"));
    private FocusInfo _info => _state.Info;
    private string? _loggedDescription;
    private (string Description, Rectangle? Bounds)? _lastTextInput;

    /// <summary>
    /// 別の入力欄 (パスワード以外) にフォーカスが移った (このクラスのスレッドから呼ばれる)。入力モード (あ / A) の表示に使う。
    /// </summary>
    public event Action? TextInputEntered;

    /// <summary>フォーカスが入力欄でない所・パスワード欄に移ったと分かったとき (このクラスのスレッドから呼ばれる)。引数は理由。</summary>
    public event Action<string>? CaptureLost;

    public FocusInspector()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Meltype focus inspector" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>フォーカスが変わったとき (フックのスレッドから呼ばれる。ブロックしない)。</summary>
    public void Invalidate()
    {
        var sequence = Interlocked.Increment(ref _focusSequence);
        Enqueue(() => InspectIfLatest(sequence));
    }

    /// <summary>前面のウィンドウが変わったとき (フックのスレッドから呼ばれる。ブロックしない)。</summary>
    public void InvalidateForeground()
    {
        Interlocked.Exchange(ref _foregroundChangedAt, Environment.TickCount64);
        Invalidate();
    }

    // 前面のウィンドウが変わった時刻と、移った先を調べ終わるのを待ち切れなかったフォーカス (同じフォーカスでは 2 回待たない)
    private long _foregroundChangedAt = long.MinValue / 2;
    private long _gaveUpSequence = -1;

    /// <summary>最新のフォーカスについて調べ終わっていて、パスワード以外の入力欄なら true。</summary>
    public bool CanCapture
    {
        get
        {
            if (Interlocked.Read(ref _resolvedSequence) != Interlocked.Read(ref _focusSequence)) return false;
            return AllowsCapture(_state);
        }
    }

    /// <summary>
    /// 打ち始めのキーで変換ボックスを開いてよいか。フォーカスを調べている最中なら、調べ終わるのを少しだけ (最大 waitMs) 待つ。
    /// それでも終わらなければ、直前に調べた結果を使う。
    /// Discord で # を打つとチャンネルの候補が開き、キーを打つたびにフォーカスの通知が来る (入力欄は同じまま) ので、
    /// 調べ終わるまで「入力欄ではない」にすると、# の後の英字がすべて変換ボックスを通らずに入ってしまっていた。
    /// </summary>
    public bool CanCaptureWaiting(int waitMs = 80)
    {
        // 待つのは、同じウィンドウで直前に調べた所が入力欄だったときだけ (Discord の # の候補)。
        // ゲームなど入力欄の無いウィンドウでは待たない: クリックのたびに調べ直すので、
        // キーを押すたびにフックで最大 waitMs 止まり、ゲームの操作が遅れていた (PSO2 NGS)。
        var lastState = _state;
        if (lastState.AffinityWindow != IntPtr.Zero) return AllowsCapture(lastState);
        var last = lastState.Info;
        // 別のウィンドウに移った直後 (500ms 以内) で、移った先をまだ調べているときも待つ。前のウィンドウ (デスクトップなど) の
        // 「入力欄ではない」で決めると、移った先の入力欄で打った先頭のキーがローマ字のまま入っていた。
        // 待ち切れなかったら、同じフォーカスの間は次から待たない (調べるのが遅いアプリで、キーを押すたびに止まらないように)。
        var sequence = Interlocked.Read(ref _focusSequence);
        var moved = Interlocked.Read(ref _resolvedSequence) != sequence && Interlocked.Read(ref _gaveUpSequence) != sequence &&
            Environment.TickCount64 - Interlocked.Read(ref _foregroundChangedAt) < 500;
        if (!moved && (!last.IsTextInput || last.IsPassword || Native.GetForegroundWindow() != _inspectedForeground)) waitMs = 0;
        var deadline = Environment.TickCount64 + waitMs;
        while (Interlocked.Read(ref _resolvedSequence) != Interlocked.Read(ref _focusSequence) && Environment.TickCount64 < deadline) Thread.Sleep(5);
        if (moved && Interlocked.Read(ref _resolvedSequence) != sequence) Interlocked.Exchange(ref _gaveUpSequence, sequence);
        // 調べ終わらなかったときに直前の結果を使うのは、前面のウィンドウが同じときだけ (別のアプリのパスワード欄などに移った直後は使わない)。
        if (Interlocked.Read(ref _resolvedSequence) != Interlocked.Read(ref _focusSequence) && Native.GetForegroundWindow() != _inspectedForeground) return false;
        return AllowsCapture(_state);
    }

    /// <summary>同じ HWND のまま編集を抜ける Affinity は、打ち始めごとに最大 20ms の読み取りで確認する。</summary>
    private bool AllowsCapture(FocusState snapshot)
    {
        if (snapshot.AffinityWindow == IntPtr.Zero) return snapshot.Info.IsTextInput && !snapshot.Info.IsPassword;
        var foreground = _inspectedForeground;
        var sequence = Interlocked.Read(ref _focusSequence);
        bool Current() => sequence == Interlocked.Read(ref _focusSequence) && Native.GetForegroundWindow() == foreground &&
            _state.AffinityWindow == snapshot.AffinityWindow && !_info.IsPassword;
        if (snapshot.Info.IsPassword || !Current()) return false;
        var active = HasActiveImeContext(foreground, snapshot.AffinityWindow);
        if (!Current()) return false;
        if (snapshot.Info.IsTextInput != active)
        {
            Enqueue(() =>
            {
                if (_info.IsTextInput == active || !Current() || HasActiveImeContext(foreground, snapshot.AffinityWindow) != active || !Current()) return;
                ApplyFocus(sequence, _info with { IsTextInput = active }, snapshot.AffinityWindow);
            });
        }
        return active && Current();
    }

    public FocusInfo Current => _info;

    private long _lastRecheck;
    private long _lastRecheckForeground;

    /// <summary>
    /// 同じ前面ウィンドウでの再確認は cooldownMs 以内なら抑止する。別ウィンドウへ移ったらすぐ再確認してよい
    /// (ブラウザーで再確認した直後に LINE へ移ると、共有クールダウンで最初の打鍵が素通りしていた)。
    /// </summary>
    internal static bool RecheckCooldownActive(IntPtr foreground, IntPtr lastRecheckForeground, long now, long lastRecheck, int cooldownMs = 1000) =>
        foreground != IntPtr.Zero && foreground == lastRecheckForeground && now - lastRecheck < cooldownMs;

    /// <summary>
    /// 前に調べた結果が「入力欄ではない」でも、ブラウザーではもう一度調べ直す (最大 waitMs 待つ。同じ前面ウィンドウでは 1 秒に 1 回まで)。
    /// Firefox でタブを切り替えた後、入力欄にフォーカスが移っても UI Automation の通知が来ず、「入力欄ではない」(ページそのもの) のまま
    /// 変換ボックスを出さなくなることがあった (issue #364)。打った時点で調べ直せば、そのときのフォーカスで決められる。
    /// </summary>
    public bool RecheckStale(int waitMs = 60)
    {
        var now = Environment.TickCount64;
        var foreground = Native.GetForegroundWindow();
        var lastForeground = new IntPtr(Interlocked.Read(ref _lastRecheckForeground));
        if (RecheckCooldownActive(foreground, lastForeground, now, Interlocked.Read(ref _lastRecheck))) return false;
        if (Interlocked.Read(ref _resolvedSequence) != Interlocked.Read(ref _focusSequence) || foreground != _inspectedForeground) return false;
        if (_info is { IsTextInput: true } or { IsPassword: true }) return false;
        Interlocked.Exchange(ref _lastRecheck, now);
        Interlocked.Exchange(ref _lastRecheckForeground, foreground.ToInt64());
        Invalidate();
        var deadline = now + waitMs;
        while (Interlocked.Read(ref _resolvedSequence) != Interlocked.Read(ref _focusSequence) && Environment.TickCount64 < deadline) Thread.Sleep(5);
        return CanCapture;
    }

    /// <summary>
    /// 前面のアプリが設定「入力欄とみなすアプリ」にあるか (このクラスのスレッドから呼ばれる)。
    /// そのアプリでは、入力欄と判定できなくてもフォーカスのある所を入力欄として扱う (issue #52)。
    /// </summary>
    public Func<bool>? TreatsAsTextInput { get; set; }

    /// <summary>
    /// 前面のアプリが、フォーカスがウィンドウそのもの (Window・Pane) にあるときも入力欄として扱うアプリか (issue #373: LINE)。
    /// LINE (Qt) は、ウィンドウを切り替えた直後、入力欄をクリックするまで変換ボックスが出なかった。そのときの UI Automation のフォーカスはウィンドウにあると見ている。
    /// </summary>
    public Func<bool>? TreatsWindowAsTextInput { get; set; }

    /// <summary>アプリ固有の入力欄判定に使う、前面アプリのプロセス名。このクラスのスレッドから呼ぶ。</summary>
    public Func<string>? ForegroundProcessName { get; set; }

    private Func<UiAutomation.Element, bool>? _selectionMatches;
    private long _selectionSequence;
    private IntPtr _selectionForeground;

    /// <summary>UIA 専用スレッドで選択文字を取得する。待つのは UI スレッドだけで、フックでは呼ばない。</summary>
    public string? SelectedText(int waitMs = 250)
    {
        string? result = null;
        var sequence = Interlocked.Read(ref _focusSequence);
        var foreground = Native.GetForegroundWindow();
        var done = new ManualResetEventSlim();
        Enqueue(() =>
        {
            try
            {
                _selectionMatches = null;
                if (!CanCapture || Input.ForegroundTracker.IsOwnWindow(foreground) ||
                    sequence != Interlocked.Read(ref _focusSequence) || foreground != Native.GetForegroundWindow()) return;
                if (Automation()?.Focused() is not { IsPassword: false, IsReadOnly: false } element) return;
                result = element.SelectedText(out _selectionMatches);
                _selectionSequence = sequence;
                _selectionForeground = foreground;
            }
            finally { done.Set(); }
        });
        return done.Wait(waitMs) && sequence == Interlocked.Read(ref _focusSequence) && foreground == Native.GetForegroundWindow() ? result : null;
    }

    public bool SelectionUnchanged(int waitMs = 250)
    {
        var result = false;
        var done = new ManualResetEventSlim();
        Enqueue(() =>
        {
            try
            {
                result = CanCapture && _selectionSequence == Interlocked.Read(ref _focusSequence) &&
                    _selectionForeground == Native.GetForegroundWindow() &&
                    Automation()?.Focused() is { IsPassword: false, IsReadOnly: false } element && _selectionMatches?.Invoke(element) == true;
            }
            finally { done.Set(); }
        });
        return done.Wait(waitMs) && result && _selectionSequence == Interlocked.Read(ref _focusSequence) && _selectionForeground == Native.GetForegroundWindow();
    }

    /// <summary>
    /// キャレット (入力位置) の画面上の四角形を UI Automation で調べる (Chrome・Discord など、Windows のキャレットを使わないアプリ用)。
    /// このクラスのスレッドで調べ、最大 waitMs 待つ。取れなければ null。
    /// </summary>
    public Rectangle? CaretBounds(int waitMs = 60)
    {
        Rectangle? result = null;
        var done = new ManualResetEventSlim();
        Enqueue(() =>
        {
            try
            {
                // UI Automation で取れなければ、ウィンドウのキャレット (MSAA の OBJID_CARET) を見る。Chrome は、Google ドキュメントのように
                // 入力位置を UI Automation で返さない編集画面でも、こちらでは返すことがある (拡大鏡などが使っている。issue #128)。
                if (Automation()?.Focused() is { IsPassword: false } element) result = element.CaretBounds() ?? AccessibleCaretBounds();
            }
            finally
            {
                done.Set();
            }
        });
        return done.Wait(waitMs) ? result : null;
    }

    /// <summary>フォーカスのあるウィンドウのキャレットの四角形 (MSAA の OBJID_CARET)。取れなければ null。</summary>
    private static Rectangle? AccessibleCaretBounds()
    {
        var thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var info = new Native.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(thread, ref info) || info.hwndFocus == IntPtr.Zero) return null;
        var iid = typeof(Accessibility.IAccessible).GUID;
        if (Native.AccessibleObjectFromWindow(info.hwndFocus, Native.OBJID_CARET, ref iid, out var accessible) != 0 || accessible is not Accessibility.IAccessible caret) return null;
        try
        {
            caret.accLocation(out var left, out var top, out var width, out var height, 0);
            // キャレットが無いときは 0,0,0,0 が返る
            return height is > 0 and < 200 && (left != 0 || top != 0) ? new Rectangle(left, top, Math.Max(1, width), height) : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(caret);
        }
    }

    /// <summary>キャレットの前後の文字列 (それぞれ最大 20 文字) を調べて callback(前, 後ろ) に渡す (このクラスのスレッドから呼ばれる)。</summary>
    public void RequestSurroundingText(Action<string?, string?> callback) => Enqueue(() =>
    {
        var (before, after) = ReadSurroundingText();
        callback(before, after);
    });

    /// <summary>キャレット直前の文字を新たに読み取り、再確定の対象と一致するか調べる。</summary>
    public bool TextBeforeCaretMatches(string expected, int waitMs = 250)
    {
        if (expected.Length == 0 || !CanCapture || Input.ForegroundTracker.IsOwnWindow(Native.GetForegroundWindow())) return false;
        var sequence = Interlocked.Read(ref _focusSequence);
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        var matches = false;
        using var done = new ManualResetEventSlim();
        Enqueue(() =>
        {
            try
            {
                if (!CanCapture || sequence != Interlocked.Read(ref _focusSequence) ||
                    foreground != Native.GetForegroundWindow() || Input.ForegroundTracker.IsOwnWindow(foreground)) return;
                if (Automation()?.Focused() is not { IsPassword: false, IsReadOnly: false } element) return;
                var before = element.Surrounding(expected.Length)?.Before;
                matches = before?.EndsWith(expected, StringComparison.Ordinal) == true;
            }
            finally { done.Set(); }
        });
        return done.Wait(waitMs) && matches &&
            sequence == Interlocked.Read(ref _focusSequence) && foreground == Native.GetForegroundWindow();
    }

    private void Enqueue(Action action)
    {
        if (!_work.IsAddingCompleted)
        {
            try { _work.Add(action); }
            catch (InvalidOperationException) { }
        }
    }

    private void Run()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception ex) { Diagnostics.Log.Warn($"フォーカスの確認で例外: {ex.Message}"); }
        }
    }

    private void InspectIfLatest(long sequence)
    {
        // 連続したフォーカス変更は、最新の 1 回だけ調べる。
        if (sequence != Interlocked.Read(ref _focusSequence)) return;
        var info = Inspect(out var affinityWindow);
        ApplyFocus(sequence, info, affinityWindow);
    }

    private void ApplyFocus(long sequence, FocusInfo info, IntPtr affinityWindow)
    {
        _state = new FocusState(info, affinityWindow);
        // 調べている間にまたフォーカスが変わっていたら、この結果は採用しない (次の要求で調べ直す)。
        if (sequence == Interlocked.Read(ref _focusSequence)) Interlocked.Exchange(ref _resolvedSequence, sequence);

        // 変換中に入力先がパスワード欄・入力欄でない所に変わったら、変換中の内容を捨てさせる (移った先に入らないように)。
        // UI Automation で調べられなかっただけ (確認できない・フォーカスなし) のときは捨てない。
        if (sequence == Interlocked.Read(ref _focusSequence) && (info.IsPassword || !info.IsTextInput && !info.Description.StartsWith("確認できない") && info.Description != "フォーカスなし"))
        {
            CaptureLost?.Invoke(info.IsPassword ? "パスワード欄にフォーカスが移った" : "入力欄でない所にフォーカスが移った");
        }

        if (info.IsTextInput && !info.IsPassword)
        {
            var key = (info.Description, info.Bounds);
            if (_lastTextInput != key)
            {
                _lastTextInput = key;
                TextInputEntered?.Invoke();
            }
        }
        else _lastTextInput = null;

        // 変換ボックスが出ない理由を後から追えるように、判断が変わったらログに残す。
        var summary = $"{(info.IsPassword ? "パスワード欄" : info.IsTextInput ? "入力欄" : "入力欄ではない")}: {info.Description}";
        if (summary != _loggedDescription)
        {
            _loggedDescription = summary;
            Diagnostics.Log.Info($"フォーカス → {summary}{(info.IsTextInput && !info.IsPassword ? "" : " (変換ボックスは出しません)")}");
        }
    }

    private UiAutomation? _automation;
    private bool _automationUnavailable;

    /// <summary>UI Automation はこのクラスのスレッド (MTA) で作り、そのスレッドからだけ使う。</summary>
    private UiAutomation? Automation()
    {
        if (_automation is not null || _automationUnavailable) return _automation;
        try
        {
            _automation = new UiAutomation();
        }
        catch (Exception ex)
        {
            _automationUnavailable = true;
            Diagnostics.Log.Warn($"UI Automation を使えません。変換ボックスは出せません: {ex.Message}");
        }
        return _automation;
    }

    /// <summary>最後に調べたときの前面のウィンドウ。</summary>
    private IntPtr _inspectedForeground;

    private FocusInfo Inspect(out IntPtr affinityWindow)
    {
        affinityWindow = IntPtr.Zero;
        try
        {
            _inspectedForeground = Native.GetForegroundWindow();
            // Meltype 自身の画面 (設定のドロップダウンなど) には UI Automation で問い合わせない。
            // 自分の UI スレッドに問い合わせが割り込むと、開いているドロップダウンが閉じてしまう。
            if (Input.ForegroundTracker.IsOwnWindow(Native.GetForegroundWindow())) return new FocusInfo(false, false, null, "Meltype の画面");
            var forced = TreatsAsTextInput?.Invoke() == true;
            var element = Automation()?.Focused();
            if (element is null) return forced ? new FocusInfo(true, false, null, "フォーカスなし (入力欄とみなすアプリ)") : new FocusInfo(false, false, null, "フォーカスなし");
            var type = element.ControlType;
            var description = $"{ControlTypeName(type)} \"{Trim(element.Name)}\" ({element.ClassName})";
            if (element.IsPassword) return new FocusInfo(true, true, element.Bounds, description);

            var editable = false;
            if (type is UiAutomation.ControlTypeEdit or UiAutomation.ControlTypeDocument)
            {
                editable = !(element.HasValuePattern && element.IsReadOnly);
            }
            else if (type is UiAutomation.ControlTypeComboBox)
            {
                // 選ぶだけのドロップダウン (設定画面の はい/いいえ など) は文字を打つ欄ではない。打ち込める ComboBox だけ。
                editable = element.HasValuePattern && !element.IsReadOnly;
            }
            else if (element.ClassName?.Contains("autocomplete", StringComparison.OrdinalIgnoreCase) == true ||
                element.ClassName?.Contains("OmniLinkItem", StringComparison.Ordinal) == true)
            {
                // 入力欄の候補の一覧 (Discord の @メンション・#チャンネルの候補、Vivaldi の検索欄・アドレスバーの候補)。
                // フォーカスは候補の行に移るが、打った文字は入力欄に入るので、入力欄として扱う。
                editable = true;
            }
            else if (element.HasTextPattern && element.IsKeyboardFocusable)
            {
                // Windows Terminal などは Edit ではなく TextPattern を持つ独自コントロール。
                editable = true;
            }
            else if (type == ControlTypeGroup && element.IsKeyboardFocusable && LooksLikeInputClass(element.ClassName))
            {
                // Web アプリの独自の入力欄 (Chatwork の検索欄: Group "チャット名、メッセージ内容を検索" (… inputLong))。
                // UI Automation では Group で、ValuePattern も TextPattern も IAccessible2 の EDITABLE も持たないが、クラス名が入力欄 (issue #70)
                editable = true;
                description += " (クラス名が入力欄)";
            }
            else if (element.IsKeyboardFocusable && element.IsIa2Editable())
            {
                // Chromium / Electron の contenteditable は、空欄の間は UI Automation では
                // Group で ValuePattern / TextPattern を持たなくても、IAccessible2 では
                // EDITABLE として公開されることがある。
                editable = true;
                description += " (IAccessible2: 編集可能)";
            }

            // UI Automation では入力欄と分からなくても、Windows のキャレット (点滅する縦線) を出しているなら文字を打つ所
            // (サクラエディタなど、独自の編集画面を持つ Win32 のアプリ)。
            if (!editable && HasCaret())
            {
                editable = true;
                description += " (キャレットあり)";
            }
            // 画面をすべて自分で描くエディター (Zed・LibreOffice) は、UI Automation でもキャレットでも入力欄と分からない (issue #75, #228)。
            // フォーカスがウィンドウそのものにあるときは編集画面とみなす。Zed は「コード」の種類なので、変換ボックスを開くのは
            // コメント・文字列の中か、半角/全角 で日本語にした行だけ (ほかの所のキーは今までどおりそのまま通す)。
            if (!editable && element.ClassName is { } windowClass && EditorWindowClasses.Contains(windowClass))
            {
                editable = true;
                description += " (エディターの画面)";
            }
            // LINE: フォーカスがウィンドウそのもの (Window・Pane) なら、入力欄に打っているとみなす (#373)
            if (!editable && type is ControlTypeWindow or ControlTypePane && TreatsWindowAsTextInput?.Invoke() == true)
            {
                editable = true;
                description += " (ウィンドウを入力欄とみなすアプリ)";
            }
            // Affinity はキャンバスを照合し、文字編集中の IME 応答で確認する (issue #289)。
            if (!editable && IsAffinityCanvasCandidate(ForegroundProcessName?.Invoke(), type, element.ClassName))
            {
                // 0 は Affinity の追加確認なしを表す。
                var window = element.NativeWindowHandle;
                if (window != 0 && Automation()?.IsRenderControl(_inspectedForeground, element, window) == true)
                {
                    affinityWindow = new IntPtr(window);
                    editable = HasActiveImeContext(_inspectedForeground, affinityWindow);
                    description += " (Affinity のキャンバス)";
                }
            }
            // それでも分からないアプリ (Premiere Pro など、画面を自分で描くアプリ) は、設定「入力欄とみなすアプリ」に
            // 書いてあれば入力欄として扱う。1 文字のショートカットが多いアプリもあるので、既定では何もしない。
            if (!editable && forced && affinityWindow == IntPtr.Zero)
            {
                editable = true;
                description += " (入力欄とみなすアプリ)";
            }
            return new FocusInfo(editable, false, element.Bounds, description, element.Name, element.ClassName ?? "");
        }
        catch (Exception ex)
        {
            affinityWindow = IntPtr.Zero;
            return new FocusInfo(false, false, null, $"確認できない: {ex.GetType().Name}");
        }
    }

    private const int ControlTypeGroup = 50026;

    /// <summary>
    /// Web の要素のクラス名 (空白区切り) に、入力欄を表す名前があるか: input・inputLong・searchInput・search-input・chat_input。
    /// 入力欄を囲む枠・ボタン (input-group、inputWrapper、inputButton) は除く。
    /// </summary>
    internal static bool LooksLikeInputClass(string? className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        foreach (var token in className.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = token.ToLowerInvariant();
            var at = lower.IndexOf("input", StringComparison.Ordinal);
            if (at < 0) continue;
            if (NotInputParts.Any(lower.Contains)) continue;
            // input の前は語の頭 (先頭・区切り・大文字の語の切れ目: searchInput)、後ろは終わりか大文字・区切り (inputLong、input-field)
            var start = at == 0 || lower[at - 1] is '-' or '_' || char.IsAsciiLetterUpper(token[at]);
            var end = at + 5;
            var finish = end == token.Length || token[end] is '-' or '_' || char.IsAsciiLetterUpper(token[end]);
            if (start && finish) return true;
        }
        return false;
    }

    private static readonly string[] NotInputParts = ["group", "wrap", "container", "label", "button", "btn", "icon"];
    private const int ControlTypeWindow = 50032, ControlTypePane = 50033;

    internal static bool IsAffinityCanvasCandidate(string? processName, int controlType, string? className) =>
        string.Equals(processName, "Affinity.exe", StringComparison.OrdinalIgnoreCase) &&
        (controlType is UiAutomation.ControlTypePane or UiAutomation.ControlTypeText) && className == "Static";

    private static bool HasActiveImeContext(IntPtr foreground, IntPtr contextWindow)
    {
        bool StillFocused() => contextWindow != IntPtr.Zero && IME.ImeTarget.FromForeground() is { } target &&
            target.TopLevel == foreground && target.Focus == contextWindow;
        return StillFocused() && IME.Imm32ImeController.HasInputContext(contextWindow, 20) && StillFocused();
    }

    /// <summary>入力欄が UI Automation に出てこない、画面をすべて自分で描くエディターのウィンドウのクラス名。</summary>
    // Zed::Window: Zed (issue #75)
    // SALFRAME: LibreOffice (Writer・Calc・Impress など)。支援技術が動いていないと UI Automation で文書の中身を出さず、
    // フォーカスは文書の窓そのもの (ControlType Window) になる (issue #228)。Calc のセルも、打てばそのまま編集が始まる
    private static readonly HashSet<string> EditorWindowClasses = new(StringComparer.Ordinal) { "Zed::Window", "SALFRAME" };

    /// <summary>前面のウィンドウのスレッドが、フォーカスのあるウィンドウにキャレットを出しているか。</summary>
    private static bool HasCaret()
    {
        var thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var info = new Native.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        return Native.GetGUIThreadInfo(thread, ref info) && info.hwndCaret != IntPtr.Zero && info.hwndCaret == info.hwndFocus;
    }

    private static string ControlTypeName(int type) => type switch
    {
        UiAutomation.ControlTypeEdit => "Edit",
        UiAutomation.ControlTypeDocument => "Document",
        UiAutomation.ControlTypeComboBox => "ComboBox",
        _ => $"ControlType {type}",
    };

    /// <summary>
    /// キャレットの前後の文字列。TextPattern (Chrome, Word, メモ帳など) ならキャレット位置から前後 20 文字ずつ、
    /// 無ければ ValuePattern の値の末尾 (キャレットが末尾にあるとみなす) を前として返す。取れなければ null。
    /// </summary>
    private (string? Before, string? After) ReadSurroundingText()
    {
        try
        {
            var element = Automation()?.Focused();
            if (element is null || element.IsPassword) return (null, null);
            if (element.Surrounding(20) is { } surrounding) return surrounding;
            if (element.HasValuePattern)
            {
                var value = element.Value;
                return (value.Length > 20 ? value[^20..] : value, null);
            }
        }
        catch
        {
            // 取れなければ自分の確定履歴で判断する。
        }
        return (null, null);
    }

    /// <summary>
    /// キャレットより前の文字列 (最大 300 文字) を読んで callback に渡す (このクラスのスレッドから呼ばれる)。
    /// コードエディターで、キャレットがコメントや文字列の中にあるかを調べるのに使う。TextPattern が無ければ null。
    /// </summary>
    public void RequestTextBeforeCaret(Action<string?> callback) => Enqueue(() =>
    {
        string? before = null;
        try
        {
            if (Automation()?.Focused() is { IsPassword: false } element) before = element.Surrounding(3000)?.Before;
        }
        catch
        {
            // 読めなければ分からないまま (コードとして扱う)。
        }
        callback(before);
    });

    /// <summary>自己診断用: UI Automation でデスクトップの要素を取れるか。</summary>
    public static string Probe() => new UiAutomation().Root() is { } root ? ControlTypeName(root.ControlType) : "(取れない)";

    private static string Trim(string text) => text.Length > 30 ? text[..30] + "…" : text;

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join(1000);
    }
}
