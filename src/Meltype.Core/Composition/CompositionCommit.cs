// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using Meltype.Detection;
using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>確定する文字の組み立てと学習、確定済みの語の修正。入力の開始・終了は CompositionLifecycle を通す。</summary>
internal static class CompositionCommit
{

    internal static void CommitIfAny(CompositionState state, CompositionEnv env, bool preserveText = false)
    {
        if (state.Scene is CompositionScene.WithInput scene) Commit(state, scene, env, preserveText: preserveText);
    }

    internal static void Commit(CompositionState state, CompositionScene.WithInput scene, CompositionEnv env, string suffix = "", bool preserveText = false)
    {
        var input = scene.Input;
        if (CompositionLifecycle.IsProtectedInput(scene))
        {
            CommitProtected(state, input, env, suffix);
            return;
        }
        var conversion = (scene as CompositionScene.Converting)?.Conversion;
        // 文全体の文節で区切りの案を選び直したら、選んだ案の文節で確定する (選び直したことは chosen に残す)。
        // 案の文 (botと・ボット) を選んだときは、英語 / 日本語を学習しない (LanguageMemory に覚えさせない)。日英判定の精度がまだ低く、
        // 間違えて選んだ案やその場限りの案まで覚えると、次からの判定を崩すため。判定の精度が上がったら、学習するかを見直す。
        // 文節が 1 つの案の、その文節の候補 (Shift+Space で変換した make の まけ) を選んだときは、その文節で選んだときと同じく学習する。
        // 案が 1 つだけで文全体の文節にならなかったときと、同じ候補を選んで学習が変わらないようにするため
        var chosenWhole = conversion?.Clauses is [{ Alternatives: not null, Changed: true }];
        if (conversion is not null) CompositionConversion.SplitWholeSentence(conversion);
        var clauses = conversion?.Clauses;
        // 区間分けの重みで決まらなかった確定の決まり。誤変換の報告をログから読み解けるよう、通った決まりを確定のログに書く (issue #1644)
        var rules = new List<string>();
        var text = clauses is not null ? CompositionPresentation.Composed(clauses) : CompositionPresentation.CommittedDisplay(input, env);
        // 英語の文脈で英字だけに見えていた入力 (hello mata) は、確定するときに判定し直してかなにしない: 見えているとおりに確定する (issue #127)。
        // 文脈がない単独の語 (mine → みね) は、今までどおり確定時の判定に任せる
        if (clauses is null && input.Text.Mode == DisplayMode.Auto && !input.Text.KanaInput && env.Context().PrecedingEnglish == true && CompositionPresentation.CurrentDisplay(input, env, final: false) is { Length: > 0 } shown &&
            shown.All(char.IsAsciiLetter) && shown != text.Text && text.Text.Any(c => c > 0x7F))
        {
            text = ComposedText.English(shown);
            rules.Add("英語の文脈なので見えていた英字のまま (#127)");
        }
        var english = clauses is not null ? clauses.All(c => c.IsEnglish) : CompositionText.IsAlphanumericAt(CompositionText.Read(input.Text, env), env, final: true);
        // F6〜F10 で、はっきり英字 / かなを選んで確定した語も、後から確定し直さない。
        var chosen = clauses is not null ? chosenWhole || clauses.Any(c => c.Changed) : input.Text.Mode != DisplayMode.Auto;
        if (input.Reconversion is not null)
        {
            // 入れた文字は、アプリが選択していた文字と置き換える。
            rules.Add("選択していた文字の再変換");
            LogCommit(scene, env, text.Text + suffix, string.Join("、", rules));
            // 学習の文脈は確定する前のキャレットの前 (確定した文字を入れる前)
            if (clauses is not null) LearnClauses(state, input, clauses, env);
            CompositionOutput.Commit(state.Out, env, text + ComposedText.English(suffix));
            CompositionLifecycle.ResetContext(state);
            CompositionLifecycle.Finish(state);
            return;
        }
        CompositionSpelling.RecordCorrection(scene, env, text.Text, suffix);
        if (clauses is not null) LearnClauses(state, input, clauses, env);
        else if (CompositionLifecycle.Learning(state, input, env) && !input.EscReverted) env.Learning.LearnLanguage(CompositionText.Read(input.Text, env), CompositionText.Segments(CompositionText.Read(CompositionText.Automatic(input.Text), env), env), CompositionText.LearningContext(env));
        if (clauses is null || clauses.All(CompositionLearning.Learnable))
            RememberPhrase(state, input, env, clauses is not null ? string.Concat(clauses.Select(c => c.IsEnglish ? "" : c.Reading)) : CompositionText.AllKana(CompositionText.Read(input.Text, env), env, final: true), NotationRules.Write(text, env.Settings.Punctuation), english);
        CommitText(state, env, text + ComposedText.English(suffix), english, CompositionText.Raw(CompositionText.Read(input.Text, env)), chosen, preserveText, string.Join("、", rules));
    }

    /// <summary>
    /// 確定のたびに、確定した文字と、そう決めた理由 (区切りの重み・次点・文節・通った決まり) を残す (issue #1644)。
    /// 誤変換の報告を、利用者のログから読み解けるようにする。打った文字は設定「ログに入力した文字を残す」に従う。
    /// </summary>
    /// <param name="rule">区切りの重みでは決まらなかった確定の決まりの名前 (#127 で見えていた英字のまま、など)。</param>
    private static void LogCommit(CompositionScene.WithInput scene, CompositionEnv env, string text, string rule)
    {
        var typed = scene.Input.Text;
        var parts = new List<string>();
        if (Diagnostics.Log.RecordText) parts.Add($"読み「{CompositionText.AllKana(CompositionText.Read(typed, env), env, final: true)}」");
        // 区切りは、表示に使ったのと同じ計算 (Segments) から取る
        if (typed.Mode == DisplayMode.Auto) parts.Add(CompositionText.ExplainSegments(CompositionText.Read(typed, env), env));
        else parts.Add($"F6〜F10 で選んだ表示 ({typed.Mode})");
        if (scene is CompositionScene.Converting { Conversion.Clauses: var clauses })
            parts.Add("文節 " + string.Join(" | ", clauses.Select(c => (Diagnostics.Log.RecordText ? $"{c.Reading}→{c.Text}" : $"({c.Reading.Length} 文字)") + (c.Changed ? " (選び直した)" : ""))));
        else if (Diagnostics.Log.RecordText && IsKanaMode(typed.Mode))
            parts.Add("形を当てる前の文節 " + string.Join(" | ", CommittedClauses(scene, env).Select(c => $"{c.Reading}→{c.Engine ?? "(英語)"}")));
        if (rule.Length > 0) parts.Add(rule);
        Diagnostics.Log.Decision($"確定: {Diagnostics.Log.Text(CompositionText.Raw(CompositionText.Read(typed, env)))}→{Diagnostics.Log.Text(text)}; {string.Join("; ", parts.Where(p => p.Length > 0))}");
    }

    /// <summary>
    /// 確定する入力の文節ごとの記録 (確定する前に呼ぶ)。変換中なら変換の文節。F6〜F8 で形を当てた入力は、形を当てる前の文節
    /// (F6〜F8 を押す前の変換。変換していなければ自動の表示のライブ変換) に、その形を当てた文字を付ける。
    /// </summary>
    internal static IReadOnlyList<CommittedClause> CommittedClauses(CompositionScene.WithInput scene, CompositionEnv env)
    {
        if (scene is CompositionScene.Converting { Conversion: var conversion }) return [.. conversion.Clauses.Select(c => new CommittedClause(c.Reading, c.Engine, c.Text))];
        var input = scene.Input;
        var mode = input.Text.Mode;
        var left = input.LeftConversion is { } kept && kept.Raw == input.Text.TypedRaw
            ? kept.Conversion.Clauses.Select(c => (c.Reading, c.Engine, c.Text)).ToList()
            : AutoClauses(input, env);
        if (!IsKanaMode(mode)) return [.. left.Select(c => new CommittedClause(c.Reading, c.Engine, mode == DisplayMode.Auto ? c.Text : null))];
        var matches = string.Concat(left.Select(c => c.Reading)) == CompositionText.AllKana(CompositionText.Read(input.Text, env), env, final: true);
        return [.. left.Select(c => new CommittedClause(c.Reading, c.Engine, matches ? KanaForm(mode, c.Reading) : null))];
    }

    private static bool IsKanaMode(DisplayMode mode) => mode is DisplayMode.Hiragana or DisplayMode.Katakana or DisplayMode.HalfWidthKatakana;

    private static string KanaForm(DisplayMode mode, string hiragana) => mode switch
    {
        DisplayMode.Katakana => CompositionText.ToKatakana(hiragana),
        DisplayMode.HalfWidthKatakana => CompositionText.ToHalfWidthKatakana(CompositionText.ToKatakana(hiragana)),
        _ => hiragana,
    };

    // 自動の表示の文節: 英語の区間は 1 つの文節 (変換エンジンの答えは無し)、日本語の区間はライブ変換の文節 (ライブ変換を使わなければかなのまま)
    private static List<(string Reading, string? Engine, string Text)> AutoClauses(CompositionInput input, CompositionEnv env)
    {
        var original = input.Text;
        input.Text = CompositionText.Automatic(input.Text);
        try
        {
            var clauses = new List<(string Reading, string? Engine, string Text)>();
            var segments = CompositionText.Segments(CompositionText.Read(input.Text, env), env);
            var typed = CompositionText.TypedSpans(segments, env);
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment.IsEnglish) clauses.Add((segment.Kana, null, typed[i].Text));
                else if (env.Settings.LiveConversion && CompositionConversion.CountsAsLive(segment.Kana))
                    clauses.AddRange(CompositionConversion.LiveClauses(input, env, segment.Kana, i > 0 && segments[i - 1].IsEnglish, CompositionConversion.SegmentBefore(env, string.Concat(clauses.Select(c => c.Text))))
                        .Select(c => (c.Reading, c.Engine, c.Text)));
                else clauses.Add((segment.Kana, null, segment.Kana));
            }
            return clauses;
        }
        finally
        {
            input.Text = original;
        }
    }

    /// <summary>
    /// 保護区間 (メンション・URL・メール・パス) を、自動変換・かな化・幅変換・誤字補正・学習・確定後補正に
    /// 渡さず、打った原文のまま確定する。Space は IME が半角空白を 1 つ足し、Enter は足さない。
    /// </summary>
    internal static void CommitProtected(CompositionState state, CompositionInput input, CompositionEnv env, string suffix)
    {
        // 表示の再計算に依存せず、打った原文そのものを使う (確定条件で区間分けが変わっても記号を変えない)。
        var text = (CompositionText.IsProtectedRaw(CompositionText.Read(input.Text, env)) ? ComposedText.Of(SpanKind.Protected, CompositionText.Raw(CompositionText.Read(input.Text, env))) : CompositionPresentation.CurrentComposed(input, env, final: true)) + ComposedText.English(suffix);
        state.Correctable.Clear();
        // raw を空にして渡し、保護原文を確定後補正・学習の対象にしない (INV-06)。
        CommitText(state, env, text, english: false, preserveText: true, rule: "保護区間 (メンション・URL・メール・パス) で終わるので、打ったまま");
    }

    /// <summary>確定した日本語の語句を、予測変換のために覚える (設定「予測変換」が ON のとき)。</summary>
    private static void RememberPhrase(CompositionState state, CompositionInput input, CompositionEnv env, string reading, string text, bool english)
    {
        if (!english && CompositionLifecycle.Learning(state, input, env) && env.Settings.PredictiveCandidates) env.Learning.RememberPhrase(reading, text);
    }

    /// <summary>確定した文節を学習する。設定「ユーザー学習を使う」が OFF なら覚えない (issue #277)</summary>
    internal static void LearnClauses(CompositionState state, CompositionInput input, IReadOnlyList<Clause> clauses, CompositionEnv env)
    {
        if (CompositionLifecycle.Learning(state, input, env)) env.Learning.Learn(clauses, CompositionText.LearningContext(env));
    }

    /// <summary>予測変換の候補を確定する。日本語なら覚え直して、次からより前に出す。</summary>
    internal static void CommitPrediction(CompositionState state, CompositionInput input, CompositionEnv env, Prediction prediction)
    {
        if (!prediction.Text.All(c => c < 0x80) && CompositionLifecycle.Learning(state, input, env))
            env.Services.Predictor?.Phrases?.Remember(PredictionReading(env, prediction.Text, prediction.Reading), prediction.Text);
        // 最後の文節の続きの候補なら、前の文節 (見えているとおり) とつなげて確定する
        var text = CompositionPresentation.PredictionText(prediction);
        var english = text.Text.All(c => c < 0x80);
        CommitText(state, env, text, english, english ? text.Text : CompositionText.Raw(CompositionText.Read(input.Text, env)), chosen: true);
    }

    /// <summary>予測の候補の読み (前に確定したときの読み)。分からなければ打ちかけの読み。</summary>
    private static string PredictionReading(CompositionEnv env, string prediction, string typed) =>
        env.Services.Predictor?.Phrases?.ReadingOf(prediction, typed) ?? typed;
    private const int MaxCorrectable = 4;

    /// <summary>
    /// 今確定しようとしている語 (raw) で文脈がはっきりしたら、直前に確定した英語とも日本語とも読める語を確定し直す。
    /// 例: 「i」を Space で「胃」にした後に want と打つ → 「I want」、「sushi 」の後に「がすき」 → 「すしがすき」。
    /// </summary>
    private static (int Count, ComposedText Text)? CorrectPreviousCommit(CompositionState state, CompositionEnv env, string raw, bool english)
    {
        if (state.Correctable.Count == 0 || !CompositionLifecycle.AutoCorrect(env) || !env.CanDeleteBackward) return null;
        var previous = state.Correctable[^1];
        List<CommitRecord> targets = [];
        ComposedText? replacement = null;
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
            if (targets.Any(t => CompositionConversion.IsSuruRomajiKeepJapanese(env, t.Raw)))
            {
                // Space で変換を始めていた分の空白だけは残す (してPR → して PR)。
                replacement = ComposedText.Concat(targets.Select(t => t.Text + ComposedText.English(t.SpaceIntended ? " " : "")));
            }
            else
            {
                replacement = ComposedText.English(string.Concat(targets.Select(t => (t.Raw == "i" ? "I" : t.Raw) + (t.SpaceIntended ? " " : ""))));
            }
        }
        // 前に英語が続いていた普通の英単語 (Are you の you) は、英文の続きとして打ったもの。後ろが日本語 (すし？) でも日本語に直さない (#265)。
        // 日本語由来の語 (I love sushi + がすき) は、日本語のつもりのことがあるので今までどおり直す。
        else if (previous.English && !(previous.AfterEnglish && !CompositionDetector.IsJapaneseOriginWord(previous.Raw.ToLowerInvariant())) && !english && !env.Detector.IsAmbiguousWord(raw) && raw.Any(char.IsAsciiLetter))
        {
            // 英語で確定した語を日本語に (Space で空白を入れていたら取る)。
            targets = [previous];
            replacement = ComposedText.Kana(env.Detector.Romaji.ConvertLenient(previous.Raw.ToLowerInvariant(), final: true));
            // ローマ字として読んでもよく使う語の読みにならない語 (issue → いっすえ、api → あぴ) は英語のまま (issue #121, #124)。
            // sushi → すし のように、日本語の語として読めるときだけ直す。
            if (env.Services.RomajiTypos is { } lexicon && !lexicon.IsWord(replacement.Text)) return null;
        }
        // ユーザーが英字 / かなに直して覚えた語 (F10 で英字にした api) は、覚えたとおりなら書き換えない (issue #124)。
        if (targets.Any(t => CompositionText.LearnedLanguages(env).Get(t.Raw.ToLowerInvariant()) == t.English)) return null;
        var original = string.Concat(targets.Select(t => t.Text.Text));
        if (replacement is null || replacement.Text == original) return null;
        // 直前に確定した語が、今のキャレットの位置に無い (別の場所をクリックした等) ときは書き換えない。
        // 消すつもりの文字がそこに無いので、消すとキャレットの前の別の文字が消えてしまう。
        // 入力欄の文字をその場で読めるホストだけ確かめる (Windows は別のスレッドで後から読むので、古い値で誤って止めない)。
        if (CompositionOutput.RawBefore(state.Out, env) is { } caretBefore && !caretBefore.EndsWith(NotationRules.Write(ComposedText.Concat(targets.Select(t => t.Text)), env.Settings.Punctuation), StringComparison.Ordinal))
        {
            Diagnostics.Log.Decision($"キャレットの位置が変わっているので確定し直しません: {Diagnostics.Log.Text(original)}");
            state.Correctable.Clear();
            return null;
        }

        Diagnostics.Log.Decision($"前後の文脈に合わせて確定し直しました: {Diagnostics.Log.Text(original)}→{Diagnostics.Log.Text(replacement.Text)}");
        // 消して入れ直すのは、続けて確定する文字と一緒に送る (CommitText)。別々に送ると、その間に入れ直した文字と
        // 続けて確定した文字の順番が入れ替わるアプリがある (Are yoよ人間う？: issue #258)
        state.Correctable.Clear();
        return (original.Length, replacement);
    }

    /// <summary>カッコの組 (設定「カッコを打ったら中にカーソルを移動」で、確定したら中に入る)。</summary>
    private static readonly HashSet<string> BracketPairs = ["「」", "『』", "（）", "()", "［］", "[]", "｛｝", "{}", "【】", "〈〉", "《》", "〔〕", "\"\"", "“”"];

    /// <summary>確定して入力する。確定した文字列を、次の入力の文脈として覚えておく。</summary>
    internal static void CommitText(CompositionState state, CompositionEnv env, ComposedText text, bool english, string raw = "", bool chosen = false, bool preserveText = false, string rule = "")
    {
        if (state.Scene is CompositionScene.WithInput scene) LogCommit(scene, env, text.Text, rule);
        var spaceIntended = state.Scene is CompositionScene.Converting { Conversion.StartedBy: ConversionTrigger.Space };
        var afterEnglish = env.Context().PrecedingEnglish == true;
        CompositionLifecycle.Finish(state);
        if (text.Length == 0) return;
        var corrected = CorrectPreviousCommit(state, env, raw, english);
        if (!preserveText)
            text = NotationRules.Commit(text, corrected?.Text.Text ?? env.Context().PrecedingText, env.Context().FollowingText, env.Settings);
        // 英語とも日本語とも読める語を、文脈を決めずに (選び直さずに) 確定したときだけ、後で確定し直せるようにしておく。
        if (!chosen && env.Detector.IsAmbiguousWord(raw))
        {
            // 前の語の後に Space で区切って続けたときだけつなげる (それ以外は新しい並び)。
            if (state.Correctable.Count > 0 && !state.Correctable[^1].SpaceIntended) state.Correctable.Clear();
            state.Correctable.Add(new CommitRecord(text, raw, english, spaceIntended, afterEnglish));
            if (state.Correctable.Count > MaxCorrectable) state.Correctable.RemoveAt(0);
        }
        else state.Correctable.Clear();
        if (corrected is var (count, replacement))
        {
            CompositionContext.NoteCommit(state, env, count, replacement + text);
            CompositionOutput.ReplaceBackward(state.Out, env, count, replacement, text);
        }
        else
        {
            CompositionContext.NoteCommit(state, env, 0, text);
            CompositionOutput.Commit(state.Out, env, text);
        }
        // 設定「カッコを打ったら中にカーソルを移動」: カッコの組だけを確定したら、中に入る (issue #72)。
        // 確定に付けて返す (キーをアプリに通して動かすと、確定に使ったキーまでアプリに通ってしまう: #991・#1353・#1404・#1581)。
        if (env.Settings.CursorIntoBrackets && text.Length == 2 && BracketPairs.Contains(text.Text))
            CompositionOutput.MoveCaretBackward(state.Out, 1);
    }

    /// <summary>変換ボックスの最後が英語の区間か (Space を空白として扱うか)。</summary>
    internal static bool EndsWithEnglish(CompositionTextState text, CompositionEnv env) =>
        text.Mode == DisplayMode.Auto && CompositionText.Segments(CompositionText.Read(text, env), env) is { Count: > 0 } segments && segments[^1].IsEnglish;
}
