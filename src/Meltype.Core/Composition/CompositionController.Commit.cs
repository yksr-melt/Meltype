// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Diagnostics;

namespace Meltype.Composition;

/// <summary>確定: 確定する文字の組み立て、英単語の前後の空白、確定した後に前の語を確定し直す自動修正、予測変換の候補の確定。</summary>
internal static partial class CompositionController
{
    /// <summary>今の表示 (ライブ変換が有効なら日本語区間は漢字に変換済み)。</summary>
    private static string CurrentDisplay(CompositionState state, CompositionEnv env, bool final) =>
        CompositionText.Display(state.Text, env, final, env.Settings.LiveConversion ? (kana, afterEnglish, shown) => LiveConvert(state, env, kana, afterEnglish, SegmentBefore(env, shown)) : null);

    private static void CommitIfAny(CompositionState state, CompositionEnv env, bool preserveText = false)
    {
        if (!CompositionText.IsEmpty(state.Text)) Commit(state, env, preserveText: preserveText);
    }

    /// <summary>今の未確定入力が、自動では変換してはいけない保護区間か (F9/F10 などの明示指定は除く)。</summary>
    private static bool IsProtectedInput(CompositionState state) => !IsConverting(state) && state.Text.Mode == DisplayMode.Auto && CompositionText.HasProtectedTail(state.Text);

    private static void Commit(CompositionState state, CompositionEnv env, string suffix = "", bool preserveText = false)
    {
        if (IsProtectedInput(state))
        {
            CommitProtected(state, env, suffix);
            return;
        }
        var converting = IsConverting(state) && state.Clauses.Count > 0;
        // 文全体の文節で区切りの案を選び直したら、選んだ案の文節で確定する (選び直したことは chosen に残す)。
        // 案の文 (botと・ボット) を選んだときは、英語 / 日本語を学習しない (LanguageMemory に覚えさせない)。日英判定の精度がまだ低く、
        // 間違えて選んだ案やその場限りの案まで覚えると、次からの判定を崩すため。判定の精度が上がったら、学習するかを見直す。
        // 文節が 1 つの案の、その文節の候補 (Shift+Space で変換した make の まけ) を選んだときは、その文節で選んだときと同じく学習する。
        // 案が 1 つだけで文全体の文節にならなかったときと、同じ候補を選んで学習が変わらないようにするため
        var chosenWhole = converting && state.Clauses is [{ Alternatives: not null, Changed: true }];
        if (converting) SplitWholeSentence(state);
        // 区間分けの重みで決まらなかった確定の決まり。誤変換の報告をログから読み解けるよう、通った決まりを確定のログに書く (issue #1644)
        var rules = new List<string>();
        // 設定「Enter は見えているとおりに確定」が OFF なら、背景の結果が間に合っていなくても env.Deadline までは答えを待つ
        var text = converting ? string.Concat(state.Clauses.Select(c => c.Text))
            : env.LateResults && !env.Settings.EnterCommitsAsShown
                ? CompositionText.Display(state.Text, env, final: true, env.Settings.LiveConversion ? (kana, afterEnglish, shown) => CountsAsLive(kana) ? string.Concat(ConvertJapaneseSegment(state, env, kana, afterEnglish, SegmentBefore(env, shown), zenzai: true).Select(c => c.Text)) : kana : null)
                : CurrentDisplay(state, env, final: true);
        // 英語の文脈で英字だけに見えていた入力 (hello mata) は、確定するときに判定し直してかなにしない: 見えているとおりに確定する (issue #127)。
        // 文脈がない単独の語 (mine → みね) は、今までどおり確定時の判定に任せる
        if (!converting && state.Text.Mode == DisplayMode.Auto && !state.Text.KanaInput && env.Context().PrecedingEnglish == true && CurrentDisplay(state, env, final: false) is { Length: > 0 } shown &&
            shown.All(char.IsAsciiLetter) && shown != text && text.Any(c => c > 0x7F))
        {
            text = shown;
            rules.Add("英語の文脈なので見えていた英字のまま (#127)");
        }
        var english = converting ? state.Clauses.All(c => c.IsEnglish) : CompositionText.IsAlphanumericAt(state.Text, env, final: true);
        // F6〜F10 で、はっきり英字 / かなを選んで確定した語も、後から確定し直さない。
        var chosen = converting ? chosenWhole || state.Clauses.Any(c => c.Changed) : state.Text.Mode != DisplayMode.Auto;
        if (state.Reconversion is not null)
        {
            // 入れた文字は、アプリが選択していた文字と置き換える。
            rules.Add("選択していた文字の再変換");
            LogCommit(state, env, text + suffix, string.Join("、", rules));
            // 学習の文脈は確定する前のキャレットの前 (確定した文字を入れる前)
            if (converting) LearnClauses(state, env);
            CompositionOutput.Commit(state.Out, env, text + suffix);
            ResetContext(state);
            ClearComposition(state);
            return;
        }
        RecordCorrection(state, env, converting, text, suffix);
        if (converting) LearnClauses(state, env);
        else if (Learning(state, env) && !state.EscReverted) env.Learning.LearnLanguage(state.Text, CompositionText.Segments(state.Text, env), CompositionText.LearningContext(state.Text, env));
        if (!converting || state.Clauses.All(CompositionLearning.Learnable))
            RememberPhrase(state, env, converting ? string.Concat(state.Clauses.Select(c => c.IsEnglish ? "" : c.Reading)) : CompositionText.AllKana(state.Text, env, final: true), text, english);
        CommitText(state, env, text + suffix, english, CompositionText.Raw(state.Text), chosen, preserveText, string.Join("、", rules));
    }

    /// <summary>
    /// 確定のたびに、確定した文字と、そう決めた理由 (区切りの重み・次点・文節・通った決まり) を残す (issue #1644)。
    /// 誤変換の報告を、利用者のログから読み解けるようにする。打った文字は設定「ログに入力した文字を残す」に従う。
    /// </summary>
    /// <param name="rule">区切りの重みでは決まらなかった確定の決まりの名前 (#127 で見えていた英字のまま、など)。</param>
    private static void LogCommit(CompositionState state, CompositionEnv env, string text, string rule)
    {
        if (CompositionText.IsEmpty(state.Text)) return;
        var parts = new List<string>();
        if (Diagnostics.Log.RecordText) parts.Add($"読み「{CompositionText.AllKana(state.Text, env, final: true)}」");
        // 区切りは、表示に使ったのと同じ計算 (Segments) から取る
        if (state.Text.Mode == DisplayMode.Auto) parts.Add(CompositionText.ExplainSegments(state.Text, env));
        else parts.Add($"F6〜F10 で選んだ表示 ({state.Text.Mode})");
        if (state.Clauses.Count > 0)
            parts.Add("文節 " + string.Join(" | ", state.Clauses.Select(c => (Diagnostics.Log.RecordText ? $"{c.Reading}→{c.Text}" : $"({c.Reading.Length} 文字)") + (c.Changed ? " (選び直した)" : ""))));
        if (rule.Length > 0) parts.Add(rule);
        Diagnostics.Log.Decision($"確定: {Diagnostics.Log.Text(CompositionText.Raw(state.Text))}→{Diagnostics.Log.Text(text)}; {string.Join("; ", parts.Where(p => p.Length > 0))}");
    }

    private static void RecordCorrection(CompositionState state, CompositionEnv env, bool converting, string text, string suffix)
    {
        if (env.Corrected is null || CompositionText.Raw(state.Text).Length == 0 || !CompositionText.Raw(state.Text).Any(char.IsAsciiLetter)) return;
        string shown;
        if (converting)
        {
            // 英語 / 日本語を選び直した文節があるときだけ (漢字の選び直しは判定の間違いではない)
            var switched = state.Clauses.Any(c => c.Changed && (c.IsEnglish ? IsJapaneseText(c.Text) : c.Raw is { } raw && c.Text == raw));
            if (!switched) return;
            shown = string.Concat(state.Clauses.Select(c => c.Candidates[0]));
        }
        else
        {
            if (state.Text.Mode == DisplayMode.Auto) return;
            var (mode, letterCase) = (state.Text.Mode, state.Text.Case);
            state.Text.Mode = DisplayMode.Auto;
            shown = CompositionText.Display(state.Text, env, final: true);
            (state.Text.Mode, state.Text.Case) = (mode, letterCase);
        }
        if (shown == text) return;
        var before = env.Context().PrecedingText ?? "";
        env.Corrected.Invoke(new Correction(CompositionText.Raw(state.Text), shown, text, suffix.Contains(' ') ? "Space (変換)" : "Enter (確定)",
            before.Length > 20 ? before[^20..] : before, DateTime.Now));
    }

    /// <summary>
    /// 保護区間 (メンション・URL・メール・パス) を、自動変換・かな化・幅変換・誤字補正・学習・確定後補正に
    /// 渡さず、打った原文のまま確定する。Space は IME が半角空白を 1 つ足し、Enter は足さない。
    /// </summary>
    private static void CommitProtected(CompositionState state, CompositionEnv env, string suffix)
    {
        // 表示の再計算に依存せず、打った原文そのものを使う (確定条件で区間分けが変わっても記号を変えない)。
        var text = (CompositionText.IsProtectedRaw(state.Text) ? CompositionText.Raw(state.Text) : CurrentDisplay(state, env, final: true)) + suffix;
        state.Clauses = [];
        state.Correctable.Clear();
        // raw を空にして渡し、保護原文を確定後補正・学習の対象にしない (INV-06)。
        CommitText(state, env, text, english: false, preserveText: true, rule: "保護区間 (メンション・URL・メール・パス) で終わるので、打ったまま");
    }

    /// <summary>確定した日本語の語句を、予測変換のために覚える (設定「予測変換」が ON のとき)。</summary>
    private static void RememberPhrase(CompositionState state, CompositionEnv env, string reading, string text, bool english)
    {
        if (!english && Learning(state, env) && env.Settings.PredictiveCandidates) env.Learning.RememberPhrase(reading, text);
    }

    /// <summary>確定した文節を学習する。設定「ユーザー学習を使う」が OFF なら、新しく覚えない (issue #277)</summary>
    private static void LearnClauses(CompositionState state, CompositionEnv env)
    {
        if (Learning(state, env)) env.Learning.Learn(state.Clauses, env.Context().PrecedingText, CompositionText.LearningContext(state.Text, env));
    }

    /// <summary>予測変換の候補を確定する。日本語なら覚え直して、次からより前に出す。</summary>
    private static void CommitPrediction(CompositionState state, CompositionEnv env, Prediction prediction)
    {
        state.PredictionIndex = -1;
        if (!prediction.Text.All(c => c < 0x80) && Learning(state, env))
            env.Services.Predictor?.Phrases?.Remember(PredictionReading(state, env, prediction.Text, prediction.Reading), prediction.Text);
        // 最後の文節の続きの候補なら、前の文節 (見えているとおり) とつなげて確定する
        var text = prediction.Before + prediction.Text;
        var english = text.All(c => c < 0x80);
        CommitText(state, env, text, english, english ? text : CompositionText.Raw(state.Text), chosen: true);
    }

    /// <summary>予測の候補の読み (前に確定したときの読み)。分からなければ打ちかけの読み。</summary>
    private static string PredictionReading(CompositionState state, CompositionEnv env, string prediction, string typed) =>
        env.Services.Predictor?.Phrases?.ReadingOf(prediction, typed) ?? typed;

    /// <summary>
    /// 直前に確定した語。英語とも日本語とも読める語 (i, sushi) を文脈が分からないまま確定したとき、
    /// 次の語で文脈がはっきりしたら確定し直す (I want: 「胃」→「I」)。キャレットが動いたら (ほかのキー・クリック) 無効。
    /// </summary>
    /// <param name="AfterEnglish">確定したとき、前が英語だった (Are + you)。英文の中の語なので、後ろが日本語でも日本語に直さない (#265)。</param>
    internal sealed record CommitRecord(string Text, string Raw, bool English, bool SpaceIntended, bool AfterEnglish = false);
    private const int MaxCorrectable = 4;

    /// <summary>キャレットが動いたかもしれないとき (Meltype を通らなかったキー・クリック)。直前の語は確定し直さない。</summary>
    public static void ForgetLastCommit(CompositionState state)
    {
        state.Correctable.Clear();
        CompositionOutput.ForgetCommitted(state.Out);
        // キャレットが動いた・アプリが文字を入れた: キャレットの前は自分が確定した語ではないので、読めたらアプリの文字を使う。
        // 読めなければ前の文字も、それが英語だったかも分からないものとして扱う (消した後・行の先頭に、英単語の前後の空白を足さない)
        state.LastCommitTime = long.MinValue / 2;
        state.LastCommitText = null;
        state.LastCommitEnglish = null;
    }

    /// <summary>
    /// 今確定しようとしている語 (raw) で文脈がはっきりしたら、直前に確定した英語とも日本語とも読める語を確定し直す。
    /// 例: 「i」を Space で「胃」にした後に want と打つ → 「I want」、「sushi 」の後に「がすき」 → 「すしがすき」。
    /// </summary>
    private static string? CorrectPreviousCommit(CompositionState state, CompositionEnv env, string raw, bool english)
    {
        if (state.Correctable.Count == 0 || !AutoCorrect(state, env) || !env.CanDeleteBackward) return null;
        var previous = state.Correctable[^1];
        List<CommitRecord> targets = [];
        string? replacement = null;
        if (!previous.English && english && env.Detector.IsDefinitelyEnglish(raw))
        {
            // 前の語の後ろを Space で区切らずに続けたとき (Enter で確定したときなど) は直さない。英語にすると前の語とつながる (あれ + ON → areON)
            if (!previous.SpaceIntended) return null;
            // 日本語で確定した語を英語に: 代名詞の i は I にし、Space で変換していたなら空白も入れる。
            // その前にも Space で区切って日本語にした語が続いていれば、まとめて英文に直す (make sure you have)。
            var start = state.Correctable.Count - 1;
            while (start > 0 && !state.Correctable[start - 1].English && state.Correctable[start - 1].SpaceIntended) start--;
            targets = state.Correctable.Skip(start).ToList();
            // 助詞と同じ形の短い語 (to, no) 1 語だけを、大文字で始まる語 (固有名詞) で直すことはしない (Google と Apple は日本語でもよく書く)。
            // 小文字の英単語で英文と分かったとき (let me know、do it) は直す。
            if (targets.Count == 1 && targets[0].Raw.Length <= 2 && targets[0].Raw != "i" && char.IsAsciiLetterUpper(raw.FirstOrDefault(char.IsAsciiLetter))) return null;
            // する の活用のローマ字 (site→して、sita→した) は、後ろが英語でも日本語のまま
            // (site PR → site PR に確定し直さず、して PR を保つ)。
            // sure→すれ は英文 (make sure) でも使うので含めない。
            if (targets.Any(t => IsSuruRomajiKeepJapanese(state, env, t.Raw)))
            {
                // Space で変換を始めていた分の空白だけは残す (してPR → して PR)。
                replacement = string.Concat(targets.Select(t => t.Text + (t.SpaceIntended ? " " : "")));
            }
            else
            {
                replacement = string.Concat(targets.Select(t => (t.Raw == "i" ? "I" : t.Raw) + (t.SpaceIntended ? " " : "")));
            }
        }
        // 前に英語が続いていた普通の英単語 (Are you の you) は、英文の続きとして打ったもの。後ろが日本語 (すし？) でも日本語に直さない (#265)。
        // 日本語由来の語 (I love sushi + がすき) は、日本語のつもりのことがあるので今までどおり直す。
        else if (previous.English && !(previous.AfterEnglish && !CompositionDetector.IsJapaneseOriginWord(previous.Raw.ToLowerInvariant())) && !english && !env.Detector.IsAmbiguousWord(raw) && raw.Any(char.IsAsciiLetter))
        {
            // 英語で確定した語を日本語に (Space で空白を入れていたら取る)。
            targets = [previous];
            replacement = env.Detector.Romaji.ConvertLenient(previous.Raw.ToLowerInvariant(), final: true);
            // ローマ字として読んでもよく使う語の読みにならない語 (issue → いっすえ、api → あぴ) は英語のまま (issue #121, #124)。
            // sushi → すし のように、日本語の語として読めるときだけ直す。
            if (env.Services.RomajiTypos is { } lexicon && !lexicon.IsWord(replacement)) return null;
        }
        // ユーザーが英字 / かなに直して覚えた語 (F10 で英字にした api) は、覚えたとおりなら書き換えない (issue #124)。
        if (targets.Any(t => env.Services.Languages?.Get(t.Raw.ToLowerInvariant(), CompositionText.LearningContext(state.Text, env)) == t.English)) return null;
        var original = string.Concat(targets.Select(t => t.Text));
        if (replacement is null || replacement == original) return null;
        // 直前に確定した語が、今のキャレットの位置に無い (別の場所をクリックした等) ときは書き換えない。
        // 消すつもりの文字がそこに無いので、消すとキャレットの前の別の文字が消えてしまう。
        // 入力欄の文字をその場で読めるホストだけ確かめる (Windows は別のスレッドで後から読むので、古い値で誤って止めない)。
        if (CompositionOutput.Before(state.Out, env) is { } caretBefore && !caretBefore.EndsWith(original, StringComparison.Ordinal))
        {
            Diagnostics.Log.Decision($"キャレットの位置が変わっているので確定し直しません: {Diagnostics.Log.Text(original)}");
            state.Correctable.Clear();
            return null;
        }

        Diagnostics.Log.Decision($"前後の文脈に合わせて確定し直しました: {Diagnostics.Log.Text(original)}→{Diagnostics.Log.Text(replacement)}");
        // 消して入れ直すのは、続けて確定する文字と一緒に送る (CommitText)。別々に送ると、その間に入れ直した文字と
        // 続けて確定した文字の順番が入れ替わるアプリがある (Are yoよ人間う？: issue #258)
        state.PendingReplace = (original.Length, replacement);
        state.LastCommitText = replacement;
        state.Correctable.Clear();
        return replacement;
    }

    /// <summary>カッコの組 (設定「カッコを打ったら中にカーソルを移動」で、確定したら中に入る)。</summary>
    private static readonly HashSet<string> BracketPairs = ["「」", "『』", "（）", "()", "［］", "[]", "｛｝", "{}", "【】", "〈〉", "《》", "〔〕", "\"\"", "“”"];

    /// <summary>確定して入力する。英語だったか日本語だったか・確定した文字列を、次の入力の文脈として覚えておく。</summary>
    private static void CommitText(CompositionState state, CompositionEnv env, string text, bool english, string raw = "", bool chosen = false, bool preserveText = false, string rule = "")
    {
        LogCommit(state, env, text, rule);
        var spaceIntended = state.SpaceStartedConversion;
        var afterEnglish = env.Context().PrecedingEnglish == true;
        state.SpaceStartedConversion = false;
        state.TabStartedConversion = false;
        CompositionText.Clear(state.Text);
        state.Clauses = [];
        if (text.Length == 0) return;
        var corrected = CorrectPreviousCommit(state, env, raw, english);
        if (!preserveText && (env.Settings.SpaceAroundEnglish || env.Settings.SpaceAroundNumbers))
            text = AddSpacesAroundEnglish(text, corrected ?? env.Context().PrecedingText, env.Context().FollowingText, env.Settings.SpaceAroundEnglish, env.Settings.SpaceAroundNumbers);
        // 英語とも日本語とも読める語を、文脈を決めずに (選び直さずに) 確定したときだけ、後で確定し直せるようにしておく。
        if (!chosen && env.Detector.IsAmbiguousWord(raw))
        {
            // 前の語の後に Space で区切って続けたときだけつなげる (それ以外は新しい並び)。
            if (state.Correctable.Count > 0 && !state.Correctable[^1].SpaceIntended) state.Correctable.Clear();
            state.Correctable.Add(new CommitRecord(text, raw, english, spaceIntended, afterEnglish));
            if (state.Correctable.Count > MaxCorrectable) state.Correctable.RemoveAt(0);
        }
        else state.Correctable.Clear();
        state.LastCommitEnglish = english;
        var joined = (state.LastCommitText ?? "") + text;
        state.LastCommitText = joined.Length > 20 ? joined[^20..] : joined;
        state.LastCommitTime = env.NowMs;
        if (state.PendingReplace is var (count, replacement))
        {
            state.PendingReplace = null;
            CompositionOutput.ReplaceBackward(state.Out, env, count, replacement, text);
        }
        else CompositionOutput.Commit(state.Out, env, text);
        // 設定「カッコを打ったら中にカーソルを移動」: カッコの組だけを確定したら、中に入る (issue #72)。
        // 確定に付けて返す (キーをアプリに通して動かすと、確定に使ったキーまでアプリに通ってしまう: #991・#1353・#1404・#1581)。
        if (env.Settings.CursorIntoBrackets && text.Length == 2 && BracketPairs.Contains(text))
            CompositionOutput.MoveCaretBackward(state.Out, 1);
    }

    /// <summary>
    /// 日本語 (かな・漢字) と英単語の間に半角スペースを入れる (今日はGitHubにpush → 今日は GitHub に push)。
    /// 英字を 1 つ以上含む英数字の並び (iPhone15、C++) を英単語とみなす。数字だけ (3時) と記号 (、。「」) の隣には入れない。
    /// 確定する文字列の端は、入力欄のキャレットの前後の文字 (before・after) との間も見る。
    /// english が false なら英単語には入れない。numbers が true なら、半角数字だけの並び (3回・3,000円 の 3 と 000) の前後にも入れる (issue #958)。
    /// </summary>
    internal static string AddSpacesAroundEnglish(string text, string? before, string? after, bool english = true, bool numbers = false)
    {
        static bool IsJapanese(char c) => c is (>= '\u3040' and <= '\u30FF') or (>= '\u3400' and <= '\u4DBF') or (>= '\u4E00' and <= '\u9FFF') or (>= '\uF900' and <= '\uFAFF');
        static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '#' or '.' or '\'' or '@';
        var previous = string.IsNullOrEmpty(before) ? '\0' : before[^1];
        var next = string.IsNullOrEmpty(after) ? '\0' : after[0];
        var builder = new StringBuilder(text.Length + 8);
        // 英字を含む並び (iPhone15) は english の設定、数字だけの並び (15) は numbers の設定に従う (文字列の中と同じ判定)
        bool SpacedWord(string word) => word.Any(char.IsAsciiLetter) ? english : numbers && word.Any(char.IsAsciiDigit);
        // 前に確定した文字列の最後の語・キャレットの後ろの最初の語で決める (iPhone15 の 5 だけを見て数字にしない)
        var previousWord = new string((before ?? "").Reverse().TakeWhile(IsWordChar).Reverse().ToArray());
        var nextWord = new string((after ?? "").TakeWhile(IsWordChar).ToArray());
        // 前に確定した英単語 (数字) のすぐ後ろに日本語を続けるとき (GitHub|に、3|回)
        if (IsJapanese(text[0]) && char.IsAsciiLetterOrDigit(previous) && SpacedWord(previousWord)) builder.Append(' ');
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
            var isWord = SpacedWord(word);
            if (isWord && IsJapanese(left) && (builder.Length == 0 || builder[^1] != ' ')) builder.Append(' ');
            builder.Append(word);
            if (isWord && IsJapanese(right)) builder.Append(' ');
            builder.Append(text, stop, end - stop);
            i = end;
        }
        // 確定した日本語のすぐ後ろが英単語 (数字) のとき (キャレットの後ろの文字)
        if (IsJapanese(text[^1]) && char.IsAsciiLetterOrDigit(next) && SpacedWord(nextWord)) builder.Append(' ');
        return builder.ToString();
    }

    /// <summary>変換ボックスの最後が英語の区間か (Space を空白として扱うか)。</summary>
    private static bool EndsWithEnglish(CompositionState state, CompositionEnv env) =>
        state.Text.Mode == DisplayMode.Auto && CompositionText.Segments(state.Text, env) is { Count: > 0 } segments && segments[^1].IsEnglish;
}
