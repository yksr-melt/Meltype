// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

using System.Globalization;
using System.Text;

namespace Meltype.Dictionary;

/// <summary>端末内の語彙と組成規則から、対応する物質名を化学式候補へ変換する。</summary>
internal static class ChemicalNameParser
{
    private static readonly Cation[] Cations = CreateCations();
    private static readonly Anion[] Anions = CreateAnions();
    private static readonly NamedFormula[] Acids =
    [
        new(["水", "みず", "water"], "H2O", true),
        new(["塩酸", "えんさん", "hydrochloric acid"], "HCl", true),
        new(["臭化水素酸", "しゅうかすいそさん", "hydrobromic acid"], "HBr", true),
        new(["フッ化水素酸", "ふっかすいそさん", "hydrofluoric acid"], "HF", true),
        new(["硫酸", "りゅうさん", "sulfuric acid"], "H2SO4", true),
        new(["亜硫酸", "ありゅうさん", "sulfurous acid"], "H2SO3", true),
        new(["硝酸", "しょうさん", "nitric acid"], "HNO3", true),
        new(["亜硝酸", "あしょうさん", "nitrous acid"], "HNO2", true),
        new(["炭酸", "たんさん", "carbonic acid"], "H2CO3", true),
        new(["リン酸", "燐酸", "りんさん", "phosphoric acid"], "H3PO4", true),
        new(["酢酸", "さくさん", "acetic acid", "ethanoic acid"], "C2H4O2", true, "CH3COOH"),
        new(["過酸化水素", "かさんかすいそ", "hydrogen peroxide"], "H2O2", true),
        new(["アンモニア", "あんもにあ", "ammonia"], "NH3", true),
    ];

    private static readonly ElementName[] StoichiometricElements =
    [
        new(["炭素", "たんそ", "carbon"], "C", true),
        new(["窒素", "ちっそ", "nitrogen"], "N", true),
        new(["酸素", "さんそ", "oxygen"], "O", true),
        new(["水素", "すいそ", "hydrogen"], "H", true),
        new(["硫黄", "いおう", "sulfur", "sulphur"], "S", true),
        new(["リン", "りん", "phosphorus"], "P", true),
        new(["塩素", "えんそ", "chlorine"], "Cl", true),
        new(["臭素", "しゅうそ", "bromine"], "Br", true),
        new(["ヨウ素", "ようそ", "iodine"], "I", true),
        new(["フッ素", "ふっそ", "fluorine"], "F", true),
        new(["ホウ素", "ほうそ", "boron"], "B", true),
        new(["ナトリウム", "なとりうむ", "sodium"], "Na", false),
        new(["カリウム", "かりうむ", "potassium"], "K", false),
        new(["リチウム", "りちうむ", "lithium"], "Li", false),
        new(["マグネシウム", "まぐねしうむ", "magnesium"], "Mg", false),
        new(["カルシウム", "かるしうむ", "calcium"], "Ca", false),
        new(["アルミニウム", "あるみにうむ", "aluminium", "aluminum"], "Al", false),
        new(["鉄", "てつ", "iron"], "Fe", false),
        new(["銅", "どう", "copper"], "Cu", false),
        new(["亜鉛", "あえん", "zinc"], "Zn", false),
        new(["銀", "ぎん", "silver"], "Ag", false),
        new(["スズ", "すず", "tin"], "Sn", false),
        new(["鉛", "なまり", "lead"], "Pb", false),
    ];

    private static readonly ParentName[] Parents = CreateParents();
    private static readonly Substituent[] Substituents = CreateSubstituents();
    private static readonly string[] PrefixNumbers = ["mono", "di", "tri", "tetra", "penta", "hexa", "hepta", "octa", "nona", "deca"];

    public static IReadOnlyList<ChemicalCompound> Parse(string input)
    {
        var name = Normalize(input);
        if (name.Length is < 2 or > 128) return [];

        var hydrateCount = 0;
        if (TrySplitHydrate(name, out var anhydrousName, out var waters))
        {
            name = anhydrousName;
            hydrateCount = waters;
        }

        var parsed = FindNamedAcid(name) ?? TryParseJapaneseStoichiometry(name) ?? TryParseEnglishStoichiometry(name) ?? TryParseIonic(name) ?? TryParseOrganic(name);
        if (parsed is null) return [];
        if (hydrateCount == 0) return parsed;

        return parsed.Select(compound => compound with
        {
            Formula = compound.Formula + "·" + hydrateCount.ToString(CultureInfo.InvariantCulture) + "H2O",
            PreferredFormula = (compound.PreferredFormula ?? compound.Formula) + "·" + hydrateCount.ToString(CultureInfo.InvariantCulture) + "H2O",
            Identity = compound.Identity + "·" + hydrateCount.ToString(CultureInfo.InvariantCulture) + "H2O",
        }).ToArray();
    }

    private static ChemicalCompound[]? FindNamedAcid(string name)
    {
        foreach (var entry in Acids)
            if (entry.Aliases.Any(alias => Normalize(alias) == name)) return [Compound(entry.Formula, entry.IsMolecule, entry.PreferredFormula)];
        return null;
    }

    private static ChemicalCompound[]? TryParseJapaneseStoichiometry(string name)
    {
        if (!TryReadLeadingMultiplier(name, out var anionCount, out var afterAnionCount)) return null;
        foreach (var anion in Anions.Where(a => a.StoichiometricName is not null).OrderByDescending(a => a.StoichiometricName!.Length))
        {
            foreach (var alias in anion.Aliases.OrderByDescending(a => a.Length))
            {
                if (!afterAnionCount.StartsWith(alias, StringComparison.Ordinal)) continue;
                var rest = afterAnionCount[alias.Length..];
                var elementCount = 1;
                var elementPart = rest;
                if (TryReadLeadingMultiplier(rest, out var parsedElementCount, out var afterElementCount))
                {
                    elementCount = parsedElementCount;
                    elementPart = afterElementCount;
                }
                foreach (var element in StoichiometricElements.OrderByDescending(e => e.Aliases.Max(a => a.Length)))
                {
                    if (!TryMatchElementAtEnd(elementPart, element, out var leadingCount) || !ChemicalFormula.TryParse(anion.StoichiometricName!, out var group)) continue;
                    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var (symbol, count) in group)
                        counts[symbol] = count * anionCount;
                    counts[element.Symbol] = counts.GetValueOrDefault(element.Symbol) + leadingCount * elementCount;
                    var formula = element.AllowMolecularCompound
                        ? FormulaFromCounts(counts)
                        : FormatGroup(element.Symbol, leadingCount * elementCount) + FormatGroup(anion.StoichiometricName!, anionCount);
                    return [Compound(formula, molecular: element.AllowMolecularCompound)];
                }
            }
        }
        return null;
    }

    private static ChemicalCompound[]? TryParseEnglishStoichiometry(string name)
    {
        foreach (var element in StoichiometricElements.OrderByDescending(e => e.Aliases.Max(a => a.Length)))
            foreach (var firstAlias in element.Aliases.Where(a => a.All(char.IsAsciiLetter)).OrderByDescending(a => a.Length))
            {
                var firstCount = 1;
                var elementName = name;
                if (TryReadLeadingMultiplier(name, out var leadingCount, out var withoutCount) && withoutCount.StartsWith(firstAlias, StringComparison.Ordinal))
                {
                    firstCount = leadingCount;
                    elementName = withoutCount;
                }
                if (!elementName.StartsWith(firstAlias, StringComparison.Ordinal)) continue;
                var rest = elementName[firstAlias.Length..];
                foreach (var anion in Anions.Where(a => a.EnglishStoichiometricSuffix is not null).OrderByDescending(a => a.EnglishStoichiometricSuffix!.Length))
                {
                    var suffix = anion.EnglishStoichiometricSuffix!;
                    var prefixCount = 1;
                    var groupName = rest;
                    var countText = PrefixNumbers.FirstOrDefault(rest.StartsWith);
                    if (suffix == "oxide" && rest.StartsWith("monoxide", StringComparison.Ordinal))
                    {
                        // The final o in mono is dropped before oxide, producing monoxide.
                        prefixCount = 1;
                        groupName = rest[3..];
                        countText = "mono";
                    }
                    else if (countText is not null)
                    {
                        prefixCount = Array.IndexOf(PrefixNumbers, countText) + 1;
                        groupName = rest[countText.Length..];
                    }
                    if (groupName != suffix) continue;
                    if (!element.AllowMolecularCompound && leadingCount == 1 && countText is null) continue;
                    if (!ChemicalFormula.TryParse(anion.StoichiometricName ?? anion.GroupFormula, out var group)) continue;
                    var counts = new Dictionary<string, int>(StringComparer.Ordinal) { [element.Symbol] = firstCount };
                    foreach (var (symbol, count) in group) counts[symbol] = counts.GetValueOrDefault(symbol) + count * prefixCount;
                    var formula = element.AllowMolecularCompound
                        ? FormulaFromCounts(counts)
                        : FormatGroup(element.Symbol, firstCount) + FormatGroup(anion.StoichiometricName ?? anion.GroupFormula, prefixCount);
                    return [Compound(formula, molecular: element.AllowMolecularCompound)];
                }
            }
        return null;
    }

    private static ChemicalCompound[]? TryParseIonic(string name)
    {
        foreach (var anion in Anions.OrderByDescending(a => a.Aliases.Max(alias => alias.Length)))
            foreach (var alias in anion.Aliases.OrderByDescending(a => a.Length))
            {
                if (!name.StartsWith(alias, StringComparison.Ordinal)) continue;
                var cationPart = name[alias.Length..];
                if (TryFindCation(cationPart, atEnd: true, out var cation, out var oxidation))
                    return FormIonicCandidates(cation, oxidation, anion);
            }

        foreach (var anion in Anions.Where(a => a.EnglishName is not null).OrderByDescending(a => a.EnglishName!.Length))
        {
            var suffix = anion.EnglishName!;
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var cationPart = name[..^suffix.Length];
            if (TryFindCation(cationPart, atEnd: false, out var cation, out var oxidation))
                return FormIonicCandidates(cation, oxidation, anion);
        }
        return null;
    }

    private static bool TryFindCation(string text, bool atEnd, out Cation cation, out int? oxidation)
    {
        text = text.Trim('-', ' ');
        oxidation = null;
        if (TryRemoveOxidation(text, out var withoutOxidation, out var state))
        {
            text = withoutOxidation;
            oxidation = state;
        }

        foreach (var candidate in Cations.OrderByDescending(c => c.Aliases.Max(a => a.Length)))
            foreach (var alias in candidate.Aliases.OrderByDescending(a => a.Length))
            {
                var matches = atEnd ? text.EndsWith(alias, StringComparison.Ordinal) : text.StartsWith(alias, StringComparison.Ordinal);
                if (!matches) continue;
                var remainder = atEnd ? text[..^alias.Length] : text[alias.Length..];
                if (remainder.Length != 0) continue;
                if (oxidation is { } value && !candidate.Charges.Contains(value)) continue;
                cation = candidate;
                return true;
            }
        cation = null!;
        return false;
    }

    private static ChemicalCompound[] FormIonicCandidates(Cation cation, int? oxidation, Anion anion)
    {
        var candidates = new List<ChemicalCompound>();
        foreach (var positiveCharge in oxidation is { } charge ? [charge] : cation.Charges)
        {
            var divisor = GreatestCommonDivisor(positiveCharge, anion.Charge);
            var cationCount = anion.Charge / divisor;
            var anionCount = positiveCharge / divisor;
            var cationGroup = FormatGroup(cation.GroupFormula, cationCount);
            var anionGroup = FormatGroup(anion.GroupFormula, anionCount);
            var formula = anion.AnionFirst ? anionGroup + cationGroup : cationGroup + anionGroup;
            candidates.Add(Compound(formula, molecular: false));
        }
        return candidates.ToArray();
    }

    private static ChemicalCompound[]? TryParseOrganic(string name)
    {
        var nameWithoutLocants = RemoveLocants(name);
        foreach (var parent in Parents.OrderByDescending(p => p.Alias.Length))
        {
            var start = nameWithoutLocants.IndexOf(parent.Alias, StringComparison.Ordinal);
            if (start < 0) continue;
            if (!ValidLocants(name, parent.CarbonCount)) continue;
            var before = nameWithoutLocants[..start];
            var after = nameWithoutLocants[(start + parent.Alias.Length)..];
            if (parent.RequiresSuffix && after.Length == 0) continue;
            if (!TryParseSubstituents(before, out var substituentDelta) || !TryParseOrganicSuffix(after, out var suffix)) continue;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["C"] = parent.CarbonCount,
                ["H"] = 2 * parent.CarbonCount + 2 - parent.DoubleBonds * 2 - parent.TripleBonds * 4,
            };
            if (parent.FixedFormula is not null)
            {
                if (!ChemicalFormula.TryParse(parent.FixedFormula, out var fixedCounts)) continue;
                counts = new Dictionary<string, int>(fixedCounts, StringComparer.Ordinal);
            }

            foreach (var (symbol, count) in substituentDelta)
                counts[symbol] = counts.GetValueOrDefault(symbol) + count;
            foreach (var (symbol, count) in suffix.CountDelta)
                counts[symbol] = counts.GetValueOrDefault(symbol) + count;
            if (counts.Any(pair => pair.Value < 0) || !counts.TryGetValue("C", out var carbons) || carbons == 0) continue;

            return [Compound(FormulaFromCounts(counts), molecular: true)];
        }
        return null;
    }

    private static bool TryParseSubstituents(string text, out Dictionary<string, int> delta)
    {
        delta = new Dictionary<string, int>(StringComparer.Ordinal);
        text = RemoveLocants(text);
        while (text.Length > 0)
        {
            var match = Substituents.OrderByDescending(s => s.Aliases.Max(a => a.Length))
                .SelectMany(s => s.Aliases.Select(alias => (Substituent: s, Alias: alias)))
                .OrderByDescending(item => item.Alias.Length)
                .FirstOrDefault(item => text.StartsWith(item.Alias, StringComparison.Ordinal));
            if (match.Alias is null) return false;
            foreach (var (symbol, count) in match.Substituent.CountDelta)
                delta[symbol] = delta.GetValueOrDefault(symbol) + count;
            text = text[match.Alias.Length..];
        }
        return true;
    }

    private static bool TryParseOrganicSuffix(string text, out OrganicSuffix suffix)
    {
        text = RemoveLocants(text);
        foreach (var candidate in OrganicSuffixes.OrderByDescending(s => s.Aliases.Max(a => a.Length)))
            if (candidate.Aliases.Contains(text, StringComparer.Ordinal))
            {
                suffix = candidate;
                return true;
            }
        suffix = OrganicSuffix.None;
        return text.Length == 0;
    }

    private static readonly OrganicSuffix[] OrganicSuffixes =
    [
        new([""], new Dictionary<string, int>(StringComparer.Ordinal)),
        new(["ol", "おーる"], new Dictionary<string, int>(StringComparer.Ordinal) { ["O"] = 1 }),
        new(["diol", "じおーる"], new Dictionary<string, int>(StringComparer.Ordinal) { ["O"] = 2 }),
        new(["triol", "さんおーる"], new Dictionary<string, int>(StringComparer.Ordinal) { ["O"] = 3 }),
        new(["one", "おん"], new Dictionary<string, int>(StringComparer.Ordinal) { ["H"] = -2, ["O"] = 1 }),
        new(["al", "あーる"], new Dictionary<string, int>(StringComparer.Ordinal) { ["H"] = -2, ["O"] = 1 }),
        new(["oicacid", "さん"], new Dictionary<string, int>(StringComparer.Ordinal) { ["H"] = -2, ["O"] = 2 }),
        new(["amine", "あみん"], new Dictionary<string, int>(StringComparer.Ordinal) { ["H"] = 1, ["N"] = 1 }),
        new(["thiol", "ちおーる"], new Dictionary<string, int>(StringComparer.Ordinal) { ["S"] = 1 }),
        new(["oxy", "ろきし"], new Dictionary<string, int>(StringComparer.Ordinal) { ["O"] = 1 }),
    ];

    private static bool TrySplitHydrate(string name, out string anhydrousName, out int waters)
    {
        foreach (var suffix in new[] { "水和物", "すいわぶつ", "hydrate" })
        {
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var beforeSuffix = name[..^suffix.Length];
            foreach (var (text, count) in JapaneseNumbers.Concat(EnglishNumbers).OrderByDescending(pair => pair.Text.Length))
                if (beforeSuffix.EndsWith(text, StringComparison.Ordinal))
                {
                    anhydrousName = beforeSuffix[..^text.Length];
                    waters = count;
                    return anhydrousName.Length > 0;
                }
            var digitStart = beforeSuffix.Length;
            while (digitStart > 0 && char.IsAsciiDigit(beforeSuffix[digitStart - 1])) digitStart--;
            if (digitStart < beforeSuffix.Length && int.TryParse(beforeSuffix.AsSpan(digitStart), NumberStyles.None, CultureInfo.InvariantCulture, out waters) && waters > 0)
            {
                anhydrousName = beforeSuffix[..digitStart];
                return anhydrousName.Length > 0;
            }
        }
        anhydrousName = "";
        waters = 0;
        return false;
    }

    private static bool TryReadLeadingMultiplier(string text, out int count, out string rest)
    {
        foreach (var (number, value) in JapaneseNumbers.Concat(EnglishNumbers).OrderByDescending(pair => pair.Text.Length))
            if (text.StartsWith(number, StringComparison.Ordinal))
            {
                count = value;
                rest = text[number.Length..];
                return true;
            }
        var digitEnd = 0;
        while (digitEnd < text.Length && char.IsAsciiDigit(text[digitEnd])) digitEnd++;
        if (digitEnd > 0 && int.TryParse(text.AsSpan(0, digitEnd), NumberStyles.None, CultureInfo.InvariantCulture, out count) && count is > 0 and <= 100)
        {
            rest = text[digitEnd..];
            return true;
        }
        count = 1;
        rest = text;
        return false;
    }

    private static bool TryMatchElementAtEnd(string text, ElementName element, out int leadingCount)
    {
        foreach (var alias in element.Aliases.OrderByDescending(a => a.Length))
        {
            if (!text.EndsWith(alias, StringComparison.Ordinal)) continue;
            var before = text[..^alias.Length];
            if (before.Length == 0) { leadingCount = 1; return true; }
            if (TryReadLeadingMultiplier(before, out leadingCount, out var rest) && rest.Length == 0) return true;
        }
        leadingCount = 0;
        return false;
    }

    private static bool TryRemoveOxidation(string text, out string name, out int state)
    {
        var close = text.Length > 0 && text[^1] == ')' ? text.Length - 1 : -1;
        if (close > 0 && text.LastIndexOf('(') is var open && open >= 0 && TryParseRomanOrNumber(text[(open + 1)..close], out state))
        {
            name = text[..open];
            return name.Length > 0;
        }
        var start = text.Length;
        while (start > 0 && char.IsAsciiDigit(text[start - 1])) start--;
        if (start < text.Length && int.TryParse(text.AsSpan(start), NumberStyles.None, CultureInfo.InvariantCulture, out state) && state is > 0 and <= 8)
        {
            name = text[..start];
            return name.Length > 0;
        }
        name = text;
        state = 0;
        return false;
    }

    private static bool TryParseRomanOrNumber(string text, out int value)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return value is > 0 and <= 8;
        value = 0;
        foreach (var character in text)
        {
            var part = character switch { 'i' => 1, 'v' => 5, 'x' => 10, _ => 0 };
            if (part == 0) return false;
            value += part;
        }
        for (var index = 0; index + 1 < text.Length; index++)
            if (RomanValue(text[index]) < RomanValue(text[index + 1])) value -= 2 * RomanValue(text[index]);
        return value is > 0 and <= 8;
    }

    private static int RomanValue(char character) => character switch { 'i' => 1, 'v' => 5, 'x' => 10, _ => 0 };

    private static string FormatGroup(string formula, int count)
    {
        if (count == 1) return formula;
        if (ChemicalFormula.TryParse(formula, out var counts) && counts.Count == 1 && counts.Values.Single() == 1)
            return formula + count.ToString(CultureInfo.InvariantCulture);
        return $"({formula}){count.ToString(CultureInfo.InvariantCulture)}";
    }

    private static ChemicalCompound Compound(string formula, bool molecular, string? preferredFormula = null) =>
        new(formula + (molecular ? ":MW" : ":FW"), formula, preferredFormula, molecular);

    private static int GreatestCommonDivisor(int first, int second)
    {
        while (second != 0) (first, second) = (second, first % second);
        return first;
    }

    private static string FormulaFromCounts(Dictionary<string, int> counts)
    {
        var symbols = counts.Keys.OrderBy(symbol => symbol == "C" ? 0 : symbol == "H" ? 1 : 2).ThenBy(symbol => symbol, StringComparer.Ordinal);
        return string.Concat(symbols.Where(symbol => counts[symbol] > 0)
            .Select(symbol => symbol + (counts[symbol] == 1 ? "" : counts[symbol].ToString(CultureInfo.InvariantCulture))));
    }

    private static string RemoveLocants(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var character in text)
            if (!char.IsAsciiDigit(character) && character is not '-' and not ',' and not '.' and not ' ' and not '(' and not ')') result.Append(character);
        return result.ToString();
    }

    private static bool ValidLocants(string text, int maximum)
    {
        for (var position = 0; position < text.Length;)
        {
            if (!char.IsAsciiDigit(text[position])) { position++; continue; }
            var start = position++;
            while (position < text.Length && char.IsAsciiDigit(text[position])) position++;
            if (!int.TryParse(text.AsSpan(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture, out var locant) || locant is < 1 || locant > maximum)
                return false;
        }
        return true;
    }

    private static string Normalize(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character)) continue;
            result.Append(character is >= '\u30a1' and <= '\u30f6' ? (char)(character - 0x60) : character);
        }
        return result.ToString();
    }

    private static readonly (string Text, int Count)[] JapaneseNumbers =
    [
        ("じゅう", 10), ("十", 10), ("九", 9), ("八", 8), ("七", 7), ("六", 6), ("五", 5), ("四", 4), ("三", 3), ("二", 2), ("一", 1),
        ("きゅう", 9), ("はち", 8), ("なな", 7), ("しち", 7), ("ろく", 6), ("ご", 5), ("よん", 4), ("し", 4), ("さん", 3), ("に", 2), ("いち", 1),
    ];

    private static readonly (string Text, int Count)[] EnglishNumbers =
    [
        ("deca", 10), ("nona", 9), ("octa", 8), ("hepta", 7), ("hexa", 6), ("penta", 5), ("tetra", 4), ("tri", 3), ("di", 2), ("mono", 1),
    ];

    private static Cation[] CreateCations() =>
    [
        new(["ナトリウム", "なとりうむ", "sodium"], "Na", [1]),
        new(["カリウム", "かりうむ", "potassium"], "K", [1]),
        new(["リチウム", "りちうむ", "lithium"], "Li", [1]),
        new(["銀", "ぎん", "silver"], "Ag", [1]),
        new(["マグネシウム", "まぐねしうむ", "magnesium"], "Mg", [2]),
        new(["カルシウム", "かるしうむ", "calcium"], "Ca", [2]),
        new(["ストロンチウム", "すとろんちうむ", "strontium"], "Sr", [2]),
        new(["バリウム", "ばりうむ", "barium"], "Ba", [2]),
        new(["亜鉛", "あえん", "zinc"], "Zn", [2]),
        new(["アルミニウム", "あるみにうむ", "aluminium", "aluminum"], "Al", [3]),
        new(["アンモニウム", "あんもにうむ", "ammonium"], "NH4", [1]),
        new(["銅", "どう", "copper"], "Cu", [1, 2]),
        new(["鉄", "てつ", "iron"], "Fe", [2, 3]),
        new(["コバルト", "こばると", "cobalt"], "Co", [2, 3]),
        new(["ニッケル", "にっける", "nickel"], "Ni", [2, 3]),
        new(["マンガン", "まんがん", "manganese"], "Mn", [2, 4, 7]),
        new(["クロム", "くろむ", "chromium"], "Cr", [2, 3, 6]),
        new(["スズ", "すず", "tin"], "Sn", [2, 4]),
        new(["鉛", "なまり", "lead"], "Pb", [2, 4]),
        new(["水銀", "すいぎん", "mercury"], "Hg", [1, 2]),
    ];

    private static Anion[] CreateAnions() =>
    [
        new(["過マンガン酸", "かまんがんさん"], "MnO4", 1, "permanganate", AnionFirst: false),
        new(["次亜塩素酸", "じあえんそさん"], "ClO", 1, "hypochlorite", AnionFirst: false),
        new(["過塩素酸", "かえんそさん"], "ClO4", 1, "perchlorate", AnionFirst: false),
        new(["亜塩素酸", "あえんそさん"], "ClO2", 1, "chlorite", AnionFirst: false),
        new(["塩素酸", "えんそさん"], "ClO3", 1, "chlorate", AnionFirst: false),
        new(["重クロム酸", "じゅうくろむさん"], "Cr2O7", 2, "dichromate", AnionFirst: false),
        new(["水酸化", "すいさんか"], "OH", 1, "hydroxide", AnionFirst: false),
        new(["亜硝酸", "あしょうさん"], "NO2", 1, "nitrite", AnionFirst: false),
        new(["亜硫酸", "ありゅうさん"], "SO3", 2, "sulfite", AnionFirst: false),
        new(["硫酸", "りゅうさん"], "SO4", 2, "sulfate", AnionFirst: false),
        new(["硝酸", "しょうさん"], "NO3", 1, "nitrate", AnionFirst: false),
        new(["リン酸", "燐酸", "りんさん"], "PO4", 3, "phosphate", AnionFirst: false),
        new(["炭酸", "たんさん"], "CO3", 2, "carbonate", AnionFirst: false),
        new(["酢酸", "さくさん"], "CH3COO", 1, "acetate", AnionFirst: true),
        new(["酸化", "さんか"], "O", 2, "oxide", AnionFirst: false, StoichiometricName: "O", EnglishStoichiometricSuffix: "oxide"),
        new(["塩化", "えんか"], "Cl", 1, "chloride", AnionFirst: false, StoichiometricName: "Cl", EnglishStoichiometricSuffix: "chloride"),
        new(["フッ化", "ふっか"], "F", 1, "fluoride", AnionFirst: false, StoichiometricName: "F", EnglishStoichiometricSuffix: "fluoride"),
        new(["臭化", "しゅうか"], "Br", 1, "bromide", AnionFirst: false, StoichiometricName: "Br", EnglishStoichiometricSuffix: "bromide"),
        new(["ヨウ化", "ようか"], "I", 1, "iodide", AnionFirst: false, StoichiometricName: "I", EnglishStoichiometricSuffix: "iodide"),
        new(["硫化", "りゅうか"], "S", 2, "sulfide", AnionFirst: false, StoichiometricName: "S", EnglishStoichiometricSuffix: "sulfide"),
        new(["窒化", "ちっか"], "N", 3, "nitride", AnionFirst: false, StoichiometricName: "N", EnglishStoichiometricSuffix: "nitride"),
        new(["水素化", "すいそか"], "H", 1, "hydride", AnionFirst: false, StoichiometricName: "H", EnglishStoichiometricSuffix: "hydride"),
    ];

    private static ParentName[] CreateParents()
    {
        List<ParentName> parents =
        [
            new(["benzene", "べんぜん"], 6, 0, 0, "C6H6"),
            new(["methane", "methan", "めたん"], 1),
            new(["ethane", "ethan", "えたん"], 2),
            new(["propane", "propan", "ぷろぱん"], 3),
            new(["butane", "butan", "ぶたん"], 4),
            new(["pentane", "pentan", "ぺんたん"], 5),
            new(["hexane", "hexan", "へきさん"], 6),
            new(["heptane", "heptan", "へぷたん"], 7),
            new(["octane", "octan", "おくたん"], 8),
            new(["nonane", "nonan", "のなん"], 9),
            new(["decane", "decan", "でかん"], 10),
            new(["undecane", "undecan", "うんでかん"], 11),
            new(["dodecane", "dodecan", "どでかん"], 12),
            new(["tridecane", "tridecan", "とりでかん"], 13),
            new(["tetradecane", "tetradecan", "てとらでかん"], 14),
            new(["pentadecane", "pentadecan", "ぺんたでかん"], 15),
            new(["hexadecane", "hexadecan", "へきさでかん"], 16),
            new(["heptadecane", "heptadecan", "へぷたでかん"], 17),
            new(["octadecane", "octadecan", "おくたでかん"], 18),
            new(["nonadecane", "nonadecan", "のなでかん"], 19),
            new(["eicosane", "icosane", "eicosan", "いこさん"], 20),
            new(["ethene", "ethen", "えてん"], 2, DoubleBonds: 1),
            new(["propene", "propen", "ぷろぺん"], 3, DoubleBonds: 1),
            new(["butene", "buten", "ぶてん"], 4, DoubleBonds: 1),
            new(["pentene", "penten", "ぺんてん"], 5, DoubleBonds: 1),
            new(["hexene", "hexen", "へきせん"], 6, DoubleBonds: 1),
            new(["heptene", "hepten", "へぷてん"], 7, DoubleBonds: 1),
            new(["octene", "octen", "おくてん"], 8, DoubleBonds: 1),
            new(["nonene", "nonen", "のねん"], 9, DoubleBonds: 1),
            new(["decene", "decen", "でけん"], 10, DoubleBonds: 1),
            new(["ethyne", "ethyn", "えちん"], 2, TripleBonds: 1),
            new(["propyne", "propyn", "ぷろぴん"], 3, TripleBonds: 1),
            new(["butyne", "butyn", "ぶちん"], 4, TripleBonds: 1),
            new(["pentyne", "pentyn", "ぺんちん"], 5, TripleBonds: 1),
            new(["hexyne", "hex-yne", "hexyn", "へきしん"], 6, TripleBonds: 1),
            new(["heptyne", "heptyn", "へぷちん"], 7, TripleBonds: 1),
            new(["octyne", "octyn", "おくちん"], 8, TripleBonds: 1),
            new(["nonyne", "nonyn", "のにん"], 9, TripleBonds: 1),
            new(["decyne", "decyn", "でしん"], 10, TripleBonds: 1),
            new(["butadiene", "butadien", "ぶたじえん"], 4, DoubleBonds: 2),
            new(["pentadiene", "pentadien", "ぺんたじえん"], 5, DoubleBonds: 2),
            new(["hexadiene", "hexadien", "へきさじえん"], 6, DoubleBonds: 2),
        ];
        return parents.SelectMany(parent => parent.Aliases.Select(alias => parent with
        {
            Alias = alias,
            RequiresSuffix = alias is "methan" or "ethan" or "propan" or "butan" or "pentan" or "hexan" or "heptan" or "octan" or "nonan" or "decan"
                or "undecan" or "dodecan" or "tridecan" or "tetradecan" or "pentadecan" or "hexadecan" or "heptadecan" or "octadecan" or "nonadecan" or "eicosan"
                or "ethen" or "propen" or "buten" or "penten" or "hexen" or "hepten" or "octen" or "nonen" or "decen"
                or "ethyn" or "propyn" or "butyn" or "pentyn" or "hexyn" or "heptyn" or "octyn" or "nonyn" or "decyn"
                or "butadien" or "pentadien" or "hexadien",
        })).ToArray();
    }

    private static Substituent[] CreateSubstituents() =>
    [
        new(["tetramethyl", "よんめちる"], ("C", 4), ("H", 8)),
        new(["trimethyl", "さんめちる"], ("C", 3), ("H", 6)),
        new(["dimethyl", "じめちる"], ("C", 2), ("H", 4)),
        new(["tetrahydroxy", "よんひどろきし"], ("O", 4)),
        new(["trihydroxy", "さんひどろきし"], ("O", 3)),
        new(["dihydroxy", "じひどろきし"], ("O", 2)),
        new(["triamino", "さんあみの"], ("H", 3), ("N", 3)),
        new(["diamino", "じあみの"], ("H", 2), ("N", 2)),
        new(["trinitro", "さんにとろ"], ("N", 3), ("O", 6)),
        new(["dinitro", "じにとろ"], ("N", 2), ("O", 4)),
        new(["tetraethyl", "よんえちる"], ("C", 8), ("H", 16)),
        new(["triethyl", "さんえちる"], ("C", 6), ("H", 12)),
        new(["diethyl", "じえちる"], ("C", 4), ("H", 8)),
        new(["dichloro", "じくろろ"], ("H", -2), ("Cl", 2)),
        new(["trichloro", "さんくろろ"], ("H", -3), ("Cl", 3)),
        new(["difluoro", "じふるおろ"], ("H", -2), ("F", 2)),
        new(["dibromo", "じぶろも"], ("H", -2), ("Br", 2)),
        new(["methyl", "めちる"], ("C", 1), ("H", 2)),
        new(["ethyl", "えちる"], ("C", 2), ("H", 4)),
        new(["isopropyl", "いそぷろぴる"], ("C", 3), ("H", 6)),
        new(["propyl", "ぷろぴる"], ("C", 3), ("H", 6)),
        new(["tertbutyl", "secbutyl", "isobutyl", "てるとぶちる", "せかんだりぶちる", "いそぶちる"], ("C", 4), ("H", 8)),
        new(["butyl", "ぶちる"], ("C", 4), ("H", 8)),
        new(["chloro", "くろろ"], ("H", -1), ("Cl", 1)),
        new(["bromo", "ぶろも"], ("H", -1), ("Br", 1)),
        new(["fluoro", "ふるおろ"], ("H", -1), ("F", 1)),
        new(["iodo", "よーど"], ("H", -1), ("I", 1)),
        new(["hydroxy", "ひどろきし"], ("O", 1)),
        new(["amino", "あみの"], ("H", 1), ("N", 1)),
        new(["nitro", "にとろ"], ("N", 1), ("O", 2)),
    ];

    private sealed record Cation(string[] Aliases, string GroupFormula, int[] Charges);
    private sealed record Anion(string[] Aliases, string GroupFormula, int Charge, string? EnglishName, bool AnionFirst, string? StoichiometricName = null, string? EnglishStoichiometricSuffix = null);
    private sealed record NamedFormula(string[] Aliases, string Formula, bool IsMolecule, string? PreferredFormula = null);
    private sealed record ElementName(string[] Aliases, string Symbol, bool AllowMolecularCompound);
    private sealed record ParentName(string[] Aliases, int CarbonCount, int DoubleBonds = 0, int TripleBonds = 0, string? FixedFormula = null, string Alias = "", bool RequiresSuffix = false);
    private sealed record Substituent(string[] Aliases, Dictionary<string, int> CountDelta)
    {
        public Substituent(string[] aliases, params (string Symbol, int Count)[] delta) : this(aliases, delta.ToDictionary(pair => pair.Symbol, pair => pair.Count, StringComparer.Ordinal)) { }
    }
    private sealed record OrganicSuffix(string[] Aliases, Dictionary<string, int> CountDelta)
    {
        public static readonly OrganicSuffix None = new([], new Dictionary<string, int>(StringComparer.Ordinal));
    }
}
