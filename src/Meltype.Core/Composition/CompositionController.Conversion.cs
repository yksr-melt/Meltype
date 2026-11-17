// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>文節と候補を作る: 変換の始まり (ユーティリティ変換を含む)、変換エンジン、候補の並び、文節の区切りの変更、ライブ変換、再変換。</summary>
public sealed partial class CompositionController
{
    /// <summary>今の入力で文節と候補を作る (<see cref="ClauseBuilder"/>)。</summary>
    private ClauseBuilder Builder => new(_converter, _services, _settings(), _learning, _detector, _text, _precedingText, _followingText);

    // 続けて打つ前に選んでいた候補 (読み → 文字)。確定するか、変換ボックスを空にするまで。
    private readonly Dictionary<string, string> _keptChoices = [];

    /// <summary>ローマ字のまま入った文か (英字を含み、半角の英数字・記号・空白だけ)。</summary>
    internal static bool IsRomajiText(string text) =>
        text.Any(char.IsAsciiLetter) && text.All(c => c is >= ' ' and <= '~') && text.Trim().Length > 0;

    /// <summary>
    /// ローマ字のまま入った文を選んで変換キーを押したとき (Meltype が止まっていた・ターミナルで打った: issue #288)。
    /// Meltype の判定で日本語の所だけを日本語にした文を候補として見せ、Enter で選んだ文字と置き換える。
    /// 候補は 日本語にした文 → 選んだままの文。日本語にする所が無ければ何もしない。
    /// </summary>
    private void StartRomajiReconversion(ReconversionSelection selection)
    {
        var converted = ConvertRomajiText(selection.Text);
        if (converted == selection.Text)
        {
            Diagnostics.Log.Info("再変換: 選んだ文字に、日本語にする所がありませんでした。");
            return;
        }
        BeginComposition();
        _reconversion = selection;
        foreach (var c in converted) _text.AppendKana(c, c);
        _text.Mode = DisplayMode.Hiragana;
        _clauses = [new Clause(converted, false, [converted, selection.Text])];
        _selectedClause = 0;
        _converting = true;
    }

    /// <summary>
    /// ローマ字の文を、打ったときと同じ判定で日本語にする。空白で区切った語ごとに英語 / 日本語を判定し (前の語が英語かも手がかりにする)、
    /// 続く日本語の語はつなげて 1 つの文として変換する (asita no → 明日の)。英語の語は前後に空白を残す
    /// (asita no meeting de review shimasu → 明日の meeting で review します)。
    /// </summary>
    internal string ConvertRomajiText(string text)
    {
        var output = new StringBuilder();
        var kana = new StringBuilder();
        var englishWords = 0;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // 前の語が日本語でも「日本語の続き」とはしない (空白で区切って打った語なので、語の途中ではない)。
            // 英語の語が 2 語以上続いた後は、打つときと同じく英文の続き (this is + a)
            var typed = new CompositionText(_detector)
            {
                PrecedingEnglish = englishWords > 0 ? true : null,
                PrecedingEnglishSentence = englishWords >= 2,
            };
            foreach (var c in word) typed.Append(c);
            var segments = typed.Segments();
            foreach (var segment in segments)
            {
                if (!segment.IsEnglish)
                {
                    kana.Append(segment.Kana);
                    continue;
                }
                // 英語の語: 前の日本語をまとめて変換してから、空白で区切って足す
                if (kana.Length > 0) output.Append(ClauseBuilder.Convert(kana.ToString(), _converter));
                kana.Clear();
                if (output.Length > 0 && output[^1] != ' ') output.Append(' ');
                output.Append(segment.Raw).Append(' ');
            }
            englishWords = segments.Count > 0 && segments[^1].IsEnglish ? (segments.Count == 1 ? englishWords + 1 : 1) : 0;
        }
        if (kana.Length > 0) output.Append(ClauseBuilder.Convert(kana.ToString(), _converter));
        return output.ToString().TrimEnd();
    }

    /// <summary>
    /// 区切りを文節にして変換する (<see cref="BuildClauses"/>)。英日の区切りの別案があれば、文全体を 1 つの文節にして、案ごとの文を候補にする。
    /// F6〜F8 でかなにして見せているときは、打った英字をすべてローマ字として読んで変換する。
    /// </summary>
    private void StartConversion(bool preferJapanese = false)
    {
        _conversionPrefersJapanese = preferJapanese;
        _clausesEdited = false;
        _tabStartedConversion = false;
        var asKana = _text.Mode is DisplayMode.Hiragana or DisplayMode.Katakana or DisplayMode.HalfWidthKatakana;
        var segments = asKana ? _text.AllJapaneseSegments() : _text.ConversionSegments();
        var utility = Utilities(segments);
        if (utility is { Placement: UtilityPlacement.Replace })
        {
            var fixedClause = new Clause(utility.Reading ?? _text.Raw, utility.Reading is null, [.. utility.Candidates])
            {
                Raw = _text.Raw,
                Expanded = true,
                Fixed = true,
                Notes = utility.Notes,
            };
            fixedClause.NotLearned.UnionWith(utility.NotLearned);
            // 続けて入力する前の選択だけを復元し、候補の順 (注記と同じ順) は変えない。
            if (_keptChoices.TryGetValue(fixedClause.Reading, out var kept) && fixedClause.Candidates.IndexOf(kept) is >= 0 and var index)
                fixedClause.Index = index;
            _clauses = [fixedClause];
            _selectedClause = 0;
            _converting = true;
            return;
        }
        // 打った英字全体がユーザー辞書の読み (helloworld → 登録した語) か子音の略入力の読み (ast → 明日。issue #62) なら、
        // 区間に分けずに 1 つの文節にして、その語を候補の先頭にする。ローマ字として読めれば、その読みの漢字も出す (go → 後・語・碁)
        if (HasRegisteredReading())
        {
            var typedRaw = _text.Raw;
            var lower = typedRaw.ToLowerInvariant();
            List<string> registered = [.. _services.UserDictionary?.Lookup(lower) ?? [], .. _settings().ConsonantAbbreviations ? _services.Candidates?.Lookup(lower) ?? [] : []];
            var reading = Builder.RomajiReading(typedRaw);
            List<string> romaji = reading is null ? [] : Builder.JapaneseCandidates(reading, null, _converter);
            List<string> english = [typedRaw, .. Builder.EnglishCandidates(typedRaw)];
            _clauses = [new Clause(typedRaw, true, preferJapanese
                ? ClauseBuilder.Distinct([.. registered, .. romaji, .. english])
                : ClauseBuilder.Distinct([.. registered, .. english, .. romaji])) { Raw = typedRaw, Kana = reading }];
            _selectedClause = 0;
            _converting = true;
            return;
        }
        // 区切りの別案があれば、別案ごとに変換した文を 1 つの文節の候補にする (issue #1737)
        var alternatives = utility is null && !asKana ? _text.ConversionSegmentAlternatives(MaxSegmentationAlternatives) : [segments];
        var clauses = Builder.BuildClauses(alternatives[0], preferJapanese, _converter);
        if (clauses.Count == 0) return;
        if (alternatives.Count > 1 && Builder.WholeSentenceClause([clauses, .. Builder.OtherAlternatives(alternatives, preferJapanese)]) is { } whole)
            clauses = [whole];
        if (utility is not null) clauses = [Builder.WithUtility(clauses, utility, segments[0].Kana)];
        // 変換したあとに続けて打っていたら、前に選んでいた候補を同じ読みの文節で使う
        foreach (var clause in clauses)
            if (_keptChoices.TryGetValue(clause.Reading, out var kept) && clause.Candidates.Contains(kept)) Prefer(clause, kept);
        // 打ち始めの ・ は、Space で / にする。
        if (_text.IsSlashAtStart && clauses is [var slash]) Prefer(slash, "/");
        _clauses = clauses;
        _selectedClause = _clauses.Count - 1;
        _converting = true;
    }

    // 変換の候補に出す英日の区切りの案の数 (いちばん良い案を含む)
    private const int MaxSegmentationAlternatives = 4;

    /// <summary>文全体の文節の候補の後ろに、文節が 1 つの案のその文節の候補を足す。</summary>
    /// <summary>入力の全体に一致したユーティリティ変換の候補。一致しなければ null。</summary>
    private UtilityCandidates? Utilities(IReadOnlyList<CompositionSegment> segments)
    {
        var reading = segments is [{ IsEnglish: false, Kana.Length: > 0 } only] ? only.Kana : null;
        var context = new UtilityContext(_settings(), _services.Now(), _services.UtcNow(), _services.LocalTimeZone(), Clipboard, NowPlaying, _services.GitHubTitles());
        return UtilityConversions.Convert(new UtilityInput(_text, reading, _services.DirectMode(), context));
    }

    internal static void AddSingleClauseCandidates(Clause whole)
    {
        foreach (var clauses in whole.Alternatives!.Values)
        {
            if (clauses is not [var only]) continue;
            foreach (var candidate in only.Candidates)
                if (!whole.Candidates.Contains(candidate)) whole.Candidates.Add(candidate);
        }
    }

    /// <summary>
    /// 文全体の文節 (<see cref="Clause.Alternatives"/>) を、選んでいる案の文節に分ける。文節ごとに候補を選び直す・区切りを動かす・確定する前に呼ぶ。
    /// 文節が 1 つの案の候補を選んでいれば、その文節でその候補を選ぶ。
    /// 文全体の文節でなければ何もしない。どの案にも無い文 (打ち始めの ・ を Space で / にした文) を選んでいれば、分けずに学習しない文節にする。
    /// 分けたら、後から届いた変換エンジンの答えで文全体の文節に戻さない (_clausesEdited)。
    /// </summary>
    private void SplitWholeSentence()
    {
        if (_clauses is not [{ Alternatives: { } alternatives } whole]) return;
        if (alternatives.TryGetValue(whole.Text, out var clauses))
        {
            _clauses = clauses;
        }
        else if (alternatives.Values.FirstOrDefault(c => c is [var only] && only.Candidates.Contains(whole.Text)) is [var single])
        {
            single.Index = single.Candidates.IndexOf(whole.Text);
            single.Changed = whole.Changed;
            _clauses = [single];
        }
        else
        {
            whole.NotLearned.Add(whole.Text);
            return;
        }
        _selectedClause = 0;
        _clausesEdited = true;
    }

    /// <summary>
    /// 次に空の変換ボックスへ入れる / は、文章の入力欄の打ち始め (先頭か空白の直後) に打ったもの。・ にする。
    /// フロントが、その / を本体に渡す前に呼ぶ。この入力の間は変えない (BackSpace で打ち直しても ・ のまま)。
    /// </summary>
    public void MarkSlashAtStart() => _text.SlashAtStart = true;

    /// <summary>
    /// 確定し直しで英語に戻さない、する活用のローマ字。
    /// sure は make sure で英文に直す必要があるので入れない (すれ と読めても除外)。
    /// </summary>
    private static readonly HashSet<string> SuruRomajiKeepJapanese =
        ["site", "sita", "suru", "shite", "shita", "shimasu", "shinai", "shiyou", "shiro", "sezu"];

    private bool IsSuruRomajiKeepJapanese(string raw)
    {
        var lower = raw.ToLowerInvariant();
        if (!SuruRomajiKeepJapanese.Contains(lower)) return false;
        var reading = _detector.Romaji.ConvertLenient(lower, final: true);
        return CompositionDetector.IsSuruForm(reading);
    }

    internal static bool IsKana(char c) => c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー';

    internal static void Prefer(Clause clause, string text)
    {
        clause.Candidates.Remove(text);
        clause.Candidates.Insert(0, text);
        clause.Index = 0;
    }

    /// <summary>日本語の候補 (かな・漢字) か。英語の文節で選ばれたら、その語を日本語として覚える。</summary>
    internal static bool IsJapaneseText(string text) => text.Any(c => c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿');

    /// <summary>候補の一覧の次 (前) のページの最初の候補を選ぶ。最後のページの次は最初のページに戻る。</summary>
    private void NextCandidatePage(int step)
    {
        var clause = _clauses[_selectedClause];
        if (!clause.IsEnglish && !clause.Expanded) Expand(clause);
        var pages = (clause.Candidates.Count + CandidatePageSize - 1) / CandidatePageSize;
        if (pages <= 1) return;
        var page = (Math.Max(0, clause.Index) / CandidatePageSize + step + pages) % pages;
        clause.Index = page * CandidatePageSize;
        clause.Changed = true;
    }

    private void NextCandidate(int step)
    {
        var clause = _clauses[_selectedClause];
        if (!clause.Expanded) Expand(clause);
        clause.Index = (clause.Index + step + clause.Candidates.Count) % clause.Candidates.Count;
        clause.Changed = true;
    }

    /// <summary>
    /// 候補の一覧で、ユーザー辞書・同梱の辞書 (candidates.txt・brands.txt) の語のうち、読み (かな) の候補より前にある最後のものの位置。
    /// 無ければ 0 (最初の候補)。読みより後ろに回した辞書の語 (記号の 、 など) は数えない。
    /// </summary>
    private int DictionaryWordsEnd(Clause clause, string reading)
    {
        var kana = clause.Candidates.IndexOf(reading);
        var limit = kana < 0 ? clause.Candidates.Count : kana;
        var words = (_services.UserDictionary?.Lookup(reading) ?? []).Concat(_services.Candidates?.LookupWords(reading) ?? []);
        return Math.Max(0, words.Select(word => clause.Candidates.IndexOf(word)).Where(index => index < limit).DefaultIfEmpty(-1).Max());
    }

    /// <summary>
    /// 変換中に文節を選んだときに、変換エンジンの候補の一覧 (はし → 橋 端 箸 …) を足す。
    /// 日本語の文節は、最初の候補とユーザー辞書・同梱の辞書の語の後ろに足す (DictionaryWordsEnd)。
    /// 英語の文節は、ローマ字として読んだかなで一覧を引き、かなの候補 (go → ご) の後ろに足す。
    /// 打つたびには呼ばない (時間がかかることがあるため。ライブ変換では呼ばない)。今選んでいる候補はそのまま選んだ状態にする。
    /// </summary>
    private void Expand(Clause clause)
    {
        clause.Expanded = true;
        // 文全体の文節は、文節が 1 つの案のその文節の一覧を足す
        if (clause.Alternatives is { } alternatives)
        {
            foreach (var only in alternatives.Values.Where(c => c.Count == 1).Select(c => c[0]).Where(c => !c.Expanded)) Expand(only);
            AddSingleClauseCandidates(clause);
            return;
        }
        if (_services.MoreCandidates is not { } more) return;
        var reading = clause.IsEnglish ? Builder.RomajiReading(clause.Reading) : clause.Reading;
        if (reading is null) return;
        // 変換エンジンの候補は、日本語の文節では、最初の候補とユーザー辞書・同梱の辞書の語の後ろに入れる。
        // 最初の候補のすぐ後ろに入れると、変換エンジンが知っている読みでは、辞書の語が候補の一覧の後ろに押しやられる (saiki → PSYQUI が 58 件中 51 番目: issue #1681)
        var anchor = clause.IsEnglish ? clause.Candidates.IndexOf(reading) : Math.Max(DictionaryWordsEnd(clause, reading), clause.Leading - 1);
        if (anchor < 0) return;
        var current = clause.Text;
        var merged = Builder.DistinctShown(clause.Compose, [.. clause.Candidates.Take(anchor + 1), .. more(reading).Select(c => NormalizeHalfWidth(c, reading)), .. clause.Candidates.Skip(anchor + 1)]);
        clause.Candidates = merged;
        clause.Index = Math.Max(0, merged.IndexOf(current));
    }

    /// <summary>
    /// 変換エンジンは読みの中の英数字を全角にする (2025ねん → ２０２５年、sだけが → ｓだけが) ので、
    /// 半角で打った数字・英字は半角に戻す。読みに半角の英数字が無ければ (全角を選んだ候補など) そのまま。
    /// </summary>
    internal static string NormalizeHalfWidth(string converted, string reading)
    {
        var digits = reading.Any(char.IsAsciiDigit);
        var letters = reading.Any(char.IsAsciiLetter);
        if (!digits && !letters) return converted;
        var chars = converted.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (digits && chars[i] is >= '０' and <= '９') chars[i] = (char)(chars[i] - 0xFEE0);
            else if (letters && chars[i] is >= 'Ａ' and <= 'Ｚ' or >= 'ａ' and <= 'ｚ') chars[i] = (char)(chars[i] - 0xFEE0);
        }
        return new string(chars);
    }

    /// <summary>
    /// Shift+→ / Shift+←: 選択中の文節を 1 文字伸ばす / 縮める。はみ出した・空いた分は次の文節とやり取りし、
    /// 変わった文節は読みから変換し直す。
    /// 英語の文節との区切りを動かすときは、英語の文節を打ったローマ字のかなで日本語の文節に読み直してからやり取りする
    /// (みー|thin|ぐ の みー を伸ばす → みーて|ぃん|ぐ)。縮めて空いた分は、後ろが英語の文節なら英語の文節はそのままにして間に新しい文節を作る。
    /// </summary>
    private void Resize(int delta)
    {
        _clausesEdited = true;
        SplitWholeSentence();
        var current = _clauses[_selectedClause];
        // ユーティリティ変換の Replace の文節 (空の括弧のペアなど) は、1 つの単位として選んで確定する。
        if (current.Fixed) return;
        var next = _selectedClause + 1 < _clauses.Count ? _clauses[_selectedClause + 1] : null;
        // 区切りを動かせないときは、英語の文節を読み直さずにそのままにする
        if (current.IsEnglish && !CanReadAsJapanese(current)) return;
        if (delta > 0 && (next is null || next.IsEnglish && !CanReadAsJapanese(next))) return;
        if (delta < 0 && (current.IsEnglish ? current.Kana! : current.Reading).Length <= 1) return;

        if (current.IsEnglish)
        {
            ReadAsJapanese(_selectedClause);
            current = _clauses[_selectedClause];
        }
        if (next is { IsEnglish: true })
        {
            if (delta < 0) next = null;
            else
            {
                ReadAsJapanese(_selectedClause + 1);
                next = _clauses[_selectedClause + 1];
            }
        }

        if (delta > 0)
        {
            current.Reading += next!.Reading[0];
            next.Reading = next.Reading[1..];
            if (next.Reading.Length == 0) _clauses.Remove(next);
            else Reconvert(next);
        }
        else
        {
            var moved = current.Reading[^1];
            current.Reading = current.Reading[..^1];
            if (next is null)
            {
                next = new Clause(moved.ToString(), false, []);
                _clauses.Insert(_selectedClause + 1, next);
            }
            else next.Reading = moved + next.Reading;
            Reconvert(next);
        }
        Reconvert(current);
        RestoreRaw();
    }

    /// <summary>
    /// 文節の区切りを動かすと Reconvert が Raw を消すので、文節ごとの打った英字を取り直す (F9 / F10 が選んだ文節だけに効くのに要る)。
    /// 英語の文節は Kana (打った英字をローマ字として読んだかな) の長さ分、日本語の文節は Reading の長さ分を、文全体のかなの上で数える。
    /// 英語の文節に Kana が無い (保護区間など) ときは位置が決まらないので、取り直さない。
    /// </summary>
    private void RestoreRaw()
    {
        if (_clauses.Any(c => c.IsEnglish && c.Kana is null)) return;
        var raws = _text.RawForKanas([.. _clauses.Select(c => c.IsEnglish ? c.Kana! : c.Reading)]);
        for (var i = 0; i < _clauses.Count; i++)
        {
            if (!_clauses[i].IsEnglish) _clauses[i].Raw = raws[i];
        }
    }

    /// <summary>英語の文節を、打ったローマ字のかなを読みにした日本語の文節に置き換える。かなに読めない英字だけ (xyz) なら置き換えずに false。</summary>
    private bool ReadAsJapanese(int index)
    {
        if (!CanReadAsJapanese(_clauses[index])) return false;
        var clause = new Clause(_clauses[index].Kana!, false, []);
        Reconvert(clause);
        _clauses[index] = clause;
        return true;
    }

    private static bool CanReadAsJapanese(Clause clause) => clause is { IsEnglish: true, Kana: { } kana } && kana.Any(IsKana);

    private void Reconvert(Clause clause)
    {
        clause.Candidates = Builder.JapaneseCandidates(clause.Reading, null, _converter);
        clause.Index = 0;
        clause.Changed = false;
        // 前の読みのもの。候補の一覧は、文節を選んだときに新しい読みで取り直す
        clause.Expanded = false;
        clause.Raw = null;
        clause.Translations.Clear();
        if (_learning.LearnedConversion(clause.Reading) is { } learned) Prefer(clause, learned);
        Builder.KeepTypedSymbols([clause], clause.Reading);
    }

    /// <summary>
    /// ライブ変換: Space を押したときと同じく、文脈・手がかり辞書・学習を使って文節ごとに変換した結果を見せる。
    /// 打つたびに呼ばれるので、かな・文脈・学習の状態が同じならキャッシュを使う。
    /// </summary>
    private ComposedText LiveConvert(string kana, string before)
    {
        // 記号 (～ ! ? …) は数えない。かなが短いうちに記号を足しただけで、的外れな漢字にしない (な～～～ が 名～～～ になっていた: issue #505)
        if (!CountsAsLive(kana)) return ComposedText.Kana(kana);
        return ComposedText.Concat(LiveClauses(kana, before).Select(c => ComposedText.Converted(c.Reading, c.Text)));
    }

    /// <summary>
    /// ライブ変換するだけの拍があるか。小書きの母音・拗音は直前のかなと合わせて 1 拍 (きょ・ふゅ・しぃ は 1 拍)、っ・ん・ー は 1 拍と数える。
    /// 1 拍の拗音・外来音を、打っている途中に変換しない (きょ が 虚、ふゅ が フュ になっていた)。
    /// </summary>
    private static bool CountsAsLive(string kana) =>
        kana.Where((c, i) => IsKana(c) && !(i > 0 && KanaChars.IsSmallKana(c) && IsKana(kana[i - 1]))).Count() >= LiveConversionMinLength;

    /// <summary>ライブ変換の文節 (読みと変換した文字)。予測変換で最後の文節を探すときも、同じ打鍵で変換した結果を使う。</summary>
    private List<(string Reading, string Text)> LiveClauses(string kana, string before)
    {
        var key = string.Join("\u0001", kana, before, _precedingText, Builder.EnglishInComposition(), _followingText, _services.History?.Version);
        if (_liveCache.TryGetValue(key, out var cached)) return cached;
        var clauses = WithoutWaiting(() => Builder.ConvertJapaneseSegment(kana, before, _converter).Select(c => (c.Reading, c.Text)).ToList());
        if (_liveCache.Count > 256) _liveCache.Clear();
        _liveCache[key] = clauses;
        return clauses;
    }

    private T WithoutWaiting<T>(Func<T> convert) => _budget is null ? convert() : _budget.WithLimit(0, convert);

    /// <summary>
    /// 選択中の確定済みの文字と、その読み。ローマ字のまま入った文 (kyouhaiitenki) は、読みにせずにそのまま渡す (Meltype の判定で日本語にする: issue #288)。
    /// 読みの句読点は 、。 にする。
    /// </summary>
    private ReconversionSelection? ReconversionSelectionNow()
    {
        if (Selection is not { Length: > 0 } text) return null;
        var reading = IsRomajiText(text) ? text : ReadingOf(text) is { } read ? CompositionOutput.FromSetting(_settings().Punctuation, read) : null;
        return reading is { Length: > 0 } ? new ReconversionSelection(text, reading) : null;
    }
}
