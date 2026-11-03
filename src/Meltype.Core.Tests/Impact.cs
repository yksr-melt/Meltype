// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Composition;
using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// 変更の影響を見るための、よく使う語の判定の一覧 (issue #1196)。
/// よく使う語の読み (readings.txt) をローマ字にしたものと、よく使う英単語 (english-words.txt) を 1 語ずつ打って確定し、見え方を書き出す。
/// Pull Request のチェックで main と Pull Request の両方で書き出して比べ、判定が変わった語を一覧にする (.github/bot/pr-checks.mjs impact)。
/// 変わった語は必ずしも間違いではない (直したい語が変わるのは当然) ので、失敗にはせず、意図しない変化に気づくための参考にする。
///   dotnet run --project src/Meltype.Core.Tests -- --judge-corpus 出力ファイル [ほかの一覧 …]
/// ほかの一覧 (前に書き出した「語[Tab]見え方」) を渡すと、その語も判定する。main と Pull Request の両方で、
/// 互いの一覧の語も判定して同じ語を比べるため (辞書から消した語・足した語も比べられるように)。
/// </summary>
internal static class Impact
{
    public static void JudgeCorpus(string output, IEnumerable<string> extraLists)
    {
        // 学習を付けない判定 (WordJudge と同じ)。スペルチェッカーは品質テスト・報告の再現と同じく組み込みの一覧
        var detector = CompositionDetector.CreateDefault();
        detector.SpellChecker ??= BuiltInWordChecker.Shared;
        // 渡した一覧を読めなければ、その語を比べられないので止める (File.ReadAllLines が例外を出す)
        var extra = extraLists.SelectMany(File.ReadAllLines).Select(line => line.Split('\t')[0].Trim());
        var words = Readings().Select(ToRomaji).OfType<string>().Concat(EnglishWords()).Concat(extra).ToList();
        File.WriteAllText(output, WordJudge.JudgeAll(detector, words), new UTF8Encoding(false));
        Console.WriteLine($"判定した語: {words.Distinct(StringComparer.Ordinal).Count()} 語");
    }

    private static IEnumerable<string> Readings() => Lines("readings.txt").Select(line => line.Split('\t')[0]);

    private static IEnumerable<string> EnglishWords() => Lines("english-words.txt").Where(w => w.All(char.IsAsciiLetterLower));

    private static IEnumerable<string> Lines(string name)
    {
        using var stream = typeof(CompositionDetector).Assembly.GetManifestResourceStream("Meltype.dictionaries." + name)
            ?? throw new InvalidOperationException($"辞書 {name} がありません");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#')) lines.Add(line);
        }
        return lines;
    }

    private static readonly Dictionary<string, string> Kana = Build();

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Row(string kana, string romaji)
        {
            var spellings = romaji.Split(' ');
            for (var i = 0; i < kana.Length; i++) map[kana[i].ToString()] = spellings[i];
        }
        Row("あいうえお", "a i u e o"); Row("かきくけこ", "ka ki ku ke ko"); Row("さしすせそ", "sa shi su se so");
        Row("たちつてと", "ta chi tsu te to"); Row("なにぬねの", "na ni nu ne no"); Row("はひふへほ", "ha hi fu he ho");
        Row("まみむめも", "ma mi mu me mo"); Row("やゆよ", "ya yu yo"); Row("らりるれろ", "ra ri ru re ro"); Row("わを", "wa wo");
        Row("がぎぐげご", "ga gi gu ge go"); Row("ざじずぜぞ", "za ji zu ze zo"); Row("だぢづでど", "da di du de do");
        Row("ばびぶべぼ", "ba bi bu be bo"); Row("ぱぴぷぺぽ", "pa pi pu pe po");
        // 拗音 (き + ゃ → kya)
        foreach (var (kana, head) in new[] { ("き", "ky"), ("に", "ny"), ("ひ", "hy"), ("み", "my"), ("り", "ry"), ("ぎ", "gy"), ("び", "by"), ("ぴ", "py"),
                     ("し", "sh"), ("ち", "ch"), ("じ", "j") })
        {
            map[kana + "ゃ"] = head + "a";
            map[kana + "ゅ"] = head + "u";
            map[kana + "ょ"] = head + "o";
        }
        return map;
    }

    /// <summary>
    /// ひらがなの読みを、IME でよく打つローマ字 (ヘボン式) にする。ん は次が母音・や行・な行・末尾なら nn、っ は次の子音を重ねる。
    /// 打ち方が決めにくい読み (ー・小書きの ぁ・ゔ など) は null (一覧に入れない)。
    /// </summary>
    internal static string? ToRomaji(string reading)
    {
        var parts = new List<string>();
        for (var i = 0; i < reading.Length;)
        {
            if (i + 1 < reading.Length && Kana.TryGetValue(reading.Substring(i, 2), out var youon)) { parts.Add(youon); i += 2; }
            else if (Kana.TryGetValue(reading[i].ToString(), out var single)) { parts.Add(single); i++; }
            else if (reading[i] is 'ん' or 'っ') { parts.Add(reading[i].ToString()); i++; }
            else return null;
        }
        var romaji = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            var next = i + 1 < parts.Count ? parts[i + 1] : null;
            if (parts[i] == "ん") romaji.Append(next is null || next[0] is 'a' or 'i' or 'u' or 'e' or 'o' or 'y' or 'n' ? "nn" : "n");
            else if (parts[i] == "っ")
            {
                if (next is null || next[0] is 'a' or 'i' or 'u' or 'e' or 'o' or 'ん' or 'っ') return null;
                romaji.Append(next[0]);
            }
            else romaji.Append(parts[i]);
        }
        return romaji.ToString();
    }
}
