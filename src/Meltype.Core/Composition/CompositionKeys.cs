// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;
using Meltype.Libraries.Chars;
using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>入力中・変換中のキー、英数状態の自動判定、かな配列、Ctrl のショートカット。</summary>
internal static class CompositionKeys
{

    /// <summary>キーを 1 つ処理する。アプリに通さないなら true。</summary>
    internal static bool Handle(CompositionState state, CompositionEnv env, KeyPress key)
    {
        (state.Shift, state.Control, state.Modifier) = (key.Shift, key.Control, key.Modifier);
        if (!CompositionLifecycle.IsComposing(state) && !state.KanaLayout.ThumbHeld && (key.Modifier || !StartsComposition(state, env, key))) return false;
        state.Pressed.Add(key.Vk);
        HandleKey(state, env, new KeyEvent(key.Vk, key.Ch ?? 0, false, false, false, key.TimeMs));
        return true;
    }

    /// <summary>変換ボックスを開くキーか。変換キーは選んだ文字の再変換、親指シフトのかな配列の 無変換・変換 は親指キー。</summary>
    private static bool StartsComposition(CompositionState state, CompositionEnv env, KeyPress key)
    {
        var direct = CompositionLifecycle.IsDirect(state);
        if (key.Vk == VirtualKeys.Convert && env.Selection is { Length: > 0 }) return true;
        if (key.Vk is VirtualKeys.Convert or VirtualKeys.NonConvert && !direct && CompositionLifecycle.KanaInput(env) &&
            KanaLayoutEngine.UsesThumbs(env.Settings.KanaLayout)) return true;
        // Space をシフトに使う配列 (薙刀式): 文頭の Space + 文字キーもシフトにする。Space だけを離したら、ふつうの Space
        if (key.Vk == VirtualKeys.Space && !key.Shift && !direct && CompositionLifecycle.KanaInput(env) && KanaLayoutEngine.UsesSpace(env.Settings.KanaLayout)) return true;
        // かな入力: かなのキーならすべて入力を始める (StartWith)
        if (CompositionLifecycle.KanaInput(env) && !direct && Detection.KanaDetector.IsKanaKey(key.Vk)) return true;
        // 日本語入力の Shift+Space・設定「スペースの幅」が全角の Space は、全角スペースにする (StartWith)
        if (key.Vk == VirtualKeys.Space) return key.Shift || !direct && env.Settings.SpaceWidth == Config.SpaceWidth.FullWidth;
        if (key.Ch is not { } c) return false;
        if (VirtualKeys.IsLetter(key.Vk) && char.IsAsciiLetter(c)) return true;
        // 句読点・かぎかっこ・長音・数字・記号 (Shift で打つものも)
        return StartsWithSymbol(c);
    }

    internal static void HandleRelease(CompositionState state, CompositionEnv env, int vk)
    {
        var pressed = state.Pressed.Remove(vk);
        if (!CompositionLifecycle.IsComposing(state) && !state.KanaLayout.ThumbHeld) return;
        var kanaUp = state.KanaLayout.Up(vk);
        // 中指NICOLA: シフトのキーを押したまま先に離した文字キーを、シフト面で打ち直す
        if (kanaUp.Stroke.Handled && state.Scene is CompositionScene.WithInput) ApplyKanaStroke(state, env, kanaUp.Stroke);
        // 親指シフト: 文字に使わずに離した親指キーは、もとの働き (変換・無変換) をする
        if (kanaUp.ThumbTap && pressed)
        {
            HandleKey(state, env with { BypassKanaLayout = true }, new KeyEvent(vk, 0, false, false, false, 0));
        }
    }

    private static void HandleKey(CompositionState state, CompositionEnv env, KeyEvent e)
    {
        var vk = e.Vk;
        if (state.Scene is CompositionScene.Judging judging)
        {
            if (VirtualKeys.IsLetter(vk) && !state.Shift && !state.Modifier)
            {
                CompositionDirectInput.Hold(state, judging, env, e);
                return;
            }
            // 母音の後の - は長音 (ro-maji → ろーまじ、de-ta → でーた)。英単語の途中にはまず出てこないので、ローマ字として
            // 読めれば日本語に戻す。英語の接頭辞 (e-mail、co-op) かもしれないときは - も送って判定を続け、- の後ろで決める
            // (e-mail → 英語、e-me-ru → えーめーる)。
            var part = CompositionDirectInput.LastHyphenPart(judging);
            if (vk == VirtualKeys.OemMinus && !state.Shift && !state.Modifier && part.Length > 0 &&
                env.Detector.Romaji.AnalyzeFragment(part) is { IsValid: true, Partial: "" })
            {
                var prefix = !judging.Letters.ToString().Contains('-') && CompositionDetector.IsHyphenPrefix(part);
                judging.Letters.Append('-');
                judging.LastKeyTime = e.TimeMs;
                if (prefix)
                {
                    CompositionDirectInput.Send(state, judging, e);
                    return;
                }
                Diagnostics.Log.Decision($"英数状態でローマ字を検知: {Diagnostics.Log.Text(judging.Letters.ToString())} (母音の後の長音)");
                CompositionDirectInput.SwitchToJapanese(state, judging, env);
                return;
            }
            // 英字以外のキー・Shift・Ctrl で判定を打ち切り、英語として出してから、そのキーを普通に処理する。
            // 仮想キーコードは打った文字 (数字・記号) を表すので、入力した文字と同じく設定に従う。
            Diagnostics.Log.Info($"英数状態の判定を打ち切り: {(Diagnostics.Log.RecordText ? $"キー 0x{vk:X2}" : "英字以外のキー")} (Shift {state.Shift})");
            CompositionDirectInput.ReleaseAsEnglish(state);
        }

        // 大文字・文節の区切りの変更に使う Shift は、ほかのキーと一緒に届く。Shift だけでは何もしない。
        if (IsShift(vk)) return;
        // 何も打っていないときのテンキーの数字・記号は、変換ボックスに入れず、そのまま通す (半角で入る。Excel・CAD などで数値を打つとき: issue #460)。
        // 入力中なら今までどおり変換ボックスに入れる (3 + ji → 3時 のように続けて変換できる)
        if (state.Scene is not CompositionScene.WithInput && !state.Control && !state.Modifier && vk is >= 0x60 and <= 0x6F)
        {
            PassKey(state);
            return;
        }
        if (state.Control && state.Scene is CompositionScene.WithInput withInput)
        {
            var converting = withInput as CompositionScene.Converting;
            // 変換ボックスに入力中の Ctrl + キー (Ctrl+U/I/O/P はかな・英字の切り替え、Ctrl+H は BackSpace。それ以外は確定してから通す)。
            if (IsControl(vk)) return;
            // 設定「Control キーの割り当て」が ATOK 風: Ctrl + K / L で文節の長さを変える。変換中だけ (Shift + ← → と同じ。issue #599)
            if (converting is not null && env.Settings.ControlKeys == Config.ControlKeyLayout.Atok && vk is 'K' or 'L')
            {
                CompositionConversion.Resize(converting, env, vk == 'L' ? +1 : -1);
                return;
            }
            // 設定「Control キーの割り当て」: 組み込みの割り当てより先に見る (issue #63・#209)
            if (BoundControlKey(env, vk) is { } bound)
            {
                vk = bound;
            }
            // 設定「Control キーの割り当て」が macOS 風: 組み込みの Ctrl+U/I/O/P/H より先に見る (issue #643)
            else if (env.Settings.ControlKeys == Config.ControlKeyLayout.Mac && MacControlShortcut(vk) is { } mac)
            {
                if (mac is ResizeExtend or ResizeShrink)
                {
                    if (converting is not null) CompositionConversion.Resize(converting, env, mac == ResizeExtend ? +1 : -1);
                    return;
                }
                vk = mac;
            }
            else if (ControlShortcut(vk) is { } function)
            {
                // Ctrl+U/I/O/P: F6/F7/F10/F9 と同じ (ATOK と同じ割り当て。続けて押すと大文字・小文字も切り替わる)。Ctrl+H は BackSpace。
                vk = function;
            }
            else if (vk == VirtualKeys.Return)
            {
                // 変換中の Ctrl+Enter (送信のショートカット) は確定だけを行う。キーはアプリへ渡さない。
                // 渡すと、アプリが確定を反映する前に送信してしまい、確定した文字が欠けた内容が送られる
                // (X のリプライで「田所工事」と「田所工事だよ」が二重に投稿された)。日本語 IME の通例どおり、
                // 変換中の Enter は確定、送信は次の Enter にする。
                CompositionCommit.Commit(state, withInput, env);
                return;
            }
            else
            {
                // ほかのショートカット (Ctrl+C・Ctrl+Z …) は、確定してから Ctrl と一緒にアプリへ送る。
                CompositionCommit.Commit(state, withInput, env);
                PassKey(state);
                return;
            }
        }
        else if (state.Modifier || VirtualKeys.IsModifier(vk))
        {
            // Ctrl / Alt / Command のショートカットの前に確定する。
            CompositionCommit.CommitIfAny(state, env);
            PassKey(state);
            return;
        }
        if (!env.BypassKanaLayout && HandleKanaLayout(state, env, e)) return;

        if (state.Scene is CompositionScene.WithInput scene) HandleInputKey(state, scene, env, e, vk);
        else StartWith(state, env, e);
    }

    /// <summary>変換ボックスに入力があるときのキー。vk は、Ctrl のショートカットを読み替えたキー。</summary>
    private static void HandleInputKey(CompositionState state, CompositionScene.WithInput scene, CompositionEnv env, KeyEvent e, int vk)
    {
        var input = scene.Input;
        if (input.Reconversion is not null && vk == VirtualKeys.Escape)
        {
            CompositionLifecycle.Finish(state);
            return;
        }

        if (scene is CompositionScene.Converting converting && HandleConversionKey(state, converting, env, vk)) return;

        switch (vk)
        {
            case VirtualKeys.Convert or VirtualKeys.Down when env.Settings.EnglishFirstInput && input.Text.Mode == DisplayMode.HalfWidthAlphanumeric && !input.ModeByKey && !CompositionLifecycle.IsProtectedInput(scene):
                // 設定「英字を主に入力」: 変換キー / ↓ で、打った英字をローマ字として読んで変換する (issue #839)
                input.Text.Mode = DisplayMode.Hiragana;
                CompositionText.FixTypos(input.Text, env);
                CompositionConversion.StartConversion(state, input, env, preferJapanese: true);
                return;
            case VirtualKeys.Space when env.Settings.EnglishFirstInput && input.Text.Mode == DisplayMode.HalfWidthAlphanumeric && !input.ModeByKey && !CompositionLifecycle.IsProtectedInput(scene) &&
                                        !state.Shift:
                // 設定「英字を主に入力」: Space は変換ではなく、ふつうの空白として入力中の文字に足す (ls -la)
                CompositionText.Append(input.Text, env, ' ');
                return;
            case VirtualKeys.Convert:
                CompositionText.FixTypos(input.Text, env);
                CompositionConversion.StartConversion(state, input, env, preferJapanese: true);
                return;
            case VirtualKeys.Return when CompositionPresentation.SelectedPrediction(input) is { } prediction:
                // 予測変換の候補を選んでいれば、それを確定する。
                CompositionCommit.CommitPrediction(state, input, env, prediction);
                return;
            case VirtualKeys.Return:
                if (CompositionLifecycle.IsProtectedInput(scene)) { CompositionCommit.CommitProtected(state, input, env, ""); return; }
                CompositionText.FixTypos(input.Text, env);
                CompositionCommit.Commit(state, scene, env);
                return;
            case VirtualKeys.Space when state.Shift:
                // Shift+Space: 英語と判定した語でも、ローマ字として読んで変換する (go → 語、camera → かめら)。
                CompositionText.FixTypos(input.Text, env);
                CompositionConversion.StartConversion(state, input, env, ConversionTrigger.Space, preferJapanese: true);
                return;
            case VirtualKeys.Space when env.Settings.SeparateKeyRoles && !CompositionLifecycle.IsProtectedInput(scene):
                // 設定「入力中のキーの役割を分ける」: Space は英語・日本語の判定にかかわらず候補を開く (英字のまま確定して空白を入れない)。
                // 英字で見えている語も、英字を最初の候補に残し、ローマ字として読めれば日本語の候補も選べる (issue #199)
                CompositionText.FixTypos(input.Text, env);
                CompositionConversion.StartConversion(state, input, env, ConversionTrigger.Space);
                return;
            case VirtualKeys.NonConvert when env.Settings.NonConvertCyclesKana && !CompositionLifecycle.IsProtectedInput(scene):
                // ひらがな → 全角カタカナ → 半角カタカナ → ひらがな の順に循環する。自動判定中は、表示中の文字列にひらがな (長音を含む) があれば全角カタカナにし、なければひらがなにする。
                SetMode(state, scene, NextKanaCycleMode(scene, env));
                input.ModeByKey = true;
                return;
            case VirtualKeys.NonConvert when env.Settings.SeparateKeyRoles:
                // 無変換: 今までの入力を英字にする (F10 と同じ。続けて押すと大文字・小文字も切り替わる)
                SetAlphanumericMode(state, scene, env, DisplayMode.HalfWidthAlphanumeric);
                input.ModeByKey = true;
                return;
            case VirtualKeys.Kana when env.Settings.SeparateKeyRoles:
                // ひらがな / カタカナ キー: 押すたびに ひらがな ⇔ カタカナ (漢字・英字の状態からは、まずひらがな)
                SetMode(state, scene, input.Text.Mode == DisplayMode.Hiragana && scene is CompositionScene.Typing ? DisplayMode.Katakana : DisplayMode.Hiragana);
                input.ModeByKey = true;
                return;
            case VirtualKeys.Space:
                // 保護区間 (メンション・URL・パス) の末尾は、自動変換せず原文のまま + 半角空白で確定する。
                if (CompositionLifecycle.IsProtectedInput(scene)) { CompositionCommit.CommitProtected(state, input, env, " "); return; }
                CompositionText.FixTypos(input.Text, env);
                // 英語と判定した語で終わっているなら、変換ではなく確定して空白を入れる
                // (日本語の部分は、ライブ変換が ON なら漢字にして、OFF なら見えているかなのまま確定)。
                // 数字だけ (1、12) は、前が英文なら確定して空白 (I have 2 cats)。それ以外は変換して ① 一 Ⅰ などの候補を出す。
                // 打った英字全体を読みとして登録した語 (ユーザー辞書の zh → ←、略入力の ast → 明日) があれば、英語と判定しても変換する
                if (CompositionConversion.HasRegisteredReading(input, env))
                {
                    CompositionConversion.StartConversion(state, input, env, ConversionTrigger.Space);
                }
                else if (SpaceCommitsMarkdownMarker(input))
                {
                    // Markdown の記号だけは、変換せず打った半角 + 空白で確定する
                    CompositionCommit.CommitText(state, env, ComposedText.English(CompositionText.Raw(input.Text) + " "), english: false, preserveText: true, rule: "Space: Markdown の記号なので打ったまま");
                }
                else if (SpaceCommitsAlphanumeric(input, env))
                {
                    CompositionCommit.Commit(state, scene, env, suffix: " ");
                }
                else if (CompositionCommit.EndsWithEnglish(input, env)) CompositionCommit.CommitText(state, env, CompositionPresentation.CommittedDisplay(input, env) + ComposedText.English(" "), english: true, CompositionText.Raw(input.Text), rule: "Space: 英語で終わるので変換せずに確定");
                else if (SpaceCommitsAutoEnglish(input, env)) CompositionCommit.CommitText(state, env, ComposedText.English(CompositionText.Raw(input.Text) + " "), english: true, CompositionText.Raw(input.Text), rule: "Space: かなにならない子音で終わる英単語");
                else
                {
                    CompositionConversion.StartConversion(state, input, env, ConversionTrigger.Space);
                }
                return;
            case VirtualKeys.Back:
                CompositionText.RemoveLast(input.Text, env);
                CompositionLifecycle.FinishIfEmpty(state, input);
                return;
            case 0x2E /* Delete */ when CompositionPresentation.SelectedPrediction(input) is { } prediction && env.Services.Predictor?.Phrases is { } phrases:
                // 予測の候補を選んでいる間の Delete: その語句を学習から消す (issue #349)
                phrases.Forget(prediction.Text);
                input.Predictions = null;
                return;
            case VirtualKeys.Escape when input.Predictions is { Index: >= 0 } selecting:
                // 予測の候補を選んでいたら、選ぶのをやめるだけ (打った内容は残す)。
                selecting.Index = -1;
                return;
            case VirtualKeys.Escape when env.Settings.EscStepsBack && StepBack(state, scene, env):
                return;
            case VirtualKeys.Escape:
                CompositionLifecycle.Finish(state);
                return;
            case VirtualKeys.Tab when CompositionSpelling.FindMisspelling(input, env) is { } typo:
                // もしかして: 書き間違いを直す。
                CompositionSpelling.FixMisspelling(input, typo);
                return;
            case VirtualKeys.Tab when CompositionText.Suggestion(input.Text, env) is not null:
                // 自動判定を使わないとき: 提案どおり英字にする。
                input.Text.SuggestionAccepted = true;
                return;
            case VirtualKeys.Tab when CompositionPresentation.CurrentPredictions(input, env).Count > 0:
                // 予測変換の候補を選ぶ (Tab で次、Shift+Tab で前。最後の次は選ばない状態に戻る)。
                var step = state.Shift ? -1 : 1;
                var list = input.Predictions!;
                list.Index = (list.Index + 1 + step + list.Items.Count + 1) % (list.Items.Count + 1) - 1;
                return;
            case VirtualKeys.Tab when scene is CompositionScene.Typing && !state.Shift && env.Settings.TabConversion && TabStartsConversion(input, env):
                // Space なら変換を始める場面の Tab は、Space と同じく変換を始める (Microsoft IME と同じ。空白を打とうとした意味ではない: issue #219)。
                CompositionConversion.StartConversion(state, input, env, ConversionTrigger.Tab);
                return;
            case VirtualKeys.F6: SetMode(state, scene, DisplayMode.Hiragana); input.ModeByKey = true; return;
            case VirtualKeys.F7: SetMode(state, scene, DisplayMode.Katakana); input.ModeByKey = true; return;
            case VirtualKeys.F8: SetMode(state, scene, DisplayMode.HalfWidthKatakana); input.ModeByKey = true; return;
            case VirtualKeys.F9: SetAlphanumericMode(state, scene, env, DisplayMode.FullWidthAlphanumeric); input.ModeByKey = true; return;
            case VirtualKeys.F10: SetAlphanumericMode(state, scene, env, DisplayMode.HalfWidthAlphanumeric); input.ModeByKey = true; return;
            case VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down when !CompositionText.IsAlphanumeric(input.Text, env):
                // 変換前でも矢印キーで文節の選択に入る (Mac のライブ変換と同じ)。
                EnterClauseSelection(state, input, env, vk);
                return;
        }

        if (VirtualKeys.IsHankakuZenkaku(vk))
        {
            // 確定せずに日本語／英字へ切り替える
            SetMode(state, scene, CompositionText.IsAlphanumeric(input.Text, env) ? DisplayMode.Hiragana : DisplayMode.HalfWidthAlphanumeric);
            return;
        }

        if (input.Text.KanaInput && KanaOf(state, e) is { } key)
        {
            CompositionText.AppendKana(CommitBeforeTyping(state, scene, env).Text, key.Raw, key.Kana);
            return;
        }

        if (CharOf(e) is { } ch && !char.IsControl(ch) && ch != ' ')
        {
            // 打ち始めの ・ を Space で / にしてから打った名前 (/help) は、/ を確定して半角のままアプリへ渡す。
            if (scene is CompositionScene.Converting { Conversion.Clauses: [{ Text: "/" }] } && CompositionText.IsSlashAtStart(input.Text) && SigilWord.IsNameChar(ch))
            {
                CompositionCommit.Commit(state, scene, env);
                PassKey(state);
                return;
            }
            CompositionText.Append(CommitBeforeTyping(state, scene, env).Text, env, ch);
            return;
        }

        // 矢印 (英字だけのとき)・Tab・Delete などは確定してから通す。
        CompositionCommit.Commit(state, scene, env);
        PassKey(state);
    }

    /// <summary>変換ボックスを開く記号・数字。</summary>
    internal static bool StartsWithSymbol(char c) => c is >= '!' and <= '~' && !char.IsAsciiLetter(c);

    /// <summary>変換ボックスが空のときの最初の打鍵。英字・句読点なら入力を始め、それ以外はそのまま通す。</summary>
    private static void StartWith(CompositionState state, CompositionEnv env, KeyEvent e)
    {
        var direct = CompositionLifecycle.IsDirect(state);
        if (e.Vk == VirtualKeys.Convert)
        {
            if (CompositionConversion.ReconversionSelectionNow(env) is not { } selection || string.IsNullOrWhiteSpace(selection.Reading)) return;
            state.Correctable.Clear();
            // ローマ字のまま入った文は、日本語にする所が無ければ何もしない (英字をローマ字として読んだ候補を出さない)
            if (CompositionConversion.IsRomajiText(selection.Text))
            {
                CompositionConversion.StartRomajiReconversion(state, env, selection);
                return;
            }
            var input = CompositionLifecycle.Begin(state, env, reconversion: selection);
            foreach (var kana in selection.Reading) CompositionText.AppendKana(input.Text, kana, kana);
            input.Text.Mode = DisplayMode.Hiragana;
            CompositionConversion.StartConversion(state, input, env, preferJapanese: true);
            return;
        }
        // 日本語入力のときの Shift+Space は全角スペース (Microsoft IME と同じ: issue #24)。英数状態では普通の空白のまま。
        // 設定「スペースの幅」が全角では、Space が全角で Shift+Space が半角 (issue #259)
        var spaceWidth = env.Settings.SpaceWidth;
        // Space をシフトに使うかな配列では、文頭で Space だけを離したときも空白を入れる。
        if (e.Vk == VirtualKeys.Space && !direct &&
            (spaceWidth == Config.SpaceWidth.FullWidth || state.Shift || CompositionLifecycle.KanaInput(env) && KanaLayoutEngine.UsesSpace(env.Settings.KanaLayout)))
        {
            // 「スペースの幅」が半角なら Shift+Space も半角 (issue #708)
            var shifted = state.Shift;
            var halfWidth = spaceWidth switch
            {
                Config.SpaceWidth.FullWidth => shifted,
                Config.SpaceWidth.HalfWidth => true,
                Config.SpaceWidth.Standard => !shifted,
                _ => throw new InvalidOperationException($"不明な SpaceWidth: {spaceWidth}"),
            };
            CompositionCommit.CommitText(state, env, ComposedText.Of(halfWidth ? SpanKind.English : SpanKind.Japanese, halfWidth ? " " : "　"), english: false);
            return;
        }
        // かな入力: かなのキーならすべて入力を始める (英数状態でなければ)。
        if (CompositionLifecycle.KanaInput(env) && !direct && KanaOf(state, e) is { } key)
        {
            CompositionText.AppendKana(CompositionLifecycle.Begin(state, env).Text, key.Raw, key.Kana);
            return;
        }
        var c = CharOf(e);
        var letter = VirtualKeys.IsLetter(e.Vk) && c is { } l && char.IsAsciiLetter(l);
        if (letter && direct)
        {
            // 英数状態: ローマ字かどうか判定する (打鍵はすぐ送る)。大文字で始まる語は英語なのでそのまま通す。
            if (env.ClassifyDirect is null || state.Shift || char.IsAsciiLetterUpper(c!.Value))
            {
                Diagnostics.Log.Info($"英数状態: {Diagnostics.Log.Text(c.ToString()!)}は大文字 / Shift なので英語のまま");
                PassKey(state);
                CompositionLifecycle.StartEnglishWord(state);
            }
            else CompositionDirectInput.Hold(state, CompositionLifecycle.StartJudging(state), env, e);
            return;
        }
        // 句読点・かぎかっこ・長音・中黒・数字でも入力を始める (、。「」ー・)。英数状態ではそのまま通す。
        if (letter || (c is { } symbol && StartsWithSymbol(symbol) && !direct))
        {
            CompositionText.Append(CompositionLifecycle.Begin(state, env).Text, env, c!.Value);
            return;
        }
        PassKey(state);
    }

    /// <summary>
    /// 変換中に次の文字を打ったとき。普通は今の候補で確定して新しい入力を始める (Microsoft IME と同じ)。
    /// 設定「変換したあとに続けて打っても確定しない」が ON なら、変換をやめて打った文字を続けて入れる。選んでいた候補は覚えておき、
    /// もう一度変換したときに同じ読みの文節で使う (issue #123)。
    /// </summary>
    private static CompositionInput CommitOrKeepConversion(CompositionState state, CompositionScene.Converting scene, CompositionEnv env)
    {
        var input = scene.Input;
        if (!env.Settings.ContinueAfterConversion || input.Reconversion is not null)
        {
            CompositionCommit.Commit(state, scene, env);
            return CompositionLifecycle.Begin(state, env);
        }
        input.ModeByKey = false;
        input.EscReverted = false;
        foreach (var clause in scene.Conversion.Clauses.Where(c => !c.IsEnglish)) input.KeptChoices[clause.Reading] = clause.Text;
        CompositionLifecycle.StopConverting(state, input);
        return input;
    }

    /// <summary>
    /// 入力中に次の文字を打ったとき。変換中なら、今の候補で確定して新しい入力を始める (IME と同じ。設定で、確定せずに続けることもできる)。
    /// F6〜F10 でかな・英字にしたあとなら、見えているとおりに確定して新しい入力を始める (続けて打った文字までカタカナ・大文字にしない)。
    /// 打った文字を入れる入力を返す。
    /// </summary>
    private static CompositionInput CommitBeforeTyping(CompositionState state, CompositionScene.WithInput scene, CompositionEnv env)
    {
        var input = scene.Input;
        if (scene is CompositionScene.Converting converting) return CommitOrKeepConversion(state, converting, env);
        if (input.ModeByKey)
        {
            CompositionCommit.Commit(state, scene, env);
            return CompositionLifecycle.Begin(state, env);
        }
        if (CompositionPresentation.SelectedPrediction(input) is { } prediction)
        {
            // Tab で予測の候補を選んだあとに続けて打ったら、その候補で確定して新しい入力を始める (選ぶ前の打ちかけに戻さない)
            CompositionCommit.CommitPrediction(state, input, env, prediction);
            return CompositionLifecycle.Begin(state, env);
        }
        return input;
    }

    /// <summary>
    /// JIS かな以外のかな配列 (月・花・NICOLA・中指NICOLA・TRON) と SKY 配列の打鍵 (issue #1415)。配列のキーとして扱ったら true。
    /// 打ったキーの英字は英単語の判定に使わない (刻印と違う配列なので)。かなをそのまま原文にする。
    /// </summary>
    private static bool HandleKanaLayout(CompositionState state, CompositionEnv env, KeyEvent e)
    {
        var direct = CompositionLifecycle.IsDirect(state);
        if (!CompositionLifecycle.KanaInput(env) && !direct && env.Settings.RomajiLayout == RomajiLayout.Sky)
        {
            // SKY 配列: キーをローマ字の綴りにして足す。英語の判定はしない (ひらがなで見せる)
            if (state.Shift || SkyLayout.Romaji(e.Vk) is not { } romaji) return false;
            var input = state.Scene is CompositionScene.WithInput scene ? CommitBeforeTyping(state, scene, env) : CompositionLifecycle.Begin(state, env);
            if (input.Text.Mode == DisplayMode.Auto) input.Text.Mode = DisplayMode.Hiragana;
            foreach (var c in romaji) CompositionText.Append(input.Text, env, c);
            return true;
        }
        var layout = CompositionLifecycle.KanaInput(env) && !direct ? env.Settings.KanaLayout : KanaLayout.Jis;
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
        var input = state.Scene is not CompositionScene.WithInput scene ? CompositionLifecycle.Begin(state, env)
            : stroke.RemoveLast == 0 ? CommitBeforeTyping(state, scene, env) : scene.Input;
        for (var i = 0; i < stroke.RemoveLast && !CompositionText.IsEmpty(input.Text); i++) CompositionText.RemoveLast(input.Text, env);
        foreach (var kana in stroke.Kana) CompositionText.AppendKana(input.Text, kana, kana);
        CompositionLifecycle.FinishIfEmpty(state, input);
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

    /// <summary>
    /// Space を押したときに、変換ではなく確定して空白を入れる場面か (英語で終わる 3 条件)。
    /// 例: hello (英語と判定) → true、kana → false。Tab で変換を始めるかの判定にも使う。
    /// </summary>
    private static bool SpaceCommitsAsEnglish(CompositionInput input, CompositionEnv env) => SpaceCommitsAlphanumeric(input, env) || CompositionCommit.EndsWithEnglish(input, env) || SpaceCommitsAutoEnglish(input, env);

    private static bool SpaceCommitsMarkdownMarker(CompositionInput input) => input.Text.Mode == DisplayMode.Auto && CompositionText.IsMarkdownMarker(input.Text);

    private static bool SpaceCommitsAlphanumeric(CompositionInput input, CompositionEnv env) =>
        CompositionText.IsAlphanumericAt(input.Text, env, final: true) && !(input.Text.Mode == DisplayMode.Auto && CompositionText.IsNumeric(input.Text) && CompositionText.Raw(input.Text).All(char.IsAsciiDigit) && env.Context().PrecedingEnglish != true);

    private static bool SpaceCommitsAutoEnglish(CompositionInput input, CompositionEnv env) => input.Text.Mode == DisplayMode.Auto && env.Detector.IsEnglishAtWordEnd(CompositionText.Raw(input.Text), !env.Settings.AutoDetection);

    /// <summary>
    /// Space の分岐で変換を始める (StartConversion に行く) 場面か。Tab で始めるときも同じ場面に限る (issue #219)。
    /// Space と同じ順で判定するため、英語かを見る前に打ち間違いを直す (Enter で確定するときと同じ直し)。
    /// </summary>
    private static bool TabStartsConversion(CompositionInput input, CompositionEnv env)
    {
        // 保護区間が語の途中にあり、その後にかなが続く入力も Tab 変換しない。
        // IsProtectedInput は末尾が保護区間かだけを見るため、ここでは全区間を調べる。
        if (input.Text.Mode == DisplayMode.Auto && CompositionText.ProtectedSpans(input.Text).Count > 0) return false;
        CompositionText.FixTypos(input.Text, env);
        return CompositionConversion.HasRegisteredReading(input, env) || !SpaceCommitsAsEnglish(input, env);
    }

    /// <summary>変換中だけ意味を持つキー。処理したら true。</summary>
    private static bool HandleConversionKey(CompositionState state, CompositionScene.Converting scene, CompositionEnv env, int vk)
    {
        var (input, conversion) = (scene.Input, scene.Conversion);
        var shift = state.Shift;
        switch (vk)
        {
            case VirtualKeys.Space when shift:
                // Shift+Space: 前の候補へ (Microsoft IME と同じ。Mac でも Shift を押したまま Space で戻れるように: issue #140)
                CompositionConversion.NextCandidate(conversion, env, -1);
                return true;
            case VirtualKeys.Convert:
            case VirtualKeys.Space:
            case VirtualKeys.Down:
                CompositionConversion.NextCandidate(conversion, env, +1);
                return true;
            case VirtualKeys.Up:
                CompositionConversion.NextCandidate(conversion, env, -1);
                return true;
            case VirtualKeys.PageDown:
                // 候補の一覧のページ送り (Microsoft IME と同じ。アプリに通すと確定してしまっていた: issue #1016)
                CompositionConversion.NextCandidatePage(conversion, env, +1);
                return true;
            case VirtualKeys.PageUp:
                CompositionConversion.NextCandidatePage(conversion, env, -1);
                return true;
            case VirtualKeys.Right when shift:
                CompositionConversion.Resize(scene, env, +1);
                return true;
            case VirtualKeys.Left when shift:
                CompositionConversion.Resize(scene, env, -1);
                return true;
            case VirtualKeys.Right:
                CompositionConversion.SplitWholeSentence(conversion);
                conversion.Selected = Math.Min(conversion.Selected + 1, conversion.Clauses.Count - 1);
                return true;
            case VirtualKeys.Left:
                CompositionConversion.SplitWholeSentence(conversion);
                conversion.Selected = Math.Max(conversion.Selected - 1, 0);
                return true;
            case VirtualKeys.Return when shift && env.Settings.PartialCommit && conversion.Selected < conversion.Clauses.Count - 1:
                CommitUpToSelectedClause(state, scene, env);
                return true;
            case VirtualKeys.Return:
                CompositionCommit.Commit(state, scene, env);
                return true;
            case VirtualKeys.F6 or VirtualKeys.F7 or VirtualKeys.F8 or VirtualKeys.F9 or VirtualKeys.F10
                when env.Settings.FunctionKeysAffectSelectedClause && conversion.Clauses.Count > 1:
                ApplyFunctionKeyToClause(conversion.SelectedClause, vk);
                return true;
            case VirtualKeys.Tab when CompositionSpelling.FindMisspelling(input, env) is { } typo:
                // もしかして: 書き間違いを直して変換し直す。
                CompositionSpelling.FixMisspelling(input, typo);
                CompositionConversion.StartConversion(state, input, env);
                return true;
            case VirtualKeys.Tab when conversion.StartedBy == ConversionTrigger.Tab:
                // Tab で始めた変換の中では、Tab が次の候補、Shift+Tab が前の候補 (issue #219)。
                CompositionConversion.NextCandidate(conversion, env, shift ? -1 : +1);
                return true;
            case VirtualKeys.Escape when env.Settings.EscRevertsSelectedClause && conversion.Clauses.Count > 1 &&
                                         conversion.SelectedClause is { IsEnglish: false } clause && clause.Text != clause.Reading:
                // 設定「変換中の Esc は選んだ文節だけをかなに戻す」: 選んでいる文節だけをひらがなにして、変換は続ける (issue #670)
                {
                    var index = clause.Candidates.IndexOf(clause.Reading);
                    if (index < 0)
                    {
                        clause.Candidates.Add(clause.Reading);
                        index = clause.Candidates.Count - 1;
                    }
                    clause.Index = index;
                    clause.Changed = true;
                }
                return true;
            case VirtualKeys.Back:
            case VirtualKeys.Escape:
                // 変換を取り消して、かなの入力に戻る。
                CompositionLifecycle.StopConverting(state, input);
                return true;
            case >= 0x31 and <= 0x39 when !shift && !input.Text.KanaInput && env.Settings.NumberKeysSelectCandidates:
            case >= 0x61 and <= 0x69 when !shift && env.Settings.NumberKeysSelectCandidates:
                // 候補の一覧の番号 (1〜9) で選ぶ。番号の無い候補 (一覧より後ろ) なら、普通に打った数字として扱う。
                // かな入力では数字の段のキーは ぬふあうえおやゆよ なので、テンキーだけで選ぶ。
                // 設定「変換中の数字キーで候補を選ぶ」が OFF なら、上の段もテンキーも普通に打った数字 (今の候補で確定して入力する: issue #989)。
                return SelectByNumber(state, scene, env, vk >= 0x61 ? vk - 0x61 : vk - 0x31);
            default:
                return false;
        }
    }

    /// <summary>
    /// 数字キーで、今見えている候補の一覧のページから選ぶ (2 → そのページの 2 番目)。選んだら次の文節へ進み、
    /// 最後の文節なら確定する (Microsoft IME と同じく、番号で選んだ候補はそのまま使う)。
    /// </summary>
    private static bool SelectByNumber(CompositionState state, CompositionScene.Converting scene, CompositionEnv env, int row)
    {
        var conversion = scene.Conversion;
        var clause = conversion.SelectedClause;
        if (!clause.Expanded) CompositionConversion.Expand(env, clause);
        var index = Math.Max(0, clause.Index) / CompositionConversion.CandidatePageSize * CompositionConversion.CandidatePageSize + row;
        if (index >= clause.Candidates.Count) return false;
        clause.Index = index;
        clause.Changed = true;
        if (conversion.Selected + 1 < conversion.Clauses.Count) conversion.Selected++;
        else CompositionCommit.Commit(state, scene, env);
        return true;
    }

    /// <summary>変換前に矢印キーを押したとき: 文節に区切って、← なら最後の文節、→ なら最初の文節、↑ ↓ なら変換を始めたときの文節を選ぶ。</summary>
    private static void EnterClauseSelection(CompositionState state, CompositionInput input, CompositionEnv env, int vk)
    {
        if (CompositionConversion.StartConversion(state, input, env) is not { Conversion: var conversion } scene) return;
        CompositionConversion.SplitWholeSentence(conversion);
        var shift = state.Shift;
        conversion.Selected = vk switch
        {
            VirtualKeys.Left => conversion.Clauses.Count - 1,
            VirtualKeys.Right => 0,
            _ => CompositionConversion.InitialClause(env, conversion.Clauses.Count),
        };
        if (shift && vk is VirtualKeys.Left or VirtualKeys.Right) CompositionConversion.Resize(scene, env, vk == VirtualKeys.Left ? -1 : +1);
        else if (vk == VirtualKeys.Up) CompositionConversion.NextCandidate(conversion, env, -1);
    }

    /// <summary>
    /// 設定「Esc で一段ずつ戻す」: 見えている内容を 漢字 → ひらがな → 打ったローマ字 の順に一段戻す。見た目が変わらない段は飛ばす。
    /// 戻せる段が無ければ false (そのまま全部消す)。戻した語は日本語 / 英語の判定として覚えない (issue #676)。
    /// </summary>
    private static bool StepBack(CompositionState state, CompositionScene.WithInput scene, CompositionEnv env)
    {
        var input = scene.Input;
        var before = CompositionPresentation.CurrentDisplay(input, env, final: false);
        // 今のモードより先の段だけ (ひらがなの次はローマ字。カタカナなどはひらがなへ)
        DisplayMode[] steps = input.Text.Mode switch
        {
            DisplayMode.Hiragana => [DisplayMode.HalfWidthAlphanumeric],
            DisplayMode.HalfWidthAlphanumeric => [],
            // 英字のまま見えている入力 (hello) は、かな (へっぉ) に変えても「一段戻す」にならない。戻せる段は無い (そのまま全部消す)
            DisplayMode.Auto when CompositionText.IsAlphanumeric(input.Text, env) => [],
            _ => [DisplayMode.Hiragana, DisplayMode.HalfWidthAlphanumeric],
        };
        foreach (var mode in steps)
        {
            SetMode(state, scene, mode);
            input.EscReverted = true;
            input.ModeByKey = true;
            if (CompositionPresentation.CurrentDisplay(input, env, final: false) != before) return true;
        }
        return false;
    }

    /// <summary>
    /// 設定「変換中の F6〜F10 は選んだ文節だけに効かせる」: 選んでいる文節だけを、ひらがな / カタカナ / 半角カタカナ / 全角英数 / 半角英数にする。
    /// 英語の文節は、打った英字 (Reading) を英数に、ローマ字として読んだかな (Kana) をかなにする。
    /// 打った英字や読みが分からない文節 (かな入力、1 音の途中で区切れた文節) では、文全体に効かせずに何もしない。issue #678
    /// </summary>
    private static void ApplyFunctionKeyToClause(Clause clause, int vk)
    {
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
        // 候補から選んだのと同じく、確定したら学習する
        clause.Changed = true;
    }

    /// <summary>
    /// 設定「Shift + Enter で選んだ文節までを確定」: 選んでいる文節までを確定し、残りの文節は読み (英語の文節は打った英字) を
    /// 未確定の入力として残す (残りは選び直した候補を忘れ、変換前に戻る)。issue #493
    /// </summary>
    private static void CommitUpToSelectedClause(CompositionState state, CompositionScene.Converting scene, CompositionEnv env)
    {
        var conversion = scene.Conversion;
        var all = conversion.Clauses;
        var done = all.Take(conversion.Selected + 1).ToList();
        var rest = all.Skip(conversion.Selected + 1).ToList();
        conversion.Clauses = done;
        CompositionCommit.LearnClauses(state, scene.Input, done, env);
        CompositionCommit.CommitText(state, env, CompositionPresentation.Composed(done), done.All(c => c.IsEnglish), "", chosen: true);
        var input = CompositionLifecycle.Begin(state, env, scene.Input.Private, scene.Input.Reconversion);
        foreach (var clause in rest)
        {
            if (clause.IsEnglish)
            {
                foreach (var letter in clause.Raw ?? clause.Reading) CompositionText.Append(input.Text, env, letter);
            }
            else
            {
                foreach (var kana in clause.Reading) CompositionText.AppendKana(input.Text, kana, kana);
            }
        }
    }

    /// <summary>無変換でのかなの循環の次の表示。ひらがな・全角カタカナ・半角カタカナで見えているときは輪の次へ進む。自動判定中は、表示中の文字列にひらがな (長音を含む) が含まれていれば全角カタカナにし、含まれていなければひらがなにする。</summary>
    private static DisplayMode NextKanaCycleMode(CompositionScene.WithInput scene, CompositionEnv env)
    {
        // 変換中は、まずひらがなに戻す
        if (scene is CompositionScene.Converting) return DisplayMode.Hiragana;
        var input = scene.Input;
        switch (input.Text.Mode)
        {
            case DisplayMode.Hiragana: return DisplayMode.Katakana;
            case DisplayMode.Katakana: return DisplayMode.HalfWidthKatakana;
            case DisplayMode.HalfWidthKatakana: return DisplayMode.Hiragana;
            case DisplayMode.Auto:
                // 自動判定中は、表示中の文字列にひらがな (長音を含む) が含まれているかで判定する。含まれていれば全角カタカナにし、含まれていなければひらがなにする。
                var shown = CompositionPresentation.CurrentDisplay(input, env, final: false);
                return shown.Length > 0 && shown.Any(KanaChars.IsHiraganaOrLongMark) ? DisplayMode.Katakana : DisplayMode.Hiragana;
            default: return DisplayMode.Hiragana;
        }
    }

    private static void SetMode(CompositionState state, CompositionScene.WithInput scene, DisplayMode mode)
    {
        LeaveConversion(state, scene);
        scene.Input.Text.Mode = mode;
        scene.Input.Text.Case = LetterCase.AsTyped;
    }

    // 変換を抜けて表示の形を選ぶ。抜けた変換は確定の記録のために残す
    private static void LeaveConversion(CompositionState state, CompositionScene.WithInput scene)
    {
        if (scene is not CompositionScene.Converting { Input: var input, Conversion: var conversion }) return;
        input.LeftConversion = (conversion, CompositionText.Raw(input.Text));
        CompositionLifecycle.StopConverting(state, input);
    }

    /// <summary>
    /// F9 / F10: 英字にする。もう英字で見せているときに押すと、大文字・小文字を 打ったまま → すべて大文字 → 先頭だけ大文字 → … と切り替える
    /// (ai → AI → Ai → ai。Microsoft IME と同じ)。見た目の変わらない段 (打ったままが既にすべて大文字など) は飛ばす。
    /// </summary>
    private static void SetAlphanumericMode(CompositionState state, CompositionScene.WithInput scene, CompositionEnv env, DisplayMode mode)
    {
        var input = scene.Input;
        var before = scene is CompositionScene.Converting ? null : CompositionPresentation.CurrentDisplay(input, env, final: false);
        var sameMode = scene is CompositionScene.Typing && input.Text.Mode == mode;
        LeaveConversion(state, scene);
        if (!sameMode)
        {
            input.Text.Mode = mode;
            input.Text.Case = LetterCase.AsTyped;
            // 設定「F9 / F10 の 1 回目は打ったまま」: 見えている内容にかかわらず、1 回目は打ったままにする (issue #515)
            if (env.Settings.AlphanumericKeyStartsAsTyped) return;
            // 英単語と判定して英字で見せていた (Auto) ときは、F10 を押した時点で見た目が変わるように次の段へ進める。
            if (CompositionPresentation.CurrentDisplay(input, env, final: false) != before) return;
        }
        for (var i = 0; i < 3; i++)
        {
            input.Text.Case = (LetterCase)(((int)input.Text.Case + 1) % 3);
            if (CompositionPresentation.CurrentDisplay(input, env, final: false) != before) return;
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
    internal static IReadOnlyList<int> ComposingControlKeys(CompositionState state, CompositionEnv env)
    {
        if (state.Scene is not CompositionScene.WithInput) return [];
        var keys = new HashSet<int>();
        if (CompositionLifecycle.IsConverting(state) && env.Settings.ControlKeys == Config.ControlKeyLayout.Atok) keys.UnionWith(['K', 'L']);
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
