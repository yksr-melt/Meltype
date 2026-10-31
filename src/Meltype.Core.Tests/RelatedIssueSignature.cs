// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using System.Text.RegularExpressions;
using Meltype.Composition;
using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// 関連 Issue 検知の試作 (issue #1382) 用。誤判定の報告の打ったものを判定エンジンに通し、判定理由を JSON Lines で出す。
///   --related-signature 入力.tsv 出力.jsonl   (入力は「番号 TAB 打ったもの」)
/// 1 行に 1 件: 語ごとの ScoreEngine の Contributions (英数状態の検知) と、変換ボックスの区間分けのラティスの道。
/// </summary>
internal static partial class RelatedIssueSignature
{
    public static void Run(string input, string output)
    {
        Diagnostics.Log.RecordText = true;
        var engine = TestSupport.CreateEngine();
        using var writer = new StreamWriter(output, false, new System.Text.UTF8Encoding(false));
        foreach (var line in File.ReadLines(input))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0) continue;
            var id = line[..tab];
            var typed = line[(tab + 1)..];

            var words = new List<object>();
            foreach (Match word in Letters().Matches(typed))
            {
                var result = TestSupport.Classify(engine, word.Value);
                words.Add(new
                {
                    word = word.Value,
                    verdict = result.Verdict.ToString(),
                    jp = result.JapaneseScore,
                    en = result.EnglishScore,
                    contributions = result.Contributions.Select(c => new { source = c.Source, jp = c.Japanese, en = c.English, reason = c.Reason }),
                });
            }

            var trace = new List<string>();
            var text = new CompositionText(CompositionTests.Detector);
            foreach (var c in typed) text.Append(c);
            CompositionTests.Detector.LatticeTrace = trace.Add;
            var segments = text.Segments();
            CompositionTests.Detector.LatticeTrace = null;

            writer.WriteLine(JsonSerializer.Serialize(new
            {
                id,
                typed,
                words,
                segments = segments.Select(s => (s.IsEnglish ? "EN:" : "JA:") + s.Raw),
                lattice = trace,
            }));
        }
    }

    [GeneratedRegex("[A-Za-z]+")]
    private static partial Regex Letters();
}
