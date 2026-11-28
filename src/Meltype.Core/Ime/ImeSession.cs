// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Dictionary;
using Meltype.Input;
using Meltype.Learning;

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
internal static class ImeSession
{
    /// <summary>入力欄 1 つ分の状態を作る。</summary>
    public static ImeSessionState Create(ImeEnv env)
    {
        env.UserDictionary?.Refresh();
        var state = new ImeSessionState { UserDictionaryCheckedMs = env.TimeMs() };
        return state;
    }

    public static void Close(ImeSessionState state, ImeEnv env, ImeField? field = null)
    {
        field ??= state.Field;
        if (!IsComposing(state) || IsPassword(field)) return;
        state.Field = field;
        CompositionController.SetPrivate(state.Composition, ForbidsLearning(field));
        CommitPending(state, ForField(state, env));
    }

    /// <summary>
    /// パスワード欄か。
    /// ワークアラウンド: ターミナルの sudo・ssh は、パスワードを尋ねていることを OS に知らせない (エコーを止めるだけ) ので、
    /// キャレットの前の行がパスワードのプロンプトならパスワード欄とする (#1201)。
    /// </summary>
    private static bool IsPassword(ImeField? field) =>
        field is not null && (field.Traits.Contains(ImeTrait.Password) ||
            field.Before is { } before && IsTerminal(field) && LineContext.IsPasswordPrompt(before));

    /// <summary>
    /// ターミナルか。ImeTrait.Terminal は IBus・fcitx5・meltype-tty しか送らないので、Windows・macOS のためにアプリ・要素の名前でも見る。
    /// アプリ別設定の種類は見ない (種類を「一般」にしたターミナルでも、パスワードは日本語にしない)。
    /// </summary>
    private static bool IsTerminal(ImeField field) =>
        field.Traits.Contains(ImeTrait.Terminal) || field.App is { } app && LineContext.ClassifyFocus(app, field.Element) == CodeFocus.Terminal;

    private static bool ForbidsLearning(ImeField? field) => IsPassword(field) || field?.Traits.Contains(ImeTrait.Private) == true;

    /// <summary>
    /// この入力欄で入力を受け持つか。設定「動作モード」がこのフロントの入力方式で、一時停止中・アプリ別設定で OFF・ゲーム (Meltype キーボード)・パスワード欄・全画面 (設定)・
    /// 日本語以外の入力言語 (設定)・メールアドレスと数字の入力欄・フロントが文字を送れない所・入力欄でない所 (アプリ別設定で「入力欄とみなす」アプリとターミナルを除く) でない。
    /// </summary>
    public static bool IsActive(IImeEnvironment environment, Settings settings, ImeField? field)
    {
        var traits = field?.Traits ?? [];
        var app = field?.App;
        return (environment.InputMethod is not { } method || settings.Mode == method) && settings.Enabled &&
            !IsPassword(field) && !traits.Contains(ImeTrait.Email) && !traits.Contains(ImeTrait.Numeric) && !traits.Contains(ImeTrait.Unreachable) &&
            !(settings.ExcludeFullscreen && traits.Contains(ImeTrait.Fullscreen)) &&
            !(settings.JapaneseKeyboardOnly && traits.Contains(ImeTrait.OtherLanguage)) &&
            !(traits.Contains(ImeTrait.NonText) && !IsGameChat(settings, field) && !settings.TreatsAsTextInput(app) && !(app is not null && LineContext.TerminalProcesses.Contains(app))) &&
            settings.IsAppEnabled(app) && !(environment.InputMethod == InputMode.Keyboard && IsGame(settings, field));
    }

    /// <summary>
    /// ゲームで、チャット欄が開いていないか。Meltype キーボードはゲームでは止める (チャット欄が開いている間 (IsGameChat) だけ受け持つ)。
    /// Meltype IME・macOS・Linux のフロントは OS の IME そのものなので、止めるとチャットで日本語を打てなくなる (#1805)。
    /// 受け持ったまま英数で始め、英数ではキーを横取りしない (自動判定もしない) ので、操作のキーはそのままゲームに届く。
    /// </summary>
    public static bool IsGame(Settings settings, ImeField? field) => IsGameApp(settings, field) && !IsGameChat(settings, field);

    /// <summary>ゲームで、チャット欄が開いている (ImeTrait.ImeContext)。Meltype IME・macOS・Linux のフロントは ImeContext を送らないので、false。</summary>
    public static bool IsGameChat(Settings settings, ImeField? field) =>
        IsGameApp(settings, field) && field?.Traits.Contains(ImeTrait.ImeContext) == true;

    private static bool IsGameApp(Settings settings, ImeField? field) =>
        settings.IsGame(field?.App, field?.Traits.Contains(ImeTrait.Game) == true);

    public static ImeOutput Handle(ImeSessionState state, ImeEnv env, ImeEvent e, long rev)
    {
        var field = e.Field ?? state.Field;
        CompositionController.SetPrivate(state.Composition, ForbidsLearning(field));
        if (IsPassword(field))
        {
            if (!IsPassword(state.Field))
            {
                CancelPending(state);
                state.LastSnippet = null;
                state.ListedPredictions = false;
                state.AltDown = 0;
                state.AltNotAlone = null;
                state.UnreadableCode = false;
                state.Line.Clear();
                // 秘密欄の周辺文字列や選択範囲をセッションに残さない。
                state.Field = new ImeField { Traits = [ImeTrait.Password] };
            }
            if (e.ImeOpen is { } open) CompositionController.SetDirect(state.Composition, !open);
            return new(InitialState(false, !Direct(state), ForField(state, env).App, game: false, entered: false, rev), Pass);
        }
        state.Field = field;
        env = ForField(state, env);
        var all = env.Settings;
        var settings = env.App;
        var active = IsActive(env.Environment, all, state.Field);
        var lastSnippet = state.LastSnippet;
        if (e is ImeEvent.KeyPressed or ImeEvent.WithCaret { Caret.Moved: true }) state.LastSnippet = null;
        NoteAltInterruption(state, e);
        ApplyFacts(state, e, settings, env.Environment.InputMethod);
        // ゲームに入ったら英数にする (IsGame)
        var game = IsGame(all, state.Field);
        // チャット欄を開いたら日本語、閉じたら (上の行で) 英数にする
        if (game && !state.Game) CompositionController.SetDirect(state.Composition, true);
        else if (state.Game && IsGameChat(all, state.Field)) CompositionController.SetDirect(state.Composition, false);
        state.Game = game;
        var before = LineBefore(state, e.Field);
        var (fieldModes, fieldKey) = FieldModeOf(env, state.Field);
        if (e is ImeEvent.WithCaret { Caret.FocusEntered: true })
        {
            state.FreshFocus = true;
            // 前にこの入力欄で切り替えたモードにする
            if (active && !game && !IsComposing(state) && LearnedMode(env, state.Field) is { } learned && learned != ShowsJapanese(state, before, settings))
                Switch(state, env, learned, before, toggle: false);
        }
        var wasJapanese = ShowsJapanese(state, before, settings);
        var composing = IsComposing(state);
        // view が null だと、フロントは変換中の表示を消す。表示を変えない出来事は今の表示を返す
        var handled = e.Match(new ImeEvent.Cases<Handled>
        {
            KeyPressed = pressed => active ? HandleKey(state, env, pressed.Key, e.Field, before, settings, lastSnippet) : Leave(state, env),
            KeyReleased = released => !active ? Unchanged(state, env)
                : released.Key.Vk is VirtualKeys.LMenu or VirtualKeys.RMenu ? AltReleased(state, env, released.Key, before, settings)
                : HandleKeyUp(state, env, ToggleShortcut.NormalizeKey(released.Key.Vk), released.Key.Physical),
            ModeToggleRequested = _ => active ? Switch(state, env, !wasJapanese, before, toggle: true) : Unchanged(state, env),
            JapaneseModeSelected = _ => active ? Switch(state, env, true, before, toggle: false) : Unchanged(state, env),
            AlphanumericModeSelected = _ => active ? Switch(state, env, false, before, toggle: false) : Unchanged(state, env),
            CommitRequested = _ => CommitPending(state, env),
            CommittedByApp = _ => CommittedOutside(state, env),
            // 番号は前の応答の一覧 (変換の候補か予測変換の候補か) で読む
            CandidateClicked = clicked => state.ListedPredictions ? SelectPrediction(state, env, clicked.Index) : SelectCandidate(state, env, clicked.Index),
            CandidateHighlighted = highlighted => state.ListedPredictions ? Unchanged(state, env) : SelectCandidate(state, env, highlighted.Index),
            ConversionFinished = _ => active && RefreshConversion(state, env) is { } refreshed ? refreshed : Unchanged(state, env),
            CaretMoved = _ => Unchanged(state, env),
            StateRequested = _ => Unchanged(state, env),
            InstanceDestroyed = _ => Unchanged(state, env),
        });
        var result = WithPaste(state, Verify(state, handled.Result, e.Field), settings);
        // ゲームでは、Enter で確定・Esc で取り消したら英数に戻す。ゲーム (Minecraft 26.2 までの GLFW など) はチャットを閉じたことを知らせてこないので、
        // 日本語のままだと閉じたあとの移動のキー (WASD) が変換中の文字になる。チャットを送る・閉じる前の Enter・Esc を区切りにする
        if (game && composing && !IsComposing(state) && e is ImeEvent.KeyPressed { Key.Vk: VirtualKeys.Return or VirtualKeys.Escape }) CompositionController.SetDirect(state.Composition, true);
        TrackLine(state, e, result);
        RememberFieldMode(state, e, result, handled.ExplicitChange, wasJapanese, ShowsJapanese(state, before, settings), fieldModes, fieldKey);
        var snippetKeys = SnippetKeys(state, env, e, result, settings);
        // キーでアプリの文字が変わったあとの位置は、フロントが次の出来事 (caret) で知らせる
        var japanese = ShowsJapanese(state, before, settings);
        state.ListedPredictions = result.View is { } shown && SessionResult.ListsPredictions(shown) == true;
        var indicate = handled.ExplicitChange && japanese != wasJapanese && settings.EffectiveModeIndicator != ModeIndicatorTiming.Never ||
            AnnouncesFocus(e) && settings.EffectiveModeIndicator == ModeIndicatorTiming.OnSwitchAndFocus;
        return new(new ImeState
        {
            HandlesInput = active,
            ImeOpen = !Direct(state),
            LanguageMode = active && japanese ? ImeLanguageMode.Japanese : ImeLanguageMode.Alphanumeric,
            ModeIndicatorMs = active && indicate && result.View is null ? ModeIndicatorMs : 0,
            Capture = Capture(active, Direct(state), japanese, DirectEnglishWord(state), settings, game, ComposingControlKeys(state, env), snippetKeys),
            Revision = rev,
        }, result);
    }

    /// <param name="ExplicitChange">入力モードを明示的に切り替えた。</param>
    private readonly record struct Handled(SessionResult Result, bool ExplicitChange = false)
    {
        public static implicit operator Handled(SessionResult result) => new(result);
    }

    private static void AltPressed(ImeSessionState state, ImeKey key, int vk)
    {
        if (key.Repeat && state.AltDown == vk) return;
        state.AltNotAlone = state.AltDown != 0 && state.AltDown != vk ? "左右の Alt を両方押した"
            : key.Shift || key.Ctrl || key.Command ? "Shift・Ctrl・Windows キーと一緒に押した"
            : null;
        state.AltDown = vk;
    }

    /// <summary>Alt を押している間のほかの出来事 (アプリに通したキー・クリック・フォーカスの移動など) があれば、単独押しにしない。</summary>
    private static void NoteAltInterruption(ImeSessionState state, ImeEvent e)
    {
        if (state.AltDown == 0 || state.AltNotAlone is not null ||
            e is ImeEvent.KeyPressed { Key.Vk: VirtualKeys.LMenu or VirtualKeys.RMenu } or ImeEvent.KeyReleased { Key.Vk: VirtualKeys.LMenu or VirtualKeys.RMenu } or
                ImeEvent.StateRequested or ImeEvent.ConversionFinished) return;
        state.AltNotAlone = $"押している間に {e.GetType().Name} があった";
    }

    private static Handled AltReleased(ImeSessionState state, ImeEnv env, ImeKeyRelease key, string? before, Settings settings)
    {
        var vk = key.Vk;
        var notAlone = !settings.AltKeysSwitchKeyboard ? "設定が OFF"
            : state.AltDown != vk ? "押したことを受け取っていない"
            : state.AltNotAlone ?? (key.Interrupted ? "押している間にほかのキー・マウスのボタンを押した" : null);
        state.AltDown = 0;
        state.AltNotAlone = null;
        var side = vk == VirtualKeys.RMenu ? "右" : "左";
        if (notAlone is not null)
        {
            Diagnostics.Log.Decision($"{side} Alt: 単独押しにしません ({notAlone})");
            return PassThrough(state, env);
        }
        Diagnostics.Log.Decision($"{side} Alt の単独押しで{(vk == VirtualKeys.RMenu ? "日本語" : "英数")}にします");
        return Switch(state, env, vk == VirtualKeys.RMenu, before, toggle: false);
    }

    /// <summary>
    /// 何も打っていないときにふつうはアプリに通すが、定型文に使うので受け取るキー: キャレットの前が登録した ;名前 で終わっていたら Space・Tab、
    /// 定型文を入れた直後なら Esc。キャレットの前は、この出来事の入力欄に、アプリに通させた文字を足したもの。
    /// </summary>
    private static List<int> SnippetKeys(ImeSessionState state, ImeEnv env, ImeEvent e, SessionResult result, Settings settings)
    {
        if (IsComposing(state)) return [];
        var before = e.Field?.Before;
        if (e is ImeEvent.KeyPressed { Key: { Ch: > 0 and < 0x10000 } key } && !result.Consumed) before += (char)key.Ch;
        var keys = new List<int>();
        if (SnippetUsable(state, env, before, settings) && SnippetTrigger.TypedBefore(settings.SnippetMark, before) is { } typed && env.Environment.Snippets.Get(typed[1..]) is not null)
            keys.AddRange([VirtualKeys.Space, VirtualKeys.Tab]);
        if (state.LastSnippet is not null) keys.Add(VirtualKeys.Escape);
        return keys;
    }

    /// <summary>
    /// 入力欄に入ったことを、入力モードの表示で知らせるか。OS のシェルの画面 (GNOME のアプリ一覧の検索欄など) は、
    /// 開くアニメーションの途中のキャレットの位置しか知らせてこないので、入ったときは出さない。
    /// </summary>
    public static bool AnnouncesFocus(ImeEvent e) => e is ImeEvent.WithCaret { Caret.FocusEntered: true } && !ShellApps.Contains(e.Field?.App ?? "");

    private static readonly HashSet<string> ShellApps = new(StringComparer.Ordinal) { "gnome-shell" };

    /// <summary>入力モードを表示する時間。</summary>
    private const int ModeIndicatorMs = 1200;

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
            Capture = Capture(active, !open, open, false, settings, game, [], []),
            Revision = rev,
        };
    }

    private static void ApplyFacts(ImeSessionState state, ImeEvent e, Settings settings, InputMode? inputMethod)
    {
        // OS の IME の開閉が変わった (アプリ・Windows が変えた・ほかのフロントの操作): 本体のモードを合わせる
        if (e.ImeOpen is { } open && Direct(state) == open)
        {
            CompositionController.SetDirect(state.Composition, !open);
            ForgetDirectInput(state);
        }
        if (e is ImeEvent.WithCaret { Caret: var caret })
        {
            if (caret.Moved)
            {
                ForgetLastCommit(state);
                // キーボードはアプリがキーを受け取る前に知らせるため、その文字がまだ前文になくても続きとみなす。
                // TSF は編集の後で知らせるので、実際の前文と照合する。
                if (DirectEnglishWord(state) &&
                    caret is { FocusLeft: false, FocusEntered: false, Ctrl: false, PassedKey: > 0, PassedChar: >= '!' and <= '~' and var passedChar } &&
                    EnglishWordCharacters.Contains((char)passedChar) &&
                    (inputMethod == InputMode.Keyboard || e.Field is { Composing: false, Selection: null or "", Before: [.., var last] } && last == passedChar))
                    CompositionController.KeepEnglishWord(state.Composition);
                else
                    ForgetDirectInput(state);
            }
            if (caret.PassedKey is { } vk) CaretMoved(state, vk, caret.Ctrl);
            // 入力欄を出入りした (入った知らせだけで、出た知らせが来ないこともある)
            if (caret.FocusLeft || caret.FocusEntered) ForgetCodeLine(state);
        }
        if (e.Field is not { } field) return;
        if (CodeFocusOf(field, settings) is { } focus)
        {
            var code = focus != CodeFocus.None;
            if (CodeInput(state) != code) SetCodeInput(state, code);
            SetCodeTerminal(state, focus == CodeFocus.Terminal);
        }
        // コードの入力欄でキャレットの前を読めないと、コードの行として英数にする。読めない入力欄を調べられるように、変わったときだけ書く
        var unreadable = CodeInput(state) && field.Before is null;
        if (unreadable != state.UnreadableCode)
        {
            state.UnreadableCode = unreadable;
            Diagnostics.Log.Decision(unreadable
                ? $"コードの入力欄で、キャレットの前の文字が届かないので、コードの行として英数にします (アプリ: {field.App ?? "不明"})"
                : "コードの入力欄で、キャレットの前の文字が届くようになりました");
        }
    }

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

    // ==== ワークアラウンド: ターミナルがキャレットの前の文字を渡さないことへの対処 (#1687) ====
    // 本体は判断を事実だけでするのが原則で、打ったキーから入力欄の中身を推測するのはこの 1 か所だけの例外。
    // 渡さないのはターミナル側の事情 (Windows Terminal の TSF は文書を持たないので空、iTerm2 は日本語を出したあと読めない)。
    // Meltype IME の要求の中で UI Automation で読み直す方法は、待ち合いになるので採らなかった (Windows Terminal の UI Automation は
    // XAML の automation peer で、アプリの UI スレッドで答える。そのスレッドは DLL の中で本体の応答を待って止まっている)。
    // ターミナルが渡すようになったら、ImeSessionState.Line・LineBefore・TrackLine・TypesText はまとめて消す。状態を足したり、ほかの判断に使い回したりしない。

    /// <summary>
    /// コードの行の判断と変換の文脈に使うキャレットの前。コードの入力欄でキャレットの前が届かない (ターミナルで空のときも) ときだけ、
    /// <see cref="ImeSessionState.Line"/> を使う (ワークアラウンド)。定型文・確定し直しは本物の文字が要るので、入力欄の文字 (ImeField.Before) を使う。
    /// </summary>
    private static string? LineBefore(ImeSessionState state, ImeField? field) =>
        CodeInput(state) && field is { } f && (f.Before is null || f.Before.Length == 0 && CodeTerminal(state)) ? state.Line.ToString() : field?.Before;

    private static void TrackLine(ImeSessionState state, ImeEvent e, SessionResult result)
    {
        if (e is ImeEvent.WithCaret { Caret: var caret } && (caret.FocusEntered || caret.FocusLeft || caret.Moved && !TypesText(caret.PassedKey))) state.Line.Clear();
        foreach (var edit in result.Commits)
        {
            state.Line.Length = Math.Max(0, state.Line.Length - edit.DeleteBefore);
            AppendToLine(state, edit.Text);
        }
        if (e is not ImeEvent.KeyPressed { Key: var key } || result.Consumed) return;
        var vk = key.Vk != 0 ? ToggleShortcut.NormalizeKey(key.Vk) : VirtualKeyOf(key.Ch);
        if (key.Ctrl || key.Alt || key.Command) state.Line.Clear();
        else if (key.Text is { Length: > 0 } text) AppendToLine(state, text);
        else if (key.Ch is >= 0x20 and < 0x10000 && !char.IsControl((char)key.Ch)) AppendToLine(state, ((char)key.Ch).ToString());
        else if (vk == VirtualKeys.Back) state.Line.Length = Math.Max(0, state.Line.Length - 1);
        else if (!VirtualKeys.IsModifier(vk) && !IsLockKey(vk)) state.Line.Clear();
    }

    private static void AppendToLine(ImeSessionState state, string text)
    {
        if (text.LastIndexOf('\n') is var newline and >= 0)
        {
            state.Line.Clear();
            text = text[(newline + 1)..];
        }
        state.Line.Append(text);
        if (state.Line.Length > MaxLine) state.Line.Remove(0, state.Line.Length - MaxLine);
    }

    private const int MaxLine = 4000;

    /// <summary>アプリに通すと文字が入るキーか (キャレットが動いても、打った文字の後ろに進んだだけ)。null は文字が変わっただけ。</summary>
    private static bool TypesText(int? vk) =>
        vk is null or VirtualKeys.Back or VirtualKeys.Space or >= 0x30 and <= 0x5A or >= 0x60 and <= 0x6F or >= 0xBA and <= 0xE2;

    /// <summary>
    /// フロントは CapsLock を反映した文字を送る。設定が OFF なら、CapsLock が ON の英字の大小を Shift の側にそろえる
    /// (CapsLock を ON のままでも日本語に変換したい人と、日本語入力中も CapsLock で大文字を打ちたい人がいる)。
    /// </summary>
    private static ImeKey CapsLockApplied(ImeKey key, Settings settings)
    {
        if (!key.CapsLock || settings.CapsLockTypesCapitals || key.Ch is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z')) return key;
        var c = (char)key.Ch;
        return key with { Ch = key.Shift ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c) };
    }

    private static Handled HandleKey(ImeSessionState state, ImeEnv env, ImeKey key, ImeField? field, string? before, Settings settings, (string Text, string Typed)? lastSnippet)
    {
        key = CapsLockApplied(key, settings);
        var composing = IsComposing(state);
        var modifiers = Modifiers(key);
        var plain = modifiers == ShortcutModifiers.None;
        var vk = key.Vk != 0 ? ToggleShortcut.NormalizeKey(key.Vk) : VirtualKeyOf(key.Ch);
        // 左右の Alt の押下は、単独押しかを見分けるためだけに受け取る (変換中の文字はそのまま。アプリに通す)
        if (vk is VirtualKeys.LMenu or VirtualKeys.RMenu)
        {
            AltPressed(state, key, vk);
            return PassThrough(state, env);
        }
        if (plain && vk is VirtualKeys.ImeOn or VirtualKeys.ImeOff)
        {
            var japanese = vk == VirtualKeys.ImeOn;
            return key.Repeat || japanese && ShowsJapanese(state, before, settings) ? Consumed : Switch(state, env, japanese, before, toggle: false);
        }
        var toggle = settings.EffectiveToggleShortcuts.Any(shortcut => shortcut.Matches(vk, modifiers));
        // 押しっぱなしで、入力モードを何度も切り替えない
        if (key.Repeat && (toggle || plain && vk is ToggleShortcut.Hankaku or VirtualKeys.OemAttn or VirtualKeys.Kana)) return Consumed;
        if (toggle && !composing) return Switch(state, env, !ShowsJapanese(state, before, settings), before, toggle: true);
        // 入力中は変換ボックスの英字表示の切り替え。変換中は変換ボックスのキーとして使う (Shift+Space は「前の候補へ」)
        if (toggle && !IsConverting(state)) return ComposeKey(state, env, VirtualKeys.Kanji, VirtualKeys.Kanji, null, false, false, false, false);
        // ロックのキーは文字を入れないので、打ちかけに触らずにアプリに通す (切り替えキーに登録したものは、上で切り替えに使った)
        if (IsLockKey(vk)) return PassThrough(state, env);
        if (vk == ToggleShortcut.Hankaku && !key.Ctrl && !key.Alt && !composing) return Consumed;
        if (vk == VirtualKeys.OemAttn && plain) return Switch(state, env, false, before, toggle: false);
        if (vk is VirtualKeys.Kana or VirtualKeys.Convert or VirtualKeys.NonConvert && plain)
        {
            if (HandleRoleKey(state, env, vk) is { } role) return role;
            // 入力中の無変換は、英数キーと同じく確定してから英数にする。入力中の変換・かなは変換ボックスのキー
            var switches = composing ? vk == VirtualKeys.NonConvert : !(vk == VirtualKeys.Convert && ShowsJapanese(state, before, settings) && field?.Selection is { Length: > 0 });
            if (switches && !IsThumbKey(state, vk, settings))
            {
                return key.Repeat ? Consumed : Switch(state, env, vk != VirtualKeys.NonConvert, before, toggle: false);
            }
        }
        if (!composing && !key.Ctrl && !key.Alt && !key.Command && Snippet(state, env, vk, key, field?.Before, settings, lastSnippet) is { } snippet) return snippet;
        // 複数のスカラーの文字 (アプリが元のイベントで入れる): 先頭が結合文字なら直前の文字につなげて確定する
        if (key.Text is { Length: > 0 } text && Rune.DecodeFromUtf16(text, out var first, out _) == System.Buffers.OperationStatus.Done)
        {
            return Rune.GetUnicodeCategory(first) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                ? CommitCombiningForPassThrough(state, env, preserveLatinRaw: first.Value is >= 0x0300 and <= 0x036F)
                : CommitForPassThrough(state, env);
        }
        // 不正な Unicode (負数・単独サロゲート・上限超過) は、状態を変えずにアプリへそのまま渡す
        if (key.Ch != 0 && !Rune.IsValid(key.Ch)) return PassThrough(state, env);
        // 結合文字は、未確定の表示に取り込まず、直前の内容を確定してからアプリに通す。
        // U+0300..036F の Latin アクセントのみ ASCII 英字の原文を優先する (濁点・異体字セレクターでは、日本語の表示や選んだ候補をそのまま確定する)
        if (key.Ch != 0 && Rune.GetUnicodeCategory(new Rune(key.Ch)) is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            return CommitCombiningForPassThrough(state, env, preserveLatinRaw: key.Ch is >= 0x0300 and <= 0x036F);
        // 補助面の文字 (絵文字など) は char に入らないので、未確定の内容を確定してからアプリに通す
        if (key.Ch >= 0x10000) return CommitForPassThrough(state, env);
        var result = ComposeKey(state, env, vk, Position(vk, key.Physical), key.Ch > 0 ? (char)key.Ch : null, key.Shift, key.Ctrl, key.Alt, key.Command, key.ReleaseFollows);
        // アプリに通したキーはキャレットを動かすかもしれない (Core が通すと決めたので、フロントからは送られてこない)
        if (!result.Consumed) CaretMoved(state, vk, key.Ctrl);
        return result;
    }

    /// <summary>CapsLock・NumLock・ScrollLock。</summary>
    private static bool IsLockKey(int vk) => vk is 0x14 or 0x90 or 0x91;

    /// <summary>親指シフトのかな配列 (NICOLA・中指NICOLA・TRON) の親指キー (無変換・変換)。変換ボックスで受け取り、文字に使わずに離したらもとの働きをする。</summary>
    private static bool IsThumbKey(ImeSessionState state, int vk, Settings settings) =>
        settings.InputStyle == InputStyle.Kana && !Direct(state) && vk is VirtualKeys.Convert or VirtualKeys.NonConvert &&
        settings.KanaLayout is KanaLayout.Nicola or KanaLayout.NakayubiNicola or KanaLayout.Tron;

    /// <summary>
    /// 定型文 (issue #290)。;名前 の文字はそのままアプリへ渡し、続く Space / Tab で登録した文に置き換える。入れた直後の Esc で、打った ;名前 に戻す。
    /// キャレットの前の文字が分からない・確定した文字を消せない入力欄では使わない。
    /// </summary>
    private static SessionResult? Snippet(ImeSessionState state, ImeEnv env, int vk, ImeKey key, string? before, Settings settings, (string Text, string Typed)? last)
    {
        if (!SnippetUsable(state, env, before, settings)) return null;
        if (vk == VirtualKeys.Escape && last is { } undo)
            return new SessionResult(true, [new TextEdit(undo.Text.Length, undo.Typed, undo.Text)], null);
        if (vk is VirtualKeys.Space or VirtualKeys.Tab && !key.Shift && SnippetTrigger.TypedBefore(settings.SnippetMark, before) is { } typed &&
            env.Environment.Snippets.Get(typed[1..]) is { } text)
        {
            state.LastSnippet = (text, typed);
            return new SessionResult(true, [new TextEdit(typed.Length, text, typed)], null);
        }
        return key.Ch is > 0 and < 0x10000 && SnippetTrigger.PassesThrough(settings.SnippetMark, before, (char)key.Ch) ? PassThrough(state, env) : null;
    }

    private static bool SnippetUsable(ImeSessionState state, ImeEnv env, [NotNullWhen(true)] string? before, Settings settings) =>
        before is not null && CanDeleteSurrounding(state) && settings.SnippetMark is [_] && settings.InputStyle != InputStyle.Kana && env.Environment.Snippets.Count > 0;

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
    private static SessionResult Verify(ImeSessionState state, SessionResult result, ImeField? field)
    {
        if (!result.Commits.Any(edit => edit.DeleteBefore > 0)) return result;
        var before = field?.Before;
        var edits = new List<TextEdit>(result.Commits.Count);
        foreach (var edit in result.Commits)
        {
            var verified = edit.DeleteBefore == 0 ||
                before is not null && edit.Expect is { } expect && expect.Length == edit.DeleteBefore && before.EndsWith(expect, StringComparison.Ordinal) &&
                CanDeleteSurrounding(state) && !(field?.Composing == true && edit.Current is null);
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
    private static SessionResult WithPaste(ImeSessionState state, SessionResult result, Settings settings)
    {
        if (result.Commits.Count == 0) return result;
        var paste = PastePolicy.ShouldPaste(settings, state.Field?.App, PastePolicy.IsQtWindowClass(state.Field?.WindowClass));
        return result with { Commits = result.Commits.Select(edit => edit with { Paste = paste || edit.Text.Contains('\n') }).ToList() };
    }

    /// <summary>OS が変換中の文字を表示のまま確定した: 本体も確定したことにする。文字はもう入っているので、確定し直しも入れない。</summary>
    private static SessionResult CommittedOutside(ImeSessionState state, ImeEnv env)
    {
        CompositionController.CommitPending(state.Composition, Env(state, env) with { CanDeleteBackward = false });
        CompositionController.ResetContext(state.Composition);
        return Consumed;
    }

    /// <summary>
    /// 表示するモードを japanese にする。変換中なら確定してから。日本語にするときは、コードの行もその行だけ日本語にする。
    /// 英数にするとき、toggle (日本語 ⇔ 英数の切り替え) は、日本語にしていたコードの行を英数に戻すだけで IME を開いたままにする。
    /// そうでなければ (英数キー・無変換) 直接入力にする。
    /// </summary>
    private static (FieldModes? Modes, string? Key) FieldModeOf(ImeEnv env, ImeField? field) =>
        (ForbidsLearning(field) || !env.Settings.ForApp(field?.App).LearningEnabled ? null : env.Parts.Services.FieldModes,
         FieldModes.KeyOf(field?.App, field?.Element?.ClassName));

    /// <summary>入力欄で前に切り替えたモード (日本語なら true)。覚えていない・ゲームなら null。</summary>
    public static bool? LearnedMode(ImeEnv env, ImeField? field) =>
        !IsGame(env.Settings, field) && FieldModeOf(env, field) is ({ } modes, { } key) ? modes.Get(key) : null;

    /// <summary>
    /// 入力欄に入ってまだ何も打たないうちに切り替えたら、その入力欄のモードとして覚える。打ち始めたら (文字のキー・確定) 覚えない
    /// (英単語を 1 つだけ英数で打つ、などの一時的な切り替え)。
    /// </summary>
    private static void RememberFieldMode(ImeSessionState state, ImeEvent e, SessionResult result, bool explicitChange, bool wasJapanese, bool japanese,
        FieldModes? fieldModes, string? fieldKey)
    {
        if (!state.FreshFocus) return;
        if (explicitChange && japanese != wasJapanese && fieldKey is not null) fieldModes?.Remember(fieldKey, japanese);
        if (IsComposing(state) || result.Commits.Count > 0 || e is ImeEvent.KeyPressed { Key.Ch: > 0 } or ImeEvent.KeyPressed { Key.Text: not null }) state.FreshFocus = false;
    }

    private static Handled Switch(ImeSessionState state, ImeEnv env, bool japanese, string? before, bool toggle)
    {
        var result = IsComposing(state) ? CommitPending(state, env) : Consumed;
        if (japanese)
        {
            CompositionController.SetDirect(state.Composition, false);
            if (CodeEnglishAt(state, before, env.App)) SetCodeLine(state, true, before, env.App);
        }
        else
        {
            var codeLineReset = SetCodeLine(state, false, before, env.App);
            if (!toggle || !codeLineReset) CompositionController.SetDirect(state.Composition, true);
        }
        return new(result, ExplicitChange: true);
    }

    /// <summary>受け持たなくなった (一時停止・動作モードの変更): 変換中の内容は確定して、キーはアプリに通す。</summary>
    private static SessionResult Leave(ImeSessionState state, ImeEnv env) => IsComposing(state) ? CommitPending(state, env) with { Consumed = false } : Pass;

    private static bool ShowsJapanese(ImeSessionState state, string? before, Settings settings) => !Direct(state) && !CodeEnglishAt(state, before, settings);

    private static ShortcutModifiers Modifiers(ImeKey key) =>
        (key.Shift ? ShortcutModifiers.Shift : 0) | (key.Ctrl ? ShortcutModifiers.Control : 0) |
        (key.Alt ? ShortcutModifiers.Alt : 0) | (key.Command ? ShortcutModifiers.Win : 0);

    private static ImeCapture Capture(bool active, bool direct, bool japanese, bool directEnglishWord, Settings settings, bool game,
        IEnumerable<int> composingControlKeys, IReadOnlyList<int> snippetKeys)
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
        // 使わない Shift+Space を受け取ると、TSF では送り直しも PreserveKey で受け取り、アプリに届かない (#1883)。
        // 英数でも、コードの行でも、全角スペースにしないので受け取らない
        if (japanese)
        {
            keys.Add(new ImeShortcut(VirtualKeys.Space, (int)ShortcutModifiers.Shift));
            if (settings.SpaceWidth == SpaceWidth.FullWidth) keys.Add(new ImeShortcut(VirtualKeys.Space, 0));
        }
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

    /// <summary>設定によらず受け取る、モードを切り替えるキー (半角/全角 の 3 つのキーコード・英数・かな・変換・無変換・ImeOn・ImeOff)。</summary>
    private static readonly int[] ModeKeys =
        [ToggleShortcut.Hankaku, VirtualKeys.OemAuto, VirtualKeys.OemEnlw, VirtualKeys.OemAttn, VirtualKeys.Kana, VirtualKeys.Convert, VirtualKeys.NonConvert, VirtualKeys.ImeOn, VirtualKeys.ImeOff];

    private static SessionResult Pass => new(false, [], null);

    private static SessionResult Consumed => new(true, [], null);
    private const long UserDictionaryCheckMs = 1000;

    /// <summary>入力欄のアプリの設定 (アプリ別設定を反映したもの)。</summary>
    /// <summary>入力欄 (state.Field) のアプリの設定を入れた env。</summary>
    private static ImeEnv ForField(ImeSessionState state, ImeEnv env) => env with { App = env.Settings.ForApp(state.Field?.App) };

    /// <summary>変換ボックスの処理に渡す、入力欄の状態でないもの (今の設定で作る)。</summary>
    private static CompositionEnv Env(ImeSessionState state, ImeEnv env) => Env(state, env, env.App);

    private static CompositionEnv Env(ImeSessionState state, ImeEnv env, Settings settings)
    {
        var lateResults = env.Parts.LateResults();
        var before = LineBefore(state, state.Field);
        CompositionEnv composition = null!;
        composition = new(env.Parts.Detector, env.Parts.Converter, settings.LearningEnabled ? env.Parts.Services : env.Parts.Services.WithoutLearning(), settings)
        {
            ReadingOf = env.Parts.ReadingOf,
            ClassifyDirect = env.Parts.ClassifyDirect is { } classify ? (letters, final) => classify(letters, final, settings) : null,
            NowMs = env.TimeMs(),
            Selection = state.Field?.Selection,
            CanDeleteBackward = CanDeleteSurrounding(state),
            Clipboard = () => env.Environment.ClipboardText,
            OtherTipVersion = () => env.Environment.OtherTipVersion,
            NowPlaying = env.Parts.NowPlaying,
            Corrected = env.Parts.Corrected,
            Before = before,
            After = state.Field?.After,
            CodeString = CodeInput(state) && CodeLineKind(state, before, settings) == LineKind.String,
            LateResults = lateResults,
            Deadline = lateResults ? 0 : long.MaxValue,
            LiveCache = state.Composition.LiveCache,
            Context = () => CompositionContext.ContextOf(state.Composition, composition),
        };
        return composition;
    }

    /// <summary>英数 (直接入力) か。true の間はキーをすべてアプリに渡す (Mac の「英数」キー、「かな」キーで戻す)。</summary>
    public static bool Direct(ImeSessionState state) => CompositionController.IsDirect(state.Composition);

    public static bool DirectEnglishWord(ImeSessionState state) => CompositionController.InEnglishWord(state.Composition);

    /// <summary>入力を覚えないように求める入力欄か。その欄で扱った未確定入力は、欄を離れて確定しても学習しない。</summary>
    public static bool Private(ImeSessionState state) => CompositionController.Private(state.Composition);
    public static bool CodeInput(ImeSessionState state) => state.CodeInput;

    /// <summary>エディターとターミナルを行き来した (VS Code など、同じ入力欄の本体のまま): 日本語にしていたら英数に戻す。</summary>
    private static void SetCodeTerminal(ImeSessionState state, bool value)
    {
        if (state.CodeTerminal != value) state.CodeJapanese = false;
        state.CodeTerminal = value;
    }

    private static void SetCodeInput(ImeSessionState state, bool value)
    {
        state.CodeInput = value;
        state.CodeJapanese = false;
    }

    /// <summary>コードの入力欄がターミナルか。</summary>
    public static bool CodeTerminal(ImeSessionState state) => state.CodeTerminal;

    /// <summary>
    /// コードの入力欄で、コードの行を日本語にする (japanese) / 英数に戻す。半角/全角 を押したとき。
    /// コードの行 (コメント・文字列の外) を日本語にしたとき、日本語にしていたのを英数に戻したときに true。
    /// コメント・文字列の中、コードの入力欄でないときは何もせずに false (IME の ON/OFF を切り替える)。
    /// </summary>
    private static bool SetCodeLine(ImeSessionState state, bool japanese, string? before, Settings settings)
    {
        if (!CodeInput(state) || Direct(state) || CompositionController.IsComposing(state.Composition)) return false;
        if (!japanese)
        {
            if (!state.CodeJapanese) return false;
            state.CodeJapanese = false;
            Diagnostics.Log.Info("コードの行: 英数に戻す");
            return true;
        }
        if (state.CodeJapanese || CodeLineKind(state, before, settings) != LineKind.Code) return false;
        state.CodeJapanese = true;
        Diagnostics.Log.Info("コードの行: 日本語で入力 (改行しても続ける。キャレットが別の場所に移るか、もう一度押すまで)");
        return true;
    }

    /// <summary>
    /// 次に打つキーが、コードの行なので英数のままアプリに通るか (入力モードの表示に使う)。
    /// before はキャレットの前。
    /// </summary>
    private static bool CodeEnglishAt(ImeSessionState state, string? before, Settings settings)
    {
        if (!CodeInput(state) || Direct(state) || CompositionController.IsComposing(state.Composition)) return false;
        return !state.CodeJapanese && CodeLineKind(state, before, settings) == LineKind.Code;
    }

    /// <summary>
    /// キャレットが動いた (passedVk はアプリに通したキー。0 ならクリックなど、control は Ctrl を押していたか)。
    /// 半角/全角 で日本語にしていたら、矢印・Tab・Ctrl の操作・クリックで英数に戻す (Meltype キーボードと同じ。改行では戻さない)。
    /// アプリに通した Esc (vim などの操作に移る) でも戻す。ターミナルは、出力でもキャレットが動くので、キーなしの移動では戻さない。
    /// </summary>
    public static void CaretMoved(ImeSessionState state, int passedVk, bool control)
    {
        if (!state.CodeJapanese) return;
        var moved = control || passedVk is VirtualKeys.Tab or VirtualKeys.Escape or (>= 0x21 and <= 0x28) or 0x2E || !CodeTerminal(state) && passedVk == 0;
        if (moved) ForgetCodeLine(state);
    }

    /// <summary>コードの行を日本語にしていたのをやめる (キャレットが別の場所に移った・フォーカスが外れた)。</summary>
    public static void ForgetCodeLine(ImeSessionState state)
    {
        if (!state.CodeJapanese) return;
        state.CodeJapanese = false;
        Diagnostics.Log.Info("コードの行: キャレットが別の場所に移ったので英数に戻す");
    }

    /// <summary>キャレットの位置の種類。キャレットの前が分からない入力欄 (周辺文字列を返さないターミナル) はコードの行とする (日本語にするには SetCodeLine)。</summary>
    private static LineKind CodeLineKind(ImeSessionState state, string? before, Settings settings) => before is null ? LineKind.Code : LineContext.ClassifyText(before, settings, CodeTerminal(state));

    /// <summary>入力欄が確定済みの文字の削除に対応しているか (Linux の IBus では、対応していないアプリがある)。false なら確定し直さない。</summary>
    public static bool CanDeleteSurrounding(ImeSessionState state) => state.Field?.CanRemove ?? true;

    /// <summary>変換ボックスに何か入っているか。</summary>
    public static bool IsComposing(ImeSessionState state) => CompositionController.IsComposing(state.Composition);

    /// <summary>変換して候補を選んでいるか。</summary>
    public static bool IsConverting(ImeSessionState state) => CompositionController.IsConverting(state.Composition);

    /// <summary>入力中に Ctrl と一緒に押すと、変換ボックスで使うキー。</summary>
    public static IReadOnlyList<int> ComposingControlKeys(ImeSessionState state, ImeEnv env) => CompositionController.ComposingControlKeys(state.Composition, Env(state, env));

    /// <summary>
    /// キーを 1 つ処理する。before / after は入力欄のキャレットの前後の文字列 (分かれば。英語とも日本語とも読める語の判定と変換の文脈に使う)。
    /// 入力モードを切り替えるキーの意味は ImeSession が決めるので、ここでは見ない。
    /// </summary>
    /// <param name="position">押したキーの位置 (<see cref="KeyPress.Position"/>)。</param>
    /// <param name="releaseFollows">ワークアラウンド (ImeEvent.KeyReleased): 離したことは <see cref="HandleKeyUp"/> で届く。false なら押した直後に離したとみなす。</param>
    public static SessionResult ComposeKey(ImeSessionState state, ImeEnv env, int vk, int position, char? ch, bool shift, bool control, bool alt, bool command, bool releaseFollows = false)
    {
        var settings = env.App;
        var before = LineBefore(state, state.Field);
        // 文字列を閉じる引用符は英数のままアプリに通す (打っている途中の日本語は確定する。空の文字列 "" を閉じるときも)。
        // 変換中の before は、変換中の文字より前 (フロントが渡す)
        if (CodeInput(state) && !Direct(state) && !control && !alt && !command &&
            ch is '"' or '\'' or '`' && before is { } line &&
            CodeLineKind(state, line, settings) == LineKind.String &&
            CodeLineKind(state, line + ch, settings) == LineKind.Code)
        {
            var committed = CompositionController.IsComposing(state.Composition) ? CommitPending(state, env) : new SessionResult(false, [], null);
            return committed with { Consumed = false };
        }
        var now = env.TimeMs();
        // Ctrl・Option・Command と一緒のキーは、変換ボックスが空ならアプリの操作 (コピーなど) なので触らない。
        var modifier = control || alt || command;
        if (DirectEnglishWord(state) &&
            (ch is { } directChar && !char.IsAsciiLetterOrDigit(directChar) && directChar is not ('@' or '_') ||
             before is { Length: > 0 } && !char.IsAsciiLetterOrDigit(before[^1]) && before[^1] is not ('@' or '_')))
            CompositionController.LeaveEnglishWord(state.Composition);
        if ((Direct(state) && (!settings.DirectModeAutoDetect || DirectEnglishWord(state))) || !settings.Enabled)
        {
            return CompositionController.Pass(state.Composition, Env(state, env), now);
        }
        // 先頭か空白の直後の /command・$skill・@ファイル名 は、変換せずにそのままアプリへ渡す (#193)。
        // 文章の入力欄の / は、変換ボックスに ・ として入れる。Space で / にすると、続く名前はそのまま渡す。
        var lineKind = CodeInput(state) && !CompositionController.IsComposing(state.Composition) ? CodeLineKind(state, before, settings) : (LineKind?)null;
        var comment = lineKind == LineKind.Comment;
        // ドキュメントコメントの /// や //! の記号は本文ではないので、日本語の記号 (・ ！) にしない。
        if (comment && !modifier && before is { } commentLine && ch is { } mark && LineContext.ContinuesSlashCommentMarker(commentLine, mark, settings.InputStyle == InputStyle.Kana))
        {
            return CompositionController.Pass(state.Composition, Env(state, env), now);
        }
        // コードの中とターミナルの AI の入力行では /command を使うので、打ち始めの / を ・ にしない。
        var passesSlash = !state.CodeJapanese && lineKind is LineKind.Code or LineKind.Prompt;
        var slashAtStart = !modifier && !CompositionController.IsComposing(state.Composition) && ch is '/' && !passesSlash && !SigilWord.InNameAfter(before, comment) && settings.SigilWordsDirect &&
            SigilWord.PassesAfter(before, '/', comment);
        if (!slashAtStart && !modifier && !CompositionController.IsComposing(state.Composition) && ch is { } c && settings.SigilWordsDirect && SigilWord.PassesAfter(before, c, comment))
        {
            return CompositionController.Pass(state.Composition, Env(state, env), now);
        }
        if (!state.CodeJapanese && lineKind == LineKind.Code)
        {
            return CompositionController.Pass(state.Composition, Env(state, env), now);
        }
        // 入力欄を作り直さないフロント (Meltype キーボード) でも、外で変えたユーザー辞書を使う
        if (!CompositionController.IsComposing(state.Composition) && now - state.UserDictionaryCheckedMs >= UserDictionaryCheckMs)
        {
            state.UserDictionaryCheckedMs = now;
            env.UserDictionary?.Refresh();
        }
        var result = CompositionController.Press(state.Composition, Env(state, env) with { SlashAtStart = slashAtStart }, new KeyPress(vk, position, ch, shift, control, alt, command, now), releaseFollows);
        // アプリに渡したキー (変換していないときの BackSpace・矢印・Enter など) はキャレットを動かすかもしれない。
        // 前に確定した語を確定し直さない (消す位置がずれて関係ない文字を消さないように)
        if (!result.Consumed && !CompositionController.IsComposing(state.Composition)) ForgetLastCommit(state);
        return result;
    }

    /// <summary>
    /// ワークアラウンド (ImeEvent.KeyReleased): releaseFollows を付けて押したキーを離した。親指シフトの親指キー・中指NICOLA のシフトのキーを離したことを、
    /// かな配列に伝える (文字に使わずに離した親指キーは、もとの変換・無変換をする)。
    /// 本体が受け持っている間に Consumed が false なら、保留していた押下と解放をアプリに渡す。
    /// </summary>
    public static SessionResult HandleKeyUp(ImeSessionState state, ImeEnv env, int vk, PhysicalKey? physical) =>
        CompositionController.Release(state.Composition, Env(state, env), vk, Position(vk, physical));

    /// <summary>
    /// キーの位置 (<see cref="KeyPress.Position"/>)。物理キーが分からないフロント (端末・Android) と文字の無いキーは、仮想キーコードの位置のキーとみなす
    /// (Vk を文字から決めたなら、QWERTY のキーボードでその文字を打つキー)。
    /// </summary>
    private static int Position(int vk, PhysicalKey? physical) => physical?.JisPosition() ?? vk;

    /// <summary>
    /// 変換・無変換・ひらがな/カタカナ キー。設定「入力中のキーの役割を分ける」が ON (無変換は「無変換でカタカナを切り替える」が ON でもよい) で入力中なら、変換ボックスで処理する (issue #199)。
    /// そうでなければ null (ImeSession が英数 / 日本語の切り替えに使う)。
    /// </summary>
    public static SessionResult? HandleRoleKey(ImeSessionState state, ImeEnv env, int vk)
    {
        if (vk is not (VirtualKeys.Convert or VirtualKeys.NonConvert or VirtualKeys.Kana)) return null;
        // 無変換でのカタカナ切り替えは、役割を分ける設定が OFF でも働く (CompositionController 側で NonConvertCyclesKana を見る)
        var settings = env.App;
        if (state.Composition.Scene is not CompositionScene.WithInput input || Direct(state)) return null;
        if (!(settings.SeparateKeyRoles || vk == VirtualKeys.NonConvert && CompositionLifecycle.NonConvertCyclesKana(input, settings))) return null;
        return ComposeKey(state, env, vk, vk, null, false, false, false, false);
    }

    /// <summary>
    /// このセッションの外でキャレットが動いた (OS の IME がアプリに通したキー・クリック・別の入力欄に移った) とき。
    /// 前に確定した語を、次の語の文脈で確定し直さないようにする。
    /// </summary>
    public static void ForgetLastCommit(ImeSessionState state)
    {
        CompositionController.ForgetLastCommit(state.Composition);
    }

    public static void ForgetDirectInput(ImeSessionState state) => CompositionController.ForgetDirectInput(state.Composition);

    /// <summary>フォーカスが外れたときなど。未確定の内容をそのまま確定する。</summary>
    public static SessionResult CommitPending(ImeSessionState state, ImeEnv env)
    {
        var result = CompositionController.CommitPending(state.Composition, Env(state, env));
        CompositionController.ResetContext(state.Composition);
        return result;
    }

    internal static void CancelPending(ImeSessionState state)
    {
        CompositionController.CancelPending(state.Composition);
        SetCodeInput(state, false);
        SetCodeTerminal(state, false);
    }

    /// <summary>
    /// 文字を伴わない・扱えないキーを、変換ボックスの状態を変えずにアプリへそのまま渡す (Consumed = false)。
    /// 不正な C ABI の引数で不正な文字列を作らないための経路。
    /// </summary>
    public static SessionResult PassThrough(ImeSessionState state, ImeEnv env) => CompositionController.Pass(state.Composition, Env(state, env), env.TimeMs());

    /// <summary>
    /// 未確定の内容を確定したうえで、元の OS イベントを 1 回だけアプリへ通す (Consumed = false)。
    /// 補助面の文字 (非 BMP) を char へ切り詰めずに渡すための経路。確定と pass-through を二重に行わない。
    /// </summary>
    public static SessionResult CommitForPassThrough(ImeSessionState state, ImeEnv env) => CommitForPassThrough(state, env, preserveLatinRaw: false, preserveText: false);

    /// <summary>
    /// 結合文字が直前の文字に付くよう、確定する文字列に自動空白を加えない。
    /// 結合アクセントなら、明示的に選択していない ASCII 英字の原文を優先して確定する。
    /// </summary>
    public static SessionResult CommitCombiningForPassThrough(ImeSessionState state, ImeEnv env, bool preserveLatinRaw) =>
        CommitForPassThrough(state, env, preserveLatinRaw, preserveText: true);

    private static SessionResult CommitForPassThrough(ImeSessionState state, ImeEnv env, bool preserveLatinRaw, bool preserveText)
    {
        var result = CompositionController.CommitPending(state.Composition, Env(state, env), consumed: false, preserveLatinRaw, preserveText);
        CompositionController.ResetContext(state.Composition);
        return result;
    }

    /// <summary>入力の内容を変えない出来事 (フォーカス・キャレットの知らせなど) の結果。表示は今のまま (変換中ならその表示)。</summary>
    public static SessionResult Unchanged(ImeSessionState state, ImeEnv env) => CompositionController.Current(state.Composition, Env(state, env));

    /// <summary>背景の変換の結果が届いたとき (IKanjiConverter.ResultReady を入力スレッドに移して呼ぶ)。表示が変わらないなら null (入力していない・候補を開いている)。</summary>
    public static SessionResult? RefreshConversion(ImeSessionState state, ImeEnv env) => CompositionController.RefreshConversion(state.Composition, Env(state, env));

    /// <summary>候補ウィンドウで候補をクリックしたとき。</summary>
    public static SessionResult SelectCandidate(ImeSessionState state, ImeEnv env, int index) => CompositionController.SelectCandidate(state.Composition, Env(state, env), index);

    /// <summary>候補の一覧で予測変換の候補をクリックしたとき。その候補を確定する。</summary>
    public static SessionResult SelectPrediction(ImeSessionState state, ImeEnv env, int index) => CompositionController.CommitPredictionAt(state.Composition, Env(state, env), index);
}
