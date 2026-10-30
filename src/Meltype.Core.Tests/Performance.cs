// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// 変換エンジンの性能の計測 (#279)。Meltype キーボードで決まった例文を打ち、1 キーごとの遅延・Space で変換する遅延・
/// 段階ごとの時間・候補の数・控えの使い回し・メモリ・正解率を JSON に書く。機能を足す前と後で 2 回動かし、--perf-compare で比べる。
///   dotnet run -c Release --project src/Meltype.Core.Tests -- --perf 出力ファイル [例文のファイル] [回数]
///   dotnet run -c Release --project src/Meltype.Core.Tests -- --perf-compare 前.json 後.json
/// 環境変数 MELTYPE_MOZC に Mozc の変換ヘルパーの場所があれば、アプリと同じく Mozc で変換する (無ければ判定だけ)。
/// アプリ本体には計測を組み込まない (ふだんの入力では何も測らず、何も残さない)。例文はファイルのものだけを使い、結果にもそれだけが入る。
/// </summary>
internal static class Performance
{
    private const string DefaultCases = "tools/mozc-compare-cases.tsv";
    public const int DefaultSamples = 5;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record Case(string Typed, string Expected);

    /// <summary>1 回打った結果。時間はミリ秒。</summary>
    private sealed record Run(List<double> Keys, double Conversion, double Total, double Converter, int ConverterCalls,
        double Candidates, long Allocated, int CandidateCount, string Output);

    public static void Measure(string output, string? casesFile, int samples)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var cases = Load(casesFile ?? DefaultCases);
        if (cases.Count == 0) throw new InvalidOperationException("例文がありません");

        // 判定の正解率 (品質テスト)。品質テストはスペルチェッカーを外して終わるので、先に動かす
        var quality = Quality.Run();
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;

        var mozc = Henkan.Engine;
        IKanjiConverter? inner = mozc ?? Henkan.FallbackConverter;
        var converter = inner is null ? null : new TimedConverter(inner);
        var more = mozc is null ? null : new TimedCandidates(mozc.Candidates);
        CompositionTests.Keyboard Keyboard() => new(live: true, converter: converter, moreCandidates: more is null ? null : more.Lookup,
            userDictionary: new UserDictionary(null), translations: Henkan.TranslationDictionary);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var heapStart = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetStart = Environment.WorkingSet;

        Run Type(Case c)
        {
            converter?.Reset();
            more?.Reset();
            var k = Keyboard();
            var keys = new List<double>(c.Typed.Length);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            foreach (var ch in c.Typed)
            {
                var t = Stopwatch.GetTimestamp();
                k.Type(ch.ToString());
                keys.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            }
            var space = Stopwatch.GetTimestamp();
            k.Type(" ");
            var conversion = Stopwatch.GetElapsedTime(space).TotalMilliseconds;
            var total = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var view = k.Host.View;
            var text = view is { Converting: true } ? string.Concat(view.Clauses ?? [view.Text]) : k.Host.Document.TrimEnd();
            return new Run(keys, conversion, total, converter?.Milliseconds ?? 0, converter?.Calls ?? 0, more?.Milliseconds ?? 0,
                allocated, view is { Converting: true } ? view.Candidates.Count : 0, text);
        }

        // 1 回目は JIT と変換ヘルパーの起動を済ませるためだけに打つ (数えない)
        foreach (var c in cases)
        {
            mozc?.ClearCache();
            Type(c);
        }

        // 控えを捨ててから打つ (cold) と、同じ例文をもう一度打つ (warm) を交互に。warm は控えの使い回しの効果を見る
        var cold = new List<(Case Case, Run Run)>();
        var warm = new List<(Case Case, Run Run)>();
        var before = mozc?.Statistics();
        for (var sample = 0; sample < samples; sample++)
        {
            foreach (var c in cases)
            {
                mozc?.ClearCache();
                cold.Add((c, Type(c)));
                warm.Add((c, Type(c)));
            }
            Console.Error.WriteLine($"計測 {sample + 1}/{samples}");
        }
        var after = mozc?.Statistics();
        var peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64;
        var heapEnd = GC.GetTotalMemory(forceFullCollection: true);

        // 正解率は 1 回目の cold の結果で数える (何回打っても同じ結果になるはず)
        var first = cold.Take(cases.Count).ToList();
        var results = first.Select(r => new
        {
            typed = r.Case.Typed,
            expected = r.Case.Expected,
            output = r.Run.Output,
            ok = Normalize(r.Run.Output) == Normalize(r.Case.Expected),
            brokenEnglish = BrokenEnglish(r.Case.Expected, r.Run.Output),
            candidates = r.Run.CandidateCount,
            keyP95Ms = Summary(cold.Where(x => x.Case == r.Case).SelectMany(x => x.Run.Keys)).P95,
            conversionColdMs = Summary(cold.Where(x => x.Case == r.Case).Select(x => x.Run.Conversion)).P50,
            conversionWarmMs = Summary(warm.Where(x => x.Case == r.Case).Select(x => x.Run.Conversion)).P50,
        }).ToList();

        var report = new
        {
            format = 1,
            meta = new
            {
                commit = Git("rev-parse", "HEAD"),
                branch = Git("rev-parse", "--abbrev-ref", "HEAD"),
                engine = Henkan.EngineName,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                dotnet = Environment.Version.ToString(),
#if DEBUG
                configuration = "Debug",
#else
                configuration = "Release",
#endif
                processors = Environment.ProcessorCount,
                cases = Path.GetFileName(casesFile ?? DefaultCases),
                caseCount = cases.Count,
                samples,
                date = DateTime.UtcNow.ToString("O"),
            },
            latency = new
            {
                keyCold = Summary(cold.SelectMany(r => r.Run.Keys)),
                keyWarm = Summary(warm.SelectMany(r => r.Run.Keys)),
                conversionCold = Summary(cold.Select(r => r.Run.Conversion)),
                conversionWarm = Summary(warm.Select(r => r.Run.Conversion)),
            },
            stages = new { cold = Stages(cold.Select(r => r.Run)), warm = Stages(warm.Select(r => r.Run)) },
            cache = Cache(before, after),
            candidates = Summary(cold.Select(r => (double)r.Run.CandidateCount)),
            memory = new
            {
                managedHeapStartBytes = heapStart,
                managedHeapEndBytes = heapEnd,
                workingSetStartBytes = workingSetStart,
                peakWorkingSetBytes = peakWorkingSet,
                helperWorkingSetBytes = after?.HelperWorkingSet ?? 0,
                allocatedBytesPerKey = Summary(cold.Concat(warm).Select(r => (double)r.Run.Allocated / (r.Case.Typed.Length + 1))),
            },
            accuracy = new
            {
                conversion = Rate(results.Count(r => r.ok), results.Count),
                brokenEnglish = Rate(results.Count(r => r.brokenEnglish), results.Count(r => EnglishWords(r.expected).Count > 0)),
                quality = Rate(quality.Pass, quality.Total),
                qualityByCategory = quality.ByCategory.ToDictionary(p => p.Key, p => Rate(p.Value.Pass, p.Value.Total)),
            },
            results,
            qualityFailures = quality.Cases.Where(c => !c.Ok).Select(c => c.Key).ToList(),
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json));

        var summary = report;
        Console.WriteLine($"変換エンジン: {summary.meta.engine} ({summary.meta.configuration})、例文 {cases.Count} × {samples} 回");
        Console.WriteLine($"1 キー (cold)   p50 {summary.latency.keyCold.P50:F3} / p95 {summary.latency.keyCold.P95:F3} / p99 {summary.latency.keyCold.P99:F3} ms");
        Console.WriteLine($"変換 (cold)     p50 {summary.latency.conversionCold.P50:F3} / p95 {summary.latency.conversionCold.P95:F3} / p99 {summary.latency.conversionCold.P99:F3} ms");
        Console.WriteLine($"変換 (warm)     p50 {summary.latency.conversionWarm.P50:F3} / p95 {summary.latency.conversionWarm.P95:F3} / p99 {summary.latency.conversionWarm.P99:F3} ms");
        Console.WriteLine($"変換の正解 {summary.accuracy.conversion.Pass}/{summary.accuracy.conversion.Total}、英単語を崩した {summary.accuracy.brokenEnglish.Pass}/{summary.accuracy.brokenEnglish.Total}、品質テスト {summary.accuracy.quality.Rate:P1}");
        Console.WriteLine($"→ {output}");
    }

    /// <summary>2 つの結果を比べて表にする (前 → 後、倍率)。正解・不正解が変わった例文も出す。</summary>
    public static void Compare(string baseFile, string currentFile)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var a = JsonNode.Parse(File.ReadAllText(baseFile))!;
        var b = JsonNode.Parse(File.ReadAllText(currentFile))!;
        Console.WriteLine($"前: {a["meta"]?["commit"]} ({a["meta"]?["engine"]})");
        Console.WriteLine($"後: {b["meta"]?["commit"]} ({b["meta"]?["engine"]})");
        if (a["meta"]?["engine"]?.ToString() != b["meta"]?["engine"]?.ToString()) Console.WriteLine("注意: 変換エンジンが違うので、時間は比べられません");
        Console.WriteLine();
        Console.WriteLine("| 項目 | 前 | 後 | 倍率 |");
        Console.WriteLine("|---|--:|--:|--:|");
        foreach (var (name, path) in Rows)
        {
            var x = Number(a, path);
            var y = Number(b, path);
            if (x is null && y is null) continue;
            var ratio = x is > 0 && y is not null ? $"{y / x:F2}x" : "";
            Console.WriteLine($"| {name} | {Format(x)} | {Format(y)} | {ratio} |");
        }

        var baseResults = (a["results"] as JsonArray ?? []).ToDictionary(n => n!["typed"]!.ToString(), n => n!["ok"]!.GetValue<bool>());
        var changed = (b["results"] as JsonArray ?? []).Where(n => baseResults.TryGetValue(n!["typed"]!.ToString(), out var ok) && ok != n!["ok"]!.GetValue<bool>()).ToList();
        if (changed.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("正解・不正解が変わった例文:");
            foreach (var n in changed) Console.WriteLine($"  {(n!["ok"]!.GetValue<bool>() ? "直った" : "外れた")} {n["typed"]} → {n["output"]} (期待: {n["expected"]})");
        }
        var baseFailures = (a["qualityFailures"] as JsonArray ?? []).Select(n => n!.ToString()).ToHashSet();
        var newFailures = (b["qualityFailures"] as JsonArray ?? []).Select(n => n!.ToString()).Where(k => !baseFailures.Contains(k)).ToList();
        if (newFailures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("品質テストで新しく外れた例:");
            foreach (var key in newFailures) Console.WriteLine("  " + key);
        }
    }

    private static readonly (string Name, string Path)[] Rows =
    [
        ("1 キー cold p50 (ms)", "latency.keyCold.P50"),
        ("1 キー cold p95 (ms)", "latency.keyCold.P95"),
        ("1 キー cold p99 (ms)", "latency.keyCold.P99"),
        ("1 キー warm p95 (ms)", "latency.keyWarm.P95"),
        ("変換 cold p50 (ms)", "latency.conversionCold.P50"),
        ("変換 cold p95 (ms)", "latency.conversionCold.P95"),
        ("変換 cold p99 (ms)", "latency.conversionCold.P99"),
        ("変換 warm p95 (ms)", "latency.conversionWarm.P95"),
        ("1 文 cold: 判定・組み立て (ms)", "stages.cold.OtherMs"),
        ("1 文 cold: 変換エンジン (ms)", "stages.cold.ConverterMs"),
        ("1 文 cold: 候補の一覧 (ms)", "stages.cold.CandidatesMs"),
        ("1 文 cold: 変換エンジンの呼び出し", "stages.cold.ConverterCalls"),
        ("変換の控えの使い回し率", "cache.ConversionHitRate"),
        ("候補の控えの使い回し率", "cache.CandidateHitRate"),
        ("ヘルパー 1 回の時間 (ms)", "cache.HelperMsPerCall"),
        ("候補の数 (平均)", "candidates.Mean"),
        ("常駐のヒープ (MB)", "memory.managedHeapEndBytes"),
        ("ワーキングセットの最大 (MB)", "memory.peakWorkingSetBytes"),
        ("変換ヘルパーのメモリ (MB)", "memory.helperWorkingSetBytes"),
        ("1 キーの一時メモリ p50 (KB)", "memory.allocatedBytesPerKey.P50"),
        ("変換の正解率", "accuracy.conversion.Rate"),
        ("英単語を崩した率", "accuracy.brokenEnglish.Rate"),
        ("品質テストの正解率", "accuracy.quality.Rate"),
    ];

    private static double? Number(JsonNode root, string path)
    {
        JsonNode? node = root;
        foreach (var part in path.Split('.')) node = node?[part];
        if (node is not JsonValue value || !value.TryGetValue<double>(out var number)) return null;
        // バイトは読みやすい単位にする
        if (path.EndsWith("Bytes", StringComparison.Ordinal)) return number / (1024 * 1024);
        if (path.Contains("allocatedBytes", StringComparison.Ordinal)) return number / 1024;
        return number;
    }

    private static string Format(double? value) => value is { } v ? v.ToString(v != 0 && Math.Abs(v) < 1 ? "F4" : "F2", CultureInfo.InvariantCulture) : "-";

    public sealed record Distribution(int Count, double Mean, double P50, double P95, double P99, double Max);

    private static Distribution Summary(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return new(0, 0, 0, 0, 0, 0);
        // nearest-rank 法 (p95 は小さい方から 95% の位置の値)
        double At(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new(sorted.Length, sorted.Average(), At(0.50), At(0.95), At(0.99), sorted[^1]);
    }

    public sealed record StageTimes(double TotalMs, double ConverterMs, double CandidatesMs, double OtherMs, double ConverterCalls);

    /// <summary>1 文 (例文を打って Space で変換するまで) あたりの、段階ごとの時間の平均。「判定・組み立て」は全体から変換エンジンと候補の一覧を引いた残り。</summary>
    private static StageTimes Stages(IEnumerable<Run> runs)
    {
        var list = runs.ToList();
        if (list.Count == 0) return new(0, 0, 0, 0, 0);
        var total = list.Average(r => r.Total);
        var converter = list.Average(r => r.Converter);
        var candidates = list.Average(r => r.Candidates);
        return new(total, converter, candidates, total - converter - candidates, list.Average(r => r.ConverterCalls));
    }

    public sealed record CacheRates(long ConversionRequests, long ConversionHits, double ConversionHitRate,
        long CandidateRequests, long CandidateHits, double CandidateHitRate, long HelperCalls, double HelperMs, double HelperMsPerCall);

    private static CacheRates? Cache(MozcStatistics? before, MozcStatistics? after)
    {
        if (before is not { } a || after is not { } b) return null;
        var requests = b.Requests - a.Requests;
        var hits = b.CacheHits - a.CacheHits;
        var candidateRequests = b.CandidateRequests - a.CandidateRequests;
        var candidateHits = b.CandidateCacheHits - a.CandidateCacheHits;
        var calls = b.HelperCalls - a.HelperCalls;
        var helper = (b.HelperTime - a.HelperTime).TotalMilliseconds;
        return new(requests, hits, requests == 0 ? 0 : (double)hits / requests, candidateRequests, candidateHits,
            candidateRequests == 0 ? 0 : (double)candidateHits / candidateRequests, calls, helper, calls == 0 ? 0 : helper / calls);
    }

    public sealed record Ratio(int Pass, int Total, double Rate);

    private static Ratio Rate(int pass, int total) => new(pass, total, total == 0 ? 0 : (double)pass / total);

    private static List<Case> Load(string file) =>
        File.ReadAllLines(file)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length == 2)
            .Select(parts => new Case(parts[0].Trim(), parts[1].Trim()))
            .ToList();

    private static string Normalize(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());

    private static List<string> EnglishWords(string text) =>
        Regex.Matches(text, "[A-Za-z][A-Za-z0-9._'+#-]*").Select(m => m.Value.ToLowerInvariant()).ToList();

    /// <summary>誤った修正: 期待にある英単語が、結果では英字のまま残っていない (かな・カタカナにした、つづりを変えた)。</summary>
    private static bool BrokenEnglish(string expected, string output)
    {
        var words = EnglishWords(output);
        return EnglishWords(expected).Any(w => !words.Contains(w));
    }

    private static string? Git(params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process is null) return null;
            var text = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? text : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>変換エンジンを呼んだ回数と時間を数える (覚えさせない: ILearningConverter を持たない)。</summary>
    private sealed class TimedConverter(IKanjiConverter inner) : IKanjiConverter
    {
        private long _ticks;
        public int Calls { get; private set; }
        public double Milliseconds => Stopwatch.GetElapsedTime(0, _ticks).TotalMilliseconds;

        public void Reset()
        {
            _ticks = 0;
            Calls = 0;
        }

        public string? Convert(string hiragana) => Time(() => inner.Convert(hiragana));

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => Time(() => inner.ConvertClauses(hiragana, context));

        private T Time<T>(Func<T> call)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                return call();
            }
            finally
            {
                _ticks += Stopwatch.GetTimestamp() - started;
                Calls++;
            }
        }
    }

    /// <summary>候補の一覧 (MoreCandidates) を引いた時間を数える。</summary>
    private sealed class TimedCandidates(Func<string, IReadOnlyList<string>> inner)
    {
        private long _ticks;
        public double Milliseconds => Stopwatch.GetElapsedTime(0, _ticks).TotalMilliseconds;

        public void Reset() => _ticks = 0;

        public IReadOnlyList<string> Lookup(string reading)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                return inner(reading);
            }
            finally
            {
                _ticks += Stopwatch.GetTimestamp() - started;
            }
        }
    }
}
