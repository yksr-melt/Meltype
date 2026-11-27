// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Net;
using System.Text;
using System.Text.Json;
using Meltype.Config;
using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Ime;
using Meltype.Input;
using Meltype.Learning;

namespace Meltype.Tests;

/// <summary>Zenzai (llama-server の /completion) の変換。本物のサーバーの代わりに、同じ形で答える小さな HTTP サーバーを動かす。</summary>
internal static class ZenzaiConverterTests
{
    /// <summary>受け取った prompt を記録して、answer(prompt) を content にして返す。status が 200 以外ならそのエラーを返す。</summary>
    private sealed class FakeServer : IDisposable
    {
        private readonly HttpListener _listener;
        public List<string> Prompts { get; } = [];
        public int Status { get; set; } = 200;
        public Func<string, string> Answer { get; set; } = _ => "";

        /// <summary>指定すると、Answer の代わりに、これを本文としてそのまま返す (壊れた答えの試し)。</summary>
        public string? RawBody { get; set; }

        /// <summary>答えの 1 文字ごとの確率 (本物のサーバーは completion_probabilities で返す)。</summary>
        public double Probability { get; set; } = 0.9;
        public Uri Endpoint { get; }

        public FakeServer()
        {
            for (var tries = 0; tries < 20; tries++)
            {
                // ephemeral port (Linux は 32768〜、Windows・macOS は 49152〜) と重ならない範囲から選ぶ (外向きの接続が使っているポートを引かないように)
                var port = Random.Shared.Next(20000, 32768);
                // Start に失敗した (ポートが使われていた) HttpListener は、Linux などでは閉じられて使えなくなる。
                // 同じものに次のポートを足すと ObjectDisposedException になってテストごと落ちていた (#1178) ので、試すたびに作り直す
                _listener = new HttpListener();
                try
                {
                    _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    _listener.Start();
                    Endpoint = new Uri($"http://127.0.0.1:{port}/");
                    Task.Run(Loop);
                    return;
                }
                catch (HttpListenerException) { _listener.Close(); }
            }
            // どのポートでも始められなければ、ここで分かるように落とす (始めていないサーバーに頼んで、別の理由で落ちたように見えないように)
            throw new InvalidOperationException("テスト用の llama-server をどのポートでも始められませんでした");
        }

        private void Loop()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    var body = new StreamReader(context.Request.InputStream, Encoding.UTF8).ReadToEnd();
                    var prompt = "";
                    if (body.Length > 0) prompt = JsonElement.Parse(body).GetProperty("prompt").GetString() ?? "";
                    lock (Prompts) Prompts.Add(prompt);
                    context.Response.StatusCode = Status;
                    var answer = Answer(prompt);
                    var probabilities = answer.Select(ch => new { content = ch.ToString(), probs = new[] { new { tok_str = ch.ToString(), prob = Probability } } }).ToArray();
                    var bytes = Encoding.UTF8.GetBytes(RawBody ?? JsonSerializer.Serialize(new { content = answer, completion_probabilities = probabilities }));
                    context.Response.OutputStream.Write(bytes);
                    context.Response.Close();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
            }
        }

        public void Dispose() => _listener.Close();
    }

    [Test]
    public static void Prompt_UsesKatakanaAndContext()
    {
        Assert.Equal("キョウ", ZenzaiConverter.Prompt("きょう", null), "文脈なし");
        Assert.Equal("この本はアツイ", ZenzaiConverter.Prompt("あつい", "この本は"), "v3 では文脈を前に置く");
        Assert.Equal("ー、。ABC", ZenzaiConverter.ToKatakana("ー、。ABC"), "かな以外はそのまま");
        // #2231: zenz は全角の記号を読めず、＊にほんご＊ を ô日本語瑤 にする
        Assert.Equal("10%#ハッシュハ*", ZenzaiConverter.Prompt("＃はっしゅは＊", "１０％"), "全角の英数字・記号は半角");
    }

    [Test]
    public static void Clean_RemovesEndMarks()
    {
        Assert.Equal("今日", ZenzaiConverter.Clean("今日</s>")!, "EOS");
        Assert.Equal("今日", ZenzaiConverter.Clean(" 今日ゴミ")!, "区切り記号以降");
        Assert.Equal("辞書の", ZenzaiConverter.Clean("辞書の\uEE08前の文章")!, "境界記号以降");
        Assert.True(ZenzaiConverter.Clean("</s>") is null, "空は null");
    }

    [Test]
    public static void IsPlausible_RejectsRunawayRepeats()
    {
        // 答えのかなが読みと合わないものは使わない
        // #1675: はーどうぇ (打ちかけ) を ハードウェアウェア… にした答え (繰り返しの回数によらない)
        Assert.True(!ZenzaiConverter.IsPlausible("はーどうぇ", "ハードウェアウェアウェアウェアウェアウェア"), "繰り返し続けた答え");
        Assert.True(!ZenzaiConverter.IsPlausible("はーどうぇ", "ハードウェアウェア"), "2 回の繰り返し");
        // #1511: きょうはで の後ろに で が続く答え・直前の文節の混入
        Assert.True(!ZenzaiConverter.IsPlausible("きょうはで", "今日はでで"), "同じ字が続く答え");
        Assert.True(!ZenzaiConverter.IsPlausible("なまえ", "名前は魚の"), "直前の文節の混入");
        Assert.True(!ZenzaiConverter.IsPlausible("はははははは", "ははははは"), "1 文字落ちた答え");
        // 読みと合う答えは使う
        Assert.True(ZenzaiConverter.IsPlausible("はーどうぇ", "ハードウェ"), "打ちかけのまま");
        Assert.True(ZenzaiConverter.IsPlausible("きょうはで", "今日はで"), "今日はで");
        Assert.True(ZenzaiConverter.IsPlausible("はたはたはた", "ハタハタハタ"), "読みにある繰り返し");
        Assert.True(ZenzaiConverter.IsPlausible("すごーーーーい", "すごーーーーい"), "長音の連続");
        Assert.True(ZenzaiConverter.IsPlausible("まいくろそふと", "Microsoft"), "英字の並び");
        Assert.True(ZenzaiConverter.IsPlausible("ぐーぐるでけんさく", "Googleで検索"), "英字と漢字");
        Assert.True(ZenzaiConverter.IsPlausible("きょうはいいてんき", "今日はいい天気"), "漢字");
        Assert.True(ZenzaiConverter.IsPlausible("さんかげつ", "三ヶ月"), "ヶ は漢字と同じ");
        Assert.True(ZenzaiConverter.IsPlausible("ほんとう?", "本当？"), "全角の記号");
        Assert.True(ZenzaiConverter.IsPlausible("2026ねん", "2026年"), "数字");
    }

    [Test]
    public static void Answers_ThatLoseTypedSymbols_AreNotUsed()
    {
        // 漢字・英字を読みの何文字にも当てて比べるので、打った記号が消えた答えを使っていた
        foreach (var (reading, answer) in new[] { ("わたしは、りんごを、たべた。", "私は、林檎を食べた。"), ("これは、ほん。", "これは、本"), ("わたしは「ほん」をよむ", "私は本を読む"), ("ぐーぐる、でけんさく", "Googleで検索") })
        {
            Assert.True(ZenzaiConverter.IsPlausible(reading, answer), answer + ": かなは読みと合う");
            Assert.True(!ZenzaiConverter.KeepsSymbols(reading, answer), answer + ": 記号が消えている");
        }
        foreach (var (reading, answer) in new[] { ("わたしは、りんごを、たべた。", "私は、林檎を、食べた。"), ("わたしは「ほん」をよむ!", "私は「本」を読む！"), ("すごーい～", "すごーい～"), ("さんかげつ", "三ヶ月") })
            Assert.True(ZenzaiConverter.KeepsSymbols(reading, answer), answer);

        IReadOnlyList<(ConversionClause, IReadOnlyList<string>)> mozc = [(new("りんごを", "林檎を"), ["林檎を"]), (new("、", "、"), ["、", "，"]), (new("たべた", "食べた"), ["食べた"])];
        Assert.Equal("林檎を|、|食べた", Joined("林檎を食べた"), "記号が消えた答えでなく Mozc の答え");
        Assert.Equal("リンゴを、喰べた", Joined("リンゴを、喰べた"), "記号が残っていれば Zenzai の答え");

        string Joined(string answer) => string.Join("|", HybridConverter.Choose("りんごを、たべた", mozc, [new("りんごを、たべた", answer)])!.Select(c => c.Text));
    }

    [Test]
    public static void LearnedHallucinations_AreForgottenWithMozcCandidates()
    {
        // 前の版が学習した Zenzai の壊れた答えは消し、表記が読みと違う Mozc の候補・ユーザー辞書の語は残す (issue #1779)。
        // 偽の Mozc のヘルパーは、きずくのがはやい の文節ごとの候補に 気づくのが・早い があり、ほかの読みは読みのまま返す
        var directory = Path.Combine(Path.GetTempPath(), $"meltype-hallucinations-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string[][] conversions =
            [
                ["はーどうぇ", "ハードウェアウェア"], ["きょうはで", "今日はでで"],
                ["きずくのがはやい", "気づくのが早い"], ["おせわ", "お世話になっております"], ["はーどうぇあ", "ハードウェア"], ["ほし", "☆"],
            ];
            File.WriteAllText(Path.Combine(directory, "conversions.json"),
                "{" + string.Join(",", conversions.Select(e => $"\"{e[0]}\":{{\"Text\":\"{e[1]}\",\"Used\":\"2026-11-01T00:00:00Z\"}}")) + "}");
            File.WriteAllLines(Path.Combine(directory, "phrases.txt"),
            [
                "なまえは\t名前は魚のは\t3\t0", "なまえは\t名前は\t3\t0", "きずくのがはやい\t気づくのが早い\t3\t0",
                "おせわに\tお世話になっております\t3\t0", "おせわになっております\tお世話になっております\t3\t0",
                "わたしは、いく\t私は，行く\t3\t0",
            ]);
            File.WriteAllText(Path.Combine(directory, "userdict.txt"), "おせわ\tお世話になっております\n");
            using var runtime = new MeltypeRuntime(directory);
            using var mozc = new MozcConverter(new MozcHelperProcess(Environment.ProcessPath!, Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile)));
            var hybrid = new HybridConverter(mozc);

            // 裏のスレッドで Mozc に聞き終えると、全部まとめて消す (予測変換の語句は最後に消す)
            runtime.ForgetZenzaiHallucinations(hybrid);
            for (var i = 0; i < 300 && runtime.Phrases.Entries().Any(e => e is ("なまえは", "名前は魚のは") or ("おせわに", "お世話になっております")); i++) Thread.Sleep(50);
            foreach (var e in conversions.Take(2)) Assert.True(runtime.History.Get(e[0]) is null, $"{e[0]} → {e[1]} は消す");
            foreach (var e in conversions.Skip(2)) Assert.Equal(e[1], runtime.History.Get(e[0]), $"{e[0]} → {e[1]} は残す");
            Assert.Equal("名前は", string.Join(",", runtime.Phrases.StartingWith("なま")), "予測変換の語句も消す");
            Assert.Equal("気づくのが早い", string.Join(",", runtime.Phrases.StartingWith("きず")), "Mozc の候補で作れる語句は残す");
            Assert.Equal("私は，行く", string.Join(",", runtime.Phrases.StartingWith("わたし")), "設定の句読点で覚えた語句は残す");
            Assert.Equal("おせわになっております", string.Join(",", runtime.Phrases.Entries().Where(e => e.Text == "お世話になっております").Select(e => e.Reading)),
                "壊れた答えと同じ語句を正しい読みで覚えたものは残す");

            // Mozc が答えないときは、Mozc の候補で作れるか分からないので消さない
            using var missing = new MozcConverter(new MozcHelperProcess(Path.Combine(directory, "missing-helper"), null));
            Assert.True(new HybridConverter(missing).IsBuiltFromMozcCandidatesNow("きょうはで", "今日はでで") is null, "Mozc が無い");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void Answers_AreCheckedWithMozcCandidates()
    {
        // 偽の Mozc のヘルパーは、きずくのがはやい を 築くのが|早い で返し、文節ごとの候補に 気づくのが・速い もある。ほかの読みは読みのまま返す
        var profile = Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile);
        using var mozc = new MozcConverter(new MozcHelperProcess(Environment.ProcessPath!, profile));
        using var server = new FakeServer
        {
            Answer = p => p.Contains("キズクノガハヤイ") ? "気づくのが早い" : p.Contains("ハードウェ") ? "ハードウェアウェアウェア" : p.Contains("ナマエ") ? "名前は魚の" : ReadingIn(p),
        };
        using var zenzai = new ZenzaiConverter(server.Endpoint, timeoutMs: 5000);
        var hybrid = new HybridConverter(mozc, () => zenzai);
        mozc.WarmUp();
        for (var i = 0; i < 200 && mozc.ConvertClauses("きずくのがはやい", null) is null; i++) Thread.Sleep(50);
        Assert.True(mozc.ConvertClauses("きずくのがはやい", null) is [_, _], "偽の Mozc のヘルパーが 10 秒で起動しなかった");

        // かなが読みと合わない答え (ず の読みの づ) も、Mozc の候補で作れるなら使う
        var variant = hybrid.ConvertClauses("きずくのがはやい", "前の文");
        Assert.True(variant is [{ Text: "気づくのが" }, { Text: "早い" }], "Mozc の候補にある表記: " + Joined(variant));
        Assert.Equal("気づくのが早い", hybrid.ConvertText("きずくのがはやい", null) ?? "", "その読みだけの変換も同じ");
        // Mozc の候補で作れず、かなが読みと合わない答え (#1675・#1511) は、Zenzai が答えても使わず、Mozc に任せる
        Assert.Equal("ハードウェアウェアウェア", Answered("はーどうぇ", "前の文"), "Zenzai は答えをそのまま返す");
        Assert.True(hybrid.ConvertClauses("はーどうぇ", "前の文") is [{ Text: "はーどうぇ" }], "繰り返し続けた答え");
        Assert.Equal("名前は魚の", Answered("なまえ", "前の文"), "Zenzai は答えをそのまま返す");
        Assert.True(hybrid.ConvertClauses("なまえ", "前の文") is [{ Text: "なまえ" }], "直前の文節の混入");
        Assert.Equal("名前は魚の", Answered("なまえ", null), "Zenzai は答えをそのまま返す");
        Assert.Equal("なまえ", hybrid.ConvertText("なまえ", null) ?? "", "その読みだけの変換も Mozc に任せる");

        // Zenzai が答えるまで待って、その答え (覚えているので、HybridConverter が聞くと同じ答えが返る)
        string? Answered(string reading, string? context)
        {
            for (var i = 0; i < 200; i++)
            {
                if (zenzai.ConvertClauses(reading, context) is [var clause]) return clause.Text;
                Thread.Sleep(50);
            }
            return null;
        }
    }

    [Test]
    public static void Context_IsTheTextBeforeTheCaret_EnglishIncluded()
    {
        using var server = new FakeServer { Answer = _ => "答え</s>" };
        using var zenzai = new ZenzaiConverter(server.Endpoint);
        // Zenzai に渡した文脈 (文の U+EE02 から、読みの U+EE00 の前まで)
        string ContextOf(string reading, string? before)
        {
            SpinWait.SpinUntil(() => zenzai.ConvertClauses(reading, before) is not null, 5000);
            var prompt = server.Prompts.Last(p => ReadingIn(p) == reading);
            return prompt[(prompt.IndexOf('\uEE02') + 1)..prompt.IndexOf('\uEE00')];
        }
        // 英単語の後ろの「に」は助詞 (GitHub + にわたるこうじ → に渡る工事)。英語の文脈を捨てると、Zenzai は 庭田るこうじ と読む
        Assert.Equal("GitHub", ContextOf("にわたるこうじ", "GitHub "), "英語もそのまま");
        Assert.Equal("昨日は雨でしたが、今日は", ContextOf("きょう", "昨日は雨でしたが、\n今日は"), "改行を除いて文をまたぐ");
        Assert.Equal("。", ContextOf("にわにはながさいている", null), "前の文脈が無ければ文の始まり");
    }

    [Test]
    public static void JapaneseAfterEnglish_InTheSameComposition_GetsTheEnglishAsContext()
    {
        // github と続けて打った についてはなす は、github の後ろとして Zenzai に頼む (入力欄のキャレットの前だけを渡すと、「に」を語の頭と読む)
        using var server = new FakeServer { Answer = p => ReadingIn(p) == "についてはなす" ? "について話す</s>" : ReadingIn(p) };
        using var zenzai = new ZenzaiConverter(server.Endpoint, timeoutMs: 5000);
        using var mozc = MozcConverterTests.Fake();
        var session = new TestSession(() => new Settings { LiveConversion = true }, new HybridConverter(mozc, () => zenzai));
        SessionFacadeTests.Type(session, "githubnitsuitehanasu", before: "昨日は");
        SpinWait.SpinUntil(() => server.Prompts.Any(p => ReadingIn(p) == "についてはなす"), 5000);
        var asked = server.Prompts.Where(p => ReadingIn(p) == "についてはなす").ToList();
        Assert.True(asked.Count > 0 && asked.All(p => p.StartsWith("昨日はGitHub", StringComparison.Ordinal)), "依頼: " + string.Join(" / ", asked));
    }

    [Test]
    public static void Converts_ThroughCompletionEndpoint()
    {
        using var server = new FakeServer { Answer = p => p.Contains("キョウ") ? "今日</s>" : "" };
        using var zenzai = new ZenzaiConverter(server.Endpoint);
        var clauses = zenzai.ConvertClauses("きょう", "朝");
        Assert.True(clauses is [{ Reading: "きょう", Text: "今日" }], "1 つの文節で返る");
        Assert.True(server.Prompts.Count == 1 && server.Prompts[0] == "朝キョウ", "渡した文: " + string.Join("|", server.Prompts));
        zenzai.ConvertClauses("きょう", "朝");
        Assert.Equal(1, server.Prompts.Count, "同じ読みと文脈は覚えている");
    }

    private static (TestSession Session, MozcConverter Mozc, ZenzaiConverter Zenzai, FakeServer Server, ManualResetEventSlim Release) LateZenzai()
    {
        var release = new ManualResetEventSlim();
        // 止めるのは きしゃがきた だけ (止めたままだと、時間切れで Zenzai が 5 秒休む)
        var server = new FakeServer
        {
            Answer = p =>
            {
                if (!p.Contains("キシャガキタ")) return ReadingIn(p);
                release.Wait(10000);
                return "汽車が来た</s>";
            },
        };
        var zenzai = new ZenzaiConverter(server.Endpoint);
        var mozc = new MozcConverter(new MozcHelperProcess(Environment.ProcessPath!, Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile)));
        var hybrid = new HybridConverter(mozc, () => zenzai);
        var session = new TestSession(() => new Settings { LiveConversion = true }, hybrid, lateResults: true);
        // Mozc の起動を待つ
        mozc.ConvertClauses("あ", null, deadline: Environment.TickCount64 + 10000);
        return (session, mozc, zenzai, server, release);
    }

    [Test]
    public static void LateZenzai_ReplacesTheMozcAnswer_InLiveConversion()
    {
        var (session, mozc, zenzai, server, release) = LateZenzai();
        using (server) using (zenzai) using (mozc) using (release)
        {
            var seen = new List<string> { SessionFacadeTests.Type(session, "kishagakita")[^1].View!.Text };
            void Record() { if (session.RefreshConversion()?.View?.Text is { } text && text != seen[^1]) seen.Add(text); }
            Assert.True(SpinWait.SpinUntil(() => { Record(); return seen[^1] == "記者が来た"; }, 5000), "先に Mozc の答え: " + string.Join(" -> ", seen));
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => { Record(); return seen[^1] == "汽車が来た"; }, 5000), "あとから Zenzai の答え: " + string.Join(" -> ", seen));
            Thread.Sleep(200);
            Record();
            // 打ち終わる前に Mozc のワーカーが答え終われば、最初の表示がもう Mozc の答えになる (速いマシン)。かなの表示は有っても無くてもよい
            Assert.Equal("記者が来た -> 汽車が来た", string.Join(" -> ", seen.SkipWhile(text => text == "きしゃがきた")), "戻らない");
            // Mozc の文節の区切りのまま (Align) なので、文節ごとに選び直せる
            Assert.Equal("汽車が,来た", string.Join(",", SessionFacadeTests.Type(session, " ")[^1].View?.Clauses ?? []));
        }
    }

    [Test]
    public static void LateZenzai_ReplacesAnUntouchedOpenConversion()
    {
        var (session, mozc, zenzai, server, release) = LateZenzai();
        using (server) using (zenzai) using (mozc) using (release)
        {
            SessionFacadeTests.Type(session, "kishagakita");
            Assert.True(SpinWait.SpinUntil(() => session.RefreshConversion()?.View?.Text == "記者が来た", 5000), "先に Mozc の答え");
            var opened = SessionFacadeTests.Type(session, " ")[^1].View;
            Assert.Equal("記者が来た", opened?.Text, "Zenzai を待ちきれずに Mozc の答えで開く");
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => session.RefreshConversion()?.View?.Text == "汽車が来た", 5000), "触っていなければ Zenzai の答えに置き換わる");
        }
    }

    [Test]
    public static void LateZenzai_KeepsTheChosenCandidateOfAnOpenConversion()
    {
        var (session, mozc, zenzai, server, release) = LateZenzai();
        using (server) using (zenzai) using (mozc) using (release)
        {
            SessionFacadeTests.Type(session, "kishagakita");
            Assert.True(SpinWait.SpinUntil(() => session.RefreshConversion()?.View?.Text == "記者が来た", 5000), "先に Mozc の答え");
            SessionFacadeTests.Type(session, " ");
            var first = session.HandleKey(VirtualKeys.Left, null, false, false, false, false).View;
            Assert.Equal(0, first?.SelectedClause, "Zenzai と異なる先頭の文節を選ぶ");
            // Zenzai の答え (汽車が) と違う候補まで選び直す
            session.HandleKey(VirtualKeys.Down, null, false, false, false, false);
            var chosen = session.HandleKey(VirtualKeys.Down, null, false, false, false, false).View?.Text;
            Assert.True(chosen is not (null or "記者が来た" or "汽車が来た"), $"Mozc の 1 番目とも Zenzai の答えとも違う: {chosen}");
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => zenzai.HasAnswered, 5000), "Zenzai が答える");
            Assert.Equal(chosen, session.RefreshConversion()?.View?.Text ?? chosen, "選び直した候補は Zenzai の答えで上書きしない");
        }
    }

    [Test]
    public static void Deadline_AnswersFromTheWorker_AndNotifies()
    {
        using var server = new FakeServer { Answer = p => p.Contains("キョウ") ? "今日</s>" : "" };
        using var zenzai = new ZenzaiConverter(server.Endpoint);
        using var ready = new ManualResetEventSlim();
        zenzai.ResultReady += ready.Set;

        Assert.True(zenzai.ConvertClauses("きょう", "朝", deadline: 0) is null, "答えが無ければ待たない");
        Assert.True(ready.Wait(5000), "答えが届いたら知らせる");
        Assert.True(zenzai.ConvertClauses("きょう", "朝", deadline: 0) is [{ Text: "今日" }], "届いた答えを返す");
        Assert.Equal(1, server.Prompts.Count, "同じ要求を 2 回送らない");

        // 答えが要るときは Deadline まで待つ
        server.Answer = _ => "明日</s>";
        Assert.True(zenzai.ConvertClauses("あした", "朝", deadline: Environment.TickCount64 + 3000) is [{ Text: "明日" }], "Deadline までは待って答える");
    }

    [Test]
    public static void Deadline_DoesNotNotifyWhenThereIsNoNewText()
    {
        using var server = new FakeServer { Answer = p => "かんたん</s>" };
        using var zenzai = new ZenzaiConverter(server.Endpoint);
        var notified = 0;
        zenzai.ResultReady += () => Interlocked.Increment(ref notified);
        // 読みのまま (変換していない) の答えは表示を変えない
        Assert.True(zenzai.ConvertClauses("かんたん", null, deadline: Environment.TickCount64 + 3000) is null, "変換していない");
        Assert.Equal(0, Volatile.Read(ref notified));
    }

    [Test]
    public static void NoContext_UsesNeutralSentenceStart()
    {
        // 前の文脈が無い入力は、モデルが迷う (文脈なしの きょう → 強) ので、文の始まりの合図「。」を文脈として渡す
        using var server = new FakeServer { Answer = p => "今日" };
        using var zenzai = new ZenzaiConverter(server.Endpoint);
        Assert.True(zenzai.ConvertClauses("きょう", null) is [{ Text: "今日" }], "文脈なしでも Zenzai で変換する");
        Assert.True(zenzai.ConvertClauses("きょう", "") is [{ Text: "今日" }], "空の文脈も同じ");
        Assert.Equal("\uEE02。\uEE00キョウ\uEE01", server.Prompts[0], "渡した文: " + server.Prompts[0]);
        zenzai.ConvertClauses("あした", "昨日は");
        Assert.Equal("\uEE02昨日は\uEE00アシタ\uEE01", server.Prompts[^1], "文脈があればそれを渡す");
    }

    [Test]
    public static void UnchangedAnswer_IsNotUsed_AndDoesNotCountAsFailure()
    {
        using var server = new FakeServer { Answer = p => "かんたんなもんだい" };
        using var zenzai = new ZenzaiConverter(server.Endpoint);
        for (var i = 0; i < 4; i++) Assert.True(zenzai.ConvertClauses("かんたんなもんだい", "前の文") is null, "読みのままなら null");
        Assert.True(zenzai.IsAvailable, "変換しなかっただけでは諦めない");
    }

    [Test]
    public static void ServerErrors_FallBackAndGiveUp()
    {
        using var server = new FakeServer { Status = 500 };
        using var zenzai = new ZenzaiConverter(server.Endpoint);
        for (var i = 0; i < 3; i++) Assert.True(zenzai.ConvertClauses("あ" + new string('い', i), "前の文") is null, "失敗したら null");
        Assert.True(!zenzai.IsAvailable, "3 回失敗したら諦める");
        Assert.True(!new ZenzaiConverter("/no/such/llama-server", "/no/such.gguf").IsAvailable, "実行ファイルが無ければ使えない");
    }

    [Test]
    public static void SlowServer_PausesZenzaiForAWhile()
    {
        using var server = new FakeServer { Answer = p => { Thread.Sleep(300); return "遅い"; } };
        using var zenzai = new ZenzaiConverter(server.Endpoint, timeoutMs: 100);
        Assert.True(zenzai.ConvertClauses("あ", "前の文") is null, "間に合わなければ null");
        var before = server.Prompts.Count;
        Assert.True(zenzai.ConvertClauses("い", "前の文") is null, "しばらくは頼まない");
        Thread.Sleep(50);
        Assert.Equal(before, server.Prompts.Count, "遅いあいだはサーバーに頼まない");
        Assert.True(zenzai.IsAvailable, "遅いだけでは諦めない (また使える)");
    }

    [Test]
    public static void ServerThatNeverAnswers_IsGivenUp()
    {
        // 接続は受けるが答えない llama-server は、休むのを繰り返さずに諦める (ZenzaiHolder が作り直す)
        using var server = new FakeServer { Answer = p => { Thread.Sleep(300); return "遅い"; } };
        using var zenzai = new ZenzaiConverter(server.Endpoint, timeoutMs: 100) { PauseMs = 0, GiveUpAfterMs = 3000 };
        for (var i = 0; i < 3; i++)
        {
            zenzai.ConvertClauses("う" + new string('い', i), "前の文");
            Thread.Sleep(100);
        }
        Assert.True(!zenzai.GaveUp, "少しのあいだ間に合わないだけでは諦めない");
        for (var i = 0; i < 50 && !zenzai.GaveUp; i++)
        {
            zenzai.ConvertClauses("あ" + new string('い', i), "前の文");
            Thread.Sleep(100);
        }
        Assert.True(zenzai.GaveUp, "間に合わないのが続いたら諦める");
    }

    [Test]
    public static void Hybrid_PrefersZenzaiAndKeepsOtherClausesWhenSame()
    {
        using var server = new FakeServer { Answer = p => p.Contains("タンイヲトル") ? "単位を取る" : "丹位" };
        using var zenzai = new ZenzaiConverter(server.Endpoint);
        using var mozc = MozcConverterTests.Fake();
        var hybrid = new HybridConverter(mozc, () => zenzai);
        // 偽の Mozc は たんいをとる を 単位を|取る の 2 文節にする。同じ文字列なら、その区切りを使う
        var same = hybrid.ConvertClauses("たんいをとる", null);
        Assert.True(same is [{ Text: "単位を" }, { Text: "取る" }], "同じなら区切りを使う: " + string.Join("|", same?.Select(c => c.Text) ?? []));
        // 違えば Zenzai の結果
        var other = hybrid.ConvertClauses("たんい", "前の文");
        Assert.True(other is [{ Reading: "たんい", Text: "丹位" }], "違えば Zenzai: " + string.Join("|", other?.Select(c => c.Text) ?? []));
        // 使わない設定 (null) なら今までどおり
        var off = new HybridConverter(mozc, () => null);
        Assert.Equal("単位", off.ConvertText("たんい", null) ?? "", "OFF は今までどおり");
    }

    [Test]
    public static void Hybrid_KeepsParticlesAndDoesNotResplitAfterEnglish()
    {
        using var server = new FakeServer { Answer = p => p.Contains("ナラ", StringComparison.Ordinal) ? "なら" : "は知る" };
        using var zenzai = new ZenzaiConverter(server.Endpoint);
        // 同じ読みでも、文のはじめでは Mozc に任せ、英語に続く付属語では Zenzai のかなを残す。
        Assert.True(zenzai.ConvertClauses("なら", null) is null, "文の始めは Mozc に任せる");
        var kana = zenzai.ConvertClauses("なら", "python")!;
        Assert.Equal("なら", kana.Single().Text);
        Assert.Equal("なら", HybridConverter.Choose("なら", [(new("なら", "奈良"), ["奈良"])], kana, "python")!.Single().Text);

        var mozc = new[] { (new ConversionClause("はしる", "走る"), (IReadOnlyList<string>)["走る"]) };
        var z = new[] { new ConversionClause("はしる", "は知る", Zenzai: true) };
        Assert.Equal("走る", HybridConverter.Choose("はしる", mozc, z, "python")!.Single().Text);
        Assert.Equal("は知る", HybridConverter.Choose("はしる", mozc, z, "彼女")!.Single().Text);
    }

    [Test]
    public static void Align_BuildsTextFromEachClauseCandidates()
    {
        IReadOnlyList<(ConversionClause, IReadOnlyList<string>)> clauses =
            [(new("きしゃが", "記者が"), ["記者が", "汽車が", "帰社が"]), (new("きた", "来た"), ["来た", "着た", "北"])];
        var aligned = HybridConverter.Align(clauses, "汽車が着た");
        Assert.True(aligned is [{ Reading: "きしゃが", Text: "汽車が" }, { Reading: "きた", Text: "着た" }], "文節ごとの候補でできる: " + Joined(aligned));
        Assert.True(HybridConverter.Align(clauses, "記者が北") is [{ Text: "記者が" }, { Text: "北" }], "1 番目の候補もそのまま使う");
        Assert.True(HybridConverter.Align(clauses, "汽車が着たよ") is null, "候補をつないでも作れない (余る)");
        Assert.True(HybridConverter.Align(clauses, "喫茶が来た") is null, "候補に無い字がある");
        // Mozc が候補を返さなかった文節は、読みのままの文字列になる (MozcConverter.ConvertWithCandidates)。それも候補として使う
        IReadOnlyList<(ConversionClause, IReadOnlyList<string>)> noCandidates = [(new("きしゃが", "記者が"), ["記者が"]), (new("きた", "きた"), [])];
        Assert.True(HybridConverter.Align(noCandidates, "記者がきた") is [{ Text: "記者が" }, { Text: "きた" }], "候補が無い文節は読みのまま: " + Joined(HybridConverter.Align(noCandidates, "記者がきた")));
        Assert.True(HybridConverter.Align(noCandidates, "記者が来た") is null, "候補が無い文節は、読みのまま以外にはしない");
        // 半角の数字を打った読みは、Mozc が全角の候補 (１５日に) を返す。半角にそろえて比べ、半角で返す (TextChars.NormalizeHalfWidth と同じ)
        IReadOnlyList<(ConversionClause, IReadOnlyList<string>)> digits = [(new("15にちに", "１５日に"), ["１５日に", "十五日に"]), (new("きた", "来た"), ["来た", "着た"])];
        var halfWidth = HybridConverter.Align(digits, "15日に着た");
        Assert.True(halfWidth is [{ Text: "15日に" }, { Text: "着た" }], "半角の数字: " + Joined(halfWidth));
        Assert.True(HybridConverter.Align(digits, "１５日に着た") is [{ Text: "15日に" }, { Text: "着た" }], "全角で返ってきても半角にそろえる");
        // 前の文節で長い候補を先に選ぶと後ろが作れないときは、短い候補に戻って試す
        // (候補は仮のもの。本物の Mozc で、同じ文節の候補の一方が他方の頭と同じになることはまれ)
        IReadOnlyList<(ConversionClause, IReadOnlyList<string>)> overlap = [(new("あ", "AB"), ["AB", "A"]), (new("い", "BC"), ["BC"])];
        var shorter = HybridConverter.Align(overlap, "ABC");
        Assert.True(shorter is [{ Text: "A" }, { Text: "BC" }], "戻って試す: " + Joined(shorter));
    }

    [Test]
    public static void Hybrid_AlignsZenzaiToMozcClauses_AndLearningStillApplies()
    {
        // 偽の Mozc のヘルパーは、きしゃがきた を 記者が|来た の 2 文節で、文節ごとの候補も付けて返す
        var profile = Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile);
        using var mozc = new MozcConverter(new MozcHelperProcess(Environment.ProcessPath!, profile));
        // ほかの読み (文節ごとの候補を作るときの きしゃが・きた など) は読みのまま返す。空を返すと失敗と数えられ、3 回で Zenzai を使わなくなる
        using var server = new FakeServer
        {
            Answer = p => p.Contains("キシャガキタ") ? (p.Contains('外') ? "帰社が喜多" : "汽車が着た") : p.Contains("ニモツヲニカイニハコブ") ? "荷物を2階に運ぶ" : ReadingIn(p),
        };
        // 遅い PC でも時間切れ (5 秒 Zenzai を使わない) にならないよう、待つ時間を長くする
        using var zenzai = new ZenzaiConverter(server.Endpoint, timeoutMs: 5000);
        var hybrid = new HybridConverter(mozc, () => zenzai);
        mozc.WarmUp();
        for (var i = 0; i < 200 && mozc.ConvertClauses("きしゃがきた", null) is null; i++) Thread.Sleep(50);
        Assert.True(mozc.ConvertClauses("きしゃがきた", null) is [_, _], "偽の Mozc のヘルパーが 10 秒で起動しなかった");

        // Zenzai の結果 (汽車が着た) は Mozc の 1 番目 (記者が来た) と違うが、文節ごとの候補でできるので、Mozc の文節で返す
        var aligned = hybrid.ConvertClauses("きしゃがきた", "前の文");
        Assert.True(aligned is [{ Reading: "きしゃが", Text: "汽車が" }, { Reading: "きた", Text: "着た" }], "Mozc の文節で返す: " + Joined(aligned));
        // 候補でできなければ、今までどおり読み全体で 1 つの文節
        var whole = hybrid.ConvertClauses("きしゃがきた", "外の文");
        Assert.True(whole is [{ Reading: "きしゃがきた", Text: "帰社が喜多" }], "できなければ 1 文節: " + Joined(whole));

        // 文節が残るので、選び直して覚えた文節 (きしゃが → 帰社が) は Zenzai の結果の上から効く。1 文節だと効かなかった
        var history = new ConversionHistory(null);
        history.Remember("きしゃが", "帰社が");
        var k = new CompositionTests.Keyboard(history: history, converter: hybrid);
        k.Type("kishagakita ");
        Assert.Equal("帰社が着た", k.Showing ?? "", "覚えた文節は覚えた語、ほかは Zenzai の結果");
        // 文節ごとの候補に足す「その読みだけの変換」は Zenzai に頼まない (頼むと文節の数だけ待たされる)
        string[] asked;
        lock (server.Prompts) asked = server.Prompts.Select(ReadingIn).ToArray();
        Assert.True(!asked.Contains("きしゃが") && !asked.Contains("きた"), "文節だけを Zenzai に頼まない: " + string.Join("|", asked));

        // 助詞だけの文節 (に) は、かな以外の候補 (2) で当てはめない。当てはめると、助詞をかなに戻す決まりで 荷物をに階に運ぶ になる
        var cargoClauses = hybrid.ConvertClauses("にもつをにかいにはこぶ", "前の文");
        Assert.True(cargoClauses is [{ Text: "荷物を2階に運ぶ" }], "助詞の文節を数字にして当てはめない: " + Joined(cargoClauses));
        var cargo = new CompositionTests.Keyboard(converter: hybrid);
        cargo.Type("nimotsuwonikainihakobu ");
        Assert.Equal("荷物を2階に運ぶ", cargo.Showing ?? "", "助詞の文節を数字にしない (1 つの文節のまま)");
    }

    [Test]
    public static void Issue1152_ShowsWhetherZenzaiAnswered()
    {
        // #1152: Zenzai が答えたかを、変換ボックスとトレイに出す
        using var server = new FakeServer { Answer = _ => "日本語を書く" };
        using var zenzai = new ZenzaiConverter(server.Endpoint, timeoutMs: 5000);
        var useZenzai = true;
        using var mozc = MozcConverterTests.Fake();
        var hybrid = new HybridConverter(mozc, () => useZenzai ? zenzai : null);
        Assert.True(hybrid.ConvertClauses("にほんごをかく", null) is { } z && z.All(c => c.Zenzai), "Zenzai の答えの文節に印を付ける");
        // 変換ボックスの説明の行に出す。Zenzai の答えを出しているときだけ
        var k = new CompositionTests.Keyboard(converter: hybrid);
        k.Type("nihongowokaku ");
        Assert.True(k.Host.View!.Hint.StartsWith("Zenzai", StringComparison.Ordinal), k.Host.View.Hint);
        Assert.Equal("Zenzai", k.Host.View.ContextHint, "固定ガイドを隠す表示にも、変換エンジンの案内だけを残す");
        useZenzai = false;
        Assert.True(hybrid.ConvertClauses("にほんごをかく", null) is { } m && !m.Any(c => c.Zenzai), "Zenzai を使わなければ印を付けない");
        var plain = new CompositionTests.Keyboard(converter: hybrid);
        plain.Type("nihongowokaku ");
        Assert.True(!plain.Host.View!.Hint.Contains("Zenzai", StringComparison.Ordinal), "Zenzai の答えでなければ出さない: " + plain.Host.View.Hint);
        Assert.True(plain.Host.View.ContextHint is null, "Zenzai の答えでなければ文脈の案内にも出さない");
        // 学習した変換で Zenzai の答えと違う文字列にした文節は数えない
        useZenzai = true;
        var history = new ConversionHistory(null);
        history.Remember("かく", "描く");
        history.Remember("にほんごを", "日本語お");
        var learned = new CompositionTests.Keyboard(converter: hybrid, history: history);
        learned.Type("nihongowokaku ");
        Assert.Equal("日本語お描く", learned.Showing ?? "", "学習した変換を出す");
        Assert.True(!learned.Host.View!.Hint.Contains("Zenzai", StringComparison.Ordinal), "学習で変わった文節には出さない: " + learned.Host.View.Hint);
        Assert.True(learned.Host.View.ContextHint is null, "学習で置き換わった文節の Zenzai 表示は残さない");

        // 候補の一覧では、Zenzai の答えの候補にだけ注記を付ける (学習で先頭が変わっても、Zenzai の答えがどれか分かる)
        Assert.Equal(CandidateNote.Zenzai, k.Host.View.Notes?[k.Host.View.SelectedIndex] ?? CandidateNote.None, string.Join(" ", k.Host.View.Candidates));
        Assert.True(plain.Host.View.Notes?.Contains(CandidateNote.Zenzai) != true, "Zenzai の答えでなければ注記を付けない");
        var learnedNotes = learned.Host.View.Notes ?? [];
        var zenzaiIndex = learned.Host.View.Candidates.ToList().IndexOf("日本語を");
        Assert.True(zenzaiIndex > 0 && learnedNotes.ElementAtOrDefault(zenzaiIndex) == CandidateNote.Zenzai, "学習で後ろに回った Zenzai の答えに注記を付ける: " + string.Join(" ", learned.Host.View.Candidates));
        Assert.True(learnedNotes.ElementAtOrDefault(learned.Host.View.SelectedIndex) != CandidateNote.Zenzai, "学習した候補には付けない");

        using var holder = new ZenzaiHolder((s, m, _) => new ZenzaiConverter(server.Endpoint, timeoutMs: 5000));
        Assert.True(holder.Status is null, "OFF・まだ使っていなければ出さない");
        holder.Get("server", "model");
        Assert.Equal("使えます", holder.Status ?? "");
        using var missing = new ZenzaiHolder();
        missing.Get("/no/such/llama-server", "/no/such.gguf");
        Assert.Equal("llama-server かモデルが見つかりません", missing.Status ?? "");
        holder.Get("", "");
        Assert.True(holder.Status is null, "OFF にしたら出さない");
    }

    [Test]
    public static void Hybrid_ZenzaiThatCannotBeCreated_FallsBackToOtherEngine()
    {
        // #973: 同梱の .NET に System.Net.Http が無く、Zenzai を作るたびに例外になった。例外が入力まで届き、日本語を打てなくなった
        using var mozc = MozcConverterTests.Fake();
        using var holder = new ZenzaiHolder((s, m, _) => throw new FileNotFoundException("Could not load file or assembly 'System.Net.Http'"));
        var hybrid = new HybridConverter(mozc, () => holder.Get("server", "model"));
        Assert.True(hybrid.ConvertClauses("たんいをとる", null) is [{ Text: "単位を" }, { Text: "取る" }], "文節に分けた変換は、ほかの変換エンジンで");
        Assert.Equal("今日", hybrid.ConvertText("きょう", null) ?? "", "1 回の変換も、ほかの変換エンジンで");
        var k = new CompositionTests.Keyboard(converter: hybrid);
        k.Type("tanniwotoru ");
        Assert.Equal("単位を取る", k.Showing ?? "", "打っても入力が止まらない");
    }

    [Test]
    public static void Holder_ZenzaiThatCannotBeCreatedForAnyReason_FallsBackAndWaits()
    {
        // 同梱の .NET のアセンブリが欠けている・合わないときの例外は型で列挙しきれない (MissingMethodException・BadImageFormatException など)
        foreach (var failure in new Exception[] { new MissingMethodException("System.Net.Http"), new BadImageFormatException("System.Net.Http") })
        {
            var created = 0;
            using var holder = new ZenzaiHolder((s, m, _) => { created++; throw failure; });
            using var mozc = MozcConverterTests.Fake();
            var hybrid = new HybridConverter(mozc, () => holder.Get("server", "model"));
            Assert.True(hybrid.ConvertClauses("たんいをとる", null) is [{ Text: "単位を" }, { Text: "取る" }], $"{failure.GetType().Name}: ほかの変換エンジンで変換する");
            hybrid.ConvertClauses("たんいをとる", null);
            Assert.Equal(1, created, $"{failure.GetType().Name}: 打つたびに作り直さない (しばらく待つ)");
        }
    }

    [Test]
    public static void Hybrid_ZenzaiWithMalformedAnswers_FallsBackAndGivesUp()
    {
        // 答えの content が文字列でない: 答えなかったのと同じに数え、続いたら Zenzai を諦める。そのあいだもほかの変換エンジンで変換する
        using var server = new FakeServer { RawBody = "{\"content\":1}" };
        using var zenzai = new ZenzaiConverter(server.Endpoint, timeoutMs: 5000);
        using var mozc = MozcConverterTests.Fake();
        var hybrid = new HybridConverter(mozc, () => zenzai);
        for (var i = 0; i < 3; i++)
            Assert.True(hybrid.ConvertClauses("たんいをとる", null) is [{ Text: "単位を" }, { Text: "取る" }], $"{i + 1} 回目も、ほかの変換エンジンで変換する (失敗した答えは覚えないので、毎回聞き直す)");
        Assert.True(zenzai.GaveUp, "形の違う答えが続いたら諦める");
        int asked;
        lock (server.Prompts) asked = server.Prompts.Count;
        hybrid.ConvertClauses("たんいをとる", null);
        lock (server.Prompts) Assert.Equal(asked, server.Prompts.Count, "諦めた Zenzai には、もう頼まない");
    }

    [Test]
    public static void Holder_ZenzaiThatCannotBeCreated_IsRetriedAfterAWhile()
    {
        var now = 0L;
        var created = 0;
        using var holder = new ZenzaiHolder((s, m, _) => { created++; throw new FileNotFoundException("Could not load file or assembly 'System.Net.Http'"); }, () => now);
        Assert.True(holder.Get("server", "model") is null, "作れなければ null (Mozc で変換する)");
        Assert.True(holder.Get("server", "model") is null && created == 1, "すぐには作り直さない (打つたびに例外にしない)");
        now += (long)holder.RetryAfter.TotalMilliseconds;
        Assert.True(holder.Get("server", "model") is null && created == 2, "しばらくたったら、もう一度作る");
        // 待つ時間は 5 → 10 → 20 → 40 → 60 (上限。倍にすると 80) 分
        const long minute = 60_000;
        now += 5 * minute;
        Assert.True(holder.Get("server", "model") is null && created == 2, "続けて作れないときは、待つ時間を延ばす (5 分ごとに作り直さない)");
        now += 5 * minute;
        Assert.True(holder.Get("server", "model") is null && created == 3, "延ばした時間がたったら、もう一度作る");
        now += 20 * minute;
        Assert.True(holder.Get("server", "model") is null && created == 4, "20 分待つ");
        now += 40 * minute;
        Assert.True(holder.Get("server", "model") is null && created == 5, "40 分待つ");
        now += 60 * minute - 1;
        Assert.True(holder.Get("server", "model") is null && created == 5, "上限の 60 分たつまでは作らない");
        now += 1;
        Assert.True(holder.Get("server", "model") is null && created == 6, "待つ時間は上限 (60 分) まで");
        Assert.True(holder.Get("server", "other") is null && created == 7, "場所を変えたら、待たずに作る");
    }

    [Test]
    public static void Issue1307_ServerArguments_PassDevice()
    {
        // 使う GPU が空 (既定) なら、今までと同じ引数 (-ngl 0 で CPU だけ。--device は渡さない)
        var cpu = ZenzaiConverter.ServerArguments("model.gguf", 8080, "");
        Assert.Equal("0", cpu[cpu.IndexOf("-ngl") + 1]);
        Assert.True(!cpu.Contains("--device"), string.Join(" ", cpu));
        // 使う GPU を書いたら --device だけを渡し、載せる層の数は llama-server に任せる (-ngl を渡さない)
        var gpu = ZenzaiConverter.ServerArguments("model.gguf", 8080, " Vulkan0 ");
        Assert.Equal("Vulkan0", gpu[gpu.IndexOf("--device") + 1]);
        Assert.True(!gpu.Contains("-ngl"), string.Join(" ", gpu));
        Assert.True(gpu.Contains("--override-kv"), "zenz の前処理の差し替えは残す");
        Assert.Equal("", new Meltype.Config.Settings { ZenzaiDevice = "  " }.Normalize().ZenzaiDevice, "空白だけなら空");
    }

    [Test]
    public static void Issue1307_Holder_PassesDeviceAndRecreatesWhenItChanges()
    {
        var devices = new List<string>();
        using var server = new FakeServer();
        using var holder = new ZenzaiHolder((s, m, device) => { devices.Add(device); return new ZenzaiConverter(server.Endpoint, timeoutMs: 5000); });
        holder.Get("server", "model");
        holder.Get("server", "model", "");
        Assert.Equal("", string.Join(",", devices), "既定のままなら作り直さない (使う GPU は空のまま渡す)");
        holder.Get("server", "model", "Vulkan0");
        Assert.Equal(",Vulkan0", string.Join(",", devices), "使う GPU を変えたら、その名前で作り直す");
        holder.Get("server", "model", " Vulkan0 ");
        Assert.Equal(2, devices.Count, "前後の空白だけの違いでは作り直さない");
    }

    [Test]
    public static void Holder_DoesNotCreateAfterDispose()
    {
        // Meltype の終了 (meltype_shutdown) で止めた後に変換が来ても、llama-server を作らない (作ると止める人がいない)
        var created = 0;
        var holder = new ZenzaiHolder((s, m, d) => { created++; return new ZenzaiConverter(new Uri("http://127.0.0.1:1/")); });
        holder.Dispose();
        Assert.True(holder.Get("server", "model") is null, "止めた後は null");
        Assert.Equal(0, created, "止めた後は作らない");
    }

    [Test]
    public static void Holder_ZenzaiThatGaveUp_IsRecreatedAfterAWhile()
    {
        // llama-server が落ちたなどで 3 回続けて答えないと、ZenzaiConverter は諦める (例外にはならない)。それも作り直す
        using var server = new FakeServer { Status = 500 };
        var now = 0L;
        var created = new List<ZenzaiConverter>();
        using var holder = new ZenzaiHolder((s, m, _) => { var z = new ZenzaiConverter(server.Endpoint, timeoutMs: 5000); created.Add(z); return z; }, () => now);
        var first = holder.Get("server", "model")!;
        for (var i = 0; i < 3; i++) first.ConvertClauses("はし" + new string('の', i), null);
        Assert.True(!first.IsAvailable, "3 回答えないと ZenzaiConverter は諦める");
        Assert.True(holder.Get("server", "model") is null, "諦めた Zenzai は止めて、しばらくは使わない");
        server.Status = 200;
        server.Answer = _ => "橋";
        now += (long)holder.RetryAfter.TotalMilliseconds;
        var second = holder.Get("server", "model");
        Assert.True(second is not null && created.Count == 2, "しばらくたったら作り直す (llama-server が一時的に落ちただけなら、また使える)");
        Assert.Equal("橋", Joined(second!.ConvertClauses("はし", null)).Split('→')[^1], "作り直したものは使える");
        // 一度でも答えたものが諦めたときは、一時的な失敗として最初の待ち時間 (5 分) から (続けて失敗した 2 回目の 10 分にしない)
        server.Status = 500;
        for (var i = 0; i < 3; i++) second!.ConvertClauses("かわ" + new string('の', i), null);
        Assert.True(holder.Get("server", "model") is null, "また諦めたら止める");
        now += (long)holder.RetryAfter.TotalMilliseconds;
        Assert.True(holder.Get("server", "model") is not null && created.Count == 3, "使えていたあとの失敗は、最初の待ち時間で作り直す");
        using var missing = new ZenzaiHolder(now: () => now);
        var notInstalled = missing.Get("/no/such/llama-server", "/no/such.gguf");
        Assert.True(notInstalled is not null && ReferenceEquals(missing.Get("/no/such/llama-server", "/no/such.gguf"), notInstalled), "場所にファイルが無いだけなら、作り直さない (場所を直すまで同じ)");
    }

    [Test]
    public static void Holder_SamePlaceReusesZenzai_AndOffStopsIt()
    {
        using var server = new FakeServer { Answer = p => ReadingIn(p) == "はし" ? "橋" : "箸" };
        var created = new List<ZenzaiConverter>();
        using var holder = new ZenzaiHolder((s, m, _) => { var z = new ZenzaiConverter(server.Endpoint, timeoutMs: 5000); created.Add(z); return z; });
        var first = holder.Get("server", "model");
        Assert.True(first is not null && ReferenceEquals(holder.Get("server", "model"), first) && created.Count == 1, "同じ場所なら同じものを使う");
        Assert.Equal("橋", Joined(first!.ConvertClauses("はし", null)).Split('→')[^1], "使える");
        Assert.True(holder.Get("", "") is null && first!.ConvertClauses("はしを", null) is null, "OFF にしたら止める (llama-server を残さない)");
    }

    /// <summary>zenz に渡した文 (U+EE00 + カタカナの読み + …) の読みを、ひらがなにして返す。</summary>
    private static string ReadingIn(string prompt)
    {
        var start = prompt.IndexOf((char)0xEE00) + 1;
        var end = prompt.IndexOf((char)0xEE01, start);
        return string.Concat(prompt[start..(end < 0 ? prompt.Length : end)].Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c));
    }

    private static string Joined(IReadOnlyList<ConversionClause>? clauses) => clauses is null ? "(null)" : string.Join("|", clauses.Select(c => $"{c.Reading}→{c.Text}"));

    /// <summary>
    /// 本物の llama-server と zenz のモデルがあるときだけ動かす (環境変数 MELTYPE_ZENZAI_SERVER と MELTYPE_ZENZAI_MODEL)。
    /// zenz の前処理 (gpt2-small-japanese-char) は本家の llama.cpp に無いが、起動するときに gpt-2 に差し替えて読む (本家の b11719 と zenz-v3.1-small で確かめた)。
    /// </summary>
    [Test]
    public static void RealZenz_ConvertsWhenAvailable()
    {
        if (Environment.GetEnvironmentVariable("MELTYPE_ZENZAI_SERVER") is not { Length: > 0 } server ||
            Environment.GetEnvironmentVariable("MELTYPE_ZENZAI_MODEL") is not { Length: > 0 } model) return;
        using var zenzai = new ZenzaiConverter(server, model, timeoutMs: 5000);
        zenzai.WarmUp();
        // 起動を待つ (モデルを読み込むので数秒かかる)
        var clauses = (IReadOnlyList<ConversionClause>?)null;
        for (var i = 0; i < 100 && clauses is null; i++)
        {
            clauses = zenzai.ConvertClauses("きょうはいいてんきですね", null);
            if (clauses is null) Thread.Sleep(300);
        }
        Assert.Equal("今日はいい天気ですね", clauses is [var c] ? c.Text : "(変換できなかった)", "文を変換");
        var chopsticks = zenzai.ConvertClauses("はし", "ごはんをたべるための");
        var bridge = zenzai.ConvertClauses("はし", "みずうみにかかる");
        Assert.Equal("箸", chopsticks is [var a] ? a.Text : "", "文脈 (食べる) で 箸");
        Assert.Equal("橋", bridge is [var b] ? b.Text : "", "文脈 (湖にかかる) で 橋");
        // 文脈の無い きょう は、何も渡すと モデルが迷って 強 になる。「。」を文脈にして 今日 にする
        Assert.Equal("今日", zenzai.ConvertClauses("きょう", null) is [var bare] ? bare.Text : "", "文脈なしでも きょう は 今日 (「。」を文脈にする: 何も渡さないと 強)");
        Assert.Equal("今日", zenzai.ConvertClauses("きょう", "あしたは") is [var today] ? today.Text : "", "文脈 (あしたは) で 今日");
        // #2231: 読みの全角の記号 (ローマ字の * は ＊ になる) の位置に、読みに無い字を出さない (記号は半角で答える)
        Assert.Equal("*日本語*", zenzai.ConvertClauses("＊にほんご＊", null) is [var star] ? star.Text : "", "＊にほんご＊");
        Assert.Equal("図書館***", zenzai.ConvertClauses("としょかん＊＊＊", null) is [var stars] ? stars.Text : "", "としょかん＊＊＊");
    }

    [Test]
    public static void RealZenz_ConvertsOnlyTheCurrentReading()
    {
        if (Environment.GetEnvironmentVariable("MELTYPE_ZENZAI_SERVER") is not { Length: > 0 } server ||
            Environment.GetEnvironmentVariable("MELTYPE_ZENZAI_MODEL") is not { Length: > 0 } model) return;
        using var zenzai = new ZenzaiConverter(server, model, timeoutMs: 5000);
        zenzai.WarmUp();
        for (var i = 0; i < 100 && !zenzai.IsReady; i++) Thread.Sleep(300);
        Assert.True(zenzai.IsReady, "llama-server が起動しなかった");

        using var mozc = MozcConverterTests.Fake();
        var hybrid = new HybridConverter(mozc, () => zenzai);
        foreach (var (typed, context, expected) in new[]
        {
            ("zisyono", "このIMEを調べた。Mozcの", "辞書の"),
            ("unnyousurubaaiha", "今回の更新では、利用者の使い方によって挙動が変わることを想定していますが、同団体の定義で", "運用する場合は"),
            ("zisyono", "辞書の", "辞書の"),
        })
        {
            var keyboard = new CompositionTests.Keyboard(converter: hybrid);
            keyboard.Host.PrecedingText = context;
            keyboard.Type(typed + " ");
            Assert.Equal(expected, keyboard.Host.View?.Candidates[0] ?? "", "最初の候補: " + typed);
            keyboard.Type("\n");
            Assert.Equal(expected, keyboard.Host.Document, "確定した文字: " + typed);
        }
    }

    [Test]
    public static void StaleServerFromAKilledMeltype_IsEnded()
    {
        // Meltype が強制終了されて llama-server が残ったときは、次の Meltype が終わらせる (PID と名前と起動した時刻が合うときだけ)
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sleep")) return;
        using var stale = Sleep();
        var (ownerPid, ownerTicks) = EndedProcess();
        var file = ZenzaiConverter.RecordServer(stale.Id, "sleep", Ticks(stale), ownerPid, ownerTicks);
        try
        {
            ZenzaiConverter.EndStaleServers();
            Assert.True(stale.WaitForExit(5000), "持ち主が終わったサーバーを終わらせる");
            Assert.True(!File.Exists(file), "終わらせたサーバーの記録は消す");
        }
        finally
        {
            if (!stale.HasExited) stale.Kill();
            if (file is not null) File.Delete(file);
        }
    }

    [Test]
    public static void ServerOfARunningMeltype_IsNotEnded()
    {
        // 自己診断 (--self-test) などで別の Meltype が Zenzai を起動しても、動いている IME の llama-server は止めない
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sleep")) return;
        using var server = Sleep();
        var file = ZenzaiConverter.RecordServer(server.Id, "sleep", Ticks(server), Environment.ProcessId, Ticks(System.Diagnostics.Process.GetCurrentProcess()));
        try
        {
            ZenzaiConverter.EndStaleServers();
            Assert.True(!server.WaitForExit(2000), "持ち主が動いているサーバーは終わらせない");
            Assert.True(File.Exists(file), "持ち主が動いている間は記録を残す");
        }
        finally
        {
            if (!server.HasExited) server.Kill();
            if (file is not null) File.Delete(file);
        }
    }

    [Test]
    public static void ProcessWithReusedPid_IsNotEnded()
    {
        // PID が使い回されて、同じ名前の別のプロセス (ユーザーが自分で動かしている llama-server など) になっていたら終わらせない
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sleep")) return;
        using var other = Sleep();
        var (ownerPid, ownerTicks) = EndedProcess();
        var file = ZenzaiConverter.RecordServer(other.Id, "sleep", other.StartTime.ToUniversalTime().AddMinutes(-5).Ticks, ownerPid, ownerTicks);
        try
        {
            ZenzaiConverter.EndStaleServers();
            Assert.True(!other.WaitForExit(2000), "起動した時刻が違うプロセスは終わらせない");
        }
        finally
        {
            if (!other.HasExited) other.Kill();
            if (file is not null) File.Delete(file);
        }
    }

    private static System.Diagnostics.Process Sleep() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sleep", "120") { UseShellExecute = false })!;

    private static long Ticks(System.Diagnostics.Process process) => process.StartTime.ToUniversalTime().Ticks;

    /// <summary>終わったプロセス (強制終了された持ち主の Meltype の代わり) の PID と起動した時刻。</summary>
    private static (int Pid, long Ticks) EndedProcess()
    {
        using var ended = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sleep", "0.2") { UseShellExecute = false })!;
        var ticks = Ticks(ended);
        ended.WaitForExit();
        return (ended.Id, ticks);
    }
}
