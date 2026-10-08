// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;

namespace Meltype.Composition;

/// <summary>変換ボックスの表示形式。Auto 以外は F6/F7/F9/F10 などでユーザーが明示的に選んだもの。</summary>
public enum DisplayMode { Auto, Hiragana, Katakana, FullWidthAlphanumeric, HalfWidthAlphanumeric }

/// <summary>英字 (F9 / F10) で見せるときの大文字・小文字。F9 / F10 を続けて押すと 打ったまま → すべて大文字 → 先頭だけ大文字 と切り替わる。</summary>
public enum LetterCase { AsTyped, Upper, Capitalized }

/// <summary>
/// 変換ボックス内の 1 単位。ローマ字 1 音 (きょ, っ, ん …)・ローマ字として読めなかった英字 1 文字・記号 1 文字のいずれか。
/// Raw は実際に打った文字 (英語として表示するときに使う)。
/// </summary>
public readonly record struct CompositionUnit(string Kana, string Raw);

/// <summary>表示上のひとまとまり。英語と判定した区間は英字のまま、それ以外は日本語 (かな/漢字)。</summary>
public readonly record struct CompositionSegment(bool IsEnglish, string Kana, string Raw);

/// <summary>
/// 未確定の入力。打ったそばからローマ字をかな 1 音ずつの単位にしていき、まだ音にならない子音
/// (k, ky, n …) だけを Pending に残す。BackSpace は 1 音ずつ消える。
/// 英単語の判定は区間ごとに行うので、「きょうは google」のように日本語の後ろの英単語だけが英字になる。
/// </summary>
public sealed class CompositionText
{
    private readonly List<CompositionUnit> _units = [];
    private readonly StringBuilder _pending = new();
    private readonly CompositionDetector _detector;

    public CompositionText(CompositionDetector detector) => _detector = detector;

    public IReadOnlyList<CompositionUnit> Units => _units;
    public string Pending => _pending.ToString();
    public bool IsEmpty => _units.Count == 0 && _pending.Length == 0;
    public DisplayMode Mode { get; set; } = DisplayMode.Auto;

    /// <summary>英字 (F9 / F10) で見せるときの大文字・小文字。確定・取り消しで戻る。</summary>
    public LetterCase Case { get; set; } = LetterCase.AsTyped;

    /// <summary>自動判定の強さ (設定)。</summary>
    public Func<DetectionLevel> Level { get; set; } = () => DetectionLevel.Balanced;

    /// <summary>この入力だけ判定の強さを変える (手動のときに Tab で提案を受け入れる)。確定・取り消しで戻る。</summary>
    public DetectionLevel? LevelOverride { get; set; }

    private DetectionLevel EffectiveLevel => LevelOverride ?? Level();

    /// <summary>かな入力 (JIS) か。かな入力では <see cref="AppendKana"/> で 1 キー 1 文字ずつ入れる。</summary>
    public bool KanaInput { get; set; }

    /// <summary>句読点の組み合わせ (設定)。, と . (かな入力の 、 。 のキー) で入れる文字。</summary>
    public PunctuationStyle Punctuation { get; set; } = PunctuationStyle.Japanese;

    /// <summary>読点 (、 か ，)。</summary>
    private char Comma => Punctuation is PunctuationStyle.FullWidthCommaPeriod or PunctuationStyle.FullWidthCommaKuten ? '，' : '、';

    /// <summary>句点 (。 か ．)。</summary>
    private char Period => Punctuation is PunctuationStyle.FullWidthCommaPeriod or PunctuationStyle.ToutenFullWidthPeriod ? '．' : '。';

    /// <summary>
    /// かな入力の 1 キー。raw はそのキーの英字 (英単語の判定と、英語として見せるときに使う)。
    /// 濁点・半濁点は直前のかなに付ける (か + ゛ → が)。
    /// </summary>
    public void AppendKana(char raw, char kana)
    {
        // 、 。 のキーも設定の句読点にする。
        if (kana == '、') kana = Comma;
        else if (kana == '。') kana = Period;
        if (kana is '゛' or '゜' && _units.Count > 0 && _units[^1].Kana.Length == 1 &&
            Detection.KanaDetector.Combine(_units[^1].Kana[0], kana) is { } combined)
        {
            var last = _units[^1];
            _units[^1] = new CompositionUnit(combined.ToString(), last.Raw + raw);
            return;
        }
        _units.Add(new CompositionUnit(kana.ToString(), raw.ToString()));
    }

    /// <summary>打った文字そのもの。</summary>
    public string Raw => string.Concat(_units.Select(u => u.Raw)) + _pending;

    public void Append(char c)
    {
        // 英数字の間に打った . / - はドメイン名や略語の区切り。入力直後は後続文字が分からないため、
        // 次の英数字が来た時点で句点・長音として読んだ記号を半角に戻す (tetr.io、J-core)。
        if (char.IsAsciiLetterOrDigit(c) && _units.Count >= 2 && (_units[^1].Raw is "." or "-") &&
            _units[^2].Raw.Length > 0 && char.IsAsciiLetterOrDigit(_units[^2].Raw[^1]))
        {
            var preceding = new StringBuilder();
            for (var i = _units.Count - 2; i >= 0 && _units[i].Raw.Length > 0 && _units[i].Raw.All(char.IsAsciiLetterOrDigit); i--) preceding.Insert(0, _units[i].Raw);
            var word = preceding.ToString();
            var lower = word.ToLowerInvariant();
            var acronym = word.Any(char.IsAsciiLetterUpper) && word.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c));
            var knownUnambiguous = word.Length >= 3 && _detector.IsKnownEnglishWord(word) && !_detector.Romaji.AnalyzeFragment(lower).IsValid;
            var properNoun = _detector.ProperNouns.Contains(lower);
            var loneConsonant = word.Length == 1 && char.IsAsciiLetterLower(word[0]) && word[0] is not ('a' or 'i' or 'u' or 'e' or 'o');
            if (acronym || knownUnambiguous || properNoun || _units[^1].Raw == "-" && loneConsonant)
                _units[^1] = _units[^1] with { Kana = _units[^1].Raw };
        }
        // / を 3 つ続けて打ったら … (三点リーダー)。URL (file:///) の : の後ろは除く。
        if (c == '/' && _pending.Length == 0 && _units.Count >= 2 && _units[^1].Raw == "/" && _units[^2].Raw == "/" &&
            !(_units.Count >= 3 && _units[^3].Raw == ":"))
        {
            _units.RemoveRange(_units.Count - 2, 2);
            _units.Add(new CompositionUnit("…", "///"));
            return;
        }
        // 数字の後の , は、次も数字なら桁区切り (1,000) のまま、それ以外なら読点 (x64、arm64)。
        if (!char.IsAsciiDigit(c) && _pending.Length == 0 && _units.Count >= 2 && _units[^1] is { Raw: ",", Kana: "," } &&
            _units[^2].Raw is [var digit] && char.IsAsciiDigit(digit))
        {
            _units[^1] = _units[^1] with { Kana = Comma.ToString() };
        }
        if (char.IsAsciiLetter(c))
        {
            // 大文字に続けて打った大文字 (TS, IME の S, M) は略語の 1 文字。後ろのローマ字 (yu) とつなげて かな (しゅ) にしない。
            var lastTyped = _pending.Length > 0 ? _pending[^1] : _units.Count > 0 && _units[^1].Raw.Length > 0 ? _units[^1].Raw[^1] : '\0';
            if (char.IsAsciiLetterUpper(c) && char.IsAsciiLetterUpper(lastTyped))
            {
                Normalize(final: true);
                _units.Add(new CompositionUnit(c.ToString(), c.ToString()));
                return;
            }
            SplitEnglishFinalN(c);
            // 直前の「ローマ字として読めなかった英字」は、次の文字と合わせると読めることがある
            // (test の t を消して u を打つ → s + u = す)。入力途中の子音に戻して読み直す。略語の大文字 (TS の S) は戻さない。
            var pulled = new StringBuilder();
            while (_units.Count > 0 && _units[^1] is { Raw.Length: 1 } last && last.Kana == last.Raw && char.IsAsciiLetter(last.Raw[0]) &&
                   !(char.IsAsciiLetterUpper(last.Raw[0]) && _units.Count >= 2 && _units[^2].Raw is { Length: > 0 } before && char.IsAsciiLetterUpper(before[^1])) &&
                   // 英単語の最後の l / x (hotel の l) は、次の文字と合わせて小書き文字 (lya = ゃ) にしない。
                   !(last.Raw is "l" or "x" or "L" or "X" && EndsWithEnglishWordFromUnit(_units.Count)) &&
                   // 英単語の最後の t (commit の t) も、続けて打った s と合わせて ts (つ) にしない。
                   !(last.Raw is "t" or "T" && _pending.Length > 0 && _pending[0] is 's' or 'S' && EndsWithEnglishWordFromUnit(_units.Count)) &&
                   // 英単語全体の最後の子音 (github の b) は、助詞 ya の拗音 (bya = びゃ) に飲み込まれないようにする (githubya → githubや)。
                   // 接尾辞だけが英単語のとき (kaibuns の buns) は日本語の拗音 (しょ) を優先する。
                   !(EndsWithWholeEnglishWord(_units.Count) && (c is 'y' or 'Y' || _pending.Length > 0 && _pending[0] is 'y' or 'Y')))
            {
                pulled.Insert(0, last.Raw);
                _units.RemoveAt(_units.Count - 1);
            }
            _pending.Insert(0, pulled.ToString());
            // 英単語 (hotel) の最後の l / x の次に打った文字は、l / x と合わせて小書き文字 (hotel + ya → ほてゃ) にしない。
            if (_pending.Length == 1 && _pending[0] is 'l' or 'x' or 'L' or 'X' && EndsWithEnglishWordFromUnit(_units.Count, _pending.ToString()))
            {
                _units.Add(new CompositionUnit(_pending.ToString(), _pending.ToString()));
                _pending.Clear();
            }
            // 英単語の最後の t の次に打った s は、t と合わせて ts (つ・つぃ) にしない
            // (commit + suru・site → こっみつる・こっみつぃて ではなく commitする・commitして)。
            // 読めない子音がいくつか残っていても同じ (reflect + sareta の ct + s → reflectされた。refェcつァれた になっていた: issue #77)。
            if (_pending.Length >= 1 && _pending[^1] is 't' or 'T' && c is 's' or 'S' && _pending.ToString().All(char.IsAsciiLetter) &&
                EndsWithEnglishWordFromUnit(_units.Count, _pending.ToString()))
            {
                foreach (var letter in _pending.ToString()) _units.Add(new CompositionUnit(letter.ToString(), letter.ToString()));
                _pending.Clear();
            }
            // 英単語全体の打ちかけ末尾の子音 (github の b) の次に y が来たら、拗音 (bya = びゃ) にせず子音を英字のまま確定する
            // (githubya → githubや)。接尾辞だけ英単語の kaibuns + yo (かいぶんしょ) は分けない。
            if (c is 'y' or 'Y' && _pending.Length > 0 && _pending.ToString().All(char.IsAsciiLetter) &&
                EndsWithWholeEnglishWord(_units.Count, _pending.ToString()))
            {
                foreach (var letter in _pending.ToString()) _units.Add(new CompositionUnit(letter.ToString(), letter.ToString()));
                _pending.Clear();
            }
            _pending.Append(c);
            SplitUnitAfterNumber(final: false);
            Normalize(final: false);
            return;
        }
        if (_pending.Length > 0 && char.ToLowerInvariant(_pending[^1]) == 'z' && ZSymbol(c) is { } z)
        {
            var raw = _pending[^1].ToString() + c;
            _pending.Length--;
            Normalize(final: true);
            _units.Add(new CompositionUnit(z.ToString(), raw));
            return;
        }
        // 記号・数字の前で、途中の n は ん に、読めない子音は英字のまま確定させる。
        Normalize(final: true);
        // 数字の前では打ち間違いを直さない (rta2026・ps5・win11 の英字は略語)。数字の後ろの単位を英字のままにするのは FixTypos の中
        if (char.IsAsciiDigit(c)) SplitUnitAfterNumber(final: true);
        else FixTypos();
        // 数字の後の . と , は小数点・桁区切り (GPL3.0、1,000)。句点・読点にしない。
        if (c is ',' or '.' && _units.Count > 0 && _units[^1].Raw is [var previous] && char.IsAsciiDigit(previous))
        {
            _units.Add(new CompositionUnit(c.ToString(), c.ToString()));
            return;
        }
        // 、 や 。 を 3 つ続けたら ... にする (、、、 → ...)。
        if (c is ',' or '.')
        {
            var run = 0;
            while (run < _units.Count && _units[^(run + 1)].Raw is "," or ".") run++;
            if (run >= 2)
            {
                for (var k = _units.Count - run; k < _units.Count; k++) _units[k] = _units[k] with { Kana = "." };
                _units.Add(new CompositionUnit(".", c.ToString()));
                return;
            }
        }
        // / キーで中黒 (設定): かなのすぐ後ろの / は ・ (いろん/ますく → いろん・ますく)。英字・数字の後ろ (and/or、3/4、URL)
        // と入力の始め (/help) は / のまま (issue #122)。英語の区間に入ったときは、打ったままの / を見せる。
        if (c == '/' && SlashAsMiddleDot() && _units.Count > 0 && _units[^1].Kana is [.., var kana] && kana is >= 'ぁ' and <= 'ヺ' or 'ー')
        {
            _units.Add(new CompositionUnit("・", "/"));
            return;
        }
        _units.Add(new CompositionUnit(Symbol(c).ToString(), c.ToString()));
    }

    /// <summary>かなのすぐ後ろの / を中黒 (・) にするか (設定)。</summary>
    public Func<bool> SlashAsMiddleDot { get; set; } = () => false;

    /// <summary>
    /// 英単語の最後の n と、続けて打った n + 母音 (の・な …) が「nn → ん」とまとまってしまうのを防ぐ
    /// (python + no → pythonno → python + ん + お ではなく python + の)。
    /// 直前の単位が nn でできた ん で、その前の英字と 1 つ目の n で知っている英単語になり、今打ったのが母音か y なら、
    /// ん を 1 つ目の n だけにして、2 つ目の n を今打った文字とつなげる。
    /// </summary>
    private void SplitEnglishFinalN(char c)
    {
        if (_pending.Length > 0 || _units.Count < 2 || char.ToLowerInvariant(c) is not ('a' or 'i' or 'u' or 'e' or 'o' or 'y')) return;
        if (_units[^1] is not { Kana: "ん" } last || !last.Raw.Equals("nn", StringComparison.OrdinalIgnoreCase)) return;
        // 直前の英字の並び (英字だけの単位) の後ろの部分 + n が英単語か (きょうは + python の python)。
        var letters = new StringBuilder();
        for (var i = _units.Count - 2; i >= 0 && _units[i].Raw.All(char.IsAsciiLetter); i--) letters.Insert(0, _units[i].Raw);
        var run = letters.ToString() + last.Raw[0];
        for (var start = 0; start <= run.Length - 3; start++)
        {
            if (!_detector.IsKnownEnglishWord(run[start..])) continue;
            // ローマ字として最後まで読める語 (gomen の omen = おめ + n) は、日本語を打っている (ごめんよ)。
            // 分けるのは、ローマ字として読めない英単語 (python、kotlin、json) だけ。
            if (_detector.Romaji.AnalyzeFragment(run[start..].ToLowerInvariant()) is { IsValid: true }) continue;
            _units[^1] = new CompositionUnit("ん", last.Raw[..1]);
            _pending.Append(last.Raw[1]);
            return;
        }
    }

    /// <summary>units の count 個目より前で、英字だけの単位が続く部分 (前の英字)。</summary>
    private string LettersBefore(int count)
    {
        var letters = new StringBuilder();
        for (var i = count - 1; i >= 0 && _units[i].Raw.Length > 0 && _units[i].Raw.All(char.IsAsciiLetter); i--) letters.Insert(0, _units[i].Raw);
        return letters.ToString();
    }

    /// <summary>英字の並びの最後が、4 文字以上の知っている英単語で終わっているか (… hotel)。</summary>
    private bool EndsWithEnglishWord(string letters)
    {
        for (var start = 0; start <= letters.Length - 4; start++)
        {
            if (_detector.IsKnownEnglishWord(letters[start..])) return true;
        }
        return false;
    }

    /// <summary>
    /// 最後の count 個の単位 (の英字の並び) + extra が、4 文字以上の知っている英単語で終わっているか (ほ|て|l = hotel)。
    /// 英単語は単位の区切りから始まるものだけを見る。音の途中から始まる語 (から|な|l の anal、だ|め|x の amex) は、
    /// ローマ字で打っている日本語 (からなぁ・だめぇ) の一部なので英単語とみなさない。
    /// </summary>
    private bool EndsWithEnglishWordFromUnit(int count, string extra = "")
    {
        var letters = extra;
        for (var i = count - 1; i >= 0 && _units[i].Raw.Length > 0 && _units[i].Raw.All(char.IsAsciiLetter); i--)
        {
            letters = _units[i].Raw + letters;
            // 大文字の略語の途中 (AI の I) から始まる語 (Init) は見ない (AInitsuite の t と s を つ にまとめるように: issue #129)
            if (i > 0 && char.IsAsciiLetterUpper(_units[i].Raw[0]) && _units[i - 1].Raw is [.., var before] && char.IsAsciiLetterUpper(before)) continue;
            if (letters.Length >= 4 && _detector.IsKnownEnglishWord(letters)) return true;
        }
        return false;
    }

    /// <summary>
    /// 末尾の英字の並び全体が英単語か (接尾辞だけの一致は含めない)。
    /// github + ya は分けるが、kaibuns + yo (かいぶんしょ。buns だけ英単語) は分けない。
    /// </summary>
    private bool EndsWithWholeEnglishWord(int count, string extra = "")
    {
        var letters = extra;
        for (var i = count - 1; i >= 0 && _units[i].Raw.Length > 0 && _units[i].Raw.All(char.IsAsciiLetter); i--)
            letters = _units[i].Raw + letters;
        return letters.Length >= 4 && _detector.IsKnownEnglishWord(letters);
    }

    /// <summary>
    /// 最後の単位からさかのぼった英字 + extra が、同梱の辞書の略語・語 (vrc・vc・jc) で、ローマ字としては読めないものか。
    /// 語は単位の区切りから始まるものだけを見る。
    /// </summary>
    private bool EndsWithListedAbbreviation(string extra)
    {
        // 大文字の略語の後ろの小文字 (ED + cyau = ちゃう) は、略語の続きではない
        if (_units.Count > 0 && _units[^1].Raw is [.., var last] && char.IsAsciiLetterUpper(last) && extra.Any(char.IsAsciiLetterLower)) return false;
        var letters = extra;
        for (var i = _units.Count - 1; i >= 0 && _units[i].Raw.Length > 0 && _units[i].Raw.All(char.IsAsciiLetter); i--)
        {
            letters = _units[i].Raw + letters;
            var lower = letters.ToLowerInvariant();
            if (_detector.IsListedEnglishWord(lower) && !_detector.Romaji.Analyze(lower).IsValid) return true;
        }
        return false;
    }

    /// <summary>1 音 (または 1 文字) 消す。入力途中の子音があればそれを 1 文字消す。</summary>
    public void RemoveLast()
    {
        if (_pending.Length > 0) _pending.Length--;
        else if (_units.Count > 0)
        {
            var englishTail = Mode is DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric ||
                              (Mode == DisplayMode.Auto && Segments() is { Count: > 0 } segments && segments[^1].IsEnglish);
            var last = _units[^1];
            _units.RemoveAt(_units.Count - 1);
            // 英字として見せている部分は 1 文字ずつ消す (google の "le" のように 1 音にまとまった単位を丸ごと消さない)。
            if (englishTail && last.Raw.Length > 1 && !KanaInput)
            {
                _pending.Append(last.Raw[..^1]);
                Normalize(final: false);
            }
        }
        if (IsEmpty)
        {
            Mode = DisplayMode.Auto;
            Case = LetterCase.AsTyped;
            LevelOverride = null;
        }
    }

    public void Clear()
    {
        _units.Clear();
        _pending.Clear();
        Mode = DisplayMode.Auto;
        Case = LetterCase.AsTyped;
        LevelOverride = null;
    }

    /// <summary>
    /// 数字のすぐ後ろの単位 (10mm、5min、3mol)。ローマ字として読める綴りでも英字のままにする
    /// (10mmで が 10っまで、5min が 5みん、50ccで が 50っcで になっていた: issue #130)。
    /// ローマ字として読めない単位 (kg、cm、px) は今までどおり英字になる。
    /// 数字の後ろでよく打つ日本語 (人 nin、個 ko、回 kai、万 man、度 do、時 ji) と同じ綴りの単位は入れない。
    /// </summary>
    private static readonly string[] UnitWords =
    [
        "mmol", "kcal", "mhz", "ghz", "khz", "kwh", "mah", "mol", "min", "sec", "rem", "dpi", "ppi", "fps", "bpm", "rpm", "mph", "kph", "ppm", "ppb", "ppt",
        "mm", "cm", "km", "nm", "um", "mg", "kg", "ml", "dl", "ms", "ns", "hz", "kb", "mb", "gb", "tb", "px", "pt", "em", "wh", "cc",
    ];

    /// <summary>
    /// 数字の後ろに打った英字 (と打ちかけ) が単位で始まるなら、その単位を英字のままの単位 (1 文字ずつ) にする。
    /// 単位の後ろにまだ文字が続くとき (10mm + で) か、確定するとき (final) だけ。より長い単位の打ちかけ (mm + o → mmol) なら待つ。
    /// </summary>
    private void SplitUnitAfterNumber(bool final)
    {
        // 最後の数字の位置 (その後ろが英字だけのとき)
        var digit = _units.Count - 1;
        while (digit >= 0 && IsLetters(_units[digit].Raw)) digit--;
        if (digit < 0 || _units[digit].Raw is not [var d] || !char.IsAsciiDigit(d)) return;
        var run = string.Concat(_units.Skip(digit + 1).Select(u => u.Raw)) + _pending;
        var lower = run.ToLowerInvariant();
        foreach (var unit in UnitWords)
        {
            if (!lower.StartsWith(unit, StringComparison.Ordinal)) continue;
            // まだ続きを打つかもしれない。ただし同じ子音を重ねた単位 (cc) は、日本語なら っ + 次の音 で続きが要るので、
            // 打った時点で単位として見せる (50cc を打っている途中に 50っc と出ていた: issue #130)。
            if (lower.Length == unit.Length && !final && !(unit is [var c1, var c2] && c1 == c2 && UnitWords.All(u => u == unit || !u.StartsWith(unit, StringComparison.Ordinal)))) return;
            if (lower.Length > unit.Length && UnitWords.Any(u => u.Length > unit.Length && u.StartsWith(lower[..(unit.Length + 1)], StringComparison.Ordinal))) return;
            // 小文字の母音が続くなら、ローマ字の語の途中 (10mina → 10みな) かもしれないので単位にしない
            // ただし単位の最後の文字の前までがローマ字として読めない (51km|ijou の k) なら、母音とつなげても読めないので単位 (51km以上)
            if (lower.Length > unit.Length && lower[unit.Length] is 'a' or 'i' or 'u' or 'e' or 'o' &&
                _detector.Romaji.Analyze(lower[..(unit.Length - 1)]) is { IsValid: true, Partial: "" }) return;
            _units.RemoveRange(digit + 1, _units.Count - digit - 1);
            foreach (var letter in run[..unit.Length]) _units.Add(new CompositionUnit(letter.ToString(), letter.ToString()));
            _pending.Clear();
            _pending.Append(run[unit.Length..]);
            return;
        }
    }

    /// <summary>Pending のうち、音として確定した部分を単位に移す。</summary>
    private void Normalize(bool final)
    {
        if (final) SplitUnitAfterNumber(final: true);
        var romaji = _detector.Romaji;
        while (_pending.Length > 0)
        {
            var original = _pending.ToString();
            var analysis = romaji.AnalyzeFragment(original.ToLowerInvariant());
            var position = 0;
            var tokens = analysis.Tokens;
            for (var i = 0; i < tokens.Count; i++)
            {
                var length = tokens[i].Romaji.Length;
                var raw = original.Substring(position, length);
                // ん (n 1 つ) の後の っ + 子音 (meeting|ga の ngga = ん + っが) は、っ と が を別の単位にする。
                // 日本語で ん の後に っ を打つことはまず無く、英単語の最後の g (meeting) が が とつながって、英単語の区切りが無くなっていた。
                if (tokens[i].Kana is ['っ', _, ..] && raw.Length >= 2 && char.ToLowerInvariant(raw[0]) == char.ToLowerInvariant(raw[1]) &&
                    _units.Count > 0 && _units[^1] is { Kana: "ん" } previous && previous.Raw.Length == 1)
                {
                    _units.Add(new CompositionUnit("っ", raw[..1]));
                    _units.Add(new CompositionUnit(tokens[i].Kana[1..], raw[1..]));
                }
                // 英単語の最後の n + や行 (corn|yori の nyo = にょ) は、n を英単語に残して よ と分ける
                // (n が にょ とつながって、英単語の区切りが無くなっていた。cornyori が 小r二より になっていた)。
                else if (raw.Length >= 3 && raw[..2].Equals("ny", StringComparison.OrdinalIgnoreCase) &&
                         LettersBefore(_units.Count) + raw[..1] is var word && EndsWithEnglishWord(word) && !romaji.Analyze(word.ToLowerInvariant()).IsValid &&
                         romaji.AnalyzeFragment(raw[1..].ToLowerInvariant()) is { IsValid: true, Partial: "" } rest)
                {
                    _units.Add(new CompositionUnit("ん", raw[..1]));
                    var offset = 1;
                    foreach (var token in rest.Tokens)
                    {
                        _units.Add(new CompositionUnit(token.Kana, raw.Substring(offset, token.Romaji.Length)));
                        offset += token.Romaji.Length;
                    }
                }
                // ローマ字として読めない略語の最後の c + h / y (vrc|ya、vc|ha、jc|ha の cya・cha = ちゃ) は、c を略語に残して や・は と分ける
                // (c が ちゃ とつながって、略語の区切りが無くなっていた。vrcyaranai が vrちゃらない になっていた)。
                // 読める語 (mac|hi = まち) は日本語のことが多いので分けない。
                else if (raw.Length >= 3 && char.ToLowerInvariant(raw[0]) == 'c' && char.ToLowerInvariant(raw[1]) is 'h' or 'y' &&
                         EndsWithListedAbbreviation(raw[..1]) &&
                         romaji.AnalyzeFragment(raw[1..].ToLowerInvariant()) is { IsValid: true, Partial: "" } after)
                {
                    _units.Add(new CompositionUnit(raw[..1], raw[..1]));
                    var offset = 1;
                    foreach (var token in after.Tokens)
                    {
                        _units.Add(new CompositionUnit(token.Kana, raw.Substring(offset, token.Romaji.Length)));
                        offset += token.Romaji.Length;
                    }
                }
                else _units.Add(new CompositionUnit(tokens[i].Kana, raw));
                position += length;
            }

            if (analysis.IsValid)
            {
                var rest = original[position..];
                _pending.Clear();
                if (final && rest.Length > 0)
                {
                    // 確定時: 残った n / nn は ん、それ以外の子音は英字のまま。
                    if (rest.ToLowerInvariant() is "n" or "nn") _units.Add(new CompositionUnit("ん", rest));
                    else foreach (var c in rest) _units.Add(new CompositionUnit(c.ToString(), c.ToString()));
                }
                else
                {
                    _pending.Append(rest);
                }
                return;
            }

            // ローマ字として読めない文字 (google の l など) は英字 1 文字の単位にして、続きを読み直す。
            _units.Add(new CompositionUnit(original[position].ToString(), original[position].ToString()));
            _pending.Clear();
            _pending.Append(original[(position + 1)..]);
        }
    }

    /// <summary>数字だけ (と . , : - /) の入力か。数字は半角のまま、Space で確定して空白を入れる (英語と同じ扱い)。</summary>
    public bool IsNumeric =>
        !KanaInput && _pending.Length == 0 && _units.Count > 0 && _units.Any(u => u.Raw.Any(char.IsAsciiDigit)) &&
        _units.All(u => u.Raw.All(c => char.IsAsciiDigit(c) || c is '.' or ',' or ':' or '-' or '/'));

    /// <summary>今の表示が英字 (か数字) だけか (Space で「確定して空白」にするかどうか)。</summary>
    public bool IsAlphanumeric => IsAlphanumericAt(final: false);

    /// <summary>今の表示が英字 (か数字) だけか。final なら打ち終わったとみなして判定する (Space・Enter のとき)。</summary>
    public bool IsAlphanumericAt(bool final) => Mode switch
    {
        DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric => true,
        DisplayMode.Auto => IsNumeric || Segments(final).All(s => s.IsEnglish),
        _ => false,
    };

    /// <summary>自動判定の区間分け (Mode が Auto のときに使う)。</summary>
    /// <param name="final">打ち終わった (Space・Enter で確定・変換する) ときは true。英単語の打ちかけ (amaz → amazon) を英語の根拠にしない。</param>
    public IReadOnlyList<CompositionSegment> Segments(bool final = false)
    {
        // 1 キーごとに、表示・打ち間違いの直し・Space の扱いなどで何度も呼ばれる。区間分けは重い (打った文字のあらゆる区間を調べる) ので、
        // 打った内容と判定の条件が同じなら前の結果を使い回す。
        var key = SegmentsKey(final);
        var slot = final ? 1 : 0;
        if (_segmentsCache[slot] is { } cached && cached.Key == key && ReferenceEquals(cached.Memory, _detector.Memory) && ReferenceEquals(cached.SpellChecker, _detector.SpellChecker))
        {
            return cached.Segments;
        }
        var result = ComputeSegments(final);
        _segmentsCache[slot] = (key, _detector.Memory, _detector.SpellChecker, result);
        return result;
    }

    private readonly (string Key, LanguageMemory? Memory, Detection.IWordChecker? SpellChecker, IReadOnlyList<CompositionSegment> Segments)?[] _segmentsCache = new (string, LanguageMemory?, Detection.IWordChecker?, IReadOnlyList<CompositionSegment>)?[2];

    /// <summary>区間分けの結果を決めるもの (打った単位・入力途中の子音・前後・判定の強さ・学習した語の版) をつないだもの。</summary>
    private string SegmentsKey(bool final)
    {
        var key = new StringBuilder();
        foreach (var unit in _units) key.Append(unit.Kana).Append('\u0001').Append(unit.Raw).Append('\u0002');
        key.Append('\u0003').Append(_pending).Append('\u0003')
            .Append(PrecedingEnglish switch { true => 'E', false => 'J', null => '-' })
            .Append(FollowingEnglish switch { true => 'E', false => 'J', null => '-' })
            .Append(PrecedingEnglishSentence ? 'S' : '-').Append(KanaInput ? 'K' : '-').Append(final ? 'F' : '-')
            .Append((int)EffectiveLevel).Append(':').Append(_detector.Memory?.Version ?? -1);
        return key.ToString();
    }

    private IReadOnlyList<CompositionSegment> ComputeSegments(bool final)
    {
        var segments = _detector.Segment(_units, Pending, PrecedingEnglish, FollowingEnglish, EffectiveLevel, PrecedingEnglishSentence, KanaInput, final);
        return HalfWidthOpeners(segments) is { } adjusted
            ? _detector.Segment(adjusted, Pending, PrecedingEnglish, FollowingEnglish, EffectiveLevel, PrecedingEnglishSentence, KanaInput, final)
            : segments;
    }

    /// <summary>英数字以外の半角の記号 1 文字か (! . , ? など)。</summary>
    private static bool IsAsciiSymbol(string raw) => raw is [var c] && c is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(c);

    /// <summary>開きの記号と、その閉じの記号。</summary>
    // [ ] は日本語の入力では「」なので、英語の前後でも半角にしない
    private static readonly Dictionary<string, string> Openers = new() { ["("] = ")", ["{"] = "}", ["\""] = "\"", ["'"] = "'" };

    /// <summary>
    /// 開きの記号 (「(」「"」) は打った時点ではまだ後ろが分からないので全角になる。後ろが分かったら、
    /// すぐ後ろが英語 ((Ooh) か、対になる閉じの記号が半角 ("…Go Through!") なら、開きの記号も半角にした単位の並びを返す。変えなければ null。
    /// ("打ち上げ話はGo Through!" の最初の " だけ全角 ” になっていた、(Ooh … wow…) の ( だけ全角 （ になっていた)
    /// </summary>
    private List<CompositionUnit>? HalfWidthOpeners(IReadOnlyList<CompositionSegment> segments)
    {
        if (KanaInput || !_units.Any(u => Openers.ContainsKey(u.Raw) && u.Kana != u.Raw)) return null;
        // 打った英字の 1 文字ずつが英語の区間か
        var english = new List<bool>();
        foreach (var segment in segments) foreach (var _ in segment.Raw) english.Add(segment.IsEnglish);
        var offsets = new int[_units.Count];
        for (int i = 0, offset = 0; i < _units.Count; offset += _units[i].Raw.Length, i++) offsets[i] = offset;
        bool IsHalf(int i) => _units[i].Kana == _units[i].Raw || (offsets[i] < english.Count && english[offsets[i]]);

        List<CompositionUnit>? adjusted = null;
        for (var i = 0; i < _units.Count; i++)
        {
            if (!Openers.TryGetValue(_units[i].Raw, out var closer) || _units[i].Kana == _units[i].Raw) continue;
            var nextIsEnglish = i + 1 < _units.Count ? offsets[i + 1] < english.Count && english[offsets[i + 1]] && char.IsAsciiLetter(_units[i + 1].Raw[0])
                : Pending.Length > 0 && segments.Count > 0 && segments[^1].IsEnglish;
            var closing = Enumerable.Range(i + 1, _units.Count - i - 1).FirstOrDefault(k => _units[k].Raw == closer, -1);
            // 閉じの記号が英語の語のすぐ後ろ (間の記号 ! . は飛ばす: …Through!") なら、閉じも、間の記号も半角にする
            var afterEnglish = -1;
            if (closing > i)
            {
                var k = closing - 1;
                while (k > i && IsAsciiSymbol(_units[k].Raw)) k--;
                if (k > i && offsets[k] < english.Count && english[offsets[k]] && char.IsAsciiLetter(_units[k].Raw[^1])) afterEnglish = k;
            }
            // まだ閉じていない開きの記号の後ろに英語の語がある ("打ち上げ話はGo + Space で確定するとき) も半角 (英語の文を続けて打っている)
            var unclosedBeforeEnglish = closing < 0 && Enumerable.Range(i + 1, _units.Count - i - 1)
                .Any(k => offsets[k] < english.Count && english[offsets[k]] && char.IsAsciiLetter(_units[k].Raw[0]));
            if (!nextIsEnglish && !(closing >= 0 && IsHalf(closing)) && afterEnglish < 0 && !unclosedBeforeEnglish) continue;
            adjusted ??= [.. _units];
            adjusted[i] = _units[i] with { Kana = _units[i].Raw };
            for (var k = afterEnglish + 1; afterEnglish >= 0 && k <= closing; k++) adjusted[k] = _units[k] with { Kana = _units[k].Raw };
        }
        return adjusted;
    }

    /// <summary>判定の強さが「手動」のとき、標準の判定なら英字にする部分 (提案)。無ければ null。</summary>
    public string? Suggestion()
    {
        if (Mode != DisplayMode.Auto || EffectiveLevel != DetectionLevel.Manual || IsNumeric) return null;
        var suggested = _detector.Segment(_units, Pending, PrecedingEnglish, FollowingEnglish, DetectionLevel.Balanced, PrecedingEnglishSentence, KanaInput);
        if (!suggested.Any(s => s.IsEnglish) || suggested.SequenceEqual(Segments())) return null;
        return string.Join(" ", suggested.Where(s => s.IsEnglish).Select(s => s.Raw));
    }

    /// <summary>
    /// 入力欄のキャレットの前の確定済みの文字が英語なら true、日本語なら false、分からなければ null。
    /// 英語とも日本語とも読める語 (sushi など) の扱いを、後ろ (<see cref="FollowingEnglish"/>) と合わせて決める。
    /// </summary>
    public bool? PrecedingEnglish { get; set; }

    /// <summary>キャレットの前が英文の途中 ("I want ") か。日本語の文の中の英単語 (GitHub の) より強い英語の根拠。</summary>
    public bool PrecedingEnglishSentence { get; set; }

    /// <summary>キャレットの後ろの文字が英語なら true、日本語なら false、分からなければ null。</summary>
    public bool? FollowingEnglish { get; set; }

    /// <summary>変換用の区間分け。末尾の入力途中の子音は確定扱い (n → ん) にして日本語区間の読みに含める。</summary>
    public IReadOnlyList<CompositionSegment> ConversionSegments()
    {
        var segments = Segments(final: true).ToList();
        if (segments.Count > 0 && !segments[^1].IsEnglish)
        {
            segments[^1] = segments[^1] with { Kana = segments[^1].Kana + PendingText(final: true) };
        }
        return segments;
    }

    /// <summary>
    /// <see cref="ConversionSegments"/> の segmentIndex 番目 (日本語の区間) の読みのうち [start, start + length) に対応する、打った英字
    /// (あぴ → api)。変換の候補に「打ったままの英字」を出すのに使う。読みの区切りが 1 音の区切りと合わないときや、かな入力では null。
    /// </summary>
    public string? RawForReading(int segmentIndex, int start, int length)
    {
        if (KanaInput) return null;
        var segments = Segments(final: true);
        if (segmentIndex < 0 || segmentIndex >= segments.Count) return null;
        // 区間ごとに単位を割り当てる (区間の Raw の長さぶんの単位)。
        var unit = 0;
        for (var s = 0; s < segmentIndex; s++)
        {
            var remaining = segments[s].Raw.Length;
            while (unit < _units.Count && remaining > 0) remaining -= _units[unit++].Raw.Length;
        }
        var pieces = new List<(string Kana, string Raw)>();
        var rawLength = segments[segmentIndex].Raw.Length - (segmentIndex == segments.Count - 1 ? _pending.Length : 0);
        while (unit < _units.Count && rawLength > 0)
        {
            pieces.Add((_units[unit].Kana, _units[unit].Raw));
            rawLength -= _units[unit++].Raw.Length;
        }
        if (segmentIndex == segments.Count - 1 && _pending.Length > 0) pieces.Add((PendingText(final: true), Pending));

        var builder = new StringBuilder();
        var position = 0;
        foreach (var (kana, raw) in pieces)
        {
            var end = position + kana.Length;
            if (end > start && position < start + length)
            {
                // 1 音の途中で区切れているなら、打った英字に対応させられない。
                if (position < start || end > start + length) return null;
                builder.Append(raw);
            }
            position = end;
        }
        return builder.Length > 0 ? builder.ToString() : null;
    }

    /// <summary>
    /// <see cref="ConversionSegments"/> の segmentIndex 番目の区間を、打った英字をローマ字として読んだかな (thin → てぃん)。
    /// 英語の区間を日本語の文節として読み直すのに使う。ローマ字として読めない英字はそのまま残る。かな入力では打ったかな。
    /// </summary>
    public string? KanaForSegment(int segmentIndex)
    {
        var segments = Segments(final: true);
        if (segmentIndex < 0 || segmentIndex >= segments.Count) return null;
        var unit = 0;
        for (var s = 0; s < segmentIndex; s++)
        {
            var remaining = segments[s].Raw.Length;
            while (unit < _units.Count && remaining > 0) remaining -= _units[unit++].Raw.Length;
        }
        var builder = new StringBuilder();
        var rawLength = segments[segmentIndex].Raw.Length - (segmentIndex == segments.Count - 1 ? _pending.Length : 0);
        while (unit < _units.Count && rawLength > 0)
        {
            builder.Append(_units[unit].Kana);
            rawLength -= _units[unit++].Raw.Length;
        }
        if (segmentIndex == segments.Count - 1 && _pending.Length > 0) builder.Append(PendingText(final: true));
        return builder.Length > 0 ? builder.ToString() : null;
    }

    /// <param name="final">確定・変換のときは true (語末の n を ん にする)。</param>
    /// <param name="convert">日本語の区間を漢字に変換する関数 (ライブ変換)。null ならかなのまま。</param>
    public string Display(bool final, Func<string, string>? convert = null) => Mode switch
    {
        DisplayMode.HalfWidthAlphanumeric => ApplyCase(Raw, Case),
        DisplayMode.FullWidthAlphanumeric => ToFullWidth(ApplyCase(Raw, Case)),
        DisplayMode.Hiragana => AllKana(final),
        DisplayMode.Katakana => ToKatakana(AllKana(final)),
        _ => IsNumeric ? Raw : RenderSegments(final, convert),
    };

    /// <summary>英字の大文字・小文字を変える (ai → AI / Ai)。</summary>
    public static string ApplyCase(string text, LetterCase letterCase) => letterCase switch
    {
        LetterCase.Upper => text.ToUpperInvariant(),
        LetterCase.Capitalized => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant(),
        _ => text,
    };

    /// <summary>英語区間は英字のまま、日本語区間はかな (または漢字)。</summary>
    public string RenderSegments(bool final, Func<string, string>? convert)
    {
        var builder = new StringBuilder();
        var segments = Segments(final);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.IsEnglish)
            {
                builder.Append(segment.Raw);
                continue;
            }
            var isLast = i == segments.Count - 1;
            var kana = segment.Kana;
            var pending = isLast ? PendingText(final) : "";
            if (final && isLast && pending == "ん")
            {
                kana += pending;
                pending = "";
            }
            builder.Append(convert is not null && kana.Length > 0 ? convert(kana) : kana);
            builder.Append(pending);
        }
        return builder.ToString();
    }

    /// <summary>英語判定を無視してすべてかなにしたもの (F6)。</summary>
    public string AllKana(bool final) => string.Concat(_units.Select(u => u.Kana)) + PendingText(final);

    /// <summary>
    /// 日本語の部分の読みに書き間違い (ブレスレッド) があれば、その位置 (単位の範囲) と正しい読みを返す。
    /// 英語として見せている部分や、入力途中の子音は見ない。読みの区切りが単位の区切りと合わないときも null。
    /// </summary>
    public (int Start, int End, Misspelling Misspelling)? FindMisspelling(MisspellingDictionary dictionary)
    {
        if (Mode != DisplayMode.Auto || _units.Count == 0) return null;
        var english = UnitIsEnglish();
        // 日本語の単位が続く範囲ごとに探す。
        var start = 0;
        while (start < _units.Count)
        {
            if (english[start])
            {
                start++;
                continue;
            }
            var end = start;
            while (end < _units.Count && !english[end]) end++;
            var offsets = new List<int>();
            var reading = new StringBuilder();
            for (var i = start; i < end; i++)
            {
                offsets.Add(reading.Length);
                reading.Append(_units[i].Kana);
            }
            offsets.Add(reading.Length);
            // 語末の n (まだ ん になっていない) も ん として読む (しゅみれーしょn)。
            if (end == _units.Count && Pending.ToLowerInvariant() is "n" or "nn")
            {
                reading.Append('ん');
                offsets.Add(reading.Length);
            }
            if (dictionary.Find(reading.ToString()) is { } found)
            {
                var first = offsets.IndexOf(found.Start);
                var last = offsets.IndexOf(found.Start + found.Length);
                if (first >= 0 && last > first) return (start + first, start + last, found);
            }
            start = end;
        }
        return null;
    }

    /// <summary>ローマ字の打ち間違いを直すもの (null なら直さない)。</summary>
    public RomajiTypoCorrector? TypoCorrector { get; set; }

    /// <summary>ローマ字の打ち間違いを直すか (設定)。</summary>
    public Func<bool> CorrectTypos { get; set; } = () => true;

    /// <summary>
    /// 英字を続けて打った部分 (ローマ字) の打ち間違いを直す (onegaishimsu → onegaishimasu)。直したら true。
    /// 確定・変換の直前 (Space・Enter・記号) に呼ぶ。打っている途中は続きを打つと読めることがあり、
    /// 直し方も続きで変わる (futtemshi の時点では ふってんし、futtemshita まで打てば ふってました) ので直さない。
    /// 大文字を含む部分・英単語は直さない。
    /// </summary>
    public bool FixTypos()
    {
        // 確定・変換の前に、数字の後ろの単位を英字のままにする (5min を 5みん にしない)。打ち間違いを直す設定に関係なく
        SplitUnitAfterNumber(final: true);
        Normalize(final: false);
        // 英字の並びの最後の w の連続 (笑い) はそのまま残し、打ち間違いの直しの対象にもしない
        KeepLaughter();
        if (TypoCorrector is not { } corrector || !CorrectTypos() || Mode != DisplayMode.Auto || KanaInput) return false;
        var changed = false;
        // 今の表示で英字に見えている文字 (きょうは|meeting|です の meeting)。ここは直さない
        // (表示では英単語なのに、確定したら g が が に直されて めえちんがです になっていた)。
        var shownEnglish = EnglishMask();
        var offset = 0;
        var start = 0;
        while (start < _units.Count)
        {
            if (!IsLetters(_units[start].Raw) || IsLaughter(_units[start]))
            {
                offset += _units[start].Raw.Length;
                start++;
                continue;
            }
            var end = start;
            while (end < _units.Count && IsLetters(_units[end].Raw) && !IsLaughter(_units[end])) end++;
            var atEnd = end == _units.Count;
            var letters = string.Concat(_units.Skip(start).Take(end - start).Select(u => u.Raw)) + (atEnd ? Pending : "");
            var runOffset = offset;
            offset += letters.Length;
            // 数字のすぐ後ろの単位 (10|mm|で) は打ち間違いではない
            var unitAfterNumber = start > 0 && _units[start - 1].Raw is [var digit] && char.IsAsciiDigit(digit) &&
                UnitWords.Any(u => letters.StartsWith(u, StringComparison.OrdinalIgnoreCase));
            if (!unitAfterNumber && letters.All(char.IsAsciiLetterLower) && !_detector.IsKnownEnglishWord(letters) &&
                corrector.FirstUnreadable(letters, final: true) is var first and > 0 &&
                !(runOffset + first < shownEnglish.Count && shownEnglish[runOffset + first]) &&
                // 数字のすぐ前の短い英字 (kaibunsyo|rta|2026 の rta) は略語。打ち間違いとして直さない
                !(end < _units.Count && _units[end].Raw is [var after] && char.IsAsciiDigit(after) && first >= letters.Length - 6) &&
                !ContainsEnglishWord(letters, first) &&
                corrector.Fix(letters, final: true) is { } fix)
            {
                Diagnostics.Log.Decision($"ローマ字の打ち間違いを直しました: {Diagnostics.Log.Text(fix.Wrong)}→{Diagnostics.Log.Text(fix.Right)}");
                var analysis = _detector.Romaji.AnalyzeFragment(fix.Right);
                var units = analysis.Tokens.Select(t => new CompositionUnit(t.Kana, t.Romaji)).ToList();
                _units.RemoveRange(start, end - start);
                _units.InsertRange(start, units);
                if (atEnd)
                {
                    _pending.Clear();
                    _pending.Append(analysis.Partial);
                    Normalize(final: true);
                }
                changed = true;
                end = start + units.Count;
            }
            start = end;
        }
        return changed;
    }

    /// <summary>
    /// 英字の並びの最後の w の連続は、チャットの笑い (きたw・だねww・www・ほんと？w)。ww を っw と読んだり、打ち間違いとして消したりせず、
    /// 1 文字ずつ w のまま残す (確定・変換・記号の直前に呼ぶ)。英単語の最後の w (new・aww) はそのまま。
    /// </summary>
    private void KeepLaughter()
    {
        if (KanaInput || Mode != DisplayMode.Auto) return;
        // 後ろの英字の並びから順に見る (前を入れ替えると後ろの位置がずれるので)
        var end = _units.Count;
        var withPending = _pending.Length > 0;
        while (end > 0 || withPending)
        {
            if (!withPending && !IsLetters(_units[end - 1].Raw))
            {
                end--;
                continue;
            }
            var runStart = end;
            while (runStart > 0 && IsLetters(_units[runStart - 1].Raw)) runStart--;
            KeepLaughterAt(end, withPending);
            withPending = false;
            end = runStart;
        }
    }

    /// <summary>笑いとして w のまま残した単位 (打ち間違いの直しの対象にしない)。</summary>
    private static bool IsLaughter(CompositionUnit unit) => unit.Raw is "w" or "W" && unit.Kana == unit.Raw;

    /// <summary>end の手前で終わる英字の並び (withPending なら入力途中の子音も) の最後の w の連続を、笑いなら w の単位にする。笑いの始まりを返す。</summary>
    private int KeepLaughterAt(int end, bool withPending)
    {
        var pending = withPending ? _pending.ToString() : "";
        if (!pending.All(c => c is 'w' or 'W')) return end;
        var start = end;
        while (start > 0 && _units[start - 1].Raw is [var c] && c is 'w' or 'W' && _units[start - 1].Kana is "っ" or "w" or "W") start--;
        var count = end - start + pending.Length;
        if (count == 0) return end;
        // 1 つだけの w は、日本語のかなの後ろ (きた + w) のときだけ笑いとみなす
        if (count == 1 && !(start > 0 && _units[start - 1].Kana is [var kana, ..] && kana is >= 'ぁ' and <= 'ヺ')) return end;
        // 前から続く英単語の終わり (aww・new・show) なら笑いではない。英単語とみなすのは、英字の並びの頭から始まる語か、同梱の辞書の語だけ
        // (スペルチェッカーの短い語が音の途中から見つかる kiyagat|taw の taw で、笑いの w を英字にしていた)
        var letters = string.Concat(_units.Skip(start).Take(end - start).Select(u => u.Raw)) + pending;
        var laugh = letters;
        for (var i = start - 1; i >= 0 && _units[i].Raw.Length > 0 && _units[i].Raw.All(char.IsAsciiLetter); i--)
        {
            letters = _units[i].Raw + letters;
            var runStart = i == 0 || !IsLetters(_units[i - 1].Raw);
            if (letters.Length >= 3 && (runStart ? _detector.IsKnownEnglishWord(letters) : _detector.IsListedEnglishWord(letters.ToLowerInvariant()))) return end;
        }
        _units.RemoveRange(start, end - start);
        if (withPending) _pending.Clear();
        _units.InsertRange(start, laugh.Select(w => new CompositionUnit(w.ToString(), w.ToString())));
        return start;
    }

    /// <summary>
    /// 打った英字 (Raw) の 1 文字ずつが、今の表示で英単語 (5 文字以上の知っている語: meeting) の区間に入っているか。
    /// 短い語 (onegai|shim|su の shim) は、ローマ字の途中に偶然現れることが多いので含めない (打ち間違いとして直す)。
    /// </summary>
    private List<bool> EnglishMask()
    {
        var mask = new List<bool>();
        foreach (var segment in Segments(final: false))
        {
            // 5 文字以上の知っている語 (meeting) か、同梱の英語の辞書の 2〜4 文字の語 (user・rta・av)
            // 4 文字の知っている語で、ローマ字として読めないもの (help・milk) も英語 (help|pe-ji → へおっぺーじ にしない)
            var lower = segment.Raw.ToLowerInvariant();
            var word = segment.IsEnglish && (segment.Raw.Length >= 5 && _detector.IsKnownEnglishWord(segment.Raw) || segment.Raw.Length is >= 2 and <= 4 && _detector.IsListedEnglishWord(lower) ||
                segment.Raw.Length == 4 && _detector.IsKnownEnglishWord(lower) && !_detector.Romaji.Analyze(lower).IsValid);
            for (var i = 0; i < segment.Raw.Length; i++) mask.Add(word);
        }
        return mask;
    }

    /// <summary>
    /// 読めない文字 (位置 first) が英単語の一部か。
    /// 先頭から打った英単語 (google|de、kotlin|ni、ok|no) と、同梱の辞書・固有名詞にある 5 文字以上の英単語 (…github|ni) は直さない。
    /// スペルチェッカーだけが知っている語は、先頭からの 5 文字以上のときだけ見る
    /// (日本語のローマ字の途中にも、shims や him のような英単語が偶然現れる)。
    /// </summary>
    private bool ContainsEnglishWord(string letters, int first)
    {
        for (var s = Math.Max(0, first - 12); s <= first; s++)
        {
            for (var e = Math.Min(letters.Length, s + 16); e > first; e--)
            {
                var word = letters[s..e];
                if (s == 0 && word.Length >= 5 && _detector.IsKnownEnglishWord(word)) return true;
                if ((s == 0 || word.Length >= 5) && _detector.IsListedEnglishWord(word)) return true;
            }
        }
        return false;
    }

    private static bool IsLetters(string raw) => raw.Length > 0 && raw.All(char.IsAsciiLetter);

    /// <summary>単位 [start, end) の読みを、正しい読み (カタカナ) に置き換える。end が単位の数を超えるときは語末の n も含む。</summary>
    public void ReplaceReading(int start, int end, string rightKatakana)
    {
        var hiragana = new string(rightKatakana.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());
        if (end > _units.Count)
        {
            _pending.Clear();
            end = _units.Count;
        }
        _units.RemoveRange(start, end - start);
        // かなの単位にする (英字の Raw を持たないので、英語と判定されることはない)。
        _units.InsertRange(start, hiragana.Select(c => new CompositionUnit(c.ToString(), c.ToString())));
    }

    /// <summary>単位ごとに、英語として見せている区間に入っているか。</summary>
    private bool[] UnitIsEnglish()
    {
        var result = new bool[_units.Count];
        var unit = 0;
        foreach (var segment in Segments())
        {
            var remaining = segment.Raw.Length;
            while (unit < _units.Count && remaining > 0)
            {
                result[unit] = segment.IsEnglish;
                remaining -= _units[unit].Raw.Length;
                unit++;
            }
        }
        return result;
    }

    private string PendingText(bool final)
    {
        var pending = Pending;
        if (pending.Length == 0) return "";
        return _detector.Romaji.ConvertLenient(pending.ToLowerInvariant(), final);
    }

    /// <summary>日本語の中で打った記号 (Microsoft IME と同じく全角)。英語の区間では打ったままの半角で出す。数字と括弧は半角のまま。</summary>
    private char Symbol(char c) => c switch
    {
        '-' => 'ー',
        // 句読点は設定の組み合わせ (、。 / ，． / ，。 / 、．)。
        ',' => Comma,
        '.' => Period,
        '[' => '「',
        ']' => '」',
        // ASCII の括弧はチャット本文でもそのまま使われるため、入力した幅を保つ。
        '(' => '(',
        ')' => ')',
        // ! ? は Microsoft IME と同じく全角 (テスターのチャットでも、日本語の後ろの ？ ！ は 96% が全角だった)。
        // : ; | はチャット本文でも半角で使うので、入力した幅を保つ。
        ':' => ':',
        ';' => ';',
        '|' => '|',
        '~' => '～',
        '\'' => '’',
        '"' => '”',
        // @ はメールアドレス・メンション、/ は URL・日付・パスで使うので、日本語の中でも半角のまま (Space で ＠ ／ ・)。
        '@' => '@',
        // # はハッシュタグ・チャンネル名 (Discord の #雑談) で使うので半角のまま (Space で ＃)。
        '#' => '#',
        '/' => '/',
        // JIS キーボードの ￥ キー
        '\\' => '￥',
        _ when c is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(c) => (char)(c + 0xFEE0),
        _ => c,
    };

    /// <summary>全角の記号 (＃ （ ％ ’ ￥) を半角に戻す。かな・漢字・句読点 (、。「」ー) はそのまま。</summary>
    public static string SymbolsToHalfWidth(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                '’' => '\'',
                '”' => '"',
                '￥' => '¥',
                >= '！' and <= '～' when !char.IsLetterOrDigit(chars[i]) => (char)(chars[i] - 0xFEE0),
                _ => chars[i],
            };
        }
        return new string(chars);
    }

    /// <summary>z + 記号 (Microsoft IME と同じ): z/ → ・、z. → …、z, → ‥、z- → ～、z[ → 『、z] → 』。</summary>
    private static char? ZSymbol(char c) => c switch
    {
        '/' => '・',
        '.' => '…',
        ',' => '‥',
        '-' => '～',
        '[' => '『',
        ']' => '』',
        _ => null,
    };

    public static string ToKatakana(string hiragana)
    {
        var chars = hiragana.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= 'ぁ' and <= 'ゖ') chars[i] = (char)(chars[i] + 0x60);
        }
        return new string(chars);
    }

    /// <summary>全角カタカナを半角カタカナにする。濁点・半濁点は半角の ﾞ / ﾟ に分ける。</summary>
    public static string ToHalfWidthKatakana(string katakana)
    {
        const string full = "。「」、・ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワンヵヶ";
        const string half = "｡｢｣､･ｦｧｨｩｪｫｬｭｮｯｰｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝｶｹ";
        var builder = new StringBuilder(katakana.Length);
        foreach (var c in katakana.Normalize(NormalizationForm.FormD))
        {
            if (c is '\u3099' or '゛')
            {
                builder.Append('ﾞ');
                continue;
            }
            if (c is '\u309A' or '゜')
            {
                builder.Append('ﾟ');
                continue;
            }
            var index = full.IndexOf(c);
            builder.Append(index >= 0 ? half[index] : c);
        }
        return builder.ToString();
    }

    public static string ToFullWidth(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '!' and <= '~') chars[i] = (char)(chars[i] + 0xFEE0);
            else if (chars[i] == ' ') chars[i] = '　';
        }
        return new string(chars);
    }
}
