// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>
/// 入力欄への書き込み 1 回分。キャレットの前の Expect (DeleteBefore 文字) を消してから Text を入れる (確定し直すとき以外は DeleteBefore が 0)。
/// 確定し直しは、ImeSession がフロントの渡したキャレットの前の文字と Expect を照合してから出す。フロントは照合せずに、Expect の長さ (入力欄の単位) だけ消す。
/// Current は、確定し直すときに Text の後ろに続けて入る、今確定した語 (先に今の語を入れてから前を書き換えるフロント向け)。
/// CaretBack は、この確定を入れた後、末尾から左へ戻す UTF-16 の文字数。クリックなどで入力先が移ったときは、フロント側で移動を適用しない。
/// </summary>
/// Paste は、キーを送って文字を入れるフロント (Meltype キーボード) が、貼り付けで入れるか (アプリ別設定の「確定した文字の入れ方」・改行を含む文)。
public readonly record struct TextEdit(int DeleteBefore, string Text, string? Expect = null, string? Current = null, int CaretBack = 0, bool Paste = false);

/// <summary>
/// 1 回のキー入力の結果。Consumed が false ならそのキーはアプリにそのまま渡す (Commits を入れた後で)。
/// View は変換ボックスの内容 (null なら変換ボックスを閉じる)。
/// </summary>
public sealed record SessionResult(bool Consumed, IReadOnlyList<TextEdit> Commits, CompositionView? View)
{
    /// <summary>
    /// フロントに渡す JSON にする (NativeAOT でも使えるよう手書き)。View は <see cref="ForFront"/> で作ったもの。
    /// 項目は値が無くても毎回すべて出す (null・0・false)。view と candidates だけは、出さないときに丸ごと null。
    /// 項目を足したら、各フロントが読むか使わないかを FrontProtocolTests で決める。
    /// 確定し直す (Commits の DeleteBefore が 1 以上) なら、消す文字 (Expect。ImeSession がキャレットの前と照合済み) を removeBefore で渡す。
    /// </summary>
    public string ToJson()
    {
        var builder = new StringBuilder();
        builder.Append("{\"consumed\":").Append(Consumed ? "true" : "false").Append(",\"commits\":[");
        for (var i = 0; i < Commits.Count; i++)
        {
            if (i > 0) builder.Append(',');
            var rewrite = Commits[i].DeleteBefore > 0;
            builder.Append("{\"text\":");
            AppendString(builder, Commits[i].Text);
            builder.Append(",\"removeBefore\":");
            AppendNullable(builder, rewrite ? Commits[i].Expect ?? throw new InvalidOperationException("確定し直す文字が照合されていない") : null);
            builder.Append(",\"currentWord\":");
            AppendNullable(builder, rewrite ? Commits[i].Current : null);
            builder.Append(",\"moveCaretBack\":").Append(Commits[i].CaretBack);
            builder.Append(",\"insertByPaste\":").Append(Commits[i].Paste ? "true" : "false");
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
        builder.Append(",\"clauses\":");
        AppendArray(builder, view.Clauses ?? []);
        builder.Append(",\"selectedClause\":").Append(view.SelectedClause);
        builder.Append(",\"hint\":");
        AppendString(builder, view.Hint);
        builder.Append(",\"spellingSuggestion\":");
        AppendNullable(builder, view.Suggestion);
        builder.Append(",\"typedKeys\":");
        AppendNullable(builder, view.Typed);
        builder.Append(",\"candidates\":");
        var predictions = ListsPredictions(view) == true;
        var items = predictions ? view.Predictions! : view.Candidates;
        if (items.Count == 0)
        {
            builder.Append("null}}");
            return builder.ToString();
        }
        builder.Append("{\"items\":");
        AppendArray(builder, items);
        // 候補ごとの注記を表示用の文字列にする (注記が無ければ null)
        builder.Append(",\"notes\":[");
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) builder.Append(',');
            AppendNullable(builder, predictions ? null : view.Notes?[i].ToDisplayText());
        }
        builder.Append("],\"numbered\":").Append(Numbered(view) ? "true" : "false");
        builder.Append(",\"selected\":").Append(predictions ? view.SelectedPrediction : view.SelectedIndex)
            .Append(",\"selectedMeaning\":");
        AppendNullable(builder, view.Meaning);
        // 一覧・view・result を閉じる
        builder.Append(",\"meaningDelayMs\":").Append(MeaningDelayMs).Append("}}}");
        return builder.ToString();
    }

    /// <summary>選んでいる候補で止まってから、その意味を出すまでの時間 (ミリ秒)。</summary>
    public const int MeaningDelayMs = 1500;

    /// <summary>フロントに渡した表示 (<see cref="ForFront"/>) の一覧に番号を出すか。</summary>
    public static bool Numbered(CompositionView view) => view.Numbered;

    /// <summary>フロントに渡した表示 (<see cref="ForFront"/>) の一覧が、予測変換の候補か。一覧が無ければ null。</summary>
    public static bool? ListsPredictions(CompositionView view) =>
        view.Predictions is { Count: > 0 } ? true : view.Candidates.Count > 0 ? false : null;

    /// <summary>
    /// フロントに渡す表示。どのフロントも、ここに入っているものをそのまま出す:
    /// text は確定される形 (OS が表示のまま確定しても、本体の確定と同じになる。打っている途中の英字は typed で見える)、
    /// hint は操作ガイドの設定を反映した案内、typed は見えている文字と違うときだけの打ったキー、
    /// 一覧は 1 つだけ (変換中で候補が 2 つ以上なら変換の候補、変換していなければ予測変換の候補、無ければ入力中の変換の候補)。
    /// </summary>
    /// <param name="numberKeysSelect">設定「変換中の数字キーで候補を選ぶ」。番号は、数字キーで選べる変換中の候補だけに出す。</param>
    internal static CompositionView ForFront(CompositionView view, bool showGuide, bool numberKeysSelect)
    {
        var predictions = !view.Converting && view.Predictions is { Count: > 0 };
        var candidates = !predictions && (view.Converting ? view.Candidates.Count > 1 : view.Candidates.Count > 0);
        return view with
        {
            Text = view.Committed ?? view.Text,
            Hint = showGuide ? view.Hint : view.ContextHint ?? "",
            Typed = view.Typed == view.Text ? null : view.Typed,
            Candidates = candidates ? view.Candidates : [],
            SelectedIndex = candidates ? view.SelectedIndex : -1,
            Notes = candidates ? view.Notes : null,
            Meaning = candidates ? view.Meaning : null,
            Predictions = predictions ? view.Predictions : null,
            SelectedPrediction = predictions ? view.SelectedPrediction : -1,
            Numbered = candidates && view.Converting && numberKeysSelect,
        };
    }

    private static void AppendNullable(StringBuilder builder, string? text)
    {
        if (text is null) builder.Append("null");
        else AppendString(builder, text);
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
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }
}

/// <summary>
/// 入力欄 1 つ分の Meltype の入力の本体 (<see cref="Ime.ImeSession"/> が使う)。フロントはキーを 1 つずつ渡し、
/// 「使ったか (アプリに渡さないか)」をその場で受け取る。コードの行・/command・英数状態の判断をして、残りを <see cref="CompositionController"/> に渡す。
///
/// キーは Windows の仮想キーコード (A-Z = 0x41-0x5A、Space = 0x20 …) で渡す。入力する文字は ch で渡す (キーボード配列の違いは OS 側で解決済み)。
/// 変換ボックスの表示・確定する文字は、戻り値の <see cref="SessionResult"/> で返す。1 つのスレッドから使う。
/// </summary>
public sealed class MeltypeSession
{
    private readonly CompositionController _controller;
    private readonly Func<Settings> _settings;
    private readonly Func<long> _timeMs;
    private readonly bool _directModeDetection;
    private bool _directEnglishWord;
    private bool _direct;

    public MeltypeSession(CompositionDetector detector, IKanjiConverter converter, CompositionServices services, Func<Settings> settings,
        bool directModeDetection = false, Func<long>? timeMs = null)
    {
        _settings = settings;
        _timeMs = timeMs ?? (() => Environment.TickCount64);
        _directModeDetection = directModeDetection;
        var controllerServices = directModeDetection
            ? services with
            {
                DirectMode = () => Direct,
                DirectDecided = japanese =>
                {
                    Direct = !japanese;
                    _directEnglishWord = !japanese;
                },
            }
            : services;
        _controller = new CompositionController(detector, converter, settings, controllerServices);
    }

    /// <summary>
    /// 既定の辞書・学習データ (保存場所は <see cref="AppPaths"/>) で作る。converter は OS 側の変換エンジン、
    /// moreCandidates は読みに対する候補の一覧 (無ければ null)、wordChecker は OS のスペルチェッカー (無ければ null)。
    /// </summary>
    /// <param name="settings">入力欄のアプリの設定 (アプリ別設定を反映したもの)。null なら config.json の設定。</param>
    public static MeltypeSession CreateDefault(IKanjiConverter converter, Func<string, IReadOnlyList<string>>? moreCandidates, IWordChecker? wordChecker, bool autoSpacing = false,
        bool directModeDetection = false, Func<Settings>? settings = null)
    {
        AppPaths.MigrateFromOldName();
        return CreateDefault(AppPaths.DataDirectory, converter, moreCandidates, wordChecker, autoSpacing, directModeDetection, settings);
    }

    /// <summary>既定の保存場所の定型文 (snippets.json)。入力欄で共有する。</summary>
    public static SnippetStore DefaultSnippets()
    {
        AppPaths.MigrateFromOldName();
        return s_snippets ??= new SnippetStore(AppPaths.SnippetsFile);
    }

    private static SnippetStore? s_snippets;

    /// <summary>既定の保存場所の設定 (config.json。変わったら読み直す)。CreateDefault の入力欄と同じものを返す。</summary>
    public static Settings DefaultSettings()
    {
        AppPaths.MigrateFromOldName();
        return Shared(AppPaths.DataDirectory).Settings.Current;
    }

    // 辞書・検出器・学習データは、入力欄 (セッション) がいくつあってもプロセスで 1 つだけ作って共有する (Windows 版の CompositionService.CreateSession と同じ)。
    // 入力欄ごとに作ると、学習データを保存するときにほかの入力欄で覚えたものを上書きし、辞書も入力欄の数だけメモリに持つ。
    // Input Method Kit・fcitx5・IBus はどれも 1 つのスレッド (メインループ) から呼ぶので、排他はしない。
    private static (string Directory, CompositionDetector Detector, CompositionServices Services, SettingsFile Settings)? s_shared;

    // #123 の候補で調べた GitHub のタイトル (設定「#番号で GitHub の Issue・PR のタイトルを候補に出す」)。セッションの間だけ覚える
    private static readonly GitHubIssueTitles s_githubTitles = new();

    /// <summary>dataDirectory に置いた設定・辞書・学習データで作る (テストでは一時フォルダーを渡す)。</summary>
    internal static MeltypeSession CreateDefault(string dataDirectory, IKanjiConverter converter, Func<string, IReadOnlyList<string>>? moreCandidates, IWordChecker? wordChecker, bool autoSpacing = false,
        bool directModeDetection = false, Func<Settings>? appSettings = null)
    {
        var shared = Shared(dataDirectory);
        var userDirectory = Path.Combine(dataDirectory, "dictionaries");
        var file = shared.Settings;
        var settings = appSettings ?? (() => file.Current);
        // 入力欄を作ったときにも読み直す (ログの設定を、入力の前に入れる)
        _ = settings();
        var detector = shared.Detector;
        // OS のスペルチェッカーが無ければ (Linux)、同梱のよく使う英単語の一覧を使う (meeting を英語と分かるように)。
        detector.SpellChecker = wordChecker is { IsAvailable: true } ? wordChecker : Detection.BuiltInWordChecker.Shared;
        // ユーザー辞書は入力欄を作るたびに読み直す (Mac は辞書を取り込んだ後に入力欄を作り直して反映する)。
        // dictionaries/ に置いた macOS の「ユーザ辞書」の .plist も読む (#40)
        var userDictionary = new UserDictionary(Path.Combine(dataDirectory, "userdict.txt"), importDirectory: userDirectory);
        var services = shared.Services with
        {
            UserDictionary = userDictionary,
            Predictor = new Predictor(shared.Services.Predictor?.Phrases, userDictionary, shared.Services.History),
            MoreCandidates = moreCandidates,
            GitHubTitles = () => s_githubTitles,
            AutomaticEnglishSpacing = () => autoSpacing,
        };
        return new MeltypeSession(detector, converter, services, settings, directModeDetection);
    }

    private static (string Directory, CompositionDetector Detector, CompositionServices Services, SettingsFile Settings) Shared(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var userDirectory = Path.Combine(dataDirectory, "dictionaries");
        if (s_shared is not { } shared || shared.Directory != dataDirectory)
        {
            SettingsSchema.Write(dataDirectory);
            // 設定は config.json が変わったら読み直す (入力欄を開いたままでも効く)。入力欄で共有する
            var settingsFile = new SettingsFile(Path.Combine(dataDirectory, "config.json"), loaded =>
            {
                // 設定で「ファイルにログを書く」を ON にしていれば、Mac でも meltype.log に書く (動かないときの調査用)。
                Diagnostics.Log.SetFileOutput(loaded.FileLog ? Path.Combine(dataDirectory, "meltype.log") : null);
                Diagnostics.Log.RecordText = loaded.LogTypedText;
            });
            var created = CompositionDetector.CreateDefault(userDirectory);
            created.UseJapanesePunctuationAfterEnglish = () => settingsFile.Current.JapanesePunctuationAfterEnglish;
            var languages = new LanguageMemory(Path.Combine(dataDirectory, "languages.json"));
            created.Memory = languages;
            shared = (dataDirectory, created, new CompositionServices
            {
                Candidates = CandidateDictionary.Load(userDirectory),
                ContextRules = ContextRules.Load(userDirectory),
                History = new ConversionHistory(Path.Combine(dataDirectory, "conversions.json")),
                Misspellings = MisspellingDictionary.Load(userDirectory),
                Languages = languages,
                Translations = TranslationDictionary.Load(),
                Meanings = MeaningDictionary.Load(),
                RomajiTypos = RomajiTypoCorrector.Load(created.Romaji),
                TranslationHistory = new TranslationHistory(Path.Combine(dataDirectory, "translations.json")),
                // 予測変換のために覚えた語句 (入力欄で共有する。ユーザー辞書は入力欄ごとの Predictor に渡す)
                Predictor = new Predictor(new PhraseHistory(Path.Combine(dataDirectory, "phrases.txt")), null, null),
            }, settingsFile);
            s_shared = shared;
        }
        return shared;
    }

    /// <summary>英数 (直接入力) か。true の間はキーをすべてアプリに渡す (Mac の「英数」キー、「かな」キーで戻す)。</summary>
    public bool Direct
    {
        get => _direct;
        set
        {
            var wasDirect = _direct;
            _direct = value;
            if (!value)
            {
                _directEnglishWord = false;
                if (wasDirect && _directModeDetection) _controller.AbandonDirectHold();
            }
        }
    }

    public bool DirectEnglishWord => _directEnglishWord;

    /// <summary>入力を覚えないように求める入力欄か (Windows のブラウザーのシークレット / InPrivate ウィンドウ)。true の間は学習しない。</summary>
    public bool Private
    {
        get => _controller.Private;
        set => _controller.Private = value;
    }

    /// <summary>
    /// コードの入力欄ではコメント・文字列以外を直接入力する。コメント・文字列の中かは、フロントが渡すキャレットの前の文字 (before) だけで決め、
    /// 打ったキーから行を追いかけない。
    /// </summary>
    private bool _codeInput;
    public bool CodeInput
    {
        get => _codeInput;
        set
        {
            _codeInput = value;
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
        if (!japanese)
        {
            if (!_codeJapanese) return false;
            _codeJapanese = false;
            Diagnostics.Log.Info("コードの行: 英数に戻す");
            return true;
        }
        if (_codeJapanese || CodeLineKind(before) != LineKind.Code) return false;
        _codeJapanese = true;
        Diagnostics.Log.Info("コードの行: 日本語で入力 (改行しても続ける。キャレットが別の場所に移るか、もう一度押すまで)");
        return true;
    }

    /// <summary>
    /// 次に打つキーが、コードの行なので英数のままアプリに通るか (入力モードの表示に使う)。
    /// before はキャレットの前。
    /// </summary>
    public bool CodeEnglishAt(string? before)
    {
        if (!CodeInput || Direct || _controller.IsComposing) return false;
        return !_codeJapanese && CodeLineKind(before) == LineKind.Code;
    }

    /// <summary>
    /// キャレットが動いた (passedVk はアプリに通したキー。0 ならクリックなど、control は Ctrl を押していたか)。
    /// 半角/全角 で日本語にしていたら、矢印・Tab・Ctrl の操作・クリックで英数に戻す (Meltype キーボードと同じ。改行では戻さない)。
    /// アプリに通した Esc (vim などの操作に移る) でも戻す。ターミナルは、出力でもキャレットが動くので、キーなしの移動では戻さない。
    /// </summary>
    public void CaretMoved(int passedVk, bool control)
    {
        if (!_codeJapanese) return;
        var moved = control || passedVk is VirtualKeys.Tab or VirtualKeys.Escape or (>= 0x21 and <= 0x28) or 0x2E || !CodeTerminal && passedVk == 0;
        if (moved) ForgetCodeLine();
    }

    /// <summary>コードの行を日本語にしていたのをやめる (キャレットが別の場所に移った・フォーカスが外れた)。</summary>
    public void ForgetCodeLine()
    {
        if (!_codeJapanese) return;
        _codeJapanese = false;
        Diagnostics.Log.Info("コードの行: キャレットが別の場所に移ったので英数に戻す");
    }

    /// <summary>キャレットの位置の種類。キャレットの前が分からない入力欄 (周辺文字列を返さないターミナル) はコードの行とする (日本語にするには SetCodeLine)。</summary>
    private static LineKind CodeLineKind(string? before) => before is null ? LineKind.Code : LineContext.ClassifyText(before);

    /// <summary>入力欄が確定済みの文字の削除に対応しているか (Linux の IBus では、対応していないアプリがある)。false なら確定し直さない。</summary>
    public bool CanDeleteSurrounding
    {
        get => _controller.CanDeleteBackward;
        set => _controller.CanDeleteBackward = value;
    }

    /// <summary>変換ボックスに何か入っているか。</summary>
    public bool IsComposing => _controller.IsComposing;

    /// <summary>変換して候補を選んでいるか。</summary>
    public bool IsConverting => _controller.IsConverting;

    /// <summary>入力中に Ctrl と一緒に押すと、変換ボックスで使う英字のキー。</summary>
    public IReadOnlyList<int> ComposingControlKeys() => _controller.ComposingControlKeys();

    /// <summary>ユーザーが判定を直した (英字 / かなに直した語。トレイの「直した誤判定を報告...」)。</summary>
    public event Action<Correction>? Corrected
    {
        add => _controller.Corrected += value;
        remove => _controller.Corrected -= value;
    }

    /// <summary>入力欄で選んでいる文字 (変換キーで再変換する。フロントが送ってきたもの)。</summary>
    public string? Selection
    {
        get => _controller.Selection;
        set => _controller.Selection = value;
    }

    /// <summary>漢字かな交じりの文の読み (再変換。OS の機能)。分からなければ null。</summary>
    public Func<string, string?> ReadingOf
    {
        set => _controller.ReadingOf = value;
    }

    /// <summary>クリップボードの文字 (OS の機能)。読めなければ null。</summary>
    public Func<string?> ClipboardText
    {
        set => _controller.Clipboard = value;
    }

    /// <summary>今再生している曲 (OS の機能)。読めなければ null。</summary>
    public Func<(string Title, string Artist)?> NowPlaying
    {
        set => _controller.NowPlaying = value;
    }

    /// <summary>
    /// キーを 1 つ処理する。before / after は入力欄のキャレットの前後の文字列 (分かれば。英語とも日本語とも読める語の判定と変換の文脈に使う)。
    /// 入力モードを切り替えるキーの意味は ImeSession が決めるので、ここでは見ない。
    /// </summary>
    /// <param name="releaseFollows">ワークアラウンド (ImeEventType.KeyReleased): 離したことは <see cref="HandleKeyUp"/> で届く。false なら押した直後に離したとみなす。</param>
    public SessionResult HandleKey(int vk, char? ch, bool shift, bool control, bool alt, bool command, string? before = null, string? after = null,
        bool releaseFollows = false)
    {
        // 文字列を閉じる引用符は英数のままアプリに通す (打っている途中の日本語は確定する。空の文字列 "" を閉じるときも)。
        // 変換中の before は、変換中の文字より前 (フロントが渡す)
        if (CodeInput && !Direct && !control && !alt && !command &&
            ch is '"' or '\'' or '`' && before is { } line &&
            LineContext.ClassifyText(line) == LineKind.String &&
            LineContext.ClassifyText(line + ch) == LineKind.Code)
        {
            var committed = _controller.IsComposing ? CommitPending() : new SessionResult(false, [], null);
            return committed with { Consumed = false };
        }
        var now = _timeMs();
        // Ctrl・Option・Command と一緒のキーは、変換ボックスが空ならアプリの操作 (コピーなど) なので触らない。
        var modifier = control || alt || command;
        var settings = _settings();
        if (Direct && _directModeDetection && _directEnglishWord &&
            (ch is { } directChar && !char.IsAsciiLetterOrDigit(directChar) && directChar is not ('@' or '_') ||
             before is { Length: > 0 } && !char.IsAsciiLetterOrDigit(before[^1]) && before[^1] is not ('@' or '_')))
            _directEnglishWord = false;
        if ((Direct && (!_directModeDetection || !settings.DirectModeAutoDetect || _directEnglishWord)) || !settings.Enabled)
        {
            return _controller.Pass(now);
        }
        // 先頭か空白の直後の /command・$skill・@ファイル名 は、変換せずにそのままアプリへ渡す (#193)。
        // 文章の入力欄の / は、変換ボックスに ・ として入れる。Space で / にすると、続く名前はそのまま渡す。
        if (!modifier && !_controller.IsComposing && ch is '/' && !PassesSlash(before) && !SigilWord.InNameAfter(before) && settings.SigilWordsDirect &&
            SigilWord.PassesAfter(before, '/'))
        {
            _controller.MarkSlashAtStart();
        }
        else if (!modifier && !_controller.IsComposing && ch is { } c && settings.SigilWordsDirect && SigilWord.PassesAfter(before, c))
        {
            return _controller.Pass(now);
        }
        if (CodeInput && !_codeJapanese && !_controller.IsComposing && CodeLineKind(before) == LineKind.Code)
        {
            return _controller.Pass(now);
        }
        var result = _controller.Press(new KeyPress(vk, ch, shift, control, alt, command, now), before, after, releaseFollows);
        // アプリに渡したキー (変換していないときの BackSpace・矢印・Enter など) はキャレットを動かすかもしれない。
        // 前に確定した語を確定し直さない (消す位置がずれて関係ない文字を消さないように)
        if (!result.Consumed && !_controller.IsComposing) ForgetLastCommit();
        return result;
    }

    /// <summary>
    /// ワークアラウンド (ImeEventType.KeyReleased): releaseFollows を付けて押したキーを離した。親指シフトの親指キー・中指NICOLA のシフトのキーを離したことを、
    /// かな配列に伝える (文字に使わずに離した親指キーは、もとの変換・無変換をする)。離したことをアプリに通すかはフロントが押下のとおりに決める。
    /// </summary>
    public SessionResult HandleKeyUp(int vk, string? before, string? after)
    {
        return _controller.Release(vk, before, after);
    }

    /// <summary>
    /// 変換・無変換・ひらがな/カタカナ キー。設定「入力中のキーの役割を分ける」が ON で入力中なら、変換ボックスで処理する (issue #199)。
    /// そうでなければ null (ImeSession が英数 / 日本語の切り替えに使う)。
    /// </summary>
    public SessionResult? HandleRoleKey(int vk)
    {
        if (vk is not (VirtualKeys.Convert or VirtualKeys.NonConvert or VirtualKeys.Kana)) return null;
        if (!_settings().SeparateKeyRoles || !_controller.IsComposing || Direct) return null;
        return HandleKey(vk, null, false, false, false, false);
    }

    /// <summary>
    /// 打ち始めの / を、・ にせずそのままアプリへ渡すか。コードの入力欄で、キャレットがコードの中か、
    /// ターミナルの AI の入力行 (Claude Code・Codex の /command を打つ所) にあるとき。
    /// コメント・文字列の中は ・ にする (Meltype キーボードと同じ)。
    /// </summary>
    private bool PassesSlash(string? before) =>
        CodeInput && !_codeJapanese && CodeLineKind(before) is LineKind.Code or LineKind.Prompt;



    /// <summary>
    /// このセッションの外でキャレットが動いた (OS の IME がアプリに通したキー・クリック・別の入力欄に移った) とき。
    /// 前に確定した語を、次の語の文脈で確定し直さないようにする。
    /// </summary>
    public void ForgetLastCommit()
    {
        _controller.ForgetLastCommit();
    }

    public void ForgetDirectInput()
    {
        _directEnglishWord = false;
        _controller.AbandonDirectHold();
    }

    /// <summary>フォーカスが外れたときなど。未確定の内容をそのまま確定する。</summary>
    public SessionResult CommitPending()
    {
        var result = _controller.CommitPending();
        _controller.ResetContext();
        return result;
    }

    /// <summary>
    /// 文字を伴わない・扱えないキーを、変換ボックスの状態を変えずにアプリへそのまま渡す (Consumed = false)。
    /// 不正な C ABI の引数で不正な文字列を作らないための経路。
    /// </summary>
    public SessionResult PassThrough() => _controller.Pass(_timeMs());

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
        var result = _controller.CommitPending(consumed: false, preserveLatinRaw, preserveText);
        _controller.ResetContext();
        return result;
    }

    /// <summary>背景の変換の結果が届いたとき (IBackgroundConverter.ResultReady を入力スレッドに移して呼ぶ)。表示が変わらないなら null (入力していない・候補を開いている)。</summary>
    /// <summary>入力の内容を変えない出来事 (フォーカス・キャレットの知らせなど) の結果。表示は今のまま (変換中ならその表示)。</summary>
    public SessionResult Unchanged() => _controller.Current();

    public SessionResult? RefreshConversion() => _controller.RefreshConversion();

    /// <summary>候補ウィンドウで候補をクリックしたとき。</summary>
    public SessionResult SelectCandidate(int index) => _controller.SelectCandidate(index);

    /// <summary>候補の一覧で予測変換の候補をクリックしたとき。その候補を確定する。</summary>
    public SessionResult SelectPrediction(int index) => _controller.CommitPredictionAt(index);
}
