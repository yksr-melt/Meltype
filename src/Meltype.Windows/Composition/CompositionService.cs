// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>
/// Meltype.exe の 2 つのフロント (Meltype キーボード・Meltype IME) が使う Windows の部品 (UI スレッドで作る)。
/// 変換エンジン・辞書・学習データ (入力欄ごとの <see cref="MeltypeSession"/> で共有する)、フォーカスのある要素 (UI Automation)、
/// 入力モードの表示と、Meltype キーボードの変換ボックス・確定した文字の送り込み (SendInput・貼り付け)。判断はしない。
/// </summary>
internal sealed class CompositionService : IDisposable
{
    private readonly Control _invoker;
    private readonly CompositionWindow _window = new();
    private readonly ModeIndicatorWindow _indicator = new();
    private readonly Func<Config.Settings> _settings;
    private readonly KeyInjector _injector = new();
    // Mozc (同梱の mozc\meltype_mozc_helper.exe) と Zenzai (zenzai\)
    private readonly HybridConverter _hybrid;

    /// <param name="runtime">設定・辞書・学習データ (どの OS も同じもの)。</param>
    /// <param name="settings">Meltype の設定 (入力欄ごとの設定は <see cref="CreateSession"/> で渡す)。変換ボックスの見た目を決める。</param>
    public CompositionService(Control invoker, MeltypeRuntime runtime, Func<Config.Settings> settings)
    {
        _invoker = invoker;
        Runtime = runtime;
        _settings = settings;
        _hybrid = runtime.CreateConverter(AppContext.BaseDirectory);
        _hybrid.Budget.Enable();
        _hybrid.ResultReady += () => ConversionReady?.Invoke();
        _hybrid.WarmUp();
    }

    public MeltypeRuntime Runtime { get; }

    /// <summary>
    /// 入力欄 1 つ分の入力の本体を作る。辞書・学習データ・変換エンジンは、ほかの入力欄・変換ボックスと共有する
    /// (UI スレッドからだけ使うので排他は要らない)。
    /// </summary>
    public MeltypeSession CreateSession(Func<Config.Settings> settings, bool directModeDetection = false)
    {
        var session = Runtime.CreateSession(_hybrid, settings, _hybrid.Candidates, directModeDetection: directModeDetection);
        // 直したときの「Zenzai を使う」も覚えておく (報告を開くまでに設定を変えても、直したときの設定で報告する。issue #1197)
        session.Corrected += correction => Corrections.Add(correction with { Zenzai = _settings().UseZenzai });
        return session;
    }

    /// <summary>背景の変換の結果が届いた (入力欄に知らせる)。ワーカーのスレッドで呼ぶ。</summary>
    public event Action? ConversionReady;

    /// <summary>選び直した変換の学習データ (トレイの「学習データをリセット」で消す)。</summary>
    public ConversionHistory History => Runtime.History;

    /// <summary>ユーザーが英字 / かなに直した語の学習 (トレイの「学習データをリセット」で消す)。</summary>
    public LanguageMemory Languages => Runtime.Languages;

    /// <summary>選んだ英訳の記録 (トレイの「学習データをリセット」で消す)。</summary>
    public TranslationHistory TranslationHistory => Runtime.TranslationHistory;

    /// <summary>予測変換のために覚えた、確定した語句。</summary>
    public PhraseHistory Phrases => Runtime.Phrases;

    /// <summary>ユーザー辞書 (トレイの「ユーザー辞書...」で編集する)。</summary>
    public UserDictionary UserDictionary => Runtime.UserDictionary;

    /// <summary>登録した定型文 (トレイの「定型文...」で編集する。;名前 + Space で入れる: issue #290)。</summary>
    public SnippetStore Snippets => Runtime.Snippets;

    /// <summary>最近自分で直した誤判定 (メモリの中だけ。トレイの「直した誤判定を報告...」: issue #284)。</summary>
    public CorrectionLog Corrections { get; } = new();


    /// <summary>
    /// ユーザー辞書の登録画面用: 入力された読み (ローマ字でもよい) をひらがなにする。
    /// </summary>
    public string ToReading(string input)
    {
        var text = input.Trim();
        return text.Any(char.IsAsciiLetter) ? Runtime.Detector.Romaji.ConvertLenient(text.ToLowerInvariant(), final: true) : text;
    }

    /// <summary>ユーザー辞書の登録画面用: 語の読みを推測する。分からなければ空。</summary>
    public string GuessReading(string word) => _hybrid.Reading(word) ?? "";

    // 登録画面は入力のスレッド (UI スレッド) から呼ぶ。入力のように待たずに返すと、ヘルパーが答える前の空の一覧になる
    private const int SuggestWaitMs = 1500;

    /// <summary>ユーザー辞書の登録画面用: 読みの変換候補 (Mozc の候補 → カタカナ)。</summary>
    public IReadOnlyList<string> SuggestWords(string reading)
    {
        _hybrid.Budget.Start(SuggestWaitMs);
        var list = _hybrid.Candidates(reading).Where(word => word.Length > 0).ToList();
        if (CompositionText.ToKatakana(reading) is { Length: > 0 } katakana && !list.Contains(katakana)) list.Add(katakana);
        return list;
    }

    public FocusInspector Focus { get; } = new();

    /// <summary>
    /// 確定した文字を入れる: 前の count 文字を消してから text を入れる。paste なら貼り付けで入れる (貼り付けられなければキーで送る。改行は Shift+Enter)。
    /// キーで送るときは、BackSpace と新しい文字を 1 回の SendInput でまとめて送る (間に物理キーやほかの入力が割り込まないように。issue #227)。
    /// </summary>
    public void Insert(int count, string text, bool paste)
    {
        // 変換ボックスを出したまま確定して、続けて打っている (kyouha Space iitenki): 次に見せるときに、確定した分だけ右へずらす
        if (_window.Visible) _committedWhileVisible += text;
        EnsureSystemImeClosed();
        if (!paste)
        {
            SendText(count, text);
            return;
        }
        DeleteBackward(count);
        if (TryPaste(text.ReplaceLineEndings("\r\n")))
        {
            Diagnostics.Log.Info($"確定した文字を貼り付けで入力しました ({text.Length} 文字)。");
            return;
        }
        Diagnostics.Log.Warn("貼り付けられなかったので、1 文字ずつ送ります (改行は Shift+Enter)。");
        foreach (var (line, i) in text.Split('\n').Select((line, i) => (line, i)))
        {
            if (i > 0) KeyInjector.SendShortcut(VirtualKeys.Shift, VirtualKeys.Return);
            SendText(0, line);
        }
    }

    private static void SendText(int count, string text)
    {
        var inputs = new List<Native.INPUT>(count * 2 + text.Length * 2);
        for (var i = 0; i < count; i++)
        {
            inputs.Add(KeyInput(VirtualKeys.Back, up: false));
            inputs.Add(KeyInput(VirtualKeys.Back, up: true));
        }
        AddUnicodeText(inputs, text);
        Native.SendAll(inputs.ToArray(), count > 0 ? "確定し直し" : "確定文字列の入力");
    }

    private DataObject? _savedClipboard;
    private System.Windows.Forms.Timer? _restoreClipboard;
    /// <summary>貼る文字を入れた直後のクリップボードの番号。戻すときに変わっていたら、ほかのアプリかユーザーが入れたものなので戻さない</summary>
    private uint _pastedSequence;

    /// <summary>Windows のクリップボード履歴に保持しない</summary>
    private static readonly string[] ClipboardHistoryExclusions =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
    ];

    private bool TryPaste(string text)
    {
        if (text.Length == 0) return true;
        try
        {
            // 前の貼り付けのあとにユーザーがコピーしていたら、戻す中身をそれに取り直す
            if (_restoreClipboard is null || Native.GetClipboardSequenceNumber() != _pastedSequence) _savedClipboard = CopyClipboard();
            _restoreClipboard?.Stop();
            Clipboard.SetDataObject(PasteData(text), copy: true);
            _pastedSequence = Native.GetClipboardSequenceNumber();
            KeyInjector.SendShortcut(VirtualKeys.Control, 0x56); // Ctrl+V
            _restoreClipboard ??= new System.Windows.Forms.Timer { Interval = 500 };
            _restoreClipboard.Tick -= RestoreClipboard;
            _restoreClipboard.Tick += RestoreClipboard;
            _restoreClipboard.Start();
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or System.Threading.ThreadStateException)
        {
            Diagnostics.Log.Warn($"クリップボードを使えないので、1 文字ずつ送ります: {ex.Message}");
            // 前の貼り付けで止めた復元を動かし直す (止めたままだと元の中身に戻らない)
            _restoreClipboard?.Start();
            return false;
        }
    }

    internal static DataObject PasteData(string text)
    {
        var data = new DataObject();
        data.SetText(text);
        foreach (var format in ClipboardHistoryExclusions)
        {
            data.SetData(format, autoConvert: false, new MemoryStream([0, 0, 0, 0]));
        }
        return data;
    }

    /// <summary>
    /// 今のクリップボードの中身を、形式ごとに取り出して写す (戻すときに使う)。
    /// Clipboard.GetDataObject() は中身のコピーではなく、そのときのクリップボードを読みに行くものを返すので、
    /// 別の中身を入れたあとにそれで戻しても元に戻らない。取り出せない形式は戻さない。中身が無ければ null。
    /// </summary>
    internal static DataObject? CopyClipboard()
    {
        if (Clipboard.GetDataObject() is not { } current) return null;
        var copy = new DataObject();
        foreach (var format in current.GetFormats(autoConvert: false))
        {
            // 画像 (Bitmap) は autoConvert: false だと取り出せない
            try { if ((current.GetData(format, autoConvert: false) ?? current.GetData(format, autoConvert: true)) is { } data) copy.SetData(format, autoConvert: false, data); }
            catch { }
        }
        return copy.GetFormats(autoConvert: false).Length > 0 ? copy : null;
    }

    private void RestoreClipboard(object? sender, EventArgs e)
    {
        var timer = _restoreClipboard;
        _restoreClipboard = null;
        timer?.Dispose();
        try
        {
            if (Native.GetClipboardSequenceNumber() != _pastedSequence) Diagnostics.Log.Info("貼り付けのあとにクリップボードが変わったので、元の中身に戻しません。");
            else if (_savedClipboard is { } saved) Clipboard.SetDataObject(saved, copy: true);
            else Clipboard.Clear();
        }
        catch (ExternalException ex)
        {
            Diagnostics.Log.Warn($"クリップボードの中身を戻せませんでした: {ex.Message}");
        }
        _savedClipboard = null;
    }

    /// <summary>
    /// 確定文字列を送る前に、アプリ側の Microsoft IME が閉じていることを確かめる。
    /// IME が開いたままだと、送り込んだ文字の一部を IME が抱え込み、後から来た文字と順番が入れ替わることがある
    /// (「やあやあ、私だよ」→「やあやあ、だよ私」)。
    /// </summary>
    private static void EnsureSystemImeClosed()
    {
        try
        {
            if (WindowsIme.ImeTarget.FromForeground() is not { } target) return;
            var state = WindowsIme.Imm32ImeController.GetState(target);
            if (state.Mode != WindowsIme.ImeMode.Open) return;
            var closed = WindowsIme.Imm32ImeController.TrySetOpen(target, false, null, 150);
            Diagnostics.Log.Warn($"確定時に Microsoft IME が ON になっていました ({state}) → {(closed ? "OFF にしてから入力します" : "OFF にできませんでした")}。");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"Microsoft IME の状態を確認できませんでした: {ex.Message}");
        }
    }

    public void Replay(KeyEvent e) => _injector.Inject([e]);

    public void MoveCaretBackward(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Replay(new KeyEvent(VirtualKeys.Left, 0, false, false, false, Environment.TickCount64));
            Replay(new KeyEvent(VirtualKeys.Left, 0, false, true, false, Environment.TickCount64));
        }
    }

    public void DeleteBackward(int count)
    {
        if (count <= 0) return;
        // Microsoft IME が ON のままだと、BackSpace と後から送る文字の順番が入れ替わることがある (issue #227: wanI t)
        EnsureSystemImeClosed();
        var events = new List<KeyEvent>(count * 2);
        for (var i = 0; i < count; i++)
        {
            events.Add(new KeyEvent(VirtualKeys.Back, 0, false, false, false, 0));
            events.Add(new KeyEvent(VirtualKeys.Back, 0, false, true, false, 0));
        }
        _injector.Inject(events);
    }

    public static void Replay(MouseButtonEvent e)
    {
        var (button, data) = e.Message switch
        {
            Native.WM_LBUTTONDOWN => (Native.MOUSEEVENTF_LEFTDOWN, 0u),
            Native.WM_LBUTTONUP => (Native.MOUSEEVENTF_LEFTUP, 0u),
            Native.WM_RBUTTONDOWN => (Native.MOUSEEVENTF_RIGHTDOWN, 0u),
            Native.WM_RBUTTONUP => (Native.MOUSEEVENTF_RIGHTUP, 0u),
            Native.WM_MBUTTONDOWN => (Native.MOUSEEVENTF_MIDDLEDOWN, 0u),
            Native.WM_MBUTTONUP => (Native.MOUSEEVENTF_MIDDLEUP, 0u),
            Native.WM_XBUTTONDOWN => (Native.MOUSEEVENTF_XDOWN, e.MouseData >> 16),
            Native.WM_XBUTTONUP => (Native.MOUSEEVENTF_XUP, e.MouseData >> 16),
            _ => (0u, 0u),
        };
        if (button == 0) return;
        // 元のクリック位置で再生する (保留中にカーソルが動いていても同じ場所をクリックする)。
        var left = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        var top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        var width = Math.Max(1, Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN) - 1);
        var height = Math.Max(1, Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN) - 1);
        var input = new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new Native.InputUnion
            {
                mi = new Native.MOUSEINPUT
                {
                    dx = (int)Math.Round((e.X - left) * 65535.0 / width),
                    dy = (int)Math.Round((e.Y - top) * 65535.0 / height),
                    mouseData = data,
                    dwFlags = button | Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK,
                    dwExtraInfo = KeyboardMonitor.InjectedMarker,
                },
            },
        };
        Native.SendAll([input], "クリックの再生");
    }

    /// <summary>
    /// 入力モード (日本語なら「あ」、英数なら「A」) をカーソルの近くに milliseconds だけ出す。どのスレッドから呼んでもよい。
    /// 変換ボックスが出ているときは出さない。
    /// </summary>
    public void ShowMode(bool japanese, int milliseconds, uint inputThread = 0, Point? inputAnchor = null)
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return;
        _invoker.BeginInvoke(() =>
        {
            if (_window.Visible) return;
            // TSF の通知が届くまでに別の入力先へ移っていたら表示しない。
            if (inputThread != 0 && WindowsIme.ImeTarget.FromForeground()?.ThreadId != inputThread) return;
            var anchor = inputAnchor ?? (inputThread == 0 ? FindAnchor() : FindImeIndicatorAnchor());
            var (jp, direct, text) = (_settings().ModeIndicatorJapaneseColor, _settings().ModeIndicatorDirectColor, _settings().ModeIndicatorTextColor);
            _indicator.SetColors(Config.Settings.ParseColor(jp), Config.Settings.ParseColor(direct), Config.Settings.ParseColor(text));
            _indicator.Flash(japanese, new Point(anchor.X + 2, anchor.Y + 2), milliseconds);
        });
    }

    /// <summary>text を、Meltype IME の入力モードの表示と同じ所 (入力位置の下) に milliseconds だけ出す。どのスレッドから呼んでもよい。</summary>
    public void ShowNotice(string text, int milliseconds)
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return;
        _invoker.BeginInvoke(() =>
        {
            var anchor = FindImeIndicatorAnchor();
            _indicator.SetColors(null, Config.Settings.ParseColor(_settings().ModeIndicatorDirectColor), Config.Settings.ParseColor(_settings().ModeIndicatorTextColor));
            _indicator.FlashNotice(text, new Point(anchor.X + 2, anchor.Y + 2), milliseconds);
        });
    }

    private Point FindImeIndicatorAnchor()
    {
        if (FindCaret() is { } caret) return new Point(caret.Left, caret.Bottom + 4);
        // 空の入力欄とは限らないため、入力欄の左上をキャレット位置と推測しない。
        Native.GetCursorPos(out var cursor);
        return new Point(cursor.X + 12, cursor.Y + 20);
    }


    /// <summary>クリップボードの文字 (変換した瞬間に 1 回だけ読む)。履歴に残さない印のある内容 (パスワード管理ソフトなど) は読まない。</summary>
    public static string? ClipboardText
    {
        get
        {
            try
            {
                if (!System.Windows.Forms.Clipboard.ContainsText()) return null;
                if (System.Windows.Forms.Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return null;
                return System.Windows.Forms.Clipboard.GetText();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
            {
                return null;
            }
        }
    }

    /// <summary>入力位置の高さとして信じる上限 (ピクセル)。これより高いのは入力欄や行全体の四角形。</summary>
    private const int MaxLineHeight = 48;

    /// <summary>変換ボックスを出したまま確定した文字 (次に見せるときに、その幅だけ変換ボックスを右へずらす)。</summary>
    private string _committedWhileVisible = "";

    public void Show(CompositionView view)
    {
        if (_window.Visible)
        {
            // Windows の検索の画面の横に出しているときは、確定しても動かさない。左なら右の端を、上なら下の端をそろえる (広がっても検索の画面の裏に入らないように)
            if (_beside != BesideSearch.None)
            {
                _committedWhileVisible = "";
                if (_beside == BesideSearch.Left) _window.ShowView(view, new Point(_window.Left, _window.Top), rightEdge: _searchLeftEdge);
                else _window.ShowView(view, null);
                return;
            }
            // 確定した文字の分だけ右へ (同じ位置のままだと、確定した文字を変換ボックスが隠してしまう: issue #57)。
            // 入力欄のキャレットは確定した文字の入力が終わるまで動かないことがあるので、確定した文字の幅で動かす。
            // 改行を含むとき・右へずらすと画面の外に出るとき (折り返し) は、ずらさずに入力欄のキャレットの位置を取り直す。
            Point? moved = null;
            var committed = _committedWhileVisible;
            _committedWhileVisible = "";
            if (committed.Length > 0)
            {
                var x = _window.Left + _window.TextWidth(committed);
                var screen = Screen.FromPoint(new Point(_window.Left, _window.Top)).WorkingArea;
                if (!committed.Contains('\n') && x + _window.Width <= screen.Right) moved = new Point(x, _window.Top);
            }
            if (committed.Length == 0 || moved is not null)
            {
                _window.ShowView(view, moved);
                return;
            }
        }
        _committedWhileVisible = "";
        _beside = BesideSearch.None;
        var caret = FindCaret();
        // どのモニターにも入らない入力位置 (フォーカスを失った窓の -32000 など) は、分からないものとして扱う。
        // そのまま使うと、そこから決めた拡大率・文字の大きさ・位置で、変換ボックスが画面の外や画面より大きく出て見えなくなった (issue #1138)
        if (caret is { } outside && !Screen.AllScreens.Any(screen => screen.Bounds.Contains(outside.Location)))
        {
            Diagnostics.Log.Info($"変換ボックスを出す入力位置がどのモニターにも入らないので、分からないものとして扱います: {outside.X},{outside.Y} 高さ {outside.Height}");
            caret = null;
        }
        // 入力欄が空のとき、アプリによっては入力位置ではなく入力欄の枠 (40px の欄など) や、複数行の欄全体の四角形が返る。
        // 枠と同じ高さなら、文字の高さは枠のおよそ半分 (1 行の欄の文字は上下の真ん中にある)。高すぎる四角形は、文字の高さが分からない。
        var element = Focus.Current.Bounds;
        var wholeField = caret is { Height: > 22 } c0 && element is { } e && Math.Abs(e.Height - c0.Height) <= 6;
        int? textHeight = caret switch
        {
            null => null,
            { Height: > MaxLineHeight } => null,
            { } c when wholeField => (int)(c.Height * 0.45),
            { } c => c.Height,
        };
        Diagnostics.Log.Info($"変換ボックスを出す入力位置: {(caret is { } r ? $"{r.X},{r.Y} 高さ {r.Height}{(wholeField ? " (入力欄の枠)" : "")}" : "分からない")}");
        _window.SetFontFamily(_settings().CompositionFont);
        _window.SetAppearance(_settings().CompositionIsLight(CompositionWindow.WindowsUsesLightTheme()), _settings().CompositionOpacityValue);
        // 出すモニターの拡大率に合わせる (入力位置のモニター。分からなければ変換ボックスを置く位置のモニター)
        var monitorScale = MonitorScale(caret?.Location ?? FindAnchor(caret, caretSearched: true));
        _window.SetMonitorScale(monitorScale);
        // 文字の大きさ: 自動なら、入力欄の文字の高さに合わせる。小さな入力欄で大きく出すぎないように。
        // 入力位置の高さはそのモニターのピクセルなので、拡大率の分を除いてから比べる
        _window.SetScale(_settings().CompositionSize switch
        {
            Config.CompositionSize.Small => 0.8F,
            Config.CompositionSize.Large => 1.25F,
            Config.CompositionSize.ExtraLarge => 1.6F,
            Config.CompositionSize.Huge => 2F,
            Config.CompositionSize.Auto when textHeight is { } h && h >= 8 => Math.Clamp(h / (_window.BaseTextHeight * monitorScale), 0.7F, 1.4F),
            // 文字の高さが分からないときは、ふつうの画面の文字 (16px 前後) に近い大きさ
            Config.CompositionSize.Auto => 0.8F,
            _ => 1F,
        });
        // Windows の検索・スタートメニューは、ふつうのアプリより上の特別な層に出るので、重なると変換ボックスが隠れてしまう。
        // 検索の画面の左に入れば左、上に打った行が入れば上、どちらも入らなければ右に出す。
        // Windows 11 の中央寄せのスタートは、検索の画面の右に「モバイル デバイス」の欄が並ぶので、右は最後にする
        // (スタート・検索の窓は CoreWindow で EnumWindows に出てこないので、横に並ぶ窓を集めて避けることもできない)。
        if (ShellSearchBounds() is { } search)
        {
            var screen = Screen.FromRectangle(search).WorkingArea;
            var size = _window.Measure(view);
            if (search.Left - 8 - size.Width >= screen.Left)
            {
                Diagnostics.Log.Info($"Windows の検索の画面: {search} (変換ボックスはその左に出す)");
                _beside = BesideSearch.Left;
                _searchLeftEdge = search.Left - 8;
                _window.ShowView(view, new Point(search.Left - 8 - size.Width, (caret?.Bottom ?? search.Top) - 4), rightEdge: _searchLeftEdge);
            }
            else if (search.Top - 8 - screen.Top >= _window.TextOffset.Y * 2)
            {
                // 候補まで入りきらなければ作業領域の上端に寄せる (打った行は見え、候補の下のほうは検索の画面の裏に入る)
                Diagnostics.Log.Info($"Windows の検索の画面: {search} (変換ボックスはその上に出す)");
                _beside = BesideSearch.Above;
                _window.ShowView(view, new Point(search.Left, search.Top - 8), above: true, belowY: screen.Top);
            }
            else
            {
                Diagnostics.Log.Info($"Windows の検索の画面: {search} (変換ボックスはその右に出す)");
                _window.ShowView(view, new Point(search.Right + 8, (caret?.Bottom ?? search.Top) - 4));
            }
            return;
        }
        // 入力位置に重ねる: 変換ボックスの文字の行を、入力位置の行の高さの真ん中にそろえる。
        if (_settings().CompositionPlacement == Config.CompositionPlacement.Overlay && caret is { } at)
        {
            var offset = _window.TextOffset;
            // 1 行の入力欄の枠なら、その上下の真ん中。複数行の欄全体 (高すぎる) なら、上端の 1 行目。
            var middle = at.Height <= MaxLineHeight ? at.Height / 2 : offset.Y - 8;
            _window.ShowView(view, new Point(at.Left - offset.X, at.Top + middle - offset.Y), overlay: true);
            return;
        }
        // カーソルの上: 入力位置の上に出す (候補の一覧で入力欄や下の行を隠さない)。
        if (_settings().CompositionPlacement == Config.CompositionPlacement.AboveCaret && caret is { } line)
        {
            _window.ShowView(view, new Point(line.Left, line.Top - 4), above: true, belowY: FindAnchor(caret).Y);
            return;
        }
        _window.ShowView(view, FindAnchor(caret, caretSearched: true));
    }

    /// <summary>変換ボックスを Windows の検索の画面のどちら側に出しているか (検索の画面のときだけ)。</summary>
    private enum BesideSearch { None, Left, Above }
    private BesideSearch _beside;
    /// <summary>検索の画面の左に出しているときの、変換ボックスの右の端。</summary>
    private int _searchLeftEdge;

    /// <summary>その点のモニターの DPI と、システムの DPI の比。取れなければ 1。</summary>
    private static float MonitorScale(Point at)
    {
        try
        {
            var monitor = Native.MonitorFromPoint(new Native.POINT { X = at.X, Y = at.Y }, Native.MONITOR_DEFAULTTONEAREST);
            var system = Native.GetDpiForSystem();
            if (monitor == IntPtr.Zero || system == 0 || Native.GetDpiForMonitor(monitor, 0, out var dpi, out _) != 0 || dpi == 0) return 1F;
            return (float)dpi / system;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 1F;
        }
    }

    public void Hide()
    {
        _committedWhileVisible = "";
        _beside = BesideSearch.None;
        if (_window.Visible) _window.Hide();
    }

    // Windows の検索・スタートメニューの画面のプロセス (Windows 10 / 11)
    private static readonly HashSet<string> ShellSearchProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost", "SearchApp", "SearchUI", "StartMenuExperienceHost",
    };

    /// <summary>前面が Windows の検索・スタートメニューなら、その画面 (検索の欄のある窓) の四角形。それ以外は null。</summary>
    private static Rectangle? ShellSearchBounds()
    {
        var foreground = Native.GetForegroundWindow();
        if (!IsShellSearchWindow(foreground) || !Native.GetWindowRect(foreground, out var rect)) return null;
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static bool IsShellSearchWindow(IntPtr hwnd)
    {
        if (Native.GetWindowThreadProcessId(hwnd, out var processId) == 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return ShellSearchProcesses.Contains(process.ProcessName);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// キャレット (入力位置) の画面上の四角形。Windows のキャレット (メモ帳など) → UI Automation (Chrome・Discord など) の順に試す。
    /// </summary>
    private Rectangle? FindCaret()
    {
        var foreground = Native.GetForegroundWindow();
        var thread = Native.GetWindowThreadProcessId(foreground, out _);
        var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (Native.GetGUIThreadInfo(thread, ref info) && info.hwndCaret != IntPtr.Zero)
        {
            var top = new Native.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
            if (Native.ClientToScreen(info.hwndCaret, ref top) && (top.X != 0 || top.Y != 0))
            {
                return new Rectangle(top.X, top.Y, 1, Math.Max(1, info.rcCaret.Bottom - info.rcCaret.Top));
            }
        }
        return Focus.CaretBounds() is { Width: > 0, Height: > 0 and < 200 } bounds && (bounds.X != 0 || bounds.Y != 0) ? bounds : null;
    }

    /// <summary>
    /// 変換ボックスを入力位置の下に出すときの位置。キャレット → 入力欄の左下 → マウスカーソルの順に試す。
    /// caretSearched なら、キャレットは探し済み (caret が null なら取れなかった) として探し直さない (FindCaret は最大 60ms 待つ)。
    /// </summary>
    private Point FindAnchor(Rectangle? caret = null, bool caretSearched = false)
    {
        if ((caretSearched ? caret : caret ?? FindCaret()) is { } found) return new Point(found.Left, found.Bottom + 4);
        // 入力欄の四角形が潰れている・画面の外にある (Google ドキュメントの、文字を受け取るための見えない欄など) ときは、
        // その左下に出すと画面の端や関係ない所に出てしまう (issue #128)。マウスカーソルの近くに出す。
        if (Focus.Current.Bounds is { } bounds && bounds.Height is > 2 and < 120 && bounds.Width > 2 && IsOnScreen(bounds))
        {
            return new Point(bounds.Left, bounds.Bottom + 2);
        }
        // 複数行の欄 (Chrome の textarea など) は、空のときに入力位置が取れない。空の欄で打ち始めるのは左上なので、そこに出す
        // (マウスカーソルの近くに出すと、欄の右下や別のモニターなど、打っている所から離れた所に出ていた)。
        if (Focus.Current.Bounds is { } field && field.Height >= 120 && field.Width > 2 && IsOnScreen(field))
        {
            return new Point(field.Left, Math.Max(field.Top, Screen.FromRectangle(field).WorkingArea.Top) + 2);
        }
        Native.GetCursorPos(out var cursor);
        return new Point(cursor.X + 12, cursor.Y + 20);
    }

    private static bool IsOnScreen(Rectangle bounds) => Screen.AllScreens.Any(s => s.Bounds.IntersectsWith(bounds));

    /// <summary>仮想キーの打鍵 (確定し直しの BackSpace)。KeyInjector と同じく、Meltype が送ったキーの印を付ける。</summary>
    private static Native.INPUT KeyInput(int vk, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = up ? Native.KEYEVENTF_KEYUP : 0,
                dwExtraInfo = KeyboardMonitor.InjectedMarker,
            },
        },
    };

    /// <summary>文字列を 1 文字ずつ (押す・離す) の打鍵にして足す。</summary>
    private static void AddUnicodeText(List<Native.INPUT> inputs, string text)
    {
        foreach (var c in text)
        {
            inputs.Add(UnicodeInput(c, up: false));
            inputs.Add(UnicodeInput(c, up: true));
        }
    }

    private static Native.INPUT UnicodeInput(char c, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0),
                dwExtraInfo = KeyboardMonitor.InjectedMarker,
            },
        },
    };

    public void Dispose()
    {
        // 変換エンジン (Mozc・Zenzai) は作った MeltypeRuntime が止める
        Focus.Dispose();
        _window.Dispose();
        _indicator.Dispose();
    }
}
