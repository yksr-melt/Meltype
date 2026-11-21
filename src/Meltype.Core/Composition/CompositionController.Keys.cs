// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Input;
using Meltype.Libraries.Chars;

namespace Meltype.Composition;

/// <summary>キーの処理: 入力中・変換中のキー、英数状態で判定中の語、かな配列、Ctrl のショートカット。</summary>
internal static partial class CompositionController
{
    /// <summary>未確定文字を確定せず日本語／英字へ切り替える。未確定入力がなければ false。</summary>
    private static bool TogglePendingInput(CompositionState state, CompositionEnv env)
    {
        // 英数状態の自動判定中の文字はすでにアプリへ渡しているので、未確定文字には含めない。
        if (CompositionText.IsEmpty(state.Text))
        {
            AbandonDirectHold(state);
            return false;
        }
        SetMode(state, CompositionText.IsAlphanumeric(state.Text, env) ? DisplayMode.Hiragana : DisplayMode.HalfWidthAlphanumeric);
        UpdateView(state, env);
        return true;
    }

    private static void HandleKey(CompositionState state, CompositionEnv env, KeyEvent e)
    {
        var vk = e.Vk;
        if (state.HeldLetters.Length > 0)
        {
            if (VirtualKeys.IsLetter(vk) && !state.Shift && !state.Modifier)
            {
                Hold(state, env, e);
                return;
            }
            // 母音の後の - は長音 (ro-maji → ろーまじ、de-ta → でーた)。英単語の途中にはまず出てこないので、ローマ字として
            // 読めれば日本語に戻す。英語の接頭辞 (e-mail、co-op) かもしれないときは - も送って判定を続け、- の後ろで決める
            // (e-mail → 英語、e-me-ru → えーめーる)。
            var part = LastHyphenPart(state);
            if (vk == VirtualKeys.OemMinus && !state.Shift && !state.Modifier && part.Length > 0 &&
                env.Detector.Romaji.AnalyzeFragment(part) is { IsValid: true, Partial: "" })
            {
                var prefix = !state.HeldLetters.ToString().Contains('-') && CompositionDetector.IsHyphenPrefix(part);
                state.HeldLetters.Append('-');
                state.HeldLastKeyTime = e.TimeMs;
                if (prefix)
                {
                    SendHeld(state, e);
                    return;
                }
                Diagnostics.Log.Decision($"英数状態でローマ字を検知: {Diagnostics.Log.Text(state.HeldLetters.ToString())} (母音の後の長音)");
                SwitchHeldToJapanese(state, env);
                return;
            }
            // 英字以外のキー・Shift・Ctrl で判定を打ち切り、英語として出してから、そのキーを普通に処理する。
            Diagnostics.Log.Info($"英数状態の判定を打ち切り: キー 0x{vk:X2} (Shift {state.Shift})");
            ReleaseHeldAsEnglish(state, env);
        }

        // 大文字・文節の区切りの変更に使う Shift は、ほかのキーと一緒に届く。Shift だけでは何もしない。
        if (IsShift(vk)) return;
        // 何も打っていないときのテンキーの数字・記号は、変換ボックスに入れず、そのまま通す (半角で入る。Excel・CAD などで数値を打つとき: issue #460)。
        // 入力中なら今までどおり変換ボックスに入れる (3 + ji → 3時 のように続けて変換できる)
        if (CompositionText.IsEmpty(state.Text) && !state.Control && !state.Modifier && vk is >= 0x60 and <= 0x6F)
        {
            PassKey(state);
            return;
        }
        if (state.Control && !CompositionText.IsEmpty(state.Text))
        {
            // 変換ボックスに入力中の Ctrl + キー (Ctrl+U/I/O/P はかな・英字の切り替え、Ctrl+H は BackSpace。それ以外は確定してから通す)。
            if (IsControl(vk)) return;
            // 設定「Control キーの割り当て」が ATOK 風: Ctrl + K / L で文節の長さを変える。変換中だけ (Shift + ← → と同じ。issue #599)
            if (IsConverting(state) && env.Settings.ControlKeys == Config.ControlKeyLayout.Atok && vk is 'K' or 'L')
            {
                Resize(state, env, vk == 'L' ? +1 : -1);
                return;
            }
            // 設定「Control キーの割り当て」: 組み込みの割り当てより先に見る (issue #63・#209)
            if (!CompositionText.IsEmpty(state.Text) && BoundControlKey(env, vk) is { } bound)
            {
                vk = bound;
            }
            // 設定「Control キーの割り当て」が macOS 風: 組み込みの Ctrl+U/I/O/P/H より先に見る (issue #643)
            else if (!CompositionText.IsEmpty(state.Text) && env.Settings.ControlKeys == Config.ControlKeyLayout.Mac && MacControlShortcut(vk) is { } mac)
            {
                if (mac is ResizeExtend or ResizeShrink)
                {
                    if (IsConverting(state)) Resize(state, env, mac == ResizeExtend ? +1 : -1);
                    return;
                }
                vk = mac;
            }
            else if (!CompositionText.IsEmpty(state.Text) && ControlShortcut(vk) is { } function)
            {
                // Ctrl+U/I/O/P: F6/F7/F10/F9 と同じ (ATOK と同じ割り当て。続けて押すと大文字・小文字も切り替わる)。Ctrl+H は BackSpace。
                vk = function;
            }
            else if (!CompositionText.IsEmpty(state.Text) && vk == VirtualKeys.Return)
            {
                // 変換中の Ctrl+Enter (送信のショートカット) は確定だけを行う。キーはアプリへ渡さない。
                // 渡すと、アプリが確定を反映する前に送信してしまい、確定した文字が欠けた内容が送られる
                // (X のリプライで「田所工事」と「田所工事だよ」が二重に投稿された)。日本語 IME の通例どおり、
                // 変換中の Enter は確定、送信は次の Enter にする。
                CommitIfAny(state, env);
                return;
            }
            else
            {
                // ほかのショートカット (Ctrl+C・Ctrl+Z …) は、確定してから Ctrl と一緒にアプリへ送る。
                CommitIfAny(state, env);
                PassKey(state);
                return;
            }
        }
        else if (state.Modifier || VirtualKeys.IsModifier(vk))
        {
            // Ctrl / Alt / Command のショートカットの前に確定する。
            CommitIfAny(state, env);
            PassKey(state);
            return;
        }
        if (!env.BypassKanaLayout && HandleKanaLayout(state, env, e)) return;

        if (state.Reconversion is not null && vk == VirtualKeys.Escape)
        {
            ClearComposition(state);
            return;
        }

        if (!IsComposing(state))
        {
            StartWith(state, env, e);
            return;
        }

        if (IsConverting(state) && HandleConversionKey(state, env, vk)) return;

        switch (vk)
        {
            case VirtualKeys.Convert or VirtualKeys.Down when env.Settings.EnglishFirstInput && state.Text.Mode == DisplayMode.HalfWidthAlphanumeric && !state.ModeByKey && !IsProtectedInput(state):
                // 設定「英字を主に入力」: 変換キー / ↓ で、打った英字をローマ字として読んで変換する (issue #839)
                state.Text.Mode = DisplayMode.Hiragana;
                CompositionText.FixTypos(state.Text, env);
                StartConversion(state, env, preferJapanese: true);
                return;
            case VirtualKeys.Space when env.Settings.EnglishFirstInput && state.Text.Mode == DisplayMode.HalfWidthAlphanumeric && !state.ModeByKey && !IsProtectedInput(state) &&
                                        !state.Shift:
                // 設定「英字を主に入力」: Space は変換ではなく、ふつうの空白として入力中の文字に足す (ls -la)
                CompositionText.Append(state.Text, env, ' ');
                return;
            case VirtualKeys.Convert:
                CompositionText.FixTypos(state.Text, env);
                StartConversion(state, env, preferJapanese: true);
                return;
            case VirtualKeys.Return when SelectedPrediction(state) is { } prediction:
                // 予測変換の候補を選んでいれば、それを確定する。
                CommitPrediction(state, env, prediction);
                return;
            case VirtualKeys.Return:
                if (IsProtectedInput(state)) { CommitProtected(state, env, ""); return; }
                // 設定「Enter は見えているとおりに確定」: 打ち間違いの直しは Space で変換するときだけ (issue #677)
                CompositionText.FixTypos(state.Text, env, correct: !env.Settings.EnterCommitsAsShown);
                Commit(state, env);
                return;
            case VirtualKeys.Space when state.Shift:
                // Shift+Space: 英語と判定した語でも、ローマ字として読んで変換する (go → 語、camera → かめら)。
                CompositionText.FixTypos(state.Text, env);
                state.SpaceStartedConversion = true;
                StartConversion(state, env, preferJapanese: true);
                return;
            case VirtualKeys.Space when env.Settings.SeparateKeyRoles && !IsProtectedInput(state):
                // 設定「入力中のキーの役割を分ける」: Space は英語・日本語の判定にかかわらず候補を開く (英字のまま確定して空白を入れない)。
                // 英字で見えている語も、英字を最初の候補に残し、ローマ字として読めれば日本語の候補も選べる (issue #199)
                CompositionText.FixTypos(state.Text, env);
                state.SpaceStartedConversion = true;
                StartConversion(state, env);
                return;
            case VirtualKeys.NonConvert when env.Settings.NonConvertCyclesKana && !IsProtectedInput(state):
                // ひらがな → 全角カタカナ → 半角カタカナ → ひらがな の順に循環する。自動判定中は、表示中の文字列にひらがな (長音を含む) があれば全角カタカナにし、なければひらがなにする。
                SetMode(state, NextKanaCycleMode(state, env));
                state.ModeByKey = true;
                return;
            case VirtualKeys.NonConvert when env.Settings.SeparateKeyRoles:
                // 無変換: 今までの入力を英字にする (F10 と同じ。続けて押すと大文字・小文字も切り替わる)
                SetAlphanumericMode(state, env, DisplayMode.HalfWidthAlphanumeric);
                state.ModeByKey = true;
                return;
            case VirtualKeys.Kana when env.Settings.SeparateKeyRoles:
                // ひらがな / カタカナ キー: 押すたびに ひらがな ⇔ カタカナ (漢字・英字の状態からは、まずひらがな)
                SetMode(state, state.Text.Mode == DisplayMode.Hiragana && !IsConverting(state) ? DisplayMode.Katakana : DisplayMode.Hiragana);
                state.ModeByKey = true;
                return;
            case VirtualKeys.Space:
                // 保護区間 (メンション・URL・パス) の末尾は、自動変換せず原文のまま + 半角空白で確定する。
                if (IsProtectedInput(state)) { CommitProtected(state, env, " "); return; }
                CompositionText.FixTypos(state.Text, env);
                // 英語と判定した語で終わっているなら、変換ではなく確定して空白を入れる
                // (日本語の部分は、ライブ変換が ON なら漢字にして、OFF なら見えているかなのまま確定)。
                // 数字だけ (1、12) は、前が英文なら確定して空白 (I have 2 cats)。それ以外は変換して ① 一 Ⅰ などの候補を出す。
                // 打った英字全体を読みとして登録した語 (ユーザー辞書の zh → ←、略入力の ast → 明日) があれば、英語と判定しても変換する
                if (HasRegisteredReading(state, env))
                {
                    state.SpaceStartedConversion = true;
                    StartConversion(state, env);
                }
                else if (SpaceCommitsMarkdownMarker(state))
                {
                    // Markdown の記号だけは、変換せず打った半角 + 空白で確定する
                    CommitText(state, env, CompositionText.Raw(state.Text) + " ", english: false, preserveText: true, rule: "Space: Markdown の記号なので打ったまま");
                }
                else if (SpaceCommitsAlphanumeric(state, env))
                {
                    Commit(state, env, suffix: " ");
                }
                else if (EndsWithEnglish(state, env)) CommitText(state, env, CompositionText.RenderSegments(state.Text, env, final: true, env.Settings.LiveConversion ? (kana, afterEnglish, shown) => string.Concat(ConvertJapaneseSegment(state, env, kana, afterEnglish, SegmentBefore(env, shown), zenzai: true).Select(c => c.Text)) : null) + " ", english: true, CompositionText.Raw(state.Text), rule: "Space: 英語で終わるので変換せずに確定");
                else if (SpaceCommitsAutoEnglish(state, env)) CommitText(state, env, CompositionText.Raw(state.Text) + " ", english: true, CompositionText.Raw(state.Text), rule: "Space: かなにならない子音で終わる英単語");
                else
                {
                    // Space で変換した = 語の後に空白を打とうとした、とも取れる (確定し直して英語にするときに空白を足す)。
                    state.SpaceStartedConversion = true;
                    StartConversion(state, env);
                }
                return;
            case VirtualKeys.Back:
                CompositionText.RemoveLast(state.Text, env);
                return;
            case 0x2E /* Delete */ when SelectedPrediction(state) is { } prediction && env.Services.Predictor?.Phrases is { } phrases:
                // 予測の候補を選んでいる間の Delete: その語句を学習から消す (issue #349)
                phrases.Forget(prediction.Text);
                state.PredictionKey = "\u0000"; // 候補を作り直す
                state.PredictionIndex = -1;
                return;
            case VirtualKeys.Escape when state.PredictionIndex >= 0:
                // 予測の候補を選んでいたら、選ぶのをやめるだけ (打った内容は残す)。
                state.PredictionIndex = -1;
                return;
            case VirtualKeys.Escape when env.Settings.EscStepsBack && StepBack(state, env):
                return;
            case VirtualKeys.Escape:
                CompositionText.Clear(state.Text);
                return;
            case VirtualKeys.Tab when FindMisspelling(state, env) is { } typo:
                // もしかして: 書き間違いを直す。
                FixMisspelling(state, typo);
                return;
            case VirtualKeys.Tab when CompositionText.Suggestion(state.Text, env) is not null:
                // 自動判定を使わないとき: 提案どおり英字にする。
                state.Text.SuggestionAccepted = true;
                return;
            case VirtualKeys.Tab when CurrentPredictions(state, env).Count > 0:
                // 予測変換の候補を選ぶ (Tab で次、Shift+Tab で前。最後の次は選ばない状態に戻る)。
                var step = state.Shift ? -1 : 1;
                state.PredictionIndex = (state.PredictionIndex + 1 + step + state.Predictions.Count + 1) % (state.Predictions.Count + 1) - 1;
                return;
            case VirtualKeys.Tab when !IsConverting(state) && !state.Shift && env.Settings.TabConversion && TabStartsConversion(state, env):
                // Space なら変換を始める場面の Tab は、Space と同じく変換を始める (Microsoft IME と同じ。空白を打とうとした意味ではない: issue #219)。
                StartConversion(state, env);
                state.TabStartedConversion = IsConverting(state);
                return;
            case VirtualKeys.F6: SetMode(state, DisplayMode.Hiragana); state.ModeByKey = true; return;
            case VirtualKeys.F7: SetMode(state, DisplayMode.Katakana); state.ModeByKey = true; return;
            case VirtualKeys.F8: SetMode(state, DisplayMode.HalfWidthKatakana); state.ModeByKey = true; return;
            case VirtualKeys.F9: SetAlphanumericMode(state, env, DisplayMode.FullWidthAlphanumeric); state.ModeByKey = true; return;
            case VirtualKeys.F10: SetAlphanumericMode(state, env, DisplayMode.HalfWidthAlphanumeric); state.ModeByKey = true; return;
            case VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down when !CompositionText.IsAlphanumeric(state.Text, env):
                // 変換前でも矢印キーで文節の選択に入る (Mac のライブ変換と同じ)。
                EnterClauseSelection(state, env, vk);
                return;
        }

        if (VirtualKeys.IsHankakuZenkaku(vk))
        {
            TogglePendingInput(state, env);
            return;
        }

        if (state.Text.KanaInput && KanaOf(state, e) is { } key)
        {
            CommitBeforeTyping(state, env);
            CompositionText.AppendKana(state.Text, key.Raw, key.Kana);
            return;
        }

        if (CharOf(e) is { } ch && !char.IsControl(ch) && ch != ' ')
        {
            // 打ち始めの ・ を Space で / にしてから打った名前 (/help) は、/ を確定して半角のままアプリへ渡す。
            if (IsConverting(state) && CompositionText.IsSlashAtStart(state.Text) && state.Clauses is [{ Text: "/" }] && SigilWord.IsNameChar(ch))
            {
                Commit(state, env);
                PassKey(state);
                return;
            }
            CommitBeforeTyping(state, env);
            CompositionText.Append(state.Text, env, ch);
            return;
        }

        // 矢印 (英字だけのとき)・Tab・Delete などは確定してから通す。
        Commit(state, env);
        PassKey(state);
    }

    /// <summary>変換ボックスを開く記号・数字。</summary>
    internal static bool StartsWithSymbol(char c) => c is >= '!' and <= '~' && !char.IsAsciiLetter(c);

    /// <summary>変換ボックスが空のときの最初の打鍵。英字・句読点なら入力を始め、それ以外はそのまま通す。</summary>
    private static void StartWith(CompositionState state, CompositionEnv env, KeyEvent e)
    {
        if (e.Vk == VirtualKeys.Convert)
        {
            if (ReconversionSelectionNow(state, env) is not { } selection || string.IsNullOrWhiteSpace(selection.Reading)) return;
            state.Correctable.Clear();
            // ローマ字のまま入った文は、日本語にする所が無ければ何もしない (英字をローマ字として読んだ候補を出さない)
            if (IsRomajiText(selection.Text))
            {
                StartRomajiReconversion(state, env, selection);
                return;
            }
            BeginComposition(state, env);
            state.Reconversion = selection;
            foreach (var kana in selection.Reading) CompositionText.AppendKana(state.Text, kana, kana);
            state.Text.Mode = DisplayMode.Hiragana;
            StartConversion(state, env, preferJapanese: true);
            return;
        }
        // 日本語入力のときの Shift+Space は全角スペース (Microsoft IME と同じ: issue #24)。英数状態では普通の空白のまま。
        // 設定「スペースの幅」が全角では、Space が全角で Shift+Space が半角 (issue #259)
        var spaceWidth = env.Settings.SpaceWidth;
        if (e.Vk == VirtualKeys.Space && !state.Direct && (spaceWidth == Config.SpaceWidth.FullWidth || state.Shift))
        {
            // 「スペースの幅」が半角なら Shift+Space も半角 (issue #708)
            var shifted = state.Shift;
            var halfWidth = spaceWidth switch
            {
                Config.SpaceWidth.FullWidth => shifted,
                Config.SpaceWidth.HalfWidth => true,
                Config.SpaceWidth.Standard => false,
                _ => throw new InvalidOperationException($"不明な SpaceWidth: {spaceWidth}"),
            };
            CommitText(state, env, halfWidth ? " " : "　", english: false);
            return;
        }
        // かな入力: かなのキーならすべて入力を始める (英数状態でなければ)。
        if (KanaInput(state, env) && !state.Direct && KanaOf(state, e) is { } key)
        {
            BeginComposition(state, env);
            CompositionText.AppendKana(state.Text, key.Raw, key.Kana);
            return;
        }
        var c = CharOf(e);
        var letter = VirtualKeys.IsLetter(e.Vk) && c is { } l && char.IsAsciiLetter(l);
        if (letter && state.Direct)
        {
            // 英数状態: ローマ字かどうか判定する (打鍵はすぐ送る)。大文字で始まる語は英語なのでそのまま通す。
            if (env.ClassifyDirect is null || state.Shift || char.IsAsciiLetterUpper(c!.Value))
            {
                Diagnostics.Log.Info($"英数状態: {Diagnostics.Log.Text(c.ToString()!)}は大文字 / Shift なので英語のまま");
                PassKey(state);
                DirectDecided(state, false);
            }
            else Hold(state, env, e);
            return;
        }
        // 句読点・かぎかっこ・長音・中黒・数字でも入力を始める (、。「」ー・)。英数状態ではそのまま通す。
        if (letter || (c is { } symbol && StartsWithSymbol(symbol) && !state.Direct))
        {
            BeginComposition(state, env);
            CompositionText.Append(state.Text, env, c!.Value);
            return;
        }
        PassKey(state);
    }

    /// <summary>
    /// 新しい入力を始める。入力欄のキャレットの前後の確定済みの文字を読みに行き、
    /// 英語とも日本語とも読める語の判定と、変換の文脈に使う。読めるまでは自分が最後に確定した文字列で代用する。
    /// </summary>
    /// <summary>
    /// 変換中に次の文字を打ったとき。普通は今の候補で確定して新しい入力を始める (Microsoft IME と同じ)。
    /// 設定「変換したあとに続けて打っても確定しない」が ON なら、変換をやめて打った文字を続けて入れる。選んでいた候補は覚えておき、
    /// もう一度変換したときに同じ読みの文節で使う (issue #123)。
    /// </summary>
    private static void CommitOrKeepConversion(CompositionState state, CompositionEnv env)
    {
        state.ModeByKey = false;
        state.EscReverted = false;
        if (!env.Settings.ContinueAfterConversion || state.Reconversion is not null)
        {
            Commit(state, env);
            BeginComposition(state, env);
            return;
        }
        foreach (var clause in state.Clauses.Where(c => !c.IsEnglish)) state.KeptChoices[clause.Reading] = clause.Text;
        state.Clauses = [];
    }

    /// <summary>
    /// 入力中に次の文字を打ったとき。変換中なら、今の候補で確定して新しい入力を始める (IME と同じ。設定で、確定せずに続けることもできる)。
    /// F6〜F10 でかな・英字にしたあとなら、見えているとおりに確定して新しい入力を始める (続けて打った文字までカタカナ・大文字にしない)。
    /// </summary>
    private static void CommitBeforeTyping(CompositionState state, CompositionEnv env)
    {
        if (IsConverting(state)) CommitOrKeepConversion(state, env);
        else if (state.ModeByKey)
        {
            Commit(state, env);
            BeginComposition(state, env);
        }
        else if (SelectedPrediction(state) is { } prediction)
        {
            // Tab で予測の候補を選んだあとに続けて打ったら、その候補で確定して新しい入力を始める (選ぶ前の打ちかけに戻さない)
            CommitPrediction(state, env, prediction);
            BeginComposition(state, env);
        }
    }

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)

    private static void Hold(CompositionState state, CompositionEnv env, KeyEvent e)
    {
        if (state.HeldLetters.Length == 0) state.PrivateComposition = Private(state);
        state.HeldLetters.Append(VirtualKeys.ToLetter(e.Vk));
        state.HeldLastKeyTime = e.TimeMs;
        SendHeld(state, e);
        DecideHeld(state, env, final: false);
    }

    /// <summary>
    /// 判定中の語の打鍵を、判定を待たずにアプリへ送る。前は判定できるまで保留していたので、英単語 (can・game・today) は
    /// Space を押すまで画面に出ず、英数状態の入力が遅れて見えた。ローマ字と分かったら、送った分を BackSpace で消す。
    /// </summary>
    private static void SendHeld(CompositionState state, KeyEvent e)
    {
        CompositionOutput.PassCurrent(state.Out);
        state.HeldSent.Append(CharOf(e) ?? '\0');
    }

    /// <summary>判定中の英字の、最後の - より後ろ (e-ma → ma)。- が無ければ全体。</summary>
    private static string LastHyphenPart(CompositionState state)
    {
        var letters = state.HeldLetters.ToString();
        return letters[(letters.LastIndexOf('-') + 1)..];
    }

    private static void DecideHeld(CompositionState state, CompositionEnv env, bool final)
    {
        // 英語の接頭辞の後の - (e-、co-) の後ろだけで判定する。e-mail・co-op のような - の入った英単語なら英語。
        var part = LastHyphenPart(state);
        var verdict = env.Detector.IsHyphenatedEnglishWord(CompositionText.LearningContext(state.Text, env), state.HeldLetters.ToString()) ? Verdict.English
            : part.Length == 0 ? Verdict.Undecided
            : env.ClassifyDirect!(part, final);
        // 2 文字の英単語 (up、my) でも、ローマ字の打ちかけとして読める (う + p) なら、続きを見てから決める
        // (upa- → うぱー、my → みゃ)。Space などで打ち終われば英語。
        if (verdict == Verdict.English && !final && part.Length <= 2 && state.HeldLetters.Length <= 2 &&
            env.Detector.Romaji.AnalyzeFragment(part.ToLowerInvariant()) is { IsValid: true, Partial.Length: > 0 })
        {
            verdict = Verdict.Undecided;
        }
        Diagnostics.Log.Info($"英数状態の判定: {Diagnostics.Log.Text(state.HeldLetters.ToString())}→ {verdict}{(final ? " (打ち終わり)" : "")}");
        if (verdict == Verdict.Japanese) SwitchHeldToJapanese(state, env);
        else if (verdict != Verdict.Undecided || final) ReleaseHeldAsEnglish(state, env);
    }

    /// <summary>ローマ字だった: 日本語入力に戻し、判定中の英字を変換ボックスに入れる。</summary>
    private static void SwitchHeldToJapanese(CompositionState state, CompositionEnv env)
    {
        var letters = state.HeldLetters.ToString();
        var sent = state.HeldSent.ToString();
        // 判定を待たずに送っていた英字を消して、変換ボックスに入れ直す。
        CompositionOutput.DeleteHeld(state.Out, sent);
        ClearHeld(state);
        DirectDecided(state, true);
        BeginComposition(state, env, state.PrivateComposition);
        foreach (var c in letters)
        {
            if (state.Text.KanaInput && Detection.KanaDetector.KanaForKey(char.ToUpperInvariant(c), char.IsAsciiLetterUpper(c)) is { } kana) CompositionText.AppendKana(state.Text, c, kana);
            else CompositionText.Append(state.Text, env, c);
        }
    }

    /// <summary>
    /// JIS かな以外のかな配列 (月・花・NICOLA・中指NICOLA・TRON) と SKY 配列の打鍵 (issue #1415)。配列のキーとして扱ったら true。
    /// 打ったキーの英字は英単語の判定に使わない (刻印と違う配列なので)。かなをそのまま原文にする。
    /// </summary>
    private static bool HandleKanaLayout(CompositionState state, CompositionEnv env, KeyEvent e)
    {
        if (!KanaInput(state, env) && !state.Direct && env.Settings.RomajiLayout == RomajiLayout.Sky)
        {
            // SKY 配列: キーをローマ字の綴りにして足す。英語の判定はしない (ひらがなで見せる)
            if (state.Shift || SkyLayout.Romaji(e.Vk) is not { } romaji) return false;
            if (!IsComposing(state)) BeginComposition(state, env);
            else CommitBeforeTyping(state, env);
            if (state.Text.Mode == DisplayMode.Auto) state.Text.Mode = DisplayMode.Hiragana;
            foreach (var c in romaji) CompositionText.Append(state.Text, env, c);
            return true;
        }
        var layout = KanaInput(state, env) && !state.Direct ? env.Settings.KanaLayout : KanaLayout.Jis;
        if (state.KanaLayout.Layout != layout) state.KanaLayout = new KanaLayoutEngine(layout);
        if (layout == KanaLayout.Jis) return false;
        // 親指キーは (入力していないとき・変換中も) 受け取っておき、文字に使わずに離したらもとの働き (再変換・変換・無変換) をする
        // (文頭の 変換 + W を再変換にしない・変換中の 変換 + W をシフト面にする。親指シフトの配列では親指キーで変換ボックスを開く: StartsComposition)
        // 前置シフトの直後の BackSpace は、シフトのキーを打ち消す (前の文字は消さない)
        if (e.Vk == VirtualKeys.Back && state.KanaLayout.HasPrefix)
        {
            state.KanaLayout.Reset();
            return true;
        }
        var shift = state.Shift;
        var stroke = state.KanaLayout.Down(e.Vk, shift, e.TimeMs);
        if (!stroke.Handled)
        {
            // 配列のキー以外 (BackSpace・Enter・Esc・Space など): 打ちかけの前置シフトを忘れる
            state.KanaLayout.Reset();
            // 配列の表で空いているかなのキー (月配列の 1 や ]) は何も入れない (JIS かなの文字を入れない)
            return !shift && Detection.KanaDetector.IsKanaKey(e.Vk);
        }
        ApplyKanaStroke(state, env, stroke);
        return true;
    }

    private static void ApplyKanaStroke(CompositionState state, CompositionEnv env, KanaStroke stroke)
    {
        if (stroke.Kana.Length == 0 && stroke.RemoveLast == 0) return;
        if (!IsComposing(state)) BeginComposition(state, env);
        else if (stroke.RemoveLast == 0) CommitBeforeTyping(state, env);
        for (var i = 0; i < stroke.RemoveLast && !CompositionText.IsEmpty(state.Text); i++) CompositionText.RemoveLast(state.Text, env);
        foreach (var kana in stroke.Kana) CompositionText.AppendKana(state.Text, kana, kana);
    }

    /// <summary>かな入力で、その打鍵が入力するかなと、そのキーの英字 (英語として見せるとき用)。かなのキーでなければ null。</summary>
    private static (char Raw, char Kana)? KanaOf(CompositionState state, KeyEvent e)
    {
        var shift = state.Shift;
        if (Detection.KanaDetector.KanaForKey(e.Vk, shift) is not { } kana) return null;
        var raw = CharOf(e) ?? kana;
        // Shift で打った小書き文字 (っ = Shift+Z、ぃ = Shift+E) は、大文字で打った英語 (Z・E) ではない
        // (きのうはたのしかった が たのしかZq になっていた)。
        if (shift && kana != Detection.KanaDetector.KanaForKey(e.Vk, false)) raw = char.ToLowerInvariant(raw);
        return (raw, kana);
    }

    /// <summary>英語だった: 打鍵はもう送ってあるので、判定をやめるだけ。</summary>
    private static void ReleaseHeldAsEnglish(CompositionState state, CompositionEnv env)
    {
        ClearHeld(state);
        state.Correctable.Clear();
        DirectDecided(state, false);
    }

    private static void ClearHeld(CompositionState state)
    {
        state.HeldLetters.Clear();
        state.HeldSent.Clear();
    }

    /// <summary>
    /// Space を押したときに、変換ではなく確定して空白を入れる場面か (英語で終わる 3 条件)。
    /// 例: hello (英語と判定) → true、kana → false。Tab で変換を始めるかの判定にも使う。
    /// </summary>
    private static bool SpaceCommitsAsEnglish(CompositionState state, CompositionEnv env) => SpaceCommitsAlphanumeric(state, env) || EndsWithEnglish(state, env) || SpaceCommitsAutoEnglish(state, env);

    private static bool HasRegisteredReading(CompositionState state, CompositionEnv env) =>
        !state.Text.KanaInput && state.Text.Mode == DisplayMode.Auto && CompositionText.Raw(state.Text) is { Length: >= 2 } raw && raw.All(char.IsAsciiLetter) &&
        (env.Services.UserDictionary?.Lookup(raw.ToLowerInvariant()) is { Count: > 0 } ||
         env.Settings.ConsonantAbbreviations && env.Services.Candidates?.Lookup(raw.ToLowerInvariant()) is { Count: > 0 });

    private static bool SpaceCommitsMarkdownMarker(CompositionState state) => state.Text.Mode == DisplayMode.Auto && CompositionText.IsMarkdownMarker(state.Text);

    private static bool SpaceCommitsAlphanumeric(CompositionState state, CompositionEnv env) =>
        CompositionText.IsAlphanumericAt(state.Text, env, final: true) && !(state.Text.Mode == DisplayMode.Auto && CompositionText.IsNumeric(state.Text) && CompositionText.Raw(state.Text).All(char.IsAsciiDigit) && env.Context().PrecedingEnglish != true);

    private static bool SpaceCommitsAutoEnglish(CompositionState state, CompositionEnv env) => state.Text.Mode == DisplayMode.Auto && env.Detector.IsEnglishAtWordEnd(CompositionText.Raw(state.Text), !env.Settings.AutoDetection);

    /// <summary>
    /// Space の分岐で変換を始める (StartConversion に行く) 場面か。Tab で始めるときも同じ場面に限る (issue #219)。
    /// Space と同じ順で判定するため、英語かを見る前に打ち間違いを直す (Enter で確定するときと同じ直し)。
    /// </summary>
    private static bool TabStartsConversion(CompositionState state, CompositionEnv env)
    {
        // 保護区間が語の途中にあり、その後にかなが続く入力も Tab 変換しない。
        // IsProtectedInput は末尾が保護区間かだけを見るため、ここでは全区間を調べる。
        if (state.Text.Mode == DisplayMode.Auto && CompositionText.ProtectedSpans(state.Text).Count > 0) return false;
        CompositionText.FixTypos(state.Text, env);
        return HasRegisteredReading(state, env) || !SpaceCommitsAsEnglish(state, env);
    }

    /// <summary>変換中だけ意味を持つキー。処理したら true。</summary>
    private static bool HandleConversionKey(CompositionState state, CompositionEnv env, int vk)
    {
        var shift = state.Shift;
        switch (vk)
        {
            case VirtualKeys.Space when shift || state.Shift:
                // Shift+Space: 前の候補へ (Microsoft IME と同じ。Mac でも Shift を押したまま Space で戻れるように: issue #140)
                NextCandidate(state, env, -1);
                return true;
            case VirtualKeys.Convert:
            case VirtualKeys.Space:
            case VirtualKeys.Down:
                NextCandidate(state, env, +1);
                return true;
            case VirtualKeys.Up:
                NextCandidate(state, env, -1);
                return true;
            case VirtualKeys.PageDown:
                // 候補の一覧のページ送り (Microsoft IME と同じ。アプリに通すと確定してしまっていた: issue #1016)
                NextCandidatePage(state, env, +1);
                return true;
            case VirtualKeys.PageUp:
                NextCandidatePage(state, env, -1);
                return true;
            case VirtualKeys.Right when shift:
                Resize(state, env, +1);
                return true;
            case VirtualKeys.Left when shift:
                Resize(state, env, -1);
                return true;
            case VirtualKeys.Right:
                SplitWholeSentence(state);
                state.SelectedClause = Math.Min(state.SelectedClause + 1, state.Clauses.Count - 1);
                return true;
            case VirtualKeys.Left:
                SplitWholeSentence(state);
                state.SelectedClause = Math.Max(state.SelectedClause - 1, 0);
                return true;
            case VirtualKeys.Return when shift && env.Settings.PartialCommit && state.SelectedClause < state.Clauses.Count - 1:
                CommitUpToSelectedClause(state, env);
                return true;
            case VirtualKeys.Return:
                Commit(state, env);
                return true;
            case VirtualKeys.F6 or VirtualKeys.F7 or VirtualKeys.F8 or VirtualKeys.F9 or VirtualKeys.F10
                when env.Settings.FunctionKeysAffectSelectedClause && state.Clauses.Count > 1:
                ApplyFunctionKeyToClause(state, vk);
                return true;
            case VirtualKeys.Tab when FindMisspelling(state, env) is { } typo:
                // もしかして: 書き間違いを直して変換し直す。
                state.Clauses = [];
                FixMisspelling(state, typo);
                StartConversion(state, env);
                return true;
            case VirtualKeys.Tab when state.TabStartedConversion:
                // Tab で始めた変換の中では、Tab が次の候補、Shift+Tab が前の候補 (issue #219)。
                NextCandidate(state, env, shift || state.Shift ? -1 : +1);
                return true;
            case VirtualKeys.Escape when env.Settings.EscRevertsSelectedClause && state.Clauses.Count > 1 &&
                                         !state.Clauses[state.SelectedClause].IsEnglish && state.Clauses[state.SelectedClause].Text != state.Clauses[state.SelectedClause].Reading:
                // 設定「変換中の Esc は選んだ文節だけをかなに戻す」: 選んでいる文節だけをひらがなにして、変換は続ける (issue #670)
                {
                    var clause = state.Clauses[state.SelectedClause];
                    var index = clause.Candidates.IndexOf(clause.Reading);
                    if (index < 0)
                    {
                        clause.Candidates.Add(clause.Reading);
                        index = clause.Candidates.Count - 1;
                    }
                    clause.Index = index;
                }
                return true;
            case VirtualKeys.Back:
            case VirtualKeys.Escape:
                // 変換を取り消して、かなの入力に戻る。
                state.Clauses = [];
                state.TabStartedConversion = false;
                return true;
            case >= 0x31 and <= 0x39 when !shift && !state.Text.KanaInput && env.Settings.NumberKeysSelectCandidates:
            case >= 0x61 and <= 0x69 when !shift && env.Settings.NumberKeysSelectCandidates:
                // 候補の一覧の番号 (1〜9) で選ぶ。番号の無い候補 (一覧より後ろ) なら、普通に打った数字として扱う。
                // かな入力では数字の段のキーは ぬふあうえおやゆよ なので、テンキーだけで選ぶ。
                // 設定「変換中の数字キーで候補を選ぶ」が OFF なら、上の段もテンキーも普通に打った数字 (今の候補で確定して入力する: issue #989)。
                return SelectByNumber(state, env, vk >= 0x61 ? vk - 0x61 : vk - 0x31);
            default:
                return false;
        }
    }

    /// <summary>
    /// 数字キーで、今見えている候補の一覧のページから選ぶ (2 → そのページの 2 番目)。選んだら次の文節へ進み、
    /// 最後の文節なら確定する (Microsoft IME と同じく、番号で選んだ候補はそのまま使う)。
    /// </summary>
    private static bool SelectByNumber(CompositionState state, CompositionEnv env, int row)
    {
        var clause = state.Clauses[state.SelectedClause];
        if (!clause.Expanded) Expand(state, env, clause);
        var index = Math.Max(0, clause.Index) / CandidatePageSize * CandidatePageSize + row;
        if (index >= clause.Candidates.Count) return false;
        clause.Index = index;
        clause.Changed = true;
        if (state.SelectedClause + 1 < state.Clauses.Count) state.SelectedClause++;
        else Commit(state, env);
        return true;
    }

    private static (int Start, int End, Misspelling Misspelling)? FindMisspelling(CompositionState state, CompositionEnv env) =>
        env.Services.Misspellings is { } dictionary ? CompositionText.FindMisspelling(state.Text, env, dictionary) : null;

    private static void FixMisspelling(CompositionState state, (int Start, int End, Misspelling Misspelling) typo)
    {
        Diagnostics.Log.Decision($"もしかして: {Diagnostics.Log.Text(typo.Misspelling.Wrong)}→{Diagnostics.Log.Text(typo.Misspelling.Right)}に直しました。");
        CompositionText.ReplaceReading(state.Text, typo.Start, typo.End, typo.Misspelling.Right);
    }

    /// <summary>「もしかして」の案内。変換ボックスの、打った文字のすぐ下に出す (書き間違いが無ければ null)。</summary>
    private static string? MisspellingSuggestion(CompositionState state, CompositionEnv env) => FindMisspelling(state, env) is { } typo ? $"もしかして: {typo.Misspelling.Right}　<Tab>で修正" : null;

    /// <summary>変換前に矢印キーを押したとき: 文節に区切って、← なら最後の文節、→ なら最初の文節、↑ ↓ なら変換を始めたときの文節を選ぶ。</summary>
    private static void EnterClauseSelection(CompositionState state, CompositionEnv env, int vk)
    {
        StartConversion(state, env);
        if (!IsConverting(state)) return;
        SplitWholeSentence(state);
        var shift = state.Shift;
        state.SelectedClause = vk switch
        {
            VirtualKeys.Left => state.Clauses.Count - 1,
            VirtualKeys.Right => 0,
            _ => InitialClause(env, state.Clauses.Count),
        };
        if (shift && vk is VirtualKeys.Left or VirtualKeys.Right) Resize(state, env, vk == VirtualKeys.Left ? -1 : +1);
        else if (vk == VirtualKeys.Up) NextCandidate(state, env, -1);
    }

    /// <summary>
    /// 設定「Esc で一段ずつ戻す」: 見えている内容を 漢字 → ひらがな → 打ったローマ字 の順に一段戻す。見た目が変わらない段は飛ばす。
    /// 戻せる段が無ければ false (そのまま全部消す)。戻した語は日本語 / 英語の判定として覚えない (issue #676)。
    /// </summary>
    private static bool StepBack(CompositionState state, CompositionEnv env)
    {
        var before = CurrentDisplay(state, env, final: false);
        // 今のモードより先の段だけ (ひらがなの次はローマ字。カタカナなどはひらがなへ)
        DisplayMode[] steps = state.Text.Mode switch
        {
            DisplayMode.Hiragana => [DisplayMode.HalfWidthAlphanumeric],
            DisplayMode.HalfWidthAlphanumeric => [],
            // 英字のまま見えている入力 (hello) は、かな (へっぉ) に変えても「一段戻す」にならない。戻せる段は無い (そのまま全部消す)
            DisplayMode.Auto when CompositionText.IsAlphanumeric(state.Text, env) => [],
            _ => [DisplayMode.Hiragana, DisplayMode.HalfWidthAlphanumeric],
        };
        foreach (var mode in steps)
        {
            SetMode(state, mode);
            state.EscReverted = true;
            state.ModeByKey = true;
            if (CurrentDisplay(state, env, final: false) != before) return true;
        }
        return false;
    }

    /// <summary>
    /// 設定「変換中の F6〜F10 は選んだ文節だけに効かせる」: 選んでいる文節だけを、ひらがな / カタカナ / 半角カタカナ / 全角英数 / 半角英数にする。
    /// 英語の文節は、打った英字 (Reading) を英数に、ローマ字として読んだかな (Kana) をかなにする。
    /// 打った英字や読みが分からない文節 (かな入力、1 音の途中で区切れた文節) では、文全体に効かせずに何もしない。issue #678
    /// </summary>
    private static void ApplyFunctionKeyToClause(CompositionState state, int vk)
    {
        var clause = state.Clauses[state.SelectedClause];
        var reading = clause.IsEnglish ? clause.Kana : clause.Reading;
        var raw = clause.IsEnglish ? clause.Reading : clause.Raw;
        var text = vk switch
        {
            VirtualKeys.F6 => reading,
            VirtualKeys.F7 => reading is null ? null : CompositionText.ToKatakana(reading),
            VirtualKeys.F8 => reading is null ? null : CompositionText.ToHalfWidthKatakana(CompositionText.ToKatakana(reading)),
            VirtualKeys.F9 => raw is null ? null : CompositionText.ToFullWidth(raw),
            _ => raw,
        };
        if (text is null) return;
        var index = clause.Candidates.IndexOf(text);
        if (index < 0)
        {
            clause.Candidates.Add(text);
            index = clause.Candidates.Count - 1;
        }
        clause.Index = index;
    }

    /// <summary>
    /// 設定「Shift + Enter で選んだ文節までを確定」: 選んでいる文節までを確定し、残りの文節は読み (英語の文節は打った英字) を
    /// 未確定の入力として残す (残りは選び直した候補を忘れ、変換前に戻る)。issue #493
    /// </summary>
    private static void CommitUpToSelectedClause(CompositionState state, CompositionEnv env)
    {
        var all = state.Clauses;
        var done = all.Take(state.SelectedClause + 1).ToList();
        var rest = all.Skip(state.SelectedClause + 1).ToList();
        state.Clauses = done;
        LearnClauses(state, env);
        var text = string.Concat(done.Select(c => c.Text));
        CommitText(state, env, text, done.All(c => c.IsEnglish), "", chosen: true);
        BeginComposition(state, env, state.PrivateComposition);
        foreach (var clause in rest)
        {
            if (clause.IsEnglish)
            {
                foreach (var letter in clause.Raw ?? clause.Reading) CompositionText.Append(state.Text, env, letter);
            }
            else
            {
                foreach (var kana in clause.Reading) CompositionText.AppendKana(state.Text, kana, kana);
            }
        }
    }

    /// <summary>無変換でのかなの循環の次の表示。ひらがな・全角カタカナ・半角カタカナで見えているときは輪の次へ進む。自動判定中は、表示中の文字列にひらがな (長音を含む) が含まれていれば全角カタカナにし、含まれていなければひらがなにする。</summary>
    private static DisplayMode NextKanaCycleMode(CompositionState state, CompositionEnv env)
    {
        // 変換中は、まずひらがなに戻す
        if (IsConverting(state)) return DisplayMode.Hiragana;
        switch (state.Text.Mode)
        {
            case DisplayMode.Hiragana: return DisplayMode.Katakana;
            case DisplayMode.Katakana: return DisplayMode.HalfWidthKatakana;
            case DisplayMode.HalfWidthKatakana: return DisplayMode.Hiragana;
            case DisplayMode.Auto:
                // 自動判定中は、表示中の文字列にひらがな (長音を含む) が含まれているかで判定する。含まれていれば全角カタカナにし、含まれていなければひらがなにする。
                var shown = CurrentDisplay(state, env, final: false);
                return shown.Length > 0 && shown.Any(KanaChars.IsHiraganaOrLongMark) ? DisplayMode.Katakana : DisplayMode.Hiragana;
            default: return DisplayMode.Hiragana;
        }
    }

    private static void SetMode(CompositionState state, DisplayMode mode)
    {
        state.Clauses = [];
        state.Text.Mode = mode;
        state.Text.Case = LetterCase.AsTyped;
    }

    /// <summary>
    /// F9 / F10: 英字にする。もう英字で見せているときに押すと、大文字・小文字を 打ったまま → すべて大文字 → 先頭だけ大文字 → … と切り替える
    /// (ai → AI → Ai → ai。Microsoft IME と同じ)。見た目の変わらない段 (打ったままが既にすべて大文字など) は飛ばす。
    /// </summary>
    private static void SetAlphanumericMode(CompositionState state, CompositionEnv env, DisplayMode mode)
    {
        var before = IsConverting(state) ? null : CurrentDisplay(state, env, final: false);
        var sameMode = !IsConverting(state) && state.Text.Mode == mode;
        state.Clauses = [];
        if (!sameMode)
        {
            state.Text.Mode = mode;
            state.Text.Case = LetterCase.AsTyped;
            // 設定「F9 / F10 の 1 回目は打ったまま」: 見えている内容にかかわらず、1 回目は打ったままにする (issue #515)
            if (env.Settings.AlphanumericKeyStartsAsTyped) return;
            // 英単語と判定して英字で見せていた (Auto) ときは、F10 を押した時点で見た目が変わるように次の段へ進める。
            if (CurrentDisplay(state, env, final: false) != before) return;
        }
        for (var i = 0; i < 3; i++)
        {
            state.Text.Case = (LetterCase)(((int)state.Text.Case + 1) % 3);
            if (CurrentDisplay(state, env, final: false) != before) return;
        }
    }

    private static bool IsShift(int vk) => vk is VirtualKeys.Shift or VirtualKeys.LShift or VirtualKeys.RShift;

    private static bool IsControl(int vk) => vk is VirtualKeys.Control or VirtualKeys.LControl or VirtualKeys.RControl;

    private const int ResizeExtend = 0x7FFF0001, ResizeShrink = 0x7FFF0002;

    /// <summary>
    /// 設定「Control キーの割り当て」が macOS 風のときの割り当て。J K L = ひらがな / カタカナ / 全角英字、H D = 1 文字消す、
    /// N P = 次 / 前の候補、B F A E = カーソル移動、O I = 文節を伸ばす / 縮める。
    /// </summary>
    private static int? MacControlShortcut(int vk) => vk switch
    {
        'J' => VirtualKeys.F6,
        'K' => VirtualKeys.F7,
        'L' => VirtualKeys.F9,
        'H' => VirtualKeys.Back,
        'D' => 0x2E,
        'N' => VirtualKeys.Down,
        'P' => VirtualKeys.Up,
        'B' => VirtualKeys.Left,
        'F' => VirtualKeys.Right,
        'A' => 0x24,
        'E' => 0x23,
        'O' => ResizeExtend,
        'I' => ResizeShrink,
        _ => null,
    };

    /// <summary>設定「Control キーの割り当て」(T=F6;Y=F7 …) で、Control + vk に割り当てたキー。無ければ null。</summary>
    private static int? BoundControlKey(CompositionEnv env, int vk)
    {
        var text = env.Settings.ControlKeyBindings;
        if (string.IsNullOrWhiteSpace(text)) return null;
        return ControlKeyBindingParser.Parse(text).TryGetValue(vk, out var key) ? key : null;
    }

    /// <summary>
    /// 入力中に Ctrl と一緒に押すと、変換ボックスで使う英字のキー (設定「Control キーの割り当て」による。HandleKey の Ctrl の処理と同じ順に見る)。
    /// 入力していなければ空。ほかの Ctrl のキーは、確定してアプリへ送る。
    /// </summary>
    public static IReadOnlyList<int> ComposingControlKeys(CompositionState state, CompositionEnv env)
    {
        if (CompositionText.IsEmpty(state.Text)) return [];
        var keys = new HashSet<int>();
        if (IsConverting(state) && env.Settings.ControlKeys == Config.ControlKeyLayout.Atok) keys.UnionWith(['K', 'L']);
        if (!string.IsNullOrWhiteSpace(env.Settings.ControlKeyBindings)) keys.UnionWith(ControlKeyBindingParser.Parse(env.Settings.ControlKeyBindings).Keys);
        for (var vk = 'A'; vk <= 'Z'; vk++)
        {
            if ((env.Settings.ControlKeys == Config.ControlKeyLayout.Mac ? MacControlShortcut(vk) : null) is not null || ControlShortcut(vk) is not null) keys.Add(vk);
        }
        return [.. keys.Order()];
    }

    /// <summary>入力中の Ctrl+英字で、ほかのキーと同じ働きをするもの (U ひらがな / I カタカナ / O 半角英数 / P 全角英数 / H BackSpace)。</summary>
    private static int? ControlShortcut(int vk) => vk switch
    {
        'U' => VirtualKeys.F6,
        'I' => VirtualKeys.F7,
        'O' => VirtualKeys.F10,
        'P' => VirtualKeys.F9,
        // Ctrl+H は BackSpace (Microsoft IME と同じ。ホームポジションのまま消せる: issue #150)
        'H' => VirtualKeys.Back,
        _ => null,
    };

    private static bool IsCommandModifier(int vk) => VirtualKeys.IsModifier(vk) && !IsShift(vk);

    /// <summary>そのキーで入力する文字 (記号・数字を含む)。文字を生まないキーなら null。</summary>
    private static char? CharOf(KeyEvent e) => e.Scan is > 0 and < 0x10000 ? (char)e.Scan : null;

    /// <summary>今のキーをアプリに通す。キャレットが動くかもしれないので、直前の語はもう確定し直さない。</summary>
    private static void PassKey(CompositionState state)
    {
        state.Correctable.Clear();
        CompositionOutput.PassCurrent(state.Out);
    }
}
