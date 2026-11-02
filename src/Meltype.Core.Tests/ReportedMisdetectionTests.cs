// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>報告された誤判定 (issue #1426 ほか) と、その逆向き・似た入力。打って Enter した結果 (漢字にしない) で確かめる。</summary>
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
    public static void Issue1158_AbbreviationBeforeParticleIsNotTypo()
    {
        // 入力の始めの知らない短い英字 + 付属語 (eq|が) は略語。打ち間違いとして ea (えあ) に直さない
        Check(("eqgatyottonigate", "eqがちょっとにがて"), ("mtgnozikan", "mtgのじかん"),
            // 打ち間違いの直しは今までどおり
            ("onegaishimsu", "おねがいします"), ("tabde", "tabで"));
    }

    [Test]
    public static void Issue1417_ModelNumberAfterDigit()
    {
        // 数字の後ろの母音 1 文字 (GPT-4o の 4o) は型番
        Check(("4o", "4o"), ("gpt-4o", "gpt-4o"),
            // 順位の 位 (1i)・数字の後ろの語 (3ko)・英字の後ろの数字の後ろ (m3e) は今までどおり
            ("1i", "1い"), ("3ko", "3こ"), ("4oku", "4おく"));
    }

    [Test]
    public static void Issue1319_LowercaseHeadThenCapitalIsName()
    {
        // 小文字 1〜2 文字 + 大文字で始まる語 (iZotope) はかなにしない。知っている語 (iPhone) + 付属語は今までどおり分ける
        Check(("iZotope", "iZotope"), ("iPhonega", "iPhoneが"), ("eBaydekatta", "eBayでかった"), ("oRPCde", "oRPCで"));
    }

    [Test]
    public static void Issue1356_NewTopLevelDomains()
    {
        Check(("meltype.moe", "meltype.moe"), ("hoge.ne.jp", "hoge.ne.jp"),
            // 句点の後ろの日本語は今までどおり
            ("yoroshiku.onegai", "よろしく。おねがい"), ("owari.desune", "おわり。ですね"));
    }

    [Test]
    public static void Issue1287_UnderscoreBetweenJapaneseWords()
    {
        // _ でつないだ日本語の語はかなに。ユーザー名・識別子 (cafely_latte・snake_case) は英字のまま
        Check(("rinngo_mikann_momo", "りんご_みかん_もも"), ("cafely_latte", "cafely_latte"), ("snake_case", "snake_case"), ("upah_setu", "upah_setu"));
    }

    [Test]
    public static void Issue971_TypoInsideWordWithShortEnglishWord()
    {
        // 語の途中に偶然できた短い英単語 (mas・kas) があっても、直すと 1 つの語になるなら直す
        Check(("sumimasne", "すみません"), ("henkasnn", "へんかん"), ("henkasnsuru", "へんかんする"),
            // 表示で英字にした 3 文字の語 + 付属語 (tab|で) は直さない (issue #220)
            ("kyouhatabdeyaru", "きょうはtabでやる"));
    }

    [Test]
    public static void Issue1365_1391_1426_EnglishWords()
    {
        Check(("metadata", "metadata"), ("Metadata", "Metadata"), ("american megatrends", "american megatrends"),
            ("American Megatrends", "American Megatrends"), ("sasuganipredasu", "さすがにpreだす"),
            // k で打った日本語は今までどおり
            ("amerikanijyuusyo", "あめりかにじゅうしょ"), ("megane", "めがね"));
    }

    [Test]
    public static void Issue1369_1347_JapaneseWords()
    {
        // pat + ち・ひとめ + bore にしない
        Check(("patti", "ぱっち"), ("hitomebore", "ひとめぼれ"), ("patchwork", "patchwork"), ("bored", "bored"));
    }

    [Test]
    public static void Issue1367_SokuonFormIsFollowedByTeOrTa()
    {
        // 前が日本語でも、いっ (行っ) + すえ と区切らない。いって・かったから・いっすよ は今までどおり
        Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var k = new Keyboard();
            k.Type("issuekakuka\n");
            k.Type("issuekakuka");
            Assert.Equal("issueかくか", k.Showing, "同じ入力欄で 2 回目も issue のまま");
        }
        finally
        {
            Detector.SpellChecker = null;
        }
        Check(("ittekita", "いってきた"), ("kattakara", "かったから"), ("issuyo", "いっすよ"), ("issune", "いっすね"), ("issyoniiku", "いっしょにいく"));
    }

    [Test]
    public static void Issue1360_ContextRuleOnlyBeforeParticle()
    {
        var rules = new ContextRules();
        rules.AddText("はし 端 : 左 右");
        Assert.Equal(null, rules.Choose("はしもと", "左内", "橋本"), "語の続き (はし|もと) には使わない");
        Assert.Equal("端を", rules.Choose("はしを", "左の"), "付属語 (を) が続くなら使う");
        Assert.Equal("端です", rules.Choose("はしです", "左の", "橋です"), "変換エンジンがかなのまま付けた残り (です) も");
    }

    [Test]
    public static void Issue1315_RareKanjiIsDemoted()
    {
        Assert.Equal("十部", RareKanji.Demote(["禸部", "十部", "重部"])[0], "まれな漢字の候補を後ろに");
        Assert.Equal("禸部", RareKanji.Demote(["禸部", "十部"])[^1], "消さずに残す");
        Assert.Equal("橋本", RareKanji.Demote(["橋本", "觜本"])[0], "一番目がふつうの漢字なら変えない");
    }

    [Test]
    public static void Issue1013_1402_ProperNouns()
    {
        // 数字を含む固有名詞 (yachi8000) と、ローマ字として読める固有名詞 (neovide) は英字のまま。一覧に無い語 + 数字は今までどおり
        Check(("yachi8000", "yachi8000"), ("yachi8000desu", "yachi8000です"), ("neovide", "neovide"), ("heisei31nenn", "へいせい31ねん"));
    }

    [Test]
    public static void Issue935_SlashDateCandidates()
    {
        var k = new Keyboard(now: () => new DateTime(2026, 10, 1));
        k.Type("10/20 ");
        Assert.True(k.Host.View is { Converting: true }, "月/日 は Space で候補を出す");
        var candidates = k.Host.View!.Candidates;
        foreach (var expected in new[] { "10/20", "10月20日", "10/20(火)", "10月20日(火)" })
            Assert.True(candidates.Contains(expected), expected + ": " + string.Join(",", candidates));
        // 日付にならない数字 (13/40) は今までどおり確定して空白
        var other = new Keyboard();
        other.Type("13/40 ");
        Assert.Equal("13/40 ", other.Host.Document, "日付でなければ候補を出さない");
    }

    /// <summary>Windows のスペルチェッカーのように、同梱の一覧に無い語 (tori) も知っている。</summary>
    private sealed class WiderChecker(params string[] extra) : Detection.IWordChecker
    {
        public bool IsAvailable => true;
        public bool IsWord(string lower) => extra.Contains(lower) || Detection.BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void Issue705_HyphenPrefixDoesNotCutInsideRomaji()
    {
        // re- + 英単語 (tori) でも、すぐ後ろに英字が続く (re-tori|mitto) なら長音を挟んだ日本語
        Detector.SpellChecker = new WiderChecker("tori");
        try
        {
            foreach (var (typed, expected) in new[] { ("re-torimitto", "れーとりみっと"), ("re-install", "re-install"), ("re-tori", "re-tori") })
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
}
