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
                if (kana.Length > 0) output.Append(Convert(kana.ToString()));
                kana.Clear();
                if (output.Length > 0 && output[^1] != ' ') output.Append(' ');
                output.Append(segment.Raw).Append(' ');
            }
            englishWords = segments.Count > 0 && segments[^1].IsEnglish ? (segments.Count == 1 ? englishWords + 1 : 1) : 0;
        }
        if (kana.Length > 0) output.Append(Convert(kana.ToString()));
        return output.ToString().TrimEnd();
    }

    /// <summary>
    /// 英語区間はそのまま、日本語区間は文脈を付けて変換エンジンで文節に区切って変換する。
    /// 英語区間も、ローマ字として読めれば日本語の候補 (go → 語 ご ゴ) を後ろに足す。preferJapanese (Shift+Space) なら前に出す。
    /// </summary>
    private void StartConversion(bool preferJapanese = false)
    {
        _conversionPrefersJapanese = preferJapanese;
        _clausesEdited = false;
        _tabStartedConversion = false;
        var segments = _text.ConversionSegments();
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
            var reading = RomajiReading(typedRaw);
            List<string> romaji = reading is null ? [] : JapaneseCandidates(reading, null);
            List<string> english = [typedRaw, .. EnglishCandidates(typedRaw)];
            _clauses = [new Clause(typedRaw, true, preferJapanese
                ? Distinct([.. registered, .. romaji, .. english])
                : Distinct([.. registered, .. english, .. romaji])) { Raw = typedRaw, Kana = reading }];
            _selectedClause = 0;
            _converting = true;
            return;
        }
        var clauses = new List<Clause>();
        for (var s = 0; s < segments.Count; s++)
        {
            var segment = segments[s];
            if (segment.IsEnglish)
            {
                if (segment.IsProtected && !preferJapanese)
                {
                    clauses.Add(new Clause(segment.Raw, true, [segment.Raw]));
                    continue;
                }
                var english = Distinct([.. _services.UserDictionary?.Lookup(segment.Raw.ToLowerInvariant()) ?? [], .. EnglishCandidates(segment.Raw)]);
                // 子音の略入力 (ast → 明日): 英字のまま見える語にも、candidates.txt の英字の読みの候補を足す (issue #62)
                if (_settings().ConsonantAbbreviations && segment.Raw.Length >= 2 && segment.Raw.All(char.IsAsciiLetter) && _services.Candidates is { } abbreviations)
                {
                    foreach (var word in abbreviations.Lookup(segment.Raw.ToLowerInvariant()))
                    {
                        if (!english.Contains(word)) english.Add(word);
                    }
                }
                // 記号だけの英語区間にも、同梱の記号候補 (... → …) を出す。
                if (segment.Raw.All(c => !char.IsLetterOrDigit(c)))
                    english = Distinct([.. english, .. _services.Candidates?.Lookup(segment.Raw) ?? []]);
                var reading = RomajiReading(segment.Raw);
                List<string> romaji = reading is null ? [] : JapaneseCandidates(reading, null);
                clauses.Add(new Clause(segment.Raw, true, preferJapanese && romaji.Count > 0
                    ? Distinct([.. romaji, .. english])
                    : Distinct([.. english, .. romaji]))
                { Kana = _text.KanaForSegment(s) });
                continue;
            }
            if (segment.Kana.Length == 0) continue;
            var japanese = ConvertJapanese(segment.Kana);
            // 英単語 + する (push|した、deploy|しよう): 変換エンジンは英語の後ろの「した」だけを見て 下・死体・使用 にしてしまう。かなのまま
            if (s > 0 && segments[s - 1].IsEnglish && japanese.Count > 0 && IsSuruAfterEnglish(japanese[0])) Prefer(japanese[0], japanese[0].Reading);
            // 候補の最後に、打ったままの英字 (あぴ → api) と全角の英字も出す (Space を連打して英字に戻せる)。
            var offset = 0;
            foreach (var clause in japanese)
            {
                clause.Raw = _text.RawForReading(s, offset, clause.Reading.Length);
                offset += clause.Reading.Length;
                AddOldKana(clause);
                AddTranslations(clause);
                AddRawCandidates(clause);
                MoveEmojiLast(clause);
            }
            clauses.AddRange(japanese);
        }
        if (clauses.Count == 0) return;
        if (utility is not null) clauses = [WithUtility(clauses, utility, segments[0].Kana)];
        // 変換したあとに続けて打っていたら、前に選んでいた候補を同じ読みの文節で使う
        foreach (var clause in clauses)
            if (_keptChoices.TryGetValue(clause.Reading, out var kept) && clause.Candidates.Contains(kept)) Prefer(clause, kept);
        // 打ち始めの ・ は、Space で / にする。
        if (_text.IsSlashAtStart && clauses is [var slash]) Prefer(slash, "/");
        _clauses = clauses;
        _selectedClause = 0;
        _converting = true;
    }

    /// <summary>入力の全体に一致したユーティリティ変換の候補。一致しなければ null。</summary>
    private UtilityCandidates? Utilities(IReadOnlyList<CompositionSegment> segments)
    {
        var reading = segments is [{ IsEnglish: false, Kana.Length: > 0 } only] ? only.Kana : null;
        var context = new UtilityContext(_settings(), _services.Now(), _services.UtcNow(), _services.LocalTimeZone(), Clipboard, NowPlaying, _services.GitHubTitles());
        return UtilityConversions.Convert(new UtilityInput(_text, reading, _services.DirectMode(), context));
    }

    /// <summary>
    /// 読み全体を 1 つの文節にして、普通の変換の候補のかなの後ろ (絵文字・打ったままの英字より前) に、ユーティリティ変換の候補を足す。
    /// 変換エンジンが文節を分けたら (じこうの|あいさつ)、読み全体をその文節だけで変換し直す。
    /// </summary>
    private Clause WithUtility(List<Clause> clauses, UtilityCandidates utility, string reading)
    {
        var clause = clauses is [var only] && only.Reading == reading ? only : WholeClause(clauses, reading);
        var katakana = CompositionText.ToKatakana(reading);
        var at = new[] { reading, katakana, CompositionText.ToHalfWidthKatakana(katakana) }.Max(clause.Candidates.IndexOf) + 1;
        if (at == 0) at = clause.Candidates.Count;
        clause.NotLearned.UnionWith(utility.NotLearned);
        foreach (var text in utility.Candidates)
        {
            if (clause.Candidates.Contains(text)) continue;
            clause.Candidates.Insert(at++, text);
        }
        return clause;
    }

    private Clause WholeClause(List<Clause> clauses, string reading)
    {
        var clause = new Clause(reading, false, JapaneseCandidates(reading, string.Concat(clauses.Select(c => c.Text)))) { Raw = _text.Raw };
        // 文の中の文節と同じく、文脈の手がかりか、前に選び直した語を最初の候補にする (ConvertWithEngine)
        var surrounding = (_precedingText ?? "") + (_followingText ?? "");
        if ((_services.ContextRules?.Choose(reading, surrounding, clause.Text, Standalone) ?? _learning.LearnedConversion(reading)) is { } preferred)
            Prefer(clause, preferred);
        AddOldKana(clause);
        AddTranslations(clause);
        AddRawCandidates(clause);
        MoveEmojiLast(clause);
        return clause;
    }

    /// <summary>
    /// 次に空の変換ボックスへ入れる / は、文章の入力欄の打ち始め (先頭か空白の直後) に打ったもの。・ にする。
    /// フロントが、その / を本体に渡す前に呼ぶ。この入力の間は変えない (BackSpace で打ち直しても ・ のまま)。
    /// </summary>
    public void MarkSlashAtStart() => _text.SlashAtStart = true;

    /// <summary>
    /// 英訳の候補 (複雑な → complex, complicated …) を、日本語の候補の後ろに足す。
    /// 選んだことのある英訳は前に出す: 1 回なら英訳の中の先頭、2 回以上なら 2 番目 (変換エンジンの 1 番目の候補は動かさない)。
    /// </summary>
    private void AddTranslations(Clause clause)
    {
        if (clause.IsEnglish || _services.Translations is not { } dictionary || !_settings().TranslationCandidates) return;
        var words = dictionary.Lookup(clause.Text, clause.Reading).Where(w => !clause.Candidates.Contains(w)).ToList();
        if (words.Count == 0) return;
        var learned = _services.TranslationHistory?.Get(clause.Reading) ?? [];
        // 選んだ回数の多い順に前へ
        words = learned.Select(l => l.Word).Where(words.Contains).Concat(words.Where(w => !learned.Any(l => l.Word == w))).ToList();
        foreach (var word in words) clause.Translations.Add(word);
        var often = learned.FirstOrDefault(l => l.Count >= 2 && words.Contains(l.Word)).Word;
        if (often is not null)
        {
            clause.Candidates.Insert(Math.Min(1, clause.Candidates.Count), often);
            words.Remove(often);
        }
        clause.Candidates.AddRange(words);
    }

    /// <summary>打った英字を英語寄りに区切り直して、英語と日本語が混ざる形 (midiか) になれば返す。</summary>
    private string? EnglishSplit(string raw)
    {
        var typed = new CompositionText(_detector);
        foreach (var c in raw) typed.Append(c);
        if (typed.Pending.Length > 0) return null;
        var segments = _detector.SegmentLeaningEnglish(typed.Units);
        if (!segments.Any(s => s.IsEnglish) || segments.All(s => s.IsEnglish)) return null;
        return string.Concat(segments.Select(s => s.IsEnglish ? s.Raw : s.Kana));
    }

    private void AddRawCandidates(Clause clause)
    {
        // 子音の略入力 (ast → 明日): 打った英字を読みとする候補 (candidates.txt の英字の読み) を足す (issue #62)
        if (_settings().ConsonantAbbreviations && clause.Raw is { Length: >= 2 } abbreviation && abbreviation.All(char.IsAsciiLetter) && _services.Candidates is { } abbreviations)
        {
            foreach (var word in abbreviations.Lookup(abbreviation.ToLowerInvariant()))
            {
                if (!clause.Candidates.Contains(word)) clause.Candidates.Add(word);
            }
        }
        // 入力中の表示が半角の記号 (* が 2 個以上・---) のときは、最初の候補も同じ半角にする。
        if (_text.Mode == DisplayMode.Auto && clause.Candidates.Count > 0 &&
            _text.AsDisplayedHalfWidth(clause.Candidates[0], clause.Raw) is var shown && shown != clause.Candidates[0])
        {
            clause.Candidates.Remove(shown);
            clause.Candidates.Insert(0, shown);
        }
        // 全角にした記号 (＃ （ ％) は、半角に戻した候補も出す (Space を続けて押すと半角の # ( %)。
        if (clause.Candidates.Count > 0 && CompositionText.SymbolsToHalfWidth(clause.Candidates[0]) is var half && half != clause.Candidates[0] &&
            !clause.Candidates.Contains(half))
        {
            clause.Candidates.Add(half);
        }
        if (clause.Raw is not { } raw || !raw.Any(c => c is >= '!' and <= '~')) return;
        // 英語を含む区切り (midika → 身近 の候補に midiか)
        if (raw.All(char.IsAsciiLetter) && EnglishSplit(raw) is { } split && !clause.Candidates.Contains(split)) clause.Candidates.Add(split);
        foreach (var candidate in new[] { raw, CompositionText.ToFullWidth(raw) })
        {
            if (!clause.Candidates.Contains(candidate)) clause.Candidates.Add(candidate);
        }
    }

    /// <summary>
    /// wi / we で打った うぃ / うぇ は、ゐ / ゑ (ヰ / ヱ) にした候補も出す (うぃすきー → ゐすきー、ヰスキー)。
    /// 候補の中の うぃ ウィ うぇ ウェ を置き換えた形を、打ったままの英字の候補より前に足す。
    /// </summary>
    private static void AddOldKana(Clause clause)
    {
        if (clause.IsEnglish || clause.Raw is not { } raw) return;
        var lower = raw.ToLowerInvariant();
        if (!lower.Contains("wi") && !lower.Contains("we")) return;
        if (!clause.Reading.Contains("うぃ") && !clause.Reading.Contains("うぇ")) return;
        static string Old(string text) => text.Replace("うぃ", "ゐ").Replace("うぇ", "ゑ").Replace("ウィ", "ヰ").Replace("ウェ", "ヱ");
        var added = new List<string>();
        foreach (var candidate in clause.Candidates.Concat([clause.Reading, CompositionText.ToKatakana(clause.Reading)]))
        {
            var old = Old(candidate);
            if (old != candidate && !clause.Candidates.Contains(old) && !added.Contains(old)) added.Add(old);
        }
        clause.Candidates.AddRange(added);
    }

    /// <summary>
    /// ユーザー辞書の読みが含まれていれば、その部分は登録した単語の文節にし、残りだけを変換エンジンで変換する
    /// (きごうとうふくめ → 記号等|含め)。含まれていなければ普通に変換する。
    /// </summary>
    private List<Clause> ConvertJapanese(string kana)
    {
        // ローマ字として読めずに残った英字 (こほぃc の c) と半角の記号 (... @) は、変換エンジンに渡すと別の記号 (© ．．． ＠) にされるので、
        // そのままの文節にする。数字は、記号・英字とつながっていればそのまま (3.0 の 3 を 1 文字だけ渡すと ❸ にされる)、
        // それ以外は変換エンジンに渡す (2026ねん → 2026年)。
        var keep = new bool[kana.Length];
        for (var i = 0; i < kana.Length; i++) keep[i] = kana[i] is >= '!' and <= '~' && !char.IsAsciiDigit(kana[i]);
        for (var i = 0; i < kana.Length; i++)
        {
            if (!char.IsAsciiDigit(kana[i])) continue;
            var end = i;
            while (end < kana.Length && char.IsAsciiDigit(kana[end])) end++;
            var touches = (i > 0 && keep[i - 1]) || (end < kana.Length && keep[end]);
            for (var k = i; k < end; k++) keep[k] = touches;
            i = end - 1;
        }
        if (keep.Any(k => k))
        {
            var result = new List<Clause>();
            var start = 0;
            for (var i = 1; i <= kana.Length; i++)
            {
                if (i < kana.Length && keep[i] == keep[start]) continue;
                var run = kana[start..i];
                // ユーザー辞書に英字のまま登録した読み (zh → ←) はここでも引く
                if (keep[start]) result.Add(new Clause(run, false, Distinct([.. _services.UserDictionary?.Lookup(run) ?? [], run, .. _services.Candidates?.Lookup(run) ?? [], CompositionText.ToFullWidth(run)])));
                else result.AddRange(ConvertJapanese(run));
                start = i;
            }
            return result;
        }
        if (_services.UserDictionary?.Split(kana) is not { } split) return ConvertWithEngine(kana);
        var pieces = AlignUserWords(kana, split);
        if (pieces.All(p => p.Word is null && p.Engine is null)) return ConvertWithEngine(kana);
        var clauses = new List<Clause>();
        var registered = new List<Clause>();
        var parts = new List<Clause>();
        foreach (var (reading, word, engine) in pieces)
        {
            if (word is null)
            {
                clauses.AddRange(ConvertWithEngine(reading, engine));
                continue;
            }
            // 同梱の語句に文節の区切りがあれば、その区切りで分ける (いちがっき → 一|学期)
            var divided = _services.UserDictionary!.Clauses(reading, word);
            foreach (var (part, text) in divided)
            {
                var clause = new Clause(part, false, JapaneseCandidates(part, text));
                clauses.Add(clause);
                // 区切りで分けた文節 (一|学期 の 一) は、学習と文脈の手がかりで選び直さない (いち で 市 を選んだことがあっても 一|学期 のまま)
                (divided.Count == 1 ? registered : parts).Add(clause);
            }
        }
        // 同梱の語句 (しょせん → 所詮) も、文脈の手がかり (試合 → 初戦) と学習で選び直せるようにする。
        // 変換エンジンの文節も、語句の文節を含めた前後で文脈の手がかりを見直す (甲斐性ない + こうかい → 後悔)。
        foreach (var clause in clauses)
        {
            if (parts.Contains(clause)) continue;
            var surrounding = (_precedingText ?? "") + string.Concat(clauses.Where(c => c != clause).Select(c => c.Text)) + (_followingText ?? "");
            var preferred = _services.ContextRules?.Choose(clause.Reading, surrounding, clause.Text, Standalone) ?? (registered.Contains(clause) ? _learning.LearnedConversion(clause.Reading) : null);
            if (preferred is not null) Prefer(clause, preferred);
        }
        return clauses;
    }

    /// <summary>
    /// ユーザーが登録した語の読みが、変換エンジンの文節の途中から始まるなら、その語では区切らない (issue #374)。
    /// しの → 紫乃 を登録すると、わたしのことがすき の途中の しの で区切って ワタ紫乃ことが好き になっていた
    /// (変換エンジンは わたしの|ことが|すき と区切るので、しの は文節の途中から始まる)。文節の頭から始まる語 (しのがきた の しの) は今までどおり。
    /// 同梱の語句 (phrases.txt) は、変換エンジンが区切りを間違える語句を補うものなので、文節の途中から始まっても使う
    /// (読みの頭に ^ を付けた語句は、ほかの語の一部になりやすいので、文節の頭から始まるときだけ使う)。
    ///
    /// 語の始まりが変換エンジンの文節の境目に合い、そこからの文節の文字が語で始まる (変換エンジンが文の中でその語にできている) なら、
    /// その語では区切らず、語から後ろは文全体を変換したときの変換エンジンの文節をそのまま使う (Engine)。
    /// 語で区切ると、残りのかなを単独で変換することになり崩れる (あげあしとら → 揚げ足取ら で区切ると、ないで が 内で になる)。
    /// 語より前は今までどおり別に変換する (文全体の変換エンジンの結果にすると、これほんあか が コレ本垢 になる)。
    /// 始まりが文節の途中の語は、どの文字が語の読みに当たるか分からないので、今までどおり語で区切る。
    /// </summary>
    private List<(string Reading, string? Word, IReadOnlyList<ConversionClause>? Engine)> AlignUserWords(string kana, List<(string Reading, string? Word)> pieces)
    {
        var unchanged = pieces.Select(p => (p.Reading, p.Word, (IReadOnlyList<ConversionClause>?)null)).ToList();
        if (_services.UserDictionary is not { } dictionary) return unchanged;
        var engine = ConversionContext() is { } context ? ConvertWithContext(kana, context) : _converter.ConvertClauses(kana);
        if (engine is null || string.Concat(engine.Select(c => c.Reading)) != kana) return unchanged;
        var starts = new HashSet<int>();
        for (int i = 0, offset = 0; i < engine.Count; offset += engine[i].Reading.Length, i++) starts.Add(offset);
        var aligned = new List<(string Reading, string? Word, IReadOnlyList<ConversionClause>? Engine)>();
        var position = 0;
        for (var p = 0; p < pieces.Count; p++)
        {
            var (reading, word) = pieces[p];
            var midStart = word is not null && (dictionary.IsUserReading(reading) || dictionary.IsHeadOnly(reading)) && !starts.Contains(position);
            if (midStart) Diagnostics.Log.Info($"ユーザー辞書の語は、変換エンジンの文節の途中から始まるので使いません: {Diagnostics.Log.Text(reading)}");
            if (word is not null && !midStart && starts.Contains(position) && EngineStartsWith(position, word))
            {
                // 次に文節の境目から始まる語の手前までを、変換エンジンの文節にする
                var next = p + 1;
                var stop = position + reading.Length;
                for (; next < pieces.Count && !(pieces[next].Word is not null && starts.Contains(stop)); next++) stop += pieces[next].Reading.Length;
                var span = new List<ConversionClause>();
                for (int i = 0, offset = 0; i < engine.Count; offset += engine[i].Reading.Length, i++)
                    if (offset >= position && offset < stop) span.Add(engine[i]);
                // 変換エンジンの文節が次の語にまたがるときは、変換エンジンの結果を使わない
                if (string.Concat(span.Select(c => c.Reading)) == kana[position..stop])
                {
                    aligned.Add((kana[position..stop], null, span));
                    position = stop;
                    p = next - 1;
                    continue;
                }
            }
            // 使わない語の読みは、前後の変換エンジンに渡す部分とつなげる
            if (word is null || midStart)
            {
                if (aligned.Count > 0 && aligned[^1] is { Word: null, Engine: null } last) aligned[^1] = (last.Reading + reading, null, null);
                else aligned.Add((reading, null, null));
            }
            else aligned.Add((reading, word, null));
            position += reading.Length;
        }
        return aligned;

        // start から始まる変換エンジンの文節の文字が、語で始まるか
        bool EngineStartsWith(int start, string word)
        {
            var text = new StringBuilder();
            for (int i = 0, offset = 0; i < engine.Count && text.Length < word.Length; offset += engine[i].Reading.Length, i++)
                if (offset >= start) text.Append(engine[i].Text);
            return text.ToString().StartsWith(word, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 「に」で始まる読みのうち、助詞の「に」に続くことが多い語 (なる・ついて・よって など) で始まるものか。
    /// 「にわ (庭)」「にほん (日本)」のように「に」で始まる語まで仮の文脈で助詞と読ませると、
    /// Zenzai が「に|ワニ|は」のように崩すので、助詞らしい続きのときだけにする。
    /// </summary>
    internal static bool StartsWithParticleNi(string kana)
    {
        if (kana.Length < 3 || kana[0] != 'に') return false;
        var rest = kana.AsSpan(1);
        foreach (var next in ParticleNiContinuations)
        {
            if (rest.StartsWith(next, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static readonly string[] ParticleNiContinuations =
    [
        "なる", "なっ", "なり", "なら", "なれ", "なろ", "ついて", "ついての", "つき", "よっ", "よる", "より", "よれ",
        "とって", "とっての", "たいし", "たいする", "かんし", "かんする", "おいて", "おける", "むけ", "むか", "わたっ", "わたる",
        "あたっ", "あたり", "して", "した", "する", "すぎ", "さえ", "しか", "ちがい", "そって", "そう", "もとづ", "くらべ",
        "かけ", "かわっ", "かわり", "ともな", "つれ", "あわせ", "おうじ", "はいっ", "いく", "いっ", "いれ", "いる", "いた",
        "きた", "くる", "きて", "でき", "みえ", "きこえ", "かんじ", "おもえ",
    ];

    /// <summary>
    /// 日本語のかなを変換エンジンで文節に区切って変換する。各文節の最初の候補は次の順で決める:
    ///   1. 文脈の手がかり辞書 (前後に 気温 があれば あつい → 暑い)
    ///   2. ユーザーが前に選び直した変換 (学習)
    ///   3. 変換エンジンの結果 (入力欄のキャレットの前の文字を文脈として渡している)
    /// engine を渡したときは、変換エンジンに頼まずにその文節を使う (文全体を変換したときの一部: AlignUserWords)。
    /// </summary>
    private List<Clause> ConvertWithEngine(string kana, IReadOnlyList<ConversionClause>? engine = null)
    {
        // 文脈が無いまま「に」で始まる読みを変換すると、変換エンジンは「に」を語の頭と読む (になってしまう → 担ってしまう)。
        // 前の文脈が取れないときは、仮の文脈「これ」を付けて助詞として読ませる (これ + になってしまう → になってしまう)。
        // 「は」「で」なども助詞になりうるが、仮の文脈を付けると はしる → は知る のように崩れるので「に」だけ。
        var context = ConversionContext();
        var parts = engine ?? (context is not null
            ? ConvertWithContext(kana, context)
            : _converter.ConvertClauses(kana, StartsWithParticleNi(kana) ? "これ" : null));
        parts ??= [new ConversionClause(kana, _converter.Convert(kana) ?? kana)];
        // 読み全体が補助辞書・絵文字の辞書の語 (かんがえるかお → 🤔) なら、文節に分けずに 1 つの文節にして候補を出す。
        if (parts.Count > 1 && _services.Candidates?.Contains(kana) == true)
        {
            parts = [new ConversionClause(kana, string.Concat(parts.Select(p => p.Text)))];
        }
        parts = JoinSmallKana(parts);
        var clauses = parts.Select(p => new Clause(p.Reading, false, JapaneseCandidates(p.Reading, NormalizeHalfWidth(p.Text, p.Reading)))
        {
            ZenzaiText = p.Zenzai ? NormalizeHalfWidth(p.Text, p.Reading) : null,
        }).ToList();
        for (var i = 0; i < clauses.Count; i++)
        {
            var others = string.Concat(clauses.Where((_, k) => k != i).Select(c => c.Text));
            var surrounding = (_precedingText ?? "") + others + (_followingText ?? "");
            var preferred = _services.ContextRules?.Choose(clauses[i].Reading, surrounding, clauses[i].Text, Standalone) ?? _learning.LearnedConversion(clauses[i].Reading);
            // 変換エンジンは、文節が「から」だけだと記号 (～) にしてしまう。記号だけの変換結果は、ひらがなの後ろに回す。
            preferred ??= IsSymbolOnly(clauses[i].Text) && clauses[i].Reading.All(KanaChars.IsHiragana) ? clauses[i].Reading : null;
            // 英単語に挟まれて助詞だけの文節になると、変換エンジンは漢字にしてしまう (github + に + push → 二)。助詞はかなのまま。
            preferred ??= JapaneseParticles.IsParticle(clauses[i].Reading) && clauses[i].Text != clauses[i].Reading ? clauses[i].Reading : null;
            // カタカナの語の後ろの「っ」で始まる文節 (スパイダーマ + っ！) は、カタカナの「ッ」にする (スパイダーマッ！)。
            preferred ??= i > 0 && clauses[i].Text.StartsWith('っ') && clauses[i - 1].Text is [.., var last] && last is >= 'ァ' and <= 'ヺ' or 'ー'
                ? "ッ" + clauses[i].Text[1..] : null;
            // 文頭・記号の後ろの「え、」「え？」(聞き返し) を、変換エンジンは 得 にしてしまう (得、知らん)。かなのまま
            preferred ??= IsInterjection(clauses, i) ? clauses[i].Reading : null;
            // がち (ガチで) を、変換エンジンは 勝ち にしてしまう (勝ちでやばい)。勝ち の読みは かち なので、がち は ガチ にする
            preferred ??= clauses[i].Reading.StartsWith("がち", StringComparison.Ordinal) && clauses[i].Text.StartsWith("勝ち", StringComparison.Ordinal)
                ? "ガチ" + clauses[i].Text["勝ち".Length..] : null;
            if (preferred is not null) Prefer(clauses[i], preferred);
        }
        return clauses;
    }

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

    /// <summary>英単語の後ろの、する の活用の文節を、変換エンジンが漢字で始めた (した → 下、したい → 死体、しよう → 使用)。</summary>
    private static bool IsSuruAfterEnglish(Clause clause) =>
        CompositionDetector.SuruForms.Any(clause.Reading.StartsWith) && clause.Text.Length > 0 && !IsKana(clause.Text[0]) && clause.Text != clause.Reading;

    private static readonly HashSet<char> SentencePunctuation = ['、', '。', '，', '．', ',', '.', '！', '？', '!', '?', '…', '‥', '「', '」', '(', ')', '（', '）', ' ', '　'];

    /// <summary>
    /// 文節が「え」だけ (後ろに記号が付いていてもよい) で、文の頭か記号の後ろにあり、後ろが記号か文の終わりか。
    /// こういう「え」は聞き返し・驚き (え、しらん) で、絵 や 得 ではない。
    /// </summary>
    private bool IsInterjection(List<Clause> clauses, int i)
    {
        var reading = clauses[i].Reading;
        if (reading.Length == 0 || reading[0] != 'え' || !reading.Skip(1).All(SentencePunctuation.Contains) || clauses[i].Text == reading) return false;
        var before = i > 0 ? clauses[i - 1].Text : _precedingText ?? "";
        if (before.Length > 0 && !SentencePunctuation.Contains(before[^1])) return false;
        if (reading.Length > 1) return true;
        return i + 1 == clauses.Count || clauses[i + 1].Text is [var next, ..] && SentencePunctuation.Contains(next);
    }

    /// <summary>
    /// 小書きのかな (ぃ ぇ ゃ …) で始まる文節は前の文節とつなげる。小書きのかなから始まる語は無いので、
    /// 変換エンジンの知らない語を区切り間違えたもの (こうぃ → 光|ぃ)。つなげた読みは、その文節だけで変換し直す
    /// (こうぃ → コウィ。wi で打っていれば こゐ も候補に出る)。
    /// </summary>
    private IReadOnlyList<ConversionClause> JoinSmallKana(IReadOnlyList<ConversionClause> parts)
    {
        if (!parts.Skip(1).Any(p => p.Reading.Length > 0 && SmallKana.Contains(p.Reading[0]))) return parts;
        var joined = new List<ConversionClause>();
        foreach (var part in parts)
        {
            if (joined.Count > 0 && part.Reading.Length > 0 && SmallKana.Contains(part.Reading[0]))
            {
                var reading = joined[^1].Reading + part.Reading;
                // 変換しても漢字の後ろに小書きのかなが残る (光ぃ) なら、カタカナ (コウィ) にする。
                var text = Convert(reading);
                if (Enumerable.Range(1, Math.Max(0, text.Length - 1)).Any(i => SmallKana.Contains(text[i]) && !IsKana(text[i - 1]))) text = CompositionText.ToKatakana(reading);
                joined[^1] = new ConversionClause(reading, text);
            }
            else joined.Add(part);
        }
        return joined;
    }

    private const string SmallKana = "ぁぃぅぇぉゃゅょゎァィゥェォャュョヮ";

    private static bool IsKana(char c) => c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー';

    /// <summary>
    /// 文脈付きと文脈なしの両方で変換し、文節の区切りが同じなら文脈付き (漢字の選び方だけが文脈で変わる: この本は + あつい → 厚い)、
    /// 区切りまで変わるなら文脈なしを使う。前に同じ語があると、変換エンジンは文脈に引きずられて区切りを変えてしまうため
    /// (「記号等」含め、 + きごうとう → き|ごうとう → 気強盗)。
    /// </summary>
    private IReadOnlyList<ConversionClause>? ConvertWithContext(string kana, string context)
    {
        var withContext = _converter.ConvertClauses(kana, context);
        var plain = _converter.ConvertClauses(kana);
        if (withContext is null || plain is null) return withContext ?? plain;
        return withContext.Select(c => c.Reading).SequenceEqual(plain.Select(c => c.Reading)) ? withContext : plain;
    }

    private static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);

    private static void Prefer(Clause clause, string text)
    {
        clause.Candidates.Remove(text);
        clause.Candidates.Insert(0, text);
        clause.Index = 0;
    }

    /// <summary>
    /// 文節の候補: 文の中での変換結果 → その文節だけでの変換結果 → 補助辞書の同音異義語 → ひらがな → 全角カタカナ → 半角カタカナ
    /// → 分野の辞書の語 → 絵文字・顔文字 (逆順)。
    /// 分野の辞書の語 (病名・駅名など) は、使わない人には要らない候補なので、ふだんの候補 (変換エンジンの候補・ひらがな・カタカナ) を
    /// 1 つも後ろに押さないよう、その後ろに並べる。こうすれば誰にとっても困らないので、分野ごとに外す設定は作らない (#1650)。
    /// 絵文字・顔文字は最後に逆順で並べるので、変換してすぐ ↑ を押すと、いちばんよく使う絵文字 (えがお → 😊) になる (issue #133)。
    /// </summary>
    private List<string> JapaneseCandidates(string reading, string? inContext)
    {
        // 文の中での変換結果が無いとき (文節の区切りを変えた・打っている途中の候補) は、その読みだけの変換が最初の候補になるので、Zenzai も使う (Convert)
        var standalone = inContext is null ? Convert(reading) : Standalone(reading);
        var katakana = CompositionText.ToKatakana(reading);
        // ユーザー辞書に登録した単語は、文の中での変換結果の次に出す (文節の区切りを変えて読みが一致したときなど)。
        return DistinctShown([inContext, .. _services.UserDictionary?.Lookup(reading) ?? [], standalone, .. _services.Candidates?.LookupWords(reading) ?? [],
            reading, katakana, CompositionText.ToHalfWidthKatakana(katakana), .. _services.Candidates?.LookupDomain(reading) ?? [], .. EmojiBlock(reading)]);
    }

    /// <summary>
    /// 入力欄に出る文字 (設定の句読点にした文字) が同じ候補は、最初の 1 つだけ残す。「，．」の設定では、変換エンジンが返す 、 と ， が同じ ， になる。
    /// 変換エンジンの候補が入る所 (この関数を呼ぶ所) だけで重複を消す。区間の種類を出口まで渡せば要らなくなる (#1735)。
    /// </summary>
    private List<string> DistinctShown(IEnumerable<string?> candidates)
    {
        var style = _settings().Punctuation;
        var shown = new HashSet<string>(StringComparer.Ordinal);
        return [.. candidates.OfType<string>().Where(c => c.Length > 0 && shown.Add(CompositionOutput.ToSetting(style, c)))];
    }

    /// <summary>絵文字・顔文字の候補を、最後に並べる順 (逆順: いちばんよく使うものが最後) で。</summary>
    private IEnumerable<string> EmojiBlock(string reading) =>
        (_services.Candidates?.LookupEmoji(reading) ?? []).Reverse();

    /// <summary>
    /// 絵文字・顔文字の候補を候補の一覧の最後に移す (英訳・打ったままの英字を足した後に呼ぶ)。
    /// 変換エンジンが最初の候補に絵文字を返したときなど、今選んでいる候補は動かさない。
    /// </summary>
    private void MoveEmojiLast(Clause clause)
    {
        var block = EmojiBlock(clause.Reading).Where(e => clause.Candidates.IndexOf(e) > 0).ToList();
        if (block.Count == 0) return;
        var current = clause.Text;
        clause.Candidates.RemoveAll(block.Contains);
        clause.Candidates.AddRange(block);
        clause.Index = Math.Max(0, clause.Candidates.IndexOf(current));
    }

    /// <summary>英語の文節の候補: 打ったまま → 固有名詞の正しい形 (GitHub) → 先頭だけ大文字 → すべて大文字 → 全角。</summary>
    private List<string> EnglishCandidates(string raw)
    {
        var lower = raw.ToLowerInvariant();
        var capitalized = raw.Length > 0 ? char.ToUpperInvariant(raw[0]) + raw[1..] : raw;
        return Distinct(raw, _detector.ProperNouns.Canonical(lower), capitalized, raw.ToUpperInvariant(), CompositionText.ToFullWidth(raw));
    }

    /// <summary>英語と判定した語を、ローマ字として読んだかな (go → ご)。読み切れなければ null。</summary>
    private string? RomajiReading(string raw)
    {
        // 大文字だけで打った語 (TEN) は、最後の n を ん と読む (TEN → てん: issue #833)。小文字の英文の中の語 (pen) は日本語にしない
        var finalN = raw.All(char.IsAsciiLetterUpper);
        if (_detector.Romaji.AnalyzeFragment(raw.ToLowerInvariant()) is not { IsValid: true } analysis || analysis.Partial != "" && !(finalN && analysis.Partial == "n") ||
            analysis.Kana.Length == 0 && analysis.Partial.Length == 0) return null;
        return analysis.Kana + (analysis.Partial == "n" ? "ん" : "");
    }

    /// <summary>日本語の候補 (かな・漢字) か。英語の文節で選ばれたら、その語を日本語として覚える。</summary>
    internal static bool IsJapaneseText(string text) => text.Any(c => c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿');

    private static List<string> Distinct(params string?[] candidates)
    {
        var list = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && !list.Contains(candidate)) list.Add(candidate);
        }
        return list;
    }

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
        if (_services.MoreCandidates is not { } more) return;
        var reading = clause.IsEnglish ? RomajiReading(clause.Reading) : clause.Reading;
        if (reading is null) return;
        // 変換エンジンの候補は、日本語の文節では、最初の候補とユーザー辞書・同梱の辞書の語の後ろに入れる。
        // 最初の候補のすぐ後ろに入れると、変換エンジンが知っている読みでは、辞書の語が候補の一覧の後ろに押しやられる (saiki → PSYQUI が 58 件中 51 番目: issue #1681)
        var anchor = clause.IsEnglish ? clause.Candidates.IndexOf(reading) : DictionaryWordsEnd(clause, reading);
        if (anchor < 0) return;
        var current = clause.Text;
        var merged = DistinctShown([.. clause.Candidates.Take(anchor + 1), .. more(reading).Select(c => NormalizeHalfWidth(c, reading)), .. clause.Candidates.Skip(anchor + 1)]);
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
        clause.Candidates = JapaneseCandidates(clause.Reading, null);
        clause.Index = 0;
        clause.Changed = false;
        // 前の読みのもの。候補の一覧は、文節を選んだときに新しい読みで取り直す
        clause.Expanded = false;
        clause.Raw = null;
        clause.Translations.Clear();
        if (_learning.LearnedConversion(clause.Reading) is { } learned) Prefer(clause, learned);
    }

    private string Convert(string kana) => NormalizeHalfWidth(_converter.Convert(kana) ?? kana, kana);

    /// <summary>
    /// 文の中での変換結果がある文節の候補に足す「その読みだけの変換」。Zenzai を使える変換エンジンでも Zenzai には頼まない (IStandaloneConverter)。
    /// 文を変換するたびに文節の数だけ頼むので、Zenzai に頼むと文節の数だけ待たされる。最初の候補は文の中での変換結果なので変わらない。
    /// </summary>
    private string Standalone(string kana)
    {
        if (_converter is not IStandaloneConverter standalone) return Convert(kana);
        return NormalizeHalfWidth(standalone.ConvertStandalone(kana) ?? kana, kana);
    }

    /// <summary>
    /// ライブ変換: Space を押したときと同じく、文脈・手がかり辞書・学習を使って文節ごとに変換した結果を見せる。
    /// 打つたびに呼ばれるので、かな・文脈・学習の状態が同じならキャッシュを使う。
    /// </summary>
    private string LiveConvert(string kana)
    {
        // 記号 (～ ! ? …) は数えない。かなが短いうちに記号を足しただけで、的外れな漢字にしない (な～～～ が 名～～～ になっていた: issue #505)
        if (!CountsAsLive(kana)) return kana;
        return string.Concat(LiveClauses(kana).Select(c => c.Text));
    }

    private static bool CountsAsLive(string kana) => kana.Count(c => c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー') >= LiveConversionMinLength;

    /// <summary>ライブ変換の文節 (読みと変換した文字)。予測変換で最後の文節を探すときも、同じ打鍵で変換した結果を使う。</summary>
    private List<(string Reading, string Text)> LiveClauses(string kana)
    {
        var key = string.Join("\u0001", kana, _precedingText, _followingText, _services.History?.Version);
        if (_liveCache.TryGetValue(key, out var cached)) return cached;
        var clauses = WithoutWaiting(() => ConvertJapanese(kana).Select(c => (c.Reading, c.Text)).ToList());
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
