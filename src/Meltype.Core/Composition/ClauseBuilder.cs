// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;
using static Meltype.Composition.CompositionController;

namespace Meltype.Composition;

/// <summary>
/// 文節と候補を作る (変換エンジン・辞書・学習・文脈の手がかりで、文節ごとの候補を並べる)。
/// 受け取った入力を読むだけで、変換の状態 (選んでいる文節・候補) は持たない。状態は CompositionController が持つ。
/// text は打った文字、precedingText・followingText は入力を始めたときのキャレットの前後の文字。
/// </summary>
internal sealed class ClauseBuilder(IKanjiConverter converter, CompositionServices services, Settings settings, CompositionLearning learning,
    CompositionDetector detector, CompositionText text, string? precedingText, string? followingText)
{
    /// <summary>
    /// 区切りの 1 案の文節。英語区間はそのまま、日本語区間は文脈を付けて変換エンジンで文節に区切って変換する。
    /// 英語区間も、ローマ字として読めれば日本語の候補 (go → 語 ご ゴ) を後ろに足す。preferJapanese (Shift+Space) なら前に出す。
    /// converter は変換に使う変換エンジン (英日の区切りの案の 2 番目以降は Zenzai に頼まないもの: <see cref="OtherAlternatives"/>)。
    /// </summary>
    internal List<Clause> BuildClauses(IReadOnlyList<CompositionSegment> segments, bool preferJapanese, IKanjiConverter converter)
    {
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
                var english = Distinct([.. services.UserDictionary?.Lookup(segment.Raw.ToLowerInvariant()) ?? [], .. EnglishCandidates(segment.Raw)]);
                // 子音の略入力 (ast → 明日): 英字のまま見える語にも、candidates.txt の英字の読みの候補を足す (issue #62)
                if (settings.ConsonantAbbreviations && segment.Raw.Length >= 2 && segment.Raw.All(char.IsAsciiLetter) && services.Candidates is { } abbreviations)
                {
                    foreach (var word in abbreviations.Lookup(segment.Raw.ToLowerInvariant()))
                    {
                        if (!english.Contains(word)) english.Add(word);
                    }
                }
                // 記号だけの英語区間にも、同梱の記号候補 (... → …) を出す。
                if (segment.Raw.All(c => !char.IsLetterOrDigit(c)))
                    english = Distinct([.. english, .. services.Candidates?.Lookup(segment.Raw) ?? []]);
                var reading = RomajiReading(segment.Raw);
                List<string> romaji = reading is null ? [] : JapaneseCandidates(reading, null, converter);
                clauses.Add(new Clause(segment.Raw, true, preferJapanese && romaji.Count > 0
                    ? Distinct([.. romaji, .. english])
                    : Distinct([.. english, .. romaji]))
                { Kana = text.KanaForSegment(segments, s) });
                continue;
            }
            if (segment.Kana.Length == 0) continue;
            var japanese = ConvertJapaneseSegment(segment.Kana, string.Concat(clauses.Select(c => c.Text)), converter);
            // 候補の最後に、打ったままの英字 (あぴ → api) と全角の英字も出す (Space を連打して英字に戻せる)。
            var offset = 0;
            foreach (var clause in japanese)
            {
                clause.Raw = text.RawForReading(segments, s, offset, clause.Reading.Length);
                offset += clause.Reading.Length;
                AddOldKana(clause);
                AddTranslations(clause);
                AddRawCandidates(clause);
                MoveEmojiLast(clause);
            }
            clauses.AddRange(japanese);
        }
        return clauses;
    }

    /// <summary>英日の区切りの案のうち 2 番目以降の文節。空になった案は除く。Zenzai には頼まない (IStandaloneConverter)。</summary>
    internal List<List<Clause>> OtherAlternatives(IReadOnlyList<IReadOnlyList<CompositionSegment>> alternatives, bool preferJapanese)
    {
        var withoutZenzai = converter is IStandaloneConverter standalone ? new WithoutZenzai(standalone) : converter;
        return [.. alternatives.Skip(1).Select(a => BuildClauses(a, preferJapanese, withoutZenzai)).Where(c => c.Count > 0)];
    }

    /// <summary>Zenzai に頼まない変換 (<see cref="IStandaloneConverter"/>) を、変換エンジンとして使う。</summary>
    internal sealed class WithoutZenzai(IStandaloneConverter standalone) : IKanjiConverter
    {
        public string? Convert(string hiragana) => standalone.ConvertStandalone(hiragana);

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => standalone.ConvertClausesStandalone(hiragana, context);
    }

    /// <summary>
    /// 区切りの案ごとの文節 (先頭がいちばん良い案) を、文全体の 1 つの文節にする。候補は案ごとの文 (botto → botと、ボット) の後ろに、
    /// 文節が 1 つの案のその文節の候補 (sushi → 寿司 の 鮨・すし)。案の文がすべて同じなら null。
    /// </summary>
    internal Clause? WholeSentenceClause(List<List<Clause>> alternatives)
    {
        var texts = new Dictionary<string, List<Clause>>(StringComparer.Ordinal);
        foreach (var clauses in alternatives) texts.TryAdd(string.Concat(clauses.Select(c => c.Text)), clauses);
        if (texts.Count < 2) return null;
        var whole = new Clause(text.AllKana(final: true), false, [.. texts.Keys]) { Raw = text.Raw, Alternatives = texts };
        AddSingleClauseCandidates(whole);
        return whole;
    }

    /// <summary>
    /// 読み全体を 1 つの文節にして、普通の変換の候補のかなの後ろ (絵文字・打ったままの英字より前) に、ユーティリティ変換の候補を足す
    /// (Prepend なら普通の変換の候補の前に足して、最初の候補にする)。
    /// 変換エンジンが文節を分けたら (じこうの|あいさつ)、読み全体をその文節だけで変換し直す。
    /// </summary>
    internal Clause WithUtility(List<Clause> clauses, UtilityCandidates utility, string reading)
    {
        var clause = clauses is [var only] && only.Reading == reading ? only : WholeClause(clauses, reading);
        var katakana = CompositionText.ToKatakana(reading);
        var at = utility.Placement == UtilityPlacement.Prepend ? 0
            : new[] { reading, katakana, CompositionText.ToHalfWidthKatakana(katakana) }.Max(clause.Candidates.IndexOf) + 1;
        if (at == 0 && utility.Placement != UtilityPlacement.Prepend) at = clause.Candidates.Count;
        clause.NotLearned.UnionWith(utility.NotLearned);
        foreach (var candidate in utility.Candidates)
        {
            if (clause.Candidates.Contains(candidate)) continue;
            clause.Candidates.Insert(at++, candidate);
        }
        if (utility.Placement == UtilityPlacement.Prepend)
        {
            clause.Index = 0;
            clause.Leading = at;
        }
        return clause;
    }

    internal Clause WholeClause(List<Clause> clauses, string reading)
    {
        var clause = new Clause(reading, false, JapaneseCandidates(reading, string.Concat(clauses.Select(c => c.Text)), converter)) { Raw = text.Raw };
        // 文の中の文節と同じく、文脈の手がかりか、前に選び直した語を最初の候補にする (ConvertWithEngine)
        var surrounding = (precedingText ?? "") + EnglishInComposition() + (followingText ?? "");
        if ((services.ContextRules?.Choose(reading, surrounding, clause.Text, Standalone) ?? learning.LearnedConversion(reading)) is { } preferred)
            Prefer(clause, preferred);
        KeepTypedSymbols([clause], reading);
        AddOldKana(clause);
        AddTranslations(clause);
        AddRawCandidates(clause);
        MoveEmojiLast(clause);
        return clause;
    }

    /// <summary>
    /// 英訳の候補 (複雑な → complex, complicated …) を、日本語の候補の後ろに足す。
    /// 選んだことのある英訳は前に出す: 1 回なら英訳の中の先頭、2 回以上なら 2 番目 (変換エンジンの 1 番目の候補は動かさない)。
    /// </summary>
    internal void AddTranslations(Clause clause)
    {
        if (clause.IsEnglish || services.Translations is not { } dictionary || !settings.TranslationCandidates) return;
        var words = dictionary.Lookup(clause.Text, clause.Reading).Where(w => !clause.Candidates.Contains(w)).ToList();
        if (words.Count == 0) return;
        var learned = services.TranslationHistory?.Get(clause.Reading) ?? [];
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
    internal string? EnglishSplit(string raw)
    {
        var typed = new CompositionText(detector);
        foreach (var c in raw) typed.Append(c);
        if (typed.Pending.Length > 0) return null;
        var segments = detector.SegmentLeaningEnglish(typed.Units);
        if (!segments.Any(s => s.IsEnglish) || segments.All(s => s.IsEnglish)) return null;
        return string.Concat(segments.Select(s => s.IsEnglish ? s.Raw : s.Kana));
    }

    internal void AddRawCandidates(Clause clause)
    {
        // 子音の略入力 (ast → 明日): 打った英字を読みとする候補 (candidates.txt の英字の読み) を足す (issue #62)
        if (settings.ConsonantAbbreviations && clause.Raw is { Length: >= 2 } abbreviation && abbreviation.All(char.IsAsciiLetter) && services.Candidates is { } abbreviations)
        {
            foreach (var word in abbreviations.Lookup(abbreviation.ToLowerInvariant()))
            {
                if (!clause.Candidates.Contains(word)) clause.Candidates.Add(word);
            }
        }
        // 入力中の表示が半角の記号 (* が 2 個以上・---) のときは、最初の候補も同じ半角にする。
        if (text.Mode == DisplayMode.Auto && clause.Candidates.Count > 0 &&
            text.AsDisplayedHalfWidth(clause.Candidates[0], clause.Raw) is var shown && shown != clause.Candidates[0])
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
    internal static void AddOldKana(Clause clause)
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
    /// 英日の区間分けの日本語の区間を変換する。Space の変換もライブ変換も、区間ごとにこれを通す。
    /// before は同じ変換ボックスでこの区間より前の文字。キャレットの前の文字とつなげて、英語も含めてそのまま変換エンジンに文脈として渡す
    /// (どこまで・どう使うかは変換エンジンが決める)。
    /// </summary>
    internal List<Clause> ConvertJapaneseSegment(string kana, string before, IKanjiConverter converter)
    {
        var japanese = ConvertJapanese(kana, (precedingText ?? "") + before, converter);
        var afterEnglish = LanguageOf(before) == true;
        // ワークアラウンド: 英単語 + 付属語 (python|なら、push|した): 変換エンジンは英語の後ろの読みだけを見て、文の頭の語 (奈良・下) にしてしまう。かなのまま。
        // 変換エンジンに文脈 (英単語・これ) を渡しても、名詞の読みを前に付けて変換しても、なら・しか・しよう は直らなかった。
        // 変換エンジンが英単語を前の文脈として受け取り、後ろの読みを付属語として変換するようになったら消せる
        if (afterEnglish && japanese.Count > 0 && IsParticleAfterEnglish(japanese[0])) Prefer(japanese[0], japanese[0].Reading);
        return japanese;
    }

    internal List<Clause> ConvertJapanese(string kana, string context, IKanjiConverter converter)
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
                if (keep[start]) result.Add(new Clause(run, false, Distinct([.. services.UserDictionary?.Lookup(run) ?? [], run, .. services.Candidates?.Lookup(run) ?? [], CompositionText.ToFullWidth(run)])));
                else result.AddRange(ConvertJapanese(run, context, converter));
                start = i;
            }
            return result;
        }
        return ConvertWithEngine(kana, context, converter);
    }

    /// <summary>
    /// 日本語のかなを変換エンジンで文節に区切って変換する。各文節の最初の候補は次の順で決める:
    ///   1. 文脈の手がかり辞書 (前後に 気温 があれば あつい → 暑い)
    ///   2. ユーザーが前に選び直した変換 (学習)
    ///   3. 変換エンジンの結果 (前の文字列 context を文脈として渡す)
    /// </summary>
    internal List<Clause> ConvertWithEngine(string kana, string context, IKanjiConverter converter)
    {
        var parts = converter.ConvertClauses(kana, context);
        parts ??= [new ConversionClause(kana, converter.Convert(kana) ?? kana)];
        // 読み全体が補助辞書・絵文字の辞書の語 (かんがえるかお → 🤔) なら、文節に分けずに 1 つの文節にして候補を出す。
        if (parts.Count > 1 && services.Candidates?.Contains(kana) == true)
        {
            parts = [new ConversionClause(kana, string.Concat(parts.Select(p => p.Text)))];
        }
        parts = JoinSmallKana(parts, converter);
        var clauses = parts.Select(p => new Clause(p.Reading, false, JapaneseCandidates(p.Reading, NormalizeHalfWidth(p.Text, p.Reading), converter))
        {
            ZenzaiText = p.Zenzai ? NormalizeHalfWidth(p.Text, p.Reading) : null,
        }).ToList();
        for (var i = 0; i < clauses.Count; i++)
        {
            var others = string.Concat(clauses.Where((_, k) => k != i).Select(c => c.Text));
            var surrounding = (precedingText ?? "") + EnglishInComposition() + others + (followingText ?? "");
            var preferred = services.ContextRules?.Choose(clauses[i].Reading, surrounding, clauses[i].Text, Standalone) ?? learning.LearnedConversion(clauses[i].Reading);
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
        KeepTypedSymbols(clauses, kana);
        return clauses;
    }

    /// <summary>
    /// かなと一緒に打った記号 (読みの 、。？「 など。CompositionText.Symbol が決めた文字) は、最初の候補でも読みの文字のままにする。
    /// 変換エンジンの答え・Mozc の学習・変換履歴は、記号を別の文字にすることがある
    /// (ライブ変換で 例文？ が 例文? に: issue #519、「今日は が ［今日は に: issue #1669、わたしは、 が 私は・ に)。別の候補は一覧に残す。
    /// 記号だけを打って変換したとき (、 だけ・( だけ) は、選んで覚えた候補を使う (issue #672)。記号だけかは、文節や
    /// ユーザー辞書で分けた部分の読みでなく、打ったもの全体で決める (わたしは、 の区切りを変えてできた 、 だけの文節にも、覚えた ・ を出さない)。
    /// 記号の数が読みと違う答え (から → ～、あんはさうぇい → アン・ハサウェイ) は、どの記号がどれに当たるか分からないので変えない。
    /// 打った記号を文字の種類と数で見分けているのは、変換の結果が区間の種類を持たない文字列だから。区間の種類を持ち回すようになったら (#1735)、区間で決める。
    /// 最初の候補を決める所 (ConvertWithEngine・Reconvert・WholeClause) の最後に呼ぶ。
    /// </summary>
    internal void KeepTypedSymbols(IReadOnlyList<Clause> clauses, string reading)
    {
        if (!text.Raw.Any(char.IsLetterOrDigit)) return;
        var typed = reading.Where(IsSymbol).ToList();
        if (clauses.Sum(c => c.Text.Count(IsSymbol)) != typed.Count) return;
        var next = 0;
        foreach (var clause in clauses)
        {
            var replaced = string.Concat(clause.Text.Select(c => IsSymbol(c) ? typed[next++] : c));
            if (replaced == clause.Text) continue;
            if (clause.ZenzaiText == clause.Text) clause.ZenzaiText = replaced;
            Prefer(clause, replaced);
        }

        // 絵文字 (サロゲートペア) は打った記号に当たらないので数えない (置き換えると壊れた文字になる)
        static bool IsSymbol(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && !char.IsSurrogate(c);
    }

    /// <summary>
    /// 英単語の後ろの、名詞に付く付属語 (助詞・助動詞) か する の活用の文節を、変換エンジンが漢字で始めた
    /// (なら → 奈良、しか → 歯科、なの → 名の、した → 下、したい → 死体)。
    /// </summary>
    internal static bool IsParticleAfterEnglish(Clause clause) =>
        (NounAttachedWords.Contains(clause.Reading) || CompositionDetector.SuruForms.Any(clause.Reading.StartsWith)) &&
        clause.Text.Length > 0 && !IsKana(clause.Text[0]) && clause.Text != clause.Reading;

    // 名詞に付く付属語。文節の読み全体と比べるので、変換エンジンが 1 つの文節にまとめる形 (には・なので) も並べる。
    // か は入れない: 英単語の後ろの 化 (docker化・API化) を変換エンジンが出したときに、かなに戻してしまう
    internal static readonly HashSet<string> NounAttachedWords =
    [
        "が", "を", "に", "で", "と", "の", "は", "も", "へ", "や", "から", "まで", "より", "って", "とか", "とは", "には", "では", "にも", "でも",
        "への", "での", "との", "なら", "しか", "だけ", "のみ", "ほど", "など", "くらい", "ぐらい", "ばかり", "さえ", "すら", "こそ", "ずつ", "ごと", "とも", "ども", "やら", "なり",
        "だ", "です", "だった", "でした", "だろう", "でしょう", "だよ", "だね", "だし", "だから", "じゃ", "なの", "なのに", "なので", "かも", "みたい", "っぽい", "らしい",
    ];

    internal static readonly HashSet<char> SentencePunctuation = ['、', '。', '，', '．', ',', '.', '！', '？', '!', '?', '…', '‥', '「', '」', '(', ')', '（', '）', ' ', '　'];

    /// <summary>
    /// 文節が「え」だけ (後ろに記号が付いていてもよい) で、文の頭か記号の後ろにあり、後ろが記号か文の終わりか。
    /// こういう「え」は聞き返し・驚き (え、しらん) で、絵 や 得 ではない。
    /// </summary>
    internal bool IsInterjection(List<Clause> clauses, int i)
    {
        var reading = clauses[i].Reading;
        if (reading.Length == 0 || reading[0] != 'え' || !reading.Skip(1).All(SentencePunctuation.Contains) || clauses[i].Text == reading) return false;
        var before = i > 0 ? clauses[i - 1].Text : precedingText ?? "";
        if (before.Length > 0 && !SentencePunctuation.Contains(before[^1])) return false;
        if (reading.Length > 1) return true;
        return i + 1 == clauses.Count || clauses[i + 1].Text is [var next, ..] && SentencePunctuation.Contains(next);
    }

    /// <summary>
    /// 小書きのかな (ぃ ぇ ゃ …) で始まる文節は前の文節とつなげる。小書きのかなから始まる語は無いので、
    /// 変換エンジンの知らない語を区切り間違えたもの (こうぃ → 光|ぃ)。つなげた読みは、その文節だけで変換し直す
    /// (こうぃ → コウィ。wi で打っていれば こゐ も候補に出る)。
    /// </summary>
    internal static IReadOnlyList<ConversionClause> JoinSmallKana(IReadOnlyList<ConversionClause> parts, IKanjiConverter converter)
    {
        if (!parts.Skip(1).Any(p => p.Reading.Length > 0 && KanaChars.IsSmallKana(p.Reading[0]))) return parts;
        var joined = new List<ConversionClause>();
        foreach (var part in parts)
        {
            if (joined.Count > 0 && part.Reading.Length > 0 && KanaChars.IsSmallKana(part.Reading[0]))
            {
                var reading = joined[^1].Reading + part.Reading;
                // 変換しても漢字の後ろに小書きのかなが残る (光ぃ) なら、カタカナ (コウィ) にする。
                var text = Convert(reading, converter);
                if (Enumerable.Range(1, Math.Max(0, text.Length - 1)).Any(i => KanaChars.IsSmallKana(text[i]) && !IsKana(text[i - 1]))) text = CompositionText.ToKatakana(reading);
                joined[^1] = new ConversionClause(reading, text);
            }
            else joined.Add(part);
        }
        return joined;
    }

    internal static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);

    /// <summary>
    /// 文節の候補: 文の中での変換結果 → その文節だけでの変換結果 → 補助辞書の同音異義語 → ひらがな → 全角カタカナ → 半角カタカナ
    /// → 分野の辞書の語 → 絵文字・顔文字 (逆順)。
    /// 分野の辞書の語 (病名・駅名など) は、使わない人には要らない候補なので、ふだんの候補 (変換エンジンの候補・ひらがな・カタカナ) を
    /// 1 つも後ろに押さないよう、その後ろに並べる。こうすれば誰にとっても困らないので、分野ごとに外す設定は作らない (#1650)。
    /// 絵文字・顔文字は最後に逆順で並べるので、変換してすぐ ↑ を押すと、いちばんよく使う絵文字 (えがお → 😊) になる (issue #133)。
    /// </summary>
    internal List<string> JapaneseCandidates(string reading, string? inContext, IKanjiConverter converter)
    {
        // 文の中での変換結果が無いとき (文節の区切りを変えた・打っている途中の候補) は、その読みだけの変換が最初の候補になるので、Zenzai も使う (Convert)
        var standalone = inContext is null ? Convert(reading, converter) : Standalone(reading);
        var katakana = CompositionText.ToKatakana(reading);
        // ユーザー辞書に登録した単語は、文の中での変換結果の次に出す (文節の区切りを変えて読みが一致したときなど)。
        return DistinctShown(c => ComposedText.Converted(reading, c), [inContext, .. services.UserDictionary?.Lookup(reading) ?? [], standalone, .. services.Candidates?.LookupWords(reading) ?? [],
            reading, katakana, CompositionText.ToHalfWidthKatakana(katakana), .. services.Candidates?.LookupDomain(reading) ?? [], .. EmojiBlock(reading)]);
    }

    /// <summary>
    /// 入力欄に出る文字 (打った句読点を設定の句読点にした文字) が同じ候補は、最初の 1 つだけ残す。「，．」の設定では、打った 、 の候補の 、 と ， が同じ ， になる。
    /// 変換エンジンの候補が入る所 (この関数を呼ぶ所) だけで重複を消す。compose は候補を打った句読点の区間と一緒にする関数。
    /// </summary>
    internal List<string> DistinctShown(Func<string, ComposedText> compose, IEnumerable<string?> candidates)
    {
        var style = settings.Punctuation;
        var shown = new HashSet<string>(StringComparer.Ordinal);
        return [.. candidates.OfType<string>().Where(c => c.Length > 0 && shown.Add(CompositionOutput.Render(style, compose(c))))];
    }

    /// <summary>絵文字・顔文字の候補を、最後に並べる順 (逆順: いちばんよく使うものが最後) で。</summary>
    internal IEnumerable<string> EmojiBlock(string reading) =>
        (services.Candidates?.LookupEmoji(reading) ?? []).Reverse();

    /// <summary>
    /// 絵文字・顔文字の候補を候補の一覧の最後に移す (英訳・打ったままの英字を足した後に呼ぶ)。
    /// 変換エンジンが最初の候補に絵文字を返したときなど、今選んでいる候補は動かさない。
    /// </summary>
    internal void MoveEmojiLast(Clause clause)
    {
        var block = EmojiBlock(clause.Reading).Where(e => clause.Candidates.IndexOf(e) > 0).ToList();
        if (block.Count == 0) return;
        var current = clause.Text;
        clause.Candidates.RemoveAll(block.Contains);
        clause.Candidates.AddRange(block);
        clause.Index = Math.Max(0, clause.Candidates.IndexOf(current));
    }

    /// <summary>英語の文節の候補: 打ったまま → 固有名詞の正しい形 (GitHub) → 先頭だけ大文字 → すべて大文字 → 全角。</summary>
    internal List<string> EnglishCandidates(string raw)
    {
        var lower = raw.ToLowerInvariant();
        var capitalized = raw.Length > 0 ? char.ToUpperInvariant(raw[0]) + raw[1..] : raw;
        return Distinct(raw, detector.ProperNouns.Canonical(lower), capitalized, raw.ToUpperInvariant(), CompositionText.ToFullWidth(raw));
    }

    /// <summary>英語と判定した語を、ローマ字として読んだかな (go → ご)。読み切れなければ null。</summary>
    internal string? RomajiReading(string raw)
    {
        // 大文字だけで打った語 (TEN) は、最後の n を ん と読む (TEN → てん: issue #833)。小文字の英文の中の語 (pen) は日本語にしない
        var finalN = raw.All(char.IsAsciiLetterUpper);
        if (detector.Romaji.AnalyzeFragment(raw.ToLowerInvariant()) is not { IsValid: true } analysis || analysis.Partial != "" && !(finalN && analysis.Partial == "n") ||
            analysis.Kana.Length == 0 && analysis.Partial.Length == 0) return null;
        return analysis.Kana + (analysis.Partial == "n" ? "ん" : "");
    }

    internal static List<string> Distinct(params string?[] candidates)
    {
        var list = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && !list.Contains(candidate)) list.Add(candidate);
        }
        return list;
    }

    internal static string Convert(string kana, IKanjiConverter converter) => NormalizeHalfWidth(converter.Convert(kana) ?? kana, kana);

    /// <summary>
    /// 文の中での変換結果がある文節の候補に足す「その読みだけの変換」。Zenzai を使える変換エンジンでも Zenzai には頼まない (IStandaloneConverter)。
    /// 文を変換するたびに文節の数だけ頼むので、Zenzai に頼むと文節の数だけ待たされる。最初の候補は文の中での変換結果なので変わらない。
    /// </summary>
    internal string Standalone(string kana)
    {
        var converted = converter is IStandaloneConverter standalone ? standalone.ConvertStandalone(kana) : converter.Convert(kana);
        return NormalizeHalfWidth(converted ?? kana, kana);
    }

    /// <summary>
    /// 同じ変換ボックスの英字の区間 (ph|しけんし の ph)。日本語の区間は区間ごとに変換するので、文脈の手がかりに足す (issue #1276)。
    /// 続いた英字の区間 (gra|ph) は 1 語のままにし、日本語の区間を空白にする (英字の手がかりは英単語の境界で当てるため)。
    /// </summary>
    internal string EnglishInComposition() =>
        text.IsEmpty ? "" : string.Concat(text.ConversionSegments().Select(s => s.IsEnglish ? s.Raw : " "));

    /// <summary>日本語の区間を、背景の変換を待って変換した文字。</summary>
    internal ComposedText ConvertedText(string kana, string before) =>
        ComposedText.Concat(ConvertJapaneseSegment(kana, before, converter).Select(c => c.Composed));
}
