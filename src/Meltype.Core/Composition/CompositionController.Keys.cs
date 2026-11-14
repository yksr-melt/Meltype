// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>キーの処理: 入力中・変換中のキー、英数状態で判定中の語、かな配列、Ctrl のショートカット。</summary>
public sealed partial class CompositionController
{
    /// <summary>未確定文字を確定せず日本語／英字へ切り替える。未確定入力がなければ false。</summary>
    private bool TogglePendingInput()
    {
        // 英数状態の自動判定中の文字はすでにアプリへ渡しているので、未確定文字には含めない。
        if (_text.IsEmpty)
        {
            AbandonDirectHold();
            return false;
        }
        SetMode(_text.IsAlphanumeric ? DisplayMode.Hiragana : DisplayMode.HalfWidthAlphanumeric);
        UpdateView();
        return true;
    }

    private void HandleKey(KeyEvent e)
    {
        var vk = e.Vk;
        if (_heldLetters.Length > 0)
        {
            if (VirtualKeys.IsLetter(vk) && !_shift && !_modifier)
            {
                Hold(e);
                return;
            }
            // 母音の後の - は長音 (ro-maji → ろーまじ、de-ta → でーた)。英単語の途中にはまず出てこないので、ローマ字として
            // 読めれば日本語に戻す。英語の接頭辞 (e-mail、co-op) かもしれないときは - も送って判定を続け、- の後ろで決める
            // (e-mail → 英語、e-me-ru → えーめーる)。
            var part = LastHyphenPart();
            if (vk == VirtualKeys.OemMinus && !_shift && !_modifier && part.Length > 0 &&
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
            // 英字以外のキー・Shift・Ctrl で判定を打ち切り、英語として出してから、そのキーを普通に処理する。
            Diagnostics.Log.Info($"英数状態の判定を打ち切り: キー 0x{vk:X2} (Shift {_shift})");
            ReleaseHeldAsEnglish();
        }

        // 大文字・文節の区切りの変更に使う Shift は、ほかのキーと一緒に届く。Shift だけでは何もしない。
        if (IsShift(vk)) return;
        // 何も打っていないときのテンキーの数字・記号は、変換ボックスに入れず、そのまま通す (半角で入る。Excel・CAD などで数値を打つとき: issue #460)。
        // 入力中なら今までどおり変換ボックスに入れる (3 + ji → 3時 のように続けて変換できる)
        if (_text.IsEmpty && !_control && !_modifier && vk is >= 0x60 and <= 0x6F)
        {
            PassKey();
            return;
        }
        if (_control && !_text.IsEmpty)
        {
            // 変換ボックスに入力中の Ctrl + キー (Ctrl+U/I/O/P はかな・英字の切り替え、Ctrl+H は BackSpace。それ以外は確定してから通す)。
            if (IsControl(vk)) return;
            // 設定「Control キーの割り当て」が ATOK 風: Ctrl + K / L で文節の長さを変える。変換中だけ (Shift + ← → と同じ。issue #599)
            if (_converting && _settings().ControlKeys == Config.ControlKeyLayout.Atok && vk is 'K' or 'L')
            {
                Resize(vk == 'L' ? +1 : -1);
                return;
            }
            // 設定「Control キーの割り当て」: 組み込みの割り当てより先に見る (issue #63・#209)
            if (!_text.IsEmpty && BoundControlKey(vk) is { } bound)
            {
                vk = bound;
            }
            // 設定「Control キーの割り当て」が macOS 風: 組み込みの Ctrl+U/I/O/P/H より先に見る (issue #643)
            else if (!_text.IsEmpty && _settings().ControlKeys == Config.ControlKeyLayout.Mac && MacControlShortcut(vk) is { } mac)
            {
                if (mac is ResizeExtend or ResizeShrink)
                {
                    if (_converting) Resize(mac == ResizeExtend ? +1 : -1);
                    return;
                }
                vk = mac;
            }
            else if (!_text.IsEmpty && ControlShortcut(vk) is { } function)
            {
                // Ctrl+U/I/O/P: F6/F7/F10/F9 と同じ (ATOK と同じ割り当て。続けて押すと大文字・小文字も切り替わる)。Ctrl+H は BackSpace。
                vk = function;
            }
            else if (!_text.IsEmpty && vk == VirtualKeys.Return)
            {
                // 変換中の Ctrl+Enter (送信のショートカット) は確定だけを行う。キーはアプリへ渡さない。
                // 渡すと、アプリが確定を反映する前に送信してしまい、確定した文字が欠けた内容が送られる
                // (X のリプライで「田所工事」と「田所工事だよ」が二重に投稿された)。日本語 IME の通例どおり、
                // 変換中の Enter は確定、送信は次の Enter にする。
                CommitIfAny();
                return;
            }
            else
            {
                // ほかのショートカット (Ctrl+C・Ctrl+Z …) は、確定してから Ctrl と一緒にアプリへ送る。
                CommitIfAny();
                PassKey();
                return;
            }
        }
        else if (_modifier || VirtualKeys.IsModifier(vk))
        {
            // Ctrl / Alt / Command のショートカットの前に確定する。
            CommitIfAny();
            PassKey();
            return;
        }
        if (!_bypassKanaLayout && HandleKanaLayout(e)) return;

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
            case VirtualKeys.Convert or VirtualKeys.Down when _settings().EnglishFirstInput && _text.Mode == DisplayMode.HalfWidthAlphanumeric && !_modeByKey && !IsProtectedInput:
                // 設定「英字を主に入力」: 変換キー / ↓ で、打った英字をローマ字として読んで変換する (issue #839)
                _text.Mode = DisplayMode.Hiragana;
                _text.FixTypos();
                StartConversion(preferJapanese: true);
                return;
            case VirtualKeys.Space when _settings().EnglishFirstInput && _text.Mode == DisplayMode.HalfWidthAlphanumeric && !_modeByKey && !IsProtectedInput &&
                                        !_shift:
                // 設定「英字を主に入力」: Space は変換ではなく、ふつうの空白として入力中の文字に足す (ls -la)
                _text.Append(' ');
                return;
            case VirtualKeys.Convert:
                _text.FixTypos();
                StartConversion(preferJapanese: true);
                return;
            case VirtualKeys.Return when SelectedPrediction() is { } prediction:
                // 予測変換の候補を選んでいれば、それを確定する。
                CommitPrediction(prediction);
                return;
            case VirtualKeys.Return:
                if (IsProtectedInput) { CommitProtected(""); return; }
                // 設定「Enter は見えているとおりに確定」: 打ち間違いの直しは Space で変換するときだけ (issue #677)
                _text.FixTypos(correct: !_settings().EnterCommitsAsShown);
                Commit();
                return;
            case VirtualKeys.Space when _shift:
                // Shift+Space: 英語と判定した語でも、ローマ字として読んで変換する (go → 語、camera → かめら)。
                _text.FixTypos();
                _spaceStartedConversion = true;
                StartConversion(preferJapanese: true);
                return;
            case VirtualKeys.Space when _settings().SeparateKeyRoles && !IsProtectedInput:
                // 設定「入力中のキーの役割を分ける」: Space は英語・日本語の判定にかかわらず候補を開く (英字のまま確定して空白を入れない)。
                // 英字で見えている語も、英字を最初の候補に残し、ローマ字として読めれば日本語の候補も選べる (issue #199)
                _text.FixTypos();
                _spaceStartedConversion = true;
                StartConversion();
                return;
            case VirtualKeys.NonConvert when _settings().SeparateKeyRoles:
                // 無変換: 今までの入力を英字にする (F10 と同じ。続けて押すと大文字・小文字も切り替わる)
                SetAlphanumericMode(DisplayMode.HalfWidthAlphanumeric);
                _modeByKey = true;
                return;
            case VirtualKeys.Kana when _settings().SeparateKeyRoles:
                // ひらがな / カタカナ キー: 押すたびに ひらがな ⇔ カタカナ (漢字・英字の状態からは、まずひらがな)
                SetMode(_text.Mode == DisplayMode.Hiragana && !_converting ? DisplayMode.Katakana : DisplayMode.Hiragana);
                _modeByKey = true;
                return;
            case VirtualKeys.Space:
                // 保護区間 (メンション・URL・パス) の末尾は、自動変換せず原文のまま + 半角空白で確定する。
                if (IsProtectedInput) { CommitProtected(" "); return; }
                _text.FixTypos();
                // 英語と判定した語で終わっているなら、変換ではなく確定して空白を入れる
                // (日本語の部分は、ライブ変換が ON なら漢字にして、OFF なら見えているかなのまま確定)。
                // 数字だけ (1、12) は、前が英文なら確定して空白 (I have 2 cats)。それ以外は変換して ① 一 Ⅰ などの候補を出す。
                // 打った英字全体を読みとして登録した語 (ユーザー辞書の zh → ←、略入力の ast → 明日) があれば、英語と判定しても変換する
                if (HasRegisteredReading())
                {
                    _spaceStartedConversion = true;
                    StartConversion();
                }
                else if (SpaceCommitsMarkdownMarker())
                {
                    // Markdown の記号だけは、変換せず打った半角 + 空白で確定する
                    CommitText(_text.Raw + " ", english: false, preserveText: true, rule: "Space: Markdown の記号なので打ったまま");
                }
                else if (SpaceCommitsAlphanumeric())
                {
                    Commit(suffix: " ", fixEnglish: true);
                }
                else if (EndsWithEnglish()) CommitText(FixEnglishTypo(_text.RenderSegments(final: true, _settings().LiveConversion ? Convert : null)) + " ", english: true, _text.Raw, rule: "Space: 英語で終わるので変換せずに確定");
                else if (SpaceCommitsAutoEnglish()) CommitText(FixEnglishTypo(_text.Raw) + " ", english: true, _text.Raw, rule: "Space: かなにならない子音で終わる英単語");
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
            case 0x2E /* Delete */ when SelectedPrediction() is { } prediction && _services.Predictor?.Phrases is { } phrases:
                // 予測の候補を選んでいる間の Delete: その語句を学習から消す (issue #349)
                phrases.Forget(prediction.Text);
                _predictionKey = "\u0000"; // 候補を作り直す
                _predictionIndex = -1;
                return;
            case VirtualKeys.Escape when _predictionIndex >= 0:
                // 予測の候補を選んでいたら、選ぶのをやめるだけ (打った内容は残す)。
                _predictionIndex = -1;
                return;
            case VirtualKeys.Escape when _settings().EscStepsBack && StepBack():
                return;
            case VirtualKeys.Escape:
                _text.Clear();
                return;
            case VirtualKeys.Tab when FindMisspelling() is { } typo:
                // もしかして: 書き間違いを直す。
                FixMisspelling(typo);
                return;
            case VirtualKeys.Tab when _text.Suggestion() is not null:
                // 自動判定を使わないとき: 提案どおり英字にする。
                _text.SuggestionAccepted = true;
                return;
            case VirtualKeys.Tab when CurrentPredictions().Count > 0:
                // 予測変換の候補を選ぶ (Tab で次、Shift+Tab で前。最後の次は選ばない状態に戻る)。
                var step = _shift ? -1 : 1;
                _predictionIndex = (_predictionIndex + 1 + step + _predictions.Count + 1) % (_predictions.Count + 1) - 1;
                return;
            case VirtualKeys.Tab when !_converting && !_shift && _settings().TabConversion && TabStartsConversion():
                // Space なら変換を始める場面の Tab は、Space と同じく変換を始める (Microsoft IME と同じ。空白を打とうとした意味ではない: issue #219)。
                StartConversion();
                _tabStartedConversion = _converting;
                return;
            case VirtualKeys.F6: SetMode(DisplayMode.Hiragana); _modeByKey = true; return;
            case VirtualKeys.F7: SetMode(DisplayMode.Katakana); _modeByKey = true; return;
            case VirtualKeys.F8: SetMode(DisplayMode.HalfWidthKatakana); _modeByKey = true; return;
            case VirtualKeys.F9: SetAlphanumericMode(DisplayMode.FullWidthAlphanumeric); _modeByKey = true; return;
            case VirtualKeys.F10: SetAlphanumericMode(DisplayMode.HalfWidthAlphanumeric); _modeByKey = true; return;
            case VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down when !_text.IsAlphanumeric:
                // 変換前でも矢印キーで文節の選択に入る (Mac のライブ変換と同じ)。
                EnterClauseSelection(vk);
                return;
        }

        if (VirtualKeys.IsHankakuZenkaku(vk))
        {
            TogglePendingInput();
            return;
        }

        if (_text.KanaInput && KanaOf(e) is { } key)
        {
            CommitBeforeTyping();
            _text.AppendKana(key.Raw, key.Kana);
            return;
        }

        if (CharOf(e) is { } ch && !char.IsControl(ch) && ch != ' ')
        {
            // 打ち始めの ・ を Space で / にしてから打った名前 (/help) は、/ を確定して半角のままアプリへ渡す。
            if (_converting && _text.IsSlashAtStart && _clauses is [{ Text: "/" }] && SigilWord.IsNameChar(ch))
            {
                Commit();
                PassKey();
                return;
            }
            CommitBeforeTyping();
            _text.Append(ch);
            return;
        }

        // 矢印 (英字だけのとき)・Tab・Delete などは確定してから通す。
        Commit();
        PassKey();
    }

    /// <summary>変換ボックスを開く記号・数字。</summary>
    internal static bool StartsWithSymbol(char c) => c is >= '!' and <= '~' && !char.IsAsciiLetter(c);

    /// <summary>変換ボックスが空のときの最初の打鍵。英字・句読点なら入力を始め、それ以外はそのまま通す。</summary>
    private void StartWith(KeyEvent e)
    {
        if (e.Vk == VirtualKeys.Convert)
        {
            if (ReconversionSelectionNow() is not { } selection || string.IsNullOrWhiteSpace(selection.Reading)) return;
            _correctable.Clear();
            // ローマ字のまま入った文は、日本語にする所が無ければ何もしない (英字をローマ字として読んだ候補を出さない)
            if (IsRomajiText(selection.Text))
            {
                StartRomajiReconversion(selection);
                return;
            }
            BeginComposition();
            _reconversion = selection;
            foreach (var kana in selection.Reading) _text.AppendKana(kana, kana);
            _text.Mode = DisplayMode.Hiragana;
            StartConversion(preferJapanese: true);
            return;
        }
        // 日本語入力のときの Shift+Space は全角スペース (Microsoft IME と同じ: issue #24)。英数状態では普通の空白のまま。
        // 設定「スペースの幅」が全角では、Space が全角で Shift+Space が半角 (issue #259)
        var spaceWidth = _settings().SpaceWidth;
        if (e.Vk == VirtualKeys.Space && !_services.DirectMode() && (spaceWidth == Config.SpaceWidth.FullWidth || _shift))
        {
            // 「スペースの幅」が半角なら Shift+Space も半角 (issue #708)
            var shifted = _shift;
            var halfWidth = spaceWidth switch
            {
                Config.SpaceWidth.FullWidth => shifted,
                Config.SpaceWidth.HalfWidth => true,
                Config.SpaceWidth.Standard => false,
                _ => throw new InvalidOperationException($"不明な SpaceWidth: {spaceWidth}"),
            };
            CommitText(halfWidth ? " " : "　", english: false);
            return;
        }
        // かな入力: かなのキーならすべて入力を始める (英数状態でなければ)。
        if (KanaInput && !_services.DirectMode() && KanaOf(e) is { } key)
        {
            BeginComposition();
            _text.AppendKana(key.Raw, key.Kana);
            return;
        }
        var c = CharOf(e);
        var letter = VirtualKeys.IsLetter(e.Vk) && c is { } l && char.IsAsciiLetter(l);
        if (letter && _services.DirectMode())
        {
            // 英数状態: ローマ字かどうか判定する (打鍵はすぐ送る)。大文字で始まる語は英語なのでそのまま通す。
            if (_services.ClassifyDirect is null || _shift || char.IsAsciiLetterUpper(c!.Value))
            {
                Diagnostics.Log.Info($"英数状態: {Diagnostics.Log.Text(c.ToString()!)}は大文字 / Shift なので英語のまま");
                PassKey();
                _services.DirectDecided?.Invoke(false);
            }
            else Hold(e);
            return;
        }
        // 句読点・かぎかっこ・長音・中黒・数字でも入力を始める (、。「」ー・)。英数状態ではそのまま通す。
        if (letter || (c is { } symbol && StartsWithSymbol(symbol) && !_services.DirectMode()))
        {
            BeginComposition();
            _text.Append(c!.Value);
            return;
        }
        PassKey();
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
    private void CommitOrKeepConversion()
    {
        _modeByKey = false;
        _escReverted = false;
        if (!_settings().ContinueAfterConversion || _reconversion is not null)
        {
            Commit();
            BeginComposition();
            return;
        }
        foreach (var clause in _clauses.Where(c => !c.IsEnglish)) _keptChoices[clause.Reading] = clause.Text;
        _converting = false;
    }

    /// <summary>
    /// 入力中に次の文字を打ったとき。変換中なら、今の候補で確定して新しい入力を始める (IME と同じ。設定で、確定せずに続けることもできる)。
    /// F6〜F10 でかな・英字にしたあとなら、見えているとおりに確定して新しい入力を始める (続けて打った文字までカタカナ・大文字にしない)。
    /// </summary>
    private void CommitBeforeTyping()
    {
        if (_converting) CommitOrKeepConversion();
        else if (_modeByKey)
        {
            Commit();
            BeginComposition();
        }
        else if (SelectedPrediction() is { } prediction)
        {
            // Tab で予測の候補を選んだあとに続けて打ったら、その候補で確定して新しい入力を始める (選ぶ前の打ちかけに戻さない)
            CommitPrediction(prediction);
            BeginComposition();
        }
    }

    // F6〜F10 (Ctrl+U/I/O/P・無変換・ひらがなキーも) でかな・英字にしたか。次に文字を打ったら確定する。
    private bool _modeByKey;

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
        _out.PassCurrent();
        if (CharOf(e) is { } c) _out.RecordHeldPassed(c);
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
            : _services.ClassifyDirect!(part, final);
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
        // 判定を待たずに送っていた英字を消して、変換ボックスに入れ直す。
        _out.DeleteHeld(sent);
        ClearHeld();
        _services.DirectDecided?.Invoke(true);
        BeginComposition();
        foreach (var c in letters)
        {
            if (_text.KanaInput && Detection.KanaDetector.KanaForKey(char.ToUpperInvariant(c), char.IsAsciiLetterUpper(c)) is { } kana) _text.AppendKana(c, kana);
            else _text.Append(c);
        }
    }

    /// <summary>
    /// JIS かな以外のかな配列 (月・花・NICOLA・中指NICOLA・TRON) と SKY 配列の打鍵 (issue #1415)。配列のキーとして扱ったら true。
    /// 打ったキーの英字は英単語の判定に使わない (刻印と違う配列なので)。かなをそのまま原文にする。
    /// </summary>
    private bool HandleKanaLayout(KeyEvent e)
    {
        if (!KanaInput && !_services.DirectMode() && _settings().RomajiLayout == RomajiLayout.Sky)
        {
            // SKY 配列: キーをローマ字の綴りにして足す。英語の判定はしない (ひらがなで見せる)
            if (_shift || SkyLayout.Romaji(e.Vk) is not { } romaji) return false;
            if (!IsComposing) BeginComposition();
            else CommitBeforeTyping();
            if (_text.Mode == DisplayMode.Auto) _text.Mode = DisplayMode.Hiragana;
            foreach (var c in romaji) _text.Append(c);
            return true;
        }
        var layout = KanaInput && !_services.DirectMode() ? _settings().KanaLayout : KanaLayout.Jis;
        if (_kanaLayout.Layout != layout) _kanaLayout = new KanaLayoutEngine(layout);
        if (layout == KanaLayout.Jis) return false;
        // 親指キーは (入力していないとき・変換中も) 受け取っておき、文字に使わずに離したらもとの働き (再変換・変換・無変換) をする
        // (文頭の 変換 + W を再変換にしない・変換中の 変換 + W をシフト面にする。親指シフトの配列では親指キーで変換ボックスを開く: StartsComposition)
        // 前置シフトの直後の BackSpace は、シフトのキーを打ち消す (前の文字は消さない)
        if (e.Vk == VirtualKeys.Back && _kanaLayout.HasPrefix)
        {
            _kanaLayout.Reset();
            return true;
        }
        var shift = _shift;
        var stroke = _kanaLayout.Down(e.Vk, shift, e.TimeMs);
        if (!stroke.Handled)
        {
            // 配列のキー以外 (BackSpace・Enter・Esc・Space など): 打ちかけの前置シフトを忘れる
            _kanaLayout.Reset();
            // 配列の表で空いているかなのキー (月配列の 1 や ]) は何も入れない (JIS かなの文字を入れない)
            return !shift && Detection.KanaDetector.IsKanaKey(e.Vk);
        }
        ApplyKanaStroke(stroke);
        return true;
    }

    private void ApplyKanaStroke(KanaStroke stroke)
    {
        if (stroke.Kana.Length == 0 && stroke.RemoveLast == 0) return;
        if (!IsComposing) BeginComposition();
        else if (stroke.RemoveLast == 0) CommitBeforeTyping();
        for (var i = 0; i < stroke.RemoveLast && !_text.IsEmpty; i++) _text.RemoveLast();
        foreach (var kana in stroke.Kana) _text.AppendKana(kana, kana);
    }

    /// <summary>かな入力で、その打鍵が入力するかなと、そのキーの英字 (英語として見せるとき用)。かなのキーでなければ null。</summary>
    private (char Raw, char Kana)? KanaOf(KeyEvent e)
    {
        var shift = _shift;
        if (Detection.KanaDetector.KanaForKey(e.Vk, shift) is not { } kana) return null;
        var raw = CharOf(e) ?? kana;
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
        _services.DirectDecided?.Invoke(false);
    }

    private void ClearHeld()
    {
        _heldLetters.Clear();
        _heldSent = 0;
        _out.ResetHeldPassed();
    }

    /// <summary>
    /// Space を押したときに、変換ではなく確定して空白を入れる場面か (英語で終わる 3 条件)。
    /// 例: hello (英語と判定) → true、kana → false。Tab で変換を始めるかの判定にも使う。
    /// </summary>
    private bool SpaceCommitsAsEnglish() => SpaceCommitsAlphanumeric() || EndsWithEnglish() || SpaceCommitsAutoEnglish();

    private bool HasRegisteredReading() =>
        !_text.KanaInput && _text.Mode == DisplayMode.Auto && _text.Raw is { Length: >= 2 } raw && raw.All(char.IsAsciiLetter) &&
        (_services.UserDictionary?.Lookup(raw.ToLowerInvariant()) is { Count: > 0 } ||
         _settings().ConsonantAbbreviations && _services.Candidates?.Lookup(raw.ToLowerInvariant()) is { Count: > 0 });

    private bool SpaceCommitsMarkdownMarker() => _text.Mode == DisplayMode.Auto && _text.IsMarkdownMarker;

    private bool SpaceCommitsAlphanumeric() =>
        _text.IsAlphanumericAt(final: true) && !(_text.Mode == DisplayMode.Auto && _text.IsNumeric && _text.Raw.All(char.IsAsciiDigit) && _text.PrecedingEnglish != true);

    private bool SpaceCommitsAutoEnglish() => _text.Mode == DisplayMode.Auto && _detector.IsEnglishAtWordEnd(_text.Raw, !_settings().AutoDetection);

    /// <summary>
    /// Space の分岐で変換を始める (StartConversion に行く) 場面か。Tab で始めるときも同じ場面に限る (issue #219)。
    /// Space と同じ順で判定するため、英語かを見る前に打ち間違いを直す (Enter で確定するときと同じ直し)。
    /// </summary>
    private bool TabStartsConversion()
    {
        // 保護区間が語の途中にあり、その後にかなが続く入力も Tab 変換しない。
        // IsProtectedInput は末尾が保護区間かだけを見るため、ここでは全区間を調べる。
        if (_text.Mode == DisplayMode.Auto && _text.ProtectedSpans().Count > 0) return false;
        _text.FixTypos();
        return HasRegisteredReading() || !SpaceCommitsAsEnglish();
    }

    /// <summary>変換中だけ意味を持つキー。処理したら true。</summary>
    private bool HandleConversionKey(int vk)
    {
        var shift = _shift;
        switch (vk)
        {
            case VirtualKeys.Space when shift || _shift:
                // Shift+Space: 前の候補へ (Microsoft IME と同じ。Mac でも Shift を押したまま Space で戻れるように: issue #140)
                NextCandidate(-1);
                return true;
            case VirtualKeys.Convert:
            case VirtualKeys.Space:
            case VirtualKeys.Down:
                NextCandidate(+1);
                return true;
            case VirtualKeys.Up:
                NextCandidate(-1);
                return true;
            case VirtualKeys.PageDown:
                // 候補の一覧のページ送り (Microsoft IME と同じ。アプリに通すと確定してしまっていた: issue #1016)
                NextCandidatePage(+1);
                return true;
            case VirtualKeys.PageUp:
                NextCandidatePage(-1);
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
            case VirtualKeys.Return when shift && _settings().PartialCommit && _selectedClause < _clauses.Count - 1:
                CommitUpToSelectedClause();
                return true;
            case VirtualKeys.Return:
                Commit();
                return true;
            case VirtualKeys.F6 or VirtualKeys.F7 or VirtualKeys.F8 or VirtualKeys.F9 or VirtualKeys.F10
                when _settings().FunctionKeysAffectSelectedClause && _clauses.Count > 1:
                ApplyFunctionKeyToClause(vk);
                return true;
            case VirtualKeys.Tab when FindMisspelling() is { } typo:
                // もしかして: 書き間違いを直して変換し直す。
                _converting = false;
                FixMisspelling(typo);
                StartConversion();
                return true;
            case VirtualKeys.Tab when _tabStartedConversion:
                // Tab で始めた変換の中では、Tab が次の候補、Shift+Tab が前の候補 (issue #219)。
                NextCandidate(shift || _shift ? -1 : +1);
                return true;
            case VirtualKeys.Escape when _settings().EscRevertsSelectedClause && _clauses.Count > 1 &&
                                         !_clauses[_selectedClause].IsEnglish && _clauses[_selectedClause].Text != _clauses[_selectedClause].Reading:
                // 設定「変換中の Esc は選んだ文節だけをかなに戻す」: 選んでいる文節だけをひらがなにして、変換は続ける (issue #670)
                {
                    var clause = _clauses[_selectedClause];
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
                _converting = false;
                _tabStartedConversion = false;
                return true;
            case >= 0x31 and <= 0x39 when !shift && !_text.KanaInput && _settings().NumberKeysSelectCandidates:
            case >= 0x61 and <= 0x69 when !shift && _settings().NumberKeysSelectCandidates:
                // 候補の一覧の番号 (1〜9) で選ぶ。番号の無い候補 (一覧より後ろ) なら、普通に打った数字として扱う。
                // かな入力では数字の段のキーは ぬふあうえおやゆよ なので、テンキーだけで選ぶ。
                // 設定「変換中の数字キーで候補を選ぶ」が OFF なら、上の段もテンキーも普通に打った数字 (今の候補で確定して入力する: issue #989)。
                return SelectByNumber(vk >= 0x61 ? vk - 0x61 : vk - 0x31);
            default:
                return false;
        }
    }

    /// <summary>
    /// 数字キーで、今見えている候補の一覧のページから選ぶ (2 → そのページの 2 番目)。選んだら次の文節へ進み、
    /// 最後の文節なら確定する (Microsoft IME と同じく、番号で選んだ候補はそのまま使う)。
    /// </summary>
    private bool SelectByNumber(int row)
    {
        var clause = _clauses[_selectedClause];
        if (!clause.Expanded) Expand(clause);
        var index = Math.Max(0, clause.Index) / CandidatePageSize * CandidatePageSize + row;
        if (index >= clause.Candidates.Count) return false;
        clause.Index = index;
        clause.Changed = true;
        if (_selectedClause + 1 < _clauses.Count) _selectedClause++;
        else Commit();
        return true;
    }

    private (int Start, int End, Misspelling Misspelling)? FindMisspelling() =>
        _services.Misspellings is { } dictionary ? _text.FindMisspelling(dictionary) : null;

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
        var shift = _shift;
        _selectedClause = vk == VirtualKeys.Left ? _clauses.Count - 1 : 0;
        if (shift && vk is VirtualKeys.Left or VirtualKeys.Right) Resize(vk == VirtualKeys.Left ? -1 : +1);
        else if (vk == VirtualKeys.Up) NextCandidate(-1);
    }

    /// <summary>
    /// 設定「Esc で一段ずつ戻す」: 見えている内容を 漢字 → ひらがな → 打ったローマ字 の順に一段戻す。見た目が変わらない段は飛ばす。
    /// 戻せる段が無ければ false (そのまま全部消す)。戻した語は日本語 / 英語の判定として覚えない (issue #676)。
    /// </summary>
    private bool StepBack()
    {
        var before = CurrentDisplay(final: false);
        // 今のモードより先の段だけ (ひらがなの次はローマ字。カタカナなどはひらがなへ)
        DisplayMode[] steps = _text.Mode switch
        {
            DisplayMode.Hiragana => [DisplayMode.HalfWidthAlphanumeric],
            DisplayMode.HalfWidthAlphanumeric => [],
            // 英字のまま見えている入力 (hello) は、かな (へっぉ) に変えても「一段戻す」にならない。戻せる段は無い (そのまま全部消す)
            DisplayMode.Auto when _text.IsAlphanumeric => [],
            _ => [DisplayMode.Hiragana, DisplayMode.HalfWidthAlphanumeric],
        };
        foreach (var mode in steps)
        {
            SetMode(mode);
            _escReverted = true;
            _modeByKey = true;
            if (CurrentDisplay(final: false) != before) return true;
        }
        return false;
    }

    // Esc で戻した語は、判定として覚えない
    private bool _escReverted;

    /// <summary>
    /// 設定「変換中の F6〜F10 は選んだ文節だけに効かせる」: 選んでいる文節だけを、ひらがな / カタカナ / 半角カタカナ / 全角英数 / 半角英数にする。
    /// 英語の文節は、打った英字 (Reading) を英数に、ローマ字として読んだかな (Kana) をかなにする。
    /// 打った英字や読みが分からない文節 (かな入力、1 音の途中で区切れた文節) では、文全体に効かせずに何もしない。issue #678
    /// </summary>
    private void ApplyFunctionKeyToClause(int vk)
    {
        var clause = _clauses[_selectedClause];
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
    private void CommitUpToSelectedClause()
    {
        var all = _clauses;
        var done = all.Take(_selectedClause + 1).ToList();
        var rest = all.Skip(_selectedClause + 1).ToList();
        _clauses = done;
        LearnClauses();
        var text = string.Concat(done.Select(c => c.Text));
        CommitText(text, done.All(c => c.IsEnglish), "", chosen: true);
        BeginComposition();
        foreach (var clause in rest)
        {
            if (clause.IsEnglish)
            {
                foreach (var letter in clause.Raw ?? clause.Reading) _text.Append(letter);
            }
            else
            {
                foreach (var kana in clause.Reading) _text.AppendKana(kana, kana);
            }
        }
    }

    private void SetMode(DisplayMode mode)
    {
        _converting = false;
        _text.Mode = mode;
        _text.Case = LetterCase.AsTyped;
    }

    /// <summary>
    /// F9 / F10: 英字にする。もう英字で見せているときに押すと、大文字・小文字を 打ったまま → すべて大文字 → 先頭だけ大文字 → … と切り替える
    /// (ai → AI → Ai → ai。Microsoft IME と同じ)。見た目の変わらない段 (打ったままが既にすべて大文字など) は飛ばす。
    /// </summary>
    private void SetAlphanumericMode(DisplayMode mode)
    {
        var before = _converting ? null : CurrentDisplay(final: false);
        var sameMode = !_converting && _text.Mode == mode;
        _converting = false;
        if (!sameMode)
        {
            _text.Mode = mode;
            _text.Case = LetterCase.AsTyped;
            // 設定「F9 / F10 の 1 回目は打ったまま」: 見えている内容にかかわらず、1 回目は打ったままにする (issue #515)
            if (_settings().AlphanumericKeyStartsAsTyped) return;
            // 英単語と判定して英字で見せていた (Auto) ときは、F10 を押した時点で見た目が変わるように次の段へ進める。
            if (CurrentDisplay(final: false) != before) return;
        }
        for (var i = 0; i < 3; i++)
        {
            _text.Case = (LetterCase)(((int)_text.Case + 1) % 3);
            if (CurrentDisplay(final: false) != before) return;
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

    private string? _boundKeysText;
    private Dictionary<int, int> _boundKeys = [];

    /// <summary>設定「Control キーの割り当て」(T=F6;Y=F7 …) で、Control + vk に割り当てたキー。無ければ null。</summary>
    private int? BoundControlKey(int vk)
    {
        var text = _settings().ControlKeyBindings;
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!ReferenceEquals(text, _boundKeysText) && text != _boundKeysText)
        {
            _boundKeysText = text;
            _boundKeys = ControlKeyBindingParser.Parse(text);
        }
        return _boundKeys.TryGetValue(vk, out var key) ? key : null;
    }

    /// <summary>
    /// 入力中に Ctrl と一緒に押すと、変換ボックスで使う英字のキー (設定「Control キーの割り当て」による。HandleKey の Ctrl の処理と同じ順に見る)。
    /// 入力していなければ空。ほかの Ctrl のキーは、確定してアプリへ送る。
    /// </summary>
    public IReadOnlyList<int> ComposingControlKeys()
    {
        if (_text.IsEmpty) return [];
        var keys = new HashSet<int>();
        if (_converting && _settings().ControlKeys == Config.ControlKeyLayout.Atok) keys.UnionWith(['K', 'L']);
        if (!string.IsNullOrWhiteSpace(_settings().ControlKeyBindings)) keys.UnionWith(ControlKeyBindingParser.Parse(_settings().ControlKeyBindings).Keys);
        for (var vk = 'A'; vk <= 'Z'; vk++)
        {
            if ((_settings().ControlKeys == Config.ControlKeyLayout.Mac ? MacControlShortcut(vk) : null) is not null || ControlShortcut(vk) is not null) keys.Add(vk);
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
    private void PassKey()
    {
        _correctable.Clear();
        _out.PassCurrent();
    }
}
