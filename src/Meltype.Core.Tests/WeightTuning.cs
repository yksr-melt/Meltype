// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// --tune-weights: 日英の区切りの辺をつなぐ重み (<see cref="LinkWeights"/>) を 1 つずつ動かして、品質テストの打鍵の例を流す。
/// 重みは書き換えない。出た結果を見て、人が重みを変えるか決める。
/// 誤判定を直すときは、報告の例を品質テスト (QualityTests の Typing) に足してから流し、重みで直せるかを先に確かめる。
/// 流すのは Typing だけで、単体テストは流さない。Typing は探索に使うので、Typing が全部通っても過適合していないとは言えない。
/// 重みを変えたら、単体テストと --expect (issue-cases.tsv) でも確かめる。
/// </summary>
internal static class WeightTuning
{
    // 試す値の刻みと上限。重みはどれも 0.5 刻みの小さな値なので、0.25 刻みなら今の値のあいだも見られる
    private const double Step = 0.25;
    private const double Max = 6;

    public static void Run()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var current = LinkWeights.Default;
        var baseline = Failures(current);
        Console.WriteLine($"今の重み: {current}");
        Console.WriteLine($"外れている例: {baseline.Count}");
        foreach (var failure in baseline.Order()) Console.WriteLine($"  {failure}");

        // 重みごとに、全部の例 (今外れている例を除く) が今と同じ結果になる範囲 (今の値の余裕)
        Console.WriteLine();
        Console.WriteLine("## 重みごとの余裕 (今通っている Typing の例が崩れない範囲。単体テストは含まない。直る例があれば + で示す)");
        var trials = new Dictionary<(LinkFeature, double), HashSet<string>>();
        foreach (var feature in Enum.GetValues<LinkFeature>())
        {
            var safe = new List<double>();
            var notes = new List<string>();
            foreach (var value in Values())
            {
                var failures = value == current[feature] ? baseline : Failures(current.With(feature, value));
                trials[(feature, value)] = failures;
                var broken = failures.Except(baseline).Count();
                var fixedCount = baseline.Except(failures).Count();
                if (broken == 0) safe.Add(value);
                if (fixedCount > 0) notes.Add($"{value}: +{fixedCount} -{broken}");
            }
            Console.WriteLine($"- {feature} (今 {current[feature]}): {Ranges(safe)}{(notes.Count > 0 ? "　" + string.Join("、", notes) : "")}");
        }
        if (baseline.Count == 0) return;

        // 外れが減るあいだ、1 つの重みを動かすことを繰り返す (座標ごとの探索)。外れの数が同じなら今の値に近いほうを選ぶ
        Console.WriteLine();
        Console.WriteLine("## 外れが減る重みの組");
        var best = current;
        var bestFailures = baseline;
        while (true)
        {
            var candidates = Enum.GetValues<LinkFeature>()
                .SelectMany(feature => Values().Where(v => v != best[feature]).Select(value => (Feature: feature, Value: value)))
                .Select(c => (c.Feature, c.Value, Failures: best == current && trials.TryGetValue((c.Feature, c.Value), out var known) ? known : Failures(best.With(c.Feature, c.Value))))
                .OrderBy(c => c.Failures.Count).ThenBy(c => Math.Abs(c.Value - current[c.Feature]))
                .First();
            if (candidates.Failures.Count >= bestFailures.Count) break;
            best = best.With(candidates.Feature, candidates.Value);
            bestFailures = candidates.Failures;
            Console.WriteLine($"- {candidates.Feature} を {candidates.Value} に: 外れ {bestFailures.Count}");
        }
        if (best == current)
        {
            Console.WriteLine("重みを 1 つずつ動かしても外れは減らない。特徴か辞書で直す。");
            return;
        }
        Console.WriteLine($"重み: {best}");
        foreach (var failure in baseline.Except(bestFailures).Order()) Console.WriteLine($"  直る: {failure}");
        foreach (var failure in bestFailures.Except(baseline).Order()) Console.WriteLine($"  崩れる: {failure}");
    }

    private static IEnumerable<double> Values()
    {
        for (var value = 0.0; value <= Max; value += Step) yield return value;
    }

    // 外れた例の Key (「[分類] 打ったもの」)。結果の文字列は重みで変わるので、比べるのは打ったものだけにする
    private static HashSet<string> Failures(LinkWeights weights) =>
        [.. Quality.TypingResults(CompositionDetector.CreateDefault(null, weights)).Where(r => !r.Case.Ok).Select(r => r.Case.Key)];

    /// <summary>0.5、1、1.5、2 → 0.5〜2 のように、続いた値をまとめて書く。</summary>
    private static string Ranges(List<double> values)
    {
        if (values.Count == 0) return "(無し)";
        var ranges = new List<string>();
        var start = values[0];
        for (var i = 1; i <= values.Count; i++)
        {
            if (i < values.Count && values[i] - values[i - 1] <= Step + 1e-9) continue;
            ranges.Add(start == values[i - 1] ? $"{start}" : $"{start}〜{values[i - 1]}");
            if (i < values.Count) start = values[i];
        }
        return string.Join("、", ranges);
    }
}
