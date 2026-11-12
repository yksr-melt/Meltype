// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>ユーザー辞書の語を変換エンジンに任せる (issue #1262・#487)。</summary>
internal static class UserWordsInEngineTests
{
    /// <summary>ユーザー辞書の語を持てる変換エンジン。持った語は読みと完全に一致するときだけ使い、ちなみに は 因みに にする (Mozc のコストの代わり)。</summary>
    private sealed class EngineWithUserWords(bool accepts) : IKanjiConverter, IUserWordConverter
    {
        public Dictionary<string, string> Words { get; } = new(StringComparer.Ordinal);
        public int Calls { get; private set; }

        public bool SetUserWords(IReadOnlyList<(string Reading, string Word)> words, int version)
        {
            Calls++;
            if (!accepts) return false;
            Words.Clear();
            foreach (var (reading, word) in words) Words[reading] = word;
            return true;
        }

        public string? Convert(string hiragana) => ConvertClauses(hiragana) is { } c ? string.Concat(c.Select(x => x.Text)) : null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => hiragana switch
        {
            "ちなみに" => [new("ちなみに", "因みに")],
            "ちなさんが" when Words.ContainsKey("ちな") => [new("ちな", Words["ちな"]), new("さんが", "さんが")],
            _ when Words.TryGetValue(hiragana, out var word) => [new(hiragana, word)],
            _ => null,
        };
    }

    private static string Convert(EngineWithUserWords engine, UserDictionary dictionary, string keys)
    {
        var k = new CompositionTests.Keyboard(converter: engine, userDictionary: dictionary);
        k.Type(keys + " ");
        return k.Host.View!.Text;
    }

    [Test]
    public static void UserWords_AreLeftToTheEngine()
    {
        // #487: ちな → 千奈 を登録しても、ちなみに の中の ちな では区切らない (変換エンジンが 因みに を選ぶ)
        var dictionary = new UserDictionary(null);
        dictionary.Add("ちな", "千奈");
        var engine = new EngineWithUserWords(accepts: true);
        Assert.Equal("因みに", Convert(engine, dictionary, "tinamini"));
        Assert.Equal("千奈", engine.Words["ちな"], "ユーザー辞書の語を変換エンジンに入れる");
        Assert.Equal("千奈さんが", Convert(engine, dictionary, "tinasannga"), "変換エンジンが区切ったときは使う");

        // 辞書が変わらなければ入れ直さない。変わったら入れ直す
        var calls = engine.Calls;
        Convert(engine, dictionary, "tina");
        Assert.True(engine.Calls > calls, "変換を始めるたびに版を確かめる");
        dictionary.Add("しの", "紫乃");
        Convert(engine, dictionary, "sino");
        Assert.Equal("紫乃", engine.Words["しの"], "登録した語を入れ直す");
    }

    [Test]
    public static void UserWords_FallBackToSplitting_WhenTheEngineCannotHoldThem()
    {
        // Microsoft IME・前の版のヘルパー: 今までどおり Meltype が読みを当てはめる
        var dictionary = new UserDictionary(null);
        dictionary.Add("ちな", "千奈");
        var engine = new EngineWithUserWords(accepts: false);
        Assert.Equal("千奈みに", Convert(engine, dictionary, "tinamini"));
        Assert.True(!dictionary.UserWordsInEngine, "任せていない");
    }
}
