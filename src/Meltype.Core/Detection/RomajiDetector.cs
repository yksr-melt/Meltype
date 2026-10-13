// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Collections.Concurrent;
using System.Text;

namespace Meltype.Detection;

public readonly record struct RomajiToken(string Romaji, string Kana);

/// <summary>ローマ字としての解析結果。入力途中 (prefix) を前提にしている。</summary>
public sealed record RomajiAnalysis(
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
public sealed class RomajiDetector
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
        ("ふぁ", ["fa"]), ("ふぃ", ["fi", "fyi"]), ("ふぇ", ["fe", "fye"]), ("ふぉ", ["fo"]),
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
        ("とぁ", ["twa"]), ("とぃ", ["twi"]), ("とぇ", ["twe"]), ("とぅ", ["twu"]),
        ("どぁ", ["dwa"]), ("どぃ", ["dwi"]), ("どぇ", ["dwe"]), ("どぅ", ["dwu"]),
        // Mozc のローマ字入力表にある拗音・外来音の綴り (issue #674)。英語の語の中に出る綴りは足さない: *yi (trying・studying・babying)、hw* (highway・pathway・northwest)、hye (hyena)、sw* (swan・swine)、two・bye・dye・rye、v 系、wo で終わる綴り (ping|wo)
        ("きぇ", ["kye"]), ("ぎぇ", ["gye"]), ("ぴぇ", ["pye"]), ("みぇ", ["mye"]),
        ("くぃ", ["kwi"]), ("くぅ", ["kwu"]), ("くぇ", ["kwe"]),
        ("ぐぃ", ["gwi"]), ("ぐぅ", ["gwu"]), ("ぐぇ", ["gwe"]),
        ("ずぁ", ["zwa"]), ("ずぃ", ["zwi"]), ("ずぅ", ["zwu"]), ("ずぇ", ["zwe"]),
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

    private static readonly Dictionary<string, string> SpellingToKana = BuildSpellingMap(Table);
    private static readonly Dictionary<string, string> CompositionSpellingToKana = BuildSpellingMap([.. Table, .. CompositionTable]);
    private static readonly HashSet<string> CompositionPartials = BuildPartials(CompositionSpellingToKana);
    private static readonly Dictionary<string, string[]> KanaToSpellings = Table.ToDictionary(e => e.Kana, e => e.Spellings);
    private static readonly HashSet<string> PartialSpellings = BuildPartials(SpellingToKana);

    private static Dictionary<string, string> BuildSpellingMap((string Kana, string[] Spellings)[] table)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (kana, spellings) in table)
        {
            foreach (var spelling in spellings) map[spelling] = kana;
        }
        return map;
    }

    private static HashSet<string> BuildPartials(Dictionary<string, string> spellings)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spelling in spellings.Keys)
        {
            for (var i = 1; i < spelling.Length; i++) set.Add(spelling[..i]);
        }
        return set;
    }

    // ユーザーのローマ字の表 (romaji.txt。AZIK などの拡張ローマ字: kz = かん、kq = かい)。組み込みの綴りより優先する。
    private readonly Dictionary<string, string>? _custom;
    private readonly HashSet<string> _customPartials = new(StringComparer.Ordinal);
    private readonly int _customMaxLength;

    public RomajiDetector() : this(null)
    {
    }

    /// <param name="custom">ユーザーの綴り → かな (英小文字だけの綴り)。null なら組み込みの表だけ。</param>
    public RomajiDetector(IReadOnlyDictionary<string, string>? custom)
    {
        if (custom is not { Count: > 0 }) return;
        _custom = new Dictionary<string, string>(custom, StringComparer.Ordinal);
        foreach (var spelling in _custom.Keys)
        {
            _customMaxLength = Math.Max(_customMaxLength, spelling.Length);
            for (var i = 1; i < spelling.Length; i++) _customPartials.Add(spelling[..i]);
        }
    }

    /// <summary>ユーザー辞書のフォルダーの romaji.txt を読んで作る (無ければ組み込みの表だけ)。</summary>
    public static RomajiDetector CreateDefault(string? userDictionaryDirectory) => new(LoadCustomTable(userDictionaryDirectory));

    /// <summary>
    /// ユーザーのローマ字の表 (romaji.txt)。1 行に「綴り<Tab>かな」(空白区切りも可)、行頭の # はコメント。
    /// 綴りは英小文字だけ (大文字は小文字にする)。英字以外 (; など) を含む行は読まない。
    /// </summary>
    public static Dictionary<string, string>? LoadCustomTable(string? userDictionaryDirectory)
    {
        if (userDictionaryDirectory is null) return null;
        var path = Path.Combine(userDictionaryDirectory, "romaji.txt");
        try
        {
            return File.Exists(path) ? ParseCustomTable(File.ReadAllText(path)) : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"ローマ字の表 (romaji.txt) を読めませんでした: {ex.Message}");
            return null;
        }
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

    public RomajiAnalysis Analyze(string letters) => Analyze(letters, strictStart: true, composition: false);

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
    public RomajiAnalysis AnalyzeFragment(string letters) => Analyze(letters, strictStart: false, composition: true);

    private RomajiAnalysis Analyze(string letters, bool strictStart, bool composition)
    {
        var slot = composition ? 2 : strictStart ? 0 : 1;
        var cache = composition ? _compositionCache : strictStart ? _strictCache : _looseCache;
        if (cache.TryGetValue(letters, out var cached)) return cached;
        var result = AnalyzeCore(letters, strictStart, composition);
        // ConcurrentDictionary.Count はすべてのロックを取るので、足した数を別に数える
        if (Interlocked.Increment(ref _cacheAdded[slot]) > CacheLimit)
        {
            cache.Clear();
            Interlocked.Exchange(ref _cacheAdded[slot], 0);
        }
        cache[letters] = result;
        return result;
    }

    private const int CacheLimit = 50000;
    private readonly int[] _cacheAdded = new int[3];
    private readonly ConcurrentDictionary<string, RomajiAnalysis> _strictCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RomajiAnalysis> _looseCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RomajiAnalysis> _compositionCache = new(StringComparer.Ordinal);

    /// <summary>位置 i の読み方 (語・っ・ん・読めない) は、i から後ろにこの文字数以上あれば、それより後ろの文字によらない。</summary>
    internal int DecisionWindow => Math.Max(Math.Max(5, MaxSpellingLength + 1), _customMaxLength + 1);

    private static readonly int MaxSpellingLength = CompositionSpellingToKana.Keys.Max(k => k.Length);

    private RomajiAnalysis AnalyzeCore(string letters, bool strictStart, bool composition)
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
                if (i == 0 && strictStart) return Invalid("語頭の「ん」");
                var consumed = 1;
                if (s[i + 1] == 'n' && (composition || i + 2 >= s.Length || !(IsVowel(s[i + 2]) || s[i + 2] == 'y'))) consumed = 2;
                tokens.Add(new RomajiToken(s.Substring(i, consumed), "ん"));
                i += consumed;
                continue;
            }

            if (i + 1 < s.Length && IsConsonant(c) && c != 'n' &&
                (s[i + 1] == c || (c == 't' && s[i + 1] == 'c' && i + 2 < s.Length && s[i + 2] == 'h')))
            {
                if (i == 0 && strictStart) return Invalid("語頭の「っ」");
                tokens.Add(new RomajiToken(c.ToString(), "っ"));
                sokuon++;
                i += 1;
                continue;
            }

            var matched = false;
            var spellings = composition ? CompositionSpellingToKana : SpellingToKana;
            for (var length = Math.Min(4, s.Length - i); length >= 1; length--)
            {
                var piece = s.Substring(i, length);
                if (!spellings.TryGetValue(piece, out var kana)) continue;
                if (StrongYouonKana.Contains(kana)) strongYouon++;
                if (piece == "tsu") tsu++;
                if (kana == "う" && tokens.Count > 0 && EndsWithVowel(tokens[^1].Romaji, 'o', 'u')) longVowels++;
                tokens.Add(new RomajiToken(piece, kana));
                i += length;
                matched = true;
                break;
            }
            if (matched) continue;

            var rest = s[i..];
            if ((composition ? CompositionPartials : PartialSpellings).Contains(rest) || rest is "n" or "tc" || _customPartials.Contains(rest))
            {
                return new RomajiAnalysis(true, tokens.ToArray(), rest, null, strongYouon, tsu, sokuon, longVowels);
            }
            return Invalid($"「{rest}」はローマ字として成立しない");
        }

        return new RomajiAnalysis(true, tokens.ToArray(), "", null, strongYouon, tsu, sokuon, longVowels);
    }

    /// <summary>s の i から始まる、ユーザーの表のいちばん長い綴り。無ければ null。</summary>
    private RomajiToken? MatchCustom(string s, int i)
    {
        for (var length = Math.Min(_customMaxLength, s.Length - i); length >= 1; length--)
        {
            var piece = s.Substring(i, length);
            // 表の綴りの頭で入力が終わっている (kz の k だけ) なら、続きを待つ (組み込みの綴りで読まない)
            if (i + length == s.Length && length < _customMaxLength && _customPartials.Contains(piece) && !_custom!.ContainsKey(piece)) return null;
            if (_custom!.TryGetValue(piece, out var kana)) return new RomajiToken(piece, kana);
        }
        return null;
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
                builder.Append(final && analysis.Partial == "n" ? "ん" : analysis.Partial);
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

    public RomajiAnalysis AnalyzeWord(string word)
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
