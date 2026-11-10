// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var detector = CompositionDetector.CreateDefault(); detector.SpellChecker = BuiltInWordChecker.Shared; var languages = new LanguageMemory(null); detector.Memory = languages;
var services = new CompositionServices { Languages = languages, Candidates = CandidateDictionary.Load(null), ContextRules = ContextRules.Load(null), History = new ConversionHistory(null), Misspellings = MisspellingDictionary.Load(null), RomajiTypos = RomajiTypoCorrector.Load(detector.Romaji) };
var samples = args.Length > 0 ? int.Parse(args[0]) : 15; var lengths = args.Length > 1 ? args[1].Split(',').Select(int.Parse).ToArray() : new[] { 256 };
var cases = new Dictionary<string, string> { ["long_url"] = "https://example.com/" + new string('a', 300), ["natural_mixed"] = "kyouhaiitenkidesunegoogledekensakushitegithubnipushshita", ["mixed_repeated"] = "googledekaigishitegithubnipushshitapythondekaita", ["japanese"] = "kyouhaiitenkidesunewatashihagakuseidesuyoroshikuonegaishimasu" };
foreach (var length in lengths) foreach (var pair in cases.Where(p => args.Length < 3 || p.Key == args[2]))
{
    var raw = string.Concat(Enumerable.Repeat(pair.Value, (length + pair.Value.Length - 1) / pair.Value.Length))[..length];
    for (var sample = -3; sample < samples; sample++)
    {
        var settings = new Settings().Normalize(); var session = new MeltypeSession(detector, new Converter(), services, () => settings); var deltas = new List<double>(); var output = new StringBuilder();
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        foreach (var c in raw) { var t = Stopwatch.GetTimestamp(); var result = session.HandleKey(char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : 0x07, c, char.IsAsciiLetterUpper(c), false, false, false); Bench.Sink += result.ToJson().Length; deltas.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds); foreach (var edit in result.Commits) output.Append(edit.Text); }
        var end = session.HandleKey(0x0D, null, false, false, false, false); Bench.Sink += end.ToJson().Length; foreach (var edit in end.Commits) output.Append(edit.Text);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds; allocated = GC.GetAllocatedBytesForCurrentThread() - allocated; deltas.Sort();
        if (sample >= 0) Console.WriteLine(string.Join("\t", pair.Key, length, sample, elapsed, allocated, deltas[deltas.Count / 2], deltas[(int)Math.Floor((deltas.Count - 1) * 0.95)], deltas[^1], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(output.ToString())))));
    }
}
static class Bench { public static int Sink; }
sealed class Converter : IKanjiConverter { public string? Convert(string reading) => null; public IReadOnlyList<ConversionClause>? ConvertClauses(string reading, string? context = null) => null; }
