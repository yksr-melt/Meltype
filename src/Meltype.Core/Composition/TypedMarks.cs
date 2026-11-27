// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>打った , . の役割。英語の区間と保護区間の中の , . は、役割によらず打ったままになる。</summary>
internal enum MarkRole
{
    /// <summary>小数点・桁区切り (1.5、1,000)。</summary>
    Number,

    /// <summary>3 つ以上続けて打った (...)。</summary>
    Ellipsis,

    /// <summary>英数字の名前の中の . (tetr.io)。</summary>
    Name,

    /// <summary>英語を囲む記号と一緒に半角にする (<see cref="CompositionText"/> の HalfWidthOpeners)。</summary>
    Enclosed,

    /// <summary>句読点。</summary>
    Punctuation,
}

/// <summary>
/// 打った , . の役割を、打ったもの全体から決める。単位 (<see cref="TextReading.Units"/>) は , . を打った文字のまま持ち、
/// 読みにするときに、ここで役割から文字を決める。かな入力の 、 。 のキーは、単位が読みの 、 。 を持つので役割を持たない。
/// </summary>
internal static class TypedMarks
{
    /// <summary>読みを決めた単位の並び。halfWidth の単位は、打った半角のままにする。</summary>
    public static IReadOnlyList<CompositionUnit> Read(TextReading state, TextEnv env, IReadOnlySet<int>? halfWidth = null)
    {
        var units = state.Units;
        if (halfWidth is null && !units.Any(IsMark)) return units;
        return [.. units.Select((unit, i) => IsMark(unit) ? unit with { Kana = Text(RoleOf(state, env, i, halfWidth), unit.Raw) }
            : halfWidth?.Contains(i) == true ? unit with { Kana = unit.Raw } : unit)];
    }

    private static bool IsMark(CompositionUnit unit) => unit.Raw is "," or "." && unit.Kana == unit.Raw;

    private static string Text(MarkRole role, string raw) => role switch
    {
        MarkRole.Number or MarkRole.Name or MarkRole.Enclosed => raw,
        MarkRole.Ellipsis => ".",
        // 読みでは設定によらず 、 。 (設定の句読点は文字列にするときに当てる)
        MarkRole.Punctuation => raw == "," ? "、" : "。",
        _ => throw new InvalidOperationException($"不明な MarkRole: {role}"),
    };

    private static MarkRole RoleOf(TextReading state, TextEnv env, int i, IReadOnlySet<int>? halfWidth)
    {
        var units = state.Units;
        if (halfWidth?.Contains(i) == true) return MarkRole.Enclosed;
        var (start, end) = (i, i + 1);
        while (start > 0 && IsMark(units[start - 1])) start--;
        while (end < units.Count && IsMark(units[end])) end++;
        if (end - start >= 3) return MarkRole.Ellipsis;
        var next = NextChar(state, i);
        if (i > 0 && units[i - 1].Raw is [var previous] && char.IsAsciiDigit(previous))
        {
            // 数字の後の , は、次も数字なら桁区切り (1,000)、ほかの文字が続くか末尾なら読点 (x64、arm64・100、)。英文の続きの末尾は半角のまま (In 2020,)
            var number = units[i].Raw == "." || (next is { } after ? char.IsAsciiDigit(after) : env.Context().EndsWithEnglishAndSpace);
            return number ? MarkRole.Number : MarkRole.Punctuation;
        }
        return units[i].Raw == "." && next is { } c && char.IsAsciiLetterOrDigit(c) && JoinsName(units, i, env) ? MarkRole.Name : MarkRole.Punctuation;
    }

    /// <summary>i 番目の単位のすぐ後ろに打った文字。かな入力のかなのキーは、打った文字 (Raw) が入力した文字でないので除く。</summary>
    private static char? NextChar(TextReading state, int i)
    {
        if (i + 1 == state.Units.Count) return state.Pending.Length > 0 ? state.Pending[0] : null;
        var unit = state.Units[i + 1];
        return unit.Raw.Length > 0 && (!state.KanaInput || unit.Kana == unit.Raw) ? unit.Raw[0] : null;
    }

    /// <summary>i 番目の記号 (. -) のすぐ前が、ドメイン名・略語の区切りとして記号を半角のまま続ける語か (tetr.io、J-core)。</summary>
    public static bool JoinsName(IReadOnlyList<CompositionUnit> units, int i, TextEnv env)
    {
        var start = i;
        while (start > 0 && units[start - 1].Raw is { Length: > 0 } raw && raw.All(char.IsAsciiLetterOrDigit)) start--;
        if (start == i) return false;
        var word = string.Concat(units.Skip(start).Take(i - start).Select(u => u.Raw));
        var lower = word.ToLowerInvariant();
        var acronym = word.Any(char.IsAsciiLetterUpper) && word.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c));
        var readable = env.Detector.Romaji.AnalyzeFragment(lower).IsValid;
        var knownUnambiguous = word.Length >= 3 && env.Detector.IsKnownEnglishWord(CompositionText.LearnedLanguages(env), word) && !readable;
        var properNoun = env.Detector.ProperNouns.Contains(lower) && (units[i].Raw != "-" || word.Any(char.IsAsciiLetterUpper) || !readable);
        return acronym || knownUnambiguous || properNoun;
    }
}
