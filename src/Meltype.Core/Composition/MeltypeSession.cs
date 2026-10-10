// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>
/// 入力欄への書き込み 1 回分。DeleteBefore 文字をキャレットの前から消してから Text を入れる (確定し直すとき以外は 0)。
/// Expect は消す文字 (このセッションで確定した文字。分からなければ null)。入力欄の文字がこれと違えば、消さないほうが安全。
/// </summary>
public readonly record struct TextEdit(int DeleteBefore, string Text, string? Expect = null);

/// <summary>
/// 1 回のキー入力の結果。Consumed が false ならそのキーはアプリにそのまま渡す (Commits を入れた後で)。
/// View は変換ボックスの内容 (null なら変換ボックスを閉じる)。
/// </summary>
public sealed record SessionResult(bool Consumed, IReadOnlyList<TextEdit> Commits, CompositionView? View)
{
    /// <summary>Swift などから読みやすいように JSON にする (NativeAOT でも使えるよう手書き)。</summary>
    public string ToJson()
    {
        var builder = new StringBuilder();
        builder.Append("{\"consumed\":").Append(Consumed ? "true" : "false").Append(",\"commits\":[");
        for (var i = 0; i < Commits.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append("{\"deleteBefore\":").Append(Commits[i].DeleteBefore).Append(",\"text\":");
            AppendString(builder, Commits[i].Text);
            if (Commits[i].Expect is { } expect)
            {
                builder.Append(",\"expect\":");
                AppendString(builder, expect);
            }
            builder.Append('}');
        }
        builder.Append("],\"view\":");
        if (View is not { } view)
        {
            builder.Append("null}");
            return builder.ToString();
        }
        builder.Append("{\"text\":");
        AppendString(builder, view.Text);
        builder.Append(",\"converting\":").Append(view.Converting ? "true" : "false");
        builder.Append(",\"selectedIndex\":").Append(view.SelectedIndex);
        builder.Append(",\"selectedClause\":").Append(view.SelectedClause);
        builder.Append(",\"hint\":");
        AppendString(builder, view.Hint);
        builder.Append(",\"candidates\":");
        AppendArray(builder, view.Candidates);
        builder.Append(",\"clauses\":");
        AppendArray(builder, view.Clauses ?? []);
        // 選んでいる候補の意味 (無ければ null)。少し止まってから出すのは Swift・Python 側
        builder.Append(",\"suggestion\":");
        if (view.Suggestion is { } suggestion) AppendString(builder, suggestion);
        else builder.Append("null");
        builder.Append(",\"meaning\":");
        if (view.Meaning is { } meaning) AppendString(builder, meaning);
        else builder.Append("null");
        // 候補ごとの注釈 (英訳の候補なら「英訳」、無ければ null)
        builder.Append(",\"notes\":[");
        for (var i = 0; i < view.Candidates.Count; i++)
        {
            if (i > 0) builder.Append(',');
            if (view.Notes?.ElementAtOrDefault(i) is { } note) AppendString(builder, note);
            else builder.Append("null");
        }
        builder.Append(']');
        builder.Append("}}");
        return builder.ToString();
    }

    private static void AppendArray(StringBuilder builder, IReadOnlyList<string> items)
    {
        builder.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) builder.Append(',');
            AppendString(builder, items[i]);
        }
        builder.Append(']');
    }

    private static void AppendString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                    else builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }
}

/// <summary>
/// OS の正式な IME の仕組み (Mac の Input Method Kit、Linux の IBus / fcitx5) から使う、Meltype の入力の本体。
/// Windows 版はキーボードフックで打鍵を横取りするが、正式な IME では OS がキーを 1 つずつ渡してきて、
/// 「使ったか (アプリに渡さないか)」をその場で返す。そのやり取りを同期的に行う。
///
/// キーは Windows の仮想キーコード (A-Z = 0x41-0x5A、Space = 0x20 …) で渡す。入力する文字は ch で渡す (キーボード配列の違いは OS 側で解決済み)。
/// 変換ボックスの表示・確定する文字は、戻り値の <see cref="SessionResult"/> で返す。1 つのスレッドから使う。
/// </summary>
public sealed class MeltypeSession
{
    private readonly CaptureGate _gate;
    private readonly CompositionController _controller;
    private readonly Host _host = new();
    private readonly Func<Settings> _settings;
    private readonly SigilWord _sigil = new();

    public MeltypeSession(CompositionDetector detector, IKanjiConverter converter, CompositionOptions options, Func<Settings> settings)
    {
        _settings = settings;
        _gate = new CaptureGate(() => { });
        _controller = new CompositionController(_gate, detector, converter, _host, options);
    }

    /// <summary>
    /// 既定の辞書・学習データ (保存場所は <see cref="AppPaths"/>) で作る。converter は OS 側の変換エンジン、
    /// moreCandidates は読みに対する候補の一覧 (無ければ null)、wordChecker は OS のスペルチェッカー (無ければ null)。
    /// </summary>
    public static MeltypeSession CreateDefault(IKanjiConverter converter, Func<string, IReadOnlyList<string>>? moreCandidates, IWordChecker? wordChecker, bool autoSpacing = false)
    {
        AppPaths.MigrateFromOldName();
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var settings = Settings.Load(AppPaths.ConfigFile);
        // 設定で「ファイルにログを書く」を ON にしていれば、Mac でも meltype.log に書く (動かないときの調査用)。
        Diagnostics.Log.SetFileOutput(settings.FileLog ? AppPaths.LogFile : null);
        Diagnostics.Log.RecordText = settings.LogTypedText;
        var userDirectory = AppPaths.UserDictionaryDirectory;
        var detector = CompositionDetector.CreateDefault(userDirectory);
        // OS のスペルチェッカーが無ければ (Linux)、同梱のよく使う英単語の一覧を使う (meeting を英語と分かるように)。
        detector.SpellChecker = wordChecker is { IsAvailable: true } ? wordChecker : Detection.BuiltInWordChecker.Shared;
        var languages = new LanguageMemory(AppPaths.LanguageMemoryFile);
        detector.Memory = languages;
        detector.UseScoredSegmentation = () => settings.ScoredSegmentation;
        var options = new CompositionOptions
        {
            LiveConversion = () => settings.LiveConversion,
            AutoCorrect = () => settings.AutoCorrectAfterCommit && settings.DetectionLevel != DetectionLevel.Manual,
            Level = () => settings.DetectionLevel,
            Candidates = CandidateDictionary.Load(userDirectory),
            ContextRules = ContextRules.Load(userDirectory),
            History = new ConversionHistory(AppPaths.ConversionHistoryFile),
            // dictionaries/ に置いた macOS の「ユーザ辞書」の .plist も読む (#40)
            UserDictionary = new UserDictionary(AppPaths.UserDictionaryFile, importDirectory: userDirectory),
            MoreCandidates = moreCandidates,
            Misspellings = MisspellingDictionary.Load(userDirectory),
            Languages = languages,
            Translations = TranslationDictionary.Load(),
            TranslationCandidates = () => settings.TranslationCandidates,
            TabConversion = () => settings.TabConversion,
            Meanings = MeaningDictionary.Load(),
            CandidateMeanings = () => settings.ShowCandidateMeanings,
            RomajiTypos = RomajiTypoCorrector.Load(detector.Romaji),
            CorrectTypos = () => settings.CorrectTypos,
            SlashAsMiddleDot = () => settings.SlashAsMiddleDot,
            SpaceAroundEnglish = () => settings.SpaceAroundEnglish,
            AutomaticEnglishSpacing = () => autoSpacing,
            Punctuation = () => settings.Punctuation,
            TranslationHistory = new TranslationHistory(AppPaths.TranslationHistoryFile),
        };
        return new MeltypeSession(detector, converter, options, () => settings);
    }

    /// <summary>英数 (直接入力) か。true の間はキーをすべてアプリに渡す (Mac の「英数」キー、「かな」キーで戻す)。</summary>
    public bool Direct { get; set; }

    /// <summary>コードの入力欄ではコメント・文字列以外を直接入力する。</summary>
    private bool _codeInput;
    private readonly LineTracker _codeLine = new();
    public bool CodeInput
    {
        get => _codeInput;
        set
        {
            _codeInput = value;
            _codeLine.SetFromText("");
            _codeJapanese = false;
        }
    }

    /// <summary>コードの入力欄がターミナルか。</summary>
    public bool CodeTerminal
    {
        get => _codeTerminal;
        set
        {
            // エディターとターミナルを行き来した (VS Code など、同じ入力の本体のまま): 日本語にしていたら英数に戻す
            if (_codeTerminal != value) _codeJapanese = false;
            _codeTerminal = value;
        }
    }
    private bool _codeTerminal;

    // 半角/全角 で、コードの行でも日本語にしている (改行しても続ける。Meltype キーボードと同じ)
    private bool _codeJapanese;

    /// <summary>
    /// コードの入力欄で、コードの行を日本語にする (japanese) / 英数に戻す。半角/全角 を押したとき。
    /// コードの行 (コメント・文字列の外) を日本語にしたとき、日本語にしていたのを英数に戻したときに true。
    /// コメント・文字列の中、コードの入力欄でないときは何もせずに false (IME の ON/OFF を切り替える)。
    /// </summary>
    public bool SetCodeLine(bool japanese, string? before)
    {
        if (!CodeInput || Direct || _controller.IsComposing) return false;
        before = SyncCodeLine(before);
        if (!japanese)
        {
            if (!_codeJapanese) return false;
            _codeJapanese = false;
            Diagnostics.Log.Info("コードの行: 英数に戻す");
            return true;
        }
        if (_codeJapanese || before is not null && LineContext.ClassifyText(before) != LineKind.Code) return false;
        _codeJapanese = true;
        Diagnostics.Log.Info("コードの行: 日本語で入力 (改行しても続ける。キャレットが別の場所に移るか、もう一度押すまで)");
        return true;
    }

    /// <summary>
    /// 次に打つキーが、コードの行なので英数のままアプリに通るか (入力モードの表示に使う)。
    /// before はキャレットの前 (null なら、打ったキーから追いかけた今の行)。
    /// </summary>
    public bool CodeEnglishAt(string? before)
    {
        if (!CodeInput || Direct || _controller.IsComposing) return false;
        before = SyncCodeLine(before);
        return !_codeJapanese && LineContext.ClassifyText(before ?? "") == LineKind.Code;
    }

    /// <summary>
    /// キャレットが動いた (passedVk はアプリに通したキー。0 ならクリックなど、control は Ctrl を押していたか)。
    /// 半角/全角 で日本語にしていたら、矢印・Tab・Ctrl の操作・クリックで英数に戻す (Meltype キーボードと同じ。改行では戻さない)。
    /// ターミナルは、出力でもキャレットが動くので、キーなしの移動では戻さない。
    /// </summary>
    public void CaretMoved(int passedVk, bool control)
    {
        if (!_codeJapanese) return;
        var moved = control || passedVk is VirtualKeys.Tab or (>= 0x21 and <= 0x28) or 0x2E || !CodeTerminal && passedVk == 0;
        if (!moved) return;
        _codeJapanese = false;
        Diagnostics.Log.Info("コードの行: キャレットが別の場所に移ったので英数に戻す");
    }

    /// <summary>今の行をキャレットの前で読み直す。今の行 (分からなければ null) を返す。</summary>
    private string? SyncCodeLine(string? before)
    {
        if (before is not null) _codeLine.SetFromText(before);
        return before ?? _codeLine.Text;
    }

    /// <summary>入力欄が確定済みの文字の削除に対応しているか (Linux の IBus では、対応していないアプリがある)。false なら確定し直さない。</summary>
    public bool CanDeleteSurrounding
    {
        get => _host.CanDeleteBackward;
        set => _host.CanDeleteBackward = value;
    }

    /// <summary>変換ボックスに何か入っているか。</summary>
    public bool IsComposing => _controller.IsComposing;

    /// <summary>
    /// キーを 1 つ処理する。before / after は入力欄のキャレットの前後の文字列 (分かれば。英語とも日本語とも読める語の判定と変換の文脈に使う)。
    /// </summary>
    public SessionResult HandleKey(int vk, char? ch, bool shift, bool control, bool alt, bool command, string? before = null, string? after = null)
    {
        if (CodeInput && !_controller.IsComposing) before = SyncCodeLine(before);
        // 文字列を閉じる引用符は英数のままアプリに通す (打っている途中の日本語は確定する。空の文字列 "" を閉じるときも)
        if (CodeInput && !Direct && !control && !alt && !command &&
            ch is '"' or '\'' or '`' && _codeLine.Text is { } line &&
            LineContext.ClassifyText(line) == LineKind.String &&
            LineContext.ClassifyText(line + ch) == LineKind.Code)
        {
            var committed = _controller.IsComposing ? CommitPending() : new SessionResult(false, [], null);
            foreach (var edit in committed.Commits) _codeLine.Append(edit.Text);
            TrackCodeKey(vk, ch, false);
            return committed with { Consumed = false };
        }
        _host.Begin(ch, shift, before, after);
        var down = new KeyEvent(vk, ch ?? 0, false, false, false, Environment.TickCount64);
        // Ctrl・Option・Command と一緒のキーは、変換ボックスが空ならアプリの操作 (コピーなど) なので触らない。
        var modifier = control || alt || command;
        if (Direct || !_settings().Enabled)
        {
            return Track(_host.Result(consumed: false), vk, ch, modifier);
        }
        // 先頭か空白の直後の /command・$skill・@ファイル名 は、変換せずにそのままアプリへ渡す (#193)。
        if (!modifier && !_controller.IsComposing && ch is { } c && _settings().SigilWordsDirect && _sigil.PassesThrough(c, before))
        {
            if (CodeInput) TrackCodeKey(vk, ch, modifier);
            return Track(_host.Result(consumed: false), vk, ch, modifier);
        }
        if (CodeInput && !_codeJapanese && !_controller.IsComposing &&
            (before is null || LineContext.ClassifyText(before) == LineKind.Code))
        {
            TrackCodeKey(vk, ch, modifier);
            return Track(_host.Result(consumed: false), vk, ch, modifier);
        }
        // 英数へ切り替えるときなどに、Shift を押したことを変換ボックスにも伝える (Shift + 英字は大文字)。
        if (shift && _controller.IsComposing) Feed(new KeyEvent(VirtualKeys.LShift, 0, false, false, false, down.TimeMs));
        if (modifier && _controller.IsComposing) Feed(new KeyEvent(control ? VirtualKeys.LControl : VirtualKeys.LMenu, 0, false, false, false, down.TimeMs));

        var swallowed = Feed(down, e => !modifier && (StartsComposition(e, ch, shift) || e.Vk == VirtualKeys.Space && shift));
        // このキーをアプリに送り直した (= 使わなかった) なら、アプリに渡す。
        var consumed = swallowed && !_host.ReplayedCurrent;
        Feed(down with { IsUp = true });
        // アプリに渡したキー (変換していないときの BackSpace・矢印・Enter など) はキャレットを動かすかもしれない。
        // 前に確定した語を確定し直さない (Windows のフックの方式と同じ。消す位置がずれて関係ない文字を消さないように)
        if (!consumed && !_controller.IsComposing) ForgetLastCommit();
        if (modifier && _controller.IsComposing) Feed(new KeyEvent(control ? VirtualKeys.LControl : VirtualKeys.LMenu, 0, false, true, false, down.TimeMs));
        if (shift && _controller.IsComposing) Feed(new KeyEvent(VirtualKeys.LShift, 0, false, true, false, down.TimeMs));
        var result = _host.Result(consumed);
        if (CodeInput)
        {
            foreach (var edit in result.Commits)
            {
                for (var i = 0; i < edit.DeleteBefore; i++) _codeLine.Backspace();
                _codeLine.Append(edit.Text);
            }
            if (!consumed) TrackCodeKey(vk, ch, modifier);
        }
        return Track(result, vk, ch, modifier);
    }

    private void TrackCodeKey(int vk, char? ch, bool modifier)
    {
        if (modifier || vk is VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down or 0x21 or 0x22 or 0x23 or 0x24 or 0x2E)
            _codeLine.Invalidate();
        else if (vk == VirtualKeys.Return) _codeLine.NewLine();
        else if (vk == VirtualKeys.Back) _codeLine.Backspace();
        else if (vk == VirtualKeys.Space) _codeLine.Append(" ");
        else if (ch is { } character && !char.IsControl(character)) _codeLine.Append(character.ToString());
    }

    /// <summary>アプリに届いた文字 (確定した文字列と、使わなかったキー) を、/ $ @ の名前の判定のために追いかける。</summary>
    private SessionResult Track(SessionResult result, int vk, char? ch, bool modifier)
    {
        foreach (var edit in result.Commits)
        {
            if (edit.Text.Length > 0) _sigil.Append(edit.Text);
            else if (edit.DeleteBefore > 0) _sigil.Lose();
        }
        if (result.Consumed) return result;
        if (modifier) _sigil.Lose();
        else _sigil.OnKey(vk, ch);
        return result;
    }

    /// <summary>
    /// このセッションの外でキャレットが動いた (OS の IME がアプリに通したキー・クリック・別の入力欄に移った) とき。
    /// 前に確定した語を、次の語の文脈で確定し直さないようにする。
    /// </summary>
    public void ForgetLastCommit()
    {
        _controller.ForgetLastCommit();
        _host.ForgetCommitted();
    }

    /// <summary>フォーカスが外れたときなど。未確定の内容をそのまま確定する。</summary>
    public SessionResult CommitPending()
    {
        _host.Begin(null, false, null, null);
        _controller.CommitPending();
        _controller.ResetContext();
        // 別の入力欄に移ったかもしれない。次に打つ文字は先頭とみなす (前の文字はホストが教えてくれればそちらを使う)。
        _sigil.Start();
        return _host.Result(consumed: true);
    }

    /// <summary>
    /// 文字を伴わない・扱えないキーを、変換ボックスの状態を変えずにアプリへそのまま渡す (Consumed = false)。
    /// 不正な C ABI の引数で不正な文字列を作らないための経路。
    /// </summary>
    public SessionResult PassThrough()
    {
        _host.Begin(null, false, null, null);
        return _host.Result(consumed: false);
    }

    /// <summary>
    /// 未確定の内容を確定したうえで、元の OS イベントを 1 回だけアプリへ通す (Consumed = false)。
    /// 補助面の文字 (非 BMP) を char へ切り詰めずに渡すための経路。確定と pass-through を二重に行わない。
    /// </summary>
    public SessionResult CommitForPassThrough() => CommitForPassThrough(preserveLatinRaw: false, preserveText: false);

    /// <summary>
    /// 結合文字が直前の文字に付くよう、確定する文字列に自動空白を加えない。
    /// 結合アクセントなら、明示的に選択していない ASCII 英字の原文を優先して確定する。
    /// </summary>
    public SessionResult CommitCombiningForPassThrough(bool preserveLatinRaw) =>
        CommitForPassThrough(preserveLatinRaw, preserveText: true);

    private SessionResult CommitForPassThrough(bool preserveLatinRaw, bool preserveText)
    {
        _host.Begin(null, false, null, null);
        _controller.CommitPending(preserveLatinRaw, preserveText);
        _controller.ResetContext();
        return _host.Result(consumed: false);
    }

    /// <summary>候補ウィンドウで候補をクリックしたとき。</summary>
    public SessionResult SelectCandidate(int index)
    {
        _host.Begin(null, false, null, null);
        _controller.SelectCandidate(index);
        return _host.Result(consumed: true);
    }

    private bool Feed(KeyEvent e, Func<KeyEvent, bool>? starts = null)
    {
        var swallowed = _gate.OnKey(e, starts ?? (_ => false));
        _controller.Pump();
        return swallowed;
    }

    /// <summary>変換ボックスを開くキーか (Windows 版の MeltypeEngine.StartsComposition と同じ考え方)。</summary>
    private static bool StartsComposition(KeyEvent e, char? ch, bool shift)
    {
        if (ch is not { } c) return false;
        if (VirtualKeys.IsLetter(e.Vk) && char.IsAsciiLetter(c)) return true;
        // 句読点・かぎかっこ・長音・数字・記号 (Shift で打つものも)
        return CompositionController.StartsWithSymbol(c);
    }

    /// <summary>変換ボックスからの指示を集めて、1 回のキー入力の結果にまとめる。</summary>
    private sealed class Host : ICompositionHost
    {
        private readonly List<TextEdit> _commits = [];
        private int _pendingDelete;
        // このセッションで続けて確定した文字の終わりの部分 (確定し直しで消す文字を、DLL などで確かめられるように)
        private readonly StringBuilder _committed = new();
        private const int MaxCommitted = 256;
        private char? _char;
        private bool _shift;
        private string? _before, _after;
        private CompositionView? _view;
        private bool _hidden;

        public bool ReplayedCurrent { get; private set; }

        public bool CanDeleteBackward { get; set; } = true;

        public void Begin(char? ch, bool shift, string? before, string? after)
        {
            _commits.Clear();
            _pendingDelete = 0;
            _char = ch;
            _shift = shift;
            _before = before;
            _after = after;
            ReplayedCurrent = false;
            _hidden = false;
        }

        public SessionResult Result(bool consumed)
        {
            if (_pendingDelete > 0) Add("");
            // アプリに渡したキーの文字は、確定した文字の続きとしては分からない
            if (!consumed) ForgetCommitted();
            return new SessionResult(consumed, _commits.ToList(), _hidden ? null : _view);
        }

        public void CommitText(string text) => Add(text);

        public void ForgetCommitted() => _committed.Clear();

        private void Add(string text)
        {
            string? expect = null;
            if (_pendingDelete > 0)
            {
                if (_committed.Length >= _pendingDelete)
                {
                    expect = _committed.ToString(_committed.Length - _pendingDelete, _pendingDelete);
                    _committed.Length -= _pendingDelete;
                }
                else _committed.Clear();
            }
            _commits.Add(new TextEdit(_pendingDelete, text, expect));
            _pendingDelete = 0;
            _committed.Append(text);
            if (_committed.Length > MaxCommitted) _committed.Remove(0, _committed.Length - MaxCommitted);
        }

        public void DeleteBackward(int count) => _pendingDelete += count;

        public void Replay(KeyEvent e)
        {
            // 送り直すのは「今処理しているキー」(押したとき)。修飾キーやキーを離したことは、OS がアプリに渡すので何もしない。
            if (e.IsDown && !VirtualKeys.IsModifier(e.Vk)) ReplayedCurrent = true;
        }

        public void Replay(MouseButtonEvent e)
        {
        }

        public char? CharFromKey(KeyEvent e, bool shift) => e.Scan is > 0 and < 0x10000 ? (char)e.Scan : null;

        public bool IsShiftDown() => _shift;

        /// <summary>入力欄の文字は、キーを渡されたその場で読んでもらっている (OS が渡してきた値をそのまま使う)。</summary>
        public bool SurroundingTextIsCurrent => true;

        public void RequestSurroundingText(Action<string?, string?> callback) => callback(_before, _after);

        public void Show(CompositionView view)
        {
            _view = view;
            _hidden = false;
        }

        public void Hide()
        {
            _view = null;
            _hidden = true;
        }
    }
}
