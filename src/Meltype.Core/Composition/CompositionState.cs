// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Detection;
using Meltype.Diagnostics;
using Meltype.Engine;
using Meltype.Input;
using Meltype.Learning;
using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>入力中の文字列の処理 (<see cref="CompositionText"/>) が使う、入力欄の状態でないもの (判定器・入力欄のアプリの設定)。</summary>
internal record TextEnv(CompositionDetector Detector, Config.Settings Settings, ConversionHistory? History = null, LanguageMemory? Languages = null)
{
    /// <summary>打った英字を読み直す断片なら、読み直す元の入力 (<see cref="CompositionText.FragmentEnv"/>)。断片でなければ null。</summary>
    public FragmentOrigin? Fragment { get; init; }

    /// <summary>コードの入力欄の文字列リテラルの中か (require('jquery') などの識別子を自動で大文字にしない)。</summary>
    public bool CodeString { get; init; }

    /// <summary>入力欄の文脈。使うたびに、そのときの入力欄の文字と自分の確定の記録から決める (<see cref="CompositionContext.ContextOf"/>)。</summary>
    public required Func<TextContext> Context { get; init; }

    /// <summary>
    /// すべて小文字で打った英単語を、自動で正しい大文字小文字に直した形 (iphone → iPhone、iphone's → iPhone's)。直さないなら null。
    /// コードの文字列の中 (require('jquery')) や、ユーザーが小文字で確定して覚えた語は直さない。
    /// </summary>
    internal string? ProperNoun(string raw)
    {
        if (CodeString) return null;
        var stem = raw.EndsWith("'s", StringComparison.Ordinal) ? raw[..^2] : raw;
        if (History?.Get(stem) == stem) return null;
        return Detector.ProperNouns.AutoCanonical(raw);
    }
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

    /// <summary>キャレットの前が空白で終わる英語 ("Hello "・"GitHub ") か。英単語を確定したすぐ後 ("GitHub") は含まない。</summary>
    public bool EndsWithEnglishAndSpace => PrecedingEnglish == true && PrecedingText is [.., ' '];
}

/// <summary>打った英字を読み直す断片が、読み直す元の入力から引き継ぐもの。Languages は元の入力で使う英語 / 日本語の学習。</summary>
internal sealed record FragmentOrigin(LanguageLearning Languages);

/// <summary>
/// 変換ボックスの処理が使う、入力欄の状態でないもの (判定器・変換エンジン・辞書・学習データ・入力欄のアプリの設定・OS の機能)。出来事ごとに作って渡す。
/// </summary>
internal sealed record CompositionEnv(CompositionDetector Detector, IKanjiConverter Converter, CompositionServices Services, Config.Settings Settings)
    : TextEnv(Detector, Settings, Services.History, Services.Languages)
{
    public CompositionLearning Learning { get; } = new(Services, Detector.ProperNouns);

    /// <summary>
    /// 変換エンジンの答えを後から受け取れるフロント (<see cref="IKanjiConverter.ResultReady"/> を受けて <see cref="CompositionController.RefreshConversion"/> を呼ぶ) か。
    /// false なら答えるまで待つ (--repro・--self-test)。
    /// </summary>
    public bool LateResults { get; init; }

    /// <summary>変換エンジンの答えを待ってよい時刻 (<see cref="ConversionRequest.Deadline"/>)。</summary>
    public long Deadline { get; init; } = long.MaxValue;

    /// <summary>Space・確定の応答を待つ時間。TSF の DLL の応答期限 (800ms) より短くする。</summary>
    private const int InputBudgetMs = 400;

    internal CompositionEnv WaitingForInput() => LateResults ? this with { Deadline = Environment.TickCount64 + InputBudgetMs } : this;

    /// <summary>ライブ変換では待たない。後から結果を受け取れないフロントは、今までどおり待つ。</summary>
    internal CompositionEnv WithoutWaiting() => LateResults ? this with { Deadline = 0 } : this;

    /// <summary>
    /// 英数状態で打ち始めた文字がローマ字 (日本語) かを判定する。null なら英数状態では判定しない。
    /// 引数は (打った英字, もう続きが無いか)。
    /// </summary>
    public Func<string, bool, Verdict>? ClassifyDirect { get; init; }

    /// <summary>文字に使わずに離した親指キーの、もとの働き (変換・無変換) をしているか。かな配列を通さない。</summary>
    public bool BypassKanaLayout { get; init; }

    /// <summary>フロントが送ってきた、キャレットの前の確定済みの文字 (この出来事の前のもの。読めなければ null)。</summary>
    public string? Before { get; init; }

    /// <summary>今回の打鍵で実際に読めた前文。前の文字を安全に置き換えられる場合に限る。</summary>
    public string? EditableBefore { get; init; }

    /// <summary>フロントが送ってきた、キャレットの後ろの確定済みの文字 (読めなければ null)。</summary>
    public string? After { get; init; }

    /// <summary>入力欄で選んでいる文字 (変換キーで再変換する。フロントが送ってきたもの)。</summary>
    public string? Selection { get; init; }

    /// <summary>このキーで始める入力の、打ち始めの / を中黒 (・) にするか (<see cref="CompositionTextState.SlashAtStart"/>)。</summary>
    public bool SlashAtStart { get; init; }

    /// <summary>ライブ変換の控え (<see cref="CompositionState.LiveCache"/>)。</summary>
    public required Dictionary<string, List<TypingClause.Converted>> LiveCache { get; init; }

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

internal enum ConversionTrigger { Other, Space, Tab }

/// <summary>自分の確定の記録。Shown は確定したときにフロントが渡したキャレットの前の文字 (読めなければ null)、Text は確定した後のキャレットの前の文字 (末尾の 20 文字まで)。</summary>
internal sealed record OwnCommit(string? Shown, string Text);

/// <summary>1 回の変換 (Space などで始めてから、確定するか取り消すまで) の状態。</summary>
internal sealed class Conversion(List<Clause> clauses, ConversionTrigger startedBy, bool preferJapanese)
{
    public List<Clause> Clauses { get; set; } = clauses;

    /// <summary>選んでいる文節の番号。</summary>
    public int Selected { get; set; }

    public Clause SelectedClause => Clauses[Selected];

    /// <summary>
    /// 変換を始めたキー。Space なら語の後に空白を打とうとした、とも取れる (確定し直して英語にするときに空白を足す)。
    /// Tab なら変換中の Tab で候補を送る (issue #219)。
    /// </summary>
    public ConversionTrigger StartedBy { get; } = startedBy;

    /// <summary>Shift+Space で、英語と判定した語もローマ字として読んで変換した。届いた答えで作り直すときも同じにする。</summary>
    public bool PreferJapanese { get; } = preferJapanese;

    /// <summary>文節の区切りを動かした。届いた答えで作り直すと、ユーザーの操作が消える。</summary>
    public bool Edited { get; set; }
}

/// <summary>打ちかけの読み・英字の続きの予測の候補。Key は作ったときの打ちかけ。Index は Tab で選んでいるもの (-1 なら選んでいない)。</summary>
internal sealed class PredictionList(string key, List<Prediction> items)
{
    public string Key { get; } = key;

    public List<Prediction> Items { get; } = items;

    public int Index { get; set; } = -1;
}

/// <summary>
/// 入力欄 1 つ分の変換ボックスの状態 (データだけ)。出来事の入口は <see cref="CompositionController"/>、
/// 場面を移す・文脈を捨てるのは <see cref="CompositionLifecycle"/>。場面と一緒に終わる値は <see cref="Scene"/> に、ほかの値はここに置く。
/// </summary>
internal sealed class CompositionState
{
    public readonly CompositionOutputState Out = new();

    /// <summary>今の場面。移すのは <see cref="CompositionLifecycle"/> だけ。</summary>
    public CompositionScene Scene = new CompositionScene.Japanese();

    // かな配列 (JIS かな以外。設定が変わったら作り直す。issue #1415)
    public KanaLayoutEngine KanaLayout = new(Input.KanaLayout.Jis);

    /// <summary>ライブ変換の控え (<see cref="CompositionConversion.LiveClauses"/>)。入力を始めるときに空にする。</summary>
    public readonly Dictionary<string, List<TypingClause.Converted>> LiveCache = new(StringComparer.Ordinal);

    // 最後に押したキーの修飾キー (Shift は大文字・文節の区切り・Shift+Space に使う)。
    // 次のキーを押すまで、ほかの出来事 (離した親指キーのもとの働きなど) もこの値で処理する。
    public bool Shift;

    public bool Control;

    public bool Modifier;

    // 押したキー (離したことを受け取るまで。親指シフトの親指キーを、文字に使わずに離したかを見る)
    public readonly HashSet<int> Pressed = [];

    /// <summary>最後の確定の記録。キャレットの前が読めないときと、確定がまだ入っていないときの文脈に使う (<see cref="CompositionContext.ContextOf"/>)。キャレットが動いたら忘れる。</summary>
    public OwnCommit? Own;

    /// <summary>入力を覚えないように求める入力欄か。</summary>
    public bool PrivateField;

    /// <summary>続けて確定した、英語とも日本語とも読める語 (古い順)。make sure you → have で全部英語に直す。</summary>
    public readonly List<CommitRecord> Correctable = [];
}
