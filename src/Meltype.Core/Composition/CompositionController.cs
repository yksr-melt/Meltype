// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>
/// 変換ボックスに表示する内容。変換中は文節ごとの文字列 (Clauses) と選択中の文節、その文節の候補を持つ。
/// <para>
/// Committed は、この表示を確定したときに文書へ入る文字列。IBus はフォーカスが外れたとき
/// 「表示中の preedit」をそのまま確定するので、表示と確定が違うと文書と本体の認識がずれる
/// (「かn」を表示したまま確定すると、本体は「かん」で確定していて食い違う)。
/// </para>
/// <para>
/// ContextHint は入力内容や変換エンジンに応じた案内 (手動の英字提案・予測候補の選択など)。固定の操作一覧は含めない。
/// 書き間違いの修正は Suggestion に入れる。案内が無ければ null。
/// </para>
/// Notes は Candidates と同じ順序・件数の注記。注記がない候補は None、リスト全体を省略するときは null。
/// 入力欄に入りうる文字の欄 (Text・Candidates・Clauses・Suggestion・Predictions・Committed) は、CompositionOutput.Show が設定の句読点にする。
/// そういう欄を足したら Show にも足す。
/// </summary>
public sealed record CompositionView(
    string Text,
    IReadOnlyList<string> Candidates,
    int SelectedIndex,
    bool Converting,
    string Hint,
    IReadOnlyList<string>? Clauses = null,
    int SelectedClause = -1,
    IReadOnlyList<CandidateNote>? Notes = null,
    string? Meaning = null,
    string? Suggestion = null,
    IReadOnlyList<string>? Predictions = null,
    int SelectedPrediction = -1,
    string? Typed = null,
    string? Committed = null,
    string? ContextHint = null,
    // 一覧に番号を出すか (数字キーで選べる変換中の候補だけ。SessionResult.ForFront が決める)
    bool Numbered = false);

/// <summary>キーを 1 つ押した。Ch は入力する文字 (キーボード配列は OS 側で解決済み。文字を生まないキーなら null)。</summary>
public readonly record struct KeyPress(int Vk, char? Ch, bool Shift, bool Control, bool Alt, bool Command, long TimeMs)
{
    public bool Modifier => Control || Alt || Command;
}

/// <summary>選択中の確定済みの文字と、その読み (再変換)。</summary>
public sealed record ReconversionSelection(string Text, string Reading);

/// <summary>
/// 入力欄 1 つ分の変換ボックス (未確定文字列) の本体 (どのフロントも <see cref="MeltypeSession"/> から使う)。責務ごとにファイルを分けている (CompositionController.*.cs)。
///   文字キー → ボックスに追加 (日本語ならかな、英単語なら英字で自動表示)
///   Space   → 文節に区切って漢字変換 (英単語で終わっているときは確定して空白)
///   Shift+Space → 英単語と判定した語も、ローマ字として読んで変換 (go → 語)
///   ←→      → 文節を選ぶ (変換前に押しても文節の選択に入る) / Space・↓↑ でその文節の候補 / Shift+←→ で区切りを変える
///   Enter   → 確定してテキストボックスへ入力
///   BackSpace / Esc → 1 音削除 / 変換取り消し・入力取り消し
///   F6 / F7 / F8 / F9 / F10, 半角/全角 → ひらがな / カタカナ / 半角カタカナ / 全角英数 / 半角英数 (続けて押すと 大文字 → 先頭だけ大文字) / 日本語⇔英字
///   その他のキー → 確定してからそのキーを通す
/// 英数状態でも、打ち始めの数文字でローマ字 (日本語) かを判定し (打鍵は待たせずに送る)、日本語なら送った分を消して日本語入力に戻し、変換ボックスに入れる。
/// 出来事を 1 つ受け取るたびに、確定・表示・キーをアプリに通すかを <see cref="SessionResult"/> で返す。1 つのスレッドから使う。
/// </summary>
public sealed partial class CompositionController
{
    /// <summary>
    /// ライブ変換は 4 文字以上のかなだけ。短い断片は変換エンジンが的外れな漢字を返しやすい
    /// (きょ → 居, きょう → 喬) ので、打っている途中はかなのまま見せる。短い語は Space で変換する。
    /// </summary>
    private const int LiveConversionMinLength = 4;

    /// <summary>Space・確定など、結果が今すぐ要る 1 回の入力で変換エンジンを待つ時間の合計。Meltype IME の DLL の応答期限 (800ms、TextService.h) より短くする。</summary>
    private const int InputBudgetMs = 400;

    /// <summary>候補の一覧の 1 ページの数 (変換ボックス・Linux の候補の一覧と合わせる)。数字キー 1〜9 でこのページの中から選ぶ。</summary>
    internal const int CandidatePageSize = 9;

    /// <summary>英数状態の判定で、この時間打鍵が無ければ英語とみなして判定をやめる。</summary>
    private const long DirectHoldIdleMs = 700;

    /// <summary>自分が確定してから、この時間内はアプリ側のテキストがまだ更新されていないかもしれないので自分の記録を優先する。</summary>
    private const long OwnCommitTrustMs = 1500;

    private readonly CompositionOutput _out;
    private readonly CompositionText _text;
    // かな配列 (JIS かな以外。設定が変わったら作り直す。issue #1415)
    private KanaLayoutEngine _kanaLayout = new(KanaLayout.Jis);
    private bool _bypassKanaLayout;
    private readonly CompositionDetector _detector;
    private readonly IKanjiConverter _converter;
    private readonly Func<Config.Settings> _settings;
    private readonly CompositionServices _services;
    private readonly CompositionLearning _learning;
    private readonly Dictionary<string, List<(string Reading, string Text)>> _liveCache = new(StringComparer.Ordinal);
    private readonly ConversionBudget? _budget;
    // 処理中のキーの修飾キー (Shift は大文字・文節の区切り・Shift+Space に使う)
    private bool _shift;
    private bool _control;
    private bool _modifier;
    // 押したキー (離したことを受け取るまで。親指シフトの親指キーを、文字に使わずに離したかを見る)
    private readonly HashSet<int> _pressed = [];
    private List<Clause> _clauses = [];
    private bool _conversionPrefersJapanese;
    // 文節の区切りを動かした。届いた答えで作り直すと、ユーザーの操作が消える
    private bool _clausesEdited;
    private int _selectedClause;
    private bool _converting;
    private bool? _lastCommitEnglish;
    private string? _lastCommitText;
    private string? _precedingText;
    private string? _followingText;
    /// <summary>入力欄が教えてくれた、キャレットの直前の確定済みの文字 (読めなければ null)。確定し直してよいかの判断に使う。</summary>
    private string? _caretBefore;
    private long _lastCommitTime = long.MinValue / 2;
    private ReconversionSelection? _reconversion;

    // 予測変換: 打ちかけの読み・英字の続きの候補と、Tab で選んでいるもの (-1 なら選んでいない)。
    private List<Prediction> _predictions = [];
    private string _predictionKey = "";
    private int _predictionIndex = -1;

    /// <summary>
    /// 予測変換の候補。Text は一覧に出す語、Before は選んだときに語の前に残す文字 (最後の文節の続きの候補なら前の文節の見えている文字、
    /// 読み全体の続きの候補なら空)、Reading は語に置き換わる打ちかけの読み。
    /// </summary>
    private sealed record Prediction(string Text, string Before, string Reading);

    // 英数状態で判定中の語。打鍵はすぐアプリに送り (待たせない)、ローマ字と分かったら消して変換ボックスに入れ直す。
    private readonly StringBuilder _heldLetters = new();
    private int _heldSent;
    private long _heldLastKeyTime;

    /// <summary>変換中の文節。英語の区間も 1 つの文節として扱う (候補は英字/全角英字)。</summary>
    internal sealed class Clause(string reading, bool isEnglish, List<string> candidates)
    {
        public string Reading { get; set; } = reading;
        public bool IsEnglish { get; } = isEnglish;
        public List<string> Candidates { get; set; } = candidates;
        public int Index { get; set; }
        public string Text => Candidates[Index];

        /// <summary>ユーザーが候補を選び直したか (確定時に学習する)。</summary>
        public bool Changed { get; set; }

        /// <summary>変換エンジンが返したときの Zenzai の答え (Zenzai の答えでなければ null)。今の文字列がこれと同じなら「Zenzai」と出す。</summary>
        public string? ZenzaiText { get; init; }

        /// <summary>外部変換エンジンの候補追加が完了したか。<see cref="Fixed"/> の文節では追加せずに完了扱いにする。</summary>
        public bool Expanded { get; set; }

        /// <summary>ユーティリティ変換の Replace の文節。候補を足さず、文節の区切りも動かさない。</summary>
        public bool Fixed { get; init; }

        /// <summary>候補と同じ順・件数の注記 (ユーティリティ変換が決めたもの)。null なら候補から決める (<see cref="CandidateNotes"/>)。</summary>
        public IReadOnlyList<CandidateNote>? Notes { get; init; }

        /// <summary>候補のうち、選んでも学習しないユーティリティ変換の候補 (<see cref="Utility.UtilityCandidates.NotLearned"/>)。</summary>
        public HashSet<string> NotLearned { get; } = new(StringComparer.Ordinal);

        /// <summary>この文節の読みを打ったときの英字 (あぴ → api)。分からなければ null。</summary>
        public string? Raw { get; set; }

        /// <summary>英語の文節を、打った英字をローマ字として読んだかな (thin → てぃん)。文節の区切りを動かすときに日本語の文節として読み直すのに使う。</summary>
        public string? Kana { get; set; }

        /// <summary>候補のうち英訳 (複雑な → complex) のもの。</summary>
        public HashSet<string> Translations { get; } = new(StringComparer.Ordinal);
    }

    public CompositionController(CompositionDetector detector, IKanjiConverter converter, Func<Config.Settings> settings, CompositionServices? services = null)
    {
        _text = new CompositionText(detector);
        _detector = detector;
        _converter = converter;
        _budget = (converter as IBackgroundConverter)?.Budget;
        _settings = settings;
        _out = new CompositionOutput(() => settings().Punctuation);
        _services = services ?? new CompositionServices();
        _learning = new CompositionLearning(_services, converter);
        _text.Manual = () => !_settings().AutoDetection;
        _text.TypoCorrector = _services.RomajiTypos;
        // よく使う日本語の読み (kyouha = 今日は) は、一度英字にして確定しただけでは英語として覚えない。
        if (_services.Languages is { } languages && _services.RomajiTypos is { } typos) languages.IsCommonJapanese ??= typos.IsCommonJapanese;
        if (_services.Languages is { } memory) memory.IsReadableRomaji ??= word => detector.Romaji.AnalyzeFragment(word) is { IsValid: true, Partial: "" };
        if (_services.RomajiTypos is { } lexicon) detector.IsCommonJapanese ??= lexicon.IsCommonJapanese;
        _text.CorrectTypos = () => _settings().CorrectTypos;
        _text.Slash = () => _settings().Slash;
        _text.ZSymbols = () => _settings().ZSymbols;
    }

    /// <summary>入力を覚えないように求める入力欄か。true の間は学習しない。</summary>
    public bool Private { get; set; }

    private bool Learning => _settings().LearningEnabled && !Private;

    private bool KanaInput => _settings().InputStyle == Config.InputStyle.Kana;

    /// <summary>英語とも日本語とも読める語を、次の語の文脈に合わせて確定し直すか。</summary>
    private bool AutoCorrect => _settings().AutoCorrectAfterCommit && _settings().AutoDetection;

    /// <summary>変換ボックスに入力中か、英数状態で打ち始めの語を判定中か。</summary>
    public bool IsComposing => !_text.IsEmpty || _heldLetters.Length > 0;

    /// <summary>変換して候補を選んでいるか。</summary>
    public bool IsConverting => _converting;

    /// <summary>直近に確定した文字列 (テスト・ログ用)。</summary>
    public event Action<string>? Committed;

    /// <summary>
    /// 入力欄の確定済みの文字を消せるか。消せない入力欄 (Linux で周りの文字の削除に対応していないアプリ) では、
    /// 確定し直すと元の文字が残ったまま書き足されてしまう (api → apiあぴ) ので、確定し直さない。
    /// </summary>
    public bool CanDeleteBackward { get; set; } = true;

    /// <summary>入力欄で選んでいる文字 (変換キーで再変換する。フロントが送ってきたもの)。</summary>
    public string? Selection { get; set; }

    /// <summary>漢字かな交じりの文の読み (再変換。OS の機能)。分からなければ null。</summary>
    public Func<string, string?> ReadingOf { get; set; } = _ => null;

    /// <summary>
    /// クリップボードの文字 (設定「クリップボードの文字を変換候補に出す」のときだけ、変換した瞬間に 1 回読む)。
    /// 読めない・文字ではない・履歴に残さない印がある (パスワード管理ソフトなど) ときは null。
    /// </summary>
    public Func<string?> Clipboard { get; set; } = () => null;

    /// <summary>今再生している曲 (曲名・アーティスト。「なう」の候補に出す。issue #1400)。読めない・再生していなければ null。</summary>
    public Func<(string Title, string Artist)?> NowPlaying { get; set; } = () => null;

    /// <summary>
    /// キーを押した。before / after は入力欄のキャレットの前後の確定済みの文字 (分かれば)。
    /// 変換ボックスに入力していないときは、入力を始めるキー (<see cref="StartsComposition"/>) だけを受け取り、ほかはアプリに通す。
    /// releaseFollows なら、離したことは <see cref="Release"/> で届く (false なら押した直後に離したとみなす)。
    /// </summary>
    public SessionResult Press(KeyPress key, string? before = null, string? after = null, bool releaseFollows = false)
    {
        _out.Begin(before, after);
        Tick(key.TimeMs);
        var consumed = Handle(key);
        if (!releaseFollows) HandleRelease(key.Vk);
        Settle();
        return Result(consumed);
    }

    /// <summary>ワークアラウンド: releaseFollows を付けて押したキーを離した (親指シフトの親指キー・中指NICOLA のシフトのキー)。</summary>
    public SessionResult Release(int vk, string? before = null, string? after = null)
    {
        _out.Begin(before, after);
        HandleRelease(vk);
        Settle();
        // 離したことはフロントが押下のとおりに通す。Consumed が false なら、離したことで今のキーの働きをした (文字に使わずに離した親指キー)
        return Result(consumed: true);
    }

    /// <summary>キーをアプリに通す。変換ボックスの状態は変えない (英数状態で判定中の語は、しばらく打たなければ英語とみなす)。</summary>
    public SessionResult Pass(long nowMs)
    {
        _out.Begin(null, null);
        Tick(nowMs);
        return Result(consumed: false);
    }

    /// <summary>入力の内容を変えない出来事 (フォーカス・キャレットの知らせなど) の結果。表示は今のまま。</summary>
    public SessionResult Current()
    {
        _out.Begin(null, null);
        return Result(consumed: true) with { Consumed = false };
    }

    /// <summary>変換ボックスの表示 (フロントに渡す形にする前のもの。テストで見る)。出していなければ null。</summary>
    internal CompositionView? View => _out.View;

    private SessionResult Result(bool consumed) => _out.Result(consumed && !_out.PassedCurrent, _settings());

    /// <summary>キーを 1 つ処理する。アプリに通さないなら true。</summary>
    private bool Handle(KeyPress key)
    {
        (_shift, _control, _modifier) = (key.Shift, key.Control, key.Modifier);
        if (!IsComposing && !_kanaLayout.ThumbHeld && (key.Modifier || !StartsComposition(key))) return false;
        _budget?.Start(InputBudgetMs);
        _pressed.Add(key.Vk);
        HandleKey(new KeyEvent(key.Vk, key.Ch ?? 0, false, false, false, key.TimeMs));
        return true;
    }

    /// <summary>変換ボックスを開くキーか。変換キーは選んだ文字の再変換、親指シフトのかな配列の 無変換・変換 は親指キー。</summary>
    private bool StartsComposition(KeyPress key)
    {
        var direct = _services.DirectMode();
        if (key.Vk == VirtualKeys.Convert && Selection is { Length: > 0 }) return true;
        if (key.Vk is VirtualKeys.Convert or VirtualKeys.NonConvert && !direct && KanaInput &&
            _settings().KanaLayout is KanaLayout.Nicola or KanaLayout.NakayubiNicola or KanaLayout.Tron) return true;
        // かな入力: かなのキーならすべて入力を始める (StartWith)
        if (KanaInput && !direct && Detection.KanaDetector.IsKanaKey(key.Vk)) return true;
        // 日本語入力の Shift+Space・設定「スペースの幅」が全角の Space は、全角スペースにする (StartWith)
        if (key.Vk == VirtualKeys.Space) return key.Shift || !direct && _settings().SpaceWidth == Config.SpaceWidth.FullWidth;
        if (key.Ch is not { } c) return false;
        if (VirtualKeys.IsLetter(key.Vk) && char.IsAsciiLetter(c)) return true;
        // 句読点・かぎかっこ・長音・数字・記号 (Shift で打つものも)
        return StartsWithSymbol(c);
    }

    private void HandleRelease(int vk)
    {
        var pressed = _pressed.Remove(vk);
        if (!IsComposing && !_kanaLayout.ThumbHeld) return;
        var kanaUp = _kanaLayout.Up(vk);
        // 中指NICOLA: シフトのキーを押したまま先に離した文字キーを、シフト面で打ち直す
        if (kanaUp.Stroke.Handled && IsComposing) ApplyKanaStroke(kanaUp.Stroke);
        // 親指シフト: 文字に使わずに離した親指キーは、もとの働き (変換・無変換) をする
        if (kanaUp.ThumbTap && pressed)
        {
            _bypassKanaLayout = true;
            try { HandleKey(new KeyEvent(vk, 0, false, false, false, 0)); }
            finally { _bypassKanaLayout = false; }
        }
    }

    /// <summary>出来事を処理した後。表示を作り直し、入力が終わったら再変換の状態と押したキーを忘れる。</summary>
    private void Settle()
    {
        UpdateView();
        // 親指キーを押したままなら、離したことを受け取るまで受け取り続ける (文頭で親指キーだけを押したとき。変換ボックスは空)
        if (IsComposing || _kanaLayout.ThumbHeld) return;
        // 読みをすべて削除した場合も、元の選択範囲は置換せず再変換を終了する。
        if (_reconversion is not null) ClearComposition();
        _pressed.Clear();
    }

    /// <summary>
    /// 背景の変換の結果が届いた。入力スレッドで呼ぶ。変換エンジンが使えなかった間の結果を捨てて、入力中の表示を作り直す。
    /// 表示を作り直したなら true。
    /// </summary>
    public SessionResult? RefreshConversion()
    {
        _out.Begin(null, null);
        return Refresh() ? Result(consumed: true) : null;
    }

    private bool Refresh()
    {
        _liveCache.Clear();
        if (_text.IsEmpty || _converting && !RefreshOpenConversion()) return false;
        // 文節が分かると最後の文節の予測が出せる。予測を選んでいる間は、選んでいる候補を置き換えない
        if (_predictionIndex < 0) _predictionKey = "\u0000";
        UpdateView();
        return true;
    }

    /// <summary>
    /// 候補を開いた後に届いた答えを反映する。文節の区切りを動かしたあとは置き換えない。
    /// 文節の並びが変わる答えは、候補を選び直したか、別の文節へ移動したあとなら置き換えない (選んでいる文節がずれる)。
    /// 候補を選び直した文節は、選んでいる候補を変えずに、新しい候補を上に入れる。何も変わらなければ false。
    /// </summary>
    private bool RefreshOpenConversion()
    {
        if (_clausesEdited || _reconversion is not null || _clauses.Count == 0) return false;
        var (old, selected, tabStarted) = (_clauses, _selectedClause, _tabStartedConversion);
        WithoutWaiting(() =>
        {
            StartConversion(_conversionPrefersJapanese);
            return true;
        });
        _tabStartedConversion = tabStarted;
        var sameClauses = _clauses.Count == old.Count && _clauses.Zip(old).All(p => p.First.Reading == p.Second.Reading);
        var nothingNew = sameClauses && _clauses.Zip(old).All(p => p.First.Text == p.Second.Text && p.First.Candidates.All(p.Second.Candidates.Contains));
        if (nothingNew || !sameClauses && (selected != 0 || old.Any(c => c.Changed)))
        {
            _clauses = old;
            _selectedClause = selected;
            return false;
        }
        if (sameClauses)
        {
            foreach (var (fresh, previous) in _clauses.Zip(old))
            {
                if (!previous.Changed) continue;
                fresh.Candidates = Distinct([.. fresh.Candidates, .. previous.Candidates]);
                fresh.Index = fresh.Candidates.IndexOf(previous.Text);
                fresh.Changed = true;
                fresh.Translations.UnionWith(previous.Translations);
                fresh.NotLearned.UnionWith(previous.NotLearned);
            }
        }
        _selectedClause = selected;
        return true;
    }

    /// <summary>英数状態で判定中の語が、無入力のまま一定時間たったら英語とみなす。</summary>
    private void Tick(long nowMs)
    {
        if (_heldLetters.Length == 0 || nowMs - _heldLastKeyTime < DirectHoldIdleMs) return;
        DecideHeld(final: true);
    }

    public void AbandonDirectHold() => ClearHeld();

    /// <summary>変換中の文節の候補を番号で選ぶ (Mac の候補ウィンドウをクリックしたときなど)。</summary>
    public SessionResult SelectCandidate(int index)
    {
        _out.Begin(null, null);
        SelectCandidateAt(index);
        return Result(consumed: true);
    }

    private void SelectCandidateAt(int index)
    {
        // 入力中に出した候補 (設定「入力中も変換候補をいくつか表示」) をクリックしたら、変換を始めてからその候補を選ぶ
        // 入力中の候補は読み全体の候補なので、変換を始めて文節に分かれると一覧が変わり、番号が別の語を指す。
        // 見えていた一覧のまま、読み全体 1 つの文節にして、クリックした語を選ぶ
        if (!_converting && TypingCandidates() is { } typing)
        {
            if (index < 0 || index >= typing.Count) return;
            _clauses = [new Clause(_text.AllKana(final: false), false, typing) { Raw = _text.Raw, Expanded = true }];
            _selectedClause = 0;
            _converting = true;
        }
        if (!_converting || _clauses.Count == 0) return;
        var clause = _clauses[_selectedClause];
        if (index < 0 || index >= clause.Candidates.Count) return;
        clause.Index = index;
        clause.Changed = true;
        UpdateView();
    }

    /// <summary>予測変換の候補を番号で選んで確定する (Meltype IME の候補の一覧をクリックしたとき)。</summary>
    public SessionResult CommitPredictionAt(int index)
    {
        _out.Begin(null, null);
        var predictions = CurrentPredictions();
        if (!_converting && index >= 0 && index < predictions.Count)
        {
            CommitPrediction(predictions[index]);
            UpdateView();
        }
        return Result(consumed: true);
    }

    /// <summary>
    /// 無効化・フォーカス喪失などで、未確定の内容をそのまま確定する。consumed: 出来事をアプリに通さないか。
    /// preserveLatinRaw: 結合アクセントが続くので、明示的に選択していない ASCII 英字の原文を確定する。preserveText: 確定する文字列に自動空白を加えない。
    /// </summary>
    public SessionResult CommitPending(bool consumed = true, bool preserveLatinRaw = false, bool preserveText = false)
    {
        _out.Begin(null, null);
        CommitAll(preserveLatinRaw, preserveText);
        return Result(consumed);
    }

    private void CommitAll(bool preserveLatinRaw, bool preserveText)
    {
        _budget?.Start(InputBudgetMs);
        if (_heldLetters.Length > 0) ReleaseHeldAsEnglish();
        // e + 結合アクセントを「え + アクセント」にしない。明示した表示モード・候補・かな入力は尊重する。
        // ASCII 英字だけの原文に Latin アクセントが続く場合に限り、変換や学習を経由せず確定する。
        if (preserveLatinRaw && !_converting && _reconversion is null && !_text.KanaInput &&
            _text.Mode is DisplayMode.Auto or DisplayMode.HalfWidthAlphanumeric &&
            !_text.IsEmpty && _text.Raw.All(char.IsAsciiLetter))
        {
            _correctable.Clear();
            CommitText(_text.Raw, english: true, preserveText: true, rule: "英字にアクセント記号が続くので打ったまま");
        }
        else CommitIfAny(preserveText);
        UpdateView();
    }

    /// <summary>変換中の文節・候補・選択中の文節をすべて消す。再変換の状態も消す。</summary>
    private void ClearComposition()
    {
        _reconversion = null;
        _text.Clear();
        _converting = false;
        _clauses = [];
        _spaceStartedConversion = false;
        _tabStartedConversion = false;
    }

    /// <summary>フォーカスが変わったときなど。前の入力欄の文脈を持ち越さない。</summary>
    public void ResetContext()
    {
        _lastCommitEnglish = null;
        _lastCommitText = null;
        _caretBefore = null;
        _correctable.Clear();
    }
}
