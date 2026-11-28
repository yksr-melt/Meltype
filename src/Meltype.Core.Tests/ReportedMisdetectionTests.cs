// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>辞書に語を足して直した、報告された誤判定 (issue #1426 ほか)。打って Enter した結果 (漢字にしない) で確かめる。</summary>
internal static class ReportedMisdetectionTests
{
    private static void Check(params (string Typed, string Expected)[] cases)
    {
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
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
        // readings-extra.txt (とぐる・びるど・まーじ・ありゃ・ばんえつ・らぷらた・いぬがみけ)。と + guru・びる + doha・ar + や にしない
        Check(("toguru", "とぐる"), ("birudoha", "びるどは"), ("datte.ma-ji", "だって。まーじ"), ("arya", "ありゃ"), ("bannetu", "ばんえつ"),
            ("rapuratagawa", "らぷらたがわ"), ("inugamikenoitizoku", "いぬがみけのいちぞく"));
        // どっとえ (ドット絵) を 1 語にする: dot + とえ にしない (issue #2390)。辞書の語でない同じ形 (cut + として・bot + とえ) は今までどおり
        Check(("dottoe", "どっとえ"), ("dottoha", "どっとは"), ("cuttosite", "cutとして"), ("cuttosuru", "cutとする"), ("bottoe", "botとえ"),
            ("bottomo", "botとも"), ("dottonet", "dotとnet"));
    }

    [Test]
    public static void Dictionary_EnglishWordsOfIssue2389()
    {
        // english.txt (assignee・sideload・linpeas・ver)・propernouns.txt (Mergify・Antigravity・Obsidian・KonomiTV)
        Check(("assignee", "assignee"), ("mergify", "mergify"), ("sideload", "sideload"), ("antigravity", "antigravity"), ("linpeas", "linpeas"),
            ("obsidian", "obsidian"), ("konomitv", "KonomiTV"), ("verageru", "verあげる"));
    }

    [Test]
    public static void Inflections_AreJapaneseBeforeEnglishLookalikes()
    {
        // 命令形 (みろ) と音便の て 形 (まって) は 1 語: miro・matte の英単語にしない (issue #2261・#1985)
        Check(("nonbirimiroyo", "のんびりみろよ"), ("tunagarumademattekara", "つながるまでまってから"), ("tabero", "たべろ"), ("yondekara", "よんでから"),
            ("korewomiro", "これをみろ"), ("mattehosii", "まってほしい"), ("mitemite", "みてみて"),
            // 逆向き: 英文の中の matte は英字のまま
            ("matte finish", "matte finish"), ("the matte", "the matte"));
        // 後ろに活用の続きが要る形 (させ・してい) の後ろは英単語にしない (issue #2065・#2206)。英単語の後ろの させ は今までどおり
        Check(("sasetaken", "させたけん"), ("setteisiteirundakedo", "せっていしているんだけど"), ("githubnipushsaseta", "GitHubにpushさせた"),
            ("pushsaseta", "pushさせた"), ("tabesasete", "たべさせて"), ("githubdeha", "GitHubでは"));
    }

    [Test]
    public static void Sokuon_BeforeVoicedKanaIsEnglishConsonant()
    {
        // 語の境目の っ の後ろに濁音は来ないので、bug + が (ぶっ + が にしない: issue #1791)。1 語の外来語 (ばっぐ) は今までどおり
        Check(("bugga", "bugが"), ("bugwo", "bugを"), ("baggu", "ばっぐ"),
            // 逆向き: っ の後ろが清音なら日本語の語 (ぶっかける)、1 語の外来語の濁音 (べっど) も日本語
            ("bukkakeru", "ぶっかける"), ("buttobasu", "ぶっとばす"), ("beddode", "べっどで"), ("jobga", "jobが"),
            // 読みが辞書に無い外来語の途中の っ + 付属語でない語 (せっど・ぶっぢずむ) と、辞書の外来語 (ぶっだ) は日本語
            ("seddo", "せっど"), ("buddizumu", "ぶっぢずむ"), ("budda", "ぶっだ"));
    }

    [Test]
    public static void Digits_InsideJapaneseAreNotNumeronyms()
    {
        // 前の語の続きの母音 + 数字 + かな (mou|1|tu・dai|1|dan) は日本語 (issue #1718・#2379)。略語 (i18n・e2e・k8s) は今までどおり
        Check(("mou1tu", "もう1つ"), ("dai1dan", "だい1だん"), ("i18n", "i18n"), ("e2e", "e2e"), ("k8s", "k8s"),
            ("a11y", "a11y"), ("h2o", "h2o"), ("l10n", "l10n"));
    }

    [Test]
    public static void Hyphen_IsLongVowelBeforeReadableWords()
    {
        // re- の後ろがローマ字として読めるよく使う英単語 (toga) だけなら、- は長音 (issue #1841)。辞書の英単語 (re-make・e-mail) は英字
        Check(("re-toga", "れーとが"), ("re-make", "re-make"), ("e-mail", "e-mail"),
            ("re-do", "re-do"), ("co-op", "co-op"), ("x-ray", "x-ray"));
    }

    [Test]
    public static void HexColor_StaysAsTyped()
    {
        // 数字を含む 16 進の色は英字のまま (issue #2383)。英字だけ (#cafe) は今までどおり
        Check(("#30b0e8", "#30b0e8"), ("#cafe", "#cafe"), ("#abc1", "#abc1"),
            // 数字だけ・数字の後ろに英字だけが続くものは Issue の番号 (+ 日本語)
            ("#1234", "#1234"), ("#2392de", "#2392で"), ("#12de", "#12で"));
    }

    [Test]
    public static void TechnicalWordsMissingFromScowl_AreEnglish()
    {
        // SCOWL の一覧に無い技術用語 (app・cron・ops・addon・infra・wiki) は英単語 (issue #2526)
        Check(("appswo", "appsを"), ("appga", "appが"), ("kyouhaappwotukau", "きょうはappをつかう"), ("cronwo", "cronを"), ("opswo", "opsを"),
            ("addonswo", "addonsを"), ("infranikansuru", "infraにかんする"), ("sorehawikininotteru", "それはwikiにのってる"),
            // 逆向き: 綴りの似たかなの語は今までどおり
            ("appuru", "あっぷる"), ("kappa", "かっぱ"), ("appare", "あっぱれ"), ("appaku", "あっぱく"), ("kuron", "くろん"),
            ("oppusu", "おっぷす"), ("adon", "あどん"), ("inhura", "いんふら"), ("uiki", "ういき"));
    }
}
