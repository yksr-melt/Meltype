// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using System.Text.Json;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// japanese-henkan-test (jht): 出てほしい文 (私はgoogleが好きです) を、考えられるローマ字の打ち方
/// (watashihagooglegasukidesu・watasihagooglegasukidesu …) ですべて打ってみて、その文になるかを確かめる。
/// 打ち方ごとに、日本語 / 英語の分かれ方・ライブ変換で Enter・Space の最初の変換・文節ごとの候補の何番目に出るかを返す。
/// </summary>
internal static class Jht
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private const int MaxPatterns = 24;

    private static readonly Dictionary<string, string[]> Romaji = Build();

    private static Dictionary<string, string[]> Build()
    {
        var table = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string kana, params string[] romaji) => table[kana] = romaji;
        foreach (var (row, consonant) in new[] { ("あいうえお", ""), ("かきくけこ", "k"), ("なにぬねの", "n"), ("まみむめも", "m"), ("らりるれろ", "r"), ("がぎぐげご", "g"), ("ばびぶべぼ", "b"), ("ぱぴぷぺぽ", "p") })
        {
            for (var i = 0; i < 5; i++) Add(row[i].ToString(), consonant + "aiueo"[i]);
        }
        Add("さ", "sa"); Add("し", "shi", "si"); Add("す", "su"); Add("せ", "se"); Add("そ", "so");
        Add("た", "ta"); Add("ち", "chi", "ti"); Add("つ", "tsu", "tu"); Add("て", "te"); Add("と", "to");
        Add("は", "ha"); Add("ひ", "hi"); Add("ふ", "fu", "hu"); Add("へ", "he"); Add("ほ", "ho");
        Add("や", "ya"); Add("ゆ", "yu"); Add("よ", "yo"); Add("わ", "wa"); Add("を", "wo"); Add("ゔ", "vu");
        Add("ざ", "za"); Add("じ", "ji", "zi"); Add("ず", "zu"); Add("ぜ", "ze"); Add("ぞ", "zo");
        Add("だ", "da"); Add("ぢ", "di"); Add("づ", "du"); Add("で", "de"); Add("ど", "do");
        foreach (var (kana, consonant) in new[] { ("き", "ky"), ("に", "ny"), ("ひ", "hy"), ("み", "my"), ("り", "ry"), ("ぎ", "gy"), ("び", "by"), ("ぴ", "py"), ("ぢ", "dy") })
        {
            Add(kana + "ゃ", consonant + "a"); Add(kana + "ゅ", consonant + "u"); Add(kana + "ょ", consonant + "o");
        }
        Add("しゃ", "sha", "sya"); Add("しゅ", "shu", "syu"); Add("しょ", "sho", "syo"); Add("しぇ", "she", "sye");
        Add("ちゃ", "cha", "tya", "cya"); Add("ちゅ", "chu", "tyu", "cyu"); Add("ちょ", "cho", "tyo", "cyo"); Add("ちぇ", "che", "tye");
        Add("じゃ", "ja", "zya", "jya"); Add("じゅ", "ju", "zyu", "jyu"); Add("じょ", "jo", "zyo", "jyo"); Add("じぇ", "je", "zye");
        Add("ふぁ", "fa"); Add("ふぃ", "fi"); Add("ふぇ", "fe"); Add("ふぉ", "fo"); Add("てぃ", "thi"); Add("でぃ", "dhi");
        Add("うぃ", "wi"); Add("うぇ", "we"); Add("ゔぁ", "va"); Add("ゔぃ", "vi"); Add("ゔぇ", "ve"); Add("ゔぉ", "vo");
        Add("ぁ", "xa", "la"); Add("ぃ", "xi", "li"); Add("ぅ", "xu", "lu"); Add("ぇ", "xe", "le"); Add("ぉ", "xo", "lo");
        Add("ゃ", "xya", "lya"); Add("ゅ", "xyu", "lyu"); Add("ょ", "xyo", "lyo");
        Add("ー", "-");
        Add("、", ","); Add("。", "."); Add("！", "!"); Add("？", "?"); Add("「", "["); Add("」", "]"); Add("・", "/"); Add("～", "~"); Add("　", " ");
        return table;
    }

    private sealed record Sound(string Kana, string[] Options);

    private static string ToHiragana(string text) => new(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());

    private static List<Sound>? Sounds(string kana)
    {
        var sounds = new List<Sound>();
        for (var i = 0; i < kana.Length;)
        {
            if (kana[i] is 'っ' or 'ん') { sounds.Add(new Sound(kana[i].ToString(), [])); i++; continue; }
            if (i + 1 < kana.Length && Romaji.TryGetValue(kana.Substring(i, 2), out var two)) { sounds.Add(new Sound(kana.Substring(i, 2), two)); i += 2; continue; }
            if (Romaji.TryGetValue(kana[i].ToString(), out var one)) { sounds.Add(new Sound(kana[i].ToString(), one)); i++; continue; }
            return null;
        }
        return sounds;
    }

    private static string Spell(List<Sound> sounds, int[] choice, bool sokuonX, bool doubleN)
    {
        var parts = new string[sounds.Count];
        for (var i = sounds.Count - 1; i >= 0; i--)
        {
            var next = i + 1 < sounds.Count ? parts[i + 1] : "";
            parts[i] = sounds[i].Kana switch
            {
                "っ" => sokuonX || next.Length == 0 || !char.IsAsciiLetterLower(next[0]) || "aiueon".Contains(next[0]) ? "xtu" : next.StartsWith("ch", StringComparison.Ordinal) ? "t" : next[0].ToString(),
                "ん" => doubleN || (next.Length > 0 && "aiueoyn".Contains(next[0])) ? "nn" : "n",
                _ => sounds[i].Options[Math.Min(choice[i], sounds[i].Options.Length - 1)],
            };
        }
        return string.Concat(parts);
    }

    private static List<(bool Ascii, string Text)> Runs(string text)
    {
        var runs = new List<(bool, string)>();
        foreach (var c in text)
        {
            var ascii = c is >= ' ' and <= '~';
            if (runs.Count > 0 && runs[^1].Item1 == ascii) runs[^1] = (ascii, runs[^1].Item2 + c);
            else runs.Add((ascii, c.ToString()));
        }
        return runs;
    }

    private static List<string> Patterns(List<(bool Ascii, string Text)> runs, List<string> readings)
    {
        var sounds = new List<List<Sound>?>();
        var index = 0;
        foreach (var (ascii, _) in runs) sounds.Add(ascii ? null : Sounds(readings[index++]));
        for (var r = 0; r < runs.Count; r++)
        {
            if (!runs[r].Ascii && sounds[r] is null) throw new InvalidDataException($"「{runs[r].Text}」の読みに、ローマ字で打てない文字があります。");
        }

        string Make(Func<int, int, int> pick, bool sokuonX = false, bool doubleN = false)
        {
            var builder = new StringBuilder();
            for (var r = 0; r < runs.Count; r++)
            {
                if (runs[r].Ascii) { builder.Append(runs[r].Text); continue; }
                var list = sounds[r]!;
                builder.Append(Spell(list, list.Select((_, i) => pick(r, i)).ToArray(), sokuonX, doubleN));
            }
            return builder.ToString();
        }

        var patterns = new List<string> { Make((_, _) => 0), Make((_, _) => 99) };
        for (var r = 0; r < runs.Count; r++)
        {
            if (sounds[r] is not { } list) continue;
            for (var i = 0; i < list.Count; i++)
            {
                for (var o = 1; o < list[i].Options.Length; o++)
                {
                    var (rr, ii, oo) = (r, i, o);
                    patterns.Add(Make((x, y) => x == rr && y == ii ? oo : 0));
                }
            }
        }
        if (sounds.Any(s => s?.Any(x => x.Kana == "ん") == true)) patterns.Add(Make((_, _) => 0, doubleN: true));
        if (sounds.Any(s => s?.Any(x => x.Kana == "っ") == true)) patterns.Add(Make((_, _) => 0, sokuonX: true));
        return patterns.Distinct().Take(MaxPatterns).ToList();
    }

    public static void Run(string expected, string? reading, Func<string, string?>? readingOf, Func<CompositionTests.Keyboard> keyboard, string engine)
    {
        try { RunCore(expected, reading, readingOf, keyboard, engine); }
        catch (InvalidDataException ex)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, Json));
        }
    }

    public static void Batch(Func<string, string?>? readingOf, Func<CompositionTests.Keyboard> keyboard, Func<string> engine)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        while (Console.In.ReadLine() is { Length: > 0 } line)
        {
            var slash = line.IndexOf(" / ", StringComparison.Ordinal);
            Henkan.Reset();
            try { Run(slash >= 0 ? line[..slash] : line, slash >= 0 ? line[(slash + 3)..] : null, readingOf, keyboard, engine()); }
            catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { error = $"試せませんでした ({ex.GetType().Name})" })); }
            Console.Out.Flush();
        }
    }

    private static void RunCore(string expected, string? reading, Func<string, string?>? readingOf, Func<CompositionTests.Keyboard> keyboard, string engine)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        expected = expected.Trim();
        var runs = Runs(expected);
        var readings = new List<string>();
        if (reading is { Length: > 0 })
        {
            var rest = ToHiragana(reading.Trim());
            var afterJapanese = false;
            foreach (var (ascii, text) in runs)
            {
                if (!ascii) { afterJapanese = true; continue; }
                var at = rest.IndexOf(text, StringComparison.Ordinal);
                if (at < 0) throw new InvalidDataException($"読みに英字の部分「{text}」がありません。");
                if (afterJapanese) readings.Add(rest[..at]);
                rest = rest[(at + text.Length)..];
                afterJapanese = false;
            }
            if (afterJapanese) readings.Add(rest);
        }
        else
        {
            // 読み省略: かなだけならそのまま。漢字は readingOf か、無ければエラーで読みを促す。
            foreach (var (ascii, text) in runs.Where(r => !r.Ascii))
            {
                var isKana = text.All(c => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヶ' or 'ー');
                if (isKana) { readings.Add(ToHiragana(text)); continue; }
                if (readingOf is null)
                    throw new InvalidDataException("読みを「/」の後ろに書いてください (例: 私はgoogleが好きです / わたしはgoogleがすきです)。");
                readings.Add(ToHiragana(readingOf(text) ?? throw new InvalidDataException($"「{text}」の読みが分かりません。「/」の後ろに読みを書いてください。")));
            }
        }

        expected = System.Text.RegularExpressions.Regex.Replace(expected, @"(?<=[^ -~]) +(?=[^ -~])", "");

        var results = new List<object>();
        foreach (var keys in Patterns(runs, readings))
        {
            var live = keyboard();
            live.Type(keys + "\n");
            var entered = live.Host.Document;

            var k = keyboard();
            k.Type(keys + " ");
            var notes = new List<string>();
            string first;
            if (k.Host.View is { Converting: true, Clauses: { } clauses } view)
            {
                var committed = k.Host.Document;
                first = committed + string.Concat(clauses);
                var all = new List<List<string>>();
                for (var c = 0; c < clauses.Count; c++)
                {
                    if (c > 0) k.Press(VirtualKeys.Right);
                    all.Add(k.Host.View!.Candidates.ToList());
                }
                var remaining = expected;
                if (committed.Length > 0)
                {
                    var same = 0;
                    while (same < committed.Length && same < expected.Length && committed[same] == expected[same]) same++;
                    if (same < committed.Length) notes.Add($"空白までで確定した部分: 「{Trim(committed[same..], 12)}」が違う (出てほしいのは「{Trim(expected[same..], 12)}」)");
                    remaining = expected[Math.Min(committed.Length, expected.Length)..];
                }
                for (var c = 0; c < clauses.Count; c++)
                {
                    if (remaining.Length == 0) { notes.Add($"{c + 1} 番目の文節「{clauses[c]}」: 出てほしい文より文節が多い (余分)"); continue; }
                    var candidates = all[c];
                    var found = candidates.FindIndex(x => x.Length > 0 && remaining.StartsWith(x, StringComparison.Ordinal));
                    if (found >= 0)
                    {
                        if (found > 0 && HalfWidth(candidates[0]) == HalfWidth(candidates[found])) notes.Add($"記号の全角 / 半角だけが違う: 「{candidates[0]}」→「{candidates[found]}」 (日本語の文の中の記号は全角になる。半角は候補の {found + 1} 番目)");
                        else if (found > 0) notes.Add($"{c + 1} 番目の文節「{clauses[c]}」: 「{candidates[found]}」は候補の {found + 1} 番目 (最初の変換では出ない)");
                        remaining = remaining[candidates[found].Length..];
                        continue;
                    }
                    var length = remaining.Length;
                    if (c + 1 < clauses.Count)
                    {
                        var next = all[c + 1];
                        length = Enumerable.Range(1, remaining.Length - 1)
                            .FirstOrDefault(p => next.Any(x => x.Length > 0 && remaining[p..].StartsWith(x, StringComparison.Ordinal)), Math.Min(clauses[c].Length, remaining.Length));
                    }
                    notes.Add($"{c + 1} 番目の文節「{clauses[c]}」: 出てほしい「{remaining[..length]}」が候補に無い (区切りが違うか、候補に無い)");
                    remaining = remaining[length..];
                }
                if (remaining.Length > 0) notes.Add($"最後の「{remaining}」が出ていない (文節が足りない)");
            }
            else first = k.Host.Document.TrimEnd();
            var split = Words(entered) == Words(expected);
            if (!split) notes.Insert(0, $"日本語 / 英語の分かれ方が違う (英字: {(Words(entered) is { Length: > 0 } w ? w : "なし")}、出てほしいのは {(Words(expected) is { Length: > 0 } e ? e : "なし")})");
            foreach (var result in new[] { first, entered }.Distinct())
            {
                if (result != expected && HalfWidth(result) == HalfWidth(expected)) notes.Add($"記号の全角 / 半角だけが違う: {WidthDifference(result, expected)} (日本語の文の中の記号は全角になる。半角は候補にある)");
            }
            results.Add(new { keys, entered, first, liveOk = entered == expected, firstOk = first == expected, splitOk = split, notes });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { expected, reading = string.Join(" / ", readings), engine, results },
            Json));
    }

    private static string HalfWidth(string text) => new(text.Select(c => c is >= '！' and <= '～' ? (char)(c - 0xFEE0) : c == '　' ? ' ' : c).ToArray());
    private static string WidthDifference(string actual, string expected) =>
        string.Join("、", actual.Zip(expected).Where(p => p.First != p.Second).Select(p => $"「{p.First}」→「{p.Second}」").Distinct());
    private static string Trim(string text, int max) => text.Length > max ? text[..max] + "…" : text;
    private static string Words(string text) =>
        string.Join(" ", System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z][A-Za-z'’-]*").Select(m => m.Value.ToLowerInvariant()));
}
