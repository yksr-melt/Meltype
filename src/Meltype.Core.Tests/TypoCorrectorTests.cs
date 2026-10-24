// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;
using static Meltype.Detection.TypoCorrector;

namespace Meltype.Tests;

/// <summary>英語 / 日本語の打ち間違いの直し (TypoCorrector) のテストと評価。</summary>
internal static class TypoCorrectorTests
{
    private static readonly Lazy<TypoCorrector> Shared = new(TypoCorrector.FromBuiltIn);

    [Test]
    public static void Normalize_UnifiesKunreiAndN()
    {
        Assert.Equal("shitsuchifuN", TypoCorrector.Normalize("SituTiHunn"));
        Assert.Equal("koNnichiha", TypoCorrector.Normalize("kon'nitiha"));
        Assert.Equal(TypoCorrector.Normalize("shinkansenn"), TypoCorrector.Normalize("shinkansen"));
        Assert.Equal(TypoCorrector.Normalize("sannpo"), TypoCorrector.Normalize("sanpo"));
        Assert.Equal("shi", TypoCorrector.Normalize("shi"));
        Assert.Equal("chu", TypoCorrector.Normalize("chu"));
    }

    [Test]
    public static void Distance_IsWeightedByKeyboard()
    {
        var t = new TypoCorrector([], ["test"]);
        Assert.Equal(0.4, t.Distance("tesr", "test"), "隣のキー r→t");
        Assert.Equal(1.0, t.Distance("tesp", "test"), "離れたキー");
        Assert.Equal(0.5, t.Distance("tset", "test"), "入れ替え");
        Assert.Equal(0.3, t.Distance("tesst", "test"), "重複");
        Assert.Equal(0.7, t.Distance("tst", "test"), "抜け");
    }

    [Test]
    public static void ExactWord_IsKept()
    {
        var r = Shared.Value.Correct("house");
        Assert.True(!r.Corrected && r.Output == "house", "辞書にある語はそのまま");
    }

    [Test]
    public static void AdjacentKeyTypo_IsCorrected()
    {
        var r = Shared.Value.Correct("hpuse", Language.English);
        Assert.Equal("house", r.Output);
        Assert.Equal(true, r.Corrected);
    }

    [Test]
    public static void UnknownGibberish_IsKept()
    {
        var r = Shared.Value.Correct("qzxjvk");
        Assert.Equal("qzxjvk", r.Output, "近い語が無ければそのまま");
    }

    [Test]
    public static void RealMisspellings_AreMostlyFixedAndNeverMiscorrectedOften()
    {
        var c = Shared.Value;
        var real = RealMisspellings.Where(p => !p.Intended.Contains(' ')).Select(p => new Sample(p.Intended, p.Typed, Language.English, null)).ToList();
        var s = Run(c, real);
        Assert.True(s.WrongRate <= 0.10, $"実際の綴り間違い: {s}");
    }

    [Test]
    public static void OutOfVocabularyWords_AreRarelyChanged()
    {
        var c = Shared.Value;
        var changed = OutOfVocabulary.Count(w => c.Correct(w).Corrected);
        // 今は pixiv → pixie の 1 語だけ残る (BackSpace で戻せば、それからは直さない: RejectedWord_IsNeverCorrectedAgain)
        Assert.True(changed <= 1, $"辞書に無い正しい語を {changed}/{OutOfVocabulary.Length} 語直した");
    }

    [Test]
    public static void HeldOutSeeds_KeepFalseCorrectionLow()
    {
        var c = Shared.Value;
        foreach (var seed in new[] { 101, 102, 103 })
        {
            var s = Run(c, MakeSamples(c, 500, seed));
            Assert.True(s.WrongRate <= 0.02, $"seed {seed}: {s}");
        }
    }

    [Test]
    public static void WrongPreviousLanguage_DoesNotExplode()
    {
        var c = Shared.Value;
        var samples = MakeSamples(c, 500, 501);
        var s = Run(c, samples, x => x.Language == Language.English ? Language.Japanese : Language.English);
        Assert.True(s.WrongRate <= 0.03, $"直前の言語が逆: {s}");
    }

    [Test]
    public static void KunreiInput_MatchesHepburnWord()
    {
        var r = Shared.Value.Correct("arigatou");
        Assert.Equal(false, r.Corrected, "辞書の語");
        Assert.Equal("arigatou", Shared.Value.Correct("arigatpu", Language.Japanese).Output);
    }

    [Test]
    public static void ProtectedWords_AreKept()
    {
        foreach (var w in new[] { "iphone", "emoji", "claude", "wifi", "kubernetes", "github" })
            Assert.Equal(false, Shared.Value.Correct(w).Corrected, w);
    }

    [Test]
    public static void KanaToRomaji_IsHepburn()
    {
        Assert.Equal("kyouhaamedesu", TypoCorrector.KanaToRomaji("きょうはあめです"));
        Assert.Equal("gakkou", TypoCorrector.KanaToRomaji("がっこう"));
        Assert.Equal("matcha", TypoCorrector.KanaToRomaji("まっちゃ"));
        Assert.Equal("shinnyuu", TypoCorrector.KanaToRomaji("しんにゅう"));
        Assert.Equal("kanji", TypoCorrector.KanaToRomaji("かんじ"));
        Assert.Equal(null, TypoCorrector.KanaToRomaji("コーヒー"));
    }

    [Test]
    public static void IsRomaji_DetectsBrokenSyllables()
    {
        Assert.Equal(true, TypoCorrector.IsRomaji(TypoCorrector.Normalize("gakkoude")));
        Assert.Equal(true, TypoCorrector.IsRomaji(TypoCorrector.Normalize("matcha")));
        Assert.Equal(true, TypoCorrector.IsRomaji(TypoCorrector.Normalize("genshin")));
        Assert.Equal(false, TypoCorrector.IsRomaji(TypoCorrector.Normalize("arigatpu")));
        Assert.Equal(false, TypoCorrector.IsRomaji(TypoCorrector.Normalize("kyoyha")));
    }

    [Test]
    public static void JapanesePhraseTypo_IsCorrected()
    {
        // 今日は雨 (kyouhaame) の h と a が入れ替わった
        var r = Shared.Value.Correct("kyouahame", Language.Japanese);
        Assert.Equal("kyouhaame", r.Output);
        Assert.Equal(false, Shared.Value.Correct("kyouhaame", Language.Japanese).Corrected, "正しい文はそのまま");
    }

    [Test]
    public static void JapanesePhraseTypos_AreMostlyFixedWithoutMiscorrection()
    {
        var c = Shared.Value;
        var s = Run(c, MakePhraseSamples(500, 901));
        // 打ち間違いの文を、別の (やはり違う) 文に直してしまう率。正しい文を壊すのとは別 (それは JapanesePhrases_AreNotChanged)
        Assert.True(s.WrongRate <= 0.06, $"日本語の文の打ち間違い: {s}");
        Assert.True(s.FixRate >= 0.30, $"日本語の文の打ち間違い: {s}");
    }

    /// <summary>語 + 助詞 + 語 の文に、雑音を 1 つ入れたもの。</summary>
    public static List<Sample> MakePhraseSamples(int count, int seed, Noise? noise = null)
    {
        var random = new Random(seed);
        var words = JapaneseWords(4);
        string[] particles = ["ha", "ga", "wo", "ni", "de", "to", "mo", "no", "kara", "made"];
        var list = new List<Sample>();
        while (list.Count < count)
        {
            var phrase = words[random.Next(words.Length)] + particles[random.Next(particles.Length)] + words[random.Next(words.Length)];
            var kind = noise ?? (Noise)random.Next(4);
            var typo = AddNoise(phrase, kind, random);
            if (typo == phrase) continue;
            list.Add(new Sample(phrase.Replace("N", "nn"), typo.Replace("N", "nn"), Language.Japanese, kind));
        }
        return list;
    }

    [Test]
    public static void ShortJapaneseTypo_IsCorrected()
    {
        // 草 (kusa) の s と a が入れ替わった。ローマ字として読めない (s で終わる) ので、4 文字でも直す
        Assert.Equal("kusa", Shared.Value.Correct("kuas", Language.Japanese).Output);
        Assert.Equal("kuas", Shared.Value.Correct("kuas", Language.English).Output, "英語を打っているときは直さない");
    }

    [Test]
    public static void KunreiYouonAndLongVowelOmission_AreCorrected()
    {
        // 出張中: tyo/tyu (訓令式の拗音) と、長音の u の抜け
        Assert.Equal("shutchouchuu", Shared.Value.Correct("shuttyotyu", Language.Japanese).Output);
        Assert.Equal("shutchouchuu", Shared.Value.Correct("Shuttyotyu", Language.Japanese).Output);
        // 正しい綴りは、cch でも tch でも書き換えない
        Assert.Equal(false, Shared.Value.Correct("shucchouchuu", Language.Japanese).Corrected, "shucchouchuu");
        Assert.Equal(false, Shared.Value.Correct("shucchou", Language.Japanese).Corrected, "shucchou");
        Assert.Equal(false, Shared.Value.Correct("shuttyou", Language.Japanese).Corrected, "shuttyou (訓令式)");
        Assert.Equal(TypoCorrector.Normalize("shutchou"), TypoCorrector.Normalize("shucchou"));
        Assert.Equal(TypoCorrector.Normalize("shutchou"), TypoCorrector.Normalize("syuttyou"));
    }

    [Test]
    public static void LongVowelOnly_NeedsJapaneseContext()
    {
        Assert.Equal("hokkaido", Shared.Value.Correct("hokkaido").Output, "言語が分からなければ Hokkaido のまま");
        Assert.Equal("hokkaidou", Shared.Value.Correct("hokkaido", Language.Japanese).Output, "日本語を打っている途中なら ほっかいどう");
    }

    [Test]
    public static void JapanesePhrases_AreNotChanged()
    {
        // 語 + 助詞 + 語 は辞書に無いが、正しい日本語。直したら直し間違い。
        var s = PhraseFalseCorrections(Shared.Value, 500, 801);
        Assert.True(s.Changed <= s.N * 0.02, $"日本語の文を {s.Changed}/{s.N} 直した: {string.Join(", ", s.Examples)}");
    }

    public static (int N, int Changed, List<string> Examples) PhraseFalseCorrections(TypoCorrector c, int count, int seed)
    {
        var random = new Random(seed);
        var words = JapaneseWords(4);
        string[] particles = ["ha", "ga", "wo", "ni", "de", "to", "mo", "no", "kara", "made"];
        int changed = 0;
        var examples = new List<string>();
        for (var n = 0; n < count; n++)
        {
            var phrase = words[random.Next(words.Length)] + particles[random.Next(particles.Length)] + words[random.Next(words.Length)];
            var r = c.Correct(phrase, Language.Japanese);
            if (!r.Corrected) continue;
            changed++;
            if (examples.Count < 8) examples.Add($"{phrase}→{r.Output}");
        }
        return (count, changed, examples);
    }

    private static string[] JapaneseWords(int minLength) =>
        DictionarySource.LinesWithoutComments(DictionarySource.ReadEmbedded("readings.txt")).Select(l => l.Split('\t')[0].Trim())
            .Select(TypoCorrector.KanaToRomaji).OfType<string>().Select(TypoCorrector.Normalize)
            .Concat(DictionarySource.Load("japanese.txt", null).Select(TypoCorrector.Normalize))
            .Where(w => w.Length >= minLength && w.All(char.IsAsciiLetterLower)).Distinct().ToArray();

    [Test]
    public static void RejectedWord_IsNeverCorrectedAgain()
    {
        var t = TypoCorrector.FromBuiltIn();
        var r = t.Correct("pixiv");
        if (r.Corrected) t.Reject("pixiv", r.Output);
        Assert.Equal(false, t.Correct("pixiv").Corrected, "一度戻した語");
    }

    [Test]
    public static void Rejection_RaisesCost()
    {
        var t = new TypoCorrector([], ["cat"]);
        var before = t.SubstituteCost('x', 'c');
        t.Reject("xat", "cat");
        Assert.True(t.SubstituteCost('x', 'c') > before, "BackSpace で戻された置換は高くなる");
        t.Learn("xat", "cat");
        t.Learn("xat", "cat");
        Assert.True(t.SubstituteCost('x', 'c') < before * 1.5, "確定した置換は安くなる");
    }

    [Test]
    public static void Eval_FalseCorrectionRateIsLow()
    {
        var m = Evaluate(Shared.Value, 400, seed: 1);
        Assert.True(m.FalseCorrectionRate <= 0.02, $"直し間違い {m.FalseCorrectionRate:P1} (2% 以下)");
        Assert.True(m.FixAccuracy >= 0.30, $"直せた率 {m.FixAccuracy:P1} (30% 以上)");
    }

    public readonly record struct Metrics(double LanguageAccuracy, double FixAccuracy, double FalseCorrectionRate, int Clean, int Noisy);

    /// <summary>
    /// 評価: 辞書の語 (clean) と、雑音 (隣のキーの置換・入れ替え・重複・抜け) を 1 つ入れた語 (noisy)。
    ///   langAcc: noisy で直した語の言語が正しい率 / fixAcc: noisy が元の語に直った率 / falseCorrectionRate: clean または noisy を間違った語にした率
    /// </summary>
    public static Metrics Evaluate(TypoCorrector corrector, int count, int seed)
    {
        var random = new Random(seed);
        var en = WordList.ParseWords(DictionarySource.ReadEmbedded("english-words.txt")).Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 5 && w.All(char.IsAsciiLetterLower)).ToArray();
        var ja = JapaneseWords(5);
        int clean = 0, cleanFalse = 0, noisy = 0, fixedOk = 0, noisyFalse = 0, langOk = 0, langN = 0;
        for (var n = 0; n < count; n++)
        {
            var isJa = ja.Length > 0 && random.Next(4) == 0;
            var word = isJa ? ja[random.Next(ja.Length)] : en[random.Next(en.Length)];
            var lang = isJa ? Language.Japanese : Language.English;
            clean++;
            if (corrector.Correct(word, lang).Output != word) cleanFalse++;
            var typo = AddNoise(word, random);
            if (corrector.Correct(typo, lang) is var exact && typo != word && exact.Candidates.Count == 0 && !exact.Corrected && IsWord(corrector, typo)) continue;
            noisy++;
            var r = corrector.Correct(typo, lang);
            if (r.Corrected)
            {
                langN++;
                if (r.Language == lang) langOk++;
                if (r.Output == word) fixedOk++;
                else noisyFalse++;
            }
        }
        return new Metrics(langN == 0 ? 1 : (double)langOk / langN, noisy == 0 ? 0 : (double)fixedOk / noisy,
            (double)(cleanFalse + noisyFalse) / (clean + noisy), clean, noisy);
    }

    private static bool IsWord(TypoCorrector c, string w) => c.Correct(w).Candidates.Count == 0;

    private static string AddNoise(string w, Random random) => AddNoise(w, (Noise)random.Next(4), random);

    private static string AddNoise(string w, Noise kind, Random random)
    {
        var i = random.Next(w.Length - 1);
        switch ((int)kind)
        {
            case 0:
                var neighbors = Qwerty.Neighbors(w[i]).ToArray();
                return neighbors.Length == 0 ? w : w[..i] + neighbors[random.Next(neighbors.Length)] + w[(i + 1)..];
            case 1: return w[..i] + w[i + 1] + w[i] + w[(i + 2)..];
            case 2: return w[..(i + 1)] + w[i] + w[(i + 1)..];
            case 4:
                // 長音の u を抜く (shutchou → shutcho)。無ければそのまま (呼ぶ側で除く)
                var spots = Enumerable.Range(1, w.Length - 1).Where(k => w[k] == 'u' && w[k - 1] is 'o' or 'u').ToArray();
                return spots.Length == 0 ? w : w.Remove(spots[random.Next(spots.Length)], 1);
            default: return w.Remove(i, 1);
        }
    }

    /// <summary>雑音の種類。</summary>
    public enum Noise { AdjacentKey, Transpose, Duplicate, Delete, LongVowel }

    public sealed record Sample(string Original, string Typed, Language Language, Noise? Noise);

    /// <summary>評価用の語の組を作る。noise が null なら 4 種類から無作為に選ぶ。打ち間違いがたまたま別の辞書の語になったものは除く。</summary>
    public static List<Sample> MakeSamples(TypoCorrector c, int count, int seed, Language? only = null, Noise? noise = null, int minLength = 5)
    {
        var random = new Random(seed);
        var en = WordList.ParseWords(DictionarySource.ReadEmbedded("english-words.txt")).Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= minLength && w.All(char.IsAsciiLetterLower)).Distinct().ToArray();
        var ja = JapaneseWords(minLength);
        var list = new List<Sample>();
        for (var tries = 0; list.Count < count && tries < count * 20; tries++)
        {
            var isJa = only == Language.Japanese || only is null && random.Next(4) == 0;
            var word = isJa ? ja[random.Next(ja.Length)] : en[random.Next(en.Length)];
            var kind = noise ?? (Noise)random.Next(4);
            var typo = AddNoise(word, kind, random);
            if (typo == word || c.Correct(typo).Margin == double.PositiveInfinity) continue; // 別の語になった
            list.Add(new Sample(word, typo, isJa ? Language.Japanese : Language.English, kind));
        }
        return list;
    }

    public readonly record struct Score(int N, int Fixed, int Wrong, int Kept)
    {
        public double FixRate => N == 0 ? 0 : (double)Fixed / N;
        public double WrongRate => N == 0 ? 0 : (double)Wrong / N;
        public override string ToString() => $"n={N,4} 直った {FixRate,6:P1}  別の語に直した {WrongRate,6:P2} ({Wrong})  そのまま {(double)Kept / Math.Max(N, 1),6:P1}";
    }

    /// <summary>null でなければ、Run が別の語に直した例をここに入れる (調べる用)。</summary>
    public static List<string>? WrongExamples;

    public static Score Run(TypoCorrector c, IEnumerable<Sample> samples, Func<Sample, Language?>? previous = null)
    {
        int n = 0, ok = 0, wrong = 0, kept = 0;
        foreach (var s in samples)
        {
            n++;
            var r = c.Correct(s.Typed, previous is null ? s.Language : previous(s));
            if (!r.Corrected) kept++;
            else if (r.Output == s.Original || s.Language == Language.Japanese && TypoCorrector.Normalize(r.Output) == TypoCorrector.Normalize(s.Original)) ok++;
            else
            {
                wrong++;
                if (WrongExamples is { Count: < 12 } list) list.Add($"{s.Typed}→{r.Output} (正解 {s.Original})");
            }
        }
        return new Score(n, ok, wrong, kept);
    }

    /// <summary>実際によくある英語の綴り間違い (手で集めた。雑音で作ったものではない)。</summary>
    public static readonly (string Typed, string Intended)[] RealMisspellings =
    [
        ("teh", "the"), ("recieve", "receive"), ("seperate", "separate"), ("definately", "definitely"), ("occured", "occurred"),
        ("untill", "until"), ("becuase", "because"), ("wierd", "weird"), ("beleive", "believe"), ("freind", "friend"),
        ("tommorow", "tomorrow"), ("accomodate", "accommodate"), ("adress", "address"), ("begining", "beginning"), ("calender", "calendar"),
        ("comming", "coming"), ("enviroment", "environment"), ("goverment", "government"), ("happend", "happened"), ("knowlege", "knowledge"),
        ("libary", "library"), ("neccessary", "necessary"), ("occassion", "occasion"), ("persue", "pursue"), ("posession", "possession"),
        ("prefered", "preferred"), ("realy", "really"), ("succesful", "successful"), ("tounge", "tongue"), ("truely", "truly"),
        ("wich", "which"), ("writting", "writing"), ("thier", "their"), ("alot", "a lot"), ("arguement", "argument"),
        ("buisness", "business"), ("collegue", "colleague"), ("existance", "existence"), ("finaly", "finally"), ("foriegn", "foreign"),
        ("grammer", "grammar"), ("independant", "independent"), ("judgement", "judgment"), ("maintainance", "maintenance"), ("noticable", "noticeable"),
        ("publically", "publicly"), ("recomend", "recommend"), ("refered", "referred"), ("relevent", "relevant"), ("wether", "whether"),
    ];

    /// <summary>辞書に無いが、正しい語・名前 (直してはいけない)。直したら直し間違い。</summary>
    public static readonly string[] OutOfVocabulary =
    [
        // 同梱のどの辞書 (直さない語の辞書を含む) にも無い語。直さない語の辞書を足しても、この評価には効かないようにしてある。
        "vscode", "xcode", "eslint", "rustc", "pnpm", "homebrew", "obsidian", "zotero", "overleaf", "markdown", "yaml", "toml", "regex", "sudo", "chmod", "nvim", "tmux", "zsh", "fzf", "ripgrep", "dotfiles", "webhook", "oauth", "jwt", "saml", "istio", "argocd", "gitops", "mlops", "vllm", "mistral", "dalle", "pixiv", "niconico", "hololive", "nijisanji", "gacha", "genshin", "mercari", "paypay", "shinkansen", "yamanote", "odakyu", "keio", "tokyu", "umeda", "namba", "tenjin", "sapporo", "sendai", "fukuoka", "hokkaido", "kyushu", "shikoku", "matcha", "wagyu", "yakitori", "takoyaki", "okonomiyaki", "onigiri", "bento", "izakaya", "omakase",
    ];

    /// <summary>
    /// --typo-eval: 調整 (seed 7) と別の seed で、種類・言語・長さ・直前の言語の誤り・実際の綴り間違い・辞書に無い語・速さを分けて出す。
    /// </summary>
    public static void Report()
    {
        var c = TypoCorrector.FromBuiltIn();
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.WriteLine($"α={c.Alpha} θ={c.Theta} (調整は seed 7。ここは seed 101〜105)");
        var seeds = new[] { 101, 102, 103, 104, 105 };

        Console.WriteLine("\n[seed ごと (英 3 : 日 1、1000 語ずつ)]");
        var rates = new List<(double Fix, double Wrong)>();
        foreach (var seed in seeds)
        {
            var sc = Run(c, MakeSamples(c, 1000, seed));
            rates.Add((sc.FixRate, sc.WrongRate));
            Console.WriteLine($"  seed {seed}: {sc}");
        }
        Console.WriteLine($"  平均 直った {rates.Average(r => r.Fix):P1} ± {StdDev(rates.Select(r => r.Fix)):P1} / 別の語 {rates.Average(r => r.Wrong):P2} ± {StdDev(rates.Select(r => r.Wrong)):P2}");

        Console.WriteLine("\n[雑音の種類ごと (英語、2000 語)]");
        foreach (var kind in Enum.GetValues<Noise>().Where(k => k != Noise.LongVowel))
            Console.WriteLine($"  {kind,-12} {Run(c, MakeSamples(c, 2000, 201, Language.English, kind))}");

        Console.WriteLine("\n[言語ごと]");
        Console.WriteLine($"  英語         {Run(c, MakeSamples(c, 2000, 301, Language.English))}");
        Console.WriteLine($"  日本語       {Run(c, MakeSamples(c, 2000, 302, Language.Japanese))}");

        Console.WriteLine("\n[日本語: 雑音の種類ごと (2000 語ずつ)]");
        foreach (var kind in Enum.GetValues<Noise>())
            Console.WriteLine($"  {kind,-12} {Run(c, MakeSamples(c, 2000, 211, Language.Japanese, kind))}");

        Console.WriteLine("\n[日本語の文の打ち間違い (語 + 助詞 + 語 に雑音 1 つ。2000 文ずつ)]");
        foreach (var kind in Enum.GetValues<Noise>())
            Console.WriteLine($"  {kind,-12} {Run(c, MakePhraseSamples(2000, 911, kind))}");
        Console.WriteLine("\n[長音の u の抜け (日本語の語 / 文、2000 ずつ)]");
        Console.WriteLine($"  語           {Run(c, MakeSamples(c, 2000, 221, Language.Japanese, Noise.LongVowel).Where(x => x.Typed != x.Original))}");
        Console.WriteLine($"  文           {Run(c, MakePhraseSamples(2000, 921, Noise.LongVowel).Where(x => x.Typed != x.Original))}");
        foreach (var t in new[] { "kuas", "shuttyotyu", "benkyotyu", "kaigityu", "kyouahame", "kyouhaaem", "watashihagakusei", "watshihagakusei", "ashitahaharedesu", "ashitahaharedsu" })
            Console.WriteLine($"  例 {t} → {c.Correct(t, Language.Japanese).Output}");

        Console.WriteLine("\n[日本語の文 (語 + 助詞 + 語。直してはいけない)]");
        var phrases = PhraseFalseCorrections(c, 2000, 811);
        Console.WriteLine($"  直してしまった {phrases.Changed}/{phrases.N}: {string.Join(", ", phrases.Examples)}");

        Console.WriteLine("\n[長さごと (英語)]");
        foreach (var len in new[] { 3, 4, 5, 7, 10 })
        {
            var samples = MakeSamples(c, 4000, 400 + len, Language.English, minLength: len).Where(s => len == 10 ? s.Original.Length >= 10 : s.Original.Length == len || len == 7 && s.Original.Length is >= 7 and < 10).Take(1000);
            Console.WriteLine($"  {(len == 10 ? "10+" : len == 7 ? "7-9" : len.ToString()),-12} {Run(c, samples)}");
        }

        Console.WriteLine("\n[直前の言語]");
        var mixed = MakeSamples(c, 2000, 501);
        Console.WriteLine($"  正しい       {Run(c, mixed)}");
        Console.WriteLine($"  分からない   {Run(c, mixed, _ => null)}");
        Console.WriteLine($"  逆           {Run(c, mixed, s => s.Language == Language.English ? Language.Japanese : Language.English)}");

        Console.WriteLine("\n[実際の英語の綴り間違い (手で集めた 50 組。雑音で作ったものではない)]");
        var real = RealMisspellings.Where(p => !p.Intended.Contains(' ')).Select(p => new Sample(p.Intended, p.Typed, Language.English, null)).ToList();
        Console.WriteLine($"  {Run(c, real)}");
        foreach (var s in real)
        {
            var r = c.Correct(s.Typed, Language.English);
            if (r.Output != s.Original) Console.WriteLine($"    {s.Typed} → {(r.Corrected ? r.Output : "(そのまま)")}  (正解 {s.Original})");
        }

        Console.WriteLine($"\n[辞書に無い正しい語・名前 (直してはいけない {OutOfVocabulary.Length} 語。直さない語の辞書にも無いもの)]");
        var changed = OutOfVocabulary.Select(w => (w, r: c.Correct(w))).Where(x => x.r.Corrected).ToList();
        Console.WriteLine($"  直してしまった {changed.Count}/{OutOfVocabulary.Length}: {string.Join(", ", changed.Select(x => $"{x.w}→{x.r.Output}"))}");

        Console.WriteLine("\n[辞書の語そのもの (直してはいけない)]");
        var clean = MakeSamples(c, 2000, 601).Select(s => s.Original).ToList();
        Console.WriteLine($"  直してしまった {clean.Count(w => c.Correct(w).Corrected)}/{clean.Count}");

        Console.WriteLine("\n[速さ (1 語あたり、辞書に無い語)]");
        var words = MakeSamples(c, 2000, 701).Select(s => s.Typed).ToArray();
        var times = new List<double>();
        foreach (var w in words)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            c.Correct(w);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        Console.WriteLine($"  中央値 {times[times.Count / 2]:F3} ms / 99% {times[(int)(times.Count * 0.99)]:F3} ms / 最大 {times[^1]:F3} ms (n={times.Count})");
        var load = System.Diagnostics.Stopwatch.StartNew();
        TypoCorrector.FromBuiltIn();
        Console.WriteLine($"  索引を作る時間 {load.Elapsed.TotalMilliseconds:F0} ms");
    }

    private static double StdDev(IEnumerable<double> values)
    {
        var v = values.ToArray();
        var mean = v.Average();
        return Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / Math.Max(v.Length - 1, 1));
    }

    /// <summary>α と θ を格子で探し、直し間違い 2% 以下で直せた率が一番高い組を返す (--typo-tune)。</summary>
    public static void Tune()
    {
        var c = TypoCorrector.FromBuiltIn();
        // 日本語: ローマ字として読める語に足す差 (seed 7)。直し間違いと、辞書に無い語を直した数を一緒に見る。
        foreach (var extra in new[] { 0.0, 1.0, 1.5, 2.0, 3.0 })
        {
            c.ValidRomajiExtra = extra;
            var ja = Run(c, MakeSamples(c, 1000, 7, Language.Japanese));
            var oov = OutOfVocabulary.Count(w => c.Correct(w).Corrected);
            Console.WriteLine($"ローマ字に足す差={extra} 日本語 {ja} / 辞書に無い語を直した {oov}/{OutOfVocabulary.Length} / 文 {PhraseFalseCorrections(c, 500, 7).Changed}/500");
        }
        c.ValidRomajiExtra = 1.5;
        // 日本語の文の直しの差 (seed 7): 打ち間違いの文と、正しい文・辞書に無い語を直してしまった数
        foreach (var pa in new[] { 6.0, 8.0, 10.0, 12.0 })
        foreach (var pt in new[] { 1.0, 2.0, 3.0 })
            foreach (var w in new[] { 1.0, 2.0 })
            {
                c.PhraseAlpha = pa;
                c.PhraseTheta = pt;
                c.PhraseSegmentWeight = w;
                WrongExamples = [];
                var ph = Run(c, MakePhraseSamples(600, 7));
                Console.WriteLine($"文の α={pa} 文の差={pt} 区切りの重み={w} {ph} / kyouahame→{c.Correct("kyouahame", Language.Japanese).Output} / 正しい文 {PhraseFalseCorrections(c, 600, 7).Changed}/600 / 辞書に無い語 {OutOfVocabulary.Count(x => c.Correct(x).Corrected)}");
                WrongExamples = null;
            }
        c.PhraseAlpha = 6.0;
        c.PhraseTheta = 1.0;
        c.PhraseSegmentWeight = 2.0;
        foreach (var extra in new[] { 0.0, 0.5, 1.0, 2.0 })
        {
            c.CostlyEditExtra = extra;
            var all = Run(c, MakeSamples(c, 1000, 7));
            var oov = OutOfVocabulary.Where(w => c.Correct(w).Corrected).ToList();
            Console.WriteLine($"距離 .7 以上に足す差={extra} {all} / 辞書に無い語を直した {oov.Count}/{OutOfVocabulary.Length} {string.Join(",", oov)}");
        }
        c.CostlyEditExtra = 0.5;
        foreach (var alpha in new[] { 2.0, 3.0, 4.0, 6.0 })
            foreach (var theta in new[] { 0.5, 1.0, 1.5, 2.0, 3.0 })
            {
                c.Alpha = alpha;
                c.Theta = theta;
                var m = Evaluate(c, 400, seed: 7);
                Console.WriteLine($"α={alpha} θ={theta} langAcc={m.LanguageAccuracy:P1} fixAcc={m.FixAccuracy:P1} FP={m.FalseCorrectionRate:P2}");
            }
    }
}
