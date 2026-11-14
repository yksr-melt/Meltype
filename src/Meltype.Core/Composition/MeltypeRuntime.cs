// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;
using Meltype.Detection;
using Meltype.NowPlaying;

namespace Meltype.Composition;

/// <summary>
/// プロセスで 1 つの入力の土台: 設定・判定器・辞書・学習データ・定型文。どの OS のアプリもこれを 1 つ作り、
/// 入力欄ごとの本体 (<see cref="MeltypeSession"/>) は <see cref="CreateSession"/> で作る。
/// 入力欄ごとに辞書・学習データを作ると、学習データを保存するときにほかの入力欄で覚えたものを上書きし、辞書も入力欄の数だけメモリに持つ。
/// </summary>
public sealed class MeltypeRuntime : IDisposable
{
    private readonly string _userDirectory;
    private readonly IWordChecker _wordChecker;
    private readonly CompositionServices _shared;
    private readonly Lazy<ScoreEngine> _directEngine;
    private readonly INowPlayingSource? _nowPlaying;
    private readonly List<IDisposable> _converters = [];
    // 入力していない間も config.json の書き換えを読む (読み直したら SettingsChanged を出す)
    private readonly Timer _settingsCheck;

    /// <param name="dataDirectory">設定・辞書・学習データの置き場所 (<see cref="AppPaths.DataDirectory"/>。テストでは一時フォルダー)。</param>
    /// <param name="wordChecker">OS のスペルチェッカー。使えなければ同梱のよく使う英単語の一覧を使う (meeting を英語と分かるように)。</param>
    /// <param name="nowPlaying">再生中の曲を OS から読み始める (テストでは読まない)。</param>
    public MeltypeRuntime(string dataDirectory, IWordChecker? wordChecker = null, bool nowPlaying = false)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(dataDirectory);
        _userDirectory = Path.Combine(dataDirectory, "dictionaries");
        _wordChecker = wordChecker is { IsAvailable: true } ? wordChecker : BuiltInWordChecker.Shared;
        SettingsSchema.Write(dataDirectory);
        Settings = new SettingsFile(Path.Combine(dataDirectory, "config.json"), loaded =>
        {
            Diagnostics.Log.SetFileOutput(loaded.FileLog ? Path.Combine(dataDirectory, "meltype.log") : null);
            Diagnostics.Log.RecordText = loaded.LogTypedText;
            SettingsChanged?.Invoke(loaded);
        });
        // ログの設定を、入力の前に入れる
        _ = Settings.Current;
        Detector = CompositionDetector.CreateDefault(_userDirectory);
        Detector.SpellChecker = _wordChecker;
        Detector.UseJapanesePunctuationAfterEnglish = () => Settings.Current.JapanesePunctuationAfterEnglish;
        var languages = new LanguageMemory(Path.Combine(dataDirectory, "languages.json"));
        Detector.Memory = languages;
        var history = new ConversionHistory(Path.Combine(dataDirectory, "conversions.json"));
        _shared = new CompositionServices
        {
            Candidates = CandidateDictionary.Load(_userDirectory),
            ContextRules = ContextRules.Load(_userDirectory),
            History = history,
            Misspellings = MisspellingDictionary.Load(_userDirectory),
            Languages = languages,
            Translations = TranslationDictionary.Load(),
            Meanings = MeaningDictionary.Load(),
            RomajiTypos = RomajiTypoCorrector.Load(Detector.Romaji),
            TranslationHistory = new TranslationHistory(Path.Combine(dataDirectory, "translations.json")),
            GitHubTitles = () => GitHubTitles,
        };
        Phrases = new PhraseHistory(Path.Combine(dataDirectory, "phrases.txt"));
        // dictionaries/ に置いた macOS の「ユーザ辞書」の .plist も読む (#40)
        UserDictionary = new UserDictionary(Path.Combine(dataDirectory, "userdict.txt"), importDirectory: _userDirectory);
        Snippets = new SnippetStore(Path.Combine(dataDirectory, "snippets.json"));
        _directEngine = new(() => ScoreEngine.CreateDefault(() => Settings.Current, _userDirectory));
        if (nowPlaying) _nowPlaying = NowPlayingSource.Create(() => Settings.Current.NowPlayingCandidates);
        _settingsCheck = new Timer(_ => _ = Settings.Current, null, SettingsFile.CheckIntervalMs, SettingsFile.CheckIntervalMs);
    }

    /// <summary>既定の保存場所 (<see cref="AppPaths.DataDirectory"/>) で作る。旧名 (AutoIME) の保存場所があれば、先に移す。</summary>
    public static MeltypeRuntime OpenDefault(IWordChecker? wordChecker = null, bool nowPlaying = true)
    {
        AppPaths.MigrateFromOldName();
        return new MeltypeRuntime(AppPaths.DataDirectory, wordChecker, nowPlaying);
    }

    public string DataDirectory { get; }

    /// <summary>config.json の設定 (変わったら読み直す。同じプロセスの設定画面は Apply で変える)。</summary>
    internal SettingsFile Settings { get; }

    /// <summary>設定を読み直した・変えた。設定の lock の中で、読んだ・変えたスレッドから呼ぶので、受け手は待たない (UI は BeginInvoke で触る)。</summary>
    public event Action<Settings>? SettingsChanged;

    public CompositionDetector Detector { get; }

    /// <summary>
    /// ユーザー辞書。入力欄を作ったとき・入力を始めたときに、外で変わっていたら読み直す (<see cref="UserDictionary.Refresh"/>)。
    /// 入力欄の本体 (<see cref="MeltypeSession"/>) と同じスレッドからだけ使う (Windows は UI スレッド。排他しない)。
    /// </summary>
    public UserDictionary UserDictionary { get; }

    public ConversionHistory History => _shared.History!;

    public LanguageMemory Languages => _shared.Languages!;

    public TranslationHistory TranslationHistory => _shared.TranslationHistory!;

    /// <summary>予測変換のために覚えた、確定した語句。</summary>
    public PhraseHistory Phrases { get; }

    /// <summary>登録した定型文 (;名前 + Space で入れる: issue #290)。</summary>
    public SnippetStore Snippets { get; }

    /// <summary>#123 の候補で調べた GitHub のタイトル (設定「#番号で GitHub の Issue・PR のタイトルを候補に出す」)。プロセスの間だけ覚える。</summary>
    public GitHubIssueTitles GitHubTitles { get; } = new();

    /// <summary>
    /// 変換エンジン (Mozc と、設定で ON なら Zenzai)。プロセスで 1 つ作り、入力欄の本体で共有する。止めるのは <see cref="Dispose"/>。
    /// appDirectory は、同梱の Mozc のヘルパー (mozc/meltype_mozc_helper) と Zenzai (zenzai/) を置いたフォルダー。
    /// </summary>
    public HybridConverter CreateConverter(string appDirectory)
    {
        // 強制終了された Meltype の llama-server は、Zenzai が OFF でも起動したときに終わらせる
        ZenzaiConverter.EndStaleServers();
        var helper = Path.Combine(appDirectory, "mozc", OperatingSystem.IsWindows() ? "meltype_mozc_helper.exe" : "meltype_mozc_helper");
        var mozc = new MozcConverter(helper, Path.Combine(DataDirectory, "mozc"));
        var zenzai = new ZenzaiHolder();
        _converters.Add(mozc);
        _converters.Add(zenzai);
        ZenzaiStatus = () => Settings.Current.UseZenzai ? zenzai.Status ?? "まだ起動していません (日本語を変換すると起動します)" : null;
        return new HybridConverter(mozc, () => Settings.Current is { UseZenzai: true } settings
            ? zenzai.Get(ZenzaiBundle.ServerPath(settings.ZenzaiServerPath, appDirectory), ZenzaiBundle.ModelPath(settings.ZenzaiModelPath, appDirectory), settings.ZenzaiDevice)
            : zenzai.Get("", ""), zenzai.Failed);
    }

    /// <summary>Zenzai の今の状態 (トレイのメニューに出す。issue #1152)。OFF・変換エンジンを作っていなければ null。</summary>
    public Func<string?> ZenzaiStatus { get; private set; } = () => null;

    /// <summary>
    /// 入力欄 1 つ分の本体を作る。converter は <see cref="CreateConverter"/> で作った変換エンジン (テストでは偽物)。
    /// autoSpacing は、つなげて打った英語の決まり文句 (seeyou) に空白を入れるか (今は Mac だけ)。
    /// directModeDetection なら、英数状態で打ち始めた語がローマ字かを判定して日本語に戻す (設定「英数状態で自動判定」。今は Windows だけ)。
    /// </summary>
    /// <param name="settings">入力欄のアプリの設定 (アプリ別設定を反映したもの)。null なら config.json の設定。</param>
    public MeltypeSession CreateSession(IKanjiConverter converter, Func<Settings>? settings = null, Func<string, IReadOnlyList<string>>? moreCandidates = null,
        bool autoSpacing = false, bool directModeDetection = false)
    {
        var current = settings ?? (() => Settings.Current);
        UserDictionary.Refresh();
        var services = _shared with
        {
            UserDictionary = UserDictionary,
            Predictor = new Predictor(Phrases, UserDictionary, History),
            MoreCandidates = moreCandidates,
            AutomaticEnglishSpacing = () => autoSpacing,
            // Mac・Linux では判定しない。判定すると、判定中の英字があるときにかなキーで日本語に切り替わらない (#1724)
            ClassifyDirect = directModeDetection && OperatingSystem.IsWindows() ? (letters, final) => ClassifyDirect(letters, final, current()) : null,
        };
        var session = new MeltypeSession(Detector, converter, services, current, directModeDetection) { NowPlaying = () => _nowPlaying?.Read() };
        if (converter is HybridConverter hybrid) session.ReadingOf = hybrid.Reading;
        return session;
    }

    public void Dispose()
    {
        _settingsCheck.Dispose();
        _nowPlaying?.Dispose();
        foreach (var converter in _converters) converter.Dispose();
    }

    /// <summary>学習データ (選び直した変換・英字 / かなに直した語・予測変換の語句・選んだ英訳) をすべて消す。ユーザー辞書と設定は消さない。</summary>
    public void ResetLearning()
    {
        History.Clear();
        Languages.Clear();
        Phrases.Clear();
        TranslationHistory.Clear();
        Diagnostics.Log.Info("学習データをリセットしました。");
    }

    /// <summary>
    /// sinceUtc より後に覚えたものだけを消す (一時的な入力・間違えて覚えたものを、全体をリセットせずに消す: issue #277)。消した数を返す。
    /// 選び直した変換・英字 / かなに直した語・予測変換の語句が対象。英訳の記録は覚えた時刻を持たないので、<see cref="ResetLearning"/> で消す。
    /// </summary>
    public int ForgetLearningSince(DateTime sinceUtc) => History.ForgetSince(sinceUtc) + Languages.ForgetSince(sinceUtc) + Phrases.ForgetSince(sinceUtc);

    /// <summary>英字 (letters) がローマ字 (日本語) か英語かの判定 (設定画面の判定のテストにも使う)。final は、もう続きが無いか。</summary>
    public DetectionResult Evaluate(string letters, bool final, Settings settings) =>
        _directEngine.Value.Evaluate(new DetectionInput(letters, letters.Select(c => (int)char.ToUpperInvariant(c)).ToArray(), final), settings);

    /// <summary>英数状態で打ち始めた英字 (letters) がローマ字 (日本語) かを判定する。final は、もう続きが無いか。</summary>
    public Verdict ClassifyDirect(string letters, bool final, Settings settings)
    {
        var result = Evaluate(letters, final, settings);
        // 同梱の辞書にない英単語 (debate, potato) はローマ字としても読めるので、スペルチェッカーの語なら日本語にしない。
        if (result.Verdict == Verdict.Japanese && letters.Length >= 4 && _wordChecker.IsWord(letters.ToLowerInvariant()))
        {
            Diagnostics.Log.Info($"英数状態: {Diagnostics.Log.Text(letters)}は英単語 (スペルチェッカー) なので日本語にしない");
            return final ? Verdict.English : Verdict.Undecided;
        }
        if (result.Verdict == Verdict.Japanese) Diagnostics.Log.Decision($"英数状態でローマ字を検知: {result.Describe()}");
        return result.Verdict;
    }
}
