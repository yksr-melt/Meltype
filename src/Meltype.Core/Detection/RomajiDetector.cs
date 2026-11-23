// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;

namespace Meltype.Detection;

internal readonly record struct RomajiToken(string Romaji, string Kana);

/// <summary>ローマ字としての解析結果。入力途中 (prefix) を前提にしている。</summary>
internal sealed record RomajiAnalysis(
    bool IsValid,
    IReadOnlyList<RomajiToken> Tokens,
    string Partial,
    string? InvalidReason,
    int StrongYouon,
    int Tsu,
    int Sokuon,
    int LongVowels)
{
    public string Kana => string.Concat(Tokens.Select(t => t.Kana));
}

/// <summary>
/// ローマ字 → かな変換の可能性を評価する (設計書 §13)。
/// ここで分かるのは「ローマ字として成立するか」と日本語らしい特徴だけで、
/// 成立しても英語の可能性は残る (kana, sushi, radio)。最終判断は ScoreEngine が行う。
/// </summary>
internal sealed class RomajiDetector
{
    // かな → 綴り (先頭が標準の綴り)。l / x / v 系は英語と区別できないので意図的に含めない。
    private static readonly (string Kana, string[] Spellings)[] Table =
    [
        ("あ", ["a"]), ("い", ["i", "yi"]), ("う", ["u", "whu", "wu"]), ("え", ["e"]), ("お", ["o"]),
        ("か", ["ka"]), ("き", ["ki"]), ("く", ["ku"]), ("け", ["ke"]), ("こ", ["ko"]),
        ("きゃ", ["kya"]), ("きゅ", ["kyu"]), ("きょ", ["kyo"]),
        ("さ", ["sa"]), ("し", ["shi", "si"]), ("す", ["su"]), ("せ", ["se", "ce"]), ("そ", ["so"]),
        ("しゃ", ["sha", "sya"]), ("しゅ", ["shu", "syu"]), ("しょ", ["sho", "syo"]), ("しぇ", ["she", "sye"]),
        ("た", ["ta"]), ("ち", ["chi", "ti"]), ("つ", ["tsu", "tu"]), ("て", ["te"]), ("と", ["to"]),
        ("ちゃ", ["cha", "tya", "cya"]), ("ちゅ", ["chu", "tyu", "cyu"]), ("ちょ", ["cho", "tyo", "cyo"]), ("ちぇ", ["che", "tye", "cye"]),
        ("てゃ", ["tha"]), ("てゅ", ["thu"]), ("てょ", ["tho"]),
        ("な", ["na"]), ("に", ["ni"]), ("ぬ", ["nu"]), ("ね", ["ne"]), ("の", ["no"]),
        ("にゃ", ["nya"]), ("にゅ", ["nyu"]), ("にょ", ["nyo"]), ("にぃ", ["nyi"]), ("にぇ", ["nye"]),
        ("は", ["ha"]), ("ひ", ["hi"]), ("ふ", ["fu", "hu"]), ("へ", ["he"]), ("ほ", ["ho"]),
        ("ひゃ", ["hya"]), ("ひゅ", ["hyu"]), ("ひょ", ["hyo"]),
        ("ふぁ", ["fa", "hwa"]), ("ふぃ", ["fi", "fyi", "hwi"]), ("ふぇ", ["fe", "fye", "hwe"]), ("ふぉ", ["fo", "hwo"]),
        ("ふゃ", ["fya"]), ("ふゅ", ["fyu"]), ("ふょ", ["fyo"]),
        ("ま", ["ma"]), ("み", ["mi"]), ("む", ["mu"]), ("め", ["me"]), ("も", ["mo"]),
        ("みゃ", ["mya"]), ("みゅ", ["myu"]), ("みょ", ["myo"]),
        ("や", ["ya"]), ("ゆ", ["yu"]), ("よ", ["yo"]),
        ("ら", ["ra"]), ("り", ["ri"]), ("る", ["ru"]), ("れ", ["re"]), ("ろ", ["ro"]),
        ("りゃ", ["rya"]), ("りゅ", ["ryu"]), ("りょ", ["ryo"]),
        ("わ", ["wa"]), ("を", ["wo"]), ("うぉ", ["who"]),
        // Microsoft IME の wh の打ち方 (whu = う、wha = うぁ)
        ("うぁ", ["wha"]),
        ("が", ["ga"]), ("ぎ", ["gi"]), ("ぐ", ["gu"]), ("げ", ["ge"]), ("ご", ["go"]),
        ("ぎゃ", ["gya"]), ("ぎゅ", ["gyu"]), ("ぎょ", ["gyo"]),
        ("ざ", ["za"]), ("じ", ["ji", "zi"]), ("ず", ["zu"]), ("ぜ", ["ze"]), ("ぞ", ["zo"]),
        ("じゃ", ["ja", "jya", "zya"]), ("じゅ", ["ju", "jyu", "zyu"]), ("じょ", ["jo", "jyo", "zyo"]), ("じぇ", ["je", "jye", "zye"]),
        ("だ", ["da"]), ("ぢ", ["di"]), ("づ", ["du"]), ("で", ["de"]), ("ど", ["do"]),
        ("でゃ", ["dha"]), ("でゅ", ["dhu"]), ("でょ", ["dho"]),
        ("ぢゃ", ["dya"]), ("ぢゅ", ["dyu"]), ("ぢょ", ["dyo"]),
        ("ば", ["ba"]), ("び", ["bi"]), ("ぶ", ["bu"]), ("べ", ["be"]), ("ぼ", ["bo"]),
        ("びゃ", ["bya"]), ("びゅ", ["byu"]), ("びょ", ["byo"]),
        ("ぱ", ["pa"]), ("ぴ", ["pi"]), ("ぷ", ["pu"]), ("ぺ", ["pe"]), ("ぽ", ["po"]),
        ("ぴゃ", ["pya"]), ("ぴゅ", ["pyu"]), ("ぴょ", ["pyo"]),
        ("とぁ", ["twa"]), ("とぃ", ["twi"]), ("とぇ", ["twe"]), ("とぅ", ["twu"]), ("とぉ", ["two"]),
        ("どぁ", ["dwa"]), ("どぃ", ["dwi"]), ("どぇ", ["dwe"]), ("どぅ", ["dwu"]), ("どぉ", ["dwo"]),
        // Mozc のローマ字入力表にある拗音・外来音の綴り (issue #674)
        ("きぃ", ["kyi"]), ("きぇ", ["kye"]), ("ぎぃ", ["gyi"]), ("ぎぇ", ["gye"]),
        ("しぃ", ["syi"]), ("じぃ", ["zyi", "jyi"]), ("ぢぃ", ["dyi"]), ("ぢぇ", ["dye"]),
        ("ひぃ", ["hyi"]), ("ひぇ", ["hye"]), ("びぃ", ["byi"]), ("びぇ", ["bye"]), ("ぴぃ", ["pyi"]), ("ぴぇ", ["pye"]),
        ("みぃ", ["myi"]), ("みぇ", ["mye"]), ("りぃ", ["ryi"]), ("りぇ", ["rye"]),
        ("くぃ", ["kwi"]), ("くぅ", ["kwu"]), ("くぇ", ["kwe"]), ("くぉ", ["kwo"]),
        ("ぐぃ", ["gwi"]), ("ぐぅ", ["gwu"]), ("ぐぇ", ["gwe"]), ("ぐぉ", ["gwo"]),
        ("すぁ", ["swa"]), ("すぃ", ["swi"]), ("すぅ", ["swu"]), ("すぇ", ["swe"]), ("すぉ", ["swo"]),
        ("ずぁ", ["zwa"]), ("ずぃ", ["zwi"]), ("ずぅ", ["zwu"]), ("ずぇ", ["zwe"]), ("ずぉ", ["zwo"]),
    ];

    // 英語の綴りにほぼ現れない拗音 (sha/cha/ja は shut, chat, jam などで普通に出るので除外)。
    private static readonly HashSet<string> StrongYouonKana =
    [
        "きゃ", "きゅ", "きょ", "にゃ", "にゅ", "にょ", "ひゃ", "ひゅ", "ひょ", "みゃ", "みゅ", "みょ",
        "りゃ", "りゅ", "りょ", "ぎゃ", "ぎゅ", "ぎょ", "びゃ", "びゅ", "びょ", "ぴゃ", "ぴゅ", "ぴょ",
        "ちぇ",
        "てゃ", "てゅ", "てょ", "でゃ", "でゅ", "でょ", "にぃ", "にぇ",
        "とぁ", "とぃ", "とぇ", "とぅ", "どぁ", "どぃ", "どぇ", "どぅ",
    ];

    // 変換ボックスでだけ使う綴り (小書き文字・外来音)。英語かどうかの判定には使わない
    // (l / x / v を読めるようにすると hello や live までローマ字として成立してしまう)。
    private static readonly (string Kana, string[] Spellings)[] CompositionTable =
    [
        ("ぁ", ["xa", "la"]), ("ぃ", ["xi", "li", "xyi", "lyi"]), ("ぅ", ["xu", "lu"]), ("ぇ", ["xe", "le", "xye", "lye"]), ("ぉ", ["xo", "lo"]),
        ("ゃ", ["xya", "lya"]), ("ゅ", ["xyu", "lyu"]), ("ょ", ["xyo", "lyo"]), ("っ", ["xtu", "ltu", "xtsu", "ltsu"]), ("ゎ", ["xwa", "lwa"]),
        ("ゕ", ["xka", "lka"]), ("ゖ", ["xke", "lke"]), ("ん", ["xn"]),
        ("ゔぁ", ["va"]), ("ゔぃ", ["vi"]), ("ゔ", ["vu"]), ("ゔぇ", ["ve"]), ("ゔぉ", ["vo"]),
        ("いぇ", ["ye"]), ("うぃ", ["wi", "whi"]), ("うぇ", ["we", "whe"]),
        // 歴史的仮名 (Microsoft IME と同じ綴り。wi / we は ウィンドウ・ウェブ の うぃ / うぇ)
        ("ゐ", ["wyi"]), ("ゑ", ["wye"]),
        ("てぃ", ["thi"]), ("でぃ", ["dhi"]), ("てゅ", ["thu"]), ("でゅ", ["dhu"]), ("とぅ", ["twu"]), ("どぅ", ["dwu"]),
        ("てゃ", ["tha"]), ("てぇ", ["the"]), ("てょ", ["tho"]), ("でゃ", ["dha"]), ("でぇ", ["dhe"]), ("でょ", ["dho"]),
        ("にぃ", ["nyi"]), ("にぇ", ["nye"]), ("とぁ", ["twa"]), ("とぃ", ["twi"]), ("とぇ", ["twe"]),
        ("どぁ", ["dwa"]), ("どぃ", ["dwi"]), ("どぇ", ["dwe"]), ("くぃ", ["qi"]), ("くぇ", ["qe"]), ("くぉ", ["qo"]),
        // c 行 (Microsoft IME と同じ。cake や code まで日本語として読めてしまうので判定には使わない)
        ("か", ["ca"]), ("し", ["ci"]), ("く", ["cu"]), ("こ", ["co"]),
        ("ちぃ", ["cyi", "tyi"]),
        ("くぁ", ["kwa"]), ("ぐぁ", ["gwa"]), ("つぁ", ["tsa"]), ("つぃ", ["tsi"]), ("つぇ", ["tse"]), ("つぉ", ["tso"]),
    ];

    private static readonly SpellingTrie Spellings = new(Pairs(Table));
    // 同じ綴りは後の表が上書きする (CompositionTable を優先する)
    private static readonly SpellingTrie CompositionSpellings = new(Pairs([.. Table, .. CompositionTable]));
    private static readonly Dictionary<string, string[]> KanaToSpellings = Table.ToDictionary(e => e.Kana, e => e.Spellings);

    private static IEnumerable<KeyValuePair<string, string>> Pairs((string Kana, string[] Spellings)[] table) =>
        table.SelectMany(e => e.Spellings.Select(spelling => KeyValuePair.Create(spelling, e.Kana)));

    // ユーザーのローマ字の表 (romaji.txt。AZIK などの拡張ローマ字: kz = かん、kq = かい)。組み込みの綴りより優先する。
    private readonly SpellingTrie? _custom;

    public RomajiDetector() : this(null)
    {
    }

    /// <param name="custom">ユーザーの綴り → かな (英小文字だけの綴り)。null なら組み込みの表だけ。</param>
    public RomajiDetector(IReadOnlyDictionary<string, string>? custom)
    {
        if (custom is { Count: > 0 }) _custom = new SpellingTrie(custom);
    }

    /// <summary>ユーザー辞書のフォルダーの romaji.txt を読んで作る (無ければ組み込みの表だけ)。</summary>
    public static RomajiDetector CreateDefault(string? userDictionaryDirectory) => new(LoadCustomTable(userDictionaryDirectory));

    /// <summary>
    /// ユーザーのローマ字の表 (romaji.txt)。1 行に「綴り<Tab>かな」(空白区切りも可)、行頭の # はコメント。
    /// 綴りは英小文字だけ (大文字は小文字にする)。英字以外 (; など) を含む行は読まない。
    /// </summary>
    public static Dictionary<string, string>? LoadCustomTable(string? userDictionaryDirectory)
    {
        Dictionary<string, string>? table = null;
        Config.DataFile.LoadUserText(userDictionaryDirectory, "romaji.txt", "ローマ字の表 (romaji.txt) ", text => table = ParseCustomTable(text));
        return table;
    }

    public static Dictionary<string, string> ParseCustomTable(string text)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(['\t', ' ', '　'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var spelling = parts[0].ToLowerInvariant();
            if (!spelling.All(c => c is >= 'a' and <= 'z') || parts[1].Any(c => c is >= 'a' and <= 'z')) continue;
            table[spelling] = parts[1];
        }
        return table;
    }

    private static bool IsVowel(char c) => c is 'a' or 'i' or 'u' or 'e' or 'o';
    private static bool IsConsonant(char c) => c is >= 'a' and <= 'z' && !IsVowel(c);

    internal RomajiAnalysis Analyze(string letters) => Analyze(letters, strictStart: true, composition: false);

    /// <summary>c 行 (ca / cu / co = か く こ) を k 行に読み替える (ch は そのまま)。英数状態の判定で、fucarete を fukarete として調べるのに使う。</summary>
    public static string ReadCRow(string letters)
    {
        if (!letters.Contains('c')) return letters;
        var chars = letters.ToCharArray();
        for (var i = 0; i + 1 < chars.Length; i++)
        {
            if (chars[i] == 'c' && chars[i + 1] is 'a' or 'u' or 'o') chars[i] = 'k';
        }
        return new string(chars);
    }

    /// <summary>
    /// 変換ボックス用。語の途中から解析し (語頭の「ん」「っ」も許す)、"nn" は Microsoft IME と同じく常に「ん」と読む
    /// (tanni → たんい。こんにちは は konnnichiha)。
    /// </summary>
    internal RomajiAnalysis AnalyzeFragment(string letters) => Analyze(letters, strictStart: false, composition: true);

    private RomajiAnalysis Analyze(string letters, bool strictStart, bool composition, bool allowCustomWait = true)
    {
        var tokens = new List<RomajiToken>();
        int strongYouon = 0, tsu = 0, sokuon = 0, longVowels = 0;
        var i = 0;
        var s = letters;

        RomajiAnalysis Invalid(string reason) =>
            new(false, tokens.ToArray(), "", reason, strongYouon, tsu, sokuon, longVowels);

        while (i < s.Length)
        {
            var c = s[i];
            if (c is < 'a' or > 'z') return Invalid($"'{c}' は英字ではない");

            // ユーザーのローマ字の表は、ん・っ の読み方を含めて組み込みの綴りより優先する (AZIK の kk = きん)
            if (_custom is not null && MatchCustom(s, i) is { } custom)
            {
                tokens.Add(custom);
                i += custom.Romaji.Length;
                continue;
            }

            if (c == 'n' && i + 1 < s.Length && (s[i + 1] == 'n' || (IsConsonant(s[i + 1]) && s[i + 1] != 'y')))
            {
                // ユーザーの表の綴りの打ちかけ (ngh の ng) なら、「ん」と読まずに続きを待つ (issue #979)。
                // 組み込みの綴りとして完成している語 (ka など) は待たない。対象は ん・っ だけ。
                // 語頭の「ん」をはじく確認 (strictStart) は待つ処理より先。表を登録していても語頭の ng ははじく (issue #979 レビュー)
                if (i == 0 && strictStart) return Invalid("語頭の「ん」");
                if (allowCustomWait && _custom is not null && CustomPrefixTail(s, i) is { } nWaiting)
                    return new RomajiAnalysis(true, tokens.ToArray(), nWaiting, null, strongYouon, tsu, sokuon, longVowels);
                var consumed = 1;
                if (s[i + 1] == 'n' && (composition || i + 2 >= s.Length || !(IsVowel(s[i + 2]) || s[i + 2] == 'y'))) consumed = 2;
                tokens.Add(new RomajiToken(s.Substring(i, consumed), "ん"));
                i += consumed;
                continue;
            }

            if (i + 1 < s.Length && IsConsonant(c) && c != 'n' &&
                (s[i + 1] == c || (c == 't' && s[i + 1] == 'c' && i + 2 < s.Length && s[i + 2] == 'h')))
            {
                // ユーザーの表の綴りの打ちかけ (kkq の kk) なら、「っ」と読まずに続きを待つ (issue #979)
                // 語頭の「っ」をはじく確認 (strictStart) は待つ処理より先。表を登録していても語頭の kk ははじく (issue #979 レビュー)
                if (i == 0 && strictStart) return Invalid("語頭の「っ」");
                if (allowCustomWait && _custom is not null && CustomPrefixTail(s, i) is { } sWaiting)
                    return new RomajiAnalysis(true, tokens.ToArray(), sWaiting, null, strongYouon, tsu, sokuon, longVowels);
                tokens.Add(new RomajiToken(c.ToString(), "っ"));
                sokuon++;
                i += 1;
                continue;
            }

            var spellings = composition ? CompositionSpellings : Spellings;
            var (length, kana) = spellings.Longest(s, i);
            if (kana is not null)
            {
                var piece = s.Substring(i, length);
                if (StrongYouonKana.Contains(kana)) strongYouon++;
                if (piece == "tsu") tsu++;
                if (kana == "う" && tokens.Count > 0 && EndsWithVowel(tokens[^1].Romaji, 'o', 'u')) longVowels++;
                tokens.Add(new RomajiToken(piece, kana));
                i += length;
                continue;
            }

            var rest = s[i..];
            if (spellings.IsPrefix(s, i) || rest is "n" or "tc" || _custom?.IsPrefix(s, i) == true)
            {
                return new RomajiAnalysis(true, tokens.ToArray(), rest, null, strongYouon, tsu, sokuon, longVowels);
            }
            return Invalid($"「{rest}」はローマ字として成立しない");
        }

        return new RomajiAnalysis(true, tokens.ToArray(), "", null, strongYouon, tsu, sokuon, longVowels);
    }

    /// <summary>s の i から末尾までが、ユーザーの表の綴りの打ちかけ (それ自体は綴りでない) ならその部分。無ければ null。</summary>
    private string? CustomPrefixTail(string s, int i) => _custom!.IsOnlyPrefix(s, i) ? s[i..] : null;

    /// <summary>s の i から始まる、ユーザーの表のいちばん長い綴り。無ければ null。</summary>
    private RomajiToken? MatchCustom(string s, int i)
    {
        // 表の綴りの頭で入力が終わっている (kz の k だけ) なら、続きを待つ (組み込みの綴りで読まない)
        if (_custom!.IsOnlyPrefix(s, i)) return null;
        var (length, kana) = _custom.Longest(s, i);
        return kana is null ? null : new RomajiToken(s.Substring(i, length), kana);
    }

    private static bool EndsWithVowel(string romaji, char a, char b) =>
        romaji.Length > 0 && (romaji[^1] == a || romaji[^1] == b);

    public string ConvertLenient(string letters, bool final)
    {
        var builder = new StringBuilder();
        var rest = letters;
        while (rest.Length > 0)
        {
            var analysis = Analyze(rest, strictStart: false, composition: true);
            foreach (var token in analysis.Tokens) builder.Append(token.Kana);
            if (analysis.IsValid)
            {
                var partial = analysis.Partial;
                // 確定のときは、ユーザーの表の綴りの打ちかけを待たずに組み込みの読みに戻す (ng → んg)
                if (final && _custom is not null && partial.Length > 0 && _custom.IsOnlyPrefix(partial, 0))
                {
                    var settled = Analyze(partial, strictStart: false, composition: true, allowCustomWait: false);
                    builder.Append(settled.Kana);
                    if (settled.IsValid)
                    {
                        builder.Append(settled.Partial == "n" ? "ん" : settled.Partial);
                    }
                    else
                    {
                        // 再解析で読めなかった文字は捨てずに残す (ngh → んgh、kkq → っkq)
                        var unread = settled.Tokens.Sum(t => t.Romaji.Length);
                        builder.Append(partial[unread..]);
                    }
                    break;
                }
                builder.Append(final && partial == "n" ? "ん" : partial);
                break;
            }
            var consumed = analysis.Tokens.Sum(t => t.Romaji.Length);
            builder.Append(rest[consumed]);
            rest = rest[(consumed + 1)..];
        }
        return builder.ToString();
    }

    public string ToKana(string letters)
    {
        var analysis = Analyze(letters);
        return analysis.IsValid ? analysis.Kana + analysis.Partial : letters;
    }

    internal RomajiAnalysis AnalyzeWord(string word)
    {
        var analysis = Analyze(word);
        return analysis.IsValid && analysis.Partial == "n" ? Analyze(word + "n") : analysis;
    }

    public IReadOnlyList<string> SpellingVariants(string canonical, int limit = 64)
    {
        var analysis = AnalyzeWord(canonical);
        if (!analysis.IsValid || analysis.Partial.Length > 0) return [canonical];

        var tokens = analysis.Tokens;
        var results = new List<string>();
        var builder = new StringBuilder();

        void Walk(int index)
        {
            if (results.Count >= limit) return;
            if (index == tokens.Count)
            {
                results.Add(builder.ToString());
                return;
            }
            var mark = builder.Length;
            foreach (var option in OptionsFor(tokens, index))
            {
                builder.Append(option);
                Walk(index + 1);
                builder.Length = mark;
            }
        }

        Walk(0);
        if (!results.Contains(canonical)) results.Insert(0, canonical);
        return results;
    }

    private static IEnumerable<string> OptionsFor(IReadOnlyList<RomajiToken> tokens, int index)
    {
        var token = tokens[index];
        var next = index + 1 < tokens.Count ? tokens[index + 1] : (RomajiToken?)null;
        switch (token.Kana)
        {
            case "ん":
                yield return "nn";
                if (next is null) yield return "n";
                if (next is { } n && n.Kana != "っ" && n.Romaji.Length > 0 && IsConsonant(n.Romaji[0]) && n.Romaji[0] is not ('n' or 'y'))
                    yield return "n";
                if (next is { } nn && nn.Romaji.Length > 0 && nn.Romaji[0] == 'n')
                    yield return "n";
                yield break;
            case "っ":
                if (next is { } following && KanaToSpellings.TryGetValue(following.Kana, out var spellings))
                {
                    foreach (var first in spellings.Select(s => s[0]).Distinct())
                    {
                        if (IsConsonant(first) && first != 'n') yield return first.ToString();
                    }
                }
                else yield return token.Romaji;
                yield break;
            default:
                if (KanaToSpellings.TryGetValue(token.Kana, out var options))
                {
                    foreach (var option in options) yield return option;
                }
                else yield return token.Romaji;
                yield break;
        }
    }
}
