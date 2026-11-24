// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Dictionary;
using Meltype.Ime;

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
