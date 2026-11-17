// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 haru0416-dev

using System.Globalization;
using System.Reflection;
using System.Text;
using Meltype.Composition;

namespace Meltype.Tests;

internal static class LearningSaveTests
{
    [Test]
    public static void DeferredSaves_WriteLaterAndOnFlush()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-deferred-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var (deferred, delay) = (LearningSaves.Deferred, LearningSaves.Delay);
        try
        {
            LearningSaves.Deferred = true;
            LearningSaves.Delay = TimeSpan.FromMilliseconds(200);
            var languages = new LanguageMemory(Path.Combine(directory, "languages.json"));
            var phrases = new PhraseHistory(Path.Combine(directory, "phrases.txt"));
            var translations = new TranslationHistory(Path.Combine(directory, "translations.json"));
            LanguageMemory Saved() => new(Path.Combine(directory, "languages.json"));

            languages.Remember("cafe", english: false, explicitChoice: true);
            phrases.Remember("きょうは", "今日は");
            translations.Remember("すし", "sushi");
            Assert.True(Directory.GetFiles(directory).Length == 0, "確定したときにはまだ書かない");

            LearningSaves.FlushAll();
            Assert.Equal(false, Saved().Get("cafe"));
            Assert.Equal("今日は", new PhraseHistory(Path.Combine(directory, "phrases.txt")).StartingWith("きょ").Single());
            Assert.Equal(1, new TranslationHistory(Path.Combine(directory, "translations.json")).Get("すし").Single().Count);

            languages.Remember("sushi", english: true, explicitChoice: true);
            Thread.Sleep(1500);
            Assert.Equal(true, Saved().Get("sushi"), "少し待てば書く");

            languages.Remember("tenki", english: true, explicitChoice: true);
            languages.Clear();
            Assert.Equal(null, Saved().Get("sushi"), "リセットはすぐに書く");
            Thread.Sleep(1500);
            Assert.Equal(null, Saved().Get("tenki"), "リセットの前の分を後から書き戻さない");

            languages.Remember("kyoto", english: true, explicitChoice: true);
            LearningSaves.FlushAll();
            languages.Remove(["kyoto"]);
            Assert.Equal(null, Saved().Get("kyoto"), "一覧から消したらすぐに書く");

            phrases.Remember("あしたは", "明日は");
            LearningSaves.FlushAll();
            Assert.Equal("明日は", phrases.StartingWith("あし").Single());
            Assert.Equal(1, phrases.Forget("明日は"));
            Assert.True(!phrases.StartingWith("あし").Any(), "Delete で消した候補を、索引から出さない");
            Assert.True(!new PhraseHistory(Path.Combine(directory, "phrases.txt")).StartingWith("あし").Any(), "Delete で消したらすぐに書く");

            // 書けなかった分は、次の FlushAll で書く
            Directory.CreateDirectory(Path.Combine(directory, "languages.json.tmp"));
            languages.Remember("umi", english: true, explicitChoice: true);
            LearningSaves.FlushAll();
            Assert.Equal(null, Saved().Get("umi"));
            Directory.Delete(Path.Combine(directory, "languages.json.tmp"));
            LearningSaves.FlushAll();
            Assert.Equal(true, Saved().Get("umi"), "書けなかった分を後で書く");
            Directory.CreateDirectory(Path.Combine(directory, "languages.json.tmp"));
            languages.Remove(["umi"]);
            Directory.Delete(Path.Combine(directory, "languages.json.tmp"));
            LearningSaves.FlushAll();
            Assert.Equal(null, Saved().Get("umi"), "書けなかった削除を後で書く");
        }
        finally
        {
            LearningSaves.FlushAll();
            (LearningSaves.Deferred, LearningSaves.Delay) = (deferred, delay);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void FlushAll_WaitsForTimerWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-flush-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var (deferred, delay) = (LearningSaves.Deferred, LearningSaves.Delay);
        try
        {
            LearningSaves.Deferred = true;
            LearningSaves.Delay = TimeSpan.FromMilliseconds(20);
            var path = Path.Combine(directory, "phrases.txt");
            var phrases = new PhraseHistory(path);
            for (var i = 0; i < 2900; i++) phrases.Remember("よみ" + i, "語" + i);
            LearningSaves.FlushAll();
            // タイマーが書いている途中に FlushAll を呼んでも、戻ったときには書き終わっている
            for (var round = 0; round < 60; round++)
            {
                phrases.Remember("さいご" + round, "最後" + round);
                Thread.Sleep(20);
                LearningSaves.FlushAll();
                Assert.True(new PhraseHistory(path).StartingWith("さいご").Contains("最後" + round), $"{round} 回目");
            }
        }
        finally
        {
            LearningSaves.FlushAll();
            (LearningSaves.Deferred, LearningSaves.Delay) = (deferred, delay);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void PhrasePrediction_SameAsScanningAllPhrases()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-phrases-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // 回数・日時が同じもの (並べ替えで差の無いもの) を混ぜる
            var random = new Random(7);
            var lines = new StringBuilder();
            string[] heads = ["き", "きょ", "きょう", "きょうは", "あ", "あし", "あした", "か", "かい", "かいぎ"];
            for (var i = 0; i < 600; i++)
            {
                var reading = heads[random.Next(heads.Length)] + new string((char)random.Next('ぁ', 'ゖ'), random.Next(0, 3));
                lines.Append(CultureInfo.InvariantCulture, $"{reading}\t語{i}\t{random.Next(1, 4)}\t{random.Next(1, 5)}\n");
            }
            var path = Path.Combine(directory, "phrases.txt");
            File.WriteAllText(path, lines.ToString());
            var phrases = new PhraseHistory(path);
            for (var round = 0; round < 3; round++)
            {
                foreach (var prefix in heads.Concat(["", "きょうはい", "ん"]))
                {
                    Assert.Equal(string.Join("|", ScanPhrases(phrases, prefix)), string.Join("|", phrases.StartingWith(prefix)), $"「{prefix}」");
                }
                phrases.Remember("きょうはいい", "今日はいい" + round);
                phrases.Remember(heads[round], "語0");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // 索引を作る前の StartingWith と同じ探し方
    private static IEnumerable<string> ScanPhrases(PhraseHistory phrases, string prefix)
    {
        var entries = (Dictionary<(string Reading, string Text), (int Count, long Used)>)typeof(PhraseHistory)
            .GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(phrases)!;
        return entries.Where(e => e.Key.Reading.Length > prefix.Length && e.Key.Reading.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(e => e.Value.Count).ThenByDescending(e => e.Value.Used)
            .Select(e => e.Key.Text);
    }
}
