// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Composition;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Diagnostics;
using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Input;
using Meltype.Learning;
using Meltype.Libraries.Chars;
using Meltype.Composition.Utility.NowPlaying;

namespace Meltype.Ime;

/// <summary>
/// プロセスで 1 つの入力の土台: 設定・判定器・辞書・学習データ・定型文。どの OS のアプリもこれを 1 つ作り、
/// 変換エンジンも 1 つだけ持ち (<see cref="StartConverter"/>)、入力欄ごとの本体 (<see cref="Ime.ImeHost"/>) は <see cref="CreateHost"/> で作る。
/// 入力欄ごとに辞書・学習データを作ると、学習データを保存するときにほかの入力欄で覚えたものを上書きし、辞書も入力欄の数だけメモリに持つ。
/// </summary>
public sealed class MeltypeRuntime : IDisposable
{
    private readonly string _userDirectory;
    private readonly CompositionServices _shared;
    private readonly Lazy<ScoreEngine> _directEngine;
    private readonly INowPlayingSource? _nowPlaying;
    private readonly List<IDisposable> _converters = [];
    // 入力していない間も、ほかのプロセス (設定アプリ・エディター) による config.json・学習データ・定型文の書き換えを読む
    // (config.json を読み直したら SettingsChanged を出す)
    private readonly Timer _settingsCheck;

    /// <param name="dataDirectory">設定・辞書・学習データの置き場所 (<see cref="AppPaths.DataDirectory"/>。テストでは一時フォルダー)。</param>
    /// <param name="nowPlaying">再生中の曲を OS から読み始める (テストでは読まない)。</param>
    public MeltypeRuntime(string dataDirectory, bool nowPlaying = false)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(dataDirectory);
        _userDirectory = Path.Combine(dataDirectory, "dictionaries");
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
        Learning = new LearningData(dataDirectory);
        _shared = new CompositionServices
        {
            Candidates = CandidateDictionary.Load(_userDirectory),
            ContextRules = ContextRules.Load(_userDirectory),
            History = Learning.History,
            Misspellings = MisspellingDictionary.Load(_userDirectory),
            Languages = Learning.LanguageMemory,
            FieldModes = Learning.FieldModes,
            Translations = TranslationDictionary.Load(),
            Meanings = MeaningDictionary.Load(),
            Lexicon = JapaneseLexicon.Load(Detector.Romaji),
            TranslationHistory = Learning.Translations,
            GitHubTitles = () => GitHubTitles,
        };
        Connect(Detector, _shared);
        // dictionaries/ に置いた macOS の「ユーザ辞書」の .plist も読む (#40)
        UserDictionary = new UserDictionary(Path.Combine(dataDirectory, "userdict.txt"), importDirectory: _userDirectory);
        Snippets = new SnippetStore(Path.Combine(dataDirectory, "snippets.json"));
        _directEngine = new(() => ScoreEngine.CreateDefault(() => Settings.Current, _userDirectory));
        if (nowPlaying) _nowPlaying = NowPlayingSource.Create(() => Settings.Current.NowPlayingCandidates);
        _settingsCheck = new Timer(_ =>
        {
            _ = Settings.Current;
            Learning.Refresh();
            Snippets.Refresh();
        }, null, SettingsFile.CheckIntervalMs, SettingsFile.CheckIntervalMs);
    }

    /// <summary>既定の保存場所 (<see cref="AppPaths.DataDirectory"/>) で作る。旧名 (AutoIME) の保存場所があれば、先に移す。</summary>
    public static MeltypeRuntime OpenDefault(bool nowPlaying = true)
    {
        AppPaths.MigrateFromOldName();
        return new MeltypeRuntime(AppPaths.DataDirectory, nowPlaying);
    }

    public string DataDirectory { get; }

    /// <summary>config.json の設定 (変わったら読み直す。同じプロセスの設定画面は Apply で変える)。</summary>
    public SettingsFile Settings { get; }

    /// <summary>設定を読み直した・変えた。設定の lock の中で、読んだ・変えたスレッドから呼ぶので、受け手は待たない (UI は BeginInvoke で触る)。</summary>
    public event Action<Settings>? SettingsChanged;

    internal CompositionDetector Detector { get; }

    /// <summary>
    /// ユーザー辞書。入力欄を作ったとき・入力を始めたときに、外で変わっていたら読み直す (<see cref="UserDictionary.Refresh"/>)。
    /// 入力欄の本体 (<see cref="Ime.ImeHost"/>) と同じスレッドからだけ使う (Windows は UI スレッド。排他しない)。
    /// </summary>
    public UserDictionary UserDictionary { get; }

    /// <summary>学習データ (選び直した変換・英字 / かなに直した語・予測変換の語句・選んだ英訳)。</summary>
    public LearningData Learning { get; }

    internal ConversionHistory History => Learning.History;

    internal LanguageMemory Languages => Learning.LanguageMemory;

    internal TranslationHistory TranslationHistory => Learning.Translations;

    /// <summary>予測変換のために覚えた、確定した語句。</summary>
    internal PhraseHistory Phrases => Learning.Phrases;

    /// <summary>登録した定型文 (;名前 + Space で入れる: issue #290)。</summary>
    public SnippetStore Snippets { get; }

    /// <summary>#123 の候補で調べた GitHub のタイトル (設定「#番号で Issue・PR のタイトルを出す GitHub のリポジトリ」)。プロセスの間だけ覚える。</summary>
    internal GitHubIssueTitles GitHubTitles { get; } = new();

    /// <summary>
    /// 入力欄の本体が使う変換エンジン (Mozc と、設定で ON なら Zenzai) を用意する。<see cref="CreateHost"/> の前に 1 回呼ぶ。止めるのは <see cref="Dispose"/>。
    /// appDirectory は、同梱の Mozc のヘルパー (mozc/meltype_mozc_helper) と Zenzai (zenzai/) を置いたフォルダー。
    /// mozc は、ヘルパーを起動せずにアプリのプロセスの Mozc を使うとき (Android)。Mozc を使えれば true (使えなければ変換はかなのまま)。
    /// </summary>
    public bool StartConverter(string appDirectory, IMozcHelper? mozc = null)
    {
        var converter = CreateConverter(appDirectory, mozc);
        _converter = converter;
        converter.WarmUp();
        return converter.MozcInstalled;
    }

    private HybridConverter? _converter;

    private HybridConverter Converter => _converter ?? throw new InvalidOperationException("変換エンジンを先に用意してください (StartConverter)");

    /// <summary>変換エンジンの答えが後から届いた (入力欄に refresh を送る)。ワーカーのスレッドで呼ぶ。<see cref="StartConverter"/> の後に登録する。</summary>
    public event Action? ConversionReady
    {
        add => Converter.ResultReady += value;
        remove => Converter.ResultReady -= value;
    }

    /// <summary>
    /// 入力欄 1 つの本体を作る。辞書・学習データ・変換エンジンは、ほかの入力欄と共有する。
    /// lateResults は、フロントが変換エンジンの答えを後から受け取れるか (<see cref="ConversionReady"/> で refresh を送る)。corrected は、ユーザーが判定を直したとき。
    /// </summary>
    /// <param name="countKeys">受け持った打鍵を数える (<see cref="StartKeyCount"/> を呼んだとき)。フックで数える Windows では付けない。</param>
    public ImeHost CreateHost(IImeEnvironment environment, Func<Settings> settings, Func<bool> lateResults, Action<Correction>? corrected = null, bool countKeys = false) =>
        new(environment, SessionParts(Converter, Converter.Candidates, Converter.Reading, corrected) with { LateResults = lateResults }, settings, countKeys ? CountKey : null);

    private KeyCounter? _keyCounter;
    private Timer? _keyCountSave;
    private DiscordPresence? _discordPresence;

    /// <summary>
    /// 打鍵数 (keycount.json) を読み、設定「打鍵数」が「数えて Discord に表示する」の間は Discord に出す。プロセスで 1 回だけ呼ぶ。
    /// keycount.json は丸ごと書き直すので、同時にいくつも動くプロセス (端末) では呼ばない。
    /// </summary>
    public KeyCounter StartKeyCount()
    {
        var counter = new KeyCounter(Path.Combine(DataDirectory, KeyCounter.FileName));
        _keyCounter = counter;
        _keyCountSave = new Timer(_ => counter.Save(), null, 60_000, 60_000);
        _discordPresence = new DiscordPresence(
            () => Settings.Current.KeyCount == KeyCountMode.CountAndDiscord,
            () => new DiscordPresence.Stats(counter.OnDay(DateTime.Today), counter.Total));
        return counter;
    }

    // 修飾キーは、フロントが送るもの (左右の Alt) と送らないものがあるので数えない
    private void CountKey(ImeKey key)
    {
        if (_keyCounter is null || key.Repeat || VirtualKeys.IsModifier(key.Vk) || Settings.Current.KeyCount == KeyCountMode.Off) return;
        _keyCounter.Count(key.Vk, key.Ch, DateTime.Now);
    }

    /// <summary>単語の登録のホットキー (Windows) 用: 選んでいた語の読みを推測する。分からなければ空。</summary>
    public string GuessReading(string word) => Converter.Reading(word) ?? "";

    /// <summary>変換エンジン (テストでは <see cref="StartConverter"/> を通さずに作って直接使う)。</summary>
    internal HybridConverter CreateConverter(string appDirectory, IMozcHelper? mozcHelper = null)
    {
        // 強制終了された Meltype の llama-server は、Zenzai が OFF でも起動したときに終わらせる
        ZenzaiConverter.EndStaleServers();
        var helper = Path.Combine(appDirectory, "mozc", OperatingSystem.IsWindows() ? "meltype_mozc_helper.exe" : "meltype_mozc_helper");
        var mozc = new MozcConverter(mozcHelper ?? new MozcHelperProcess(helper, MozcProfile()));
        var zenzai = new ZenzaiHolder();
        _converters.Add(mozc);
        _converters.Add(zenzai);
        ZenzaiStatus = () => Settings.Current.UseZenzai ? zenzai.Status ?? "まだ起動していません (日本語を変換すると起動します)" : null;
        var converter = new HybridConverter(mozc, () => Settings.Current is { UseZenzai: true } settings
            ? zenzai.Get(ZenzaiBundle.ServerPath(settings.ZenzaiServerPath, appDirectory), ZenzaiBundle.ModelPath(settings.ZenzaiModelPath, appDirectory), settings.ZenzaiDevice)
            : zenzai.Get("", ""));
        ForgetZenzaiHallucinations(converter);
        ConnectUserDictionary(converter);
        return converter;
    }

    /// <summary>
    /// Mozc のユーザーデータの場所。Meltype は Mozc に学習させないので、起動のたびに学習データだけを消す。
    /// 前の版が Mozc に覚えさせた文節の区切りと候補 (boundary.db・segment.db など) は、読みを誤った位置で区切る変換を固定するので捨てる (issue #2014)。
    /// 場所は残す (渡さないと、Mozc は入れてある Mozc のユーザーデータを読む)。
    /// </summary>
    private string MozcProfile()
    {
        var profile = Path.Combine(DataDirectory, "mozc");
        try
        {
            // MELTYPE_DATA_DIR がアプリの配置先と同じでも、mozc/ にあるヘルパーや辞書は消さない。
            // 変換の区切りと候補を覚えるファイルだけを捨てる (#2014)。
            if (Directory.Exists(profile))
                foreach (var name in new[] { "boundary.db", "segment.db" }) File.Delete(Path.Combine(profile, name));
        }
        catch (Exception ex) when (DataFile.IsFileError(ex))
        {
            // ほかのプロセスが開いている・権限が無い場合も、入力の起動は続け、次の起動時に消し直す。
            Diagnostics.Log.Warn($"前の版の Mozc の学習データを消せませんでした: {ex.Message}");
        }
        return profile;
    }

    private readonly object _engineDictionaryGate = new();
    private int _engineDictionarySent = -1;

    /// <summary>
    /// ユーザー辞書の語を Mozc に渡す (変換エンジンを作ったとき・語が変わったとき)。ヘルパーの応答を待つので裏のスレッドで送る。
    /// </summary>
    private void ConnectUserDictionary(HybridConverter converter)
    {
        void Send()
        {
            var (version, words) = UserDictionary.EngineWords();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (_engineDictionaryGate)
                {
                    // 後から変わった語をもう送った
                    if (version < _engineDictionarySent) return;
                    _engineDictionarySent = version;
                    converter.SetUserDictionary([.. words.Select(w => (w.Reading, w.Word, w.Pos))]);
                }
            });
        }
        UserDictionary.WordsChanged += Send;
        Send();
    }

    /// <summary>Zenzai の今の状態 (トレイのアイコンのツールチップに出す。issue #1152)。OFF・変換エンジンを作っていなければ null。</summary>
    public Func<string?> ZenzaiStatus { get; private set; } = () => null;

    /// <summary>入力欄の本体を作る部品 (<see cref="Ime.ImeHost"/> に渡す)。converter は変換エンジン (テストでは偽物)。</summary>
    /// <param name="candidates">読みの変換候補の一覧 (<see cref="HybridConverter.Candidates"/>)。</param>
    /// <param name="readingOf">漢字かな交じりの文の読み (<see cref="HybridConverter.Reading"/>)。</param>
    internal SessionParts SessionParts(IKanjiConverter converter, Func<string, long, IReadOnlyList<string>>? candidates, Func<string, string?> readingOf,
        Action<Correction>? corrected = null)
    {
        var services = _shared with
        {
            UserDictionary = UserDictionary,
            Predictor = new Predictor(Phrases, () => UserDictionary.Words.Select(w => (w.Reading, w.Word)), History),
            MoreCandidates = candidates,
        };
        return new SessionParts(Detector, converter, services)
        {
            // Mac・Linux では判定しない。判定すると、判定中の英字があるときにかなキーで日本語に切り替わらない (#1724)
            ClassifyDirect = OperatingSystem.IsWindows() ? ClassifyDirect : null,
            ReadingOf = readingOf,
            NowPlaying = () => _nowPlaying?.Read(),
            Corrected = corrected,
        };
    }

    /// <summary>
    /// 判定器と辞書・学習データをつなぐ (持ち主が作ったときに 1 回)。よく使う日本語の読み (kyouha = 今日は) は、一度英字にして確定しただけでは
    /// 英語として覚えない。
    /// </summary>
    internal static void Connect(CompositionDetector detector, CompositionServices services)
    {
        if (services.Languages is { } languages && services.Lexicon is { } lexicon) languages.IsCommonJapanese ??= lexicon.IsCommonJapanese;
        if (services.Languages is { } memory) memory.IsReadableRomaji ??= LanguageMemory.ReadableBy(detector.Romaji);
    }

    public void Dispose()
    {
        _settingsCheck.Dispose();
        _nowPlaying?.Dispose();
        _keyCountSave?.Dispose();
        _keyCounter?.Save();
        foreach (var converter in _converters) converter.Dispose();
        // Discord の応答を待つことがあるので最後
        _discordPresence?.Dispose();
    }

    /// <summary>
    /// 前の版が学習した Zenzai の壊れた答え (はーどうぇ → ハードウェアウェア、なまえは → 名前は魚のは: issue #1779) を、
    /// 選び直した変換と予測変換の語句から消す。#1776 から壊れた答えは使わないので、新しく学習することはない。
    /// 消すのは、かなを含み、かなが読みと合わず (<see cref="ZenzaiConverter.IsPlausible"/>)、ユーザー辞書・補助辞書の候補でもなく、
    /// Mozc の文節ごとの候補をつないでも作れない語 (<see cref="HybridConverter"/> が Zenzai の答えを使うかと同じ決め方)。
    /// 読みと比べるだけだと、Mozc の辞書の表記が読みと違う語 (あんはさうぇい → アン・ハサウェイ) を約 3.4 万件巻き添えにする (#1788)。
    /// Mozc が答えないときは消さない。消したかは覚えず、起動するたびに呼ぶ (読みと合わない語は少ないので、Mozc に聞くのは数件)。
    /// 入力スレッドから呼ぶ (ユーザー辞書は入力スレッドで読む)。Mozc に聞いて消すのは裏のスレッドで行う。
    /// </summary>
    // TODO: 壊れた答えを学習したのは 1.5.0 以前 (#1776 は 1.5.1 から)。1.5.0 以前から更新する人がいなくなったら、
    // これと、これだけが使う HybridConverter.IsBuiltFromMozcCandidatesNow・MozcConverter.ConvertWithCandidatesNow・PhraseHistory.Entries・PhraseHistory.Forget(reading, text) と、
    // ConversionHistory・PhraseHistory の読むときのロックを消す
    internal void ForgetZenzaiHallucinations(HybridConverter converter)
    {
        bool Suspect(string reading, string text) =>
            text.Any(c => KanaChars.IsHiragana(c) || KanaChars.IsKatakana(c)) && !ZenzaiConverter.IsPlausible(reading, text) &&
            !UserDictionary.Lookup(reading).Contains(text) && _shared.Candidates?.Lookup(reading).Contains(text) != true;
        var conversions = History.Entries().Where(e => Suspect(e.Reading, e.Text)).Select(e => (e.Reading, e.Text)).ToList();
        // 予測変換の語句は確定した文字のまま (設定の句読点) なので、読みと同じ 、。 にして比べる
        var phrases = Phrases.Entries().Where(e => Suspect(e.Reading, e.Text.Replace('，', '、').Replace('．', '。'))).ToList();
        if (conversions.Count == 0 && phrases.Count == 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var brokenConversions = conversions.Where(e => converter.IsBuiltFromMozcCandidatesNow(e.Reading, e.Text) == false).ToList();
            var brokenPhrases = phrases.Where(e => converter.IsBuiltFromMozcCandidatesNow(e.Reading, e.Text) == false).ToList();
            if (brokenConversions.Count == 0 && brokenPhrases.Count == 0) return;
            // Mozc に聞いている間に選び直した変換は消さない
            foreach (var (reading, text) in brokenConversions) if (History.Get(reading) == text) History.Forget(reading);
            // 同じ語句を正しい読みでも覚えていることがある (おせわに と おせわになっております → お世話になっております) ので、読みと語句の組で消す
            foreach (var (reading, text) in brokenPhrases) Phrases.Forget(reading, text);
            Diagnostics.Log.Info($"Zenzai の壊れた答えを学習から消しました (選び直した変換 {brokenConversions.Count} 件、予測変換の語句 {brokenPhrases.Count} 件)。");
        });
    }

    /// <summary>英字 (letters) がローマ字 (日本語) か英語かの判定 (Windows の「ログ / 判定理由」の判定にも使う)。final は、もう続きが無いか。</summary>
    public DetectionResult Evaluate(string letters, bool final, Settings settings) => Evaluate(_directEngine.Value, letters, final, settings);

    private static DetectionResult Evaluate(ScoreEngine engine, string letters, bool final, Settings settings) =>
        engine.Evaluate(new DetectionInput(letters, letters.Select(c => (int)char.ToUpperInvariant(c)).ToArray(), final), settings);

    /// <summary>英数状態で打ち始めた英字 (letters) がローマ字 (日本語) かを判定する。final は、もう続きが無いか。</summary>
    public Verdict ClassifyDirect(string letters, bool final, Settings settings) => ClassifyDirect(_directEngine.Value, letters, final, settings);

    /// <summary><see cref="ClassifyDirect(string, bool, Settings)"/> を、判定器 engine で行う (テストは辞書を読み直さずに共有の判定器で行う)。</summary>
    internal static Verdict ClassifyDirect(ScoreEngine engine, string letters, bool final, Settings settings)
    {
        var result = Evaluate(engine, letters, final, settings);
        // 同梱の辞書にない英単語 (debate, potato) はローマ字としても読めるので、よく使う英単語の一覧の語なら日本語にしない。
        if (result.Verdict == Verdict.Japanese && letters.Length >= 4 && BuiltInWordChecker.Shared.IsWord(letters.ToLowerInvariant()))
        {
            Diagnostics.Log.Info($"英数状態: {Diagnostics.Log.Text(letters)}はよく使う英単語なので日本語にしない");
            return final ? Verdict.English : Verdict.Undecided;
        }
        if (result.Verdict == Verdict.Japanese) Diagnostics.Log.Decision($"英数状態でローマ字を検知: {result.Describe()}");
        return result.Verdict;
    }
}
