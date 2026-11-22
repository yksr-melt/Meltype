// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Dictionary;
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
internal readonly record struct KeyPress(int Vk, char? Ch, bool Shift, bool Control, bool Alt, bool Command, long TimeMs)
{
    public bool Modifier => Control || Alt || Command;
}

/// <summary>選択中の確定済みの文字と、その読み (再変換)。</summary>
internal sealed record ReconversionSelection(string Text, string Reading);

/// <summary>
/// 入力欄 1 つ分の変換ボックス (未確定文字列) の処理 (どのフロントも <see cref="Ime.ImeSession"/> から使う)。状態は <see cref="CompositionState"/> に置き、
/// 入力欄の状態でないものは出来事ごとに <see cref="CompositionEnv"/> で受け取る。責務ごとにファイルを分けている (CompositionController.*.cs)。
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
internal static partial class CompositionController
{
    /// <summary>
    /// ライブ変換は 2 拍以上のかなだけ。2〜3 拍の語 (きた → 来た, りかい → 理解) もライブ変換する。
    /// 1 拍は漢字を推測できず、ひらがな 1 字のまま打つことも多いので、かなのまま見せる。拍の数え方は CountsAsLive。
    /// </summary>
    private const int LiveConversionMinLength = 2;

    /// <summary>Space・確定など、結果が今すぐ要る 1 回の入力で変換エンジンを待つ時間の合計。Meltype IME の DLL の応答期限 (800ms、TextService.h) より短くする。</summary>
    private const int InputBudgetMs = 400;

    /// <summary>この入力で変換エンジンの答えを InputBudgetMs まで待つ env (答えを後から受け取れないフロントでは、答えるまで待つ)。</summary>
    private static CompositionEnv WaitingForInput(CompositionEnv env) => env.LateResults ? env with { Deadline = Environment.TickCount64 + InputBudgetMs } : env;

    /// <summary>候補の一覧の 1 ページの数 (変換ボックス・Linux の候補の一覧と合わせる)。数字キー 1〜9 でこのページの中から選ぶ。</summary>
    internal const int CandidatePageSize = 9;

    /// <summary>英数状態の判定で、この時間打鍵が無ければ英語とみなして判定をやめる。</summary>
    private const long DirectHoldIdleMs = 700;

    /// <summary>自分が確定してから、この時間内はアプリ側のテキストがまだ更新されていないかもしれないので自分の記録を優先する。</summary>
    private const long OwnCommitTrustMs = 1500;

    /// <summary>
    /// 予測変換の候補。Text は一覧に出す語、Before は選んだときに語の前に残す文字 (最後の文節の続きの候補なら前の文節の見えている文字、
    /// 読み全体の続きの候補なら空)、Reading は語に置き換わる打ちかけの読み。
    /// </summary>
    internal sealed record Prediction(string Text, string Before, string Reading);

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

        /// <summary>変換エンジンが返したときの Zenzai の答え (Zenzai の答えでなければ null)。今の文字列がこれと同じなら説明に「Zenzai」と出し、候補の一覧ではこの候補に注記を付ける。</summary>
        public string? ZenzaiText { get; set; }

        /// <summary>
        /// 日本語の文節の、文の中での変換結果 (変換エンジンの答え・ユーザー辞書の語)。<see cref="AddCandidates"/> が、その読みだけの変換などの候補をこの後ろに並べる。
        /// 候補を足し終えた文節と、変換エンジンに頼まない文節 (英語・記号) は null。
        /// </summary>
        public string? Engine { get; set; }

        /// <summary>外部変換エンジンの候補追加が完了したか。<see cref="Fixed"/> の文節では追加せずに完了扱いにする。</summary>
        public bool Expanded { get; set; }

        /// <summary>ユーティリティ変換の Replace の文節。候補を足さず、文節の区切りも動かさない。</summary>
        public bool Fixed { get; init; }

        /// <summary>
        /// ユーティリティ変換の Prepend で、候補の先頭に足した候補の数。変換エンジンの候補の一覧 (Expand) は、その後ろに入れる
        /// (先頭に足した漢字の間に 言偏にふるとり が入らないように)。
        /// </summary>
        public int Leading { get; set; }

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

        /// <summary>英日の区切りの案ごとの文を候補にした、文全体の文節なら、候補の文 → その案の文節。</summary>
        public Dictionary<string, List<Clause>>? Alternatives { get; init; }
    }

    /// <summary>入力を覚えないように求める入力欄か。その欄で扱った未確定入力は、欄を離れて確定しても学習しない。</summary>
    public static bool Private(CompositionState state) => state.PrivateField;

    public static void SetPrivate(CompositionState state, bool value)
    {
        state.PrivateField = value;
        if (value && IsComposing(state)) state.PrivateComposition = true;
    }

    private static bool Learning(CompositionState state, CompositionEnv env) => env.Settings.LearningEnabled && !Private(state) && !state.PrivateComposition;

    private static bool KanaInput(CompositionState state, CompositionEnv env) => env.Settings.InputStyle == Config.InputStyle.Kana;

    /// <summary>英語とも日本語とも読める語を、次の語の文脈に合わせて確定し直すか。</summary>
    private static bool AutoCorrect(CompositionState state, CompositionEnv env) => env.Settings.AutoCorrectAfterCommit && env.Settings.AutoDetection;

    /// <summary>変換ボックスに入力中か、英数状態で打ち始めの語を判定中か。</summary>
    public static bool IsComposing(CompositionState state) => !CompositionText.IsEmpty(state.Text) || state.HeldLetters.Length > 0;

    /// <summary>変換して候補を選んでいるか。</summary>
    public static bool IsConverting(CompositionState state) => state.Clauses.Count > 0;

    /// <summary>
    /// キーを押した。
    /// 変換ボックスに入力していないときは、入力を始めるキー (<see cref="StartsComposition"/>) だけを受け取り、ほかはアプリに通す。
    /// releaseFollows なら、離したことは <see cref="Release"/> で届く (false なら押した直後に離したとみなす)。
    /// </summary>
    public static SessionResult Press(CompositionState state, CompositionEnv env, KeyPress key, bool releaseFollows = false)
    {
        CompositionOutput.Begin(state.Out);
        Tick(state, env, key.TimeMs);
        env = WaitingForInput(env);
        var consumed = Handle(state, env, key);
        if (!releaseFollows) HandleRelease(state, env, key.Vk);
        Settle(state, env);
        return Result(state, env, consumed);
    }

    /// <summary>ワークアラウンド: releaseFollows を付けて押したキーを離した (親指シフトの親指キー・中指NICOLA のシフトのキー)。</summary>
    public static SessionResult Release(CompositionState state, CompositionEnv env, int vk)
    {
        CompositionOutput.Begin(state.Out);
        HandleRelease(state, env, vk);
        Settle(state, env);
        // 離したことはフロントが押下のとおりに通す。Consumed が false なら、離したことで今のキーの働きをした (文字に使わずに離した親指キー)
        return Result(state, env, consumed: true);
    }

    /// <summary>キーをアプリに通す。変換ボックスの状態は変えない (英数状態で判定中の語は、しばらく打たなければ英語とみなす)。</summary>
    public static SessionResult Pass(CompositionState state, CompositionEnv env, long nowMs)
    {
        CompositionOutput.Begin(state.Out);
        Tick(state, env, nowMs);
        return Result(state, env, consumed: false);
    }

    /// <summary>入力の内容を変えない出来事 (フォーカス・キャレットの知らせなど) の結果。表示は今のまま。</summary>
    public static SessionResult Current(CompositionState state, CompositionEnv env)
    {
        CompositionOutput.Begin(state.Out);
        return Result(state, env, consumed: true) with { Consumed = false };
    }

    private static SessionResult Result(CompositionState state, CompositionEnv env, bool consumed) => CompositionOutput.Result(state.Out, env, consumed && !state.Out.PassedCurrent, env.Settings);

    /// <summary>キーを 1 つ処理する。アプリに通さないなら true。</summary>
    private static bool Handle(CompositionState state, CompositionEnv env, KeyPress key)
    {
        (state.Shift, state.Control, state.Modifier) = (key.Shift, key.Control, key.Modifier);
        if (!IsComposing(state) && !state.KanaLayout.ThumbHeld && (key.Modifier || !StartsComposition(state, env, key))) return false;
        state.Pressed.Add(key.Vk);
        HandleKey(state, env, new KeyEvent(key.Vk, key.Ch ?? 0, false, false, false, key.TimeMs));
        return true;
    }

    /// <summary>変換ボックスを開くキーか。変換キーは選んだ文字の再変換、親指シフトのかな配列の 無変換・変換 は親指キー。</summary>
    private static bool StartsComposition(CompositionState state, CompositionEnv env, KeyPress key)
    {
        var direct = state.Direct;
        if (key.Vk == VirtualKeys.Convert && env.Selection is { Length: > 0 }) return true;
        if (key.Vk is VirtualKeys.Convert or VirtualKeys.NonConvert && !direct && KanaInput(state, env) &&
            env.Settings.KanaLayout is KanaLayout.Nicola or KanaLayout.NakayubiNicola or KanaLayout.Tron) return true;
        // かな入力: かなのキーならすべて入力を始める (StartWith)
        if (KanaInput(state, env) && !direct && Detection.KanaDetector.IsKanaKey(key.Vk)) return true;
        // 日本語入力の Shift+Space・設定「スペースの幅」が全角の Space は、全角スペースにする (StartWith)
        if (key.Vk == VirtualKeys.Space) return key.Shift || !direct && env.Settings.SpaceWidth == Config.SpaceWidth.FullWidth;
        if (key.Ch is not { } c) return false;
        if (VirtualKeys.IsLetter(key.Vk) && char.IsAsciiLetter(c)) return true;
        // 句読点・かぎかっこ・長音・数字・記号 (Shift で打つものも)
        return StartsWithSymbol(c);
    }

    private static void HandleRelease(CompositionState state, CompositionEnv env, int vk)
    {
        var pressed = state.Pressed.Remove(vk);
        if (!IsComposing(state) && !state.KanaLayout.ThumbHeld) return;
        var kanaUp = state.KanaLayout.Up(vk);
        // 中指NICOLA: シフトのキーを押したまま先に離した文字キーを、シフト面で打ち直す
        if (kanaUp.Stroke.Handled && IsComposing(state)) ApplyKanaStroke(state, env, kanaUp.Stroke);
        // 親指シフト: 文字に使わずに離した親指キーは、もとの働き (変換・無変換) をする
        if (kanaUp.ThumbTap && pressed)
        {
            HandleKey(state, env with { BypassKanaLayout = true }, new KeyEvent(vk, 0, false, false, false, 0));
        }
    }

    /// <summary>出来事を処理した後。表示を作り直し、入力が終わったら再変換の状態と押したキーを忘れる。</summary>
    private static void Settle(CompositionState state, CompositionEnv env)
    {
        UpdateView(state, env);
        // 親指キーを押したままなら、離したことを受け取るまで受け取り続ける (文頭で親指キーだけを押したとき。変換ボックスは空)
        if (IsComposing(state) || state.KanaLayout.ThumbHeld) return;
        // 読みをすべて削除した場合も、元の選択範囲は置換せず再変換を終了する。
        if (state.Reconversion is not null) ClearComposition(state);
        state.Pressed.Clear();
    }

    /// <summary>
    /// 背景の変換の結果が届いた。入力スレッドで呼ぶ。変換エンジンが使えなかった間の結果を捨てて、入力中の表示を作り直す。
    /// 表示を作り直したなら true。
    /// </summary>
    public static SessionResult? RefreshConversion(CompositionState state, CompositionEnv env)
    {
        CompositionOutput.Begin(state.Out);
        return Refresh(state, env) ? Result(state, env, consumed: true) : null;
    }

    private static bool Refresh(CompositionState state, CompositionEnv env)
    {
        state.LiveCache.Clear();
        if (CompositionText.IsEmpty(state.Text) || IsConverting(state) && !RefreshOpenConversion(state, env)) return false;
        // 文節が分かると最後の文節の予測が出せる。予測を選んでいる間は、選んでいる候補を置き換えない
        if (state.PredictionIndex < 0) state.PredictionKey = "\u0000";
        UpdateView(state, env);
        return true;
    }

    /// <summary>
    /// 候補を開いた後に届いた答えを反映する。文節の区切りを動かしたあとは置き換えない。
    /// 文節の並びが変わる答えは、候補を選び直したか、別の文節へ移動したあとなら置き換えない (選んでいる文節がずれる)。
    /// 候補を選び直した文節は、選んでいる候補を変えずに、新しい候補を上に入れる。何も変わらなければ false。
    /// 英日の区切りの案を候補にした文全体の文節を選び直したあとも置き換えない。作り直すと選んだ文が新しい案に無くなることがあり、
    /// その案の文節に分けられなくなる (← → で選び直せず、文節ごとの学習もできない)。
    /// </summary>
    private static bool RefreshOpenConversion(CompositionState state, CompositionEnv env)
    {
        if (state.ClausesEdited || state.Reconversion is not null || state.Clauses.Count == 0 || state.Clauses is [{ Alternatives: not null, Changed: true }]) return false;
        var (old, selected, tabStarted) = (state.Clauses, state.SelectedClause, state.TabStartedConversion);
        StartConversion(state, WithoutWaiting(env), state.ConversionPrefersJapanese);
        state.TabStartedConversion = tabStarted;
        var sameClauses = state.Clauses.Count == old.Count && state.Clauses.Zip(old).All(p => p.First.Reading == p.Second.Reading);
        var nothingNew = sameClauses && state.Clauses.Zip(old).All(p => p.First.Text == p.Second.Text && p.First.Candidates.All(p.Second.Candidates.Contains));
        if (nothingNew || !sameClauses && (selected != InitialClause(env, old.Count) || old.Any(c => c.Changed)))
        {
            state.Clauses = old;
            state.SelectedClause = selected;
            return false;
        }
        if (sameClauses)
        {
            foreach (var (fresh, previous) in state.Clauses.Zip(old))
            {
                if (!previous.Changed) continue;
                fresh.Candidates = Distinct([.. fresh.Candidates, .. previous.Candidates]);
                fresh.Index = fresh.Candidates.IndexOf(previous.Text);
                fresh.Changed = true;
                fresh.Translations.UnionWith(previous.Translations);
                fresh.NotLearned.UnionWith(previous.NotLearned);
            }
        }
        state.SelectedClause = sameClauses ? selected : InitialClause(env, state.Clauses.Count);
        return true;
    }

    /// <summary>英数状態で判定中の語が、無入力のまま一定時間たったら英語とみなす。</summary>
    private static void Tick(CompositionState state, CompositionEnv env, long nowMs)
    {
        if (state.HeldLetters.Length == 0 || nowMs - state.HeldLastKeyTime < DirectHoldIdleMs) return;
        DecideHeld(state, env, final: true);
    }

    public static void AbandonDirectHold(CompositionState state) => ClearHeld(state);

    /// <summary>英数 (直接入力) にする・やめる。やめたら、英数状態で判定中の語も忘れる。</summary>
    public static void SetDirect(CompositionState state, bool direct)
    {
        var wasDirect = state.Direct;
        state.Direct = direct;
        if (direct) return;
        state.DirectEnglishWord = false;
        if (wasDirect) ClearHeld(state);
    }

    /// <summary>英数状態で打ち始めた語の判定が決まった (japanese: ローマ字だったので日本語入力に戻った / false: 英語だった)。</summary>
    private static void DirectDecided(CompositionState state, bool japanese)
    {
        state.Direct = !japanese;
        state.DirectEnglishWord = !japanese;
    }

    /// <summary>変換中の文節の候補を番号で選ぶ (Mac の候補ウィンドウをクリックしたときなど)。</summary>
    public static SessionResult SelectCandidate(CompositionState state, CompositionEnv env, int index)
    {
        CompositionOutput.Begin(state.Out);
        SelectCandidateAt(state, env, index);
        return Result(state, env, consumed: true);
    }

    private static void SelectCandidateAt(CompositionState state, CompositionEnv env, int index)
    {
        // 入力中に出した候補 (設定「入力中も変換候補をいくつか表示」) をクリックしたら、変換を始めてからその候補を選ぶ
        // 入力中の候補は読み全体の候補なので、変換を始めて文節に分かれると一覧が変わり、番号が別の語を指す。
        // 見えていた一覧のまま、読み全体 1 つの文節にして、クリックした語を選ぶ
        if (!IsConverting(state) && TypingCandidates(state, env) is { } typing)
        {
            if (index < 0 || index >= typing.Count) return;
            state.Clauses = [new Clause(CompositionText.AllKana(state.Text, env, final: false), false, typing) { Raw = CompositionText.Raw(state.Text), Expanded = true }];
            state.SelectedClause = 0;
        }
        if (!IsConverting(state) || state.Clauses.Count == 0) return;
        var clause = state.Clauses[state.SelectedClause];
        if (index < 0 || index >= clause.Candidates.Count) return;
        clause.Index = index;
        clause.Changed = true;
        UpdateView(state, env);
    }

    /// <summary>予測変換の候補を番号で選んで確定する (Meltype IME の候補の一覧をクリックしたとき)。</summary>
    public static SessionResult CommitPredictionAt(CompositionState state, CompositionEnv env, int index)
    {
        CompositionOutput.Begin(state.Out);
        var predictions = CurrentPredictions(state, env);
        if (!IsConverting(state) && index >= 0 && index < predictions.Count)
        {
            CommitPrediction(state, env, predictions[index]);
            UpdateView(state, env);
        }
        return Result(state, env, consumed: true);
    }

    /// <summary>
    /// 無効化・フォーカス喪失などで、未確定の内容をそのまま確定する。consumed: 出来事をアプリに通さないか。
    /// preserveLatinRaw: 結合アクセントが続くので、明示的に選択していない ASCII 英字の原文を確定する。preserveText: 確定する文字列に自動空白を加えない。
    /// </summary>
    public static SessionResult CommitPending(CompositionState state, CompositionEnv env, bool consumed = true, bool preserveLatinRaw = false, bool preserveText = false)
    {
        CompositionOutput.Begin(state.Out);
        CommitAll(state, env, preserveLatinRaw, preserveText);
        return Result(state, env, consumed);
    }

    private static void CommitAll(CompositionState state, CompositionEnv env, bool preserveLatinRaw, bool preserveText)
    {
        env = WaitingForInput(env);
        if (state.HeldLetters.Length > 0) ReleaseHeldAsEnglish(state, env);
        // e + 結合アクセントを「え + アクセント」にしない。明示した表示モード・候補・かな入力は尊重する。
        // ASCII 英字だけの原文に Latin アクセントが続く場合に限り、変換や学習を経由せず確定する。
        if (preserveLatinRaw && !IsConverting(state) && state.Reconversion is null && !state.Text.KanaInput &&
            state.Text.Mode is DisplayMode.Auto or DisplayMode.HalfWidthAlphanumeric &&
            !CompositionText.IsEmpty(state.Text) && CompositionText.Raw(state.Text).All(char.IsAsciiLetter))
        {
            state.Correctable.Clear();
            CommitText(state, env, CompositionText.Raw(state.Text), english: true, preserveText: true, rule: "英字にアクセント記号が続くので打ったまま");
        }
        else CommitIfAny(state, env, preserveText);
        UpdateView(state, env);
    }

    /// <summary>変換中の文節・候補・選択中の文節をすべて消す。再変換の状態も消す。</summary>
    private static void ClearComposition(CompositionState state)
    {
        state.Reconversion = null;
        CompositionText.Clear(state.Text);
        state.Clauses = [];
        state.SpaceStartedConversion = false;
        state.TabStartedConversion = false;
    }

    /// <summary>パスワード欄に移ったとき、未確定入力と前の欄の文脈を確定せずに捨てる。</summary>
    internal static void CancelPending(CompositionState state, CompositionEnv env)
    {
        ClearComposition(state);
        ClearHeld(state);
        ForgetLastCommit(state);
        state.PendingReplace = null;
        state.PrivateComposition = false;
        state.KeptChoices.Clear();
        state.LiveCache.Clear();
        state.Predictions = [];
        state.PredictionKey = "";
        state.PredictionIndex = -1;
        state.Pressed.Clear();
        state.KanaLayout.Reset();
        CompositionOutput.Begin(state.Out);
        CompositionOutput.Hide(state.Out);
    }

    /// <summary>フォーカスが変わったときなど。前の入力欄の文脈を持ち越さない。</summary>
    public static void ResetContext(CompositionState state)
    {
        state.LastCommitEnglish = null;
        state.LastCommitText = null;
        state.Correctable.Clear();
    }
}
