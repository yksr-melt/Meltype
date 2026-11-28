// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

using System.Globalization;
using System.Text;

namespace Meltype.Dictionary;

/// <summary>化学式の元素数を数え、Meltype の原子量で式量・分子量を計算する。化学名の解析は行わない。</summary>
internal static class ChemicalFormula
{
    private const string Subscripts = "₀₁₂₃₄₅₆₇₈₉";

    public static string ToSubscript(string formula)
    {
        var result = new StringBuilder(formula.Length);
        foreach (var character in formula)
            result.Append(character is >= '0' and <= '9' ? Subscripts[character - '0'] : character);
        return result.ToString();
    }

    public static bool IsElement(string symbol) => PeriodicSymbols.Contains(symbol);

    public static bool TryCalculateMass(string formula, AtomicWeights weights, out decimal mass)
    {
        mass = 0;
        if (!TryParse(formula, out var counts)) return false;
        foreach (var (symbol, count) in counts)
        {
            if (weights.Get(symbol) is not { } weight) return false;
            try { mass = checked(mass + checked(weight * count)); }
            catch (OverflowException) { mass = 0; return false; }
        }
        return true;
    }

    public static bool TryParse(string formula, out IReadOnlyDictionary<string, int> counts)
    {
        counts = new Dictionary<string, int>();
        if (string.IsNullOrWhiteSpace(formula)) return false;
        var text = formula.Trim();
        // イオン式の全体電荷は末尾に付くことがある。電荷は質量に含めない。
        var charge = text.LastIndexOfAny(['+', '-']);
        if (charge >= 0) text = text[..charge];
        var total = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var part in text.Split(['·', '.'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parser = new FormulaParser(part);
            if (!parser.TryParse(out var partCounts)) return false;
            foreach (var (symbol, count) in partCounts)
            {
                try { total[symbol] = checked(total.GetValueOrDefault(symbol) + count); }
                catch (OverflowException) { return false; }
            }
        }
        if (total.Count == 0) return false;
        counts = total;
        return true;
    }

    private sealed class FormulaParser(string text)
    {
        private int _position;

        public bool TryParse(out Dictionary<string, int> counts)
        {
            counts = [];
            var partMultiplier = ReadNumber();
            if (partMultiplier == 0) return false;
            if (!ReadSequence(counts, terminator: '\0') || _position != text.Length) return false;
            if (partMultiplier != 1)
            {
                foreach (var symbol in counts.Keys.ToArray())
                {
                    try { counts[symbol] = checked(counts[symbol] * partMultiplier); }
                    catch (OverflowException) { return false; }
                }
            }
            return counts.Count > 0;
        }

        private bool ReadSequence(Dictionary<string, int> counts, char terminator)
        {
            while (_position < text.Length)
            {
                var current = text[_position];
                if (current == terminator && terminator != '\0') { _position++; return true; }
                if (current is '(' or '[')
                {
                    var close = current == '(' ? ')' : ']';
                    _position++;
                    var nested = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (!ReadSequence(nested, close) || nested.Count == 0) return false;
                    var multiplier = ReadNumber();
                    if (multiplier == 0) return false;
                    foreach (var (symbol, count) in nested)
                    {
                        try { counts[symbol] = checked(counts.GetValueOrDefault(symbol) + checked(count * multiplier)); }
                        catch (OverflowException) { return false; }
                    }
                    continue;
                }
                if (current is ')' or ']') return false;
                if (current is < 'A' or > 'Z') return false;
                var start = _position++;
                while (_position < text.Length && text[_position] is >= 'a' and <= 'z') _position++;
                var symbolName = text[start.._position];
                if (!PeriodicSymbols.Contains(symbolName)) return false;
                var atomCount = ReadNumber();
                if (atomCount == 0) return false;
                try { counts[symbolName] = checked(counts.GetValueOrDefault(symbolName) + atomCount); }
                catch (OverflowException) { return false; }
            }
            return terminator == '\0';
        }

        private int ReadNumber()
        {
            var start = _position;
            while (_position < text.Length && char.IsAsciiDigit(text[_position])) _position++;
            if (start == _position) return 1;
            return int.TryParse(text.AsSpan(start, _position - start), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : 0;
        }
    }

    private static readonly HashSet<string> PeriodicSymbols = new(StringComparer.Ordinal)
    {
        "H", "He", "Li", "Be", "B", "C", "N", "O", "F", "Ne", "Na", "Mg", "Al", "Si", "P", "S", "Cl", "Ar",
        "K", "Ca", "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn", "Ga", "Ge", "As", "Se", "Br", "Kr",
        "Rb", "Sr", "Y", "Zr", "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd", "In", "Sn", "Sb", "Te", "I", "Xe",
        "Cs", "Ba", "La", "Ce", "Pr", "Nd", "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb", "Lu", "Hf",
        "Ta", "W", "Re", "Os", "Ir", "Pt", "Au", "Hg", "Tl", "Pb", "Bi", "Po", "At", "Rn", "Fr", "Ra", "Ac", "Th",
        "Pa", "U", "Np", "Pu", "Am", "Cm", "Bk", "Cf", "Es", "Fm", "Md", "No", "Lr", "Rf", "Db", "Sg", "Bh", "Hs",
        "Mt", "Ds", "Rg", "Cn", "Nh", "Fl", "Mc", "Lv", "Ts", "Og",
    };
}
