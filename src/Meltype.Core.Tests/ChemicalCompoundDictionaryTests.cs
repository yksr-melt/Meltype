// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

using Meltype.Composition;
using Meltype.Config;
using Meltype.Dictionary;

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
            Assert.True(hydrate.Contains("CuSO₄·₅H₂O"), "水和数を式に保持する");
            Assert.True(hydrate.Contains("CuSO₄·₅H₂O (FW=249.677)"), "水和水を式量に含める");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void ChemicalFormulaCandidates_AppearInTheConversionList()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-chemical-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            TestSession NewSession() => new(() => new Settings { LiveConversion = false }, new CompositionTests.FakeConverter(),
                new CompositionServices { ChemicalCompounds = new ChemicalCompoundDictionary(directory) });

            var acetate = SessionFacadeTests.Type(NewSession(), "sakusannnatoriumu ")[^1].View;
            Assert.True(acetate?.Candidates.Take(CompositionConversion.CandidatePageSize).Contains("CH₃COONa") == true, "酢酸ナトリウムの式が候補の最初のページに出る: " + string.Join(",", acetate?.Candidates ?? []));

            var copperIi = SessionFacadeTests.Type(NewSession(), "sannkadou2 ")[^1].View;
            Assert.True(copperIi?.Candidates.Contains("CuO") == true, "価数を付けた酸化銅の式が変換候補に出る: " + string.Join(",", copperIi?.Candidates ?? []));
            Assert.True(copperIi?.Candidates.Contains("Cu₂O") == false, "価数を指定したとき別の酸化数を出さない");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
