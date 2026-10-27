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
/// <b>このテストが通っても、実際の Windows のデスクトップで打てることは保証しない</b> (docs/TSF_DESIGN.md の「未対応・確かめていないこと」)。
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

        public Dll()
        {
            _service = TipHarness.CreateService(_invoker);
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
                >= 'a' and <= 'z' => (key - 'a' + 'A', (int)key),
                _ => throw new ArgumentException($"送れないキー: {key}"),
            };
            var composing = Composition.GetValueOrDefault(sid) is not null;
            var before = Document.Length > 20 ? Document[^20..] : Document;
            var reply = Send($"{{\"op\":\"key\",\"sid\":{Json(sid)},\"vk\":{vk},\"ch\":{ch},\"mods\":0,\"before\":{Json(before)},\"after\":\"\"," +
                $"\"process\":{Json(process)}{(moved ? ",\"moved\":true" : "")}{(composing ? ",\"composing\":true" : "")}}}");
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

        /// <summary>DLL と同じく、確定し直し (deleteBefore) は入力欄の文字が expect と同じときだけ消し、確定した文字を入れる。表示を置き換える。</summary>
        private void Apply(JsonElement reply, string sid)
        {
            foreach (var commit in reply.GetProperty("commits").EnumerateArray())
            {
                var delete = commit.GetProperty("deleteBefore").GetInt32();
                var expect = commit.TryGetProperty("expect", out var e) ? e.GetString() : null;
                if (delete > 0 && delete <= Document.Length && (expect is null || Document.EndsWith(expect, StringComparison.Ordinal)))
                    Document = Document[..^delete];
                Document += commit.GetProperty("text").GetString();
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

    private static bool Consumed(JsonElement reply) => reply.GetProperty("consumed").GetBoolean();

    private static bool Active(JsonElement reply) => reply.GetProperty("active").GetBoolean();

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
        // 確定したあとキャレットを動かして (moved) 打ったら、前に確定した文字を消して確定し直さない (--tip-e2e の「動かしたあとは直さない」)
        using var dll = new Dll();
        dll.Type("i\n");
        Assert.Equal("い", dll.Document, dll.Trace);
        var replies = new List<JsonElement> { dll.Key('w', moved: true) };
        replies.AddRange(dll.Type("ant "));
        Assert.True(replies.SelectMany(r => r.GetProperty("commits").EnumerateArray()).All(c => c.GetProperty("deleteBefore").GetInt32() == 0),
            "前の確定を消さない\n" + dll.Trace);
        Assert.True(dll.Document.StartsWith('い'), dll.Trace);
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
