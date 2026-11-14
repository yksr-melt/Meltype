// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>確定: 確定する文字の組み立て、英単語の前後の空白、確定した後に前の語を確定し直す自動修正、予測変換の候補の確定。</summary>
public sealed partial class CompositionController
{
    /// <summary>今の表示 (ライブ変換が有効なら日本語区間は漢字に変換済み)。</summary>
    private string CurrentDisplay(bool final) =>
        _text.Display(final, _settings().LiveConversion ? LiveConvert : null);

    private void CommitIfAny(bool preserveText = false)
    {
        if (!_text.IsEmpty) Commit(preserveText: preserveText);
    }

    /// <summary>今の未確定入力が、自動では変換してはいけない保護区間か (F9/F10 などの明示指定は除く)。</summary>
    private bool IsProtectedInput => !_converting && _text.Mode == DisplayMode.Auto && _text.HasProtectedTail;

    private void Commit(string suffix = "", bool fixEnglish = false, bool preserveText = false)
    {
        if (IsProtectedInput)
        {
            CommitProtected(suffix);
            return;
        }
        var converting = _converting && _clauses.Count > 0;
        // 区間分けの重みで決まらなかった確定の決まり。誤変換の報告をログから読み解けるよう、通った決まりを確定のログに書く (issue #1644)
        var rules = new List<string>();
        // 設定「Enter は見えているとおりに確定」が OFF なら、背景の結果が間に合っていなくても Budget の間は答えを待つ
        var text = converting ? string.Concat(_clauses.Select(c => c.Text))
            : _budget is { Enabled: true } && !_settings().EnterCommitsAsShown
                ? _text.Display(final: true, _settings().LiveConversion ? kana => CountsAsLive(kana) ? string.Concat(ConvertJapanese(kana).Select(c => c.Text)) : kana : null)
                : CurrentDisplay(final: true);
        // 英語の文脈で英字だけに見えていた入力 (hello mata) は、確定するときに判定し直してかなにしない: 見えているとおりに確定する (issue #127)。
        // 文脈がない単独の語 (mine → みね) は、今までどおり確定時の判定に任せる
        if (!converting && _text.Mode == DisplayMode.Auto && !_text.KanaInput && _text.PrecedingEnglish == true && CurrentDisplay(final: false) is { Length: > 0 } shown &&
            shown.All(char.IsAsciiLetter) && shown != text && text.Any(c => c > 0x7F))
        {
            text = shown;
            rules.Add("英語の文脈なので見えていた英字のまま (#127)");
        }
        // 設定「Enter は見えているとおりに確定」: かな + 語末の n (かんたn) は、確定でライブ変換が漢字にせず、見えていたかなに n を ん にしただけで確定する (issue #677)
        if (!converting && _settings().EnterCommitsAsShown && _text.Mode == DisplayMode.Auto && _text.Pending == "n" &&
            CurrentDisplay(final: false) is { Length: > 1 } shownKana && shownKana[^1] == 'n' && shownKana[^2] is >= '\u3041' and <= '\u3096')
        {
            text = shownKana[..^1] + "ん";
            rules.Add("見えていたかなのまま (#677)");
        }
        if (fixEnglish && !converting && _text.Mode == DisplayMode.Auto && FixEnglishTypo(text) is var fixedText && fixedText != text)
        {
            text = fixedText;
            rules.Add("英語の打ち間違いを直した");
        }
        var english = converting ? _clauses.All(c => c.IsEnglish) : _text.IsAlphanumericAt(final: true);
        // F6〜F10 で、はっきり英字 / かなを選んで確定した語も、後から確定し直さない。
        var chosen = converting ? _clauses.Any(c => c.Changed) : _text.Mode != DisplayMode.Auto;
        if (_reconversion is not null)
        {
            // 入れた文字は、アプリが選択していた文字と置き換える。
            rules.Add("選択していた文字の再変換");
            LogCommit(text + suffix, string.Join("、", rules));
            _out.Commit(text + suffix);
            if (converting) LearnClauses();
            ResetContext();
            ClearComposition();
            return;
        }
        RecordCorrection(converting, text, suffix);
        if (converting) LearnClauses();
        else if (Learning && !_escReverted) _learning.LearnLanguage(_text);
        if (!converting || _clauses.All(CompositionLearning.Learnable))
            RememberPhrase(converting ? string.Concat(_clauses.Select(c => c.IsEnglish ? "" : c.Reading)) : _text.AllKana(final: true), text, english);
        CommitText(text + suffix, english, _text.Raw, chosen, preserveText, string.Join("、", rules));
    }

    /// <summary>
    /// 確定のたびに、確定した文字と、そう決めた理由 (区切りの重み・次点・文節・通った決まり) を残す (issue #1644)。
    /// 誤変換の報告を、利用者のログから読み解けるようにする。打った文字は設定「ログに入力した文字を残す」に従う。
    /// </summary>
    /// <param name="rule">区切りの重みでは決まらなかった確定の決まりの名前 (#127 で見えていた英字のまま、など)。</param>
    private void LogCommit(string text, string rule)
    {
        if (_text.IsEmpty) return;
        var parts = new List<string>();
        if (Diagnostics.Log.RecordText) parts.Add($"読み「{_text.AllKana(final: true)}」");
        // 区切りは、表示に使ったのと同じ計算 (Segments) から取る
        if (_text.Mode == DisplayMode.Auto) parts.Add(_text.ExplainSegments());
        else parts.Add($"F6〜F10 で選んだ表示 ({_text.Mode})");
        if (_clauses.Count > 0)
            parts.Add("文節 " + string.Join(" | ", _clauses.Select(c => (Diagnostics.Log.RecordText ? $"{c.Reading}→{c.Text}" : $"({c.Reading.Length} 文字)") + (c.Changed ? " (選び直した)" : ""))));
        if (rule.Length > 0) parts.Add(rule);
        Diagnostics.Log.Decision($"確定: {Diagnostics.Log.Text(_text.Raw)}→{Diagnostics.Log.Text(text)}; {string.Join("; ", parts.Where(p => p.Length > 0))}");
    }

    /// <summary>
    /// ユーザーが自動の判定を自分で直して確定したとき (F6〜F10 で英字 / かなにした、変換の候補から打ったままの英字・日本語を選んだ)。
    /// 誤判定の報告 (トレイの「直した誤判定を報告...」) に使う。記録を作るだけで、どこにも送らない (issue #284)。
    /// </summary>
    public event Action<Correction>? Corrected;

    private void RecordCorrection(bool converting, string text, string suffix)
    {
        if (Corrected is null || _text.Raw.Length == 0 || !_text.Raw.Any(char.IsAsciiLetter)) return;
        string shown;
        if (converting)
        {
            // 英語 / 日本語を選び直した文節があるときだけ (漢字の選び直しは判定の間違いではない)
            var switched = _clauses.Any(c => c.Changed && (c.IsEnglish ? IsJapaneseText(c.Text) : c.Raw is { } raw && c.Text == raw));
            if (!switched) return;
            shown = string.Concat(_clauses.Select(c => c.Candidates[0]));
        }
        else
        {
            if (_text.Mode == DisplayMode.Auto) return;
            var (mode, letterCase) = (_text.Mode, _text.Case);
            _text.Mode = DisplayMode.Auto;
            shown = _text.Display(final: true);
            (_text.Mode, _text.Case) = (mode, letterCase);
        }
        if (shown == text) return;
        var before = _precedingText ?? "";
        Corrected.Invoke(new Correction(_text.Raw, shown, text, suffix.Contains(' ') ? "Space (変換)" : "Enter (確定)",
            before.Length > 20 ? before[^20..] : before, DateTime.Now));
    }

    /// <summary>
    /// 保護区間 (メンション・URL・メール・パス) を、自動変換・かな化・幅変換・誤字補正・学習・確定後補正に
    /// 渡さず、打った原文のまま確定する。Space は IME が半角空白を 1 つ足し、Enter は足さない。
    /// </summary>
    private void CommitProtected(string suffix)
    {
        // 表示の再計算に依存せず、打った原文そのものを使う (確定条件で区間分けが変わっても記号を変えない)。
        var text = (_text.IsProtectedRaw ? _text.Raw : CurrentDisplay(final: true)) + suffix;
        _converting = false;
        _clauses = [];
        _correctable.Clear();
        // raw を空にして渡し、保護原文を確定後補正・学習の対象にしない (INV-06)。
        CommitText(text, english: false, preserveText: true, rule: "保護区間 (メンション・URL・メール・パス) で終わるので、打ったまま");
    }

    /// <summary>確定した日本語の語句を、予測変換のために覚える (設定「予測変換」が ON のとき)。</summary>
    private void RememberPhrase(string reading, string text, bool english)
    {
        if (!english && Learning && _settings().PredictiveCandidates) _learning.RememberPhrase(reading, text);
    }

    /// <summary>確定した文節を学習する。設定「ユーザー学習を使う」が OFF なら、新しく覚えない (issue #277)</summary>
    private void LearnClauses()
    {
        if (Learning) _learning.Learn(_clauses, ConversionContext());
    }

    /// <summary>予測変換の候補を確定する。日本語なら覚え直して、次からより前に出す。</summary>
    private void CommitPrediction(Prediction prediction)
    {
        _predictionIndex = -1;
        if (!prediction.Text.All(c => c < 0x80) && Learning)
            _services.Predictor?.Phrases?.Remember(PredictionReading(prediction.Text, prediction.Reading), prediction.Text);
        // 最後の文節の続きの候補なら、前の文節 (見えているとおり) とつなげて確定する
        var text = prediction.Before + prediction.Text;
        var english = text.All(c => c < 0x80);
        CommitText(text, english, english ? text : _text.Raw, chosen: true);
    }

    /// <summary>予測の候補の読み (前に確定したときの読み)。分からなければ打ちかけの読み。</summary>
    private string PredictionReading(string prediction, string typed) =>
        _services.Predictor?.Phrases?.ReadingOf(prediction, typed) ?? typed;

    /// <summary>
    /// 英単語で終わる文字列の最後の語が、よくある打ち間違い (teh、recieve) なら正しい綴りにする (Space で確定するとき)。
    /// </summary>
    private string FixEnglishTypo(string text)
    {
        if (!_settings().CorrectTypos) return text;
        var start = text.Length;
        while (start > 0 && char.IsAsciiLetter(text[start - 1])) start--;
        if (start == text.Length || _detector.EnglishAutoCorrection(text[start..]) is not { } right) return text;
        Diagnostics.Log.Decision($"英語の打ち間違いを直しました: {Diagnostics.Log.Text(text[start..])}→{Diagnostics.Log.Text(right)}");
        return text[..start] + right;
    }


    /// <summary>
    /// 直前に確定した語。英語とも日本語とも読める語 (i, sushi) を文脈が分からないまま確定したとき、
    /// 次の語で文脈がはっきりしたら確定し直す (I want: 「胃」→「I」)。キャレットが動いたら (ほかのキー・クリック) 無効。
    /// </summary>
    /// <param name="AfterEnglish">確定したとき、前が英語だった (Are + you)。英文の中の語なので、後ろが日本語でも日本語に直さない (#265)。</param>
    private sealed record CommitRecord(string Text, string Raw, bool English, bool SpaceIntended, bool AfterEnglish = false);

    /// <summary>続けて確定した、英語とも日本語とも読める語 (古い順)。make sure you → have で全部英語に直す。</summary>
    private readonly List<CommitRecord> _correctable = [];
    private const int MaxCorrectable = 4;
    private bool _spaceStartedConversion;
    // 確定し直しで消す文字数と、入れ直す文字。今確定する文字と一緒に 1 回で送る。
    private (int Count, string Text)? _pendingReplace;

    /// <summary>今の変換を Tab で始めたか (変換中の Tab で候補を送るため。issue #219)。</summary>
    private bool _tabStartedConversion;

    /// <summary>キャレットが動いたかもしれないとき (Meltype を通らなかったキー・クリック)。直前の語は確定し直さない。</summary>
    public void ForgetLastCommit()
    {
        _correctable.Clear();
        _out.ForgetCommitted();
        // キャレットが動いた・アプリが文字を入れた: キャレットの前は自分が確定した語ではないので、読めたらアプリの文字を使う。
        // 読めなければ前の文字も、それが英語だったかも分からないものとして扱う (消した後・行の先頭に、英単語の前後の空白を足さない)
        _lastCommitTime = long.MinValue / 2;
        _lastCommitText = null;
        _lastCommitEnglish = null;
    }

    /// <summary>
    /// 今確定しようとしている語 (raw) で文脈がはっきりしたら、直前に確定した英語とも日本語とも読める語を確定し直す。
    /// 例: 「i」を Space で「胃」にした後に want と打つ → 「I want」、「sushi 」の後に「がすき」 → 「すしがすき」。
    /// </summary>
    private string? CorrectPreviousCommit(string raw, bool english)
    {
        if (_correctable.Count == 0 || !AutoCorrect || !CanDeleteBackward) return null;
        var previous = _correctable[^1];
        List<CommitRecord> targets = [];
        string? replacement = null;
        if (!previous.English && english && _detector.IsDefinitelyEnglish(raw))
        {
            // 前の語の後ろを Space で区切らずに続けたとき (Enter で確定したときなど) は直さない。英語にすると前の語とつながる (あれ + ON → areON)
            if (!previous.SpaceIntended) return null;
            // 日本語で確定した語を英語に: 代名詞の i は I にし、Space で変換していたなら空白も入れる。
            // その前にも Space で区切って日本語にした語が続いていれば、まとめて英文に直す (make sure you have)。
            var start = _correctable.Count - 1;
            while (start > 0 && !_correctable[start - 1].English && _correctable[start - 1].SpaceIntended) start--;
            targets = _correctable.Skip(start).ToList();
            // 助詞と同じ形の短い語 (to, no) 1 語だけを、大文字で始まる語 (固有名詞) で直すことはしない (Google と Apple は日本語でもよく書く)。
            // 小文字の英単語で英文と分かったとき (let me know、do it) は直す。
            if (targets.Count == 1 && targets[0].Raw.Length <= 2 && targets[0].Raw != "i" && char.IsAsciiLetterUpper(raw.FirstOrDefault(char.IsAsciiLetter))) return null;
            // する の活用のローマ字 (site→して、sita→した) は、後ろが英語でも日本語のまま
            // (site PR → site PR に確定し直さず、して PR を保つ)。
            // sure→すれ は英文 (make sure) でも使うので含めない。
            if (targets.Any(t => IsSuruRomajiKeepJapanese(t.Raw)))
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
        else if (previous.English && !(previous.AfterEnglish && !CompositionDetector.IsJapaneseOriginWord(previous.Raw.ToLowerInvariant())) && !english && !_detector.IsAmbiguousWord(raw) && raw.Any(char.IsAsciiLetter))
        {
            // 英語で確定した語を日本語に (Space で空白を入れていたら取る)。
            targets = [previous];
            replacement = _detector.Romaji.ConvertLenient(previous.Raw.ToLowerInvariant(), final: true);
            // ローマ字として読んでもよく使う語の読みにならない語 (issue → いっすえ、api → あぴ) は英語のまま (issue #121, #124)。
            // sushi → すし のように、日本語の語として読めるときだけ直す。
            if (_services.RomajiTypos is { } lexicon && !lexicon.IsWord(replacement)) return null;
        }
        // ユーザーが英字 / かなに直して覚えた語 (F10 で英字にした api) は、覚えたとおりなら書き換えない (issue #124)。
        if (targets.Any(t => _services.Languages?.Get(t.Raw.ToLowerInvariant()) == t.English)) return null;
        var original = string.Concat(targets.Select(t => t.Text));
        if (replacement is null || replacement == original) return null;
        // 直前に確定した語が、今のキャレットの位置に無い (別の場所をクリックした等) ときは書き換えない。
        // 消すつもりの文字がそこに無いので、消すとキャレットの前の別の文字が消えてしまう。
        // 入力欄の文字をその場で読めるホストだけ確かめる (Windows は別のスレッドで後から読むので、古い値で誤って止めない)。
        if (_caretBefore is { } caretBefore && !caretBefore.EndsWith(original, StringComparison.Ordinal))
        {
            Diagnostics.Log.Decision($"キャレットの位置が変わっているので確定し直しません: {Diagnostics.Log.Text(original)}");
            _correctable.Clear();
            return null;
        }

        Diagnostics.Log.Decision($"前後の文脈に合わせて確定し直しました: {Diagnostics.Log.Text(original)}→{Diagnostics.Log.Text(replacement)}");
        // 消して入れ直すのは、続けて確定する文字と一緒に送る (CommitText)。別々に送ると、その間に入れ直した文字と
        // 続けて確定した文字の順番が入れ替わるアプリがある (Are yoよ人間う？: issue #258)
        _pendingReplace = (original.Length, replacement);
        _lastCommitText = replacement;
        _correctable.Clear();
        return replacement;
    }

    /// <summary>カッコの組 (設定「カッコを打ったら中にカーソルを移動」で、確定したら中に入る)。</summary>
    private static readonly HashSet<string> BracketPairs = ["「」", "『』", "（）", "()", "［］", "[]", "｛｝", "{}", "【】", "〈〉", "《》", "〔〕", "\"\"", "“”"];

    /// <summary>確定して入力する。英語だったか日本語だったか・確定した文字列を、次の入力の文脈として覚えておく。</summary>
    private void CommitText(string text, bool english, string raw = "", bool chosen = false, bool preserveText = false, string rule = "")
    {
        LogCommit(text, rule);
        var formatEnglish = _services.AutomaticEnglishSpacing() && _text.Mode == DisplayMode.Auto;
        var spaceIntended = _spaceStartedConversion;
        var afterEnglish = _text.PrecedingEnglish == true;
        _spaceStartedConversion = false;
        _tabStartedConversion = false;
        _text.Clear();
        _converting = false;
        _clauses = [];
        if (text.Length == 0) return;
        if (formatEnglish && !preserveText) text = EnglishPhraseSpacing.Format(text);
        var corrected = CorrectPreviousCommit(raw, english);
        if (!preserveText && (_settings().SpaceAroundEnglish || _settings().SpaceAroundNumbers))
            text = AddSpacesAroundEnglish(text, corrected ?? _precedingText, _followingText, _settings().SpaceAroundEnglish, _settings().SpaceAroundNumbers);
        // 英語とも日本語とも読める語を、文脈を決めずに (選び直さずに) 確定したときだけ、後で確定し直せるようにしておく。
        if (!chosen && _detector.IsAmbiguousWord(raw))
        {
            // 前の語の後に Space で区切って続けたときだけつなげる (それ以外は新しい並び)。
            if (_correctable.Count > 0 && !_correctable[^1].SpaceIntended) _correctable.Clear();
            _correctable.Add(new CommitRecord(text, raw, english, spaceIntended, afterEnglish));
            if (_correctable.Count > MaxCorrectable) _correctable.RemoveAt(0);
        }
        else _correctable.Clear();
        _lastCommitEnglish = english;
        var joined = (_lastCommitText ?? "") + text;
        _lastCommitText = joined.Length > 20 ? joined[^20..] : joined;
        _lastCommitTime = Environment.TickCount64;
        if (_pendingReplace is var (count, replacement))
        {
            _pendingReplace = null;
            _out.ReplaceBackward(count, replacement, text);
        }
        else _out.Commit(text);
        // 設定「カッコを打ったら中にカーソルを移動」: カッコの組だけを確定したら、中に入る (issue #72)。
        // 確定に付けて返す (キーをアプリに通して動かすと、確定に使ったキーまでアプリに通ってしまう: #991・#1353・#1404・#1581)。
        if (_settings().CursorIntoBrackets && text.Length == 2 && BracketPairs.Contains(text))
            _out.MoveCaretBackward(1);
        Committed?.Invoke(text);
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
    private bool EndsWithEnglish() =>
        _text.Mode == DisplayMode.Auto && _text.Segments() is { Count: > 0 } segments && segments[^1].IsEnglish;
}
