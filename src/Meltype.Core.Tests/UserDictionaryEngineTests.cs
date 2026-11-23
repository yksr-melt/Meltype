// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Ime;

namespace Meltype.Tests;

/// <summary>ユーザー辞書の語を変換エンジン (Mozc) のユーザー辞書に渡す (#1262)。</summary>
internal static class UserDictionaryEngineTests
{
    [Test]
    public static void UserWords_AreNotSplitByCore()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("しの", "紫乃");
        Assert.True(dictionary.Split("わたしの") is null, "ユーザーの語は Mozc のコストで決める");
        Assert.Equal("紫乃", dictionary.Lookup("しの").Single());
    }

    [Test]
    public static void BuiltInPhrases_AreStillSplit()
    {
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.Split("いちがっき") is not null, "同梱の語句は、今までどおり Core が当てはめる");
    }

    [Test]
    public static void SetUserDictionary_SendsWordsAndAcceptsOnlyOk()
    {
        var helper = new FakeHelper("OK");
        using var mozc = new MozcConverter(helper);
        Assert.True(mozc.SetUserDictionary([("しの", "紫乃", "名詞"), ("き\tごう", "記号", "名詞")]), "OK なら受け取った");
        Assert.Equal("D\tしの\x1f紫乃\x1f名詞\x1eき ごう\x1f記号\x1f名詞", helper.Requests.Single());
        using var old = new MozcConverter(new FakeHelper(""));
        Assert.Equal(false, old.SetUserDictionary([("しの", "紫乃", "名詞")]), "要求を知らない前の版のヘルパー (空行) は受け取っていない");
    }

    [Test]
    public static void SetUserDictionary_ResentAfterHelperRestarts()
    {
        var helper = new FakeHelper("OK");
        using var mozc = new MozcConverter(helper);
        mozc.SetUserDictionary([("しの", "紫乃", "名詞")]);
        mozc.Reading("紫乃");
        Assert.Equal(2, helper.Requests.Count, "起動し直していなければ送り直さない: " + string.Join(" / ", helper.Requests));
        // ヘルパーが落ちて起動し直した (ユーザー辞書はメモリにだけ置いている)
        helper.Launches++;
        mozc.Reading("紫乃");
        Assert.Equal("D\tしの\x1f紫乃\x1f名詞|R\t紫乃", string.Join("|", helper.Requests.Skip(2)));
    }

    [Test]
    public static void Runtime_SendsUserWordsWhenStartedAndChanged()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-userdict-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var runtime = new MeltypeRuntime(directory);
            runtime.UserDictionary.Add("しの", "紫乃");
            var helper = new FakeHelper("OK");
            runtime.StartConverter(AppContext.BaseDirectory, helper);
            Assert.True(SpinWait.SpinUntil(() => helper.Dictionary().Contains("しの\x1f紫乃\x1f名詞"), 5000), "変換エンジンを作ったら、登録した語を名詞として渡す: " + helper.Dictionary());
            runtime.UserDictionary.Add("きごうとう", "記号等");
            Assert.True(SpinWait.SpinUntil(() => helper.Dictionary().Contains("きごうとう\x1f記号等"), 5000), "語が変わったら渡し直す: " + helper.Dictionary());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class FakeHelper(string answer) : IMozcHelper
    {
        private readonly object _gate = new();

        public List<string> Requests { get; } = [];

        /// <summary>最後に受け取ったユーザー辞書の要求。</summary>
        public string Dictionary()
        {
            lock (_gate) return Requests.LastOrDefault(r => r.StartsWith("D\t", StringComparison.Ordinal)) ?? "";
        }

        public bool IsInstalled => true;

        public int Launches { get; set; } = 1;

        public long WorkingSet => 0;

        public bool Start() => true;

        public string? Send(string request)
        {
            lock (_gate) Requests.Add(request);
            return answer;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}
