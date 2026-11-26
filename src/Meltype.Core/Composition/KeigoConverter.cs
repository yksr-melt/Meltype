// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

using System.Buffers;
using System.Text;
using Meltype.Libraries.Lexicon;

namespace Meltype.Composition;

/// <summary>
/// 敬語モード: ライブ変換・通常変換の日本語文節列を、丁寧語・謙譲語・ビジネス敬語へ変換する純粋後処理。
/// 語彙や変換表は dictionaries/keigo.txt から読み込み、コードは読み込みと適用規則のみを持つ。
/// </summary>
internal static class KeigoConverter
{
    private static readonly Rules LoadedRules = LoadRules();

    private static string[] AlreadyKeigoEndings => LoadedRules.AlreadyKeigoEndings;
    private static string[] AlreadyPoliteEndings => LoadedRules.AlreadyPoliteEndings;
    private static Dictionary<string, string> ReadingWords => LoadedRules.ReadingWords;
    private static Dictionary<string, string> RespectfulReadingMap => LoadedRules.RespectfulReadingMap;
    private static Dictionary<char, (string Suffix, string Replacement)[]> RespectfulTextSuffixBuckets => LoadedRules.RespectfulTextSuffixBuckets;
    private static string[] OtherSubjectTokens => LoadedRules.OtherSubjectTokens;
    private static string[] SelfSubjectTokens => LoadedRules.SelfSubjectTokens;
    private static SearchValues<string> SubjectSearchValues => LoadedRules.SubjectSearchValues;
    private static Dictionary<string, string> QuestionReadingMap => LoadedRules.QuestionReadingMap;
    private static Dictionary<char, (string Suffix, string Replacement)[]> QuestionSuffixBuckets => LoadedRules.QuestionSuffixBuckets;
    private static Dictionary<string, string> TailReadingMap => LoadedRules.TailReadingMap;
    private static Dictionary<char, (string Suffix, string Replacement)[]> MidClauseSuffixBuckets => LoadedRules.MidClauseSuffixBuckets;
    private static Dictionary<char, (string Suffix, string Replacement)[]> RespectfulMidClauseSuffixBuckets => LoadedRules.RespectfulMidClauseSuffixBuckets;
    private static (string Suffix, string RespectfulStem)[] TekureteVerbs => LoadedRules.TekureteVerbs;
    private static string[] GratitudeFollowingReadings => LoadedRules.GratitudeFollowingReadings;
    private static (string From, string To, bool RequireEndBoundary)[] InTextReplacements => LoadedRules.InTextReplacements;
    private static SearchValues<string> InTextSearchValues => LoadedRules.InTextSearchValues;
    private static string[] Particles => LoadedRules.Particles;
    private static Dictionary<char, (string ReadingPrefix, string KeigoPrefix, bool AllowsParticle)[]> ReadingPrefixBuckets => LoadedRules.ReadingPrefixBuckets;
    private static Dictionary<char, (string Suffix, string Replacement)[]> TailTextSuffixBuckets => LoadedRules.TailTextSuffixBuckets;
    private static (string Suffix, string SuruReplacement, string? GodanSaReplacement)[] SuruOrGodanSuffixes => LoadedRules.SuruOrGodanSuffixes;
    private static string[] PoliteAdjectiveEndings => LoadedRules.PoliteAdjectiveEndings;
    private static string[] NonRequestTeEndings => LoadedRules.NonRequestTeEndings;

    private sealed class Rules
    {
        public required string[] AlreadyKeigoEndings { get; init; }
        public required string[] AlreadyPoliteEndings { get; init; }
        public required Dictionary<string, string> ReadingWords { get; init; }
        public required Dictionary<string, string> RespectfulReadingMap { get; init; }
        public required Dictionary<char, (string Suffix, string Replacement)[]> RespectfulTextSuffixBuckets { get; init; }
        public required string[] OtherSubjectTokens { get; init; }
        public required string[] SelfSubjectTokens { get; init; }
        public required SearchValues<string> SubjectSearchValues { get; init; }
        public required Dictionary<string, string> QuestionReadingMap { get; init; }
        public required Dictionary<char, (string Suffix, string Replacement)[]> QuestionSuffixBuckets { get; init; }
        public required Dictionary<string, string> TailReadingMap { get; init; }
        public required Dictionary<char, (string Suffix, string Replacement)[]> MidClauseSuffixBuckets { get; init; }
        public required Dictionary<char, (string Suffix, string Replacement)[]> RespectfulMidClauseSuffixBuckets { get; init; }
        public required (string Suffix, string RespectfulStem)[] TekureteVerbs { get; init; }
        public required string[] GratitudeFollowingReadings { get; init; }
        public required (string From, string To, bool RequireEndBoundary)[] InTextReplacements { get; init; }
        public required SearchValues<string> InTextSearchValues { get; init; }
        public required string[] Particles { get; init; }
        public required Dictionary<char, (string ReadingPrefix, string KeigoPrefix, bool AllowsParticle)[]> ReadingPrefixBuckets { get; init; }
        public required Dictionary<char, (string Suffix, string Replacement)[]> TailTextSuffixBuckets { get; init; }
        public required (string Suffix, string SuruReplacement, string? GodanSaReplacement)[] SuruOrGodanSuffixes { get; init; }
        public required string[] PoliteAdjectiveEndings { get; init; }
        public required string[] NonRequestTeEndings { get; init; }
    }

    private static Rules LoadRules()
    {
        var alreadyKeigo = new List<string>();
        var alreadyPolite = new List<string>();
        var readingWords = new Dictionary<string, string>(StringComparer.Ordinal);
        var respectfulReadings = new Dictionary<string, string>(StringComparer.Ordinal);
        var respectfulSuffixes = new List<(string Suffix, string Replacement)>();
        var otherSubjectMarkers = new List<string>();
        var selfSubjectMarkers = new List<string>();
        var questionReadings = new Dictionary<string, string>(StringComparer.Ordinal);
        var questionSuffixes = new List<(string Suffix, string Replacement)>();
        var tailReadings = new Dictionary<string, string>(StringComparer.Ordinal);
        var midClauseSuffixes = new List<(string Suffix, string Replacement)>();
        var respectfulMidClauseSuffixes = new List<(string Suffix, string Replacement)>();
        var tekureteVerbs = new List<(string Suffix, string RespectfulStem)>();
        var gratitudeFollowing = new List<string>();
        var inTextReplacements = new List<(string From, string To, bool RequireEndBoundary)>();
        var particles = new List<string>();
        var readingPrefixes = new List<(string ReadingPrefix, string KeigoPrefix, bool AllowsParticle)>();
        var tailTextSuffixes = new List<(string Suffix, string Replacement)>();
        var suruOrGodanSuffixes = new List<(string Suffix, string SuruReplacement, string? GodanSaReplacement)>();
        var politeAdjectives = new List<string>();
        var nonRequestTe = new List<string>();

        var section = "";
        foreach (var raw in DictionarySource.ReadEmbedded("keigo.txt").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith('#'))
            {
                if (trimmed.Length > 3 && trimmed[1..].TrimStart() is ['[', ..] header && header.EndsWith(']'))
                    section = header[1..^1];
                continue;
            }

            var fields = line.Split('\t');
            switch (section)
            {
                case "already_keigo": alreadyKeigo.Add(fields[0]); break;
                case "already_polite": alreadyPolite.Add(fields[0]); break;
                case "particles": particles.Add(fields[0]); break;
                case "other_subjects": otherSubjectMarkers.Add(fields[0]); break;
                case "self_subjects": selfSubjectMarkers.Add(fields[0]); break;
                case "gratitude_following": gratitudeFollowing.Add(fields[0]); break;
                case "polite_adjectives": politeAdjectives.Add(fields[0]); break;
                case "non_request_te": nonRequestTe.Add(fields[0]); break;
                case "reading_words" when fields.Length >= 2: readingWords[fields[0]] = fields[1]; break;
                case "respectful_readings" when fields.Length >= 2: respectfulReadings[fields[0]] = fields[1]; break;
                case "respectful_suffixes" when fields.Length >= 2: respectfulSuffixes.Add((fields[0], fields[1])); break;
                case "question_readings" when fields.Length >= 2: questionReadings[fields[0]] = fields[1]; break;
                case "question_suffixes" when fields.Length >= 2: questionSuffixes.Add((fields[0], fields[1])); break;
                case "tail_readings" when fields.Length >= 2: tailReadings[fields[0]] = fields[1]; break;
                case "mid_clause_suffixes" when fields.Length >= 2: midClauseSuffixes.Add((fields[0], fields[1])); break;
                case "respectful_mid_clause_suffixes" when fields.Length >= 2: respectfulMidClauseSuffixes.Add((fields[0], fields[1])); break;
                case "tekurete_verbs" when fields.Length >= 2: tekureteVerbs.Add((fields[0], fields[1])); break;
                case "tail_text_suffixes" when fields.Length >= 2: tailTextSuffixes.Add((fields[0], fields[1])); break;
                case "reading_prefixes" when fields.Length >= 3: readingPrefixes.Add((fields[0], fields[1], fields[2] == "1")); break;
                case "in_text_replacements" when fields.Length >= 3: inTextReplacements.Add((fields[0], fields[1], fields[2] == "1")); break;
                case "suru_or_godan_suffixes" when fields.Length >= 2: suruOrGodanSuffixes.Add((fields[0], fields[1], fields.Length > 2 ? fields[2] : null)); break;
            }
        }

        string[] alreadyKeigoArr = [.. alreadyKeigo];
        string[] otherSubjects = BuildSubjectTokens([.. otherSubjectMarkers]);
        string[] selfSubjects = BuildSubjectTokens([.. selfSubjectMarkers]);
        var inTextArray = inTextReplacements.ToArray();

        return new Rules
        {
            AlreadyKeigoEndings = alreadyKeigoArr,
            AlreadyPoliteEndings = [.. alreadyKeigoArr, .. alreadyPolite],
            ReadingWords = readingWords,
            RespectfulReadingMap = respectfulReadings,
            RespectfulTextSuffixBuckets = BuildSuffixBuckets([.. respectfulSuffixes]),
            OtherSubjectTokens = otherSubjects,
            SelfSubjectTokens = selfSubjects,
            SubjectSearchValues = SearchValues.Create([.. otherSubjects, .. selfSubjects], StringComparison.Ordinal),
            QuestionReadingMap = questionReadings,
            QuestionSuffixBuckets = BuildSuffixBuckets([.. questionSuffixes]),
            TailReadingMap = tailReadings,
            MidClauseSuffixBuckets = BuildSuffixBuckets([.. midClauseSuffixes]),
            RespectfulMidClauseSuffixBuckets = BuildSuffixBuckets([.. respectfulMidClauseSuffixes]),
            TekureteVerbs = [.. tekureteVerbs],
            GratitudeFollowingReadings = [.. gratitudeFollowing],
            InTextReplacements = inTextArray,
            InTextSearchValues = SearchValues.Create([.. inTextArray.Select(x => x.From)], StringComparison.Ordinal),
            Particles = [.. particles],
            ReadingPrefixBuckets = readingPrefixes.GroupBy(x => x.ReadingPrefix[0]).ToDictionary(g => g.Key, g => g.ToArray()),
            TailTextSuffixBuckets = BuildSuffixBuckets([.. tailTextSuffixes]),
            SuruOrGodanSuffixes = [.. suruOrGodanSuffixes],
            PoliteAdjectiveEndings = BuildAdjectiveEndings([.. politeAdjectives]),
            NonRequestTeEndings = [.. nonRequestTe],
        };
    }

    private static Dictionary<char, (string Suffix, string Replacement)[]> BuildSuffixBuckets(
        (string Suffix, string Replacement)[] suffixes) =>
        suffixes.GroupBy(x => x.Suffix[^1]).ToDictionary(g => g.Key, g => g.ToArray());

    private static string[] BuildSubjectTokens(string[] markers)
    {
        var list = new List<string>(markers.Length * 3);
        foreach (var marker in markers)
        {
            list.Add(marker + "は");
            list.Add(marker + "が");
            list.Add(marker + "も");
        }
        return [.. list];
    }

    private static string[] BuildAdjectiveEndings(string[] adjectives)
    {
        var list = new List<string>(adjectives.Length * 2);
        foreach (var adj in adjectives)
        {
            list.Add(adj);
            list.Add(adj[..^1] + "かった");
        }
        return [.. list];
    }

    /// <summary>文節の列と直前文脈を受け取り、敬語を先頭候補に置いた新しい文節の列を返す純粋関数。</summary>
    public static List<Clause> Convert(IReadOnlyList<Clause> clauses, string? before = null)
    {
        if (clauses.Count == 0) return [];

        // 隣接する 2〜3 文節を連結した読みが定型句に一致する場合、1 文節に統合して変換する
        // (例: [うちの, 会社] → 弊社、[忙しい, ところ] → お忙しいところ恐れ入りますが、[ちょっと, 待ってて] → 少々お待ちください)
        var result = MergeMultiClauseIdioms(clauses);

        var lastContentIndex = result.Count - 1;
        while (lastContentIndex >= 0 && IsPunctuationOnly(result[lastContentIndex].Text))
            lastContentIndex--;

        // 単文節かつ先行文脈なしの高頻度ケースは StringBuilder を確保せず処理する
        if (result.Count == 1 && string.IsNullOrEmpty(before))
        {
            var c = result[0];
            if (IsPunctuationOnly(c.Text) && IsPunctuationOnly(c.Reading)) return result;
            var role = DetectSubjectRole(c.Text);
            if (ConvertClause(c.Reading, c.Text, isSentenceEnd: true, nextIsQuestion: false, gratitudeFollows: false, role, prevReading: "", hasNextContent: false) is { } singleConverted && singleConverted != c.Text)
                result[0] = WithKeigo(c, singleConverted);
            return result;
        }

        // 直前の確定済みテキストからは「最後の文」だけを引き継ぐ (前の文の主語が改行や句点をまたいで残らないようにする)
        var currentSentence = new StringBuilder().Append(LastSentenceOf(before));

        for (var i = 0; i < result.Count; i++)
        {
            var clauseText = result[i].Text;
            var clauseReading = result[i].Reading;
            currentSentence.Append(clauseText);

            if (IsPunctuationOnly(clauseText) && IsPunctuationOnly(clauseReading))
            {
                if (HasSentenceBreak(clauseText) || (HasClauseComma(clauseText) && (i == 0 || !EndsWithSubjectToken(result[i - 1].Text))))
                    currentSentence.Clear();
                continue;
            }

            var hasSentenceBreak = HasSentenceBreak(clauseText) || HasSentenceBreak(clauseReading);
            var nextIsPunct = i + 1 < result.Count && IsPunctuationOnly(result[i + 1].Text);
            var nextIsSentenceBreak = nextIsPunct && HasSentenceBreak(result[i + 1].Text);
            var hasNextContent = i < lastContentIndex;
            // 読点 (、) の後ろに続きの文節があるときは途中節なので文末扱いにしない (「見て、連絡する」を「ご覧ください、ご連絡いたします」にしない)
            var isSentenceEnd = !hasNextContent || hasSentenceBreak || nextIsSentenceBreak;
            var nextIsQuestion = nextIsPunct && IsQuestion(result[i + 1].Text);

            var prevReading = i > 0 ? result[i - 1].Reading : "";
            var nextReading = i + 1 < result.Count ? result[i + 1].Reading : "";
            var nextText = i + 1 < result.Count ? result[i + 1].Text : "";

            // 次の文節と合わせてすでに敬語になっている場合は二重敬語を防ぐ
            if (!nextIsPunct && IsAlreadyFollowedByPolite(clauseReading, nextReading, nextText)) continue;

            var subjectRole = DetectSubjectRole(currentSentence);
            var gratitudeFollows = i + 1 < result.Count && IsGratitudeFollowing(nextReading, nextText);

            if (ConvertClause(clauseReading, clauseText, isSentenceEnd, nextIsQuestion, gratitudeFollows, subjectRole, prevReading, hasNextContent) is { } converted && converted != clauseText)
            {
                result[i] = WithKeigo(result[i], converted);
            }

            if (HasSentenceBreak(clauseText) || HasSentenceBreak(clauseReading) ||
                (HasClauseComma(clauseText) && !EndsWithSubjectToken(clauseText)))
            {
                currentSentence.Clear();
            }
        }

        return result;
    }

    private static List<Clause> MergeMultiClauseIdioms(IReadOnlyList<Clause> clauses)
    {
        var mergedList = new List<Clause>(clauses);
        for (var i = 0; i < mergedList.Count - 1; i++)
        {
            var maxSpan = Math.Min(3, mergedList.Count - i);
            for (var span = maxSpan; span >= 2; span--)
            {
                var reading = string.Concat(mergedList.Skip(i).Take(span).Select(c => c.Reading));
                var (core, trail) = SplitTrailingPunctuation(reading);
                if (IsQuestion(trail)) continue;

                var isAtEnd = i + span == mergedList.Count || IsPunctuationOnly(mergedList[i + span].Text) || HasSentenceBreak(trail);
                string? matched = null;
                if (ReadingWords.TryGetValue(core, out var w)) matched = w;
                else if (isAtEnd && TailReadingMap.TryGetValue(core, out var t)) matched = t;

                if (matched is null) continue;

                var original = string.Concat(mergedList.Skip(i).Take(span).Select(c => c.Text));
                var keigo = matched + trail;
                var merged = new Clause(reading, false, keigo == original ? [keigo] : [keigo, original]) { Engine = original };
                merged.NotLearned.Add(keigo);
                mergedList.RemoveRange(i, span);
                mergedList.Insert(i, merged);
                break;
            }
        }
        return mergedList;
    }

    private static bool IsGratitudeFollowing(string nextReading, string nextText)
    {
        if (nextReading.Length == 0) return false;
        foreach (var g in GratitudeFollowingReadings)
            if (nextReading.StartsWith(g, StringComparison.Ordinal)) return true;
        return nextText.StartsWith("感謝", StringComparison.Ordinal) || nextText.StartsWith("恐縮", StringComparison.Ordinal);
    }

    private static bool HasClauseComma(string text) =>
        text.Length > 0 && text[^1] is '、' or '，' or ',';

    private static bool EndsWithSubjectToken(ReadOnlySpan<char> text)
    {
        var core = text.TrimEnd(['、', '，', ',']);
        foreach (var token in OtherSubjectTokens)
            if (core.EndsWith(token, StringComparison.Ordinal)) return true;
        foreach (var token in SelfSubjectTokens)
            if (core.EndsWith(token, StringComparison.Ordinal)) return true;
        return false;
    }

    private static ReadOnlySpan<char> LastSentenceOf(string? before)
    {
        if (string.IsNullOrEmpty(before)) return [];
        var lastBreak = before.LastIndexOfAny(['\r', '\n', '。', '！', '？', '!', '?']);
        var sentence = lastBreak >= 0 ? before.AsSpan(lastBreak + 1) : before.AsSpan();
        // 「お客様がお越しになりますので、」のように読点で節が切れている場合は主語を引き継がない (「お客様は、」のような主語直後は保持)
        var lastComma = sentence.LastIndexOfAny(['、', '，', ',']);
        if (lastComma >= 0 && !EndsWithSubjectToken(sentence[..lastComma]))
            return sentence[(lastComma + 1)..];
        return sentence;
    }

    private static bool HasSentenceBreak(string text) =>
        text.Length > 0 && text[^1] is '。' or '！' or '？' or '!' or '?';

    // 主語の種類: 相手・目上が主語 / 自分側が主語 / 主語の手がかりなし
    private enum SubjectRole { Unknown, Other, Self }

    private static SubjectRole DetectSubjectRole(StringBuilder sentence)
    {
        var len = sentence.Length;
        if (len < 2) return SubjectRole.Unknown;
        if (len <= 256)
        {
            Span<char> buf = stackalloc char[len];
            sentence.CopyTo(0, buf, len);
            return DetectSubjectRole(buf);
        }
        return DetectSubjectRole(sentence.ToString());
    }

    private static SubjectRole DetectSubjectRole(ReadOnlySpan<char> text)
    {
        if (text.Length < 2 || !text.ContainsAny(SubjectSearchValues))
            return SubjectRole.Unknown;
        var lastOther = LastTokenEnd(text, OtherSubjectTokens);
        var lastSelf = LastTokenEnd(text, SelfSubjectTokens);
        if (lastOther < 0 && lastSelf < 0) return SubjectRole.Unknown;
        return lastOther > lastSelf ? SubjectRole.Other : SubjectRole.Self;
    }

    private static int LastTokenEnd(ReadOnlySpan<char> text, string[] tokens)
    {
        var best = -1;
        foreach (var token in tokens)
        {
            var idx = text.LastIndexOf(token, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var end = idx + token.Length;
                if (end > best) best = end;
            }
        }
        return best;
    }

    private static bool IsAlreadyFollowedByPolite(string reading, string nextReading, string nextText)
    {
        if (nextReading.Length == 0) return false;
        // ありがとう + ございます / よろしく + お願いします / お疲れ + 様です / 大丈夫 + ですか
        if (reading is "ありがとう" or "ありがと" && nextReading.StartsWith("ござい", StringComparison.Ordinal)) return true;
        if (reading is "よろしく" or "おねがい" && (nextReading.StartsWith("おねがい", StringComparison.Ordinal) || nextReading.StartsWith("いたし", StringComparison.Ordinal))) return true;
        if (reading is "おつかれ" or "おつかれさま" && (nextReading.StartsWith("さま", StringComparison.Ordinal) || nextReading.StartsWith("です", StringComparison.Ordinal))) return true;
        if (reading is "だいじょうぶ" or "もんだいない" && (nextReading.StartsWith("です", StringComparison.Ordinal) || nextText.StartsWith("です", StringComparison.Ordinal))) return true;
        // お世話 + になっております / お待たせ + いたしました / 失礼 + いたします / お先に + 失礼します 等での重複表現を防ぐ
        if (reading is "おせわ" && (nextReading.StartsWith("にな", StringComparison.Ordinal) || nextText.StartsWith("にな", StringComparison.Ordinal))) return true;
        if (reading is "おまたせ" or "しつれい" && (nextReading.StartsWith("いたし", StringComparison.Ordinal) || nextReading.StartsWith("しま", StringComparison.Ordinal))) return true;
        if (reading is "おわび" or "かんしゃ" && (nextReading.StartsWith("もうし", StringComparison.Ordinal) || nextReading.StartsWith("いたし", StringComparison.Ordinal) || nextReading.StartsWith("しま", StringComparison.Ordinal))) return true;
        if (reading is "おてすう" && (nextReading.StartsWith("をおかけ", StringComparison.Ordinal) || nextReading.StartsWith("ですが", StringComparison.Ordinal))) return true;
        if (reading is "おさきに" && (nextReading.StartsWith("しつれい", StringComparison.Ordinal) || nextText.StartsWith("失礼", StringComparison.Ordinal))) return true;
        if (reading is "りょうかい" && (nextReading.StartsWith("いたし", StringComparison.Ordinal) || nextReading.StartsWith("しま", StringComparison.Ordinal) || nextReading.StartsWith("です", StringComparison.Ordinal))) return true;
        return false;
    }

    private static string? ConvertClause(string reading, string text, bool isSentenceEnd, bool nextIsQuestion, bool gratitudeFollows, SubjectRole subjectRole, string prevReading, bool hasNextContent)
    {
        var (readingCore, readingPunct) = SplitTrailingPunctuation(reading);
        var (textCore, textPunct) = SplitTrailingPunctuation(text);
        var punct = textPunct.Length > 0 ? textPunct : readingPunct;
        if (readingCore.Length == 0) return null;

        var prevEndsWithTe = prevReading.EndsWith('て') || prevReading.EndsWith('で');
        var isQuestion = IsQuestion(punct) || nextIsQuestion;

        // 0. 疑問文 (？ / ?) は主語に応じて切り替える (自分主語「私が送った？」→ お送りいたしましたでしょうか？ / それ以外 → 尊敬・相手への質問)
        if (isSentenceEnd && isQuestion)
            return ConvertQuestion(readingCore, textCore, punct, subjectRole);

        // 0.3 「〜てくれて」の振り分け (後ろに感謝・助かりが続く → 「〜いただき」、目上主語や経過接続 → 「〜くださり / くださいまして」)
        if (textCore.EndsWith("くれて", StringComparison.Ordinal) || readingCore.EndsWith("くれて", StringComparison.Ordinal))
        {
            if (TryConvertTekurete(textCore, gratitudeFollows, subjectRole) is { } tekurete)
                return tekurete + punct;
        }

        // 0.5 相手・目上が主語の文末は尊敬語を優先する (社長が見る → ご覧になります、お客様が来ている → お越しになっています)
        if (isSentenceEnd && subjectRole == SubjectRole.Other && !IsAlreadyKeigo(textCore))
        {
            // 漢字変換済みの同音異義語 (着た vs 来た) を区別するため、テキスト末尾ルールを先に照合する
            var respText = ApplyInTextReplacements(textCore);
            if (respText.Length > 0 && RespectfulTextSuffixBuckets.TryGetValue(respText[^1], out var respBucket))
            {
                foreach (var (suffix, replacement) in respBucket)
                {
                    if (respText.EndsWith(suffix, StringComparison.Ordinal))
                        return AppendWithoutDuplicatePrefix(respText[..^suffix.Length], replacement) + punct;
                }
            }
            if (RespectfulReadingMap.TryGetValue(readingCore, out var respWord))
                return respWord + punct;
        }

        // 1. 前の文節が「〜て / 〜で」で、この文節が補助動詞のとき (確認して + ほしい → いただけますでしょうか)
        if (isSentenceEnd && prevEndsWithTe)
        {
            if (readingCore is "ほしい" or "くれ" or "ちょうだい") return "いただけますでしょうか" + punct;
            if (readingCore is "いる" or "る") return "おります" + punct;
            if (readingCore is "いた" or "た") return "おりました" + punct;
            if (readingCore is "もらえる") return "いただけますでしょうか" + punct;
            if (readingCore is "もらった") return "いただきました" + punct;
            if (readingCore is "もらう") return "いただきます" + punct;
            if (readingCore is "くれた") return "くださいました" + punct;
            if (readingCore is "くれる") return "くださいます" + punct;
            if (readingCore is "いい") return "よろしいでしょうか" + punct;
        }

        // 2. 単語・定型句の完全一致
        if (ReadingWords.TryGetValue(readingCore, out var word))
            return word + punct;
        if (isSentenceEnd && TailReadingMap.TryGetValue(readingCore, out var tailWord))
            return tailWord + punct;

        // 3. 語頭接頭辞 (+ 助詞) の一致と、残りの部分の再帰変換 (きょうはいく → 本日は + 伺います)
        if (TryConvertWithReadingPrefix(readingCore, rest => ConvertReadingRemainder(rest, isSentenceEnd)) is { } prefixed)
            return prefixed + punct;

        // 4. 変換後テキスト内の語彙と文末表現・複文途中節の変換
        var rewritten = ApplyInTextReplacements(textCore);

        // 途中の文節だけでなく、「修正したから、」のように読点直後でまだ続きを打っていない段階でも複文接続を即時適用する
        var hasTrailingComma = punct is "、" or "，" or ",";
        if ((!isSentenceEnd || hasTrailingComma) && rewritten.Length >= 2)
        {
            rewritten = ApplyMidClausePolite(rewritten, subjectRole, prevReading);
        }

        if (isSentenceEnd && !hasTrailingComma)
        {
            var suffixApplied = false;
            if (rewritten.Length > 0 && !IsAlreadyKeigo(rewritten))
            {
                if (TailTextSuffixBuckets.TryGetValue(rewritten[^1], out var bucket))
                {
                    foreach (var (suffix, replacement) in bucket)
                    {
                        if (rewritten.EndsWith(suffix, StringComparison.Ordinal) &&
                            (rewritten.Length > suffix.Length || suffix.Length >= 2))
                        {
                            rewritten = AppendWithoutDuplicatePrefix(rewritten[..^suffix.Length], replacement);
                            suffixApplied = true;
                            break;
                        }
                    }
                }

                if (!suffixApplied && TrySuruOrGodanSa(rewritten) is { } suruOrGodan)
                {
                    rewritten = suruOrGodan;
                    suffixApplied = true;
                }
            }

            if (!suffixApplied && !IsAlreadyPolite(rewritten))
            {
                // 名詞・形容動詞語幹 + だ (予定だ → 予定です、テストだ → テストです)。動詞過去形 (読んだ・進んだ) は除外
                if (rewritten.Length >= 2 && rewritten.EndsWith('だ') && IsNominalStem(rewritten[^2]))
                {
                    rewritten = rewritten[..^1] + "です";
                }
                // 文末の い形容詞 (難しい → 難しいです、難しかった → 難しかったです)
                else if (TryPoliteAdjective(rewritten) is { } politeAdj)
                {
                    rewritten = politeAdj;
                }
                // 文末の「〜て」依頼形 (教えて → 教えてください、確認して → 確認してください)。後ろに実質の文節があるときや、名詞+助詞「で」・副詞/接続の「〜て」には付けない
                else if (!hasNextContent && IsRequestTeForm(rewritten))
                {
                    rewritten += "ください";
                }
            }
        }

        return rewritten != textCore ? rewritten + punct : null;
    }

    private static bool IsRequestTeForm(string text)
    {
        if (text.Length < 2) return false;
        foreach (var nonRequest in NonRequestTeEndings)
        {
            if (text.EndsWith(nonRequest, StringComparison.Ordinal)) return false;
        }
        if (text.EndsWith("して", StringComparison.Ordinal)) return true;
        if (IsKanaOnly(text)) return false;
        // 「会議で」「予定で」のような名詞+助詞「で」の誤爆を防ぐため、「で」は五段撥音便・イ音便 (んで/いで) のみ対象にする
        if (text.EndsWith('で'))
            return text.EndsWith("んで", StringComparison.Ordinal) || text.EndsWith("いで", StringComparison.Ordinal);
        return text.EndsWith('て');
    }

    private static string ApplyMidClausePolite(string text, SubjectRole subjectRole, string prevReading)
    {
        var prevEndsWithTe = prevReading.EndsWith('て') || prevReading.EndsWith('で');
        if (!prevEndsWithTe)
        {
            if (text is "あれば" or "あったら") return "ございましたら";
            if (text is "なければ" or "なかったら") return "ございませんでしたら";
        }
        if (subjectRole == SubjectRole.Other && RespectfulMidClauseSuffixBuckets.TryGetValue(text[^1], out var respBucket))
        {
            foreach (var (suffix, replacement) in respBucket)
            {
                if (text.EndsWith(suffix, StringComparison.Ordinal))
                    return AppendWithoutDuplicatePrefix(text[..^suffix.Length], replacement);
            }
        }
        if (MidClauseSuffixBuckets.TryGetValue(text[^1], out var bucket))
        {
            foreach (var (suffix, replacement) in bucket)
            {
                if (!text.EndsWith(suffix, StringComparison.Ordinal)) continue;
                var stem = text[..^suffix.Length];
                // 「だけど」「だから」は名詞・形容動詞語幹のみ (動詞過去形「読んだから」を誤変換しない)
                if (suffix is "だけど" or "だけれど" or "だから" && (stem.Length == 0 || !IsNominalStem(stem[^1])))
                    continue;
                return stem + replacement;
            }
        }
        // サ変動詞の複文接続 (修正したから → 修正いたしましたので、完了したけど → 完了いたしましたが)
        foreach (var (suffix, replacement) in (ReadOnlySpan<(string, string)>)[("したので", "いたしましたので"), ("したから", "いたしましたので"), ("したけど", "いたしましたが"), ("したけれど", "いたしましたが"), ("するので", "いたしますので"), ("するから", "いたしますので"), ("するけど", "いたしますが")])
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal) && IsVerbalNounStem(text.AsSpan(0, text.Length - suffix.Length)))
                return text[..^suffix.Length] + replacement;
        }
        return text;
    }

    /// <summary>
    /// 「〜てくれて」を文脈に応じて振り分ける:
    ///   - 後ろに感謝・恩恵 (ありがとう・助かった・嬉しい等) が続くとき → 「〜いただき」
    ///   - 目上の主語が明示されている経過節のとき → 「〜くださり」
    ///   - それ以外の途中節 → 「〜いただき」(主語を問わず自然で汎用性が高い)
    /// </summary>
    private static string? TryConvertTekurete(string textCore, bool gratitudeFollows, SubjectRole subjectRole)
    {
        var aux = gratitudeFollows ? "いただき" : (subjectRole == SubjectRole.Other ? "くださり" : "いただき");

        foreach (var (suffix, respectfulStem) in TekureteVerbs)
        {
            if (textCore.EndsWith(suffix, StringComparison.Ordinal))
            {
                var head = textCore[..^suffix.Length];
                return AppendWithoutDuplicatePrefix(head, respectfulStem) + aux;
            }
        }

        // 汎用サ変動詞 (修正してくれて → 修正いただき / ご修正くださり 等)
        if (textCore.EndsWith("してくれて", StringComparison.Ordinal))
        {
            var stem = textCore[..^"してくれて".Length];
            if (IsVerbalNounStem(stem))
                return stem + aux;
        }
        // 一般の「〜てくれて / 〜でくれて」(作ってくれて → 作っていただき / 作ってくださり)
        if (textCore.Length > "くれて".Length && (textCore.EndsWith("てくれて", StringComparison.Ordinal) || textCore.EndsWith("でくれて", StringComparison.Ordinal)))
        {
            return textCore[..^"くれて".Length] + aux;
        }
        return null;
    }

    private static string? TrySuruOrGodanSa(string text)
    {
        foreach (var (suffix, suruReplacement, godanSaReplacement) in SuruOrGodanSuffixes)
        {
            if (!text.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var stem = text[..^suffix.Length];
            if (IsVerbalNounStem(stem))
                return stem + suruReplacement;
            if (godanSaReplacement is not null && stem.Length >= 1 && !IsKanaOnly(stem))
                return stem + godanSaReplacement;
            return null;
        }
        return null;
    }

    // サ変動詞の語幹か: 末尾が 2 文字以上の漢字熟語またはカタカナ語 (完了・設定・テスト・ログイン など。押・貸・思い出 などの五段サ行は除外)
    private static bool IsVerbalNounStem(ReadOnlySpan<char> stem)
    {
        if (stem.Length < 2) return false;
        var a = stem[^2];
        var b = stem[^1];
        var bothKanji = IsKanji(a) && IsKanji(b);
        var bothKatakana = IsKatakana(a) && IsKatakana(b);
        return bothKanji || bothKatakana;
    }

    private static bool IsKanji(char c) => c is >= '一' and <= '鿿' or '々';
    private static bool IsKatakana(char c) => c is (>= 'ァ' and <= 'ヺ') or 'ー';

    private static string? ConvertQuestion(string readingCore, string textCore, string punct, SubjectRole subjectRole)
    {
        // 自分が主語の疑問文 (私が送った？ / 弊社が確認した？) は謙譲語 + でしょうか にする
        if (subjectRole == SubjectRole.Self)
        {
            if (TailReadingMap.TryGetValue(readingCore, out var selfKeigo))
                return selfKeigo + "でしょうか" + punct;
            var selfText = ApplyInTextReplacements(textCore);
            if (selfText.Length > 0 && TailTextSuffixBuckets.TryGetValue(selfText[^1], out var bucket))
            {
                foreach (var (suffix, replacement) in bucket)
                {
                    if (selfText.EndsWith(suffix, StringComparison.Ordinal))
                        return AppendWithoutDuplicatePrefix(selfText[..^suffix.Length], replacement) + "でしょうか" + punct;
                }
            }
        }

        if (QuestionReadingMap.TryGetValue(readingCore, out var qWord))
            return qWord + punct;

        // 語頭接頭辞 + 疑問の残り (きょうはいく？ → 本日は行かれますでしょうか？)
        if (TryConvertWithReadingPrefix(readingCore, tail => QuestionReadingMap.GetValueOrDefault(tail)) is { } prefixedQuestion)
            return prefixedQuestion + punct;

        var rewritten = ApplyInTextReplacements(textCore);
        if (!rewritten.EndsWith("でしょうか", StringComparison.Ordinal) &&
            !rewritten.EndsWith("ますか", StringComparison.Ordinal) &&
            !rewritten.EndsWith("ましたか", StringComparison.Ordinal) &&
            !rewritten.EndsWith("ですか", StringComparison.Ordinal) &&
            !rewritten.EndsWith("ませんか", StringComparison.Ordinal))
        {
            var matched = false;
            if (rewritten.Length > 0 && QuestionSuffixBuckets.TryGetValue(rewritten[^1], out var qBucket))
            {
                foreach (var (suffix, replacement) in qBucket)
                {
                    if (rewritten.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        rewritten = AppendWithoutDuplicatePrefix(rewritten[..^suffix.Length], replacement);
                        matched = true;
                        break;
                    }
                }
            }

            if (!matched)
            {
                // サ変動詞 + した？ / する？ (完了した？ → 完了されましたでしょうか？)
                if (rewritten.EndsWith("した", StringComparison.Ordinal) && IsVerbalNounStem(rewritten.AsSpan(0, rewritten.Length - 2)))
                    rewritten = rewritten[..^2] + "されましたでしょうか";
                else if (rewritten.EndsWith("する", StringComparison.Ordinal) && IsVerbalNounStem(rewritten.AsSpan(0, rewritten.Length - 2)))
                    rewritten = rewritten[..^2] + "されますでしょうか";
                else if (rewritten.Length >= 2 && rewritten.EndsWith('だ') && IsNominalStem(rewritten[^2]))
                    rewritten = rewritten[..^1] + "でしょうか";
                else if (TryPoliteAdjective(rewritten) is not null)
                    rewritten += "でしょうか";
            }
        }

        return rewritten != textCore ? rewritten + punct : null;
    }

    /// <summary>「ご連絡」の直後に「ご連絡いたします」が重なって「ごご連絡いたします」になる二重接頭辞を防ぐ。</summary>
    private static string AppendWithoutDuplicatePrefix(string stem, string replacement)
    {
        if (stem.Length > 0 && replacement.Length > 0)
        {
            if ((stem.EndsWith('ご') && replacement.StartsWith('ご')) ||
                (stem.EndsWith('お') && replacement.StartsWith('お')))
            {
                return stem[..^1] + replacement;
            }
        }
        return stem + replacement;
    }

    private static string ApplyInTextReplacements(string text)
    {
        // 高速フィルタ: 候補語彙が 1 つも含まれない大半の文節は 0 アロケーションで即返す
        if (text.Length < 2 || !text.AsSpan().ContainsAny(InTextSearchValues))
            return text;

        var rewritten = text;
        foreach (var (from, to, requireEndBoundary) in InTextReplacements)
        {
            var searchFrom = 0;
            while (searchFrom <= rewritten.Length - from.Length)
            {
                var idx = rewritten.IndexOf(from, searchFrom, StringComparison.Ordinal);
                if (idx < 0) break;
                if (IsReplacementBoundary(rewritten, idx) && (!requireEndBoundary || IsReplacementEndBoundary(rewritten, idx + from.Length)))
                {
                    // 「問題ないかも」「問題ないかと存じます」「問題ないでしょう」等を「問題ございません〜」に再置換しない
                    if (from is "問題ない" or "大丈夫" && idx + from.Length < rewritten.Length &&
                        rewritten[idx + from.Length] is 'か' or 'で' or 'だ' or 'と' or 'は' or 'み' or 'そ')
                    {
                        searchFrom = idx + 1;
                        continue;
                    }
                    rewritten = string.Concat(rewritten.AsSpan(0, idx), to, rewritten.AsSpan(idx + from.Length));
                    searchFrom = idx + to.Length;
                }
                else
                {
                    searchFrom = idx + 1;
                }
            }
        }
        return rewritten;
    }

    /// <summary>「午後で」「早急に」「只今から」のように別の漢字熟語の途中へ部分一致する誤爆を防ぐ。</summary>
    private static bool IsReplacementBoundary(string text, int index)
    {
        if (index == 0) return true;
        var prev = text[index - 1];
        return prev is '、' or '。' or '，' or '．' or ',' or '.' or '！' or '？' or '!' or '?' or ' ' or '　' or '「' or '（' or '('
            or 'は' or 'が' or 'を' or 'に' or 'で' or 'と' or 'も' or 'の' or 'へ' or 'か' or 'て';
    }

    /// <summary>「客室」「観客」「皆既日食」のように後ろに別の漢字が続く熟語への誤爆を防ぐ。</summary>
    private static bool IsReplacementEndBoundary(string text, int endIndex)
    {
        if (endIndex >= text.Length) return true;
        var next = text[endIndex];
        return next is '、' or '。' or '，' or '．' or ',' or '.' or '！' or '？' or '!' or '?' or ' ' or '　' or '」' or '）' or ')'
            or 'は' or 'が' or 'を' or 'に' or 'で' or 'と' or 'も' or 'の' or 'へ' or 'か' or 'だ' or 'で';
    }

    private static string? TryPoliteAdjective(string text)
    {
        if (text.Length < 2 || text[^1] is not ('い' or 'た')) return null;
        foreach (var ending in PoliteAdjectiveEndings)
        {
            if (text.EndsWith(ending, StringComparison.Ordinal))
                return text + "です";
        }
        return null;
    }

    private static bool IsAlreadyKeigo(string text) =>
        text.Length > 0 && text[^1] is 'す' or 'た' or 'ん' or 'か' or 'い' &&
        AlreadyKeigoEndings.Any(e => text.EndsWith(e, StringComparison.Ordinal));

    private static bool IsAlreadyPolite(string text) =>
        text.Length > 0 && text[^1] is 'す' or 'た' or 'ん' or 'か' or 'い' or 'せ' or 'う' &&
        AlreadyPoliteEndings.Any(e => text.EndsWith(e, StringComparison.Ordinal));

    // 「〜だ → 〜です」を安全に適用できる直前文字 (漢字・カタカナ・英数字・一部の記号。ひらがなの「んだ」「まだ」は除外)
    private static bool IsNominalStem(char c) =>
        c is (>= '一' and <= '鿿') or (>= 'ァ' and <= 'ヺ') or 'ー' or '%' or '％' or '）' or ')' or '」' || char.IsAsciiLetterOrDigit(c);

    private static bool IsPunctuationChar(char c) =>
        c is '。' or '、' or '．' or '，' or '.' or ',' or '！' or '？' or '!' or '?' or '…' or '‥';

    private static bool IsPunctuationOnly(string text) =>
        text.Length > 0 && text.All(IsPunctuationChar);

    private static bool IsQuestion(string punct) =>
        punct.Contains('？') || punct.Contains('?');

    private static string? ConvertReadingRemainder(string reading, bool isSentenceEnd)
    {
        if (ReadingWords.TryGetValue(reading, out var word)) return word;
        if (isSentenceEnd && TailReadingMap.TryGetValue(reading, out var tail)) return tail;
        return TryConvertWithReadingPrefix(reading, rest => ConvertReadingRemainder(rest, isSentenceEnd));
    }

    private static string? TryConvertWithReadingPrefix(string reading, Func<string, string?> convertRest)
    {
        if (reading.Length == 0 || !ReadingPrefixBuckets.TryGetValue(reading[0], out var bucket))
            return null;
        foreach (var (readingPrefix, keigoPrefix, allowsParticle) in bucket)
        {
            if (!reading.StartsWith(readingPrefix, StringComparison.Ordinal)) continue;
            var rest = reading[readingPrefix.Length..];
            if (rest.Length == 0) return keigoPrefix;
            if (allowsParticle)
            {
                foreach (var particle in Particles)
                {
                    if (!rest.StartsWith(particle, StringComparison.Ordinal)) continue;
                    var tail = rest[particle.Length..];
                    if (tail.Length == 0) return keigoPrefix + particle;
                    if (convertRest(tail) is { } convertedTail)
                        return keigoPrefix + particle + convertedTail;
                }
            }
            if (convertRest(rest) is { } convertedRest)
                return keigoPrefix + convertedRest;
        }
        return null;
    }

    private static bool IsKanaOnly(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c is not ((>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー'))
                return false;
        }
        return true;
    }

    private static (string Core, string Punctuation) SplitTrailingPunctuation(string text)
    {
        if (text.Length == 0 || !IsPunctuationChar(text[^1]))
            return (text, "");
        var end = text.Length - 1;
        while (end > 0 && IsPunctuationChar(text[end - 1]))
            end--;
        return (text[..end], text[end..]);
    }

    private static Clause WithKeigo(Clause source, string keigo)
    {
        var candidates = new List<string>(source.Candidates.Count + 1) { keigo };
        foreach (var candidate in source.Candidates)
        {
            if (!string.Equals(candidate, keigo, StringComparison.Ordinal))
                candidates.Add(candidate);
        }
        var copy = new Clause(source.Reading, source.IsEnglish, candidates)
        {
            Engine = source.Engine,
            EngineCandidatesAdded = source.EngineCandidatesAdded,
            Expanded = source.Expanded,
            Fixed = source.Fixed,
            Leading = source.Leading,
            Notes = source.Notes,
            Raw = source.Raw,
            Kana = source.Kana,
            Alternatives = source.Alternatives,
            Changed = source.Changed,
            ZenzaiText = source.ZenzaiText,
            Index = 0,
        };
        copy.Translations.UnionWith(source.Translations);
        copy.NotLearned.UnionWith(source.NotLearned);
        copy.NotLearned.Add(keigo);
        return copy;
    }
}
