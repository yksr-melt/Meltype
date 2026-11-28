// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

using System.Text;

namespace Meltype.Dictionary;

/// <summary>利用者が用意した名称表を起動時に読み、端末内の物質名別名として保持する。</summary>
internal sealed class ChemicalNameCatalog
{
    private const string ResourceName = "Meltype.Dictionary.chemical-compounds.tsv";
    private readonly Dictionary<string, List<ChemicalCompound>> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ChemicalCompound>> _unqualifiedValences = new(StringComparer.Ordinal);

    public ChemicalNameCatalog() => Load();

    public bool TryLookup(string term, out IReadOnlyList<ChemicalCompound> compounds, out bool mergeRuleCandidates)
    {
        var name = ChemicalNameParser.Normalize(term);
        var exactMatch = _exact.TryGetValue(name, out var exact);
        var valenceMatch = _unqualifiedValences.TryGetValue(name, out var valences);
        if (!exactMatch && !valenceMatch)
        {
            compounds = [];
            mergeRuleCandidates = false;
            return false;
        }

        var matches = new List<ChemicalCompound>();
        AddDistinct(matches, exact);
        AddDistinct(matches, valences);
        compounds = matches;
        // 複数の価数が登録された曖昧な名称では、一般的な電荷規則から推測した候補を混ぜない。
        mergeRuleCandidates = !exactMatch && valenceMatch && matches.Select(compound => compound.Formula).Distinct(StringComparer.Ordinal).Count() == 1;
        return true;
    }

    private void Load()
    {
        using var stream = typeof(ChemicalNameCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"埋め込み物質名一覧「{ResourceName}」を読み込めません。");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("ひらがな\t", StringComparison.Ordinal)) continue;
            var fields = line.Split('\t');
            if (fields.Length != 3) throw new InvalidDataException("物質名一覧は「読み・日本語名・化学式」の3列にしてください。");

            var reading = ChemicalNameParser.Normalize(fields[0]);
            var japaneseName = ChemicalNameParser.Normalize(fields[1]);
            var displayFormula = fields[2].Trim();
            if (reading.Length == 0 || japaneseName.Length == 0 || displayFormula.Length == 0)
                throw new InvalidDataException("物質名一覧に空の読み・日本語名・化学式があります。");

            var parsedFormula = FormulaForCalculation(displayFormula);
            IReadOnlyDictionary<string, int>? counts = null;
            if (parsedFormula is not null && ChemicalFormula.TryParse(parsedFormula, out var parsedCounts)) counts = parsedCounts;
            var molecularFormula = counts is null
                ? displayFormula.Replace('・', '·')
                : ChemicalNameParser.FormulaFromCounts(counts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            var isMolecule = counts is not null && !IsFormulaWeightCompound(fields[1], counts);
            var preferredFormula = isMolecule && IsCondensedStructure(displayFormula) ? displayFormula : null;
            var formula = preferredFormula is null ? displayFormula.Replace('・', '·') : molecularFormula;
            var identity = formula + (isMolecule ? ":MW:" : ":FW:") + (preferredFormula ?? formula);
            var compound = new ChemicalCompound(identity, formula, preferredFormula, isMolecule);

            Add(_exact, reading, compound);
            Add(_exact, japaneseName, compound);
            if (TryUnqualifiedNames(reading, japaneseName, out var readingBase, out var japaneseBase))
            {
                Add(_unqualifiedValences, readingBase, compound);
                Add(_unqualifiedValences, japaneseBase, compound);
            }
        }
    }

    private static string? FormulaForCalculation(string formula)
    {
        var normalized = formula.Replace('・', '·')
            .Replace("=", "", StringComparison.Ordinal)
            .Replace("≡", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("−", "", StringComparison.Ordinal);
        return ChemicalFormula.TryParse(normalized, out _) ? normalized : null;
    }

    private static bool IsCondensedStructure(string formula)
    {
        if (formula.IndexOfAny(['=', '≡', '-']) >= 0) return true;
        var elements = new HashSet<string>(StringComparer.Ordinal);
        for (var position = 0; position < formula.Length;)
        {
            if (formula[position] is < 'A' or > 'Z') { position++; continue; }
            var start = position++;
            if (position < formula.Length && formula[position] is >= 'a' and <= 'z') position++;
            if (!elements.Add(formula[start..position])) return true;
            while (position < formula.Length && char.IsAsciiDigit(formula[position])) position++;
        }
        return false;
    }

    private static bool IsFormulaWeightCompound(string name, IReadOnlyDictionary<string, int> counts) =>
        counts.Keys.Any(FormulaWeightElements.Contains) || FormulaWeightNameMarkers.Any(name.Contains);

    private static bool TryUnqualifiedNames(string reading, string japaneseName, out string readingBase, out string japaneseBase)
    {
        var open = japaneseName.LastIndexOf('(');
        if (open >= 1 && japaneseName[^1] == ')' && OxidationStates.TryGetValue(japaneseName[(open + 1)..^1], out var oxidation))
        {
            japaneseBase = japaneseName[..open];
            var digitStart = reading.Length;
            while (digitStart > 0 && char.IsAsciiDigit(reading[digitStart - 1])) digitStart--;
            if (digitStart < reading.Length && int.TryParse(reading.AsSpan(digitStart), out var readingState) && readingState == oxidation)
                readingBase = reading[..digitStart];
            else
                readingBase = reading;
            return true;
        }

        var readingEnd = reading.Length;
        while (readingEnd > 0 && char.IsAsciiDigit(reading[readingEnd - 1])) readingEnd--;
        if (readingEnd < reading.Length && int.TryParse(reading.AsSpan(readingEnd), out var numericOxidation) && numericOxidation is > 0 and <= 10)
        {
            readingBase = reading[..readingEnd];
            japaneseBase = "";
            return readingBase.Length > 0;
        }
        readingBase = "";
        japaneseBase = "";
        return false;
    }

    private static void Add(Dictionary<string, List<ChemicalCompound>> index, string key, ChemicalCompound compound)
    {
        if (key.Length == 0) return;
        if (!index.TryGetValue(key, out var compounds)) index.Add(key, compounds = []);
        if (!compounds.Any(existing => existing.Identity == compound.Identity)) compounds.Add(compound);
    }

    private static void AddDistinct(List<ChemicalCompound> target, List<ChemicalCompound>? source)
    {
        if (source is null) return;
        foreach (var compound in source)
            if (!target.Any(existing => existing.Identity == compound.Identity)) target.Add(compound);
    }

    private static readonly Dictionary<string, int> OxidationStates = new(StringComparer.Ordinal)
    {
        ["i"] = 1, ["ii"] = 2, ["iii"] = 3, ["iv"] = 4, ["v"] = 5,
        ["vi"] = 6, ["vii"] = 7, ["viii"] = 8, ["ix"] = 9, ["x"] = 10,
    };

    private static readonly HashSet<string> FormulaWeightElements = new(StringComparer.Ordinal)
    {
        "Li", "Be", "B", "Na", "Mg", "Al", "Si", "K", "Ca", "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn",
        "Ga", "Ge", "As", "Rb", "Sr", "Y", "Zr", "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd", "In", "Sn", "Sb", "Te",
        "Cs", "Ba", "La", "Ce", "Pr", "Nd", "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb", "Lu", "Hf", "Ta",
        "W", "Re", "Os", "Ir", "Pt", "Au", "Hg", "Tl", "Pb", "Bi", "Po", "Fr", "Ra", "Ac", "Th", "Pa", "U", "Np", "Pu",
    };

    private static readonly string[] FormulaWeightNameMarkers = ["アンモニウム", "塩酸塩", "ジアゾニウム"];
}
