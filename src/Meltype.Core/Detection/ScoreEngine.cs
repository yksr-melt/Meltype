using Meltype.Config;

namespace Meltype.Detection;

/// <summary>
/// 各 Detector の結果を統合して Japanese / English / Unknown (と保留継続の Undecided) を返す (設計書 §17, §18)。
///
/// 日本語と判定するのは
///   JapaneseScore >= 閾値  かつ  JapaneseScore - EnglishScore >= 閾値
/// のときだけ。英語の根拠があるときは、日本語スコアが「多少高い」程度では切り替えない。
/// </summary>
public sealed class ScoreEngine
{
    private readonly RomajiDetector _romaji;
    private readonly KanaDetector _kana;
    private readonly EnglishDetector _english;
    private readonly DictionaryDetector _dictionary;
    private readonly TypoDetector _typo;
    private readonly Func<Settings> _settings;

    public ScoreEngine(
        RomajiDetector romaji,
        KanaDetector kana,
        EnglishDetector english,
        DictionaryDetector dictionary,
        TypoDetector typo,
        Func<Settings> settings)
    {
        _romaji = romaji;
        _kana = kana;
        _english = english;
        _dictionary = dictionary;
        _typo = typo;
        _settings = settings;
    }

    /// <summary>組み込み辞書 (+ ユーザー辞書) から一式を組み立てる。</summary>
    public static ScoreEngine CreateDefault(Func<Settings> settings, string? userDictionaryDirectory = null)
    {
        var romaji = RomajiDetector.CreateDefault(userDictionaryDirectory);
        var japaneseWords = DictionarySource.Load("japanese.txt", userDictionaryDirectory).ToList();
        var dictionary = new DictionaryDetector(japaneseWords, romaji);
        var english = new EnglishDetector(DictionarySource.Load("english.txt", userDictionaryDirectory).Concat(ProperNouns.Load(userDictionaryDirectory).LowercaseWords));
        var kana = new KanaDetector(japaneseWords, romaji);
        return new ScoreEngine(romaji, kana, english, dictionary, new TypoDetector(dictionary.Words), settings);
    }

    public RomajiDetector Romaji => _romaji;

    public DetectionResult Evaluate(DetectionInput input) => Evaluate(input, _settings());

    /// <summary>設定画面の判定テスト用に、保存前の設定で判定できるようにしてある。</summary>
    public DetectionResult Evaluate(DetectionInput input, Settings settings)
    {
        var threshold = Math.Max(2, settings.EffectiveJapaneseThreshold);
        // c 行 (ca / cu / co = か く こ) は k に読み替えてローマ字・日本語の辞書で調べる (fucarete = fukarete)。英語の判定は打ったまま。
        var letters = RomajiDetector.ReadCRow(input.Letters);
        var original = input.Letters;
        var contributions = new List<Contribution>();

        if (letters.Length == 0)
        {
            return Result(input.IsFinal ? Verdict.Unknown : Verdict.Undecided, letters, contributions, "入力なし");
        }

        var useRomaji = settings.InputStyle != InputStyle.Kana;
        var useKana = settings.InputStyle != InputStyle.Romaji;

        // ローマ字専用では l/q/v/x を一文字で英語とする。かな入力を有効にした設定では、Q/X/V/L はかなキーとして判定する。
        if (useRomaji && !useKana && letters.Length == 1 && letters is "l" or "q" or "v" or "x")
        {
            return Result(Verdict.English, letters, contributions, "明らかな英字キー");
        }

        var romajiValid = false;
        var japaneseDictionaryPrefix = false;
        if (useRomaji)
        {
            var analysis = _romaji.Analyze(letters);
            romajiValid = analysis.IsValid;
            if (!analysis.IsValid)
            {
                contributions.Add(new Contribution("Romaji", 0, useKana ? 0 : 5, analysis.InvalidReason ?? "ローマ字として成立しない"));
            }
            else
            {
                if (analysis.StrongYouon > 0) contributions.Add(new Contribution("Romaji", 3, 0, "日本語特有の拗音 (kya/ryo …)"));
                if (analysis.Tsu > 0) contributions.Add(new Contribution("Romaji", 3, 0, "tsu"));
                if (analysis.LongVowels > 0) contributions.Add(new Contribution("Romaji", 1, 0, "長音 (ou/uu)"));
                if (analysis.Sokuon > 0) contributions.Add(new Contribution("Romaji", 1, 0, "促音"));

                _dictionary.Evaluate(letters, contributions);
                japaneseDictionaryPrefix = _dictionary.IsPrefix(letters);

                if (settings.TypoEnabled) _typo.Evaluate(letters, contributions);

                // 5 文字以上が最後までローマ字として読めて、英単語 (の先頭) にも当たらない (fukare) なら日本語寄り。
                // 辞書にない英単語 (debate) は、英数状態の判定でスペルチェッカーが止める (MeltypeEngine.ClassifyDirect)。
                if (letters.Length >= 5 && analysis.Partial.Length == 0 && !_english.IsPrefix(original))
                {
                    contributions.Add(new Contribution("Romaji", letters.Length >= 6 ? 4 : 3, 0, "5 文字以上がすべてローマ字として成立し、英単語にも当たらない"));
                }
            }
        }

        var kanaPlausible = false;
        if (useKana)
        {
            kanaPlausible = _kana.Evaluate(input.Keys, contributions);
        }

        _english.Evaluate(original, contributions);

        var japanese = contributions.Sum(c => c.Japanese);
        var english = contributions.Sum(c => c.English);

        // どの入力方式としても日本語になり得ないなら、この時点で英語と確定して保留をやめる。
        if (!romajiValid && !kanaPlausible)
        {
            return Result(Verdict.English, letters, contributions, "日本語の入力として成立しない");
        }

        if (japanese >= threshold && japanese - english >= threshold)
        {
            return Result(Verdict.Japanese, letters, contributions, "日本語スコアが閾値を超えた");
        }

        // 英語の語 (の先頭) と一致し、日本語の語の途中でもない → これ以上待っても日本語にはならない。
        // "as" (ashita) や "to" (tomodachi) のように日本語の語の先頭でもある間は待つ。
        // 助詞で始まり、助詞の後ろがまだ 3 文字以下の語 (not, nota = の + た…) は、助詞 + 次の語の打ちかけかもしれないので待つ
        // (4 文字になれば日本語の辞書で調べられる)。
        var particleThenMore = romajiValid && !input.IsFinal && DictionaryDetector.StartsWithParticle(letters) is { } particle &&
            letters.Length - particle.Length is > 0 and < 4;
        if (english >= 3 && japanese < threshold && !japaneseDictionaryPrefix && !particleThenMore && !(useKana && kanaPlausible) && letters.Length >= 2)
        {
            return Result(Verdict.English, letters, contributions, "英語の語と一致");
        }

        if (input.IsFinal || letters.Length >= settings.MaxPendingKeys)
        {
            return Result(Verdict.Unknown, letters, contributions, "判断できない");
        }
        return Result(Verdict.Undecided, letters, contributions, "判定材料を収集中");

        DetectionResult Result(Verdict verdict, string text, List<Contribution> list, string summary) =>
            new(verdict, text, list.Sum(c => c.Japanese), list.Sum(c => c.English), list, summary);
    }
}
