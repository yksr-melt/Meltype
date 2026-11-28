// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

using Meltype.Composition;
using Meltype.Config;
using Meltype.Dictionary;
using Meltype.Engine;

namespace Meltype.Tests;

internal static class ChemicalCompoundDictionaryTests
{
    [Test]
    public static void ChemicalNames_IncludeFormulaAndMassCandidates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-chemical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var chemicals = new ChemicalCompoundDictionary(directory);
            var sodiumAcetate = chemicals.Lookup("さくさんなとりうむ", []);
            Assert.True(sodiumAcetate.Contains("CH₃COONa"), "酢酸ナトリウムの示性式を出す: " + string.Join(",", sodiumAcetate));
            Assert.True(sodiumAcetate.Contains("CH₃COONa (FW=82.034)"), "酢酸ナトリウムの式量を出す: " + string.Join(",", sodiumAcetate));

            var copperOxide = chemicals.Lookup("さんかどう", []);
            Assert.True(copperOxide.Contains("CuO") && copperOxide.Contains("Cu₂O"), "酸化銅の両方の価数を出す: " + string.Join(",", copperOxide));
            Assert.True(copperOxide.Contains("CuO (FW=79.545)") && copperOxide.Contains("Cu₂O (FW=143.091)"), "それぞれの式量を出す: " + string.Join(",", copperOxide));

            var copperOxideIi = chemicals.Lookup("さんかどう2", []);
            Assert.True(copperOxideIi.Contains("CuO") && !copperOxideIi.Contains("Cu₂O"), "酸化銅(II)だけを出す: " + string.Join(",", copperOxideIi));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void ChemicalFormula_UsesEditableAtomicWeights()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-atomic-weights-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "atomic-weights.json"), "{\"Cu\":64,\"Tc\":98}");
            var weights = new AtomicWeights(directory);
            Assert.True(ChemicalFormula.TryCalculateMass("CuO", weights, out var copperOxide), "酸化銅の質量を計算できる");
            Assert.Equal(79.999m, copperOxide, "上書きした Cu=64 を使う");
            Assert.True(ChemicalFormula.TryCalculateMass("TcO", weights, out var technetiumOxide), "標準原子量がない元素も上書きできる");
            Assert.Equal(113.999m, technetiumOxide, "上書きした Tc=98 を使う");
            Assert.True(ChemicalFormula.TryCalculateMass("(CH3COO)2Ca", weights, out var calciumAcetate), "括弧付きの示性式を計算できる");
            Assert.Equal(158.166m, calciumAcetate, "括弧内の元素数を掛けて計算する");
            Assert.Equal("(COOH)₂·10H₂O", ChemicalFormula.ToSubscript("(COOH)2·10H2O"), "水和物の係数は下付きにしない");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void SystematicNames_AreComposedFromNameRules()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-chemical-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var chemicals = new ChemicalCompoundDictionary(directory);
            Assert.True(chemicals.Lookup("さんかてつ(III)", []).Contains("Fe₂O₃"), "酸化物の価数から最簡整数比を作る");
            Assert.True(chemicals.Lookup("えんかてつ3", []).Contains("FeCl₃"), "塩化物の価数から最簡整数比を作る");
            Assert.True(chemicals.Lookup("にさんかたんそ", []).Contains("CO₂"), "日本語の数詞を使った二元分子名を解釈する");
            Assert.True(chemicals.Lookup("carbon dioxide", []).Contains("CO₂"), "英語の数詞を使った二元分子名を解釈する");
            var dinitrogenMonoxide = chemicals.Lookup("dinitrogen monoxide", []);
            Assert.True(dinitrogenMonoxide.Contains("N₂O"), "英語名で両方の元素数を解釈する: " + string.Join(",", dinitrogenMonoxide));
            Assert.True(chemicals.Lookup("nitrogen monoxide", []).Contains("NO"), "一酸化窒素で mono の末尾母音脱落を解釈する");
            Assert.True(chemicals.Lookup("iron trichloride", []).Contains("FeCl₃"), "金属の元素名と化学量論接頭語を解釈する");
            Assert.True(chemicals.Lookup("三酸化二鉄", []).Contains("Fe₂O₃"), "日本語名の金属・酸素の原子数を使う");
            Assert.True(chemicals.Lookup("二塩化銅", []).Contains("CuCl₂"), "日本語名の金属ハロゲン化物を組成式順で表示する");
            Assert.True(chemicals.Lookup("2-メチルプロパン", []).Contains("C₄H₁₀"), "炭素鎖と置換基から組成を求める");
            Assert.True(chemicals.Lookup("2-クロロブタン", []).Contains("C₄H₉Cl"), "置換ハロゲンを組成に加える");
            Assert.True(chemicals.Lookup("2,3-dimethylbutane", []).Contains("C₆H₁₄"), "置換基の位置番号を主鎖名の前から解釈する");
            Assert.True(chemicals.Lookup("but-2-ene", []).Contains("C₄H₈"), "不飽和結合の位置番号を主鎖名の途中から解釈する");
            Assert.True(chemicals.Lookup("buta-1,3-diene", []).Contains("C₄H₆"), "複数の不飽和結合の位置番号を解釈する");
            Assert.True(chemicals.Lookup("propan-2-ol", []).Contains("C₃H₈O"), "英語の系統名の主鎖と接尾語を解釈する");
            Assert.True(chemicals.Lookup("butan-2-ol", []).Contains("C₄H₁₀O"), "主鎖の位置番号付き接尾語を解釈する");
            Assert.Equal(0, chemicals.Lookup("4-methylpropane", []).Count, "主鎖外の置換位置を推測しない");

            var hydrate = chemicals.Lookup("硫酸銅(II)五水和物", []);
            Assert.True(hydrate.Contains("CuSO₄·5H₂O"), "水和数を係数として表示する");
            Assert.True(hydrate.Contains("CuSO₄·5H₂O (FW=249.677)"), "水和水を式量に含める");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void CommonNames_IncludeCondensedFormulas()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-chemical-common-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var chemicals = new ChemicalCompoundDictionary(directory);
            var compounds = new (string Name, string Formula, string MolecularFormula)[]
            {
                ("塩化水素", "HCl", "HCl"),
                ("ベンゼンヘキサクロリド", "C₆H₆Cl₆", "C₆H₆Cl₆"),
                ("アニリン", "C₆H₅NH₂", "C₆H₇N"),
                ("アセトアニリド", "C₆H₅NHCOCH₃", "C₈H₉NO"),
                ("ギ酸", "HCOOH", "CH₂O₂"),
                ("吉草酸", "CH₃(CH₂)₃COOH", "C₅H₁₀O₂"),
                ("エタノール", "CH₃CH₂OH", "C₂H₆O"),
                ("ジエチルエーテル", "CH₃CH₂OCH₂CH₃", "C₄H₁₀O"),
                ("トルエン", "C₆H₅CH₃", "C₇H₈"),
                ("フェノール", "C₆H₅OH", "C₆H₆O"),
                ("エチルベンゼン", "C₆H₅CH₂CH₃", "C₈H₁₀"),
            };
            foreach (var (name, formula, molecularFormula) in compounds)
            {
                var candidates = chemicals.Lookup(name, []);
                Assert.True(candidates.Contains(formula), $"{name} の示性式を出す: {string.Join(",", candidates)}");
                Assert.True(candidates.Contains(molecularFormula), $"{name} の分子式も残す: {string.Join(",", candidates)}");
                Assert.True(candidates.Any(candidate => candidate.StartsWith(formula + " (MW=", StringComparison.Ordinal)), $"{name} の分子量も出す");
            }

            var carboxylicAcids = new (string Name, string Formula)[]
            {
                ("methanoic acid", "HCOOH"),
                ("ethanoic acid", "CH₃COOH"),
                ("propanoic acid", "CH₃CH₂COOH"),
                ("butanoic acid", "CH₃(CH₂)₂COOH"),
                ("pentanoic acid", "CH₃(CH₂)₃COOH"),
                ("hexanoic acid", "CH₃(CH₂)₄COOH"),
                ("heptanoic acid", "CH₃(CH₂)₅COOH"),
                ("octanoic acid", "CH₃(CH₂)₆COOH"),
                ("nonanoic acid", "CH₃(CH₂)₇COOH"),
                ("decanoic acid", "CH₃(CH₂)₈COOH"),
                ("デカン酸", "CH₃(CH₂)₈COOH"),
            };
            foreach (var (name, formula) in carboxylicAcids)
                Assert.True(chemicals.Lookup(name, []).Contains(formula), $"{name} の示性式を出す");

            var copperOxide = chemicals.Lookup("酸化銅", []);
            Assert.True(copperOxide.Contains("CuO") && copperOxide.Contains("Cu₂O"), "漢字入力でも酸化銅の価数候補を出す");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void SuppliedNames_AreLocalAndExpandUnqualifiedValences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-chemical-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var chemicals = new ChemicalCompoundDictionary(directory);
            var copperOxide = chemicals.Lookup("酸化銅", []);
            Assert.True(copperOxide.Contains("CuO") && copperOxide.Contains("Cu₂O"), "漢字の酸化銅から価数違いをすべて出す");
            Assert.True(chemicals.Lookup("さんかどう", []).Contains("CuO") && chemicals.Lookup("さんかどう", []).Contains("Cu₂O"), "読みだけの酸化銅からも両方を出す");

            var specifiedCopperOxide = chemicals.Lookup("さんかどう2", ["酸化銅"]);
            Assert.True(specifiedCopperOxide.Contains("CuO") && !specifiedCopperOxide.Contains("Cu₂O"), "価数を指定した入力は指定候補に保つ");

            var mercuryChloride = chemicals.Lookup("えんかすいぎん", []);
            Assert.True(mercuryChloride.Contains("Hg₂Cl₂") && mercuryChloride.Contains("HgCl₂"), "塩化水銀から水銀(I)と水銀(II)を出す");
            Assert.True(!mercuryChloride.Contains("HgCl"), "塩化水銀(I)を誤った一対一式にしない");
            var mercuryIiChloride = chemicals.Lookup("えんかすいぎん1", ["塩化水銀"]);
            Assert.True(mercuryIiChloride.Contains("Hg₂Cl₂") && !mercuryIiChloride.Contains("HgCl₂"), "塩化水銀(I)の指定を保つ");

            Assert.True(chemicals.Lookup("あえんか", []).Contains("ZnO (FW=81.379)"), "一覧の別名と式量を出す");
            Assert.True(chemicals.Lookup("あくりるさん", []).Contains("CH₂=CHCOOH (MW=72.063)"), "結合式を示性式のまま表示して質量を計算する");
            Assert.True(chemicals.Lookup("えんかあんもにうむ", []).Contains("NH₄Cl (FW=53.489)"), "アンモニウム塩を式量として計算する");
            Assert.True(chemicals.Lookup("りゅうさんどう2ごすいわぶつ", []).Contains("CuSO₄·5H₂O (FW=249.677)"), "一覧の水和数を正しい大きさで表示して式量を計算する");
            Assert.True(chemicals.Lookup("さくさんめちる", []).Contains("CH₃COOCH₃ (MW=74.079)"), "辞書にない酢酸メチルをカルボン酸とアルキル基から作る");
            Assert.True(chemicals.Lookup("ぎさんえちる", []).Contains("HCOOCH₂CH₃ (MW=74.079)"), "辞書にないギ酸エチルの結合順を示す");
            Assert.True(chemicals.Lookup("さくさんぺんちる", []).Contains("CH₃COOCH₂CH₂CH₂CH₂CH₃ (MW=130.187)"), "長いアルキル基を持つエステルも組み立てる");
            Assert.True(chemicals.Lookup("さりちるさん2なとりうむ", []).Contains("C₆H₄(ONa)COONa (FW=182.086)"), "サリチル酸二ナトリウムを候補に出す");

            var polymer = chemicals.Lookup("きちん", []);
            Assert.True(polymer.Contains("(C₈H₁₃NO₅)n") && !polymer.Any(candidate => candidate.Contains("MW=", StringComparison.Ordinal) || candidate.Contains("FW=", StringComparison.Ordinal)), "重合度が未指定の式は質量を決めつけない");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void ReportedChemicalExpectations_AppearInLocalCandidates()
    {
        var chemicals = new ChemicalCompoundDictionary(null);
        var cases = new (string Reading, string Candidate)[]
        {
            ("えんかなとりうむ", "NaCl (FW=58.44)"),
            ("べんぜん", "C₆H₆ (MW=78.114)"),
            ("さくさんかるしうむ", "(CH₃COO)₂Ca (FW=158.166)"),
            ("さんかどう", "CuO (FW=79.545)"),
            ("さんかどう", "Cu₂O (FW=143.091)"),
            ("さんかどう2", "CuO (FW=79.545)"),
            ("さくさんなとりうむ", "CH₃COONa (FW=82.034)"),
            ("さくさん", "CH₃COOH"),
            ("えんかすいぎん", "Hg₂Cl₂ (FW=472.08)"),
            ("あえんか", "ZnO (FW=81.379)"),
        };
        foreach (var (reading, candidate) in cases)
            Assert.True(chemicals.Lookup(reading, []).Contains(candidate), $"{reading} の候補に {candidate} を出す");

        Assert.True(Checks.IsKanaReading("さんかどう2"), "価数を数字で指定したかな入力を読みとして扱う");
        Assert.True(!Checks.IsKanaReading("2026") && !Checks.IsKanaReading("n8n"), "数字だけ・英字混じりはかなの読みとして扱わない");
    }

    [Test]
    public static void ChemicalFormulaCandidates_AppearInTheConversionList()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-chemical-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            TestSession NewSession(IKanjiConverter? converter = null) => new(() => new Settings { LiveConversion = false }, converter ?? new CompositionTests.FakeConverter(),
                new CompositionServices { ChemicalCompounds = new ChemicalCompoundDictionary(directory) });

            var acetate = SessionFacadeTests.Type(NewSession(), "sakusannnatoriumu ")[^1].View;
            Assert.True(acetate?.Candidates.Take(CompositionConversion.CandidatePageSize).Contains("CH₃COONa") == true, "酢酸ナトリウムの式が候補の最初のページに出る: " + string.Join(",", acetate?.Candidates ?? []));

            var aniline = SessionFacadeTests.Type(NewSession(), "anirin ")[^1].View;
            Assert.True(aniline?.Candidates.Contains("C₆H₅NH₂") == true && aniline?.Candidates.Contains("C₆H₇N") == true, "アニリンの示性式と分子式が変換候補に出る: " + string.Join(",", aniline?.Candidates ?? []));

            var copperIi = SessionFacadeTests.Type(NewSession(), "sannkadou2 ")[^1].View;
            Assert.True(copperIi?.Candidates.Contains("CuO") == true, "価数を付けた酸化銅の式が変換候補に出る: " + string.Join(",", copperIi?.Candidates ?? []));
            Assert.True(copperIi?.Candidates.Contains("Cu₂O") == false, "価数を指定したとき別の酸化数を出さない");

            var splitCopperOxide = SessionFacadeTests.Type(NewSession(new SplitCopperOxideConverter()), "sannkadou ")[^1].View;
            Assert.True(splitCopperOxide?.Candidates.Contains("酸化銅") == true && splitCopperOxide.Candidates.Contains("CuO") && splitCopperOxide.Candidates.Contains("Cu₂O"),
                "変換エンジンが「さんか/どう」と分けても物質名全体の候補を出す: " + string.Join(",", splitCopperOxide?.Candidates ?? []));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class SplitCopperOxideConverter : TestConverter
    {
        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) => hiragana == "さんかどう"
            ? [new("さんか", "酸化"), new("どう", "銅")]
            : null;
    }
}
