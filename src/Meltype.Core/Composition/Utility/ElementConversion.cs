// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Libraries.Lexicon;

namespace Meltype.Composition.Utility;

/// <summary>元素名の読み全体を変換すると、元素記号と略記標準原子量を候補に足す。</summary>
internal sealed class ElementConversion : IUtilityConversion
{
    private static readonly Lazy<Dictionary<string, string[]>> Elements = new(() =>
        DictionarySource.DataLines(DictionarySource.ReadEmbedded("chemical-elements.txt"))
            .Select(line => line.Split('\t'))
            .ToDictionary(fields => fields[0], fields => Candidates(fields[1], fields[2]), StringComparer.Ordinal));

    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading is { } reading && Elements.Value.TryGetValue(reading, out var candidates)
            ? new(candidates, UtilityPlacement.Append) { Learnable = true } : null;

    private static string[] Candidates(string symbol, string weight) =>
        weight == "-" ? [symbol] : [symbol, symbol + " (AW=" + weight + ")"];
}
