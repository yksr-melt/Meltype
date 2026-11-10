// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Eisenferik

using System.Globalization;
using Meltype.Composition;
using Meltype.Detection;

namespace Meltype.Tests;

internal static class PackedDictionaryTests
{
    [Test]
    public static void PackedWordSet_ContainsExactlyTheListedWords()
    {
        // 並んでいない行・同じ語・前後の空白・空行・# の行があっても、書いた語だけを引ける
        var set = PackedWordSet.FromLines("# 見出し\nたなか\n  あおき \n\nたなか\nあい\n#すずき\nあおきの\n");
        Assert.Equal(4, set.Count);
        Assert.Equal(4, set.MaxLength);
        foreach (var word in (string[])["たなか", "あおき", "あい", "あおきの"]) Assert.True(set.Contains(word), word);
        foreach (var word in (string[])["すずき", "あお", "たな", "たなかさん", "", "# 見出し"]) Assert.True(!set.Contains(word), word);
    }

    [Test]
    public static void PackedWordSet_EmptyTextHasNoWords()
    {
        var set = PackedWordSet.FromLines("");
        Assert.Equal(0, set.Count);
        Assert.True(!set.Contains("あ"), "あ");
    }

    [Test]
    public static void PackedWordSet_FirstFieldTakesTheTextBeforeTheTab()
    {
        // readings.txt の形 (読み [TAB] 活用の種類、行末の \r)
        var set = PackedWordSet.FromLines("# 読み\r\nかく\tv5k\r\nたべる\tv1\r\nきょう\r\n", firstField: true);
        Assert.Equal(3, set.Count);
        foreach (var word in (string[])["かく", "たべる", "きょう"]) Assert.True(set.Contains(word), word);
        foreach (var word in (string[])["v5k", "かく\tv5k", "きょう\r"]) Assert.True(!set.Contains(word), word);
    }

    [Test]
    public static void PackedWordSet_HasPrefixOfListedWords()
    {
        var set = PackedWordSet.FromLines("たなか\nあおき\nあい\nあおきの\n");
        foreach (var prefix in (string[])["", "あ", "あお", "あおき", "あおきの", "たな", "たなか"]) Assert.True(set.HasPrefix(prefix), prefix);
        foreach (var prefix in (string[])["あう", "あおきのこ", "た な", "ん"]) Assert.True(!set.HasPrefix(prefix), prefix);
        Assert.True(!PackedWordSet.FromLines("").HasPrefix(""), "空の集合");
    }

    [Test]
    public static void PackedWordSet_FromWordsDropsRepeatedWords()
    {
        var words = Enumerable.Range(0, 5000).Select(i => $"w{i % 3000}").ToList();
        var set = PackedWordSet.FromWords(words);
        Assert.Equal(3000, set.Count);
        Assert.True(words.All(w => set.Contains(w)), "どの語も引ける");
        Assert.True(!set.Contains("") && !set.Contains("w3000") && !set.Contains("w"), "無い語は引けない");
    }

    [Test]
    public static void PackedLines_FindsLinesByKeyInTextOrder()
    {
        // 同じ見出しの行は本文の順。欄が足りない行・# の行・空行は飛ばす
        var lines = PackedLines.FromText("# 見出し\r\nはし\tn\tbridge\r\nあめ\tn\train\r\nはし\tn\tchopsticks\r\nはし\tn\r\n\r\nあ\tn\tah\r\n");
        Assert.Equal(3, lines.Count);
        Assert.Equal(4, lines.LineCount);
        Assert.Equal(2, lines.MaxKeyLength);
        var (first, count) = lines.Find("はし");
        Assert.Equal(2, count);
        Assert.Equal("bridge", lines.Field(first, 2).ToString());
        Assert.Equal("chopsticks", lines.Field(first + 1, 2).ToString());
        Assert.Equal("はし", lines.Key(first).ToString());
        Assert.True(lines.Position(first) < lines.Position(first + 1), "本文の順");
        Assert.Equal(0, lines.Find("は").Count);
        Assert.Equal(0, lines.Find("はしご").Count);
        Assert.Equal(1, lines.Find("あ").Count);
    }

    [Test]
    public static void WordList_KeepsEveryPrefix()
    {
        var words = Enumerable.Range(0, 2000).Select(i => "w" + i.ToString("x", CultureInfo.InvariantCulture)).ToList();
        // 1 語ずつ足して prefix の表が何度も大きくなっても、まとめて渡して (同じ語が 2 度あっても) 頭の数から表を作っても、どの語のどの先頭も引ける
        var added = new WordList();
        foreach (var word in words) added.Add(word);
        foreach (var (name, list) in new[] { ("Add", added), ("まとめて", new WordList(words.Concat(words))) })
        {
            Assert.Equal(words.Count, list.Count);
            Assert.True(words.All(w => Enumerable.Range(1, w.Length).All(n => list.HasPrefix(w[..n]))), $"{name}: 語の先頭");
            Assert.True(words.All(list.ContainsWord), $"{name}: 語");
            Assert.True(!list.HasPrefix("") && !list.HasPrefix("x") && !list.HasPrefix("w7d0q"), $"{name}: 無い先頭");
            Assert.True(!list.ContainsWord("w"), $"{name}: 先頭だけの語");
        }
    }

    [Test]
    public static void Meanings_StemPicksTheWordWrittenFirst()
    {
        // 同じ語幹 (持) の語がいくつかあれば、本文で先に書いてある語の意味を出す
        Assert.Equal("A", MeaningDictionary.Parse("持つ\t\tA\n持る\t\tB\n").Lookup("持って", null));
        Assert.Equal("B", MeaningDictionary.Parse("持る\t\tB\n持つ\t\tA\n").Lookup("持って", null));
        Assert.Equal("① A\n② B", MeaningDictionary.Parse("持る\t\tA\n持つ\t\tC\n持る\t\tB\n").Lookup("持って", null));
        // 語そのものが辞書にあれば、語幹より先に使う
        Assert.Equal("C", MeaningDictionary.Parse("持つ\t\tA\n持\t\tC\n").Lookup("持って", null));
    }

    [Test]
    public static void Meanings_SelectReadingsBeforeLimitingSenses()
    {
        var meanings = MeaningDictionary.Parse("橋\t\t共通\n橋\tはし\t渡る\n橋\tはし\t架かる\n橋\tきょう\t脳\n");
        Assert.Equal("① 渡る\n② 架かる", meanings.Lookup("橋を", "はしを"));
        Assert.Equal("共通", meanings.Lookup("橋", "ほか"));
        var marked = MeaningDictionary.Parse("橋\tはし\t渡る\n橋\tきょう\t脳\n橋\tはし\t架かる\n");
        Assert.Equal("① 渡る\n② 脳", marked.Lookup("橋", "ほか"));
        Assert.Equal("① 渡る\n② 脳", marked.Lookup("橋", null));
        var oneMatch = MeaningDictionary.Parse("橋\tはし\t端\n橋\t\t印なし\n橋\tきょう\t京\n");
        Assert.Equal("① 京\n② 印なし", oneMatch.Lookup("橋", "きょう"));
    }

    [Test]
    public static void Meanings_DoNotUseKatakanaOnlyStems()
    {
        var meanings = MeaningDictionary.Parse("ナウい\t\t流行に合っている\n");
        Assert.Equal("流行に合っている", meanings.Lookup("ナウい", null));
        Assert.Equal(null, meanings.Lookup("ナウかった", null));
    }

    [Test]
    public static void Meanings_DoNotMatchPartOfAKanjiWord()
    {
        Assert.Equal(null, MeaningDictionary.Parse("橋\t\tA\n").Lookup("橋渡し", null));
        Assert.Equal(null, TranslationDictionary.Parse("橋\tn\tbridge\n").Meaning("橋渡し"));
    }

    [Test]
    public static void Translations_KeepKindOrderAndDropRepeatedWords()
    {
        var translations = TranslationDictionary.Parse("複雑\tna\tcomplex,complicated\n複雑\tn\tcomplexity\n複雑\tna\tcomplex\n複雑\tv\n");
        // な が付くなら な形容詞の訳を先に (同じ訳は 1 つ)。を が付くなら名詞を先にして、を を付けたまま
        Assert.Equal("complex|complicated|complexity", string.Join("|", translations.Lookup("複雑な", "")));
        Assert.Equal("complexityを|complexを|complicatedを", string.Join("|", translations.Lookup("複雑を", "")));
        Assert.Equal("complexityです|complexです|complicatedです", string.Join("|", translations.Lookup("複雑です", "")));
        // 助詞・だ・です などでないもの (複雑|ます) が続くなら出さない
        Assert.Equal(0, translations.Lookup("複雑ます", "").Count);
        Assert.Equal("complex, complicated, complexity", translations.Meaning("複雑を"));
    }

    [Test]
    public static void Translations_LimitDistinctWordsInPriorityOrder()
    {
        var translations = TranslationDictionary.Parse("複雑\tn\tA,B,A,C,D,E,F\n複雑\tna\tG,H,G,I,J,K,L\n");
        Assert.Equal("G|H|I|J|K", string.Join("|", translations.Lookup("複雑な", "")));
        Assert.Equal("A, B, C, D", translations.Meaning("複雑"));
    }

    [Test]
    public static void Candidates_KeepAddedOrderAndDropRepeatedWords()
    {
        var dictionary = new CandidateDictionary();
        dictionary.AddText("はし 橋 箸\nはし 端 橋\n");
        dictionary.Add("はし", ["箸", "嘴"]);
        dictionary.AddTabText("はし\t🌉\t🥢\n");
        Assert.Equal("橋|箸|端|嘴", string.Join("|", dictionary.LookupWords("はし")));
        Assert.Equal("🌉|🥢", string.Join("|", dictionary.LookupEmoji("はし")));
        Assert.Equal("橋を|箸を|端を|嘴を|🌉を|🥢を", string.Join("|", dictionary.Lookup("はしを")));
    }

    [Test]
    public static void RomajiTypos_KnowReadingsAndTheirForms()
    {
        var romaji = new RomajiDetector(null);
        // 読みそのもの・活用した形 (たべ、かき・かい、はやく)・付属語
        var typos = new RomajiTypoCorrector(romaji, "# 読み\nたべる\tv1\nかく\tv5k\nはやい\ti\nきょう\n");
        foreach (var word in (string[])["たべる", "たべ", "かく", "かき", "かか", "かい", "はやい", "はやく", "はやかっ", "きょう", "ます", "は"]) Assert.True(typos.IsWord(word), word);
        foreach (var word in (string[])["たべろ", "かくる", "きょ", "v1", "# 読み"]) Assert.True(!typos.IsWord(word), word);
    }
}
