// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Ime;
using Meltype.Learning;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var detector = CompositionDetector.CreateDefault(); var languages = new LanguageMemory(null);
var services = new CompositionServices { Languages = languages, Candidates = CandidateDictionary.Load(null), ContextRules = ContextRules.Load(null), History = new ConversionHistory(null), Misspellings = MisspellingDictionary.Load(null), RomajiTypos = RomajiTypoCorrector.Load(detector.Romaji) };
MeltypeRuntime.Connect(detector, services);
// 4 番目の引数 app: アプリ別設定 (自動判定を全体と同じ値で上書きした種類) のアプリの入力欄で打つ
var app = args.Length > 3 && args[3] == "app";
var samples = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 15; var lengths = args.Length > 1 ? args[1].Split(',').Select(n => int.Parse(n, CultureInfo.InvariantCulture)).ToArray() : new[] { 256 };
var cases = new Dictionary<string, string> { ["long_url"] = "https://example.com/" + new string('a', 300), ["natural_mixed"] = "kyouhaiitenkidesunegoogledekensakushitegithubnipushshita", ["mixed_repeated"] = "googledekaigishitegithubnipushshitapythondekaita", ["japanese"] = "kyouhaiitenkidesunewatashihagakuseidesuyoroshikuonegaishimasu" };
foreach (var length in lengths) foreach (var pair in cases.Where(p => args.Length < 3 || p.Key == args[2]))
{
    var raw = string.Concat(Enumerable.Repeat(pair.Value, (length + pair.Value.Length - 1) / pair.Value.Length))[..length];
    for (var sample = -3; sample < samples; sample++)
    {
        var settings = new Settings().Normalize();
        if (app)
        {
            settings.AppKinds.Add(new AppKind { Name = "bench", AutoDetection = settings.AutoDetection });
            settings.AppRules.Add(new AppRule { Process = "bench.exe", Kind = "bench" });
        }
        var host = new ImeHost(new Environment(), new SessionParts(detector, new Converter(), services), () => settings); var deltas = new List<double>(); var output = new StringBuilder();
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        foreach (var c in raw) { var t = Stopwatch.GetTimestamp(); var result = host.Handle(Key(char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : 0, c, char.IsAsciiLetterUpper(c), app)); Bench.Sink += result.ToJson().Length; deltas.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds); foreach (var edit in result.Result.Commits) output.Append(edit.Text); }
        var end = host.Handle(Key(0x0D, 0, false, app)); Bench.Sink += end.ToJson().Length; foreach (var edit in end.Result.Commits) output.Append(edit.Text);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds; allocated = GC.GetAllocatedBytesForCurrentThread() - allocated; deltas.Sort();
        if (sample >= 0) Console.WriteLine(string.Join("\t", pair.Key, length, sample, elapsed, allocated, deltas[deltas.Count / 2], deltas[(int)Math.Floor((deltas.Count - 1) * 0.95)], deltas[^1], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(output.ToString())))));
    }
}
// JSON で作る (baseline の古い版でもビルドできるように。ImeEvent の型の形は版で違う)
static ImeEvent Key(int vk, int ch, bool shift, bool app) => ImeHost.Parse($$"""{"version":{{ImeHost.Version}},"type":"KeyPressed","key":{"vk":{{vk}},"ch":{{ch}},"shift":{{(shift ? "true" : "false")}} }{{(app ? ""","field":{"app":"bench.exe"}""" : "")}} }""")!;
static class Bench { public static int Sink; }
sealed class Environment : IImeEnvironment { public InputMode? InputMethod => null; public SnippetStore Snippets { get; } = new(null); public string? ClipboardText => null; public string? OtherTipVersion => null; }
sealed class Converter : IKanjiConverter { public IReadOnlyList<ConversionClause>? Convert(ConversionRequest request) => null; }
