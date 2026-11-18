// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;
using Meltype.Input;
using static Meltype.Composition.CompositionController;

namespace Meltype.Composition;

/// <summary>
/// 変換ボックスの処理が使う、入力欄の状態でないもの (判定器・変換エンジン・辞書・学習データ・入力欄のアプリの設定・OS の機能)。出来事ごとに作って渡す。
/// </summary>
internal sealed record CompositionEnv(CompositionDetector Detector, IKanjiConverter Converter, CompositionServices Services, Config.Settings Settings)
{
    public CompositionLearning Learning { get; } = new(Services, Converter);

    public ConversionBudget? Budget => (Converter as IBackgroundConverter)?.Budget;

    /// <summary>漢字かな交じりの文の読み (再変換)。分からなければ null。</summary>
    public Func<string, string?> ReadingOf { get; init; } = _ => null;

    /// <summary>
    /// クリップボードの文字 (設定「クリップボードの文字を変換候補に出す」のときだけ、変換した瞬間に 1 回読む)。
    /// 読めない・文字ではない・履歴に残さない印がある (パスワード管理ソフトなど) ときは null。
    /// </summary>
    public Func<string?> Clipboard { get; init; } = () => null;

    /// <summary>今再生している曲 (曲名・アーティスト。「なう」の候補に出す。issue #1400)。読めない・再生していなければ null。</summary>
    public Func<(string Title, string Artist)?> NowPlaying { get; init; } = () => null;

    /// <summary>
    /// ユーザーが自動の判定を自分で直して確定したとき (F6〜F10 で英字 / かなにした、変換の候補から打ったままの英字・日本語を選んだ)。
    /// 誤判定の報告 (トレイの「直した誤判定を報告...」) に使う。記録を作るだけで、どこにも送らない (issue #284)。
    /// </summary>
    public Action<Correction>? Corrected { get; init; }
}

/// <summary>入力欄 1 つ分の変換ボックスの状態 (データだけ)。処理は <see cref="CompositionController"/> の関数が受け取って書き換える。</summary>
internal sealed class CompositionState(CompositionOutput output, CompositionText text)
{
    public readonly CompositionOutput Out = output;

    public readonly CompositionText Text = text;

    // かな配列 (JIS かな以外。設定が変わったら作り直す。issue #1415)
    public KanaLayoutEngine KanaLayout = new(Input.KanaLayout.Jis);

    public bool BypassKanaLayout;

    public readonly Dictionary<string, List<(string Reading, string Text)>> LiveCache = new(StringComparer.Ordinal);

    // 処理中のキーの修飾キー (Shift は大文字・文節の区切り・Shift+Space に使う)
    public bool Shift;

    public bool Control;

    public bool Modifier;

    // 押したキー (離したことを受け取るまで。親指シフトの親指キーを、文字に使わずに離したかを見る)
    public readonly HashSet<int> Pressed = [];

    public List<Clause> Clauses = [];

    public bool ConversionPrefersJapanese;

    // 文節の区切りを動かした。届いた答えで作り直すと、ユーザーの操作が消える
    public bool ClausesEdited;

    public int SelectedClause;

    public bool Converting;

    public bool? LastCommitEnglish;

    public string? LastCommitText;

    public string? PrecedingText;

    public string? FollowingText;

    /// <summary>入力欄が教えてくれた、キャレットの直前の確定済みの文字 (読めなければ null)。確定し直してよいかの判断に使う。</summary>
    public string? CaretBefore;

    public long LastCommitTime = long.MinValue / 2;

    public ReconversionSelection? Reconversion;

    // 予測変換: 打ちかけの読み・英字の続きの候補と、Tab で選んでいるもの (-1 なら選んでいない)。
    public List<Prediction> Predictions = [];

    public string PredictionKey = "";

    public int PredictionIndex = -1;

    // 英数状態で判定中の語。打鍵はすぐアプリに送り (待たせない)、ローマ字と分かったら消して変換ボックスに入れ直す。
    public readonly StringBuilder HeldLetters = new();

    public int HeldSent;

    public long HeldLastKeyTime;

    public bool PrivateField;

    public bool PrivateComposition;

    /// <summary>
    /// 入力欄の確定済みの文字を消せるか。消せない入力欄 (Linux で周りの文字の削除に対応していないアプリ) では、
    /// 確定し直すと元の文字が残ったまま書き足されてしまう (api → apiあぴ) ので、確定し直さない。
    /// </summary>
    public bool CanDeleteBackward = true;

    /// <summary>入力欄で選んでいる文字 (変換キーで再変換する。フロントが送ってきたもの)。</summary>
    public string? Selection;

    // F6〜F10 (Ctrl+U/I/O/P・無変換・ひらがなキーも) でかな・英字にしたか。次に文字を打ったら確定する。
    public bool ModeByKey;

    // Esc で戻した語は、判定として覚えない
    public bool EscReverted;

    public string? BoundKeysText;

    public Dictionary<int, int> BoundKeys = [];

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
