// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Ime;

namespace Meltype.Tests;

/// <summary>ユーザー辞書の語を変換エンジン (Mozc) のユーザー辞書に渡す (#1262)。</summary>
internal static class UserDictionaryEngineTests
{
    [Test]
    public static void BuiltInPhrases_AreGivenToTheEngine()
    {
        var dictionary = new UserDictionary(null);
        dictionary.Add("ほんあか", "本垢");
        var words = dictionary.EngineWords().Words;
        Assert.True(words.Contains(new UserWord("あげあしとる", "揚げ足取る", "動詞ラ行五段")), "同梱の語句を品詞と一緒に Mozc に渡す");
        Assert.Equal(1, words.Count(w => w is { Reading: "ほんあか", Word: "本垢" }), "ユーザーの語と同じ語句は 1 つだけ渡す");
        Assert.Equal("ほんあか", string.Join(" ", dictionary.Words.Select(w => w.Reading)), "同梱の語句はユーザー辞書の一覧に出さない");
        Assert.Equal(0, new UserDictionary(null, builtIn: false).EngineWords().Words.Count);
    }

    [Test]
    public static void UserReading_HidesTheSamePhrase()
    {
        // いちがっき → 壱学期 を登録すると、同梱の語句 いち|がっき → 一|学期 で上書きしていた
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.EngineWords().Words.Any(w => w.Word == "一学期"), "同梱の語句も渡す");
        dictionary.Add("いちがっき", "壱学期");
        Assert.True(dictionary.EngineWords().Words.Where(w => w.Reading == "いちがっき").Select(w => w.Word).SequenceEqual(["壱学期"]), "同じ読みの語句は渡さない");
        var k = new CompositionTests.Keyboard(converter: new Engine(new() { ["いちがっき"] = [new("いちがっき", "壱学期")] }), userDictionary: dictionary);
        k.Type("ichigakki ");
        Assert.Equal("壱学期", string.Concat(k.Host.View!.Clauses!));
    }

    [Test]
    public static void UserWordInsideAnEngineClause_KeepsTheEngineResult()
    {
        // #2019: にこ → (*>ω<*) を登録すると、にこみはんばーぐ が (*>ω<*)みハンバーグ になっていた。
        // #1011: へん を登録すると、へんかんこうりつがよくない の頭に当てはめて、残りを 観光率が良くない にしていた
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("にこ", "(*>ω<*)");
        dictionary.Add("へん", "返信が遅くなり、誠に申し訳ありません。");
        var engine = new Engine(new()
        {
            ["にこみはんばーぐ"] = [new("にこみ", "煮込み"), new("はんばーぐ", "ハンバーグ")],
            ["へんかんこうりつがよくない"] = [new("へんかんこうりつが", "変換効率が"), new("よくない", "良くない")],
            ["にこ"] = [new("にこ", "(*>ω<*)")],
        });
        var k = new CompositionTests.Keyboard(converter: engine, userDictionary: dictionary);
        k.Type("nikomihannba-gu ");
        Assert.Equal("煮込みハンバーグ", string.Concat(k.Host.View!.Clauses!));
        k = new CompositionTests.Keyboard(converter: engine, userDictionary: dictionary);
        k.Type("hennkannkouritugayokunai ");
        Assert.Equal("変換効率が良くない", string.Concat(k.Host.View!.Clauses!));
        k = new CompositionTests.Keyboard(converter: engine, userDictionary: dictionary);
        k.Type("niko ");
        Assert.Equal("(*>ω<*)", string.Concat(k.Host.View!.Clauses!), "登録した読みだけなら、変換エンジンが選んだ登録語");
    }

    [Test]
    public static void UserSymbolWord_IsNotTurnedBackIntoKana()
    {
        // 記号だけの変換結果 (から → ～) はかなに戻すが、ユーザー辞書の語 (なかてん → ・) は戻さない
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("なかてん", "・");
        var k = new CompositionTests.Keyboard(converter: new Engine(new() { ["なかてん"] = [new("なかてん", "・")] }), userDictionary: dictionary);
        k.Type("nakaten ");
        Assert.Equal("・", string.Concat(k.Host.View!.Clauses!));
    }

    [Test]
    public static void Zenzai_DoesNotReplaceUserWords()
    {
        // ヘルパーは、ユーザー辞書の語を含む候補 (活用した形も) の頭に GS を付けて返す
        var helper = new FakeHelper("OK") { Conversion = "とった\x1f\x1d撮った\x1f取った\x1eから\x1fから" };
        using var mozc = new MozcConverter(helper);
        var took = mozc.ConvertWithCandidatesNow("とったから")!;
        Assert.Equal("撮った:True:撮った,取った|から:False:から", string.Join("|", took.Select(s => $"{s.Clause.Text}:{s.Clause.UserWord}:{string.Join(",", s.Candidates)}")), "印を外した候補と、登録語の文節か");
        Assert.Equal("撮った|から", Text(HybridConverter.Choose("とったから", took, [new("とったから", "取ったから")])), "Mozc が選んだ登録語は、Zenzai の答えで置き換えない");

        IReadOnlyList<(ConversionClause, IReadOnlyList<string>)> plain = [(new("しの", "篠"), ["篠", "紫乃"])];
        Assert.Equal("紫乃", Text(HybridConverter.Choose("しの", plain, [new("しの", "紫乃")])), "登録語でなければ Zenzai の答え");
        IReadOnlyList<(ConversionClause, IReadOnlyList<string>)> sentence = [(new("しのが", "紫乃が") { UserWord = true }, ["紫乃が", "篠が"]), (new("きた", "来た"), ["来た", "北"])];
        Assert.Equal("紫乃が|北", Text(HybridConverter.Choose("しのがきた", sentence, [new("しのがきた", "紫乃が北")])), "ほかの文節は Zenzai の答え");
        Assert.Equal("紫乃が|来た", Text(HybridConverter.Choose("しのがきた", sentence, [new("しのがきた", "篠が北")])), "登録語を残して作れなければ Mozc の結果");

        static string Text(IReadOnlyList<ConversionClause>? clauses) => string.Join("|", clauses?.Select(c => c.Text) ?? []);
    }

    [Test]
    public static void SetUserDictionary_ResentAfterAFailedSend()
    {
        var helper = new FakeHelper("OK");
        helper.Next.Enqueue(null);
        using var mozc = new MozcConverter(helper);
        Assert.Equal(false, mozc.SetUserDictionary([("しの", "紫乃", "名詞")]), "答えなかった");
        mozc.Reading("紫乃");
        Assert.Equal("D\tしの\x1f紫乃\x1f名詞|D\tしの\x1f紫乃\x1f名詞|R\t紫乃", string.Join("|", helper.Requests), "次の要求の前に送り直す");
        mozc.Reading("紫乃");
        Assert.Equal(4, helper.Requests.Count, "受け取ったらもう送らない");
    }

    [Test]
    public static void FailedResend_DoesNotCacheResultsWithoutTheDictionary()
    {
        var helper = new FakeHelper("OK") { Conversion = "しの\x1f紫乃" };
        using var mozc = new MozcConverter(helper);
        mozc.SetUserDictionary([("しの", "紫乃", "名詞")]);
        // ヘルパーが落ちて起動し直し、送り直したユーザー辞書に答えなかった
        helper.Launches++;
        helper.Next.Enqueue(null);
        Assert.True(mozc.ConvertClauses("しの", null) is null, "ユーザー辞書の無いヘルパーには変換を頼まない");
        Assert.Equal("紫乃", mozc.ConvertText("しの", null) ?? "", "次の変換で送り直してから頼む");
        Assert.Equal("D|D|D|C", string.Join("|", helper.Requests.Select(r => r[..1])));
    }

    [Test]
    public static void FailedUpdate_DoesNotKeepResultsOfTheOldWords()
    {
        var helper = new FakeHelper("OK") { Conversion = "しの\x1f篠" };
        using var mozc = new MozcConverter(helper);
        Assert.Equal("篠", mozc.ConvertText("しの", null) ?? "");
        helper.Next.Enqueue(null);
        Assert.Equal(false, mozc.SetUserDictionary([("しの", "紫乃", "名詞")]), "答えなかった");
        helper.Conversion = "しの\x1f紫乃";
        Assert.Equal("紫乃", mozc.ConvertText("しの", null) ?? "", "前の語での結果を控えから使わず、送り直してから頼む");
        Assert.Equal("C|D|D|C", string.Join("|", helper.Requests.Select(r => r[..1])));
    }

    private sealed class Engine(Dictionary<string, ConversionClause[]> answers) : TestConverter
    {
        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) => answers.GetValueOrDefault(hiragana);
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
            runtime.UserDictionary.AddRange([new UserWord("ぐぐる", "ググる", "動詞ラ行五段")]);
            Assert.True(SpinWait.SpinUntil(() => helper.Dictionary().Contains("ぐぐる\x1fググる\x1f動詞ラ行五段"), 5000), "登録した品詞を渡す: " + helper.Dictionary());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void Mozc_ThatGaveUp_TriesAgainAfterAWhile()
    {
        // 一時的に答えなかっただけで、再起動までかなのままにしない
        var helper = new FakeHelper("OK");
        for (var i = 0; i < 3; i++) helper.Next.Enqueue(null);
        using var mozc = new MozcConverter(helper) { RetryAfterMs = 200 };
        for (var i = 0; i < 3; i++) mozc.Reading("漢字" + i);
        Assert.True(!mozc.IsAvailable, "3 回答えなかったら、しばらく諦める");
        Thread.Sleep(300);
        Assert.True(mozc.IsAvailable, "しばらくしたら、もう一度使う");
        helper.Next.Enqueue(null);
        mozc.Reading("漢字");
        Assert.True(!mozc.IsAvailable, "試して答えなければ、また待つ");
        Thread.Sleep(300);
        mozc.Reading("漢字");
        Assert.True(mozc.IsAvailable, "答えたら元に戻る");
        Thread.Sleep(300);
        Assert.True(mozc.IsAvailable, "元に戻ったあとは、待った時間にかかわらず使える");
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

        /// <summary>次に返す答え (空なら answer)。</summary>
        public Queue<string?> Next { get; } = new();

        /// <summary>変換 (C) の答え。null なら answer。</summary>
        public string? Conversion { get; set; }

        public string? Send(string request)
        {
            lock (_gate)
            {
                Requests.Add(request);
                return Next.Count > 0 ? Next.Dequeue() : Conversion is not null && request.StartsWith("C\t", StringComparison.Ordinal) ? Conversion : answer;
            }
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}
