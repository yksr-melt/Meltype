// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Tip;

namespace Meltype.Tests;

/// <summary>
/// Meltype IME (TSF) の DLL と Meltype.exe の境目 (<see cref="TipServer.Handle"/> の JSON) の契約テスト (issue #1215)。
/// DLL がするのと同じ要求 (key・commit・close・hello) を順に送り、応答の確定 (commits)・未確定の表示 (view)・キーを使ったか (consumed) を確かめる。
/// パイプも DLL も使わないので、GitHub Actions の Windows でも安定して流せる。
/// 確かめられないこと: DLL (native/tip) の TSF の処理 (キーの受け取り・未確定の文字の表示・フォーカスの通知) と、OS の実際のキーの配送。
/// <b>このテストが通っても、実際の Windows のデスクトップで打てることは保証しない</b> (docs/dev/TSF_DESIGN.md の「未対応・確かめていないこと」)。
/// </summary>
internal static class TipContractTests
{
    /// <summary>
    /// DLL の代わり。要求を送って応答を受け取り、DLL と同じように入力欄 (Document) に確定を入れる。
    /// 失敗したときに操作の順番と応答が分かるように、すべてのやり取りを残す (Trace)。
    /// </summary>
    private sealed class Dll : IDisposable
    {
        private readonly Control _invoker = new();
        private readonly CompositionService _service;
        private readonly TipServer _server;
        private readonly List<string> _trace = [];

        public Settings Settings = new() { Mode = InputMode.Tsf, LiveConversion = false };
        public Dictionary<string, MeltypeSession> Sessions { get; } = [];

        /// <summary>入力欄に入った文字 (確定した文字と、アプリに通したキーの文字)。</summary>
        public string Document { get; private set; } = "";

        /// <summary>入力欄ごとの、変換中の表示 (無ければ null)。</summary>
        public Dictionary<string, string?> Composition { get; } = [];

        /// <summary>DLL が見送った確定し直し (deleteBefore が 1 以上で、DLL の条件に合わなかったもの)。</summary>
        public List<string> Skipped { get; } = [];

        public Dll()
        {
            _service = TipHarness.CreateService(_invoker, () => Settings.CursorIntoBrackets);
            _server = new TipServer(_invoker, _service, () => Settings);
        }

        public string Trace => "操作と応答:\n" + string.Join("\n", _trace);

        public JsonElement Send(string request)
        {
            var reply = _server.Handle(request, Sessions);
            _trace.Add($"  {request}\n    → {reply}");
            using var document = JsonDocument.Parse(reply);
            return document.RootElement.Clone();
        }

        public JsonElement Hello(string process = "notepad") => Send($"{{\"op\":\"hello\",\"process\":{Json(process)}}}");

        /// <summary>1 キー送る。' ' = Space、'\n' = Enter、'\b' = BackSpace、'\u001b' = Esc。</summary>
        public JsonElement Key(char key, string sid = "t1", bool moved = false, string process = "notepad")
        {
            var (vk, ch) = key switch
            {
                ' ' => (0x20, 0),
                '\n' => (0x0D, 0),
                '\b' => (0x08, 0),
                '\u001b' => (0x1B, 0),
                '[' => (0xDB, (int)key),
                ']' => (0xDD, (int)key),
                >= 'a' and <= 'z' => (key - 'a' + 'A', (int)key),
                _ => throw new ArgumentException($"送れないキー: {key}"),
            };
            // DLL (native/tip の TextService の key) と同じ形で送る: どのキーにも code と direct を付け、
            // 変換中でなければキャレットの前後 (before・after) を、変換中なら composing だけを送る
            var surrounding = Composition.GetValueOrDefault(sid) is not null
                ? ",\"composing\":true"
                : $",\"before\":{Json(Document.Length > 4000 ? Document[^4000..] : Document)},\"after\":\"\"";
            var reply = Send($"{{\"op\":\"key\",\"sid\":{Json(sid)},\"vk\":{vk},\"ch\":{ch},\"mods\":0,\"process\":{Json(process)},\"code\":true,\"direct\":false" +
                $"{(moved ? ",\"moved\":true" : "")}{surrounding}}}");
            Apply(reply, sid);
            // 使わなかった (または受け持たない) キーは、アプリがそのまま入れる
            if (!reply.GetProperty("consumed").GetBoolean() && ch > 0) Document += key;
            return reply;
        }

        public List<JsonElement> Type(string keys, string sid = "t1") => keys.Select(key => Key(key, sid)).ToList();

        public JsonElement Commit(string sid = "t1", bool moved = false)
        {
            var reply = Send($"{{\"op\":\"commit\",\"sid\":{Json(sid)}{(moved ? ",\"moved\":true" : "")}}}");
            Apply(reply, sid);
            return reply;
        }

        public JsonElement Close(string sid = "t1")
        {
            var reply = Send($"{{\"op\":\"close\",\"sid\":{Json(sid)}}}");
            Composition[sid] = null;
            return reply;
        }

        /// <summary>
        /// DLL (native/tip の TextService::Apply・ExtendOverPrevious・RewriteBeforeCaret) と同じ条件で確定を入れ、表示を置き換える。
        /// 確定し直し (deleteBefore が 1 以上) は:
        ///   current (今確定した語) があれば、まず current を入れ、次に入力欄の前が expect + current のままなら、まとめて text に書き換える。
        ///   書き換えられなければ current を確定したまま残す (変換中でも行う)。
        ///   current が無ければ、変換中でなく、前の deleteBefore 文字がちょうど expect のときだけ、消してから text を入れる。
        /// 見送った確定し直し (書き換えなかったもの) は Skipped に残す。
        /// </summary>
        public void Apply(JsonElement reply, string sid)
        {
            var composing = Composition.GetValueOrDefault(sid) is not null;
            foreach (var commit in reply.GetProperty("commits").EnumerateArray())
            {
                var delete = commit.GetProperty("deleteBefore").GetInt32();
                var text = commit.GetProperty("text").GetString()!;
                if (delete == 0)
                {
                    Document += text;
                    continue;
                }
                var expect = commit.TryGetProperty("expect", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                if (commit.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.String)
                {
                    var current = c.GetString()!;
                    Document += current;
                    var rewritten = expect + current;
                    if (expect is null || !Document.EndsWith(rewritten, StringComparison.Ordinal))
                    {
                        Skipped.Add(commit.GetRawText());
                        continue;
                    }
                    Document = Document[..^rewritten.Length] + text;
                    continue;
                }
                if (composing || expect is null || delete > Document.Length || Document[^delete..] != expect)
                {
                    Skipped.Add(commit.GetRawText());
                    continue;
                }
                Document = Document[..^delete] + text;
            }
            Composition[sid] = reply.GetProperty("view") is { ValueKind: JsonValueKind.Object } view ? view.GetProperty("text").GetString() : null;
        }

        private static string Json(string text) => JsonSerializer.Serialize(text);

        public void Dispose()
        {
            _server.Dispose();
            _service.Dispose();
            _invoker.Dispose();
        }
    }

    [Test]
    public static void BracketCommitKeepsEnterConsumedAndReturnsCaretBack()
    {
        foreach (var enabled in new[] { false, true })
        {
            foreach (var convert in new[] { false, true })
            {
                using var dll = new Dll();
                dll.Settings.CursorIntoBrackets = enabled;
                dll.Type(convert ? "[] " : "[]");
                var result = dll.Key('\n');
                Assert.True(result.GetProperty("consumed").GetBoolean(), "確定 Enter を送り直さない\n" + dll.Trace);
                Assert.Equal("「」", dll.Document, dll.Trace);
                var edit = result.GetProperty("commits")[0];
                Assert.Equal(enabled, edit.TryGetProperty("caretBack", out var caret), dll.Trace);
                if (enabled) Assert.Equal(1, caret.GetInt32(), "DLL に独立した移動要求を渡す\n" + dll.Trace);
            }
        }
    }

    private static bool Consumed(JsonElement reply) => reply.GetProperty("consumed").GetBoolean();

    private static bool Active(JsonElement reply) => reply.GetProperty("active").GetBoolean();

    [Test]
    public static void BracketCandidateCommitReturnsTheFollowingCompositionTogether()
    {
        foreach (var enabled in new[] { false, true })
        {
            using var dll = new Dll();
            dll.Settings.CursorIntoBrackets = enabled;
            dll.Type("[] ");
            var result = dll.Key('k');
            Assert.True(result.GetProperty("consumed").GetBoolean(), dll.Trace);
            var edit = result.GetProperty("commits")[0];
            Assert.Equal("「」", edit.GetProperty("text").GetString(), dll.Trace);
            Assert.Equal(enabled, edit.TryGetProperty("caretBack", out var caret), dll.Trace);
            if (enabled) Assert.Equal(1, caret.GetInt32(), dll.Trace);
            Assert.Equal("k", result.GetProperty("view").GetProperty("text").GetString(), dll.Trace);
        }
    }

    [Test]
    public static void Type_Convert_Commit_ThenTypeAgain()
    {
        // 入力 → 変換 → 確定 → 続けて入力 (issue #255 の「確定のあと 2 回目の入力が止まる」と同じ流れ)
        using var dll = new Dll();
        Assert.True(Active(dll.Hello()), "Meltype IME のモードでは受け持つ\n" + dll.Trace);
        Assert.True(dll.Type("kyouha").All(Consumed), "打った英字はアプリに通さない\n" + dll.Trace);
        Assert.Equal("きょうは", dll.Composition["t1"], dll.Trace);
        var space = dll.Key(' ');
        Assert.True(Consumed(space) && space.GetProperty("view").GetProperty("converting").GetBoolean(), "Space で変換を始める\n" + dll.Trace);
        dll.Key('\n');
        Assert.True(dll.Composition["t1"] is null, "Enter で確定して表示を閉じる\n" + dll.Trace);
        var committed = dll.Document;
        Assert.True(committed.Length > 0, "変換した文字が入る\n" + dll.Trace);
        Assert.True(dll.Type("aiueo").All(Consumed), "確定のあとも続けて打てる\n" + dll.Trace);
        Assert.Equal("あいうえお", dll.Composition["t1"], dll.Trace);
        dll.Key('\n');
        Assert.Equal(committed + "あいうえお", dll.Document, dll.Trace);
        Assert.True(!Consumed(dll.Key('\n')), "打っていないときの Enter はアプリに通す (改行)\n" + dll.Trace);
    }

    [Test]
    public static void BackSpace_And_Esc()
    {
        using var dll = new Dll();
        dll.Type("kyouhaa");
        Assert.True(Consumed(dll.Key('\b')), "変換中の BackSpace は Meltype が使う\n" + dll.Trace);
        Assert.Equal("きょうは", dll.Composition["t1"], dll.Trace);
        dll.Key('\n');
        Assert.Equal("きょうは", dll.Document, dll.Trace);
        dll.Type("aiu");
        dll.Key('\u001b');
        Assert.True(dll.Composition["t1"] is null, "Esc で打っていた文字を消す\n" + dll.Trace);
        Assert.Equal("きょうは", dll.Document, "Esc では何も入れない\n" + dll.Trace);
        Assert.True(!Consumed(dll.Key('\b')), "打っていないときの BackSpace はアプリに通す\n" + dll.Trace);
    }

    [Test]
    public static void Composition_EndsWhenTheAppCommits()
    {
        // クリック・別の入力欄へのフォーカスの移動では、DLL が commit (moved) を送る。変換中の文字を確定し、表示を閉じる
        using var dll = new Dll();
        dll.Type("kyouha");
        var commit = dll.Commit(moved: true);
        Assert.Equal("きょうは", dll.Document, dll.Trace);
        Assert.True(dll.Composition["t1"] is null && commit.GetProperty("view").ValueKind == JsonValueKind.Null, "確定したら表示を閉じる\n" + dll.Trace);
        Assert.Equal(0, dll.Commit().GetProperty("commits").GetArrayLength(), "打っていなければ何も確定しない\n" + dll.Trace);
    }

    [Test]
    public static void CaretMoved_DoesNotRewriteThePreviousCommit()
    {
        // i Space want Space と続けて打つと、前の「い」を英語の I に確定し直す (#975。SessionFacadeTests の AutoCorrect と同じ操作)。
        // 前の語を確定したあとキャレットを動かして (moved) から打ったときは、前の確定を消して確定し直さない (--tip-e2e の「動かしたあとは直さない」)
        static (Dll Dll, List<JsonElement> Replies) TypeWantAfterI(bool moved)
        {
            var dll = new Dll();
            dll.Type("i ");
            // クリックで動かしたときは、DLL が先に変換中の文字を確定し (commit + moved)、次のキーにも moved を付ける
            if (moved) dll.Commit(moved: true);
            var replies = new List<JsonElement> { dll.Key('w', moved: moved) };
            replies.AddRange(dll.Type("ant "));
            return (dll, replies);
        }
        static bool Rewrites(List<JsonElement> replies) =>
            replies.SelectMany(r => r.GetProperty("commits").EnumerateArray()).Any(c => c.GetProperty("deleteBefore").GetInt32() > 0);

        var (still, stillReplies) = TypeWantAfterI(moved: false);
        using (still)
        {
            Assert.True(Rewrites(stillReplies), "動かさなければ、前の語を確定し直す\n" + still.Trace);
            Assert.Equal("I want ", still.Document, "前の語ごと書き換え、今の語も消えない\n" + still.Trace);
            Assert.Equal(0, still.Skipped.Count, "見送った確定し直し: " + string.Join(", ", still.Skipped) + "\n" + still.Trace);
        }
        var (moved, movedReplies) = TypeWantAfterI(moved: true);
        using (moved)
        {
            Assert.True(!Rewrites(movedReplies), "動かしたら、前の確定を消さない\n" + moved.Trace);
            Assert.True(moved.Document.EndsWith("want ", StringComparison.Ordinal) && !moved.Document.StartsWith("I ", StringComparison.Ordinal), moved.Trace);
        }
    }

    [Test]
    public static void CommitRewrite_IsAppliedOnlyWhenTheDllWould()
    {
        // 確定し直し (deleteBefore) は DLL と同じ条件でだけ入れる: expect が前の文字と同じなら消して入れ、
        // expect が無い・違う・消す文字が足りない・変換中なら、丸ごと見送る (native/tip の TextService::Apply)
        static JsonElement Reply(string commit, string? view = null)
        {
            using var document = JsonDocument.Parse($"{{\"active\":true,\"consumed\":true,\"commits\":[{commit}],\"view\":{view ?? "null"}}}");
            return document.RootElement.Clone();
        }
        using var dll = new Dll();
        dll.Apply(Reply("{\"deleteBefore\":0,\"text\":\"teh\"}"), "t1");
        dll.Apply(Reply("{\"deleteBefore\":3,\"text\":\"the\",\"expect\":\"teh\"}"), "t1");
        Assert.Equal("the", dll.Document, "expect が同じなら確定し直す");
        Assert.Equal(0, dll.Skipped.Count);
        foreach (var (commit, reason) in new[]
                 {
                     ("{\"deleteBefore\":3,\"text\":\"xyz\"}", "expect が無い"),
                     ("{\"deleteBefore\":3,\"text\":\"xyz\",\"expect\":\"abc\"}", "expect が違う"),
                     ("{\"deleteBefore\":9,\"text\":\"xyz\",\"expect\":\"thethethe\"}", "消す文字が足りない"),
                 })
        {
            dll.Apply(Reply(commit), "t1");
            Assert.Equal("the", dll.Document, reason + ": 消しも入れもしない");
        }
        dll.Apply(Reply("{\"deleteBefore\":0,\"text\":\"\"}", "{\"text\":\"か\"}"), "t1");
        dll.Apply(Reply("{\"deleteBefore\":3,\"text\":\"xyz\",\"expect\":\"the\"}", "{\"text\":\"か\"}"), "t1");
        Assert.Equal("the", dll.Document, "変換中は確定し直さない");
        Assert.Equal(4, dll.Skipped.Count);

        // 今確定した語 (current) つきの確定し直しは、変換中でも今の語を入れ、前が expect + current のままなら前の語ごと書き換える (#975)
        using var rewrite = new Dll();
        rewrite.Apply(Reply("{\"deleteBefore\":0,\"text\":\"負け\"}"), "t1");
        rewrite.Apply(Reply("{\"deleteBefore\":0,\"text\":\"\"}", "{\"text\":\"want\"}"), "t1");
        rewrite.Apply(Reply("{\"deleteBefore\":2,\"text\":\"make want \",\"expect\":\"負け\",\"current\":\"want \"}"), "t1");
        Assert.Equal("make want ", rewrite.Document, "前の語ごと書き換える");
        // 前の文字が expect と違えば、今の語だけを確定したまま残す (今の語は消さない・前の語を二重に入れない)
        rewrite.Apply(Reply("{\"deleteBefore\":2,\"text\":\"xx yy \",\"expect\":\"負け\",\"current\":\"yy \"}"), "t1");
        Assert.Equal("make want yy ", rewrite.Document, "書き換えられなければ今の語だけ");
        Assert.Equal(1, rewrite.Skipped.Count);
    }

    [Test]
    public static void ServerRewrites_AlwaysMatchWhatTheDllCanApply()
    {
        // Meltype.exe が送る確定し直しは、DLL が入れられる形 (expect が前の文字と同じ) になっている。
        // 確定のあとキャレットを動かさずに続けて打つ流れで、DLL が見送るものが無いこと。
        // i Enter のあとの want Space は、#975 で確定し直さなくなった (Enter で確定した日本語の語は英語に直さない)
        using var dll = new Dll();
        dll.Type("i\n");
        dll.Type("want ");
        dll.Type("kyouha\n");
        Assert.Equal("いwant きょうは", dll.Document, "前の語も今の語も消えない\n" + dll.Trace);
        Assert.Equal(0, dll.Skipped.Count, "見送った確定し直し: " + string.Join(", ", dll.Skipped) + "\n" + dll.Trace);
    }

    [Test]
    public static void InputFields_AreSeparatedBySid()
    {
        // DLL は入力欄 (スレッド) ごとに sid を振る。別の入力欄の変換中の文字は混ざらない
        using var dll = new Dll();
        dll.Type("ka", "t1");
        dll.Type("ki", "t2");
        Assert.Equal("か", dll.Composition["t1"], dll.Trace);
        Assert.Equal("き", dll.Composition["t2"], dll.Trace);
        dll.Close("t2");
        Assert.True(!dll.Sessions.ContainsKey("t2"), "閉じた入力欄の入力の本体は捨てる\n" + dll.Trace);
        dll.Key('\n', "t1");
        Assert.Equal("か", dll.Document, "残った入力欄はそのまま確定できる\n" + dll.Trace);
    }

    [Test]
    public static void Reconnected_DoesNotTakeOverACompositionItDoesNotKnow()
    {
        // つなぎ直した DLL が「変換中」と言っても、続きを知らない入力は受け持たない (DLL は変換中の文字をそのまま確定し、キーをアプリに通す)
        using var dll = new Dll();
        var reply = dll.Send("{\"op\":\"key\",\"sid\":\"lost\",\"vk\":65,\"ch\":97,\"mods\":0,\"process\":\"notepad\",\"composing\":true}");
        Assert.True(!Active(reply) && !Consumed(reply), dll.Trace);
        Assert.True(!dll.Sessions.ContainsKey("lost"), dll.Trace);
        Assert.True(!Active(dll.Send("{\"op\":\"unknown\",\"sid\":\"t1\"}")), "知らない要求には何もしない\n" + dll.Trace);
    }

    [Test]
    public static void SettingsChange_WhileComposing_CommitsAndPassesTheKey()
    {
        // 変換中に一時停止・動作モードの切り替え: 変換中の文字を確定し、キーはアプリに通す。戻したらまた受け持つ
        foreach (var change in new Action<Settings>[] { s => s.Enabled = false, s => s.Mode = InputMode.Keyboard })
        {
            using var dll = new Dll();
            dll.Type("kyouha");
            change(dll.Settings);
            var key = dll.Key('a');
            Assert.True(!Active(key) && !Consumed(key), "受け持たないことを伝え、キーはアプリに通す\n" + dll.Trace);
            Assert.Equal("きょうはa", dll.Document, "変換中の文字を確定してからキーを通す\n" + dll.Trace);
            Assert.True(!Consumed(dll.Key('b')), "そのあとのキーもアプリに通す\n" + dll.Trace);
            dll.Settings = new Settings { Mode = InputMode.Tsf, LiveConversion = false };
            Assert.True(Active(dll.Hello()), "戻したら受け持つ\n" + dll.Trace);
            dll.Type("ka");
            Assert.Equal("か", dll.Composition["t1"], dll.Trace);
        }
    }

    [Test]
    public static void AppRule_TurnsTheImeOffForThatApp()
    {
        // アプリ別設定で OFF にしたアプリでは、Meltype IME も何もしない (英字がそのまま入る)
        using var dll = new Dll();
        dll.Settings = new Settings { Mode = InputMode.Tsf, LiveConversion = false, AppRules = [new AppRule { Process = "notepad.exe", Enabled = false }] };
        Assert.True(!Active(dll.Hello("notepad")), dll.Trace);
        Assert.True(Active(dll.Hello("wordpad")), "ほかのアプリでは受け持つ\n" + dll.Trace);
        var key = dll.Key('k', process: "notepad");
        Assert.True(!Active(key) && !Consumed(key), dll.Trace);
        Assert.Equal("k", dll.Document, dll.Trace);
    }
}
