// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>っ で終わる活用形 (いっ = 行っ) の後ろに、て・た 行でない語を続けない (issue #1367)。</summary>
internal static class SokuonFormTests
{
    [Test]
    public static void Issue1367_IssueAfterJapaneseStaysEnglish()
    {
        Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            // 同じ入力欄で 2 回目 (前が日本語) も、いっ|すえ と区切らない
            var k = new Keyboard();
            k.Type("issuekakuka\n");
            k.Type("issuekakuka");
            Assert.Equal("issueかくか", k.Showing, "前が日本語でも issue のまま");
        }
        finally
        {
            Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Issue1367_Boundaries()
    {
        foreach (var (typed, expected) in new[]
        {
            // 辞書にある語の っ (あっ・おっ・えっ) + 語、っす は今までどおり
            ("assou", "あっそう"), ("ossu", "おっす"), ("ettosore", "えっとそれ"), ("issuyo", "いっすよ"), ("issune", "いっすね"),
            // 活用形 + て・た
            ("ittekita", "いってきた"), ("kattakara", "かったから"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }
}
