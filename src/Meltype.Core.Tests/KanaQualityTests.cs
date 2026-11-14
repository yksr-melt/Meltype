// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Reflection;
using Meltype.Composition;
using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>かな入力 (JIS) の判定の精度: 日本語の文を英字にしないか、日本語の中の英単語を英字にするか。</summary>
internal static class KanaQualityTests
{
    /// <summary>ひらがな → JIS かな配列のキー (仮想キー, Shift)。濁点・半濁点は 2 キー。</summary>
    internal static (int Vk, bool Shift)[] KeysFor(string kana)
    {
        var keys = new List<(int, bool)>();
        foreach (var c in kana)
        {
            if (Find(c, false) is { } plain) keys.Add((plain, false));
            else if (Find(c, true) is { } shifted) keys.Add((shifted, true));
            else if (Base(c, "がぎぐげござじずぜぞだぢづでどばびぶべぼゔ", "かきくけこさしすせそたちつてとはひふへほう") is { } voiced) { keys.Add((Find(voiced, false) ?? throw new ArgumentException($"{c} ({kana})"), false)); keys.Add((0xC0, false)); }
            else if (Base(c, "ぱぴぷぺぽ", "はひふへほ") is { } semi) { keys.Add((Find(semi, false) ?? throw new ArgumentException($"{c} ({kana})"), false)); keys.Add((0xDB, false)); }
            else throw new ArgumentException($"かな入力で打てない文字: {c} ({kana})");
        }
        return keys.ToArray();
    }

    private static char? Base(char c, string marked, string plain) => marked.IndexOf(c) is var i and >= 0 ? plain[i] : null;

    private static int? Find(char c, bool shift)
    {
        for (var vk = 0x20; vk <= 0xE2; vk++)
            if (KanaDetector.KanaForKey(vk, shift) == c) return vk;
        return null;
    }

    /// <summary>
    /// 日本語の文 (品質テストのかなの文と、読みの一覧の語をつないだ 1500 文) を英字にしないか、
    /// 日本語の中の英単語 (きょうは + google + でけんさく) を英字にするか。かな入力はアルファ版なので、目安は 99 % と 95 %。
    /// </summary>
    [Test]
    public static void KanaInput_Accuracy()
    {
        var romaji = new RomajiDetector();
        var typing = (System.Collections.IEnumerable)typeof(Quality).GetField("Typing", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var japanese = new List<string>();
        foreach (var c in typing)
        {
            var expected = (string)c.GetType().GetProperty("Expected")!.GetValue(c)!;
            var before = (string?)c.GetType().GetProperty("Before")!.GetValue(c);
            if (before is null && expected.Length >= 2 && expected.All(ch => ch is >= 'ぁ' and <= 'ゔ' or 'ー')) japanese.Add(expected);
        }
        // 読みの一覧の語をつないだ文 (名詞 + 助詞 + 名詞 + 助詞 + 動詞)
        var readings = DictionarySource.ReadEmbedded("readings.txt").Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => !l.StartsWith('#') && l.Length > 0).Select(l => l.Split('\t')).ToList();
        var nouns = readings.Where(r => r.Length == 1 && r[0].Length >= 2).Select(r => r[0]).ToList();
        var verbs = readings.Where(r => r.Length > 1 && r[1].StartsWith('v')).Select(r => r[0]).ToList();
        var particles = new[] { "は", "が", "を", "に", "で", "の", "と", "も" };
        var random = new Random(1);
        for (var n = 0; n < 1500; n++)
            japanese.Add(nouns[random.Next(nouns.Count)] + particles[random.Next(particles.Length)] + nouns[random.Next(nouns.Count)] + particles[random.Next(particles.Length)] + verbs[random.Next(verbs.Count)]);
        var fails = new List<string>();
        foreach (var sentence in japanese.Distinct())
        {
            if (sentence.Any(ch => ch == 'ゔ')) continue;
            var k = new CompositionTests.Keyboard { Kana = true };
            k.TypeKeys(KeysFor(sentence));
            k.Type("\n");
            if (k.Host.Document != sentence) fails.Add($"{sentence} → {k.Host.Document}");
        }
        var mixed = new List<string>();
        var english = ((string[])typeof(Quality).GetField("CommonEnglish", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Where(w => w.Length >= 4 && w.All(char.IsAsciiLetterLower)).Distinct().ToArray();
        var heads = new[] { "きょうは", "さっき", "これは", "あしたの", "わたしの" };
        var tails = new[] { "でけんさく", "がおちた", "をみた", "のはなし", "です" };
        var okMixed = 0; var totalMixed = 0;
        foreach (var w in english)
            foreach (var h in heads.Take(2))
                foreach (var t in tails.Take(2))
                {
                    totalMixed++;
                    var k = new CompositionTests.Keyboard { Kana = true };
                    k.TypeKeys(KeysFor(h));
                    k.TypeKanaKeys(w);
                    k.TypeKeys(KeysFor(t));
                    k.Type("\n");
                    if (k.Host.Document == h + w + t) okMixed++; else mixed.Add($"{h}+{w}+{t} → {k.Host.Document}");
                }
        var total = japanese.Count(sentence => !sentence.Contains('ゔ'));
        Console.WriteLine($"  かな入力: 日本語 {total - fails.Count}/{total}、混在 {okMixed}/{totalMixed}");
        Assert.True((total - fails.Count) * 100 >= total * 99, $"日本語の文を英字にした: {string.Join(" / ", fails.Take(10))}");
        Assert.True(okMixed * 100 >= totalMixed * 95, $"日本語の中の英単語を英字にしなかった: {string.Join(" / ", mixed.Take(10))}");
    }
}

