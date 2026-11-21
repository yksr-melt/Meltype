// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;
using Meltype.Detection;
using Meltype.Diagnostics;
using Meltype.Engine;
using Meltype.Input;
using static Meltype.Composition.CompositionController;

namespace Meltype.Composition;

/// <summary>入力中の文字列の処理 (<see cref="CompositionText"/>) が使う、入力欄の状態でないもの (判定器・入力欄のアプリの設定・ローマ字の打ち間違いの直し方)。</summary>
internal record TextEnv(CompositionDetector Detector, Config.Settings Settings, RomajiTypoCorrector? RomajiTypos)
{
    /// <summary>打った英字を読み直す断片なら、読み直す元の入力 (<see cref="CompositionText.FragmentEnv"/>)。断片でなければ null。</summary>
    public FragmentOrigin? Fragment { get; init; }

    /// <summary>入力欄の文脈。使うたびに、そのときの入力欄の文字と自分の確定の記録から決める (<see cref="CompositionController.ContextOf"/>)。</summary>
    public required Func<TextContext> Context { get; init; }
}

/// <summary>
/// 入力欄の文脈。PrecedingText・FollowingText はキャレットの前後の確定済みの文字列 (変換エンジンに渡す・英単語の前後の空白を決める)。
/// PrecedingEnglish・FollowingEnglish は、前・後ろの文字が英語なら true、日本語なら false、分からなければ null。英語とも日本語とも読める語 (sushi など) の扱いを決める。
/// PrecedingEnglishSentence は、キャレットの前が英文の途中 ("I want ") か。日本語の文の中の英単語 (GitHub の) より強い英語の根拠。
/// PrecedingEnglishName は、キャレットの前が名前を言う英文 ("my name is ") か。
/// </summary>
internal sealed record TextContext(string? PrecedingText, bool? PrecedingEnglish, bool PrecedingEnglishSentence, bool PrecedingEnglishName, string? FollowingText, bool? FollowingEnglish)
{
    public static readonly TextContext None = new(null, null, false, false, null, null);
}

/// <summary>打った英字を読み直す断片が、読み直す元の入力から引き継ぐもの。LearningContext は元の入力の学習の文脈。</summary>
internal sealed record FragmentOrigin(bool? LearningContext);

/// <summary>
/// 変換ボックスの処理が使う、入力欄の状態でないもの (判定器・変換エンジン・辞書・学習データ・入力欄のアプリの設定・OS の機能)。出来事ごとに作って渡す。
/// </summary>
internal sealed record CompositionEnv(CompositionDetector Detector, IKanjiConverter Converter, CompositionServices Services, Config.Settings Settings)
    : TextEnv(Detector, Settings, Services.RomajiTypos)
{
    public CompositionLearning Learning { get; } = new(Services, Converter);

    /// <summary>
    /// 変換エンジンの答えを後から受け取れるフロント (<see cref="IKanjiConverter.ResultReady"/> を受けて RefreshConversion を呼ぶ) か。
    /// false なら答えるまで待つ (--repro・--self-test)。
    /// </summary>
    public bool LateResults { get; init; }

    /// <summary>変換エンジンの答えを待ってよい時刻 (<see cref="ConversionRequest.Deadline"/>)。</summary>
    public long Deadline { get; init; } = long.MaxValue;

    /// <summary>
    /// 英数状態で打ち始めた文字がローマ字 (日本語) かを判定する。null なら英数状態では判定しない。
    /// 引数は (打った英字, もう続きが無いか)。
    /// </summary>
    public Func<string, bool, Verdict>? ClassifyDirect { get; init; }

    /// <summary>文字に使わずに離した親指キーの、もとの働き (変換・無変換) をしているか。かな配列を通さない。</summary>
    public bool BypassKanaLayout { get; init; }

    /// <summary>フロントが送ってきた、キャレットの前の確定済みの文字 (この出来事の前のもの。読めなければ null)。</summary>
    public string? Before { get; init; }

    /// <summary>フロントが送ってきた、キャレットの後ろの確定済みの文字 (読めなければ null)。</summary>
    public string? After { get; init; }

    /// <summary>入力欄で選んでいる文字 (変換キーで再変換する。フロントが送ってきたもの)。</summary>
    public string? Selection { get; init; }

    /// <summary>
    /// 入力欄の確定済みの文字を消せるか。消せない入力欄 (Linux で周りの文字の削除に対応していないアプリ) では、
    /// 確定し直すと元の文字が残ったまま書き足されてしまう (api → apiあぴ) ので、確定し直さない。
    /// </summary>
    public bool CanDeleteBackward { get; init; } = true;

    /// <summary>出来事を処理する時刻 (ミリ秒)。</summary>
    public long NowMs { get; init; }

    /// <summary>漢字かな交じりの文の読み (再変換)。分からなければ null。</summary>
    public Func<string, string?> ReadingOf { get; init; } = _ => null;

    /// <summary>
    /// クリップボードの文字 (設定「クリップボードの文字を変換候補に出す」のときだけ、変換した瞬間に 1 回読む)。
    /// 読めない・文字ではない・履歴に残さない印がある (パスワード管理ソフトなど) ときは null。
    /// </summary>
    public Func<string?> Clipboard { get; init; } = () => null;

    /// <summary>今再生している曲 (曲名・アーティスト。「なう」の候補に出す。issue #1400)。読めない・再生していなければ null。</summary>
    public Func<(string Title, string Artist)?> NowPlaying { get; init; } = () => null;

    /// <summary>Windows に登録されている、別のビルドの Meltype IME の版 (<see cref="Ime.IImeEnvironment.OtherTipVersion"/>。「ばーじょん」の候補に出す)。</summary>
    public Func<string?> OtherTipVersion { get; init; } = () => null;

    /// <summary>
    /// ユーザーが自動の判定を自分で直して確定したとき (F6〜F10 で英字 / かなにした、変換の候補から打ったままの英字・日本語を選んだ)。
    /// 誤判定の報告 (トレイの「直した誤判定を報告...」) に使う。記録を作るだけで、どこにも送らない (issue #284)。
    /// </summary>
    public Action<Correction>? Corrected { get; init; }
}

/// <summary>入力欄 1 つ分の変換ボックスの状態 (データだけ)。処理は <see cref="CompositionController"/> の関数が受け取って書き換える。</summary>
internal sealed class CompositionState
{
    public readonly CompositionOutputState Out = new();

    public readonly CompositionTextState Text = new();

    // かな配列 (JIS かな以外。設定が変わったら作り直す。issue #1415)
    public KanaLayoutEngine KanaLayout = new(Input.KanaLayout.Jis);

    /// <summary>英数 (直接入力) か。true の間はキーをすべてアプリに渡す (Mac の「英数」キー、「かな」キーで戻す)。</summary>
    public bool Direct;

    /// <summary>英数状態で打ち始めた語を英語と判定して、その続きを打っているか。</summary>
    public bool DirectEnglishWord;

    public readonly Dictionary<string, List<(string Reading, string Text)>> LiveCache = new(StringComparer.Ordinal);

    // 最後に押したキーの修飾キー (Shift は大文字・文節の区切り・Shift+Space に使う)。
    // 次のキーを押すまで、ほかの出来事 (離した親指キーのもとの働きなど) もこの値で処理する。
    public bool Shift;

    public bool Control;

    public bool Modifier;

    // 押したキー (離したことを受け取るまで。親指シフトの親指キーを、文字に使わずに離したかを見る)
    public readonly HashSet<int> Pressed = [];

    // 変換中の文節 (変換していなければ空)
    public List<Clause> Clauses = [];

    public bool ConversionPrefersJapanese;

    // 文節の区切りを動かした。届いた答えで作り直すと、ユーザーの操作が消える
    public bool ClausesEdited;

    public int SelectedClause;

    public bool? LastCommitEnglish;

    public string? LastCommitText;

    public long LastCommitTime = long.MinValue / 2;

    public ReconversionSelection? Reconversion;

    // 予測変換: 打ちかけの読み・英字の続きの候補と、Tab で選んでいるもの (-1 なら選んでいない)。
    public List<Prediction> Predictions = [];

    public string PredictionKey = "";

    public int PredictionIndex = -1;

    // 英数状態で判定中の語。打鍵はすぐアプリに送り (待たせない)、ローマ字と分かったら消して変換ボックスに入れ直す。
    public readonly StringBuilder HeldLetters = new();

    // 英数状態で判定中の語のうち、アプリに通した文字 (文字の無いキーは \0)
    public readonly StringBuilder HeldSent = new();

    public long HeldLastKeyTime;

    public bool PrivateField;

    public bool PrivateComposition;

    // F6〜F10 (Ctrl+U/I/O/P・無変換・ひらがなキーも) でかな・英字にしたか。次に文字を打ったら確定する。
    public bool ModeByKey;

    // Esc で戻した語は、判定として覚えない
    public bool EscReverted;

    /// <summary>続けて確定した、英語とも日本語とも読める語 (古い順)。make sure you → have で全部英語に直す。</summary>
    public readonly List<CommitRecord> Correctable = [];

    public bool SpaceStartedConversion;

    // 確定し直しで消す文字数と、入れ直す文字。今確定する文字と一緒に 1 回で送る。
    public (int Count, string Text)? PendingReplace;

    /// <summary>今の変換を Tab で始めたか (変換中の Tab で候補を送るため。issue #219)。</summary>
    public bool TabStartedConversion;

    // 続けて打つ前に選んでいた候補 (読み → 文字)。確定するか、変換ボックスを空にするまで。
    public readonly Dictionary<string, string> KeptChoices = [];
}
