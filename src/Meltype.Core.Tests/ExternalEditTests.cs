// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Dictionary;
using Meltype.Ime;
using Meltype.Input;
using Meltype.Learning;

namespace Meltype.Tests;

/// <summary>入力している Meltype と別のプロセス (設定アプリ) が、同じ学習データ・定型文を書き換える。</summary>
internal static class ExternalEditTests
{
    // ファイルの更新時刻は OS の粗い時計 (Linux は数ミリ秒刻み) で付くので、2 つのプロセスの書き込みの間を空ける (実際は人の操作の間が空く)
    private static void Tick() => Thread.Sleep(30);

    [Test]
    public static void LearningForgottenByAnotherProcess_IsNotWrittenBack()
    {
        var directory = Directory.CreateTempSubdirectory("meltype-external-").FullName;
        try
        {
            var ime = new LearningData(directory);
            ime.LanguageMemory.Remember("kyoto", english: true, explicitChoice: true);
            ime.History.Remember("はし", "箸");
            ime.Phrases.Remember("きょうは", "今日は");
            ime.Translations.Remember("すし", "sushi");
            Tick();

            var app = new LearningData(directory);
            Assert.Equal("kyoto", app.Languages().Single().Word, "設定アプリは入力している Meltype が保存した学習を読む");
            app.ForgetLanguages(["kyoto"]);
            app.ForgetConversion("はし");
            Tick();

            // 入力している Meltype は、読み直す前に覚えたものを保存しようとしても、消した語を書き戻さない
            ime.LanguageMemory.Remember("osaka", english: true, explicitChoice: true);
            ime.History.Remember("かき", "柿");
            Assert.Equal(null, new LearningData(directory).LanguageMemory.Get("kyoto"), "設定アプリで消した語を書き戻さない");
            Assert.Equal(null, new LearningData(directory).History.Get("はし"), "設定アプリで消した変換を書き戻さない");
            Assert.Equal(null, ime.LanguageMemory.Get("kyoto"), "入力している Meltype も消した語を使わない");
            Assert.Equal(null, ime.History.Get("はし"), "入力している Meltype も消した変換を使わない");

            ime.History.Remember("かき", "柿");
            Tick();
            new LearningData(directory).Reset();
            Tick();
            ime.Refresh();
            Assert.Equal(null, ime.History.Get("かき"), "リセットを読み直す");
            Assert.True(!ime.Phrases.StartingWith("きょ").Any(), "リセットを読み直す");
            Assert.True(ime.Translations.Get("すし").Count == 0, "リセットを読み直す");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void FilesThatCannotBeReadForAWhile_KeepTheirContents()
    {
        // ほかのプロセスが開いていて一時的に読めないだけなら、中身を空にせず、.broken にも残さない。読めるようになったら読み直す。
        var directory = Directory.CreateTempSubdirectory("meltype-external-").FullName;
        try
        {
            var snippetsPath = Path.Combine(directory, "snippets.json");
            var phrasesPath = Path.Combine(directory, "phrases.txt");
            var snippets = new SnippetStore(snippetsPath);
            snippets.Set("sig", "署名");
            var phrases = new PhraseHistory(phrasesPath);
            phrases.Remember("きょうは", "今日は");
            Tick();

            new SnippetStore(snippetsPath).Set("ty", "ありがとうございます");
            var other = new PhraseHistory(phrasesPath);
            other.Remember("あしたは", "明日は");
            Tick();
            using (new FileStream(snippetsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (new FileStream(phrasesPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                snippets.Refresh();
                phrases.Refresh();
                Assert.Equal("署名", snippets.Get("sig"), "読めないあいだは前の定型文のまま");
                Assert.Equal(null, snippets.Get("ty"), "読めないあいだは読み直さない");
                Assert.Equal(1, phrases.Count, "読めないあいだは前の学習のまま");
                snippets.Set("new", "新しい");
            }
            Assert.Equal("ありがとうございます", new SnippetStore(snippetsPath).Get("ty"), "読めなかったファイルを上書きしない");
            Assert.True(!File.Exists(snippetsPath + ".broken") && !File.Exists(phrasesPath + ".broken"), "壊れていないので .broken に残さない");
            snippets.Refresh();
            phrases.Refresh();
            Assert.Equal("ありがとうございます", snippets.Get("ty"), "読めるようになったら読み直す");
            Assert.Equal(2, phrases.Count, "読めるようになったら読み直す");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void BrokenSnippets_KeepTheSnippetsInUse()
    {
        // 動いているあいだにファイルが壊れても、使っている定型文は消さない (壊れたファイルは .broken に残る)
        var directory = Directory.CreateTempSubdirectory("meltype-external-").FullName;
        try
        {
            var path = Path.Combine(directory, "snippets.json");
            var snippets = new SnippetStore(path);
            snippets.Set("sig", "署名");
            Tick();
            File.WriteAllText(path, "{\"sig\":\"署");
            snippets.Refresh();
            Assert.Equal("署名", snippets.Get("sig"), "壊れたファイルを読んでも、前の定型文のまま");
            Assert.True(File.Exists(path + ".broken"), "壊れたファイルは .broken に残す");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void KeyCountThatCannotBeReadAtStart_IsNotOverwritten()
    {
        // 起動したときに読めなかった打鍵数を、0 から数えた数で上書きしない
        var directory = Directory.CreateTempSubdirectory("meltype-external-").FullName;
        try
        {
            var path = Path.Combine(directory, "keycount.json");
            File.WriteAllText(path, "{\"Total\":12345,\"Enabled\":true}");
            KeyCounter counter;
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                counter = new KeyCounter(path);
                counter.Enabled = true;
                counter.Save();
            }
            Assert.Equal(12345L, new KeyCounter(path).Total, "読めないあいだは保存しない");
            counter.Save();
            Assert.Equal(12345L, counter.Total, "読めるようになったら読み直す");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void SnippetsEditedByAnotherProcess_AreReloadedAndKept()
    {
        var directory = Directory.CreateTempSubdirectory("meltype-external-").FullName;
        try
        {
            var path = Path.Combine(directory, "snippets.json");
            var ime = new SnippetStore(path);
            ime.Set("sig", "署名");
            Tick();

            var app = new SnippetStore(path);
            app.Set("ty", "ありがとうございます");
            app.Remove("sig");
            Tick();
            ime.Refresh();
            Assert.Equal("ありがとうございます", ime.Get("ty"), "設定アプリで足した定型文を読み直す");
            Assert.Equal(null, ime.Get("sig"), "設定アプリで消した定型文を読み直す");

            app.Set("pr", "レビューお願いします");
            Tick();
            // 読み直す前に編集しても、設定アプリで足した定型文を消さない
            ime.Set("mt", "Meltype");
            var saved = new SnippetStore(path);
            Assert.Equal("レビューお願いします", saved.Get("pr"), "ほかのプロセスで足した定型文を残す");
            Assert.Equal("Meltype", saved.Get("mt"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
