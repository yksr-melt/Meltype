// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

using System.Globalization;

namespace Meltype.Dictionary;

/// <summary>物質名を端末内の語彙・命名規則で解析し、化学式と質量を変換候補にする。</summary>
internal sealed class ChemicalCompoundDictionary
{
    private readonly AtomicWeights _atomicWeights;

    public ChemicalCompoundDictionary(string dataDirectory) => _atomicWeights = new AtomicWeights(dataDirectory);

    public IReadOnlyList<string> Lookup(string reading, IEnumerable<string> names)
    {
        var compounds = new Dictionary<string, ChemicalCompound>(StringComparer.Ordinal);
        foreach (var term in new[] { reading }.Concat(names).Distinct(StringComparer.Ordinal))
            foreach (var compound in ChemicalNameParser.Parse(term)) compounds.TryAdd(compound.Identity, compound);

        var candidates = new List<string>();
        foreach (var compound in compounds.Values)
        {
            var shownFormula = ChemicalFormula.ToSubscript(compound.PreferredFormula ?? compound.Formula);
            candidates.Add(shownFormula);
            if (compound.PreferredFormula is not null && compound.PreferredFormula != compound.Formula)
                candidates.Add(ChemicalFormula.ToSubscript(compound.Formula));
            if (ChemicalFormula.TryCalculateMass(compound.Formula, _atomicWeights, out var mass))
            {
                var kind = compound.IsMolecule ? "MW" : "FW";
                candidates.Add($"{shownFormula} ({kind}={mass.ToString("0.######", CultureInfo.InvariantCulture)})");
            }
        }
        return candidates.Distinct(StringComparer.Ordinal).ToArray();
    }

    public void RefreshAtomicWeights() => _atomicWeights.Refresh();
}

internal sealed record ChemicalCompound(string Identity, string Formula, string? PreferredFormula, bool IsMolecule);
