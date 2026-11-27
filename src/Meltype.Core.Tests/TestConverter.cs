// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Engine;

namespace Meltype.Tests;

/// <summary>
/// 偽の変換エンジンの土台。文節に区切った答え (ConvertClauses) が無ければ、読み全体の答え (Convert) を 1 つの文節にして返す。
/// 偽物は、答えやすい方を書く。
/// </summary>
internal abstract class TestConverter : IKanjiConverter
{
    public virtual string? Convert(string hiragana, string? before) => null;

    public virtual IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) => null;


    public IReadOnlyList<ConversionClause>? Convert(ConversionRequest request) =>
        ConvertClauses(request.Hiragana, request.Before) ?? (Convert(request.Hiragana, request.Before) is { } text ? [new ConversionClause(request.Hiragana, text)] : null);
}

/// <summary>テストから変換エンジンを呼ぶ。deadline (Environment.TickCount64) を渡さなければ、答えるまで待つ。</summary>
internal static class ConverterCalls
{
    public static IReadOnlyList<ConversionClause>? ConvertClauses(this IKanjiConverter converter, string hiragana, string? before, long deadline = long.MaxValue) =>
        converter.Convert(new ConversionRequest(hiragana, before) { Deadline = deadline });

    /// <summary>文節をつなげた文字列。変換できなければ null。</summary>
    public static string? ConvertText(this IKanjiConverter converter, string hiragana, string? before, long deadline = long.MaxValue) =>
        converter.ConvertClauses(hiragana, before, deadline) is { Count: > 0 } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;
}
