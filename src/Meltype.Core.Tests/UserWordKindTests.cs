// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>ユーザー辞書の品詞 (issue #487)。</summary>
internal static class UserWordKindTests
{
    private static string? Split(UserDictionary dictionary, string kana) =>
        dictionary.Split(kana) is { } pieces ? string.Join("|", pieces.Select(p => p.Word ?? p.Reading)) : null;

    [Test]
    public static void NounLikeWords_AreUsedOnlyBeforeParticlesOrHonorifics()
    {
        // #487: ちな → 千奈 を登録すると、ちなみに が 千奈ミニ になっていた
        var dictionary = new UserDictionary(null);
        dictionary.Add("ちな", "千奈", UserWordKind.PersonName);
        Assert.True(Split(dictionary, "ちなみに") is null, "ほかの語の途中では使わない");
        Assert.Equal("千奈", Split(dictionary, "ちな"), "読みだけ");
        Assert.Equal("千奈|さんがきた", Split(dictionary, "ちなさんがきた"), "敬称の前");
        Assert.Equal("千奈|はがくせい", Split(dictionary, "ちなはがくせい"), "助詞の前");
        Assert.Equal("きょうは|千奈|と", Split(dictionary, "きょうはちなと"), "文の途中でも、後ろが助詞なら使う");

        // 指定なし (今までどおり) は、どこでも使う
        var plain = new UserDictionary(null);
        plain.Add("ちな", "千奈");
        Assert.Equal("千奈|みに", Split(plain, "ちなみに"), "品詞を決めていない語は今までどおり");
    }

    [Test]
    public static void Abbreviation_IsUsedOnlyForTheWholeInput()
    {
        var dictionary = new UserDictionary(null);
        dictionary.Add("おせ", "お世話になっております", UserWordKind.Abbreviation);
        Assert.Equal("お世話になっております", Split(dictionary, "おせ"));
        Assert.True(Split(dictionary, "おせじ") is null, "ほかの語の中では使わない");
    }

    [Test]
    public static void Kind_IsSavedReadAndImported()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-userdict-{Guid.NewGuid():N}.txt");
        try
        {
            var dictionary = new UserDictionary(path, builtIn: false);
            dictionary.Add("ちな", "千奈", UserWordKind.PersonName);
            dictionary.Add("きごうとう", "記号等");
            var lines = File.ReadAllLines(path);
            Assert.True(lines.Contains("ちな\t千奈\t人名"), "品詞は 3 列目に書く");
            Assert.True(lines.Contains("きごうとう\t記号等"), "品詞を決めていない語は今までどおり 2 列");
            var reloaded = new UserDictionary(path, builtIn: false);
            Assert.Equal(UserWordKind.PersonName, reloaded.Words.Single(w => w.Word == "千奈").Kind, "読み直しても品詞を保つ");
            Assert.Equal(UserWordKind.None, reloaded.Words.Single(w => w.Word == "記号等").Kind);
        }
        finally
        {
            File.Delete(path);
        }

        // Microsoft IME・Google 日本語入力の品詞を取り込む。人名・地名・短縮よみだけを読み、名詞・固有名詞 (Microsoft IME はほとんどの語を名詞で書き出す)・動詞などは指定なし
        var imported = UserDictionaryFile.Parse(Encoding.UTF8.GetBytes("ちな\t千奈\t人名\nしぶや\t渋谷\t地名\nおせ\tお世話になっております\t短縮よみ\nめるたいぷ\tMeltype\t固有名詞\nきごうとう\t記号等\t名詞\nかく\t書く\t動詞カ行五段\nきごう\t記号\n")).Words;
        Assert.Equal("PersonName,PlaceName,Abbreviation,None,None,None,None", string.Join(",", imported.Select(w => w.Kind)));
        // 書き出しは、品詞を決めていない語を名詞にする
        var exported = Encoding.Unicode.GetString(UserDictionaryFile.Export([new("ちな", "千奈", UserWordKind.PersonName), new("きごう", "記号")]));
        Assert.True(exported.Contains("ちな\t千奈\t人名") && exported.Contains("きごう\t記号\t名詞"), exported);
    }
}
