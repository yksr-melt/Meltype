// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>辞書に語を足して直した、報告された誤判定 (issue #1426 ほか)。打って Enter した結果 (漢字にしない) で確かめる。</summary>
internal static class ReportedMisdetectionTests
{
    private static void Check(params (string Typed, string Expected)[] cases)
    {
        // アプリと同じく、英単語はスペルチェッカー (Windows 以外は同梱の一覧) でも確かめる
        Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in cases)
            {
                var k = new Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
        }
        finally
        {
            Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Dictionary_EnglishWords()
    {
        // english.txt (metadata・mega・pre・eq)・propernouns.txt (America・American・Megatrends・iZotope・Neovide)
        Check(("metadata", "metadata"), ("Metadata", "Metadata"), ("american megatrends", "american megatrends"),
            ("sasuganipredasu", "さすがにpreだす"), ("eqgatyottonigate", "eqがちょっとにがて"), ("iZotope", "iZotope"), ("neovide", "neovide"),
            // 似た綴りの日本語は今までどおり
            ("amerikanijyuusyo", "あめりかにじゅうしょ"), ("megane", "めがね"));
    }

    [Test]
    public static void Dictionary_JapaneseWords()
    {
        // readings-extra.txt (ぱっち・ひとめぼれ)。pat + ち・ひとめ + bore にしない
        Check(("patti", "ぱっち"), ("hitomebore", "ひとめぼれ"));
    }
}
