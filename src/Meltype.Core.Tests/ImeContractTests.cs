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

        public Front() => _host = new ImeHost(this, () => Settings);

        public Settings Settings { get; set; } = new() { LiveConversion = false };

        /// <summary>このフロントが Windows のどちらの入力方式か (null なら入力方式が 1 つしかない OS)。</summary>
        public InputMode? InputMethod { get; set; }

        /// <summary>予測変換の候補を出すか (英語の語の補完)。</summary>
        public bool Predicts { get; init; }

        public MeltypeSession CreateSession(Func<Settings> settings) => new(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions().WithSettings(settings) with { Predictor = Predicts ? new Predictor(null, null, null) : null }, settings, directModeDetection: true);

        /// <summary>再変換の読み (OS の機能の代わり)。</summary>
        public Dictionary<string, string> Readings { get; } = [];

        public string? ReadingOf(string text) => Readings.GetValueOrDefault(text);

        public SnippetStore Snippets { get; } = new(null);

        public string? ClipboardText => null;

        public (string Title, string Artist)? NowPlaying => null;

        public int Sessions => _host.SessionCount;

        public string Document { get; set; } = "";

        /// <summary>OS の IME の開閉 (TSF の OPENCLOSE)。</summary>
        public bool Open { get; set; } = true;

        /// <summary>入力欄の性質 (OS が知らせるもの)。</summary>
        public List<string> Traits { get; } = [];

        public bool Terminal
        {
            set
            {
                if (value) Traits.Add("Terminal");
            }
        }

        public string? App { get; set; }

        /// <summary>入力欄で選んでいる文字。</summary>
        public string? Selection { get; set; }

        public Dictionary<string, string?> Composition { get; } = [];

        /// <summary>本体が出した、入力欄と合わない確定し直し (消す文字 expect が入力欄の末尾に無いもの)。</summary>
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
        public JsonElement Event(string type, string instance = "t1", string extra = "", bool moved = false)
        {
            // キャレットが動いたことは、見つけたときに caret で送る
            if (moved) Event("CaretMoved", instance, ",\"caret\":{\"moved\":true,\"passedKey\":0}");
            var composing = Composition.GetValueOrDefault(instance) is not null;
            var field = $"{{\"before\":{Json(Document)},\"after\":\"\",\"composing\":{(composing ? "true" : "false")},\"traits\":{JsonSerializer.Serialize(Traits)}" +
                (App is null ? "" : $",\"app\":{Json(App)}") + (Selection is null ? "" : $",\"selection\":{Json(Selection)}") + "}";
            var output = Send($"{{\"v\":{ImeHost.Version},\"type\":\"{type}\",\"instance\":{Json(instance)},\"open\":{(Open ? "true" : "false")},\"field\":{field}{extra}}}");
            Apply(output, instance);
            return output;
        }

        /// <summary>1 キー送る。' ' = Space、'\n' = Enter、'\b' = BackSpace、'\u001b' = Esc。</summary>
        public JsonElement Key(char key, string instance = "t1", bool moved = false) => key switch
        {
            ' ' => Key(VirtualKeys.Space, instance: instance, moved: moved),
            '\n' => Key(VirtualKeys.Return, instance: instance, moved: moved),
            '\b' => Key(VirtualKeys.Back, instance: instance, moved: moved),
            '\u001b' => Key(VirtualKeys.Escape, instance: instance, moved: moved),
            '[' => Key(0xDB, key, instance: instance, moved: moved),
            ']' => Key(0xDD, key, instance: instance, moved: moved),
            >= 'a' and <= 'z' => Key(key - 'a' + 'A', key, instance: instance, moved: moved),
            _ => throw new ArgumentException($"送れないキー: {key}"),
        };

        public JsonElement Key(int vk, char ch = '\0', bool shift = false, bool ctrl = false, string instance = "t1", bool moved = false)
        {
            var output = Event("KeyPressed", instance, $",\"key\":{{\"vk\":{vk},\"ch\":{(int)ch},\"shift\":{(shift ? "true" : "false")},\"ctrl\":{(ctrl ? "true" : "false")}}}", moved);
            // 使わなかったキーは、アプリがそのまま入れる
            if (!Consumed(output) && ch != '\0') Document += ch;
            return output;
        }

        public List<JsonElement> Type(string keys, string instance = "t1") => keys.Select(key => Key(key, instance)).ToList();

        /// <summary>
        /// 確定を入れ、表示と開閉を置き換える。確定し直し (remove がある) は照合せずに、remove の分を消してから text を入れる
        /// (照合は本体の仕事。消す文字が入力欄の末尾に無ければ、本体が約束を破ったので Skipped に残して入れない)。
        /// </summary>
        public void Apply(JsonElement output, string instance)
        {
            Assert.Equal(ImeHost.Version, output.GetProperty("v").GetInt32(), Trace);
            State = output.GetProperty("state");
            var result = output.GetProperty("result");
            foreach (var commit in result.GetProperty("commits").EnumerateArray())
            {
                var text = commit.GetProperty("text").GetString()!;
                if (commit.TryGetProperty("remove", out var remove))
                {
                    var expect = remove.GetString()!;
                    if (!Document.EndsWith(expect, StringComparison.Ordinal))
                    {
                        Skipped.Add(commit.GetRawText());
                        continue;
                    }
                    Document = Document[..^expect.Length];
                }
                Document += text;
            }
            Composition[instance] = result.GetProperty("view") is { ValueKind: JsonValueKind.Object } view ? view.GetProperty("text").GetString() : null;
            if (Active(output)) Open = State.GetProperty("open").GetBoolean();
        }
    }

    private static string Json(string text) => JsonSerializer.Serialize(text);

    /// <summary>入力欄にフォーカスが入った (caret の出来事に付ける)。</summary>
    private const string Entered = ",\"caret\":{\"moved\":true,\"passedKey\":0,\"focusEntered\":true}";

    private static bool Consumed(JsonElement output) => output.GetProperty("result").GetProperty("consumed").GetBoolean();

    private static bool Active(JsonElement output) => output.GetProperty("state").GetProperty("active").GetBoolean();

    private static bool Japanese(JsonElement output) => output.GetProperty("state").GetProperty("japanese").GetBoolean();

    private static bool Indicator(JsonElement output) => output.GetProperty("state").GetProperty("indicatorMs").GetInt32() > 0;

    private static JsonElement Capture(JsonElement output) => output.GetProperty("state").GetProperty("capture");

    private static JsonElement Commits(JsonElement output) => output.GetProperty("result").GetProperty("commits");

    [Test]
    public static void Type_Convert_Commit_ThenTypeAgain()
    {
        var front = new Front();
        Assert.True(Active(front.Event("StateRequested")), "受け持つ\n" + front.Trace);
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
        var commit = front.Event("CommitRequested", moved: true);
        Assert.Equal("きょうは", front.Document, front.Trace);
        Assert.True(front.Composition["t1"] is null && commit.GetProperty("result").GetProperty("view").ValueKind == JsonValueKind.Null, "確定したら表示を閉じる\n" + front.Trace);
        Assert.Equal(0, Commits(front.Event("CommitRequested")).GetArrayLength(), "打っていなければ何も確定しない\n" + front.Trace);
    }

    [Test]
    public static void CaretMoved_DoesNotRewriteThePreviousCommit()
    {
        // i Space want Space と続けて打つと、前の「い」を英語の I に確定し直す (#975)。キャレットを動かしてから打ったときは確定し直さない
        static (Front Front, List<JsonElement> Outputs) TypeWantAfterI(bool moved)
        {
            var front = new Front();
            front.Type("i ");
            if (moved) front.Event("CommitRequested", moved: true);
            var outputs = new List<JsonElement> { front.Key('w', moved: moved) };
            outputs.AddRange(front.Type("ant "));
            return (front, outputs);
        }
        static bool Rewrites(List<JsonElement> outputs) =>
            outputs.SelectMany(o => Commits(o).EnumerateArray()).Any(c => c.TryGetProperty("remove", out _));

        var (still, stillOutputs) = TypeWantAfterI(moved: false);
        Assert.True(Rewrites(stillOutputs), "動かさなければ、前の語を確定し直す\n" + still.Trace);
        Assert.Equal("I want ", still.Document, still.Trace);
        Assert.Equal(0, still.Skipped.Count, "入力欄と合わない確定し直し: " + string.Join(", ", still.Skipped) + "\n" + still.Trace);
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
        Assert.Equal(0, front.Skipped.Count, "入力欄と合わない確定し直し: " + string.Join(", ", front.Skipped) + "\n" + front.Trace);
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
        front.Event("InstanceDestroyed", "t2");
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
        var lost = front.Key('a', instance: "lost");
        Assert.True(!Consumed(lost), "キーはアプリに通す\n" + front.Trace);
        Assert.Equal(0, front.Sessions, front.Trace);
        foreach (var json in new[]
                 {
                     "{\"op\":\"key\",\"instance\":\"t1\",\"vk\":65,\"ch\":97}",
                     $"{{\"v\":{ImeHost.Version + 1},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"v\":{ImeHost.Version},\"type\":\"jump\",\"instance\":\"t1\"}}",
                     $"{{\"v\":{ImeHost.Version},\"type\":\"key\",\"instance\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"v\":{ImeHost.Version},\"type\":99,\"instance\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"v\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"field\":{{\"traits\":[\"Hologram\"]}},\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"v\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"field\":{{\"traits\":[42]}},\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"v\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}},\"code\":true}}",
                     "not json",
                 })
        {
            var output = front.Send(json);
            Assert.True(!Active(output) && !Consumed(output), "版の違う・知らない出来事・知らない性質のある出来事は受け持たない: " + json);
        }
        Assert.Equal(0, front.Sessions, front.Trace);
        var noInstance = front.Send($"{{\"v\":{ImeHost.Version},\"type\":\"KeyPressed\",\"key\":{{\"vk\":65,\"ch\":97}}}}");
        Assert.True(Active(noInstance) && Consumed(noInstance), "instance を省いた出来事も、空の instance の入力欄として受け持つ");
    }

    [Test]
    public static void Deactivated_WhileComposing_CommitsAndPassesTheKey()
    {
        foreach (var change in new Action<Front>[] { f => f.Settings.Enabled = false, f => f.InputMethod = InputMode.Keyboard, f =>
        {
            f.Settings.AppRules.Add(new AppRule { Process = "off.exe", Enabled = false });
            f.App = "off.exe";
        } })
        {
            var front = new Front();
            front.Type("kyouha");
            change(front);
            var key = front.Key('a');
            Assert.True(!Active(key) && !Consumed(key), "受け持たないことを伝え、キーはアプリに通す\n" + front.Trace);
            Assert.Equal("きょうはa", front.Document, "変換中の文字を確定してからキーを通す\n" + front.Trace);
            Assert.True(!Consumed(front.Key('b')), front.Trace);
            Assert.Equal(0, Capture(front.Event("StateRequested")).GetProperty("keys").GetArrayLength(), "受け持たないときはキーを受け取らせない\n" + front.Trace);
            front.Settings.Enabled = true;
            front.InputMethod = null;
            front.App = null;
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
        var repeated = front.Event("KeyPressed", extra: $",\"key\":{{\"vk\":{ToggleShortcut.Hankaku},\"repeat\":true}}");
        Assert.True(Consumed(repeated) && front.Open && !Indicator(repeated), "押しっぱなしの繰り返しでは切り替えない\n" + front.Trace);
        front.Type("ka");
        var composing = front.Key(ToggleShortcut.Hankaku);
        Assert.True(Consumed(composing) && front.Open && !Indicator(composing), "変換中はモードを変えない (変換ボックスで使う)\n" + front.Trace);
    }

    [Test]
    public static void Hankaku_WithoutTheShortcut_IsSwallowed()
    {
        var front = new Front();
        front.Settings.ToggleShortcuts = [new() { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.Control }, new() { Key = VirtualKeys.LMenu }];
        var hankaku = front.Key(ToggleShortcut.Hankaku);
        Assert.True(Consumed(hankaku) && Japanese(hankaku) && !Indicator(hankaku), "切り替えず、アプリにも通さない\n" + front.Trace);
        var toggle = front.Key(VirtualKeys.Space, ctrl: true);
        Assert.True(Consumed(toggle) && !Japanese(toggle), "登録したキーで切り替える\n" + front.Trace);
        var keys = Capture(toggle).GetProperty("keys").EnumerateArray().Select(k => (k.GetProperty("key").GetInt32(), k.GetProperty("mods").GetInt32())).ToList();
        Assert.True(keys.Contains((VirtualKeys.Space, 2)) && keys.Contains((ToggleShortcut.Hankaku, 0)), "受け取るキーに切り替えキーと 半角/全角 を入れる\n" + front.Trace);
        Assert.True(!keys.Contains((VirtualKeys.LMenu, 0)), "修飾キー単独の割り当ては受け取らせない\n" + front.Trace);
    }

    [Test]
    public static void ToggleShortcuts_WhileComposing()
    {
        // #1033: Shift+Space を切り替えキーにしても、変換中は「前の候補へ」(docs/USAGE.md)。入力中は英字表示の切り替え
        var front = new Front();
        front.Settings.ToggleShortcuts = [new() { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.Shift }];
        var shiftSpace = front.Key(VirtualKeys.Space, ' ', shift: true);
        Assert.True(Consumed(shiftSpace) && !front.Open && Commits(shiftSpace).GetArrayLength() == 0, "全角スペースを確定せずに英数にする\n" + front.Trace);
        front.Key(VirtualKeys.Space, ' ', shift: true);
        Assert.True(front.Open, "もう一度で日本語に戻る\n" + front.Trace);
        front.Type("kyou");
        var english = front.Key(VirtualKeys.Space, ' ', shift: true);
        Assert.True(front.Open && Commits(english).GetArrayLength() == 0 && front.Composition["t1"] == "kyou",
            "入力中は英字表示にする\n" + front.Trace);
        front.Key(VirtualKeys.Space, ' ', shift: true);
        front.Key(' ');
        var previous = front.Key(VirtualKeys.Space, ' ', shift: true);
        Assert.True(front.Open && Commits(previous).GetArrayLength() == 0 && front.Composition["t1"] is not null, "変換中は切り替えず、確定もしない\n" + front.Trace);
        front.Key('\n');
        front.Key(VirtualKeys.Space, ' ', shift: true, ctrl: true);
        Assert.True(front.Open, "Ctrl+Shift+Space は Shift+Space の割り当てではない\n" + front.Trace);
        front.Settings.ToggleShortcuts = [];
        var fullWidth = front.Key(VirtualKeys.Space, ' ', shift: true);
        Assert.Equal("　", Commits(fullWidth)[0].GetProperty("text").GetString(), "割り当てが無ければ全角スペース\n" + front.Trace);
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
    public static void ConvertKeys_SetTheMode()
    {
        var front = new Front();
        front.Key(VirtualKeys.NonConvert);
        Assert.True(!front.Open, "無変換で英数\n" + front.Trace);
        front.Key(VirtualKeys.Convert);
        Assert.True(front.Open, "変換で日本語\n" + front.Trace);
        var repeat = front.Send($"{{\"v\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"key\":{{\"vk\":{VirtualKeys.NonConvert},\"repeat\":true}}}}");
        Assert.True(Consumed(repeat) && repeat.GetProperty("state").GetProperty("open").GetBoolean(), "押しっぱなしでは切り替えない\n" + front.Trace);
        // 親指シフトの配列では親指キー (変換ボックスで使う)
        front.Settings.InputStyle = InputStyle.Kana;
        front.Settings.KanaLayout = KanaLayout.Nicola;
        front.Key(VirtualKeys.NonConvert);
        Assert.True(front.Open, "NICOLA の無変換は切り替えない\n" + front.Trace);
    }

    [Test]
    public static void ConvertKey_ReconvertsTheSelection()
    {
        var front = new Front { Document = "a ", Selection = "kyouha" };
        var convert = front.Key(VirtualKeys.Convert);
        Assert.True(Consumed(convert) && front.Composition["t1"] is not null, "ローマ字のまま入った文を変換ボックスに入れる\n" + front.Trace);
        front.Selection = null;
        front.Key('\n');
        Assert.Equal("a 今日は", front.Document, "選んでいた文字は、入れた文字でアプリが置き換える\n" + front.Trace);
        // 読みが分からない文字は再変換しない
        front = new Front { Selection = "漢字" };
        front.Key(VirtualKeys.Convert);
        Assert.True(front.Composition["t1"] is null && front.Open, front.Trace);
    }

    [Test]
    public static void AltTap_SetsTheModeWithTheSetting()
    {
        var front = new Front();
        string Tap(int vk) => $"{{\"v\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"field\":{{\"before\":\"\"}},\"key\":{{\"vk\":{vk},\"tappedAlone\":true}}}}";
        Assert.True(!Consumed(front.Send(Tap(VirtualKeys.LMenu))), "設定が OFF ならアプリに通す (メニューバーに移る)\n" + front.Trace);
        front.Settings.AltKeysSwitchKeyboard = true;
        front.Type("ka");
        var left = front.Send(Tap(VirtualKeys.LMenu));
        front.Apply(left, "t1");
        Assert.True(Consumed(left) && !front.Open && front.Document == "か", "左 Alt で、変換中の文字を確定して英数\n" + front.Trace);
        var right = front.Send(Tap(VirtualKeys.RMenu));
        Assert.True(Consumed(right) && right.GetProperty("state").GetProperty("open").GetBoolean(), "右 Alt で日本語\n" + front.Trace);
        Assert.True(!Consumed(front.Send(Tap(VirtualKeys.LControl))), "ほかの修飾キーは使わない\n" + front.Trace);
    }

    [Test]
    public static void InputFieldTraits_DecideWhatCoreDoes()
    {
        var front = new Front();
        front.Traits.Add("Password");
        Assert.True(!Active(front.Key('a')) && front.Document == "a", "パスワード欄では受け持たない\n" + front.Trace);
        front = new Front();
        front.Traits.Add("Game");
        Assert.True(!Active(front.Key('a')), "ゲームでは受け持たない (設定「ゲームでは止める」)\n" + front.Trace);
        front = new Front();
        front.Settings.AppKinds.Add(new AppKind { Name = "英数から", StartInEnglish = true });
        front.Settings.AppRules.Add(new AppRule { Process = "term.exe", Enabled = true, Kind = "英数から" });
        front.App = "term.exe";
        front.Event("StateRequested");
        Assert.True(!front.Key('k').GetProperty("state").GetProperty("open").GetBoolean(), "「最初は英数」のアプリに移ったら英数\n" + front.Trace);
    }

    [Test]
    public static void EventsThatDoNotChangeTheInput_KeepTheView()
    {
        // 変換中に OS がフォーカス・キャレットを知らせてきても、変換中の表示は消さない (view が null ならフロントは消す)
        var front = new Front();
        front.Type("ka");
        foreach (var (type, extra) in new[] { ("CaretMoved", Entered), ("StateRequested", ""), ("ConversionFinished", "") })
            Assert.Equal("か", front.Event(type, extra: extra).GetProperty("result").GetProperty("view").GetProperty("text").GetString(), type + "\n" + front.Trace);
        front.Event("CaretMoved", extra: ",\"caret\":{\"moved\":false}");
        Assert.Equal("か", front.Composition["t1"], "caret\n" + front.Trace);
    }

    [Test]
    public static void View_ShowsWhatWillBeCommitted()
    {
        // どのフロントも text をそのまま出す。確定される形 (OS が表示のまま確定しても同じになる) で、打っている途中の英字は typed で見える
        var front = new Front();
        front.Settings.ShowTypedKeys = true;
        var view = front.Type("kan")[^1].GetProperty("result").GetProperty("view");
        Assert.Equal("かん", view.GetProperty("text").GetString(), front.Trace);
        Assert.Equal("kan", view.GetProperty("typed").GetString(), front.Trace);
        Assert.True(!view.TryGetProperty("committed", out _), "見せる文字は 1 つだけ\n" + front.Trace);
    }

    [Test]
    public static void List_IsOneAndSelectReadsTheLastOne()
    {
        // 入力中の変換の候補 (設定「入力中も変換候補をいくつか表示」) も、変換の候補と同じ一覧で渡す。select の番号は前の応答の一覧で読む
        var front = new Front();
        front.Settings.ShowCandidatesWhileTyping = true;
        var list = front.Type("kyou")[^1].GetProperty("result").GetProperty("view").GetProperty("list");
        Assert.True(list.ValueKind == JsonValueKind.Object && list.GetProperty("items").GetArrayLength() > 0 && !list.TryGetProperty("prediction", out _),
            "入力中も一覧を出す\n" + front.Trace);
        var second = list.GetProperty("items")[1].GetString();
        var selected = front.Event("CandidateClicked", extra: ",\"index\":1");
        Assert.Equal(second, selected.GetProperty("result").GetProperty("view").GetProperty("text").GetString(), "見えていた一覧の 2 番目\n" + front.Trace);
    }

    [Test]
    public static void List_IsNumberedOnlyWhenNumberKeysSelect()
    {
        // 数字キーで選べるのは変換中の候補だけ。入力中の一覧の数字は、そのまま打つ文字になる
        var front = new Front();
        front.Settings.ShowCandidatesWhileTyping = true;
        static bool Numbered(JsonElement output) => output.GetProperty("result").GetProperty("view").GetProperty("list").GetProperty("numbered").GetBoolean();
        Assert.True(!Numbered(front.Type("kyou")[^1]), "入力中\n" + front.Trace);
        Assert.True(Numbered(front.Key(' ')), "変換中\n" + front.Trace);
    }

    [Test]
    public static void Select_HighlightDoesNotCommitAPrediction()
    {
        // マウスでなぞった (CandidateHighlighted) だけでは予測変換の候補を確定しない。クリックしたら確定する
        var front = new Front { Predicts = true };
        front.Settings.PredictiveCandidates = true;
        front.Document = "I made ";
        var list = front.Type("decis")[^1].GetProperty("result").GetProperty("view").GetProperty("list");
        Assert.True(list.ValueKind == JsonValueKind.Object && list.GetProperty("items").GetArrayLength() > 1, "予測変換の候補を出す\n" + front.Trace);
        Assert.True(Commits(front.Event("CandidateHighlighted", extra: ",\"index\":1")).GetArrayLength() == 0, "なぞっただけ\n" + front.Trace);
        Assert.Equal(list.GetProperty("items")[1].GetString(), Commits(front.Event("CandidateClicked", extra: ",\"index\":1"))[0].GetProperty("text").GetString(),
            "クリックした候補を確定する\n" + front.Trace);
    }

    [Test]
    public static void ModeIndicator_IsNotShownWhileComposing()
    {
        var front = new Front();
        front.Type("ka");
        Assert.True(!Indicator(front.Event("CaretMoved", extra: Entered)), "変換中は出さない\n" + front.Trace);
    }

    [Test]
    public static void ModeIndicator_IsDecidedByTheSetting()
    {
        var front = new Front();
        Assert.True(Indicator(front.Event("CaretMoved", extra: Entered)), "既定では入力欄に入ったときも表示する\n" + front.Trace);
        Assert.True(Indicator(front.Key(ToggleShortcut.Hankaku)), "切り替えたら表示する\n" + front.Trace);
        front.Settings.ModeIndicator = ModeIndicatorTiming.OnSwitch;
        Assert.True(!Indicator(front.Event("CaretMoved", extra: Entered)) && Indicator(front.Key(ToggleShortcut.Hankaku)), "切り替えたときだけ\n" + front.Trace);
        front.Settings.ModeIndicator = ModeIndicatorTiming.Never;
        Assert.True(!Indicator(front.Key(ToggleShortcut.Hankaku)), "出さない設定\n" + front.Trace);
    }

    [Test]
    public static void Commits_SayWhenToPaste()
    {
        // キーを送って文字を入れるフロント向け: アプリ別設定の「確定した文字の入れ方」・Qt のアプリ・改行を含む文
        var front = new Front();
        front.Type("ka");
        Assert.True(!Commits(front.Key('\n'))[0].TryGetProperty("paste", out _), "ふつうはキーで入れる\n" + front.Trace);
        front.Settings.AppRules.Add(new AppRule { Process = "paste.exe", Enabled = true, Commit = CommitMethod.Paste });
        front.App = "paste.exe";
        front.Type("ka");
        Assert.True(Commits(front.Key('\n'))[0].GetProperty("paste").GetBoolean(), "アプリ別設定で貼り付け\n" + front.Trace);
        front = new Front();
        front.Snippets.Set("sig", "--\nメルト");
        front.Key(0xBA, ';');
        front.Type("sig");
        Assert.True(Commits(front.Key(' '))[0].GetProperty("paste").GetBoolean(), "改行を含む定型文は貼り付け\n" + front.Trace);
    }

    [Test]
    public static void Capture_ListsControlKeysUsedWhileComposing()
    {
        var front = new Front();
        Assert.Equal(0, Capture(front.Event("StateRequested")).GetProperty("composing").GetArrayLength(), "入力していなければ空\n" + front.Trace);
        var typing = front.Key('k');
        var keys = Capture(typing).GetProperty("composing").EnumerateArray().Select(k => (k.GetProperty("key").GetInt32(), k.GetProperty("mods").GetInt32())).ToList();
        Assert.True(keys.Contains(('U', 2)) && keys.Contains(('H', 2)) && !keys.Contains(('J', 2)), "Ctrl+U・Ctrl+H は変換ボックスで使う\n" + front.Trace);
        front.Settings.ControlKeys = ControlKeyLayout.Mac;
        keys = Capture(front.Key('a')).GetProperty("composing").EnumerateArray().Select(k => (k.GetProperty("key").GetInt32(), k.GetProperty("mods").GetInt32())).ToList();
        Assert.True(keys.Contains(('J', 2)) && keys.Contains(('E', 2)), "macOS 風の割り当て\n" + front.Trace);
    }

    [Test]
    public static void FrontFacts_DecideWhetherCoreTakesTheField()
    {
        foreach (var trait in new[] { "Unreachable", "NonText" })
        {
            var front = new Front();
            front.Traits.Add(trait);
            Assert.True(!Active(front.Key('a')), $"{trait} では受け持たない\n" + front.Trace);
        }
        var text = new Front { App = "game.exe" };
        text.Traits.Add("NonText");
        text.Settings.AppRules.Add(new AppRule { Process = "game.exe", Enabled = true, TextInput = true });
        Assert.True(Active(text.Key('a')), "アプリ別設定で「入力欄とみなす」アプリは受け持つ\n" + text.Trace);
        var language = new Front();
        language.Traits.Add("OtherLanguage");
        Assert.True(Active(language.Key('a')), "日本語以外のキーボードでも、設定が OFF なら受け持つ\n" + language.Trace);
        language.Settings.JapaneseKeyboardOnly = true;
        Assert.True(!Active(language.Key('a')), "設定「日本語のキーボードだけ」\n" + language.Trace);
    }

    [Test]
    public static void CodeApps_ComeFromTheAppName()
    {
        // 既定で VS Code・ターミナルは「コード」(Windows・macOS・Linux の名前)
        foreach (var app in new[] { "Code.exe", "com.microsoft.VSCode", "code", "com.apple.Terminal", "gnome-terminal-server" })
        {
            var front = new Front { App = app, Document = "let x = " };
            Assert.True(!Consumed(front.Key('k')), $"{app}: コードの行は英数のまま\n" + front.Trace);
        }
        var text = new Front { App = "notes", Document = "let x = " };
        Assert.True(Consumed(text.Key('k')), "一般のアプリ\n" + text.Trace);
    }

    [Test]
    public static void Snippets_ExpandFromTheTextBeforeTheCaret()
    {
        var front = new Front();
        front.Snippets.Set("ty", "ありがとう");
        foreach (var c in ";ty") Assert.True(!Consumed(front.Key(c == ';' ? 0xBA : c - 'a' + 'A', c)), $"{c} はそのままアプリへ\n" + front.Trace);
        front.Key(' ');
        Assert.Equal("ありがとう", front.Document, front.Trace);
        front.Key('\u001b');
        Assert.Equal(";ty", front.Document, "すぐに Esc で戻す\n" + front.Trace);
        front.Document = "a";
        front.Key(0xBA, ';');
        Assert.True(Consumed(front.Key('t')), "単語の途中の ; は定型文ではない\n" + front.Trace);
    }

    [Test]
    public static void OsOpenState_IsFollowed()
    {
        // アプリ・Windows が IME を閉じた (パスワード欄への移動など): 次の出来事の open で本体のモードを合わせる
        var front = new Front();
        front.Type("ka\n");
        front.Open = false;
        var closed = front.Event("CaretMoved");
        Assert.True(!Japanese(closed) && !Indicator(closed), "合わせるだけで表示はしない\n" + front.Trace);
        Assert.True(!Consumed(front.Key('a')), front.Trace);
        front.Open = true;
        Assert.True(Consumed(front.Key('k')), front.Trace);
    }

    [Test]
    public static void Capture_FollowsTheModeAndSettings()
    {
        static bool HasKey(JsonElement capture, int key, int mods) =>
            capture.GetProperty("keys").EnumerateArray().Any(k => k.GetProperty("key").GetInt32() == key && k.GetProperty("mods").GetInt32() == mods);
        var front = new Front();
        var japanese = Capture(front.Event("StateRequested"));
        Assert.True(japanese.GetProperty("text").GetBoolean() && japanese.GetProperty("except").GetString() == "", "日本語では文字を受け取る\n" + front.Trace);
        Assert.True(HasKey(japanese, VirtualKeys.Space, 1) && !HasKey(japanese, VirtualKeys.Space, 0), "Shift+Space は受け取り、半角の Space は通す\n" + front.Trace);
        front.Settings.SpaceWidth = SpaceWidth.FullWidth;
        Assert.True(HasKey(Capture(front.Event("StateRequested")), VirtualKeys.Space, 0), "全角スペースなら Space も受け取る\n" + front.Trace);
        front.Open = false;
        var english = Capture(front.Event("StateRequested"));
        Assert.True(english.GetProperty("text").GetBoolean(), "英数でもローマ字を見分けるので受け取る\n" + front.Trace);
        front.Settings.DirectModeAutoDetect = false;
        english = Capture(front.Event("StateRequested"));
        Assert.True(!english.GetProperty("text").GetBoolean() && !HasKey(english, VirtualKeys.Space, 1), "見分けないなら通す\n" + front.Trace);
        Assert.True(HasKey(english, ToggleShortcut.Hankaku, 0), "英数でも切り替えキーは受け取る\n" + front.Trace);
    }

    [Test]
    public static void CharacterKeys_NeedNoVirtualKey()
    {
        // 文字を入力するキーは ch だけで送れる (仮想キーコードは本体が決める)
        var front = new Front();
        foreach (var c in "kyouha,")
            front.Send($"{{\"v\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"field\":{{\"before\":\"\",\"composing\":{(c == 'k' ? "false" : "true")}}},\"key\":{{\"ch\":{(int)c}}}}}");
        var view = front.Send($"{{\"v\":{ImeHost.Version},\"type\":\"StateRequested\",\"instance\":\"t1\"}}");
        var enter = front.Send($"{{\"v\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"field\":{{\"before\":\"\",\"composing\":true}},\"key\":{{\"vk\":13}}}}");
        Assert.Equal("きょうは、", Commits(enter)[0].GetProperty("text").GetString(), front.Trace + view);
    }

    [Test]
    public static void MultiScalarText_CommitsAndPasses()
    {
        // 1 回のキーで複数のスカラーが入る (Mac): 未確定の内容を確定してアプリに通させる。先頭が結合文字なら英字の原文につなげる
        foreach (var (typed, text, expected) in new[] { ("e", "́̀", "e"), ("ka", "👩‍💻", "か") })
        {
            var front = new Front();
            front.Type(typed);
            var output = front.Event("KeyPressed", extra: $",\"key\":{{\"text\":{JsonSerializer.Serialize(text)}}}");
            Assert.True(!Consumed(output) && front.Composition["t1"] is null, "アプリに通す\n" + front.Trace);
            Assert.Equal(expected, front.Document, "確定してから通す\n" + front.Trace);
        }
    }

    [Test]
    public static void View_HasOnlyWhatTheFrontShows()
    {
        var front = new Front();
        front.Type("kyou");
        var typing = front.Key(' ');
        var list = typing.GetProperty("result").GetProperty("view").GetProperty("list");
        Assert.True(list.ValueKind == JsonValueKind.Object && list.GetProperty("items").GetArrayLength() > 1,
            "変換中で候補が 2 つ以上なら一覧を出す\n" + front.Trace);
        Assert.True(!typing.GetProperty("result").TryGetProperty("showGuide", out _) && !typing.GetProperty("result").GetProperty("view").TryGetProperty("contextHint", out _),
            "案内は操作ガイドの設定を反映済みで渡す\n" + front.Trace);
        front.Settings.ShowGuide = false;
        Assert.Equal("", front.Key('\b').GetProperty("result").GetProperty("view").GetProperty("hint").GetString(), "操作ガイドを出さない設定では固定の案内を出さない\n" + front.Trace);
    }

    [Test]
    public static void Rewrites_NeedTheTextBeforeTheCaret()
    {
        // 確定し直しは、本体がキャレットの前の文字と照合できたときだけ。読めない・合わないなら、今の語だけを確定する
        var front = new Front();
        front.Type("i ");
        front.Document = "pasted";
        front.Type("want ");
        Assert.True(front.Document.EndsWith("want ", StringComparison.Ordinal) && front.Document.StartsWith("pasted", StringComparison.Ordinal), front.Trace);
        Assert.Equal(0, front.Skipped.Count, "合わない確定し直しを出さない\n" + front.Trace);
    }

    [Test]
    public static void Toggle_InTheCodeLine_KeepsTheImeOpen()
    {
        var front = new Front { Terminal = true, Document = "let x = 1; " };
        var line = front.Key('\u001b');
        Assert.True(!Japanese(line) && front.Open, "コードの行は IME を開いたまま英数\n" + front.Trace);
        var japanese = front.Key(ToggleShortcut.Hankaku);
        Assert.True(Japanese(japanese) && front.Open && Indicator(japanese), "コードの行を日本語にする\n" + front.Trace);
        var back = front.Event("ModeToggleRequested");
        Assert.True(!Japanese(back) && front.Open && Indicator(back), "もう一度で英数に戻す (IME は開いたまま)\n" + front.Trace);
        var closed = front.Event("ModeToggleRequested");
        Assert.True(Japanese(closed) && front.Open, "英数に見えているので、次は日本語にする\n" + front.Trace);
    }

    [Test]
    public static void CodeLine_ToggleShortcutWorksLikeHankaku()
    {
        // #1184: 設定「入力モード切り替えキー」(Shift+Space) も、半角/全角 と同じくコードの行を日本語にして、もう一度で英数に戻す
        var front = new Front { Terminal = true, Document = "ls " };
        front.Settings.ToggleShortcuts = [new() { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.Shift }];
        var japanese = front.Key(VirtualKeys.Space, ' ', shift: true);
        Assert.True(Consumed(japanese) && Japanese(japanese) && front.Open, "コードの行を日本語にする\n" + front.Trace);
        front.Type("kyou");
        Assert.Equal("きょう", front.Composition["t1"], front.Trace);
        front.Key('\n');
        var back = front.Key(VirtualKeys.Space, ' ', shift: true);
        Assert.True(Consumed(back) && !Japanese(back) && front.Open, "もう一度で英数 (IME は開いたまま)\n" + front.Trace);
        Assert.True(!Consumed(front.Key('k')), "コードの行は英数のまま\n" + front.Trace);
    }

    [Test]
    public static void CodeLine_EscPassedToTheAppForgetsJapanese()
    {
        // #1183: 日本語にしたコードの行で、アプリに渡した Esc (vim のノーマルモードに戻る) の後は英数。変換中の文字を取り消す Esc では戻さない
        var front = new Front { Terminal = true, Document = "ls " };
        front.Key(ToggleShortcut.Hankaku);
        front.Type("ka");
        front.Key('\u001b');
        Assert.True(front.Composition["t1"] is null && Japanese(front.Event("StateRequested")), "変換中の文字を取り消した Esc の後も日本語\n" + front.Trace);
        var esc = front.Key('\u001b');
        Assert.True(!Consumed(esc) && !Japanese(esc), "アプリに渡した Esc で英数に戻す\n" + front.Trace);
        Assert.True(!Consumed(front.Key('j')), "vim の j はアプリへ\n" + front.Trace);
        // 引用符の中かも、キャレットの前の文字だけで決める (Esc の前に開いた引用符が、閉じたかどうかを覚えていない)
        front = new Front { Terminal = true, Document = "echo \"" };
        Assert.True(Japanese(front.Event("StateRequested")), "開いた引用符の中は日本語\n" + front.Trace);
        front.Key('\u001b');
        front.Document = "";
        Assert.True(!Consumed(front.Key('j')), "TUI が画面を書き換えたあとは、読めたキャレットの前の文字で決める\n" + front.Trace);
    }

    [Test]
    public static void CodeLine_EisuClosesTheImeAndLeavingForgetsJapanese()
    {
        var front = new Front { Terminal = true, Document = "ls " };
        Assert.True(Japanese(front.Key(ToggleShortcut.Hankaku)) && front.Open, "コードの行を日本語にする\n" + front.Trace);
        front.Key(VirtualKeys.OemAttn);
        Assert.True(!front.Open, "英数キーは、コードの行でも直接入力にする\n" + front.Trace);
        front.Key(VirtualKeys.Kana);
        Assert.True(front.Open && Japanese(front.Event("StateRequested")), "かなで日本語 (コードの行も)\n" + front.Trace);
        front.Event("CaretMoved", extra: ",\"caret\":{\"moved\":true,\"passedKey\":0}");
        Assert.True(Japanese(front.Event("StateRequested")), "ターミナルは、キーなしでキャレットが動いても (出力) 日本語のまま\n" + front.Trace);
        front.Event("CaretMoved", extra: ",\"caret\":{\"moved\":true,\"passedKey\":0,\"focusLeft\":true}");
        Assert.True(!Japanese(front.Event("StateRequested")) && front.Open, "フォーカスが外れたら、コードの行を英数に戻す\n" + front.Trace);
    }

    [Test]
    public static void CodeLine_IsDecidedOnlyByTheTextBeforeTheCaret()
    {
        // 通したキーから行を追いかけない: アプリが文字を入れたあとの位置は、フロントが caret で送る before で決める
        var front = new Front { Terminal = true, Document = "echo " };
        Assert.True(!Japanese(front.Key('\u001b')), "コードの行は英数\n" + front.Trace);
        Assert.True(!Consumed(front.Key(0xDE, '"')), "引用符はアプリへ\n" + front.Trace);
        Assert.True(Japanese(front.Event("CaretMoved", extra: ",\"caret\":{\"passedKey\":222}")), "アプリに入った引用符の後は日本語\n" + front.Trace);
        front.Type("kyouha");
        var close = front.Key(0xDE, '"');
        Assert.True(!Consumed(close) && front.Document == "echo \"きょうは\"", "閉じる引用符は確定してからアプリへ\n" + front.Trace);
        Assert.True(!Japanese(front.Event("CaretMoved", extra: ",\"caret\":{\"passedKey\":222}")), "閉じたあとは英数\n" + front.Trace);
        var unknown = front.Send($"{{\"v\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"open\":true,\"field\":{{\"traits\":[\"Terminal\"]}},\"key\":{{\"vk\":75,\"ch\":107}}}}");
        Assert.True(Active(unknown) && !Consumed(unknown), "キャレットの前を読めないコードの入力欄は、コードの行として英数\n" + front.Trace);
    }
}
