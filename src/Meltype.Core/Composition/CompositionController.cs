// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>
/// 変換ボックスに表示する内容。変換中は文節ごとの文字列 (Clauses) と選択中の文節、その文節の候補を持つ。
/// </summary>
public sealed record CompositionView(
    string Text,
    IReadOnlyList<string> Candidates,
    int SelectedIndex,
    bool Converting,
    string Hint,
    IReadOnlyList<string>? Clauses = null,
    int SelectedClause = -1,
    IReadOnlyList<string?>? Notes = null,
    string? Meaning = null,
    string? Suggestion = null);

/// <summary>CompositionController が外界とやり取りする口。テストでは偽物に差し替える。</summary>
public interface ICompositionHost
{
    /// <summary>選択中の確定済み文字と、その読み。取得できなければ null。</summary>
    ReconversionSelection? GetReconversionSelection() => null;

    /// <summary>元の選択範囲が維持されている場合だけ置換する。</summary>
    bool TryReplaceSelection(ReconversionSelection selection, string text) => false;

    /// <summary>確定した文字列を、フォーカスのあるテキストボックスへ入力する。</summary>
    void CommitText(string text);

    /// <summary>握りつぶしていた打鍵を、そのままアプリへ送り直す。</summary>
    void Replay(KeyEvent e);

    /// <summary>キャレットの前の文字を count 文字消す (確定し直すとき)。</summary>
    void DeleteBackward(int count);

    void Replay(MouseButtonEvent e);

    /// <summary>その打鍵で入力される文字 (記号・数字を含む)。文字を生まないキーなら null。</summary>
    char? CharFromKey(KeyEvent e, bool shift);

    /// <summary>今 Shift キーが押されているか (かな入力の小書き文字・句読点の判定に使う)。</summary>
    bool IsShiftDown() => false;

    /// <summary>
    /// 入力欄のキャレットの前後の文字列 (確定済みの文字) を取りに行く。結果は後から UI スレッドで callback(前, 後ろ) に渡す。
    /// 取れなければ null を渡す。
    /// </summary>
    void RequestSurroundingText(Action<string?, string?> callback);

    void Show(CompositionView view);

    void Hide();
}

public sealed record ReconversionSelection(string Text, string Reading);

/// <summary>CompositionController の設定と、外の判定器へのつなぎ。</summary>
public sealed class CompositionOptions
{
    /// <summary>打ったそばから漢字に変換して見せるか。</summary>
    public Func<bool> LiveConversion { get; init; } = () => false;

    /// <summary>かな漢字変換のエンジン (Windows の CompositionService が Mozc / Microsoft IME を選ぶのに使う)。</summary>
    public Func<Config.ConversionEngine> Engine { get; init; } = () => Config.ConversionEngine.System;

    /// <summary>英数 (直接入力) 状態か。</summary>
    public Func<bool> DirectMode { get; init; } = () => false;

    /// <summary>
    /// 英数状態で打ち始めた文字がローマ字 (日本語) かを判定する。null なら英数状態では判定しない。
    /// 引数は (打った英字, もう続きが無いか)。
    /// </summary>
    public Func<string, bool, Verdict>? ClassifyDirect { get; init; }

    /// <summary>英数状態での判定結果を知らせる (true: 日本語だったので日本語入力に戻った / false: 英語だった)。</summary>
    public Action<bool>? DirectDecided { get; init; }

    /// <summary>同音異義語などの補助候補。</summary>
    public CandidateDictionary? Candidates { get; init; }

    /// <summary>前後の文字列の手がかりで候補を選ぶ規則 (気温 → 暑い)。</summary>
    public ContextRules? ContextRules { get; init; }

    /// <summary>ユーザーが選び直した変換の記録 (次から最初の候補にする)。</summary>
    public ConversionHistory? History { get; init; }

    /// <summary>
    /// 読みに対する変換候補の一覧 (Windows の変換候補 API)。時間がかかることがあるので、
    /// 候補を切り替え始めたときだけ呼ぶ。null なら補助辞書の候補だけ。
    /// </summary>
    public Func<string, IReadOnlyList<string>>? MoreCandidates { get; init; }

    /// <summary>英訳の候補 (複雑な → complex)。null なら出さない。</summary>
    public TranslationDictionary? Translations { get; init; }

    /// <summary>英訳の候補を出すか (設定)。</summary>
    public Func<bool> TranslationCandidates { get; init; } = () => true;

    /// <summary>候補の日本語の意味 (ウィクショナリー)。null なら英訳だけ。</summary>
    public MeaningDictionary? Meanings { get; init; }

    /// <summary>変換中に選んでいる候補の意味 (英訳) を変換ボックスに渡すか (設定)。</summary>
    public Func<bool> CandidateMeanings { get; init; } = () => true;

    /// <summary>選んだ英訳の記録 (普通の変換の学習より弱く効かせる)。</summary>
    public TranslationHistory? TranslationHistory { get; init; }

    /// <summary>英語とも日本語とも読める語を、次の語の文脈に合わせて確定し直すか。</summary>
    public Func<bool> AutoCorrect { get; init; } = () => true;

    /// <summary>ユーザー辞書 (変換で最優先に使う)。</summary>
    public UserDictionary? UserDictionary { get; init; }

    /// <summary>英語か日本語かの自動判定の強さ。</summary>
    public Func<DetectionLevel> Level { get; init; } = () => DetectionLevel.Balanced;

    /// <summary>かな入力 (JIS) か。</summary>
    public Func<bool> KanaInput { get; init; } = () => false;

    /// <summary>よくある書き間違い (ブレスレッド → ブレスレット) の辞書。「もしかして」に使う。null なら出さない。</summary>
    public MisspellingDictionary? Misspellings { get; init; }

    /// <summary>ローマ字の打ち間違いを直すもの (onegaishimsu → お願いします)。null なら直さない。</summary>
    public RomajiTypoCorrector? RomajiTypos { get; init; }

    /// <summary>ローマ字の打ち間違いを直すか (設定)。</summary>
    public Func<bool> CorrectTypos { get; init; } = () => true;

    /// <summary>確定するときに、日本語と英単語の間に半角スペースを入れるか (設定)。</summary>
    public Func<bool> SpaceAroundEnglish { get; init; } = () => false;

    /// <summary>ユーザーが英字 / かなに直した語の学習。</summary>
    public LanguageMemory? Languages { get; init; }

    /// <summary>入力欄に入ったときなどに、入力モード (あ / A) をカーソルの近くに出すか。</summary>
    public Func<bool> ModeIndicator { get; init; } = () => false;

    /// <summary>変換ボックスを出す位置 (入力位置に重ねる / カーソルの下)。</summary>
    public Func<Config.CompositionPlacement> Placement { get; init; } = () => Config.CompositionPlacement.Overlay;

    /// <summary>変換ボックスの文字の大きさ。</summary>
    public Func<Config.CompositionSize> Size { get; init; } = () => Config.CompositionSize.Auto;

    /// <summary>入力欄に入った (フォーカスが入った) ときにも入力モードを出すか。false なら 半角/全角 を押したときだけ。</summary>
    public Func<bool> ModeIndicatorOnFocus { get; init; } = () => true;
}

/// <summary>
/// Meltype キーボードの本体。変換ボックス (未確定文字列) を持ち、
///   文字キー → ボックスに追加 (日本語ならかな、英単語なら英字で自動表示)
///   Space   → 文節に区切って漢字変換 (英単語で終わっているときは確定して空白)
///   Shift+Space → 英単語と判定した語も、ローマ字として読んで変換 (go → 語)
///   ←→      → 文節を選ぶ (変換前に押しても文節の選択に入る) / Space・↓↑ でその文節の候補 / Shift+←→ で区切りを変える
///   Enter   → 確定してテキストボックスへ入力
///   BackSpace / Esc → 1 音削除 / 変換取り消し・入力取り消し
///   F6 / F7 / F9 / F10, 半角/全角 → ひらがな / カタカナ / 全角英数 / 半角英数 / 日本語⇔英字
///   その他のキー・クリック → 確定してからそのキーやクリックを通す
/// 英数状態でも、打ち始めの数文字でローマ字 (日本語) かを判定し (打鍵は待たせずに送る)、日本語なら送った分を消して日本語入力に戻し、変換ボックスに入れる。
/// UI スレッドだけで動く。フックからは CaptureGate 経由で入力が順番どおり届く。
/// </summary>
public sealed class CompositionController
{
    /// <summary>
    /// ライブ変換は 4 文字以上のかなだけ。短い断片は変換エンジンが的外れな漢字を返しやすい
    /// (きょ → 居, きょう → 喬) ので、打っている途中はかなのまま見せる。短い語は Space で変換する。
    /// </summary>
    private const int LiveConversionMinLength = 4;

    /// <summary>英数状態の判定で、この時間打鍵が無ければ英語とみなして判定をやめる。</summary>
    private const long DirectHoldIdleMs = 700;

    /// <summary>自分が確定してから、この時間内はアプリ側のテキストがまだ更新されていないかもしれないので自分の記録を優先する。</summary>
    private const long OwnCommitTrustMs = 1500;

    private readonly CaptureGate _gate;
    private readonly CompositionText _text;
    private readonly CompositionDetector _detector;
    private readonly IKanjiConverter _converter;
    private readonly ICompositionHost _host;
    private readonly CompositionOptions _options;
    private readonly Dictionary<string, string> _conversionCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _liveCache = new(StringComparer.Ordinal);
    private readonly HashSet<int> _swallowedShift = [];
    private readonly HashSet<int> _replayedDown = [];
    private readonly HashSet<int> _capturedDown = [];
    private List<Clause> _clauses = [];
    private int _selectedClause;
    private bool _converting;
    private bool? _lastCommitEnglish;
    private string? _lastCommitText;
    private string? _precedingText;
    private string? _followingText;
    private long _lastCommitTime = long.MinValue / 2;
    private int _compositionId;
    private ReconversionSelection? _reconversion;

    // 英数状態で判定中の語。打鍵はすぐアプリに送り (待たせない)、ローマ字と分かったら消して変換ボックスに入れ直す。
    private readonly StringBuilder _heldLetters = new();
    private int _heldSent;
    private long _heldLastKeyTime;

    /// <summary>変換中の文節。英語の区間も 1 つの文節として扱う (候補は英字/全角英字)。</summary>
    private sealed class Clause(string reading, bool isEnglish, List<string> candidates)
    {
        public string Reading { get; set; } = reading;
        public bool IsEnglish { get; } = isEnglish;
        public List<string> Candidates { get; set; } = candidates;
        public int Index { get; set; }
        public string Text => Candidates[Index];

        /// <summary>ユーザーが候補を選び直したか (確定時に学習する)。</summary>
        public bool Changed { get; set; }

        /// <summary>Windows の変換候補 API の候補を足したか。</summary>
        public bool Expanded { get; set; }

        /// <summary>この文節の読みを打ったときの英字 (あぴ → api)。分からなければ null。</summary>
        public string? Raw { get; set; }

        /// <summary>候補のうち英訳 (複雑な → complex) のもの。</summary>
        public HashSet<string> Translations { get; } = new(StringComparer.Ordinal);
    }

    public CompositionController(CaptureGate gate, CompositionDetector detector, IKanjiConverter converter, ICompositionHost host, CompositionOptions? options = null)
    {
        _gate = gate;
        _text = new CompositionText(detector);
        _detector = detector;
        _converter = converter;
        _host = host;
        _options = options ?? new CompositionOptions();
        _text.Level = () => _options.Level();
        _text.TypoCorrector = _options.RomajiTypos;
        // よく使う日本語の読み (kyouha = 今日は) は、一度英字にして確定しただけでは英語として覚えない。
        if (_options.Languages is { } languages && _options.RomajiTypos is { } typos) languages.IsCommonJapanese ??= typos.IsCommonJapanese;
        if (_options.Languages is { } memory) memory.IsReadableRomaji ??= word => detector.Romaji.AnalyzeFragment(word) is { IsValid: true, Partial: "" };
        if (_options.RomajiTypos is { } lexicon) detector.IsCommonJapanese ??= lexicon.IsCommonJapanese;
        _text.CorrectTypos = () => _options.CorrectTypos();
    }

    /// <summary>変換ボックスに入力中か、英数状態で打ち始めの語を判定中か。</summary>
    public bool IsComposing => !_text.IsEmpty || _heldLetters.Length > 0;

    /// <summary>直近に確定した文字列 (テスト・ログ用)。</summary>
    public event Action<string>? Committed;

    /// <summary>選択範囲を置換した。入力先の行を読み直すための通知。</summary>
    public event Action? ReconversionCommitted;

    /// <summary>キューにたまった入力をすべて処理する。UI スレッドで呼ぶ。</summary>
    public void Pump()
    {
        while (true)
        {
            while (_gate.TryDequeue(out var input))
            {
                if (input.Key is { } key) HandleKey(key);
                else if (input.Mouse is { } mouse) HandleMouse(mouse);
            }
            UpdateView();
            if (IsComposing) return;
            // 読みをすべて削除した場合も、元の選択範囲は置換せず再変換を終了する。
            if (_reconversion is not null) ClearComposition();
            if (_gate.TryRelease())
            {
                // 以降のキーアップはフックを素通りしてアプリに直接届くので、追跡をやめる。
                _swallowedShift.Clear();
                _replayedDown.Clear();
                _capturedDown.Clear();
                return;
            }
            // 解放しようとした間に新しい入力が届いた。続けて処理する。
        }
    }

    /// <summary>定期的に呼ぶ。英数状態で判定中の語が、無入力のまま一定時間たったら英語とみなす。</summary>
    public void Tick(long nowMs)
    {
        if (_heldLetters.Length == 0 || nowMs - _heldLastKeyTime < DirectHoldIdleMs) return;
        DecideHeld(final: true);
        Pump();
    }

    /// <summary>変換中の文節の候補を番号で選ぶ (Mac の候補ウィンドウをクリックしたときなど)。</summary>
    public void SelectCandidate(int index)
    {
        if (!_converting || _clauses.Count == 0) return;
        var clause = _clauses[_selectedClause];
        if (index < 0 || index >= clause.Candidates.Count) return;
        clause.Index = index;
        clause.Changed = true;
        UpdateView();
    }

    /// <summary>無効化・フォーカス喪失などで、未確定の内容をそのまま確定する。</summary>
    public void CommitPending()
    {
        if (_heldLetters.Length > 0) ReleaseHeldAsEnglish();
        CommitIfAny();
        UpdateView();
    }

    /// <summary>
    /// 入力先が変わった (別のウィンドウ・パスワード欄・入力欄でない所にフォーカスが移った) とき。未確定の内容を確定せずに捨てる。
    /// 確定すると、移った先 (別のアプリやパスワード欄) に入ってしまうため。捨てたら true。
    /// </summary>
    public bool Abandon(string reason)
    {
        // 英数状態で判定中の語は、もうアプリに送ってあるので判定をやめるだけ。
        ClearHeld();
        if (!IsComposing) return false;
        Diagnostics.Log.Warn($"{reason}ので、変換中の入力を取り消しました (移った先に入らないように)。");
        Reset();
        _correctable.Clear();
        UpdateView();
        return true;
    }

    /// <summary>例外からの復旧用。未確定の内容と追跡中の状態をすべて捨てる (次の入力で同じ例外を繰り返さないように)。</summary>
    public void Reset()
    {
        ClearComposition();
        ClearHeld();
        _swallowedShift.Clear();
        _replayedDown.Clear();
        _capturedDown.Clear();
    }

    /// <summary>変換中の文節・候補・選択中の文節をすべて消す。再変換の状態も消す。</summary>
    //追加処理に伴って、整理のための関数追加
    private void ClearComposition()
    {
        _reconversion = null;
        ++_compositionId;
        _text.Clear();
        _converting = false;
        _clauses = [];
        _spaceStartedConversion = false;
    }

    /// <summary>フォーカスが変わったときなど。前の入力欄の文脈を持ち越さない。</summary>
    public void ResetContext()
    {
        _lastCommitEnglish = null;
        _lastCommitText = null;
        _correctable.Clear();
    }

    /// <summary>入力言語が対象外になったとき。判定を止め、未処理のキー・クリックは順番どおりに通す。</summary>
    public void SuspendInput()
    {
        Abandon("入力言語が日本語ではなくなった");
        Reset();
        ResetContext();
        foreach (var input in _gate.Abort())
        {
            if (input.Key is { } key) _host.Replay(key);
            else if (input.Mouse is { } mouse) _host.Replay(mouse);
        }
        _host.Hide();
    }

    private void HandleMouse(MouseButtonEvent e)
    {
        // クリックで別の場所に移る前に、今の位置へ確定しておく。
        CommitPending();
        _correctable.Clear();
        _host.Replay(e);
    }

    private void HandleKey(KeyEvent e)
    {
        var vk = e.Vk;
        if (e.IsUp)
        {
            _swallowedShift.Remove(vk);
            // 押下をアプリに送ったキー、または押下が関所を閉じる前に通っていたキーは、離したこともアプリに伝える。
            var replayed = _replayedDown.Remove(vk);
            var capturedHere = _capturedDown.Remove(vk);
            if (replayed || !capturedHere) _host.Replay(e);
            return;
        }
        _capturedDown.Add(vk);

        if (_heldLetters.Length > 0)
        {
            if (VirtualKeys.IsLetter(vk) && _swallowedShift.Count == 0 && !VirtualKeys.IsModifier(vk))
            {
                Hold(e);
                return;
            }
            // 母音の後の - は長音 (ro-maji → ろーまじ、de-ta → でーた)。英単語の途中にはまず出てこないので、ローマ字として
            // 読めれば日本語に戻す。英語の接頭辞 (e-mail、co-op) かもしれないときは - も送って判定を続け、- の後ろで決める
            // (e-mail → 英語、e-me-ru → えーめーる)。
            var part = LastHyphenPart();
            if (vk == VirtualKeys.OemMinus && _swallowedShift.Count == 0 && part.Length > 0 &&
                _detector.Romaji.AnalyzeFragment(part) is { IsValid: true, Partial: "" })
            {
                var prefix = !_heldLetters.ToString().Contains('-') && CompositionDetector.IsHyphenPrefix(part);
                _heldLetters.Append('-');
                _heldLastKeyTime = e.TimeMs;
                if (prefix)
                {
                    SendHeld(e);
                    return;
                }
                Diagnostics.Log.Decision($"英数状態でローマ字を検知: {Diagnostics.Log.Text(_heldLetters.ToString())} (母音の後の長音)");
                SwitchHeldToJapanese();
                return;
            }
            // 英字以外のキー・Shift で判定を打ち切り、英語として出してから、そのキーを普通に処理する。
            Diagnostics.Log.Info($"英数状態の判定を打ち切り: キー 0x{vk:X2} (Shift {_swallowedShift.Count})");
            ReleaseHeldAsEnglish();
        }

        if (IsShift(vk))
        {
            // 大文字入力や文節の区切り変更のための Shift はアプリに渡さない。ほかのキーと一緒に送り直すときにまとめて送る。
            _swallowedShift.Add(vk);
            return;
        }
        if (VirtualKeys.IsModifier(vk))
        {
            // Ctrl / Alt / Win: ショートカットの前に確定する。
            CommitIfAny();
            ReplayDown(e);
            return;
        }
        if (_replayedDown.Any(IsCommandModifier))
        {
            CommitIfAny();
            ReplayDown(e);
            return;
        }

        if (_reconversion is not null && vk == VirtualKeys.Escape)
        {
            ClearComposition();
            return;
        }

        if (!IsComposing)
        {
            StartWith(e);
            return;
        }

        if (_converting && HandleConversionKey(vk)) return;

        switch (vk)
        {
            case VirtualKeys.Convert:
                _text.FixTypos();
                StartConversion(preferJapanese: true);
                return;
            case VirtualKeys.Return:
                _text.FixTypos();
                Commit();
                return;
            case VirtualKeys.Space when _swallowedShift.Count > 0 || _host.IsShiftDown():
                // Shift+Space: 英語と判定した語でも、ローマ字として読んで変換する (go → 語、camera → かめら)。
                _text.FixTypos();
                _spaceStartedConversion = true;
                StartConversion(preferJapanese: true);
                return;
            case VirtualKeys.Space:
                // ユーザー辞書に文字列があれば必ず変換処理を入れる
                if (ContainsUserDictionarySegment())
                {
                    StartConversion();
                    return;
                }

                _text.FixTypos();
                // 英語と判定した語で終わっているなら、変換ではなく確定して空白を入れる
                // (日本語の部分は、ライブ変換が ON なら漢字にして、OFF なら見えているかなのまま確定)。
                // 数字だけ (1、12) は、前が英文なら確定して空白 (I have 2 cats)。それ以外は変換して ① 一 Ⅰ などの候補を出す。
                if (_text.IsAlphanumericAt(final: true) && !(_text.Mode == DisplayMode.Auto && _text.IsNumeric && _text.Raw.All(char.IsAsciiDigit) && _text.PrecedingEnglish != true))
                {
                    Commit(suffix: " ", fixEnglish: true);
                }
                else if (EndsWithEnglish(final: true)) CommitText(FixEnglishTypo(_text.RenderSegments(final: true, _options.LiveConversion() ? Convert : null)) + " ", english: true, _text.Raw);
                else if (_text.Mode == DisplayMode.Auto && _detector.IsEnglishAtWordEnd(_text.Raw, _options.Level())) CommitText(FixEnglishTypo(_text.Raw) + " ", english: true, _text.Raw);
                else
                {
                    // Space で変換した = 語の後に空白を打とうとした、とも取れる (確定し直して英語にするときに空白を足す)。
                    _spaceStartedConversion = true;
                    StartConversion();
                }
                return;
            case VirtualKeys.Back:
                _text.RemoveLast();
                return;
            case VirtualKeys.Escape:
                _text.Clear();
                return;
            case VirtualKeys.Tab when FindMisspelling() is { } typo:
                // もしかして: 書き間違いを直す。
                FixMisspelling(typo);
                return;
            case VirtualKeys.Tab when _text.Suggestion() is not null:
                // 判定の強さが手動: 提案どおり英字にする。
                _text.LevelOverride = DetectionLevel.Balanced;
                return;
            case VirtualKeys.F6: SetMode(DisplayMode.Hiragana); return;
            case VirtualKeys.F7: SetMode(DisplayMode.Katakana); return;
            case VirtualKeys.F9: SetMode(DisplayMode.FullWidthAlphanumeric); return;
            case VirtualKeys.F10: SetMode(DisplayMode.HalfWidthAlphanumeric); return;
            case VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down when !_text.IsAlphanumeric:
                // 変換前でも矢印キーで文節の選択に入る (Mac のライブ変換と同じ)。
                EnterClauseSelection(vk);
                return;
        }

        if (VirtualKeys.IsHankakuZenkaku(vk))
        {
            SetMode(_text.IsAlphanumeric ? DisplayMode.Hiragana : DisplayMode.HalfWidthAlphanumeric);
            return;
        }

        if (_text.KanaInput && KanaOf(e) is { } key)
        {
            if (_converting)
            {
                Commit();
                BeginComposition();
            }
            _text.AppendKana(key.Raw, key.Kana);
            return;
        }

        if (_host.CharFromKey(e, _swallowedShift.Count > 0) is { } ch && !char.IsControl(ch) && ch != ' ')
        {
            // 変換中に次の文字を打ったら、今の候補で確定して新しい入力を始める (IME と同じ)。
            if (_converting)
            {
                Commit();
                BeginComposition();
            }
            _text.Append(ch);
            return;
        }

        // 矢印 (英字だけのとき)・Tab・Delete などは確定してから通す。
        Commit();
        ReplayDown(e);
    }

    /// <summary>変換ボックスを開く記号・数字 (フック側の MeltypeEngine.StartsComposition と合わせる)。</summary>
    internal static bool StartsWithSymbol(char c) => c is >= '!' and <= '~' && !char.IsAsciiLetter(c);

    /// <summary>変換ボックスが空のときの最初の打鍵。英字・句読点なら入力を始め、それ以外はそのまま通す。</summary>
    private void StartWith(KeyEvent e)
    {
        if (e.Vk == VirtualKeys.Convert)
        {
            if (_host.GetReconversionSelection() is not { } selection || string.IsNullOrWhiteSpace(selection.Reading)) return;
            _correctable.Clear();
            BeginComposition();
            _reconversion = selection;
            foreach (var kana in selection.Reading) _text.AppendKana(kana, kana);
            _text.Mode = DisplayMode.Hiragana;
            StartConversion(preferJapanese: true);
            return;
        }
        // かな入力: かなのキーならすべて入力を始める (英数状態でなければ)。
        if (_options.KanaInput() && !_options.DirectMode() && KanaOf(e) is { } key)
        {
            BeginComposition();
            _text.AppendKana(key.Raw, key.Kana);
            return;
        }
        var c = _host.CharFromKey(e, _swallowedShift.Count > 0);
        var letter = VirtualKeys.IsLetter(e.Vk) && c is { } l && char.IsAsciiLetter(l);
        if (letter && _options.DirectMode())
        {
            // 英数状態: ローマ字かどうか判定する (打鍵はすぐ送る)。大文字で始まる語は英語なのでそのまま通す。
            if (_options.ClassifyDirect is null || _swallowedShift.Count > 0 || char.IsAsciiLetterUpper(c!.Value))
            {
                Diagnostics.Log.Info($"英数状態: {Diagnostics.Log.Text(c.ToString()!)}は大文字 / Shift なので英語のまま");
                ReplayDown(e);
                _options.DirectDecided?.Invoke(false);
            }
            else Hold(e);
            return;
        }
        // 句読点・かぎかっこ・長音・中黒・数字でも入力を始める (、。「」ー・)。英数状態ではそのまま通す。
        if (letter || (c is { } symbol && StartsWithSymbol(symbol) && !_options.DirectMode()))
        {
            BeginComposition();
            _text.Append(c!.Value);
            return;
        }
        ReplayDown(e);
    }

    /// <summary>
    /// 新しい入力を始める。入力欄のキャレットの前後の確定済みの文字を読みに行き、
    /// 英語とも日本語とも読める語の判定と、変換の文脈に使う。読めるまでは自分が最後に確定した文字列で代用する。
    /// </summary>
    private void BeginComposition()
    {
        _text.KanaInput = _options.KanaInput();
        var id = ++_compositionId;
        // 自分が確定した直後は、アプリ側のテキストがまだ更新されていないかもしれないので自分の記録を信じる。
        var recentOwnCommit = Environment.TickCount64 - _lastCommitTime < OwnCommitTrustMs;
        _precedingText = _lastCommitEnglish is null ? null : _lastCommitText;
        _followingText = null;
        _text.PrecedingEnglish = _lastCommitEnglish;
        _text.PrecedingEnglishSentence = _lastCommitEnglish == true && IsEnglishSentence(_lastCommitText);
        _text.FollowingEnglish = null;
        _host.RequestSurroundingText((before, after) =>
        {
            // 返ってくるまでに別の入力になっていたら使わない。
            if (id != _compositionId) return;
            if (!recentOwnCommit && before is not null)
            {
                _precedingText = before;
                if (LanguageOf(before) is { } english) _text.PrecedingEnglish = english;
                _text.PrecedingEnglishSentence = IsEnglishSentence(before);
            }
            _followingText = after;
            _text.FollowingEnglish = LanguageOfStart(after);
            UpdateView();
        });
    }

    /// <summary>
    /// 英文の途中か: 最後の行の日本語の文字より後ろが、空白で区切った英単語 2 語以上で、空白で終わる ("I want ", "Thanks, see ")。
    /// 日本語の文の中の英単語 ("今日は GitHub ") は 1 語なので当たらない。
    /// </summary>
    /// <summary>1 語でも英文の始まりとみなす、行の始めのあいさつ・感動詞。</summary>
    private static readonly HashSet<string> SentenceOpeners = ["hey", "hi", "hello", "oh", "wow", "yeah", "yes", "well", "so", "hmm", "ah", "ooh", "oops", "thanks", "sorry", "please", "dear", "yay", "whoa", "nope", "yep"];

    /// <summary>日本語の後ろでも英文の始まりとみなす感動詞 (日本語のローマ字としては使わない綴りのもの)。</summary>
    private static readonly HashSet<string> Interjections = ["oh", "wow", "yeah", "ooh", "oops", "whoa", "woah", "yay", "hmm", "hey"];

    internal static bool IsEnglishSentence(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[^1] != ' ') return false;
        var start = text.Length;
        while (start > 0 && text[start - 1] < 0x80 && text[start - 1] is not ('\n' or '\r')) start--;
        var words = text[start..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // 行の始めのあいさつ・感動詞 (hey・hi・oh …) なら 1 語でも英文の始まり ("hey " の後の yo)。
        // ほかの 1 語 (GitHub no repo の GitHub) は、日本語の文の中の英単語のことが多いので 2 語以上
        var lineStart = start == 0 || text[start - 1] is '\n' or '\r';
        if (lineStart && words is [var first] && SentenceOpeners.Contains(first.TrimEnd(',', '!', '.').ToLowerInvariant())) return true;
        // 感動詞 (oh・wow・yeah …) は日本語の後ろでも英文の始まり (爆弾にはなれない oh + no! の no を の にしない)
        if (words is [var interjection] && Interjections.Contains(interjection.TrimEnd(',', '!', '.').ToLowerInvariant())) return true;
        // 行の始めの、' で縮めた英語 (I'll・We're・don't) も 1 語で英文の始まり ("I'll " の後の go)。ローマ字には ' が入らない
        if (lineStart && words is [var contraction] && System.Text.RegularExpressions.Regex.IsMatch(contraction, @"^[A-Za-z]+['’][A-Za-z]{1,2}$")) return true;
        return words.Length >= 2 && words.All(w => w.Any(char.IsAsciiLetter) && w.All(c => char.IsAsciiLetterOrDigit(c) || c is ',' or '.' or '\'' or '-' or '!' or '?' or ':' or ';'));
    }

    /// <summary>確定済みの文字列の最後の (空白以外の) 文字が英数字なら英語、かな・漢字・全角記号なら日本語。</summary>
    internal static bool? LanguageOf(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (Classify(text[i]) is { } english) return english;
            if (text[i] >= 0x80 && !char.IsWhiteSpace(text[i]) && text[i] != '　') return null;
        }
        return null;
    }

    /// <summary>キャレットの後ろの文字列の最初の (空白以外の) 文字で判断する。</summary>
    internal static bool? LanguageOfStart(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var c in text)
        {
            if (c is '\r' or '\n') return null; // 次の行は別の文
            if (Classify(c) is { } english) return english;
            if (c >= 0x80 && !char.IsWhiteSpace(c) && c != '　') return null;
        }
        return null;
    }

    /// <summary>
    /// 英数字なら英語 (true)、かな・漢字・全角文字・日本語の文で使う記号 (○ ※ ★ 「」 など U+2000 以降) なら日本語 (false)、
    /// 空白や英文の記号なら判断しない (null)。
    /// </summary>
    private static bool? Classify(char c)
    {
        if (char.IsAsciiLetterOrDigit(c)) return true;
        if (c >= '\u2000' && !char.IsWhiteSpace(c) && c != '\u3000') return false;
        return null;
    }

    // ---- 英数状態のローマ字判定 ----

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)

    private void Hold(KeyEvent e)
    {
        _heldLetters.Append(VirtualKeys.ToLetter(e.Vk));
        _heldLastKeyTime = e.TimeMs;
        SendHeld(e);
        DecideHeld(final: false);
    }

    /// <summary>
    /// 判定中の語の打鍵を、判定を待たずにアプリへ送る。前は判定できるまで保留していたので、英単語 (can・game・today) は
    /// Space を押すまで画面に出ず、英数状態の入力が遅れて見えた。ローマ字と分かったら、送った分を BackSpace で消す。
    /// </summary>
    private void SendHeld(KeyEvent e)
    {
        _host.Replay(e);
        _replayedDown.Add(e.Vk);
        _heldSent++;
    }

    /// <summary>判定中の英字の、最後の - より後ろ (e-ma → ma)。- が無ければ全体。</summary>
    private string LastHyphenPart()
    {
        var letters = _heldLetters.ToString();
        return letters[(letters.LastIndexOf('-') + 1)..];
    }

    private void DecideHeld(bool final)
    {
        // 英語の接頭辞の後の - (e-、co-) の後ろだけで判定する。e-mail・co-op のような - の入った英単語なら英語。
        var part = LastHyphenPart();
        var verdict = _detector.IsHyphenatedEnglishWord(_heldLetters.ToString()) ? Verdict.English
            : part.Length == 0 ? Verdict.Undecided
            : _options.ClassifyDirect!(part, final);
        // 2 文字の英単語 (up、my) でも、ローマ字の打ちかけとして読める (う + p) なら、続きを見てから決める
        // (upa- → うぱー、my → みゃ)。Space などで打ち終われば英語。
        if (verdict == Verdict.English && !final && part.Length <= 2 && _heldLetters.Length <= 2 &&
            _detector.Romaji.AnalyzeFragment(part.ToLowerInvariant()) is { IsValid: true, Partial.Length: > 0 })
        {
            verdict = Verdict.Undecided;
        }
        Diagnostics.Log.Info($"英数状態の判定: {Diagnostics.Log.Text(_heldLetters.ToString())}→ {verdict}{(final ? " (打ち終わり)" : "")}");
        if (verdict == Verdict.Japanese) SwitchHeldToJapanese();
        else if (verdict != Verdict.Undecided || final) ReleaseHeldAsEnglish();
    }

    /// <summary>ローマ字だった: 日本語入力に戻し、判定中の英字を変換ボックスに入れる。</summary>
    private void SwitchHeldToJapanese()
    {
        var letters = _heldLetters.ToString();
        var sent = _heldSent;
        ClearHeld();
        // 判定を待たずに送っていた英字を消して、変換ボックスに入れ直す。
        _host.DeleteBackward(sent);
        _options.DirectDecided?.Invoke(true);
        BeginComposition();
        foreach (var c in letters)
        {
            if (_text.KanaInput && Detection.KanaDetector.KanaForKey(char.ToUpperInvariant(c), char.IsAsciiLetterUpper(c)) is { } kana) _text.AppendKana(c, kana);
            else _text.Append(c);
        }
    }

    /// <summary>かな入力で、その打鍵が入力するかなと、そのキーの英字 (英語として見せるとき用)。かなのキーでなければ null。</summary>
    private (char Raw, char Kana)? KanaOf(KeyEvent e)
    {
        var shift = _swallowedShift.Count > 0 || _host.IsShiftDown();
        if (Detection.KanaDetector.KanaForKey(e.Vk, shift) is not { } kana) return null;
        var raw = _host.CharFromKey(e, _swallowedShift.Count > 0) ?? kana;
        // Shift で打った小書き文字 (っ = Shift+Z、ぃ = Shift+E) は、大文字で打った英語 (Z・E) ではない
        // (きのうはたのしかった が たのしかZq になっていた)。
        if (shift && kana != Detection.KanaDetector.KanaForKey(e.Vk, false)) raw = char.ToLowerInvariant(raw);
        return (raw, kana);
    }

    /// <summary>英語だった: 打鍵はもう送ってあるので、判定をやめるだけ。</summary>
    private void ReleaseHeldAsEnglish()
    {
        ClearHeld();
        _correctable.Clear();
        _options.DirectDecided?.Invoke(false);
    }

    private void ClearHeld()
    {
        _heldLetters.Clear();
        _heldSent = 0;
    }

    // ---- 変換 (文節) ----

    /// <summary>変換中だけ意味を持つキー。処理したら true。</summary>
    private bool HandleConversionKey(int vk)
    {
        var shift = _swallowedShift.Count > 0;
        switch (vk)
        {
            case VirtualKeys.Convert:
            case VirtualKeys.Space:
            case VirtualKeys.Down:
                NextCandidate(+1);
                return true;
            case VirtualKeys.Up:
                NextCandidate(-1);
                return true;
            case VirtualKeys.Right when shift:
                Resize(+1);
                return true;
            case VirtualKeys.Left when shift:
                Resize(-1);
                return true;
            case VirtualKeys.Right:
                _selectedClause = Math.Min(_selectedClause + 1, _clauses.Count - 1);
                return true;
            case VirtualKeys.Left:
                _selectedClause = Math.Max(_selectedClause - 1, 0);
                return true;
            case VirtualKeys.Return:
                Commit();
                return true;
            case VirtualKeys.Tab when FindMisspelling() is { } typo:
                // もしかして: 書き間違いを直して変換し直す。
                _converting = false;
                FixMisspelling(typo);
                StartConversion();
                return true;
            case VirtualKeys.Back:
            case VirtualKeys.Escape:
                // 変換を取り消して、かなの入力に戻る。
                _converting = false;
                return true;
            default:
                return false;
        }
    }

    private (int Start, int End, Misspelling Misspelling)? FindMisspelling() =>
        _options.Misspellings is { } dictionary ? _text.FindMisspelling(dictionary) : null;

    private void FixMisspelling((int Start, int End, Misspelling Misspelling) typo)
    {
        Diagnostics.Log.Decision($"もしかして: {Diagnostics.Log.Text(typo.Misspelling.Wrong)}→{Diagnostics.Log.Text(typo.Misspelling.Right)}に直しました。");
        _text.ReplaceReading(typo.Start, typo.End, typo.Misspelling.Right);
    }

    /// <summary>「もしかして」の案内。変換ボックスの、打った文字のすぐ下に出す (書き間違いが無ければ null)。</summary>
    private string? MisspellingSuggestion() => FindMisspelling() is { } typo ? $"もしかして: {typo.Misspelling.Right}　<Tab>で修正" : null;

    /// <summary>変換前に矢印キーを押したとき: 文節に区切って、← なら最後の文節、→ なら最初の文節を選ぶ。</summary>
    private void EnterClauseSelection(int vk)
    {
        StartConversion();
        if (!_converting) return;
        var shift = _swallowedShift.Count > 0;
        _selectedClause = vk == VirtualKeys.Left ? _clauses.Count - 1 : 0;
        if (shift && vk is VirtualKeys.Left or VirtualKeys.Right) Resize(vk == VirtualKeys.Left ? -1 : +1);
        else if (vk == VirtualKeys.Up) NextCandidate(-1);
    }

    private void SetMode(DisplayMode mode)
    {
        _converting = false;
        _text.Mode = mode;
    }

    /// <summary>
    /// 英語区間はそのまま、日本語区間は文脈を付けて変換エンジンで文節に区切って変換する。
    /// 英語区間も、ローマ字として読めれば日本語の候補 (go → 語 ご ゴ) を後ろに足す。preferJapanese (Shift+Space) なら前に出す。
    /// </summary>
    private void StartConversion(bool preferJapanese = false)
    {
        var userDictClauses = new List<Clause>();
        var clauses = new List<Clause>();
        var segments = _text.ConversionSegments();
        for (var s = 0; s < segments.Count; s++)
        {
            var segment = segments[s];

            // 一度このSegmentでユーザー辞書を調べる。もし一致したのがあれば一時的にsegment.Rawで追加してあげる
            // 最後にセグメントで後に追加されたclausesに候補を追加するので、被りは気にしなくてよい
            var userDictLookup = _options?.UserDictionary?.Lookup(segment.Raw);
            if (userDictLookup?.Count > 0) userDictClauses.Add(new(segment.Raw, false, [.. userDictLookup]));

            if (segment.IsEnglish)
            {
                var english = EnglishCandidates(segment.Raw);
                var romaji = RomajiCandidates(segment.Raw);
                clauses.Add(new Clause(segment.Raw, true, preferJapanese && romaji.Count > 0
                    ? Distinct([.. romaji, .. english])
                    : Distinct([.. english, .. romaji])));
                continue;
            }
            if (segment.Kana.Length == 0) continue;
            var japanese = ConvertJapanese(segment.Kana);
            // 英単語 + する (push|した、deploy|しよう): 変換エンジンは英語の後ろの「した」だけを見て 下・死体・使用 にしてしまう。かなのまま
            if (s > 0 && segments[s - 1].IsEnglish && japanese.Count > 0 && IsSuruAfterEnglish(japanese[0])) Prefer(japanese[0], japanese[0].Reading);
            // 候補の最後に、打ったままの英字 (あぴ → api) と全角の英字も出す (Space を連打して英字に戻せる)。
            var offset = 0;
            foreach (var clause in japanese)
            {
                clause.Raw = _text.RawForReading(s, offset, clause.Reading.Length);
                offset += clause.Reading.Length;
                AddOldKana(clause);
                AddTranslations(clause);
                AddRawCandidates(clause);
            }
            clauses.AddRange(japanese);
        }

        // 普通のclausesに含まれてないセグメントの候補を逃さないためのリスト
        var userDictSegmentClauses = new List<Clause>();
        foreach (var userDictClause in userDictClauses)
        {
            var candidatesAdded = false;
            foreach (var clause in clauses)
            {
                if (clause.Reading == userDictClause.Reading)
                {
                    // 既にあるclauseの候補に追加してあげる (ユーザー辞書優先)
                    clause.Candidates = userDictClause.Candidates
                        .Concat(clause.Candidates)
                        .Distinct()
                        .ToList();
                    candidatesAdded = true;
                    break;
                }
            }

            if (!candidatesAdded) userDictSegmentClauses.Add(userDictClause);
        }

        if (clauses.Count == 0) return;
        _clauses = [ .. userDictSegmentClauses, .. clauses]; // ユーザー辞書優先
        _selectedClause = 0;
        _converting = true;
    }

    private bool ContainsUserDictionarySegment()
    {
        var segments = _text.ConversionSegments();
        foreach (var segment in segments)
        {
            if (_options?.UserDictionary?.Lookup(segment.Raw).Count > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// 英訳の候補 (複雑な → complex, complicated …) を、日本語の候補の後ろに足す。
    /// 選んだことのある英訳は前に出す: 1 回なら英訳の中の先頭、2 回以上なら 2 番目 (変換エンジンの 1 番目の候補は動かさない)。
    /// </summary>
    private void AddTranslations(Clause clause)
    {
        if (clause.IsEnglish || _options.Translations is not { } dictionary || !_options.TranslationCandidates()) return;
        var words = dictionary.Lookup(clause.Text, clause.Reading).Where(w => !clause.Candidates.Contains(w)).ToList();
        if (words.Count == 0) return;
        var learned = _options.TranslationHistory?.Get(clause.Reading) ?? [];
        // 選んだ回数の多い順に前へ
        words = learned.Select(l => l.Word).Where(words.Contains).Concat(words.Where(w => !learned.Any(l => l.Word == w))).ToList();
        foreach (var word in words) clause.Translations.Add(word);
        var often = learned.FirstOrDefault(l => l.Count >= 2 && words.Contains(l.Word)).Word;
        if (often is not null)
        {
            clause.Candidates.Insert(Math.Min(1, clause.Candidates.Count), often);
            words.Remove(often);
        }
        clause.Candidates.AddRange(words);
    }

    private static void AddRawCandidates(Clause clause)
    {
        // 全角にした記号 (＃ （ ％) は、半角に戻した候補も出す (Space を続けて押すと半角の # ( %)。
        if (clause.Candidates.Count > 0 && CompositionText.SymbolsToHalfWidth(clause.Candidates[0]) is var half && half != clause.Candidates[0] &&
            !clause.Candidates.Contains(half))
        {
            clause.Candidates.Add(half);
        }
        if (clause.Raw is not { } raw || !raw.Any(c => c is >= '!' and <= '~')) return;
        foreach (var candidate in new[] { raw, CompositionText.ToFullWidth(raw) })
        {
            if (!clause.Candidates.Contains(candidate)) clause.Candidates.Add(candidate);
        }
    }

    /// <summary>
    /// wi / we で打った うぃ / うぇ は、ゐ / ゑ (ヰ / ヱ) にした候補も出す (うぃすきー → ゐすきー、ヰスキー)。
    /// 候補の中の うぃ ウィ うぇ ウェ を置き換えた形を、打ったままの英字の候補より前に足す。
    /// </summary>
    private static void AddOldKana(Clause clause)
    {
        if (clause.IsEnglish || clause.Raw is not { } raw) return;
        var lower = raw.ToLowerInvariant();
        if (!lower.Contains("wi") && !lower.Contains("we")) return;
        if (!clause.Reading.Contains("うぃ") && !clause.Reading.Contains("うぇ")) return;
        static string Old(string text) => text.Replace("うぃ", "ゐ").Replace("うぇ", "ゑ").Replace("ウィ", "ヰ").Replace("ウェ", "ヱ");
        var added = new List<string>();
        foreach (var candidate in clause.Candidates.Concat([clause.Reading, CompositionText.ToKatakana(clause.Reading)]))
        {
            var old = Old(candidate);
            if (old != candidate && !clause.Candidates.Contains(old) && !added.Contains(old)) added.Add(old);
        }
        clause.Candidates.AddRange(added);
    }

    /// <summary>
    /// ユーザー辞書の読みが含まれていれば、その部分は登録した単語の文節にし、残りだけを変換エンジンで変換する
    /// (きごうとうふくめ → 記号等|含め)。含まれていなければ普通に変換する。
    /// </summary>
    private List<Clause> ConvertJapanese(string kana)
    {
        // ローマ字として読めずに残った英字 (こほぃc の c) と半角の記号 (... @) は、変換エンジンに渡すと別の記号 (© ．．． ＠) にされるので、
        // そのままの文節にする。数字は、記号・英字とつながっていればそのまま (3.0 の 3 を 1 文字だけ渡すと ❸ にされる)、
        // それ以外は変換エンジンに渡す (2026ねん → 2026年)。
        var keep = new bool[kana.Length];
        for (var i = 0; i < kana.Length; i++) keep[i] = kana[i] is >= '!' and <= '~' && !char.IsAsciiDigit(kana[i]);
        for (var i = 0; i < kana.Length; i++)
        {
            if (!char.IsAsciiDigit(kana[i])) continue;
            var end = i;
            while (end < kana.Length && char.IsAsciiDigit(kana[end])) end++;
            var touches = (i > 0 && keep[i - 1]) || (end < kana.Length && keep[end]);
            for (var k = i; k < end; k++) keep[k] = touches;
            i = end - 1;
        }
        if (keep.Any(k => k))
        {
            var result = new List<Clause>();
            var start = 0;
            for (var i = 1; i <= kana.Length; i++)
            {
                if (i < kana.Length && keep[i] == keep[start]) continue;
                var run = kana[start..i];
                if (keep[start]) result.Add(new Clause(run, false, Distinct([run, .. _options.Candidates?.Lookup(run) ?? [], CompositionText.ToFullWidth(run)])));
                else result.AddRange(ConvertJapanese(run));
                start = i;
            }
            return result;
        }
        if (_options.UserDictionary?.Split(kana) is not { } pieces) return ConvertWithEngine(kana);
        var clauses = new List<Clause>();
        var registered = new List<Clause>();
        foreach (var (reading, word) in pieces)
        {
            if (word is null)
            {
                clauses.AddRange(ConvertWithEngine(reading));
                continue;
            }
            var clause = new Clause(reading, false, JapaneseCandidates(reading, word));
            clauses.Add(clause);
            registered.Add(clause);
        }
        // 同梱の語句 (しょせん → 所詮) も、文脈の手がかり (試合 → 初戦) と学習で選び直せるようにする。
        // 変換エンジンの文節も、語句の文節を含めた前後で文脈の手がかりを見直す (甲斐性ない + こうかい → 後悔)。
        foreach (var clause in clauses)
        {
            var surrounding = (_precedingText ?? "") + string.Concat(clauses.Where(c => c != clause).Select(c => c.Text)) + (_followingText ?? "");
            var preferred = _options.ContextRules?.Choose(clause.Reading, surrounding) ?? (registered.Contains(clause) ? _options.History?.Get(clause.Reading) : null);
            if (preferred is not null) Prefer(clause, preferred);
        }
        return clauses;
    }

    /// <summary>
    /// 日本語のかなを変換エンジンで文節に区切って変換する。各文節の最初の候補は次の順で決める:
    ///   1. 文脈の手がかり辞書 (前後に 気温 があれば あつい → 暑い)
    ///   2. ユーザーが前に選び直した変換 (学習)
    ///   3. 変換エンジンの結果 (入力欄のキャレットの前の文字を文脈として渡している)
    /// </summary>
    private List<Clause> ConvertWithEngine(string kana)
    {
        // 文脈が無いまま「に」で始まる読みを変換すると、変換エンジンは「に」を語の頭と読む (になってしまう → 担ってしまう)。
        // 前の文脈が取れないときは、仮の文脈「これ」を付けて助詞として読ませる (これ + になってしまう → になってしまう)。
        // 「は」「で」なども助詞になりうるが、仮の文脈を付けると はしる → は知る のように崩れるので「に」だけ。
        var context = ConversionContext();
        var parts = context is not null
            ? ConvertWithContext(kana, context)
            : _converter.ConvertClauses(kana, kana.Length >= 3 && kana[0] == 'に' ? "これ" : null);
        parts ??= [new ConversionClause(kana, _converter.Convert(kana) ?? kana)];
        // 読み全体が補助辞書・絵文字の辞書の語 (かんがえるかお → 🤔) なら、文節に分けずに 1 つの文節にして候補を出す。
        if (parts.Count > 1 && _options.Candidates?.Contains(kana) == true)
        {
            parts = [new ConversionClause(kana, string.Concat(parts.Select(p => p.Text)))];
        }
        parts = JoinSmallKana(parts);
        var clauses = parts.Select(p => new Clause(p.Reading, false, JapaneseCandidates(p.Reading, NormalizeHalfWidth(p.Text, p.Reading)))).ToList();
        for (var i = 0; i < clauses.Count; i++)
        {
            var others = string.Concat(clauses.Where((_, k) => k != i).Select(c => c.Text));
            var surrounding = (_precedingText ?? "") + others + (_followingText ?? "");
            var preferred = _options.ContextRules?.Choose(clauses[i].Reading, surrounding) ?? _options.History?.Get(clauses[i].Reading);
            // 変換エンジンは、文節が「から」だけだと記号 (～) にしてしまう。記号だけの変換結果は、ひらがなの後ろに回す。
            preferred ??= IsSymbolOnly(clauses[i].Text) && clauses[i].Reading.All(c => c is >= 'ぁ' and <= 'ゖ') ? clauses[i].Reading : null;
            // 英単語に挟まれて助詞だけの文節になると、変換エンジンは漢字にしてしまう (github + に + push → 二)。助詞はかなのまま。
            preferred ??= Particles.Contains(clauses[i].Reading) && clauses[i].Text != clauses[i].Reading ? clauses[i].Reading : null;
            // カタカナの語の後ろの「っ」で始まる文節 (スパイダーマ + っ！) は、カタカナの「ッ」にする (スパイダーマッ！)。
            preferred ??= i > 0 && clauses[i].Text.StartsWith('っ') && clauses[i - 1].Text is [.., var last] && last is >= 'ァ' and <= 'ヺ' or 'ー'
                ? "ッ" + clauses[i].Text[1..] : null;
            // 文頭・記号の後ろの「え、」「え？」(聞き返し) を、変換エンジンは 得 にしてしまう (得、知らん)。かなのまま
            preferred ??= IsInterjection(clauses, i) ? clauses[i].Reading : null;
            // がち (ガチで) を、変換エンジンは 勝ち にしてしまう (勝ちでやばい)。勝ち の読みは かち なので、がち は ガチ にする
            preferred ??= clauses[i].Reading.StartsWith("がち", StringComparison.Ordinal) && clauses[i].Text.StartsWith("勝ち", StringComparison.Ordinal)
                ? "ガチ" + clauses[i].Text["勝ち".Length..] : null;
            if (preferred is not null) Prefer(clauses[i], preferred);
        }
        return clauses;
    }

    /// <summary>する の活用 (した・して・しない・しよう・したい …) で始まる読み。</summary>
    private static readonly string[] SuruForms = ["する", "すれ", "した", "して", "しま", "しな", "しよ", "しと", "しちゃ", "しろ", "され", "させ", "せず"];

    /// <summary>英単語の後ろの、する の活用の文節を、変換エンジンが漢字で始めた (した → 下、したい → 死体、しよう → 使用)。</summary>
    private static bool IsSuruAfterEnglish(Clause clause) =>
        SuruForms.Any(clause.Reading.StartsWith) && clause.Text.Length > 0 && !IsKana(clause.Text[0]) && clause.Text != clause.Reading;

    private static readonly HashSet<char> SentencePunctuation = ['、', '。', '，', '．', ',', '.', '！', '？', '!', '?', '…', '‥', '「', '」', '(', ')', '（', '）', ' ', '　'];

    /// <summary>
    /// 文節が「え」だけ (後ろに記号が付いていてもよい) で、文の頭か記号の後ろにあり、後ろが記号か文の終わりか。
    /// こういう「え」は聞き返し・驚き (え、しらん) で、絵 や 得 ではない。
    /// </summary>
    private bool IsInterjection(List<Clause> clauses, int i)
    {
        var reading = clauses[i].Reading;
        if (reading.Length == 0 || reading[0] != 'え' || !reading.Skip(1).All(SentencePunctuation.Contains) || clauses[i].Text == reading) return false;
        var before = i > 0 ? clauses[i - 1].Text : _precedingText ?? "";
        if (before.Length > 0 && !SentencePunctuation.Contains(before[^1])) return false;
        if (reading.Length > 1) return true;
        return i + 1 == clauses.Count || clauses[i + 1].Text is [var next, ..] && SentencePunctuation.Contains(next);
    }

    /// <summary>
    /// 小書きのかな (ぃ ぇ ゃ …) で始まる文節は前の文節とつなげる。小書きのかなから始まる語は無いので、
    /// 変換エンジンの知らない語を区切り間違えたもの (こうぃ → 光|ぃ)。つなげた読みは、その文節だけで変換し直す
    /// (こうぃ → コウィ。wi で打っていれば こゐ も候補に出る)。
    /// </summary>
    private IReadOnlyList<ConversionClause> JoinSmallKana(IReadOnlyList<ConversionClause> parts)
    {
        if (!parts.Skip(1).Any(p => p.Reading.Length > 0 && SmallKana.Contains(p.Reading[0]))) return parts;
        var joined = new List<ConversionClause>();
        foreach (var part in parts)
        {
            if (joined.Count > 0 && part.Reading.Length > 0 && SmallKana.Contains(part.Reading[0]))
            {
                var reading = joined[^1].Reading + part.Reading;
                // 変換しても漢字の後ろに小書きのかなが残る (光ぃ) なら、カタカナ (コウィ) にする。
                var text = Convert(reading);
                if (Enumerable.Range(1, Math.Max(0, text.Length - 1)).Any(i => SmallKana.Contains(text[i]) && !IsKana(text[i - 1]))) text = CompositionText.ToKatakana(reading);
                joined[^1] = new ConversionClause(reading, text);
            }
            else joined.Add(part);
        }
        return joined;
    }

    private const string SmallKana = "ぁぃぅぇぉゃゅょゎァィゥェォャュョヮ";

    private static bool IsKana(char c) => c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー';

    /// <summary>
    /// 文脈付きと文脈なしの両方で変換し、文節の区切りが同じなら文脈付き (漢字の選び方だけが文脈で変わる: この本は + あつい → 厚い)、
    /// 区切りまで変わるなら文脈なしを使う。前に同じ語があると、変換エンジンは文脈に引きずられて区切りを変えてしまうため
    /// (「記号等」含め、 + きごうとう → き|ごうとう → 気強盗)。
    /// </summary>
    private IReadOnlyList<ConversionClause>? ConvertWithContext(string kana, string context)
    {
        var withContext = _converter.ConvertClauses(kana, context);
        var plain = _converter.ConvertClauses(kana);
        if (withContext is null || plain is null) return withContext ?? plain;
        return withContext.Select(c => c.Reading).SequenceEqual(plain.Select(c => c.Reading)) ? withContext : plain;
    }

    /// <summary>変換エンジンに渡す文脈: キャレットの前の確定済みの文字のうち、同じ文の日本語の部分 (最大 10 文字)。</summary>
    private string? ConversionContext()
    {
        var text = _precedingText;
        if (string.IsNullOrEmpty(text) || LanguageOf(text) != false) return null;
        var start = text.LastIndexOfAny(SentenceEnds);
        text = text[(start + 1)..].Trim();
        return text.Length == 0 ? null : text.Length > 10 ? text[^10..] : text;
    }

    private static readonly char[] SentenceEnds = ['。', '！', '？', '\n', '\r'];

    private static readonly HashSet<string> Particles = ["は", "が", "を", "に", "で", "と", "も", "へ", "の", "や", "か", "から", "まで", "より"];

    private static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);

    private static void Prefer(Clause clause, string text)
    {
        clause.Candidates.Remove(text);
        clause.Candidates.Insert(0, text);
        clause.Index = 0;
    }

    /// <summary>
    /// 文節の候補: 文の中での変換結果 → その文節だけでの変換結果 → 補助辞書の同音異義語 → ひらがな → 全角カタカナ → 半角カタカナ。
    /// </summary>
    private List<string> JapaneseCandidates(string reading, string? inContext)
    {
        // ユーザー辞書に登録した単語は、文の中での変換結果の次に出す (文節の区切りを変えて読みが一致したときなど)。
        var candidates = Distinct(inContext);
        foreach (var word in _options.UserDictionary?.Lookup(reading) ?? []) if (!candidates.Contains(word)) candidates.Add(word);
        if (Convert(reading) is var standalone && !candidates.Contains(standalone)) candidates.Add(standalone);
        foreach (var extra in _options.Candidates?.Lookup(reading) ?? [])
        {
            if (!candidates.Contains(extra)) candidates.Add(extra);
        }
        var katakana = CompositionText.ToKatakana(reading);
        foreach (var kana in new[] { reading, katakana, CompositionText.ToHalfWidthKatakana(katakana) })
        {
            if (!candidates.Contains(kana)) candidates.Add(kana);
        }
        return candidates;
    }

    /// <summary>英語の文節の候補: 打ったまま → 固有名詞の正しい形 (GitHub) → 先頭だけ大文字 → すべて大文字 → 全角。</summary>
    private List<string> EnglishCandidates(string raw)
    {
        var lower = raw.ToLowerInvariant();
        var capitalized = raw.Length > 0 ? char.ToUpperInvariant(raw[0]) + raw[1..] : raw;
        return Distinct(raw, _detector.ProperNouns.Canonical(lower), capitalized, raw.ToUpperInvariant(), CompositionText.ToFullWidth(raw));
    }

    /// <summary>選んでいる候補の意味: 日本語の意味 (ウィクショナリー)、無ければ英訳 (JMdict)。</summary>
    private string? CandidateMeaning(Clause clause) =>
        _options.Meanings?.Lookup(clause.Text, clause.IsEnglish ? null : clause.Reading) ?? _options.Translations?.Meaning(clause.Text);

    /// <summary>
    /// 候補の右に小さく出す注記: 英訳 (complex)、半角 / 全角 (同じ記号・英数字の半角と全角が両方候補にあるとき。# と ＃ は見分けにくい)。
    /// 注記が 1 つも無ければ null。
    /// </summary>
    private static List<string?>? CandidateNotes(Clause clause)
    {
        var notes = clause.Candidates.Select(c =>
        {
            if (clause.Translations.Contains(c)) return "英訳";
            var half = ToHalfWidth(c);
            if (half != c && clause.Candidates.Contains(half)) return "全角";
            var full = CompositionText.ToFullWidth(c);
            if (full != c && clause.Candidates.Contains(full)) return "半角";
            return null;
        }).ToList();
        return notes.Any(n => n is not null) ? notes : null;
    }

    /// <summary>全角の英数字・記号 (！〜～) と全角の空白を半角にする。</summary>
    private static string ToHalfWidth(string text) =>
        new(text.Select(c => c is >= '！' and <= '～' ? (char)(c - 0xFEE0) : c == '　' ? ' ' : c).ToArray());

    /// <summary>英語と判定した語を、ローマ字として読んだときの候補 (go → 語 ご ゴ)。読み切れなければ空。</summary>
    private List<string> RomajiCandidates(string raw)
    {
        if (_detector.Romaji.AnalyzeFragment(raw.ToLowerInvariant()) is not { IsValid: true, Partial: "" } analysis || analysis.Kana.Length == 0) return [];
        return JapaneseCandidates(analysis.Kana, null);
    }

    /// <summary>日本語の候補 (かな・漢字) か。英語の文節で選ばれたら、その語を日本語として覚える。</summary>
    private static bool IsJapaneseText(string text) => text.Any(c => c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿');

    private static List<string> Distinct(params string?[] candidates)
    {
        var list = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && !list.Contains(candidate)) list.Add(candidate);
        }
        return list;
    }

    private void NextCandidate(int step)
    {
        var clause = _clauses[_selectedClause];
        if (!clause.IsEnglish && !clause.Expanded) Expand(clause);
        clause.Index = (clause.Index + step + clause.Candidates.Count) % clause.Candidates.Count;
        clause.Changed = true;
    }

    /// <summary>
    /// 変換中に文節を選んだときに、変換エンジンの候補の一覧 (はし → 橋 端 箸 …) を 2 番目以降に足す。
    /// 打つたびには呼ばない (時間がかかることがあるため。ライブ変換では呼ばない)。今選んでいる候補はそのまま選んだ状態にする。
    /// </summary>
    private void Expand(Clause clause)
    {
        clause.Expanded = true;
        if (_options.MoreCandidates is not { } more) return;
        var current = clause.Text;
        var merged = new List<string> { clause.Candidates[0] };
        foreach (var candidate in more(clause.Reading).Select(c => NormalizeHalfWidth(c, clause.Reading)).Concat(clause.Candidates.Skip(1)))
        {
            if (!merged.Contains(candidate)) merged.Add(candidate);
        }
        clause.Candidates = merged;
        clause.Index = Math.Max(0, merged.IndexOf(current));
    }

    /// <summary>
    /// 変換エンジンは読みの中の英数字を全角にする (2025ねん → ２０２５年、sだけが → ｓだけが) ので、
    /// 半角で打った数字・英字は半角に戻す。読みに半角の英数字が無ければ (全角を選んだ候補など) そのまま。
    /// </summary>
    internal static string NormalizeHalfWidth(string converted, string reading)
    {
        var digits = reading.Any(char.IsAsciiDigit);
        var letters = reading.Any(char.IsAsciiLetter);
        if (!digits && !letters) return converted;
        var chars = converted.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (digits && chars[i] is >= '０' and <= '９') chars[i] = (char)(chars[i] - 0xFEE0);
            else if (letters && chars[i] is >= 'Ａ' and <= 'Ｚ' or >= 'ａ' and <= 'ｚ') chars[i] = (char)(chars[i] - 0xFEE0);
        }
        return new string(chars);
    }

    /// <summary>
    /// Shift+→ / Shift+←: 選択中の文節を 1 文字伸ばす / 縮める。はみ出した・空いた分は次の文節とやり取りし、
    /// 変わった文節は読みから変換し直す。英語の文節とは区切りをやり取りしない。
    /// </summary>
    private void Resize(int delta)
    {
        var current = _clauses[_selectedClause];
        if (current.IsEnglish) return;
        var next = _selectedClause + 1 < _clauses.Count ? _clauses[_selectedClause + 1] : null;
        if (next is { IsEnglish: true }) next = null;

        if (delta > 0)
        {
            if (next is null) return;
            current.Reading += next.Reading[0];
            next.Reading = next.Reading[1..];
            if (next.Reading.Length == 0) _clauses.Remove(next);
            else Reconvert(next);
        }
        else
        {
            if (current.Reading.Length <= 1) return;
            var moved = current.Reading[^1];
            current.Reading = current.Reading[..^1];
            if (next is null)
            {
                next = new Clause(moved.ToString(), false, []);
                _clauses.Insert(_selectedClause + 1, next);
            }
            else next.Reading = moved + next.Reading;
            Reconvert(next);
        }
        Reconvert(current);
    }

    private void Reconvert(Clause clause)
    {
        clause.Candidates = JapaneseCandidates(clause.Reading, null);
        clause.Index = 0;
        clause.Changed = false;
        if (_options.History?.Get(clause.Reading) is { } learned) Prefer(clause, learned);
    }

    /// <summary>同じかなを何度も変換しないようにキャッシュする。</summary>
    private string Convert(string kana)
    {
        if (_conversionCache.TryGetValue(kana, out var cached)) return cached;
        var converted = NormalizeHalfWidth(_converter.Convert(kana) ?? kana, kana);
        if (_conversionCache.Count > 256) _conversionCache.Clear();
        _conversionCache[kana] = converted;
        return converted;
    }

    /// <summary>
    /// ライブ変換: Space を押したときと同じく、文脈・手がかり辞書・学習を使って文節ごとに変換した結果を見せる。
    /// 打つたびに呼ばれるので、かな・文脈・学習の状態が同じならキャッシュを使う。
    /// </summary>
    private string LiveConvert(string kana)
    {
        if (kana.Length < LiveConversionMinLength) return kana;
        var key = string.Join("\u0001", kana, _precedingText, _followingText, _options.History?.Version);
        if (_liveCache.TryGetValue(key, out var cached)) return cached;
        var converted = string.Concat(ConvertJapanese(kana).Select(c => c.Text));
        if (_liveCache.Count > 256) _liveCache.Clear();
        _liveCache[key] = converted;
        return converted;
    }

    /// <summary>今の表示 (ライブ変換が有効なら日本語区間は漢字に変換済み)。</summary>
    private string CurrentDisplay(bool final) =>
        _text.Display(final, _options.LiveConversion() ? LiveConvert : null);

    // ---- 確定 ----

    private void CommitIfAny()
    {
        if (!_text.IsEmpty) Commit();
    }

    private void Commit(string suffix = "", bool fixEnglish = false)
    {
        var converting = _converting && _clauses.Count > 0;
        var text = converting ? string.Concat(_clauses.Select(c => c.Text)) : CurrentDisplay(final: true);
        if (fixEnglish && !converting && _text.Mode == DisplayMode.Auto) text = FixEnglishTypo(text);
        var english = converting ? _clauses.All(c => c.IsEnglish) : _text.IsAlphanumericAt(final: true);
        var chosen = converting && _clauses.Any(c => c.Changed);
        if (_reconversion is { } selection)
        {
            if (_host.TryReplaceSelection(selection, text + suffix))
            {
                if (converting) Learn();
                ResetContext();
                ReconversionCommitted?.Invoke();
            }
            ClearComposition();
            return;
        }
        if (converting) Learn();
        else LearnLanguage();
        CommitText(text + suffix, english, _text.Raw, chosen);
    }

    /// <summary>
    /// 英単語で終わる文字列の最後の語が、よくある打ち間違い (teh、recieve) なら正しい綴りにする (Space で確定するとき)。
    /// </summary>
    private string FixEnglishTypo(string text)
    {
        if (!_options.CorrectTypos()) return text;
        var start = text.Length;
        while (start > 0 && char.IsAsciiLetter(text[start - 1])) start--;
        if (start == text.Length || _detector.EnglishAutoCorrection(text[start..]) is not { } right) return text;
        Diagnostics.Log.Decision($"英語の打ち間違いを直しました: {Diagnostics.Log.Text(text[start..])}→{Diagnostics.Log.Text(right)}");
        return text[..start] + right;
    }

    /// <summary>
    /// ユーザーが自分で英字 / かなに直して確定した語を覚える (api と打って F10 で英字にした → 次から api は英字)。
    /// 自動の判定と違う方を選んだときだけ。1 語の英字だけを対象にする。
    /// </summary>
    private void LearnLanguage()
    {
        if (_options.Languages is not { } memory) return;
        var raw = _text.Raw;
        if (raw.Length < 2 || !raw.All(char.IsAsciiLetter)) return;
        var automatic = _text.Segments(final: true);
        switch (_text.Mode)
        {
            case DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric when !automatic.All(s => s.IsEnglish):
                memory.Remember(raw, english: true, explicitChoice: true);
                break;
            case DisplayMode.Hiragana or DisplayMode.Katakana when automatic.Any(s => s.IsEnglish):
                memory.Remember(raw, english: false, explicitChoice: true);
                break;
            case DisplayMode.Auto when _text.LevelOverride is not null && automatic.All(s => s.IsEnglish):
                // 判定の強さが手動で、Tab で提案どおり英字にした。
                memory.Remember(raw, english: true, explicitChoice: true);
                break;
        }
    }


    /// <summary>
    /// 直前に確定した語。英語とも日本語とも読める語 (i, sushi) を文脈が分からないまま確定したとき、
    /// 次の語で文脈がはっきりしたら確定し直す (I want: 「胃」→「I」)。キャレットが動いたら (ほかのキー・クリック) 無効。
    /// </summary>
    private sealed record CommitRecord(string Text, string Raw, bool English, bool SpaceIntended);

    /// <summary>続けて確定した、英語とも日本語とも読める語 (古い順)。make sure you → have で全部英語に直す。</summary>
    private readonly List<CommitRecord> _correctable = [];
    private const int MaxCorrectable = 4;
    private bool _spaceStartedConversion;

    /// <summary>キャレットが動いたかもしれないとき (Meltype を通らなかったキー・クリック)。直前の語は確定し直さない。</summary>
    public void ForgetLastCommit() => _correctable.Clear();

    /// <summary>
    /// 今確定しようとしている語 (raw) で文脈がはっきりしたら、直前に確定した英語とも日本語とも読める語を確定し直す。
    /// 例: 「i」を Space で「胃」にした後に want と打つ → 「I want」、「sushi 」の後に「がすき」 → 「すしがすき」。
    /// </summary>
    private void CorrectPreviousCommit(string raw, bool english)
    {
        if (_correctable.Count == 0 || !_options.AutoCorrect()) return;
        var previous = _correctable[^1];
        List<CommitRecord> targets = [];
        string? replacement = null;
        if (!previous.English && english && _detector.IsDefinitelyEnglish(raw))
        {
            // 日本語で確定した語を英語に: 代名詞の i は I にし、Space で変換していたなら空白も入れる。
            // その前にも Space で区切って日本語にした語が続いていれば、まとめて英文に直す (make sure you have)。
            var start = _correctable.Count - 1;
            while (start > 0 && !_correctable[start - 1].English && _correctable[start - 1].SpaceIntended) start--;
            targets = _correctable.Skip(start).ToList();
            // 助詞と同じ形の短い語 (to, no) 1 語だけを、大文字で始まる語 (固有名詞) で直すことはしない (Google と Apple は日本語でもよく書く)。
            // 小文字の英単語で英文と分かったとき (let me know、do it) は直す。
            if (targets.Count == 1 && targets[0].Raw.Length <= 2 && targets[0].Raw != "i" && char.IsAsciiLetterUpper(raw.FirstOrDefault(char.IsAsciiLetter))) return;
            replacement = string.Concat(targets.Select(t => (t.Raw == "i" ? "I" : t.Raw) + (t.SpaceIntended ? " " : "")));
        }
        else if (previous.English && !english && !_detector.IsAmbiguousWord(raw) && raw.Any(char.IsAsciiLetter))
        {
            // 英語で確定した語を日本語に (Space で空白を入れていたら取る)。
            targets = [previous];
            replacement = _detector.Romaji.ConvertLenient(previous.Raw.ToLowerInvariant(), final: true);
        }
        var original = string.Concat(targets.Select(t => t.Text));
        if (replacement is null || replacement == original) return;

        Diagnostics.Log.Decision($"前後の文脈に合わせて確定し直しました: {Diagnostics.Log.Text(original)}→{Diagnostics.Log.Text(replacement)}");
        _host.DeleteBackward(original.Length);
        _host.CommitText(replacement);
        _lastCommitText = replacement;
        _correctable.Clear();
    }

    /// <summary>選び直した文節を学習する (次に同じ読みを変換したとき最初の候補にする)。</summary>
    private void Learn()
    {
        // 変換の候補から打ったままの英字 (api) を選んで確定したら、その語は次から英字にする (F10 と同じ)。
        foreach (var clause in _clauses.Where(c => !c.IsEnglish && c.Changed && c.Raw is { } raw && c.Text == raw))
        {
            _options.Languages?.Remember(clause.Raw!, english: true);
        }
        // 英語と判定した語で日本語の候補 (go → 語) を選んだら、その語は次から日本語にする (F6 と同じ)。
        foreach (var clause in _clauses.Where(c => c.IsEnglish && IsJapaneseText(c.Text) && c.Reading.All(char.IsAsciiLetter)))
        {
            _options.Languages?.Remember(clause.Reading, english: false);
        }
        // 英訳を選んだら、英訳の記録に (普通の変換の学習より弱く効く)。
        foreach (var clause in _clauses.Where(c => c.Translations.Contains(c.Text)))
        {
            _options.TranslationHistory?.Remember(clause.Reading, clause.Text);
        }
        LearnConversion();
        if (_options.History is not { } history) return;
        // 1 文字の読み (き → 記) を覚えると、関係ない変換 (き + ごうとう) まで巻き込むので 2 文字以上だけ。
        foreach (var clause in _clauses.Where(c => !c.IsEnglish && c.Changed && c.Reading.Length >= 2))
        {
            // かな・カタカナのまま確定したのは、その場限りのことが多いので覚えない。
            // 打ったままの英字を選んだのは、上で英語として覚えた。
            if (clause.Text == clause.Reading || clause.Text == CompositionText.ToKatakana(clause.Reading) || clause.Text == clause.Raw) continue;
            // 英訳は上で英訳の記録に入れた (ここで覚えると次から 1 番目に出てしまう)。
            if (clause.Translations.Contains(clause.Text)) continue;
            history.Remember(clause.Reading, clause.Text);
        }
    }

    /// <summary>
    /// 変換エンジン (Mozc) にも、確定した文節を覚えさせる。英語の文節・打ったままの英字で区切った、日本語の文節のまとまりごとに。
    /// 変換エンジンとのやり取りで入力を待たせないよう、裏で行う。
    /// </summary>
    private void LearnConversion()
    {
        if (_converter is not ILearningConverter learner) return;
        var context = ConversionContext();
        var run = new List<ConversionClause>();
        void Flush()
        {
            if (run.Count == 0) return;
            var clauses = run.ToList();
            run.Clear();
            ThreadPool.QueueUserWorkItem(_ => learner.Learn(context, clauses));
        }
        foreach (var clause in _clauses)
        {
            if (clause.IsEnglish || clause.Text == clause.Raw || clause.Translations.Contains(clause.Text) || clause.Reading.Any(char.IsAsciiLetterOrDigit))
            {
                Flush();
                continue;
            }
            run.Add(new ConversionClause(clause.Reading, clause.Text));
        }
        Flush();
    }

    /// <summary>確定して入力する。英語だったか日本語だったか・確定した文字列を、次の入力の文脈として覚えておく。</summary>
    private void CommitText(string text, bool english, string raw = "", bool chosen = false)
    {
        var spaceIntended = _spaceStartedConversion;
        _spaceStartedConversion = false;
        // 誤変換の報告を調べられるように、打った英字・読み・文節の区切りもログに残す (ログはファイルに書く設定のときだけ保存される)。
        if (!_text.IsEmpty)
        {
            var clauses = _clauses.Count > 0 ? "　文節 " + string.Join(" | ", _clauses.Select(c => $"{c.Reading}→{c.Text}")) : "";
            if (Diagnostics.Log.RecordText) Diagnostics.Log.Info($"確定の内訳: 打った英字「{_text.Raw}」　読み「{_text.AllKana(final: true)}」{clauses}");
        }
        _text.Clear();
        _converting = false;
        _clauses = [];
        if (text.Length == 0) return;
        if (_options.SpaceAroundEnglish()) text = AddSpacesAroundEnglish(text, _precedingText, _followingText);
        CorrectPreviousCommit(raw, english);
        // 英語とも日本語とも読める語を、文脈を決めずに (選び直さずに) 確定したときだけ、後で確定し直せるようにしておく。
        if (!chosen && _detector.IsAmbiguousWord(raw))
        {
            // 前の語の後に Space で区切って続けたときだけつなげる (それ以外は新しい並び)。
            if (_correctable.Count > 0 && !_correctable[^1].SpaceIntended) _correctable.Clear();
            _correctable.Add(new CommitRecord(text, raw, english, spaceIntended));
            if (_correctable.Count > MaxCorrectable) _correctable.RemoveAt(0);
        }
        else _correctable.Clear();
        _lastCommitEnglish = english;
        var joined = (_lastCommitText ?? "") + text;
        _lastCommitText = joined.Length > 20 ? joined[^20..] : joined;
        _lastCommitTime = Environment.TickCount64;
        _host.CommitText(text);
        Committed?.Invoke(text);
    }

    /// <summary>
    /// 日本語 (かな・漢字) と英単語の間に半角スペースを入れる (今日はGitHubにpush → 今日は GitHub に push)。
    /// 英字を 1 つ以上含む英数字の並び (iPhone15、C++) を英単語とみなす。数字だけ (3時) と記号 (、。「」) の隣には入れない。
    /// 確定する文字列の端は、入力欄のキャレットの前後の文字 (before・after) との間も見る。
    /// </summary>
    internal static string AddSpacesAroundEnglish(string text, string? before, string? after)
    {
        static bool IsJapanese(char c) => c is (>= '\u3040' and <= '\u30FF') or (>= '\u3400' and <= '\u4DBF') or (>= '\u4E00' and <= '\u9FFF') or (>= '\uF900' and <= '\uFAFF');
        static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '#' or '.' or '\'' or '@';
        var previous = string.IsNullOrEmpty(before) ? '\0' : before[^1];
        var next = string.IsNullOrEmpty(after) ? '\0' : after[0];
        var builder = new StringBuilder(text.Length + 8);
        // 前に確定した英単語のすぐ後ろに日本語を続けるとき (GitHub|に)
        if (IsJapanese(text[0]) && char.IsAsciiLetter(previous)) builder.Append(' ');
        var i = 0;
        while (i < text.Length)
        {
            if (!IsWordChar(text[i]))
            {
                builder.Append(text[i++]);
                continue;
            }
            var end = i;
            while (end < text.Length && IsWordChar(text[end])) end++;
            // 語の端の記号 (. ' -) は語に含めない (Hello. の . の後ろに日本語が続いても、. の前に入れない)
            var start = i;
            var stop = end;
            while (stop > start && text[stop - 1] is '.' or '\'' or '-' or '@' or '_') stop--;
            var word = text[start..stop];
            var left = start > 0 ? text[start - 1] : previous;
            var right = stop < text.Length ? text[stop] : next;
            var isWord = word.Any(char.IsAsciiLetter);
            if (isWord && IsJapanese(left) && (builder.Length == 0 || builder[^1] != ' ')) builder.Append(' ');
            builder.Append(word);
            if (isWord && IsJapanese(right)) builder.Append(' ');
            builder.Append(text, stop, end - stop);
            i = end;
        }
        // 確定した日本語のすぐ後ろが英単語のとき (キャレットの後ろの文字)
        if (IsJapanese(text[^1]) && char.IsAsciiLetter(next)) builder.Append(' ');
        return builder.ToString();
    }

    /// <summary>変換ボックスの最後が英語の区間か (Space を空白として扱うか)。</summary>
    private bool EndsWithEnglish(bool final = false) =>
        _text.Mode == DisplayMode.Auto && _text.Segments(final) is { Count: > 0 } segments && segments[^1].IsEnglish;

    private void ReplayDown(KeyEvent e)
    {
        // Meltype を通らないキーを送る = キャレットが動くかもしれないので、直前の語はもう確定し直さない。
        _correctable.Clear();
        // 握りつぶしていた Shift を先に送る (Shift+矢印 の範囲選択、Ctrl+Shift+Z など)。
        foreach (var shift in _swallowedShift)
        {
            _host.Replay(new KeyEvent(shift, 0, false, false, false, e.TimeMs));
            _replayedDown.Add(shift);
        }
        _swallowedShift.Clear();
        _host.Replay(e);
        _replayedDown.Add(e.Vk);
    }

    private void UpdateView()
    {
        if (_text.IsEmpty)
        {
            // 英数状態の判定中は何も表示しない (英語ならそのまま出るだけ)。
            _host.Hide();
            return;
        }
        if (_converting && _clauses.Count > 0)
        {
            var selected = _clauses[_selectedClause];
            // 選んでいる文節は、最初から候補の一覧をすべて出す (Space を 1 回押しただけでは一部しか出ず、選び間違えやすかった)。
            if (!selected.IsEnglish && !selected.Expanded) Expand(selected);
            _host.Show(new CompositionView(
                string.Concat(_clauses.Select(c => c.Text)),
                selected.Candidates,
                selected.Index,
                true,
                "←→ 文節　Space/↓ 候補　Shift+←→ 区切り　Enter 確定　Esc 戻る",
                _clauses.Select(c => c.Text).ToList(),
                _selectedClause,
                CandidateNotes(selected),
                _options.CandidateMeanings() ? CandidateMeaning(selected) : null,
                MisspellingSuggestion()));
        }
        else
        {
            var hint = _text.IsAlphanumeric ? "Enter 確定　Space 確定+空白　Shift+Space 日本語で変換　半角/全角 日本語に" : "Space 変換　←→ 文節　Enter 確定　F7 カタカナ　F10 英字";
            if (_text.Suggestion() is { } suggestion) hint = $"Tab → {suggestion} (英字に)　" + hint;
            _host.Show(new CompositionView(CurrentDisplay(final: false), [], -1, false, hint, Suggestion: MisspellingSuggestion()));
        }
    }

    private static bool IsShift(int vk) => vk is VirtualKeys.Shift or VirtualKeys.LShift or VirtualKeys.RShift;

    private static bool IsCommandModifier(int vk) => VirtualKeys.IsModifier(vk) && !IsShift(vk);
}
