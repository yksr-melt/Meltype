// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Ime;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// フロントと Core の境目 (<see cref="ImeHost"/> の JSON) の契約テスト。
/// フロントの代わりの <see cref="Front"/> が、事実だけを送って指示どおりに入力欄 (Document) を書き換える。
/// </summary>
internal static class ImeContractTests
{
    /// <summary>
    /// 何も判断しないフロント。入力欄の文字・変換中の表示・OS の IME の開閉だけを持ち、
    /// キャレットの前後と開閉を事実として送り、返ってきた確定・表示・開閉をそのまま反映する。
    /// </summary>
    private sealed class Front : IImeEnvironment
    {
        private readonly ImeHost _host;
        private readonly List<string> _trace = [];

        public Front() => _host = new ImeHost(this);

        public Settings Settings { get; set; } = new() { LiveConversion = false };

        /// <summary>アプリ別設定・動作モードで受け持たない (TSF では TipServer が決める)。</summary>
        public bool AppEnabled { get; set; } = true;

        public bool Active => Settings.Enabled && AppEnabled;

        public CodeFocus? CodeFocus(bool terminal) => terminal ? Input.CodeFocus.Terminal : Input.CodeFocus.None;

        public MeltypeSession CreateSession() => new(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { CursorIntoBrackets = () => Settings.CursorIntoBrackets, LiveConversion = () => Settings.LiveConversion },
            () => Settings, directModeDetection: true);

        public int Sessions => _host.SessionCount;

        public string Document { get; set; } = "";

        /// <summary>OS の IME の開閉 (TSF の OPENCLOSE)。</summary>
        public bool Open { get; set; } = true;

        public bool Terminal { get; set; }

        public Dictionary<string, string?> Composition { get; } = [];

        /// <summary>入れられなかった確定し直し (deleteBefore が 1 以上で、入力欄の文字が expect と違ったもの)。</summary>
        public List<string> Skipped { get; } = [];

        public JsonElement State { get; private set; }

        public string Trace => "やり取り:\n" + string.Join("\n", _trace);

        public JsonElement Send(string json)
        {
            var reply = _host.Handle(json);
            _trace.Add($"  {json}\n    → {reply}");
            using var document = JsonDocument.Parse(reply);
            return document.RootElement.Clone();
        }

        /// <summary>出来事を送る。キャレットの前は、確定した文字とアプリに通した文字 (Document。変換中の文字は入れない)。</summary>
        public JsonElement Event(string type, string sid = "t1", string extra = "", bool moved = false)
        {
            // キャレットが動いたことは、見つけたときに caret で送る
            if (moved) Event("caret", sid, ",\"caret\":{\"moved\":true,\"passed\":0}");
            var composing = Composition.GetValueOrDefault(sid) is not null;
            var field = $"{{\"before\":{Json(Document)},\"after\":\"\",\"composing\":{(composing ? "true" : "false")},\"terminal\":{(Terminal ? "true" : "false")}}}";
            var output = Send($"{{\"v\":{ImeHost.Version},\"type\":\"{type}\",\"sid\":{Json(sid)},\"open\":{(Open ? "true" : "false")},\"field\":{field}{extra}}}");
            Apply(output, sid);
            return output;
        }

        /// <summary>1 キー送る。' ' = Space、'\n' = Enter、'\b' = BackSpace、'\u001b' = Esc。</summary>
        public JsonElement Key(char key, string sid = "t1", bool moved = false) => key switch
        {
            ' ' => Key(VirtualKeys.Space, sid: sid, moved: moved),
            '\n' => Key(VirtualKeys.Return, sid: sid, moved: moved),
            '\b' => Key(VirtualKeys.Back, sid: sid, moved: moved),
            '\u001b' => Key(VirtualKeys.Escape, sid: sid, moved: moved),
            '[' => Key(0xDB, key, sid: sid, moved: moved),
            ']' => Key(0xDD, key, sid: sid, moved: moved),
            >= 'a' and <= 'z' => Key(key - 'a' + 'A', key, sid: sid, moved: moved),
            _ => throw new ArgumentException($"送れないキー: {key}"),
        };

        public JsonElement Key(int vk, char ch = '\0', bool shift = false, bool ctrl = false, string sid = "t1", bool moved = false)
        {
            var output = Event("key", sid, $",\"key\":{{\"vk\":{vk},\"ch\":{(int)ch},\"shift\":{(shift ? "true" : "false")},\"ctrl\":{(ctrl ? "true" : "false")}}}", moved);
            // 使わなかったキーは、アプリがそのまま入れる
            if (!Consumed(output) && ch != '\0') Document += ch;
            return output;
        }

        public List<JsonElement> Type(string keys, string sid = "t1") => keys.Select(key => Key(key, sid)).ToList();

        /// <summary>
        /// 確定を入れ、表示と開閉を置き換える。確定し直し (deleteBefore が 1 以上) は:
        ///   current (今確定した語) があれば、まず current を入れ、入力欄の前が expect + current のままなら、まとめて text に書き換える。
        ///   current が無ければ、変換中でなく、前の deleteBefore 文字がちょうど expect のときだけ、消してから text を入れる。
        /// </summary>
        public void Apply(JsonElement output, string sid)
        {
            Assert.Equal(ImeHost.Version, output.GetProperty("v").GetInt32(), Trace);
            State = output.GetProperty("state");
            var result = output.GetProperty("result");
            var composing = Composition.GetValueOrDefault(sid) is not null;
            foreach (var commit in result.GetProperty("commits").EnumerateArray())
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
            Composition[sid] = result.GetProperty("view") is { ValueKind: JsonValueKind.Object } view ? view.GetProperty("text").GetString() : null;
            if (Active(output)) Open = State.GetProperty("open").GetBoolean();
        }
    }

    private static string Json(string text) => JsonSerializer.Serialize(text);

    private static bool Consumed(JsonElement output) => output.GetProperty("result").GetProperty("consumed").GetBoolean();

    private static bool Active(JsonElement output) => output.GetProperty("state").GetProperty("active").GetBoolean();

    private static bool Japanese(JsonElement output) => output.GetProperty("state").GetProperty("japanese").GetBoolean();

    private static bool Indicator(JsonElement output) => output.GetProperty("state").GetProperty("indicator").GetBoolean();

    private static JsonElement Capture(JsonElement output) => output.GetProperty("state").GetProperty("capture");

    private static JsonElement Commits(JsonElement output) => output.GetProperty("result").GetProperty("commits");

    [Test]
    public static void Type_Convert_Commit_ThenTypeAgain()
    {
        var front = new Front();
        Assert.True(Active(front.Event("sync")), "受け持つ\n" + front.Trace);
        Assert.Equal(0, front.Sessions, "sync では入力の本体を作らない\n" + front.Trace);
        Assert.True(front.Type("kyouha").All(Consumed), "打った英字はアプリに通さない\n" + front.Trace);
        Assert.Equal("きょうは", front.Composition["t1"], front.Trace);
        var space = front.Key(' ');
        Assert.True(Consumed(space) && space.GetProperty("result").GetProperty("view").GetProperty("converting").GetBoolean(), "Space で変換を始める\n" + front.Trace);
        front.Key('\n');
        Assert.True(front.Composition["t1"] is null, "Enter で確定して表示を閉じる\n" + front.Trace);
        Assert.Equal("今日は", front.Document, front.Trace);
        Assert.True(front.Type("aiueo").All(Consumed), "確定のあとも続けて打てる\n" + front.Trace);
        front.Key('\n');
        Assert.Equal("今日はあいうえお", front.Document, front.Trace);
        Assert.True(!Consumed(front.Key('\n')), "打っていないときの Enter はアプリに通す\n" + front.Trace);
    }

    [Test]
    public static void BackSpace_And_Esc()
    {
        var front = new Front();
        front.Type("kyouhaa");
        Assert.True(Consumed(front.Key('\b')), "変換中の BackSpace は Meltype が使う\n" + front.Trace);
        Assert.Equal("きょうは", front.Composition["t1"], front.Trace);
        front.Key('\n');
        front.Type("aiu");
        front.Key('\u001b');
        Assert.True(front.Composition["t1"] is null, "Esc で打っていた文字を消す\n" + front.Trace);
        Assert.Equal("きょうは", front.Document, "Esc では何も入れない\n" + front.Trace);
        Assert.True(!Consumed(front.Key('\b')), "打っていないときの BackSpace はアプリに通す\n" + front.Trace);
    }

    [Test]
    public static void Commit_EndsTheComposition()
    {
        // クリック・フォーカスの移動・アプリの確定では、フロントが commit (moved) を送る
        var front = new Front();
        front.Type("kyouha");
        var commit = front.Event("commit", moved: true);
        Assert.Equal("きょうは", front.Document, front.Trace);
        Assert.True(front.Composition["t1"] is null && commit.GetProperty("result").GetProperty("view").ValueKind == JsonValueKind.Null, "確定したら表示を閉じる\n" + front.Trace);
        Assert.Equal(0, Commits(front.Event("commit")).GetArrayLength(), "打っていなければ何も確定しない\n" + front.Trace);
    }

    [Test]
    public static void CaretMoved_DoesNotRewriteThePreviousCommit()
    {
        // i Space want Space と続けて打つと、前の「い」を英語の I に確定し直す (#975)。キャレットを動かしてから打ったときは確定し直さない
        static (Front Front, List<JsonElement> Outputs) TypeWantAfterI(bool moved)
        {
            var front = new Front();
            front.Type("i ");
            if (moved) front.Event("commit", moved: true);
            var outputs = new List<JsonElement> { front.Key('w', moved: moved) };
            outputs.AddRange(front.Type("ant "));
            return (front, outputs);
        }
        static bool Rewrites(List<JsonElement> outputs) =>
            outputs.SelectMany(o => Commits(o).EnumerateArray()).Any(c => c.GetProperty("deleteBefore").GetInt32() > 0);

        var (still, stillOutputs) = TypeWantAfterI(moved: false);
        Assert.True(Rewrites(stillOutputs), "動かさなければ、前の語を確定し直す\n" + still.Trace);
        Assert.Equal("I want ", still.Document, still.Trace);
        Assert.Equal(0, still.Skipped.Count, "入れられなかった確定し直し: " + string.Join(", ", still.Skipped) + "\n" + still.Trace);
        var (moved, movedOutputs) = TypeWantAfterI(moved: true);
        Assert.True(!Rewrites(movedOutputs), "動かしたら、前の確定を消さない\n" + moved.Trace);
        Assert.True(moved.Document.EndsWith("want ", StringComparison.Ordinal) && !moved.Document.StartsWith("I ", StringComparison.Ordinal), moved.Trace);
    }

    [Test]
    public static void Rewrites_AlwaysMatchWhatTheFrontCanApply()
    {
        // キャレットを動かさずに続けて打つ流れで、フロントが入れられない確定し直しを送らない
        var front = new Front();
        front.Type("i\n");
        front.Type("want ");
        front.Type("kyouha\n");
        Assert.Equal("いwant きょうは", front.Document, front.Trace);
        Assert.Equal(0, front.Skipped.Count, "入れられなかった確定し直し: " + string.Join(", ", front.Skipped) + "\n" + front.Trace);
    }

    [Test]
    public static void Brackets_ReturnCaretBackWithTheCommit()
    {
        foreach (var enabled in new[] { false, true })
        {
            foreach (var convert in new[] { false, true })
            {
                var front = new Front();
                front.Settings.CursorIntoBrackets = enabled;
                front.Type(convert ? "[] " : "[]");
                var enter = front.Key('\n');
                Assert.True(Consumed(enter), "確定に使った Enter はアプリに通さない\n" + front.Trace);
                Assert.Equal("「」", front.Document, front.Trace);
                Assert.Equal(enabled, Commits(enter)[0].TryGetProperty("caretBack", out var caret), front.Trace);
                if (enabled) Assert.Equal(1, caret.GetInt32(), front.Trace);
            }
            var next = new Front();
            next.Settings.CursorIntoBrackets = enabled;
            next.Type("[] ");
            var k = next.Key('k');
            Assert.Equal("「」", Commits(k)[0].GetProperty("text").GetString(), next.Trace);
            Assert.Equal("k", k.GetProperty("result").GetProperty("view").GetProperty("text").GetString(), "括弧の確定と続きの表示を一緒に返す\n" + next.Trace);
        }
    }

    [Test]
    public static void InputFields_AreSeparatedBySid()
    {
        var front = new Front();
        front.Type("ka", "t1");
        front.Type("ki", "t2");
        Assert.Equal("か", front.Composition["t1"], front.Trace);
        Assert.Equal("き", front.Composition["t2"], front.Trace);
        front.Event("close", "t2");
        Assert.Equal(1, front.Sessions, "閉じた入力欄の入力の本体は捨てる\n" + front.Trace);
        front.Key('\n', "t1");
        Assert.Equal("か", front.Document, front.Trace);
    }

    [Test]
    public static void UnknownCompositionsAndProtocols_AreNotTakenOver()
    {
        var front = new Front();
        // つなぎ直したフロントが「変換中」と言っても、続きを知らない入力は受け持たない
        front.Composition["lost"] = "か";
        var lost = front.Key('a', sid: "lost");
        Assert.True(!Consumed(lost), "キーはアプリに通す\n" + front.Trace);
        Assert.Equal(0, front.Sessions, front.Trace);
        foreach (var json in new[]
                 {
                     "{\"op\":\"key\",\"sid\":\"t1\",\"vk\":65,\"ch\":97}",
                     $"{{\"v\":{ImeHost.Version + 1},\"type\":\"key\",\"sid\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"v\":{ImeHost.Version},\"type\":\"jump\",\"sid\":\"t1\"}}",
                     $"{{\"v\":{ImeHost.Version},\"type\":\"key\",\"sid\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}},\"code\":true}}",
                     "not json",
                 })
        {
            var output = front.Send(json);
            Assert.True(!Active(output) && !Consumed(output), "版の違う・知らない出来事は受け持たない: " + json);
        }
        Assert.Equal(0, front.Sessions, front.Trace);
    }

    [Test]
    public static void Deactivated_WhileComposing_CommitsAndPassesTheKey()
    {
        foreach (var change in new Action<Front>[] { f => f.Settings.Enabled = false, f => f.AppEnabled = false })
        {
            var front = new Front();
            front.Type("kyouha");
            change(front);
            var key = front.Key('a');
            Assert.True(!Active(key) && !Consumed(key), "受け持たないことを伝え、キーはアプリに通す\n" + front.Trace);
            Assert.Equal("きょうはa", front.Document, "変換中の文字を確定してからキーを通す\n" + front.Trace);
            Assert.True(!Consumed(front.Key('b')), front.Trace);
            Assert.Equal(0, Capture(front.Event("sync")).GetProperty("keys").GetArrayLength(), "受け持たないときはキーを受け取らせない\n" + front.Trace);
            front.Settings.Enabled = true;
            front.AppEnabled = true;
            front.Type("ka");
            Assert.Equal("か", front.Composition["t1"], "戻したら受け持つ\n" + front.Trace);
        }
    }

    [Test]
    public static void Hankaku_TogglesTheShownMode()
    {
        var front = new Front();
        var off = front.Key(ToggleShortcut.Hankaku);
        Assert.True(Consumed(off) && !Japanese(off) && !front.Open && Indicator(off), "日本語 → 英数 (IME を閉じる)\n" + front.Trace);
        Assert.True(!Consumed(front.Key('a')), "英数ではアプリに通す\n" + front.Trace);
        var on = front.Key(VirtualKeys.OemEnlw);
        Assert.True(Japanese(on) && front.Open && Indicator(on), "JIS の 半角/全角 のキーコードも同じ\n" + front.Trace);
        front.Type("ka");
        var composing = front.Key(ToggleShortcut.Hankaku);
        Assert.True(Consumed(composing) && front.Open && !Indicator(composing), "変換中はモードを変えない (変換ボックスで使う)\n" + front.Trace);
    }

    [Test]
    public static void Hankaku_WithoutTheShortcut_IsSwallowed()
    {
        var front = new Front();
        front.Settings.ToggleShortcuts = [new() { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.Control }];
        var hankaku = front.Key(ToggleShortcut.Hankaku);
        Assert.True(Consumed(hankaku) && Japanese(hankaku) && !Indicator(hankaku), "切り替えず、アプリにも通さない\n" + front.Trace);
        var toggle = front.Key(VirtualKeys.Space, ctrl: true);
        Assert.True(Consumed(toggle) && !Japanese(toggle), "登録したキーで切り替える\n" + front.Trace);
        var keys = Capture(toggle).GetProperty("keys").EnumerateArray().Select(k => (k.GetProperty("key").GetInt32(), k.GetProperty("mods").GetInt32())).ToList();
        Assert.True(keys.Contains((VirtualKeys.Space, 2)) && keys.Contains((ToggleShortcut.Hankaku, 0)), "受け取るキーに切り替えキーと 半角/全角 を入れる\n" + front.Trace);
    }

    [Test]
    public static void EisuAndKana_SetTheMode()
    {
        var front = new Front();
        front.Type("ka");
        var eisu = front.Key(VirtualKeys.OemAttn);
        Assert.True(!front.Open && !Japanese(eisu) && Indicator(eisu), "英数にする\n" + front.Trace);
        Assert.Equal("か", front.Document, "変換中なら確定してから\n" + front.Trace);
        Assert.True(Consumed(front.Key(VirtualKeys.OemAttn)) && !front.Open, "英数のままでも英数\n" + front.Trace);
        var kana = front.Key(VirtualKeys.Kana);
        Assert.True(front.Open && Japanese(kana), "かなで日本語\n" + front.Trace);
        Assert.True(!Indicator(front.Key(VirtualKeys.Kana)), "変わらなければ表示しない\n" + front.Trace);
    }

    [Test]
    public static void ConvertKeys_SwitchOnlyWithTheSetting()
    {
        var front = new Front();
        front.Key(VirtualKeys.NonConvert);
        Assert.True(front.Open, "設定が OFF なら切り替えない\n" + front.Trace);
        front.Settings.ConvertKeysSwitchKeyboard = true;
        front.Key(VirtualKeys.NonConvert);
        Assert.True(!front.Open, "無変換で英数\n" + front.Trace);
        front.Key(VirtualKeys.Convert);
        Assert.True(front.Open, "変換で日本語\n" + front.Trace);
    }

    [Test]
    public static void OsOpenState_IsFollowed()
    {
        // アプリ・Windows が IME を閉じた (パスワード欄への移動など): 次の出来事の open で本体のモードを合わせる
        var front = new Front();
        front.Type("ka\n");
        front.Open = false;
        var closed = front.Event("caret");
        Assert.True(!Japanese(closed) && !Indicator(closed), "合わせるだけで表示はしない\n" + front.Trace);
        Assert.True(!Consumed(front.Key('a')), front.Trace);
        front.Open = true;
        Assert.True(Consumed(front.Key('k')), front.Trace);
    }

    [Test]
    public static void Capture_FollowsTheModeAndSettings()
    {
        var front = new Front();
        var japanese = Capture(front.Event("sync"));
        Assert.True(japanese.GetProperty("word").GetBoolean() && japanese.GetProperty("symbol").GetBoolean() && !japanese.GetProperty("space").GetBoolean(),
            "日本語では文字を受け取り、半角の Space は通す\n" + front.Trace);
        front.Settings.SpaceWidth = SpaceWidth.FullWidth;
        Assert.True(Capture(front.Event("sync")).GetProperty("space").GetBoolean(), "全角スペースなら Space も受け取る\n" + front.Trace);
        front.Open = false;
        var english = Capture(front.Event("sync"));
        Assert.True(english.GetProperty("word").GetBoolean(), "英数でもローマ字を見分けるので受け取る\n" + front.Trace);
        front.Settings.DirectModeAutoDetect = false;
        english = Capture(front.Event("sync"));
        Assert.True(!english.GetProperty("word").GetBoolean() && !english.GetProperty("symbol").GetBoolean(), "見分けないなら通す\n" + front.Trace);
        Assert.True(english.GetProperty("keys").GetArrayLength() > 0, "英数でも切り替えキーは受け取る\n" + front.Trace);
    }

    [Test]
    public static void Toggle_InTheCodeLine_KeepsTheImeOpen()
    {
        var front = new Front { Terminal = true, Document = "let x = 1; " };
        var line = front.Key('\u001b');
        Assert.True(!Japanese(line) && front.Open, "コードの行は IME を開いたまま英数\n" + front.Trace);
        var japanese = front.Key(ToggleShortcut.Hankaku);
        Assert.True(Japanese(japanese) && front.Open && Indicator(japanese), "コードの行を日本語にする\n" + front.Trace);
        var back = front.Event("toggle");
        Assert.True(!Japanese(back) && front.Open && Indicator(back), "もう一度で英数に戻す (IME は開いたまま)\n" + front.Trace);
        var closed = front.Event("toggle");
        Assert.True(Japanese(closed) && front.Open, "英数に見えているので、次は日本語にする\n" + front.Trace);
    }

    [Test]
    public static void CodeLine_IsDecidedOnlyByTheTextBeforeTheCaret()
    {
        // 通したキーから行を追いかけない: アプリが文字を入れたあとの位置は、フロントが caret で送る before で決める
        var front = new Front { Terminal = true, Document = "echo " };
        Assert.True(!Japanese(front.Key('\u001b')), "コードの行は英数\n" + front.Trace);
        Assert.True(!Consumed(front.Key(0xDE, '"')), "引用符はアプリへ\n" + front.Trace);
        Assert.True(Japanese(front.Event("caret", extra: ",\"caret\":{\"passed\":222}")), "アプリに入った引用符の後は日本語\n" + front.Trace);
        front.Type("kyouha");
        var close = front.Key(0xDE, '"');
        Assert.True(!Consumed(close) && front.Document == "echo \"きょうは\"", "閉じる引用符は確定してからアプリへ\n" + front.Trace);
        Assert.True(!Japanese(front.Event("caret", extra: ",\"caret\":{\"passed\":222}")), "閉じたあとは英数\n" + front.Trace);
        var unknown = front.Send($"{{\"v\":{ImeHost.Version},\"type\":\"key\",\"sid\":\"t1\",\"open\":true,\"field\":{{\"terminal\":true}},\"key\":{{\"vk\":75,\"ch\":107}}}}");
        Assert.True(!Consumed(unknown), "キャレットの前を読めないコードの入力欄は、コードの行として英数\n" + front.Trace);
    }
}
