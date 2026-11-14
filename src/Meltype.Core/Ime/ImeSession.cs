// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Ime;

/// <summary>
/// 1 つの入力欄。入力モード (日本語 / 英数・コードの行) とモードを切り替えるキーの意味は、どの OS のフロントでもここで決める。
///   設定「入力モード切り替えキー」: 日本語 ⇔ 英数。入力中は変換ボックスの英字表示の切り替え、変換中は変換ボックスのキー (Shift+Space は「前の候補へ」)
///   半角/全角 (切り替えキーに無いとき): 何もしない (アプリに通すと PowerShell (PSReadLine) が @ として入れる)
///   英数: 英数にする (変換中なら確定してから)
///   かな (ひらがな/カタカナ): 日本語にする
///   変換・無変換: 変換していないときに日本語 / 英数にする。日本語で文字を選んでいれば、変換は選んだ文字の再変換。親指シフトの配列では親指キー
///   左右の Alt の単独押し: 設定「左 Alt で英数 / 右 Alt で日本語」が ON なら、英数 / 日本語にする
///   かな・変換・無変換は、設定「入力中のキーの役割を分ける」が ON で変換中なら、変換ボックスで使う
/// 日本語 ⇔ 英数の切り替えは、表示しているモードを入れ替える。コードの行 (英数のまま通る所) で日本語にすると、その行を日本語にする。
/// 入力欄の事実 (アプリ・性質) は、出来事に付いてきた最後のものを使う。
/// </summary>
internal sealed class ImeSession
{
    private readonly IImeEnvironment _environment;
    private readonly Func<Settings> _source;
    private readonly MeltypeSession _session;
    private ImeField? _field;
    private Settings _settings;
    // 直前に入れた定型文 (すぐに Esc を押したら、打った ;名前 に戻す)
    private (string Text, string Typed)? _lastSnippet;

    public ImeSession(IImeEnvironment environment, Func<Settings> settings)
    {
        _environment = environment;
        _source = settings;
        _settings = settings();
        _session = environment.CreateSession(() => _settings);
        _session.ClipboardText = () => environment.ClipboardText;
    }

    /// <summary>コードの入力欄でないときに、判定の文脈として使うキャレットの前の文字数。</summary>
    private const int ContextLength = 20;

    public bool IsComposing => _session.IsComposing;

    public void Close() => _session.CommitPending();

    /// <summary>
    /// この入力欄で入力を受け持つか。設定「動作モード」がこのフロントの入力方式で、一時停止中・アプリ別設定で OFF・ゲーム (Meltype キーボード)・パスワード欄・全画面 (設定)・
    /// 日本語以外の入力言語 (設定)・フロントが文字を送れない所・入力欄でない所 (アプリ別設定で「入力欄とみなす」アプリとターミナルを除く) でない。
    /// </summary>
    public static bool IsActive(IImeEnvironment environment, Settings settings, ImeField? field)
    {
        var traits = field?.Traits ?? [];
        var app = field?.App;
        return (environment.InputMethod is not { } method || settings.Mode == method) && settings.Enabled &&
            !traits.Contains(ImeTrait.Password) && !traits.Contains(ImeTrait.Unreachable) &&
            !(settings.ExcludeFullscreen && traits.Contains(ImeTrait.Fullscreen)) &&
            !(settings.JapaneseKeyboardOnly && traits.Contains(ImeTrait.OtherLanguage)) &&
            !(traits.Contains(ImeTrait.NonText) && !settings.TreatsAsTextInput(app) && !(app is not null && LineContext.TerminalProcesses.Contains(app))) &&
            settings.IsAppEnabled(app) && !(environment.InputMethod == InputMode.Keyboard && IsGame(settings, field));
    }

    /// <summary>
    /// ゲームか。Meltype キーボードはゲームでは止めて、チャットは Windows の IME で打ってもらう。
    /// Meltype IME・macOS・Linux のフロントは OS の IME そのものなので、止めるとチャットで日本語を打てなくなる (#1805)。
    /// 受け持ったまま英数で始め、英数ではキーを横取りしない (自動判定もしない) ので、操作のキーはそのままゲームに届く。
    /// </summary>
    public static bool IsGame(Settings settings, ImeField? field) =>
        settings.IsGame(field?.App, field?.Traits.Contains(ImeTrait.Game) == true);

    public ImeOutput Handle(ImeEvent e, long rev)
    {
        _field = e.Field ?? _field;
        var all = _source();
        var settings = _settings = all.ForApp(_field?.App);
        var active = IsActive(_environment, all, _field);
        var lastSnippet = _lastSnippet;
        if (e.Type == ImeEventType.KeyPressed || e.Caret?.Moved == true) _lastSnippet = null;
        NoteAltInterruption(e);
        ApplyFacts(e, settings);
        // ゲームに入ったら英数にする (IsGame)
        var game = IsGame(all, _field);
        if (game && !_game) _session.Direct = true;
        _game = game;
        var before = LineBefore(e.Field);
        var wasJapanese = ShowsJapanese(before);
        var explicitChange = false;
        var composing = _session.IsComposing;
        var result = WithPaste(Verify(e.Type switch
        {
            ImeEventType.KeyPressed when e.Key is { } key => active ? HandleKey(key, e.Field, before, settings, lastSnippet, ref explicitChange) : Leave(),
            ImeEventType.KeyReleased when active && e.Key is { Vk: VirtualKeys.LMenu or VirtualKeys.RMenu } alt => AltReleased(alt, before, settings, ref explicitChange),
            ImeEventType.KeyReleased when active && e.Key is { } released =>
                _session.HandleKeyUp(ToggleShortcut.NormalizeHankakuKey(released.Vk), before, e.Field?.After),
            ImeEventType.ModeToggleRequested when active => Switch(!wasJapanese, before, ref explicitChange, toggle: true),
            ImeEventType.JapaneseModeSelected when active => Switch(true, before, ref explicitChange, toggle: false),
            ImeEventType.AlphanumericModeSelected when active => Switch(false, before, ref explicitChange, toggle: false),
            ImeEventType.CommitRequested => _session.CommitPending(),
            ImeEventType.CommittedByApp => CommittedOutside(e.Field),
            // 番号は前の応答の一覧 (変換の候補か予測変換の候補か) で読む
            ImeEventType.CandidateClicked when _listedPredictions => _session.SelectPrediction(e.Index),
            ImeEventType.CandidateHighlighted when _listedPredictions => _session.Unchanged(),
            ImeEventType.CandidateClicked or ImeEventType.CandidateHighlighted => _session.SelectCandidate(e.Index),
            ImeEventType.ConversionFinished when active => _session.RefreshConversion() ?? _session.Unchanged(),
            // view が null だと、フロントは変換中の表示を消す。表示を変えない出来事は今の表示を返す
            _ => _session.Unchanged(),
        }, e.Field), settings);
        // ゲームでは、Enter で確定・Esc で取り消したら英数に戻す。ゲーム (Minecraft 26.2 までの GLFW など) はチャットを閉じたことを知らせてこないので、
        // 日本語のままだと閉じたあとの移動のキー (WASD) が変換中の文字になる。チャットを送る・閉じる前の Enter・Esc を区切りにする
        if (game && composing && !_session.IsComposing && e.Key?.Vk is VirtualKeys.Return or VirtualKeys.Escape) _session.Direct = true;
        TrackLine(e, result);
        _snippetKeys = SnippetKeys(e, result, settings);
        // キーでアプリの文字が変わったあとの位置は、フロントが次の出来事 (caret) で知らせる
        var japanese = ShowsJapanese(before);
        _listedPredictions = result.View is { } shown && SessionResult.ListsPredictions(shown) == true;
        var indicate = explicitChange && japanese != wasJapanese && settings.EffectiveModeIndicator != ModeIndicatorTiming.Never ||
            AnnouncesFocus(e) && settings.EffectiveModeIndicator == ModeIndicatorTiming.OnSwitchAndFocus;
        return new(new ImeState
        {
            HandlesInput = active,
            ImeOpen = !_session.Direct,
            LanguageMode = active && japanese ? ImeLanguageMode.Japanese : ImeLanguageMode.Alphanumeric,
            ModeIndicatorMs = active && indicate && result.View is null ? ModeIndicatorMs : 0,
            Capture = Capture(active, _session.Direct, _session.DirectEnglishWord, settings, game, _session.ComposingControlKeys(), _snippetKeys),
            Revision = rev,
        }, result);
    }

    // 前の出来事の入力欄がゲームだった
    private bool _game;

    // ---- 左右の Alt の単独押し (設定「左 Alt で英数 / 右 Alt で日本語」) ----

    // 押している左右の Alt と、単独押しにしない理由 (null なら、まだ単独押しの途中)
    private int _altDown;
    private string? _altNotAlone;

    private void AltPressed(ImeKey key, int vk)
    {
        if (key.Repeat && _altDown == vk) return;
        _altNotAlone = _altDown != 0 && _altDown != vk ? "左右の Alt を両方押した"
            : key.Shift || key.Ctrl || key.Command ? "Shift・Ctrl・Windows キーと一緒に押した"
            : null;
        _altDown = vk;
    }

    /// <summary>Alt を押している間のほかの出来事 (アプリに通したキー・クリック・フォーカスの移動など) があれば、単独押しにしない。</summary>
    private void NoteAltInterruption(ImeEvent e)
    {
        if (_altDown == 0 || _altNotAlone is not null || e.Key?.Vk is VirtualKeys.LMenu or VirtualKeys.RMenu ||
            e.Type is ImeEventType.StateRequested or ImeEventType.ConversionFinished) return;
        _altNotAlone = $"押している間に {e.Type} があった";
    }

    private SessionResult AltReleased(ImeKey key, string? before, Settings settings, ref bool explicitChange)
    {
        var vk = key.Vk;
        var notAlone = !settings.AltKeysSwitchKeyboard ? "設定が OFF"
            : _altDown != vk ? "押したことを受け取っていない"
            : _altNotAlone ?? (key.Interrupted ? "押している間にほかのキー・マウスのボタンを押した" : null);
        _altDown = 0;
        _altNotAlone = null;
        var side = vk == VirtualKeys.RMenu ? "右" : "左";
        if (notAlone is not null)
        {
            Diagnostics.Log.Decision($"{side} Alt: 単独押しにしません ({notAlone})");
            return _session.PassThrough();
        }
        Diagnostics.Log.Decision($"{side} Alt の単独押しで{(vk == VirtualKeys.RMenu ? "日本語" : "英数")}にします");
        return Switch(vk == VirtualKeys.RMenu, before, ref explicitChange, toggle: false);
    }

    // 次に受け取る、定型文のキー (SnippetKeys)
    private List<int> _snippetKeys = [];

    /// <summary>
    /// 何も打っていないときにふつうはアプリに通すが、定型文に使うので受け取るキー: キャレットの前が登録した ;名前 で終わっていたら Space・Tab、
    /// 定型文を入れた直後なら Esc。キャレットの前は、この出来事の入力欄に、アプリに通させた文字を足したもの。
    /// </summary>
    private List<int> SnippetKeys(ImeEvent e, SessionResult result, Settings settings)
    {
        if (_session.IsComposing) return [];
        var before = Before(e.Field);
        if (e.Type == ImeEventType.KeyPressed && !result.Consumed && e.Key is { Ch: > 0 and < 0x10000 } key) before += (char)key.Ch;
        var keys = new List<int>();
        if (SnippetUsable(before, settings) && SnippetTrigger.TypedBefore(settings.SnippetMark, before) is { } typed && _environment.Snippets.Get(typed[1..]) is not null)
            keys.AddRange([VirtualKeys.Space, VirtualKeys.Tab]);
        if (_lastSnippet is not null) keys.Add(VirtualKeys.Escape);
        return keys;
    }

    /// <summary>
    /// 入力欄に入ったことを、入力モードの表示で知らせるか。OS のシェルの画面 (GNOME のアプリ一覧の検索欄など) は、
    /// 開くアニメーションの途中のキャレットの位置しか知らせてこないので、入ったときは出さない。
    /// </summary>
    public static bool AnnouncesFocus(ImeEvent e) => e.Caret?.FocusEntered == true && !ShellApps.Contains(e.Field?.App ?? "");

    private static readonly HashSet<string> ShellApps = new(StringComparer.Ordinal) { "gnome-shell" };

    /// <summary>入力モードを表示する時間。</summary>
    private const int ModeIndicatorMs = 1200;

    // 前の応答の一覧が予測変換の候補だった (select の番号を読むため)
    private bool _listedPredictions;

    /// <summary>入力の本体を作る前 (まだ打っていない入力欄) の状態。</summary>
    /// <param name="entered">入力欄に入ったことを知らせる (<see cref="AnnouncesFocus"/>。設定によっては入力モードを表示する)。</param>
    /// <param name="game">ゲーム (<see cref="IsGame"/>)。英数で始める。</param>
    public static ImeState InitialState(bool active, bool open, Settings settings, bool game, bool entered, long rev)
    {
        open &= !game;
        return new()
        {
            HandlesInput = active,
            ImeOpen = open,
            LanguageMode = active && open ? ImeLanguageMode.Japanese : ImeLanguageMode.Alphanumeric,
            ModeIndicatorMs = active && entered && settings.EffectiveModeIndicator == ModeIndicatorTiming.OnSwitchAndFocus ? ModeIndicatorMs : 0,
            Capture = Capture(active, !open, false, settings, game, [], []),
            Revision = rev,
        };
    }

    private void ApplyFacts(ImeEvent e, Settings settings)
    {
        // OS の IME の開閉が変わった (アプリ・Windows が変えた・ほかのフロントの操作): 本体のモードを合わせる
        if (e.ImeOpen is { } open && _session.Direct == open)
        {
            _session.Direct = !open;
            _session.ForgetDirectInput();
        }
        if (e.Caret is { } caret)
        {
            if (caret.Moved)
            {
                _session.ForgetLastCommit();
                _session.ForgetDirectInput();
            }
            if (caret.PassedKey is { } vk) _session.CaretMoved(vk, caret.Ctrl);
            // 入力欄を出入りした (入った知らせだけで、出た知らせが来ないこともある)
            if (caret.FocusLeft || caret.FocusEntered) _session.ForgetCodeLine();
        }
        if (e.Field is not { } field) return;
        _session.Private = field.Traits.Contains(ImeTrait.Private);
        _session.CanDeleteSurrounding = field.CanRemove;
        _session.Selection = field.Selection;
        if (CodeFocusOf(field, settings) is { } focus)
        {
            var code = focus != CodeFocus.None;
            if (_session.CodeInput != code) _session.CodeInput = code;
            _session.CodeTerminal = focus == CodeFocus.Terminal;
        }
        // コードの入力欄でキャレットの前を読めないと、コードの行として英数にする。読めない入力欄を調べられるように、変わったときだけ書く
        var unreadable = _session.CodeInput && field.Before is null;
        if (unreadable != _unreadableCode)
        {
            _unreadableCode = unreadable;
            Diagnostics.Log.Decision(unreadable
                ? $"コードの入力欄で、キャレットの前の文字が届かないので、コードの行として英数にします (アプリ: {field.App ?? "不明"})"
                : "コードの入力欄で、キャレットの前の文字が届くようになりました");
        }
    }

    private bool _unreadableCode;

    /// <summary>
    /// フォーカスのある所の種類。OS がターミナルと知らせてきたか、アプリの種類が「コード」なら、ターミナル・エディターと要素の名前から決める。
    /// README.md などの文章のファイルを開いているなら一般。打つと開く補完の一覧 (VS Code など) に移っただけなら null (今までのまま)。
    /// </summary>
    private static CodeFocus? CodeFocusOf(ImeField field, Settings settings)
    {
        if (field.Traits.Contains(ImeTrait.Terminal)) return CodeFocus.Terminal;
        if (field.App is not { } app || settings.ProfileFor(app) != AppProfile.Code || field.Title is { } title && LineContext.IsDocumentTitle(title)) return CodeFocus.None;
        // UI Automation のフォーカスは一覧の行に移るが、打った文字はエディターに入る
        if (field.Element?.ClassName.Contains("monaco-list-row", StringComparison.Ordinal) == true) return null;
        return LineContext.ClassifyFocus(app, field.Element);
    }

    private string? Before(ImeField? field) =>
        field?.Before is { Length: > ContextLength } before && !_session.CodeInput ? before[^ContextLength..] : field?.Before;

    // ==== ワークアラウンド: ターミナルがキャレットの前の文字を渡さないことへの対処 (#1687) ====
    // 本体は判断を事実だけでするのが原則で、打ったキーから入力欄の中身を推測するのはこの 1 か所だけの例外。
    // 渡さないのはターミナル側の事情 (Windows Terminal の TSF は文書を持たないので空、iTerm2 は日本語を出したあと読めない)。
    // Meltype IME の要求の中で UI Automation で読み直す方法は、待ち合いになるので採らなかった (Windows Terminal の UI Automation は
    // XAML の automation peer で、アプリの UI スレッドで答える。そのスレッドは DLL の中で本体の応答を待って止まっている)。
    // ターミナルが渡すようになったら、_line・LineBefore・TrackLine・TypesText はまとめて消す。状態を足したり、ほかの判断に使い回したりしない。

    /// <summary>
    /// コードの行の判断と変換の文脈に使うキャレットの前。コードの入力欄でキャレットの前が届かない (ターミナルで空のときも) ときだけ、
    /// <see cref="_line"/> を使う (ワークアラウンド)。定型文・確定し直しは本物の文字が要るので、<see cref="Before"/> を使う。
    /// </summary>
    private string? LineBefore(ImeField? field) =>
        _session.CodeInput && field is { } f && (f.Before is null || f.Before.Length == 0 && _session.CodeTerminal) ? _line.ToString() : Before(field);

    // ワークアラウンド: この入力欄の今の行のうち、本体が知っている文字 (通したキーの文字と確定した文字)。
    // キャレットが打つ以外で動いたら、空 (作ったときと同じ状態) に戻す
    private readonly StringBuilder _line = new();

    private void TrackLine(ImeEvent e, SessionResult result)
    {
        if (e.Caret is { } caret && (caret.FocusEntered || caret.FocusLeft || caret.Moved && !TypesText(caret.PassedKey))) _line.Clear();
        foreach (var edit in result.Commits)
        {
            _line.Length = Math.Max(0, _line.Length - edit.DeleteBefore);
            AppendToLine(edit.Text);
        }
        if (e.Type != ImeEventType.KeyPressed || e.Key is not { } key || result.Consumed) return;
        var vk = key.Vk != 0 ? ToggleShortcut.NormalizeHankakuKey(key.Vk) : VirtualKeyOf(key.Ch);
        if (key.Ctrl || key.Alt || key.Command) _line.Clear();
        else if (key.Text is { Length: > 0 } text) AppendToLine(text);
        else if (key.Ch is >= 0x20 and < 0x10000 && !char.IsControl((char)key.Ch)) AppendToLine(((char)key.Ch).ToString());
        else if (vk == VirtualKeys.Back) _line.Length = Math.Max(0, _line.Length - 1);
        else if (!VirtualKeys.IsModifier(vk) && !IsLockKey(vk)) _line.Clear();
    }

    private void AppendToLine(string text)
    {
        if (text.LastIndexOf('\n') is var newline and >= 0)
        {
            _line.Clear();
            text = text[(newline + 1)..];
        }
        _line.Append(text);
        if (_line.Length > MaxLine) _line.Remove(0, _line.Length - MaxLine);
    }

    private const int MaxLine = 4000;

    /// <summary>アプリに通すと文字が入るキーか (キャレットが動いても、打った文字の後ろに進んだだけ)。null は文字が変わっただけ。</summary>
    private static bool TypesText(int? vk) =>
        vk is null or VirtualKeys.Back or VirtualKeys.Space or >= 0x30 and <= 0x5A or >= 0x60 and <= 0x6F or >= 0xBA and <= 0xE2;

    private SessionResult HandleKey(ImeKey key, ImeField? field, string? before, Settings settings, (string Text, string Typed)? lastSnippet,
        ref bool explicitChange)
    {
        var composing = _session.IsComposing;
        var modifiers = Modifiers(key);
        var plain = modifiers == ShortcutModifiers.None;
        var vk = key.Vk != 0 ? ToggleShortcut.NormalizeHankakuKey(key.Vk) : VirtualKeyOf(key.Ch);
        // 左右の Alt の押下は、単独押しかを見分けるためだけに受け取る (変換中の文字はそのまま。アプリに通す)
        if (vk is VirtualKeys.LMenu or VirtualKeys.RMenu)
        {
            AltPressed(key, vk);
            return _session.PassThrough();
        }
        var toggle = settings.EffectiveToggleShortcuts.Any(shortcut => shortcut.Matches(vk, modifiers));
        // 押しっぱなしで、入力モードを何度も切り替えない
        if (key.Repeat && (toggle || plain && vk is ToggleShortcut.Hankaku or VirtualKeys.OemAttn or VirtualKeys.Kana)) return Consumed;
        if (toggle && !composing) return Switch(!ShowsJapanese(before), before, ref explicitChange, toggle: true);
        // 入力中は変換ボックスの英字表示の切り替え。変換中は変換ボックスのキーとして使う (Shift+Space は「前の候補へ」)
        if (toggle && !_session.IsConverting) return _session.HandleKey(VirtualKeys.Kanji, null, false, false, false, false, before, field?.After);
        // ロックのキーは文字を入れないので、打ちかけに触らずにアプリに通す (切り替えキーに登録したものは、上で切り替えに使った)
        if (IsLockKey(vk)) return _session.PassThrough();
        if (vk == ToggleShortcut.Hankaku && !key.Ctrl && !key.Alt && !composing) return Consumed;
        if (vk == VirtualKeys.OemAttn && plain) return Switch(false, before, ref explicitChange, toggle: false);
        if (vk is VirtualKeys.Kana or VirtualKeys.Convert or VirtualKeys.NonConvert && plain)
        {
            if (_session.HandleRoleKey(vk) is { } role) return role;
            // 入力中の無変換は、英数キーと同じく確定してから英数にする。入力中の変換・かなは変換ボックスのキー
            var switches = composing ? vk == VirtualKeys.NonConvert : !(vk == VirtualKeys.Convert && ShowsJapanese(before) && field?.Selection is { Length: > 0 });
            if (switches && !IsThumbKey(vk, settings))
            {
                return key.Repeat ? Consumed : Switch(vk != VirtualKeys.NonConvert, before, ref explicitChange, toggle: false);
            }
        }
        if (!composing && !key.Ctrl && !key.Alt && !key.Command && Snippet(vk, key, Before(field), settings, lastSnippet) is { } snippet) return snippet;
        // 複数のスカラーの文字 (アプリが元のイベントで入れる): 先頭が結合文字なら直前の文字につなげて確定する
        if (key.Text is { Length: > 0 } text && Rune.DecodeFromUtf16(text, out var first, out _) == System.Buffers.OperationStatus.Done)
        {
            return Rune.GetUnicodeCategory(first) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                ? _session.CommitCombiningForPassThrough(preserveLatinRaw: first.Value is >= 0x0300 and <= 0x036F)
                : _session.CommitForPassThrough();
        }
        // 不正な Unicode (負数・単独サロゲート・上限超過) は、状態を変えずにアプリへそのまま渡す
        if (key.Ch != 0 && !Rune.IsValid(key.Ch)) return _session.PassThrough();
        // 結合文字は、未確定の表示に取り込まず、直前の内容を確定してからアプリに通す。
        // U+0300..036F の Latin アクセントのみ ASCII 英字の原文を優先する (濁点・異体字セレクターでは、日本語の表示や選んだ候補をそのまま確定する)
        if (key.Ch != 0 && Rune.GetUnicodeCategory(new Rune(key.Ch)) is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            return _session.CommitCombiningForPassThrough(preserveLatinRaw: key.Ch is >= 0x0300 and <= 0x036F);
        // 補助面の文字 (絵文字など) は char に入らないので、未確定の内容を確定してからアプリに通す
        if (key.Ch >= 0x10000) return _session.CommitForPassThrough();
        var result = _session.HandleKey(vk, key.Ch > 0 ? (char)key.Ch : null, key.Shift, key.Ctrl, key.Alt, key.Command, before, field?.After, key.ReleaseFollows);
        // アプリに通したキーはキャレットを動かすかもしれない (Core が通すと決めたので、フロントからは送られてこない)
        if (!result.Consumed) _session.CaretMoved(vk, key.Ctrl);
        return result;
    }

    /// <summary>CapsLock・NumLock・ScrollLock。</summary>
    private static bool IsLockKey(int vk) => vk is 0x14 or 0x90 or 0x91;

    /// <summary>親指シフトのかな配列 (NICOLA・中指NICOLA・TRON) の親指キー (無変換・変換)。変換ボックスで受け取り、文字に使わずに離したらもとの働きをする。</summary>
    private bool IsThumbKey(int vk, Settings settings) =>
        settings.InputStyle == InputStyle.Kana && !_session.Direct && vk is VirtualKeys.Convert or VirtualKeys.NonConvert &&
        settings.KanaLayout is KanaLayout.Nicola or KanaLayout.NakayubiNicola or KanaLayout.Tron;

    /// <summary>
    /// 定型文 (issue #290)。;名前 の文字はそのままアプリへ渡し、続く Space / Tab で登録した文に置き換える。入れた直後の Esc で、打った ;名前 に戻す。
    /// キャレットの前の文字が分からない・確定した文字を消せない入力欄では使わない。
    /// </summary>
    private SessionResult? Snippet(int vk, ImeKey key, string? before, Settings settings, (string Text, string Typed)? last)
    {
        if (!SnippetUsable(before, settings)) return null;
        if (vk == VirtualKeys.Escape && last is { } undo)
            return new SessionResult(true, [new TextEdit(undo.Text.Length, undo.Typed, undo.Text)], null);
        if (vk is VirtualKeys.Space or VirtualKeys.Tab && !key.Shift && SnippetTrigger.TypedBefore(settings.SnippetMark, before) is { } typed &&
            _environment.Snippets.Get(typed[1..]) is { } text)
        {
            _lastSnippet = (text, typed);
            return new SessionResult(true, [new TextEdit(typed.Length, text, typed)], null);
        }
        return key.Ch is > 0 and < 0x10000 && SnippetTrigger.PassesThrough(settings.SnippetMark, before, (char)key.Ch) ? _session.PassThrough() : null;
    }

    private bool SnippetUsable([NotNullWhen(true)] string? before, Settings settings) =>
        before is not null && _session.CanDeleteSurrounding && settings.SnippetMark is [_] && settings.InputStyle != InputStyle.Kana && _environment.Snippets.Count > 0;

    /// <summary>文字を入力するキーの仮想キーコード。特別な扱いのない文字 (記号など) は 0x07 (本体は文字だけを見る)。</summary>
    private static int VirtualKeyOf(int ch) => ch switch
    {
        >= 'a' and <= 'z' => ch - 0x20,
        >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' => ch,
        ',' => VirtualKeys.OemComma,
        '.' => VirtualKeys.OemPeriod,
        '-' => VirtualKeys.OemMinus,
        '/' => VirtualKeys.Oem2,
        '[' => 0xDB,
        ']' => 0xDD,
        _ => 0x07,
    };

    /// <summary>
    /// 確定し直し (DeleteBefore) を、フロントが渡したキャレットの前の文字と照合する。消す文字 (Expect) がキャレットの前に無い・
    /// キャレットの前が分からない・消せない入力欄・変換中の文字の外を書き換える (今の語が無い) ときは、前の文字を消さずに今の語 (Current。無ければ Text) だけを入れる。
    /// </summary>
    private SessionResult Verify(SessionResult result, ImeField? field)
    {
        if (!result.Commits.Any(edit => edit.DeleteBefore > 0)) return result;
        var before = field?.Before;
        var edits = new List<TextEdit>(result.Commits.Count);
        foreach (var edit in result.Commits)
        {
            var verified = edit.DeleteBefore == 0 ||
                before is not null && edit.Expect is { } expect && expect.Length == edit.DeleteBefore && before.EndsWith(expect, StringComparison.Ordinal) &&
                _session.CanDeleteSurrounding && !(field?.Composing == true && edit.Current is null);
            var applied = verified ? edit : edit with { DeleteBefore = 0, Text = edit.Current ?? edit.Text, Expect = null, Current = null };
            if (!verified) Diagnostics.Log.Decision("確定し直す文字がキャレットの前に無いので、前の文字を消しません");
            before = before is null ? null : before[..^applied.DeleteBefore] + applied.Text;
            edits.Add(applied);
        }
        return result with { Commits = edits };
    }

    /// <summary>
    /// キーを送って文字を入れるフロント (Meltype キーボード) が貼り付けで入れる確定: アプリ別設定の「確定した文字の入れ方」(Qt のアプリは既定で貼り付け) と、
    /// 改行を含む文 (改行を Enter として送ると、チャットでは途中で送信される)。
    /// </summary>
    private SessionResult WithPaste(SessionResult result, Settings settings)
    {
        if (result.Commits.Count == 0) return result;
        var paste = PastePolicy.ShouldPaste(settings, _field?.App, PastePolicy.IsQtWindowClass(_field?.WindowClass));
        return result with { Commits = result.Commits.Select(edit => edit with { Paste = paste || edit.Text.Contains('\n') }).ToList() };
    }

    /// <summary>OS が変換中の文字を表示のまま確定した: 本体も確定したことにする。文字はもう入っているので、確定し直しも入れない。</summary>
    private SessionResult CommittedOutside(ImeField? field)
    {
        _session.CanDeleteSurrounding = false;
        _session.CommitPending();
        _session.CanDeleteSurrounding = field?.CanRemove ?? true;
        return Consumed;
    }

    /// <summary>
    /// 表示するモードを japanese にする。変換中なら確定してから。日本語にするときは、コードの行もその行だけ日本語にする。
    /// 英数にするとき、toggle (日本語 ⇔ 英数の切り替え) は、日本語にしていたコードの行を英数に戻すだけで IME を開いたままにする。
    /// そうでなければ (英数キー・無変換) 直接入力にする。
    /// </summary>
    private SessionResult Switch(bool japanese, string? before, ref bool explicitChange, bool toggle)
    {
        explicitChange = true;
        var result = _session.IsComposing ? _session.CommitPending() : Consumed;
        if (japanese)
        {
            _session.Direct = false;
            if (_session.CodeEnglishAt(before)) _session.SetCodeLine(true, before);
        }
        else
        {
            var codeLineReset = _session.SetCodeLine(false, before);
            if (!toggle || !codeLineReset) _session.Direct = true;
        }
        return result;
    }

    /// <summary>受け持たなくなった (一時停止・動作モードの変更): 変換中の内容は確定して、キーはアプリに通す。</summary>
    private SessionResult Leave() => _session.IsComposing ? _session.CommitPending() with { Consumed = false } : Pass;

    private bool ShowsJapanese(string? before) => !_session.Direct && !_session.CodeEnglishAt(before);

    private static ShortcutModifiers Modifiers(ImeKey key) =>
        (key.Shift ? ShortcutModifiers.Shift : 0) | (key.Ctrl ? ShortcutModifiers.Control : 0) |
        (key.Alt ? ShortcutModifiers.Alt : 0) | (key.Command ? ShortcutModifiers.Win : 0);

    private static ImeCapture Capture(bool active, bool direct, bool directEnglishWord, Settings settings, bool game, IEnumerable<int> composingControlKeys,
        IReadOnlyList<int> snippetKeys)
    {
        if (!active) return new ImeCapture();
        // 修飾キー単独の割り当ては使えない (設定の読み込みで外す)。左右の Alt の単独押しは AltKeys
        var keys = settings.EffectiveToggleShortcuts.Where(shortcut => !ToggleShortcut.IsModifier(shortcut.Key))
            .Select(shortcut => new ImeShortcut(shortcut.Key, (int)shortcut.Modifiers))
            .Concat(ModeKeys.Select(key => new ImeShortcut(key, 0)))
            .ToList();
        // 英数でもローマ字を見分ける (英語の語を打っている途中は、続きの英字をそのまま通す)
        // ゲームの英数では判定しない: 英字を受け取って送り直すと、移動のキーが一回目に反応しない・離しても押したままになる (#1447)
        var detects = settings.DirectModeAutoDetect && settings.AutoDetection && !game;
        if (!direct || detects) keys.Add(new ImeShortcut(VirtualKeys.Space, (int)ShortcutModifiers.Shift));
        if (!direct && settings.SpaceWidth == SpaceWidth.FullWidth) keys.Add(new ImeShortcut(VirtualKeys.Space, 0));
        keys.AddRange(snippetKeys.Select(key => new ImeShortcut(key, 0)));
        return new ImeCapture
        {
            PrintableAscii = !direct || detects,
            PrintableAsciiExcept = direct && directEnglishWord ? EnglishWordCharacters : "",
            Shortcuts = keys.Distinct().ToList(),
            CtrlKeysWhileComposing = composingControlKeys.Select(key => new ImeShortcut(key, (int)ShortcutModifiers.Control)).ToList(),
            KeyReleases = settings.InputStyle == InputStyle.Kana && settings.KanaLayout is KanaLayout.Nicola or KanaLayout.NakayubiNicola or KanaLayout.Tron,
            AltKeys = settings.AltKeysSwitchKeyboard,
        };
    }

    /// <summary>英語の語の続きとして、英数のままアプリに通す文字。</summary>
    private const string EnglishWordCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789@_";

    /// <summary>設定によらず受け取る、モードを切り替えるキー (半角/全角 の 3 つのキーコード・英数・かな・変換・無変換)。</summary>
    private static readonly int[] ModeKeys =
        [ToggleShortcut.Hankaku, VirtualKeys.OemAuto, VirtualKeys.OemEnlw, VirtualKeys.OemAttn, VirtualKeys.Kana, VirtualKeys.Convert, VirtualKeys.NonConvert];

    private static SessionResult Pass => new(false, [], null);

    private static SessionResult Consumed => new(true, [], null);
}
