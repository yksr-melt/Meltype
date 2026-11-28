// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Collections.Concurrent;
using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Ime;
using Meltype.Input;
using Meltype.Learning;

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
        private ImeHost? _host;
        private readonly List<string> _trace = [];

        // Converter・Services・ClassifyDirect を初期化子で決めてから作る
        private ImeHost Host => _host ??= Create();

        private ImeHost Create()
        {
            MeltypeRuntime.Connect(CompositionTests.Detector, Services);
            return new ImeHost(this, new SessionParts(CompositionTests.Detector, Converter, Services) { ClassifyDirect = ClassifyDirect, LateResults = () => LateResults }, () => Settings);
        }

        /// <summary>変換エンジンの答えを待たず、後から受け取るか (<see cref="SessionParts.LateResults"/>)。入力欄を作った後に変えてもよい。</summary>
        public bool LateResults { get; set; }

        /// <summary>英数状態で打ち始めた英字がローマ字 (日本語) かの判定。null なら判定しない。</summary>
        public Func<string, bool, Settings, Verdict>? ClassifyDirect { get; init; }

        // 入力モードの表示の既定は OS と入力方式で違うので、どの OS でも同じに決めておく
        public Settings Settings { get; set; } = new() { LiveConversion = false, ModeIndicator = ModeIndicatorTiming.OnSwitchAndFocus };

        /// <summary>このフロントが Windows のどちらの入力方式か (null なら入力方式が 1 つしかない OS)。</summary>
        public InputMode? InputMethod { get; set; }

        public IKanjiConverter Converter { get; init; } = new CompositionTests.FakeConverter();
        public CompositionServices Services { get; init; } = new();

        public SnippetStore Snippets { get; } = new(null);

        public string? ClipboardText => null;

        public string? OtherTipVersion => null;

        public int Sessions => Host.SessionCount;

        public void CloseAll() => Host.CloseAll();

        public string Document { get; set; } = "";

        /// <summary>OS の IME の開閉 (TSF の OPENCLOSE)。</summary>
        public bool Open { get; set; } = true;

        /// <summary>OS が IME の開閉を持つフロント (TSF など) か。持たないフロント (Android) は imeOpen を送らない。</summary>
        public bool SendsImeOpen { get; init; } = true;

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

        /// <summary>キャレットの前後を送るか。null は送らない (読めない)、"" は空で送る (Windows Terminal の TSF)。</summary>
        public string? Sends { get; init; } = "Document";

        /// <summary>入力欄で選んでいる文字。</summary>
        public string? Selection { get; set; }

        public Dictionary<string, string?> Composition { get; } = [];

        /// <summary>本体が出した、入力欄と合わない確定し直し (消す文字 expect が入力欄の末尾に無いもの)。</summary>
        public List<string> Skipped { get; } = [];

        public JsonElement State { get; private set; }

        public string Trace => "やり取り:\n" + string.Join("\n", _trace);

        public JsonElement Send(string json)
        {
            var reply = Host.Handle(json);
            _trace.Add($"  {json}\n    → {reply}");
            using var document = JsonDocument.Parse(reply);
            return document.RootElement.Clone();
        }

        /// <summary>出来事を送る。キャレットの前は、確定した文字とアプリに通した文字 (Document。変換中の文字は入れない)。</summary>
        public JsonElement Event(string type, string instance = "t1", string extra = "", bool moved = false, bool omitField = false)
        {
            // キャレットが動いたことは、見つけたときに caret で送る
            if (moved) Event("CaretMoved", instance, ",\"caret\":{\"moved\":true,\"passedKey\":0}");
            var composing = Composition.GetValueOrDefault(instance) is not null;
            var surrounding = Sends switch { "Document" => $"\"before\":{Json(Document)},\"after\":\"\",", null => "", _ => $"\"before\":{Json(Sends)},\"after\":\"\"," };
            var field = $"{{{surrounding}\"composing\":{(composing ? "true" : "false")},\"traits\":{JsonSerializer.Serialize(Traits)}" +
                (App is null ? "" : $",\"app\":{Json(App)}") + (Selection is null ? "" : $",\"selection\":{Json(Selection)}") + "}";
            var fieldFact = omitField ? "" : $",\"field\":{field}";
            var output = Send($"{{\"version\":{ImeHost.Version},\"type\":\"{type}\",\"instance\":{Json(instance)}{(SendsImeOpen ? $",\"imeOpen\":{(Open ? "true" : "false")}" : "")}{fieldFact}{extra}}}");
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
            '/' => Key(0xBF, key, instance: instance, moved: moved),
            '"' => Key(0xDE, key, shift: true, instance: instance, moved: moved),
            ']' => Key(0xDD, key, instance: instance, moved: moved),
            ';' => Key(0xBB, key, instance: instance, moved: moved),
            ',' => Key(0xBC, key, instance: instance, moved: moved),
            >= 'a' and <= 'z' => Key(key - 'a' + 'A', key, instance: instance, moved: moved),
            _ => throw new ArgumentException($"送れないキー: {key}"),
        };

        public JsonElement Key(int vk, char ch = '\0', bool shift = false, bool ctrl = false, string instance = "t1", bool moved = false, bool omitField = false, bool capsLock = false)
        {
            var output = Event("KeyPressed", instance, $",\"key\":{{\"vk\":{vk},\"ch\":{(int)ch},\"shift\":{(shift ? "true" : "false")},\"ctrl\":{(ctrl ? "true" : "false")},\"capsLock\":{(capsLock ? "true" : "false")}}}", moved, omitField);
            // 使わなかったキーは、アプリがそのまま入れる
            if (!Consumed(output) && (ch != '\0' || vk == VirtualKeys.Return)) Document += vk == VirtualKeys.Return ? '\n' : ch;
            return output;
        }

        public List<JsonElement> Type(string keys, string instance = "t1") => keys.Select(key => Key(key, instance)).ToList();

        /// <summary>
        /// 確定を入れ、表示と開閉を置き換える。確定し直し (remove がある) は照合せずに、remove の分を消してから text を入れる
        /// (照合は本体の仕事。消す文字が入力欄の末尾に無ければ、本体が約束を破ったので Skipped に残して入れない)。
        /// </summary>
        public void Apply(JsonElement output, string instance)
        {
            Assert.Equal(ImeHost.Version, output.GetProperty("version").GetInt32(), Trace);
            State = output.GetProperty("state");
            var result = output.GetProperty("result");
            foreach (var commit in result.GetProperty("commits").EnumerateArray())
            {
                var text = commit.GetProperty("text").GetString()!;
                if (commit.GetProperty("removeBefore") is { ValueKind: JsonValueKind.String } remove)
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
            if (Active(output)) Open = State.GetProperty("imeOpen").GetBoolean();
        }
    }

    private static string Json(string text) => JsonSerializer.Serialize(text);

    /// <summary>入力欄にフォーカスが入った (caret の出来事に付ける)。</summary>
    private const string Entered = ",\"caret\":{\"moved\":true,\"passedKey\":0,\"focusEntered\":true}";

    private static bool Consumed(JsonElement output) => output.GetProperty("result").GetProperty("consumed").GetBoolean();

    private static bool Active(JsonElement output) => output.GetProperty("state").GetProperty("handlesInput").GetBoolean();

    private static bool Japanese(JsonElement output) => output.GetProperty("state").GetProperty("languageMode").GetString() == "Japanese";

    private static bool Indicator(JsonElement output) => output.GetProperty("state").GetProperty("modeIndicatorMs").GetInt32() > 0;

    private static JsonElement Capture(JsonElement output) => output.GetProperty("state").GetProperty("capture");

    private static JsonElement Commits(JsonElement output) => output.GetProperty("result").GetProperty("commits");

    private static JsonElement View(JsonElement output) => output.GetProperty("result").GetProperty("view");

    private static List<string?> Candidates(JsonElement output) =>
        View(output).GetProperty("candidates").GetProperty("items").EnumerateArray().Select(item => item.GetString()).ToList();

    private static void AssertPasswordPass(Front front, JsonElement output)
    {
        Assert.True(!Active(output) && !Consumed(output) && !Indicator(output), front.Trace);
        Assert.Equal(0, Capture(output).GetProperty("shortcuts").GetArrayLength(), front.Trace);
        Assert.Equal(0, Commits(output).GetArrayLength(), front.Trace);
        Assert.Equal(JsonValueKind.Null, View(output).ValueKind, front.Trace);
    }

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
    public static void LetterLayout_TypesRomajiByPosition()
    {
        // ローマ字の配列が Dvorak: QWERTY の位置で打った j f = h u = ふ、v a g = k a i = かい
        var front = new Front();
        front.Settings.RomajiLayout = RomajiLayout.Dvorak;
        front.Event("StateRequested");
        front.Type("jfvag");
        Assert.Equal("ふかい", front.Composition["t1"], front.Trace);
        // Colemak で google と打つキー (QWERTY の t;;tuk) は英語の google
        front = new Front();
        front.Settings.RomajiLayout = RomajiLayout.Colemak;
        front.Event("StateRequested");
        front.Type("t;;tuk");
        Assert.Equal("google", front.Composition["t1"], front.Trace);
        // 大西配列: QWERTY の l ; d = s h a、r = , (、)
        front = new Front();
        front.Settings.RomajiLayout = RomajiLayout.Ohnishi;
        front.Event("StateRequested");
        front.Type("l;dr");
        Assert.Equal("しゃ、", front.Composition["t1"], front.Trace);
    }

    [Test]
    public static void LetterLayout_ShortcutsAndDirectModeKeepQwerty()
    {
        // Ctrl と一緒のキーと英数状態のキーは入れ替えない
        var settings = new Settings { RomajiLayout = RomajiLayout.Dvorak };
        Assert.Equal('c', (char)LetterLayout.Apply(new ImeKey { Ch = 'c', Ctrl = true }, settings, direct: false).Ch);
        Assert.Equal('j', (char)LetterLayout.Apply(new ImeKey { Ch = 'j' }, settings, direct: true).Ch);
        Assert.Equal('H', (char)LetterLayout.Apply(new ImeKey { Ch = 'J', Shift = true }, settings, direct: false).Ch);
        // 記号の位置の Shift (:) と QWERTY・SKY 配列は入れ替えない
        Assert.Equal(null, LetterLayout.Map(RomajiLayout.Dvorak, ':'));
        Assert.Equal(null, LetterLayout.Map(RomajiLayout.Qwerty, 'a'));
        Assert.Equal(null, LetterLayout.Map(RomajiLayout.Sky, 'a'));
    }

    [Test]
    public static void Settings_AreReadOncePerEvent()
    {
        // 出来事の途中で設定が変わっても (受け持つ → 止める)、その出来事の中の判断は始めに読んだ設定でそろえる
        var reads = 0;
        var session = new TestSession(() => new Settings { Enabled = ++reads <= 2 });
        session.HandleKey('A', 'a', false, false, false, false);
        var k = session.HandleKey('K', 'k', false, false, false, false);
        Assert.Equal(2, reads, "設定は出来事ごとに 1 回読む");
        Assert.True(session.State.HandlesInput && k.Consumed, "受け持つと答えたキーはアプリに通さない");
    }

    [Test]
    public static void Numpad_IsPassedOnlyWhenNothingIsTyped()
    {
        // #460: Windows・Mac・fcitx5・IBus は、テンキーの数字・記号を「仮想キーコード (0x60〜0x6F) と文字」で送る。
        // 上の段の数字は「仮想キーコード 0 と文字」(本体が文字から決める)。何も打っていないときのテンキーだけそのまま通す
        var front = new Front();
        front.Event("StateRequested");
        Assert.True(!Consumed(front.Key(0x61, '1')) && !Consumed(front.Key(0x6E, '.')) && !Consumed(front.Key(0x65, '5')), "テンキーは通す\n" + front.Trace);
        Assert.Equal("1.5", front.Document, front.Trace);
        Assert.True(Consumed(front.Key(0, '1')), "上の段の数字 (仮想キーコード 0 と文字) は変換ボックスに入れる\n" + front.Trace);

        front = new Front();
        front.Event("StateRequested");
        front.Type("ai");
        Assert.True(Consumed(front.Key(0x61, '1')), "入力中のテンキーは変換ボックスに入れる\n" + front.Trace);
        Assert.Equal("あい1", front.Composition["t1"], front.Trace);
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
            outputs.SelectMany(o => Commits(o).EnumerateArray()).Any(c => c.GetProperty("removeBefore").ValueKind == JsonValueKind.String);

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
                Assert.Equal(enabled ? 1 : 0, Commits(enter)[0].GetProperty("moveCaretBack").GetInt32(), front.Trace);
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
                     $"{{\"version\":{ImeHost.Version + 1},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"version\":{ImeHost.Version},\"type\":\"jump\",\"instance\":\"t1\"}}",
                     $"{{\"version\":{ImeHost.Version},\"type\":\"key\",\"instance\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"version\":{ImeHost.Version},\"type\":99,\"instance\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"version\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"field\":{{\"traits\":[\"Hologram\"]}},\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"version\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"field\":{{\"traits\":[42]}},\"key\":{{\"vk\":65,\"ch\":97}}}}",
                     $"{{\"version\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"key\":{{\"vk\":65,\"ch\":97}},\"code\":true}}",
                     "not json",
                 })
        {
            var output = front.Send(json);
            Assert.True(!Active(output) && !Consumed(output), "版の違う・知らない出来事・知らない性質のある出来事は受け持たない: " + json);
        }
        Assert.Equal(0, front.Sessions, front.Trace);
        Assert.True(Active(front.Event("StateRequested", omitField: true)), "起動時は設定に従う捕捉条件を返す\n" + front.Trace);
        var noInstance = front.Send($"{{\"version\":{ImeHost.Version},\"type\":\"KeyPressed\",\"field\":{{}},\"key\":{{\"vk\":65,\"ch\":97}}}}");
        Assert.True(Active(noInstance) && Consumed(noInstance), "instance を省いた出来事も、属性があれば空の instance として受け持つ");
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
            Assert.Equal(0, Capture(front.Event("StateRequested")).GetProperty("shortcuts").GetArrayLength(), "受け持たないときはキーを受け取らせない\n" + front.Trace);
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
        var keys = Capture(toggle).GetProperty("shortcuts").EnumerateArray().Select(k => (k.GetProperty("vk").GetInt32(), k.GetProperty("modifiers").GetInt32())).ToList();
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
    public static void LangKeys_SelectJapaneseOrAlphanumeric()
    {
        var front = new Front();
        front.Type("ka");
        Assert.True(Consumed(front.Key(VirtualKeys.ImeOn)) && front.Open && front.Document == "", front.Trace);
        front.Key(VirtualKeys.ImeOff);
        Assert.True(!front.Open && front.Document == "か", front.Trace);
        Assert.True(Consumed(front.Key(VirtualKeys.ImeOff)) && !front.Open, front.Trace);
        Assert.True(Consumed(front.Key(VirtualKeys.ImeOn)) && front.Open, front.Trace);
    }

    [Test]
    public static void ConvertKeys_SetTheMode()
    {
        var front = new Front();
        front.Key(VirtualKeys.NonConvert);
        Assert.True(!front.Open, "無変換で英数\n" + front.Trace);
        front.Key(VirtualKeys.Convert);
        Assert.True(front.Open, "変換で日本語\n" + front.Trace);
        var repeat = front.Send($"{{\"version\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"key\":{{\"vk\":{VirtualKeys.NonConvert},\"repeat\":true}}}}");
        Assert.True(Consumed(repeat) && repeat.GetProperty("state").GetProperty("imeOpen").GetBoolean(), "押しっぱなしでは切り替えない\n" + front.Trace);
        // 入力中の無変換は、英数キーと同じく確定してから英数。入力中の変換は変換する
        front.Type("ka");
        front.Key(VirtualKeys.NonConvert);
        Assert.True(front.Document == "か" && !front.Open, "入力中の無変換は確定してから英数\n" + front.Trace);
        front.Key(VirtualKeys.Convert);
        front.Type("kyou");
        var converting = front.Key(VirtualKeys.Convert).GetProperty("result").GetProperty("view");
        Assert.True(converting.GetProperty("converting").GetBoolean() && front.Document == "か", "入力中の変換は変換する\n" + front.Trace);
        front.Key('\n');
        front.Settings.SeparateKeyRoles = true;
        front.Type("ka");
        var role = front.Key(VirtualKeys.NonConvert);
        Assert.True(front.Open && role.GetProperty("result").GetProperty("view").GetProperty("text").GetString() == "ka",
            "入力中のキーの役割を分けるなら、無変換は英字にする\n" + front.Trace);
        front.Key('\n');
        front.Settings.SeparateKeyRoles = false;
        // 役割を分けなくても、無変換でカタカナを切り替える設定が ON なら、入力中の無変換は確定せず切り替える
        front.Settings.NonConvertCyclesKana = true;
        front.Type("ka");
        var cycle = front.Key(VirtualKeys.NonConvert);
        Assert.True(front.Open && cycle.GetProperty("result").GetProperty("view").GetProperty("text").GetString() == "カ",
            "無変換でカタカナ (確定して英数にしない)\n" + front.Trace);
        front.Key('\n');
        front.Settings.NonConvertCyclesKana = false;
        // 親指シフトの配列では親指キー (変換ボックスで使う)
        front.Settings.InputStyle = InputStyle.Kana;
        front.Settings.KanaLayout = KanaLayout.Nicola;
        front.Key(VirtualKeys.NonConvert);
        Assert.True(front.Open, "NICOLA の無変換は切り替えない\n" + front.Trace);
        // 変換・無変換が 1 つのキーのキーボードでは、そのキーを切り替えキーに登録して日本語 / 英数を行き来する
        var toggle = new Front();
        toggle.Settings.ToggleShortcuts = [new() { Key = VirtualKeys.Convert }];
        toggle.Key(VirtualKeys.Convert);
        Assert.True(!toggle.Open, "切り替えキーの変換で英数\n" + toggle.Trace);
        toggle.Key(VirtualKeys.Convert);
        Assert.True(toggle.Open, "もう一度押して日本語\n" + toggle.Trace);
    }

    [Test]
    public static void NonConvertAfterProtectedInput_SwitchesToAlphanumeric()
    {
        foreach (var cycle in new[] { false, true })
        {
            var front = new Front();
            front.Settings.SeparateKeyRoles = false;
            front.Settings.NonConvertCyclesKana = cycle;
            const string url = "https://example.com/abc";
            front.Type("https");
            front.Key(0xBA, ':');
            front.Type("//example");
            front.Key(VirtualKeys.OemPeriod, '.');
            front.Type("com/abc");
            front.Key(VirtualKeys.NonConvert);
            Assert.Equal(url, front.Document, front.Trace);
            Assert.True(!front.Open, front.Trace);
        }
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
        // 左右の Alt の押下を KeyPressed、解放を KeyReleased で受け取り、単独押しかは本体が決める (Windows のフロント)
        var front = new Front();
        JsonElement Press(int vk, bool ctrl = false, bool repeat = false) => front.Event("KeyPressed",
            extra: $",\"key\":{{\"vk\":{vk},\"alt\":true,\"ctrl\":{(ctrl ? "true" : "false")},\"repeat\":{(repeat ? "true" : "false")}}}");
        JsonElement Release(int vk, bool interrupted = false) =>
            front.Event("KeyReleased", extra: $",\"key\":{{\"vk\":{vk},\"interrupted\":{(interrupted ? "true" : "false")}}}");
        bool Tap(int vk, bool interrupted = false)
        {
            Assert.True(!Consumed(Press(vk)), "押下はアプリに通す\n" + front.Trace);
            return Consumed(Release(vk, interrupted));
        }
        Assert.True(!Capture(front.Event("StateRequested")).GetProperty("altKeys").GetBoolean(), "設定が OFF なら Alt を受け取らない\n" + front.Trace);
        front.Type("ka");
        Assert.True(!Tap(VirtualKeys.LMenu) && front.Composition["t1"] == "か" && front.Open, "設定が OFF なら切り替えず、変換中の文字もそのまま\n" + front.Trace);
        front.Key('\n');
        front.Settings.AltKeysSwitchKeyboard = true;
        Assert.True(Capture(front.Event("StateRequested")).GetProperty("altKeys").GetBoolean(), front.Trace);
        front.Type("ka");
        Assert.True(Tap(VirtualKeys.LMenu) && !front.Open && front.Document == "かか", "左 Alt で、変換中の文字を確定して英数\n" + front.Trace);
        Assert.True(Tap(VirtualKeys.RMenu) && front.Open, "右 Alt で日本語\n" + front.Trace);
        // 押したままの自動の繰り返しは単独押しのまま
        Press(VirtualKeys.LMenu);
        Press(VirtualKeys.LMenu, repeat: true);
        Assert.True(Consumed(Release(VirtualKeys.LMenu)) && !front.Open, "自動の繰り返し\n" + front.Trace);
        Tap(VirtualKeys.RMenu);
        // 単独押しにしないとき: 押している間のほかの入力 (フロントが見たもの・本体に届いた出来事)、ほかの修飾キーと一緒、左右両方
        Assert.True(!Tap(VirtualKeys.LMenu, interrupted: true) && front.Open, "Alt + クリック・Alt + Shift\n" + front.Trace);
        Press(VirtualKeys.LMenu);
        front.Event("CaretMoved", extra: ",\"caret\":{\"moved\":true,\"passedKey\":88}");
        Assert.True(!Consumed(Release(VirtualKeys.LMenu)) && front.Open, "Alt + X (アプリに通したキー)\n" + front.Trace);
        Press(VirtualKeys.LMenu, ctrl: true);
        Assert.True(!Consumed(Release(VirtualKeys.LMenu)) && front.Open, "Ctrl + Alt\n" + front.Trace);
        Press(VirtualKeys.LMenu);
        Press(VirtualKeys.RMenu);
        Assert.True(!Consumed(Release(VirtualKeys.RMenu)) && !Consumed(Release(VirtualKeys.LMenu)) && front.Open, "左右の Alt を両方\n" + front.Trace);
        Assert.True(!Consumed(Release(VirtualKeys.LMenu)), "押したことを受け取っていない解放\n" + front.Trace);
        Assert.True(Tap(VirtualKeys.LMenu) && !front.Open, "そのあとは、また単独押し\n" + front.Trace);
    }

    [Test]
    public static void InputFieldTraits_DecideWhatCoreDoes()
    {
        var front = new Front();
        front.Traits.Add("Password");
        Assert.True(!Active(front.Key('a')) && front.Document == "a", "パスワード欄では受け持たない\n" + front.Trace);
        front = new Front { InputMethod = InputMode.Keyboard };
        front.Traits.Add("Game");
        Assert.True(!Active(front.Key('a')), "Meltype キーボードはゲームでは受け持たない (チャットは Windows の IME で打つ)\n" + front.Trace);
    }

    [Test]
    public static void KeyboardGame_IsHandledOnlyWhileTheChatIsOpen()
    {
        // Meltype キーボードはゲームでは止め、IME の入力コンテキストが結び付いている間 (チャット欄が開いている間) だけ、日本語で受け持つ
        var front = new Front { InputMethod = InputMode.Keyboard, SendsImeOpen = false };
        front.Settings.Mode = InputMode.Keyboard;
        front.Traits.AddRange(["Game", "NonText"]);
        Assert.True(!Active(front.Event("StateRequested")), "チャット欄が閉じている間は止める\n" + front.Trace);
        front.Traits.Add("ImeContext");
        var opened = front.Event("StateRequested");
        Assert.True(Active(opened) && Japanese(opened), "チャット欄が開いたら日本語で受け持つ\n" + front.Trace);
        front.Traits.Remove("ImeContext");
        var closed = front.Event("StateRequested");
        Assert.True(!Active(closed) && !Japanese(closed), "チャット欄が閉じたら止めて英数に戻す\n" + front.Trace);
    }

    [Test]
    public static void Game_StartsAlphanumericAndCanSwitchToJapanese()
    {
        // OS の IME そのもののフロントは、ゲームでも止めない。止めるとチャットで日本語を打てない (#1805)
        var front = new Front { InputMethod = InputMode.Tsf };
        front.Settings.DirectModeAutoDetect = true;
        front.Traits.Add("Game");
        var entered = front.Event("CaretMoved", extra: Entered);
        Assert.True(Active(entered) && !front.Open, "英数で始める\n" + front.Trace);
        Assert.True(!Capture(entered).GetProperty("printableAscii").GetBoolean(), "英字を横取りしない (移動のキーをゲームに直接届ける)\n" + front.Trace);
        front.Type("wasd");
        Assert.Equal("wasd", front.Document, front.Trace);
        Assert.True(Consumed(front.Key(ToggleShortcut.Hankaku)) && front.Open, "半角/全角 で日本語にできる\n" + front.Trace);
        front.Type("ka");
        front.Key('\n');
        Assert.True(front.Document == "wasdか" && !front.Open, "Enter で確定したら英数に戻す (チャットを閉じたことは分からない)\n" + front.Trace);
        front.Type("w");
        Assert.Equal("wasdかw", front.Document, "閉じたあとの移動のキーはゲームに届く\n" + front.Trace);
        front.Key(ToggleShortcut.Hankaku);
        front.Type("ka");
        front.Key('\u001b');
        Assert.True(!front.Open, "Esc で取り消しても英数に戻す\n" + front.Trace);
        front.Key(ToggleShortcut.Hankaku);
        front.Type("ka ");
        front.Type("ki");
        Assert.True(front.Open, "変換して打ち続けて確定したときは日本語のまま\n" + front.Trace);
        // ゲームでない入力欄は、OS の開閉のまま
        var general = new Front { InputMethod = InputMode.Tsf };
        Assert.True(general.Event("CaretMoved", extra: Entered).GetProperty("state").GetProperty("imeOpen").GetBoolean(), general.Trace);
    }

    [Test]
    public static void EmailAndNumericFields_AreNotHandled()
    {
        foreach (var trait in new[] { "Email", "Numeric" })
        {
            var front = new Front { SendsImeOpen = false };
            front.Traits.Add(trait);
            var entered = front.Event("CaretMoved", extra: Entered);
            Assert.True(!Active(entered) && entered.GetProperty("state").GetProperty("languageMode").GetString() == "Alphanumeric", $"{trait}: 受け持たない (英数)\n" + front.Trace);
            Assert.True(!Active(front.Key('a')) && front.Document == "a", $"{trait}: キーはそのまま入る\n" + front.Trace);
            Assert.True(front.Event("JapaneseModeSelected").GetProperty("state").GetProperty("languageMode").GetString() == "Alphanumeric", $"{trait}: 日本語にしない\n" + front.Trace);
            front.Traits.Clear();
            Assert.True(front.Event("CaretMoved", extra: Entered).GetProperty("state").GetProperty("languageMode").GetString() == "Japanese", $"{trait}: ふつうの欄では日本語\n" + front.Trace);
        }
    }

    [Test]
    public static void Password_PassesKeysWithoutCreatingASession()
    {
        foreach (var traits in new string[][] { ["Password"], ["Password", "Private"], ["Terminal", "Password"] })
        {
            var front = new Front { Sends = null, Selection = "nihongo" };
            front.Traits.AddRange(traits);
            AssertPasswordPass(front, front.Event("CaretMoved", extra: Entered));
            Assert.Equal(0, front.Sessions, front.Trace);
            foreach (var (vk, ch) in new[] { ((int)'S', (int)'s'), ((int)'A', (int)'a'), ((int)'1', (int)'1'), ((int)'2', (int)'2'),
                         (VirtualKeys.Space, 0), (VirtualKeys.Return, 0), (VirtualKeys.Convert, 0), (ToggleShortcut.Hankaku, 0) })
            {
                AssertPasswordPass(front, front.Event("KeyPressed", extra: $",\"key\":{{\"vk\":{vk},\"ch\":{ch}}}"));
            }
            AssertPasswordPass(front, front.Event("CandidateClicked", extra: ",\"index\":0"));
            AssertPasswordPass(front, front.Event("AlphanumericModeSelected"));
            Assert.Equal(0, front.Sessions, front.Trace);
            front.Traits.Clear();
            Assert.True(Active(front.Event("CaretMoved", extra: Entered)), front.Trace);
            front.Type("sa");
            Assert.Equal("さ", front.Composition["t1"], "周辺文字列が届かない通常欄では変換する\n" + front.Trace);
        }
    }

    [Test]
    public static void Password_OverridesAppRuleThatTreatsFieldAsText()
    {
        const string app = "auth-client";
        var front = new Front { App = app };
        front.Settings.AppRules.Add(new AppRule { Process = app, TextInput = true });
        front.Traits.Add("NonText");
        Assert.True(front.Type("ka").All(Consumed), "アプリ別設定でテキスト欄として受け持つ\n" + front.Trace);
        Assert.Equal("か", front.Composition["t1"], front.Trace);

        front.Traits.Add("Password");
        foreach (var key in "aiueo\n")
        {
            AssertPasswordPass(front, front.Key(key));
        }
        Assert.Equal("aiueo\n", front.Document, "パスワード欄では未確定文字を取り消して打鍵を通す\n" + front.Trace);
    }

    [Test]
    public static void Password_TerminalPromptPassesKeys()
    {
        // 履歴の /* で、後ろの行が複数行コメントの中と判定される (#1201)
        const string history = "$ ls /etc/*\nfoo bar\n$ sudo ls\n";
        foreach (var prompt in new[]
                 {
                     "[sudo] password for user: ", "[sudo] user のパスワード:", "[sudo] Passwort für user: ", "[sudo] user 的密码：",
                     "[sudo: authenticate] Password: ", "[sudo: authenticate] パスワード: ", "Password: ", "user@host's password: ",
                     "Enter passphrase for key '/home/user/.ssh/id_ed25519': ", "Password for 'https://user@github.com': ",
                 })
        {
            // Meltype キーボードはターミナルと知らせず、アプリ名だけ送る
            foreach (var front in new[] { new Front { Terminal = true, Sends = history + prompt }, new Front { App = "WindowsTerminal.exe", Sends = history + prompt } })
            {
                foreach (var key in "nihongo\n")
                {
                    AssertPasswordPass(front, front.Key(key));
                }
            }
        }

        var comment = new Front { Terminal = true, Sends = history + "[sudo] password for user: \n$ echo # " };
        comment.Type("sa");
        Assert.Equal("さ", comment.Composition["t1"], "プロンプトの次の行では、今までどおり判定する\n" + comment.Trace);
        var text = new Front { Sends = "Password: " };
        text.Type("sa");
        Assert.Equal("さ", text.Composition["t1"], "ターミナルでない入力欄では変換する\n" + text.Trace);
    }

    [Test]
    public static void Password_CancelsExistingInputOnTheFirstEvent()
    {
        foreach (var (type, extra) in new[]
                 {
                     ("KeyPressed", ",\"key\":{\"vk\":13}"), ("CaretMoved", Entered), ("CommitRequested", ""),
                     ("CommittedByApp", Entered), ("CandidateClicked", ",\"index\":0"), ("ModeToggleRequested", ""), ("StateRequested", ""),
                 })
        {
            var languages = new LanguageMemory(null);
            var phrases = new PhraseHistory(null);
            phrases.Remember("にほんごでにゅうりょく", "日本語で入力");
            var front = new Front { Services = new CompositionServices { Languages = languages, Predictor = new Predictor(phrases, null, null) } };
            front.Settings.PredictiveCandidates = true;
            Assert.True(Candidates(front.Type("nihongo")[^1]).Contains("日本語で入力"), front.Trace);
            front.Traits.Add("Password");
            AssertPasswordPass(front, front.Event(type, extra: extra));
            AssertPasswordPass(front, front.Event("CaretMoved", extra: Entered));
            AssertPasswordPass(front, front.Event("CommitRequested", omitField: true));
            AssertPasswordPass(front, front.Event("CandidateHighlighted", extra: ",\"index\":0", omitField: true));
            AssertPasswordPass(front, front.Event("CandidateClicked", extra: ",\"index\":0", omitField: true));
            AssertPasswordPass(front, front.Event("CommittedByApp", extra: ",\"caret\":{\"moved\":true,\"passedKey\":0}", omitField: true));
            Assert.Equal("", front.Document, front.Trace);
            Assert.Equal(0, languages.Count, front.Trace);
            front.Traits.Clear();
            Assert.True(Japanese(front.Event("StateRequested")), front.Trace);
            Assert.Equal(JsonValueKind.Null, View(front.Event("ConversionFinished")).ValueKind, front.Trace);
            Assert.Equal(0, Commits(front.Event("CommitRequested")).GetArrayLength(), front.Trace);
            front.Type("ka\n");
            Assert.Equal("か", front.Document, front.Trace);
        }
    }

    [Test]
    public static void Password_DoesNotLearnWhenTheInstanceCloses()
    {
        foreach (var password in new[] { false, true })
            foreach (var closing in new[] { "InstanceDestroyed", "CloseAll" })
            {
                var languages = new LanguageMemory(null);
                var front = new Front { Services = new CompositionServices { Languages = languages } };
                front.Type("nihongo");
                front.Key(VirtualKeys.F10);
                Assert.Equal("nihongo", front.Composition["t1"], front.Trace);
                if (password) front.Traits.Add("Password");
                if (closing == "InstanceDestroyed") AssertPasswordPass(front, front.Event(closing));
                else
                {
                    if (password) AssertPasswordPass(front, front.Event("StateRequested"));
                    front.CloseAll();
                }
                Assert.Equal(password ? 0 : 1, languages.Count, front.Trace);
                Assert.Equal(0, front.Sessions, front.Trace);
            }
    }

    [Test]
    public static void Password_EndsDirectInputDetectionWithoutDeletingPassedKeys()
    {
        var front = new Front
        {
            Open = false,
            ClassifyDirect = (letters, _, _) => letters == "ka" ? Verdict.Japanese : Verdict.Undecided,
        };
        front.Settings.DirectModeAutoDetect = true;
        front.Key('k');
        Assert.Equal("k", front.Document, front.Trace);
        front.Traits.Add("Password");
        AssertPasswordPass(front, front.Event("StateRequested"));
        front.Traits.Clear();
        var resumed = front.Key('a');
        Assert.True(!Consumed(resumed) && !Japanese(resumed), front.Trace);
        Assert.Equal(0, Commits(resumed).GetArrayLength(), front.Trace);
        Assert.Equal("ka", front.Document, front.Trace);
    }

    [Test]
    public static void Password_DropsTheViewBeforeDelayedConversionFinishes()
    {
        foreach (var restoreBeforeResult in new[] { false, true })
        {
            using var release = new ManualResetEventSlim();
            using var ready = new ManualResetEventSlim();
            using var converter = new BackgroundCallbackConverter((reading, _) =>
            {
                if (reading != "にほんご" || !release.Wait(5000)) return null;
                return [new ConversionClause(reading, "日本語")];
            }, _ => []);
            converter.ResultReady += ready.Set;
            var phrases = new PhraseHistory(null);
            var front = new Front { Converter = converter, LateResults = true, Services = new CompositionServices { Predictor = new Predictor(phrases, null, null) } };
            front.Settings.LiveConversion = true;
            front.Settings.PredictiveCandidates = true;
            Assert.Equal("にほんご", View(front.Type("nihongo")[^1]).GetProperty("text").GetString(), front.Trace);
            front.Traits.Add("Password");
            AssertPasswordPass(front, front.Event("CaretMoved", extra: Entered));
            if (restoreBeforeResult) front.Traits.Clear();
            front.Event("StateRequested");
            release.Set();
            Assert.True(ready.Wait(5000), "遅れていた変換結果が届く");
            var result = front.Event("ConversionFinished", omitField: true);
            Assert.Equal(restoreBeforeResult, Active(result), front.Trace);
            Assert.Equal(JsonValueKind.Null, View(result).ValueKind, front.Trace);
            Assert.Equal(0, Commits(result).GetArrayLength(), front.Trace);
            Assert.Equal(0, phrases.Count, front.Trace);
        }
    }

    [Test]
    public static void LateResults_TakesEffectAfterTheHostIsCreated()
    {
        // fcitx5 は入力欄を作ってから、答えを後から受け取ると知らせる (meltype_set_conversion_ready)。その後のキーは答えを待たない
        using var release = new ManualResetEventSlim();
        using var converter = new BackgroundCallbackConverter((reading, _) =>
        {
            if (reading != "にほんご" || !release.Wait(5000)) return null;
            return [new ConversionClause(reading, "日本語")];
        }, _ => []);
        var front = new Front { Converter = converter };
        front.Settings.LiveConversion = true;
        front.Event("StateRequested");
        front.LateResults = true;
        var started = Environment.TickCount64;
        Assert.Equal("にほんご", View(front.Type("nihongo")[^1]).GetProperty("text").GetString(), front.Trace);
        Assert.True(Environment.TickCount64 - started < 2000, "変換エンジンの答えを待たない");
        release.Set();
    }

    private sealed class LearningConverter : TestConverter
    {
        private readonly CompositionTests.FakeConverter _converter = new();
        public override string? Convert(string hiragana, string? before) => _converter.Convert(hiragana, before);
        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) => _converter.ConvertClauses(hiragana, before);
    }

    [Test]
    public static void PrivateAndLearningOff_ConvertButDoNotLearn()
    {
        foreach (var mode in new[] { "通常", "設定OFF", "Private", "Privateから通常欄へ移動", "Password" })
        {
            var directory = Directory.CreateTempSubdirectory();
            var (deferred, delay) = (LearningSaves.Deferred, LearningSaves.Delay);
            try
            {
                LearningSaves.Deferred = true;
                LearningSaves.Delay = TimeSpan.FromMinutes(1);
                CheckLearning(directory.FullName, mode);
            }
            finally
            {
                LearningSaves.FlushAll();
                (LearningSaves.Deferred, LearningSaves.Delay) = (deferred, delay);
                directory.Delete(recursive: true);
            }
        }
    }

    private static void CheckLearning(string directory, string mode)
    {
        var languagePath = Path.Combine(directory, "languages.json");
        var historyPath = Path.Combine(directory, "conversions.json");
        var phrasePath = Path.Combine(directory, "phrases.txt");
        var translationPath = Path.Combine(directory, "translations.json");
        var languages = new LanguageMemory(languagePath);
        var history = new ConversionHistory(historyPath);
        var phrases = new PhraseHistory(phrasePath);
        phrases.Remember("にほんごでにゅうりょく", "日本語で入力");
        LearningSaves.FlushAll();
        var originalPhrases = File.ReadAllText(phrasePath);
        var translationHistory = new TranslationHistory(translationPath);
        var converter = new LearningConverter();
        var front = new Front
        {
            Converter = converter, Services = new CompositionServices
            {
                // 学習 OFF でも出る候補 (ユーザー辞書の語) を確定しても覚えないことを見る
                Languages = languages, History = history, Predictor = new Predictor(phrases, () => [("にほんごでにゅうりょく", "日本語で入力")], history),
                Translations = TranslationDictionary.Parse("川\tn\triver"), TranslationHistory = translationHistory,
                MoreCandidates = (reading, _) => reading == "にほんご" ? ["日本語", "日本語文"] : [],
            }
        };
        front.Settings.PredictiveCandidates = true;
        front.Settings.LearningEnabled = mode != "設定OFF";

        void SetField(bool beforeCommit)
        {
            front.Traits.Clear();
            if (mode == "Private" || mode == "Privateから通常欄へ移動" && !beforeCommit) front.Traits.Add("Private");
            if (mode == "Password" && beforeCommit) front.Traits.Add("Password");
        }

        void Commit(string expected, string type = "CommitRequested", string extra = "")
        {
            SetField(beforeCommit: true);
            var output = front.Event(type, extra: extra);
            if (mode == "Password") AssertPasswordPass(front, output);
            else Assert.Equal(expected, Commits(output)[0].GetProperty("text").GetString(), front.Trace);
            SetField(beforeCommit: false);
            front.Document = "";
        }

        void Select(JsonElement output, string candidate)
        {
            var index = Candidates(output).IndexOf(candidate);
            Assert.True(index >= 0, front.Trace);
            front.Event("CandidateClicked", extra: $",\"index\":{index}");
        }

        SetField(beforeCommit: false);
        // 最初の Private 通知は打鍵より先に届く。
        front.Event("CaretMoved", extra: Entered);
        // 後の nihongo を英字にしないように、F10 で直す語は別にする
        front.Key('t');
        front.Type("ao");
        front.Key(VirtualKeys.F10);
        Commit("tao");

        Select(front.Type("nihongo ")[^1], "日本語文");
        Commit("日本語文");
        Select(front.Type("kawa ")[^1], "river");
        Commit("river");
        var predictionIndex = Candidates(front.Type("nihongo")[^1]).IndexOf("日本語で入力");
        Assert.True(predictionIndex >= 0, front.Trace);
        Commit("日本語で入力", "CandidateClicked", $",\"index\":{predictionIndex}");

        var learns = mode == "通常";
        LearningSaves.FlushAll();
        Assert.Equal(learns, languages.Get("tao") == true, mode);
        Assert.Equal(learns ? "日本語文" : null, history.Get("にほんご"), mode);
        Assert.Equal(learns ? 1 : 0, translationHistory.Get("かわ").Count, mode);
        Assert.Equal(learns ? 2 : 1, phrases.Count, mode);
        Assert.Equal(learns, originalPhrases != File.ReadAllText(phrasePath), mode);
        Assert.Equal(learns ? 4 : 1, Directory.GetFiles(directory).Length, mode);
        Assert.Equal(languages.Get("tao"), new LanguageMemory(languagePath).Get("tao"), mode);
        Assert.Equal(history.Get("にほんご"), new ConversionHistory(historyPath).Get("にほんご"), mode);
        Assert.Equal(phrases.Count, new PhraseHistory(phrasePath).Count, mode);
        Assert.Equal(translationHistory.Get("かわ").Count, new TranslationHistory(translationPath).Get("かわ").Count, mode);
        if (mode == "Privateから通常欄へ移動")
        {
            // Private で始めた入力が終わったあと、同じ instance で始め直した通常入力は学習する。
            front.Traits.Clear();
            front.Type("tao");
            front.Key(VirtualKeys.F10);
            front.Key('\n');
            Assert.Equal(true, languages.Get("tao"), front.Trace);
            LearningSaves.FlushAll();
        }
    }

    [Test]
    public static void Private_StillSuppressesLearningAfterPartialCommit()
    {
        var phrases = new PhraseHistory(null);
        var converter = new LearningConverter();
        var front = new Front { Converter = converter, Services = new CompositionServices { Predictor = new Predictor(phrases, null, null) } };
        front.Settings.PredictiveCandidates = true;
        front.Settings.PartialCommit = true;
        front.Traits.Add("Private");
        front.Type("tanniwotoru ");
        Assert.Equal("単位を取る", front.Composition["t1"], front.Trace);
        front.Traits.Clear();
        front.Key(VirtualKeys.Left);
        front.Key(VirtualKeys.Return, shift: true);
        Assert.Equal("単位を", front.Document, front.Trace);
        Assert.Equal("とる", front.Composition["t1"], front.Trace);
        front.Key(' ');
        Assert.True(View(front.Event("StateRequested")).GetProperty("converting").GetBoolean(), front.Trace);
        front.Key('\n');
        Assert.True(front.Document.Length > "単位を".Length, front.Trace);
        Assert.Equal(0, phrases.Count, "Private で始めた入力は、途中で通常欄へ移っても学習しない\n" + front.Trace);
    }

    [Test]
    public static void Private_ReportedBeforeResumingDoesNotLearnOnLaterCommit()
    {
        var phrases = new PhraseHistory(null);
        var converter = new LearningConverter();
        var front = new Front { Converter = converter, Services = new CompositionServices { Predictor = new Predictor(phrases, null, null) } };
        front.Settings.PredictiveCandidates = true;
        front.Type("nihongo");

        // TSF はアプリが表示済みの文字を確定した時点で Private を読み、再開を保留する前に知らせる。
        front.Traits.Add("Private");
        Assert.Equal("にほんご", View(front.Event("StateRequested")).GetProperty("text").GetString(), front.Trace);
        front.Traits.Clear();
        front.Key(' ');
        Assert.Equal("日本語", front.Composition["t1"], front.Trace);
        front.Key('\n');
        Assert.Equal("日本語", front.Document, front.Trace);
        Assert.Equal(0, phrases.Count, "途中で Private と分かった入力も学習しない\n" + front.Trace);

        front.Type("kyou \n");
        Assert.Equal(1, phrases.Count, "次に始めた通常入力は学習する\n" + front.Trace);
    }

    [Test]
    public static void CommitRequestedWithoutField_CommitsForTheComposingField()
    {
        // TSF は編集ロックを取れない旧欄から離れるとき、欄を読めないまま確定を求め、返った文字をあとで旧欄の変換中の範囲に入れる。
        // 前の語の確定し直し (i → I) は入れない。欄の事実が無くても、変換中の欄 (Private) として学習しない。
        const string leave = ",\"caret\":{\"moved\":true,\"passedKey\":0,\"focusLeft\":true}";
        foreach (var isPrivate in new[] { false, true })
        {
            var phrases = new PhraseHistory(null);
            var front = new Front { Converter = new LearningConverter(), Services = new CompositionServices { Predictor = new Predictor(phrases, null, null) } };
            front.Settings.PredictiveCandidates = true;
            if (isPrivate) front.Traits.Add("Private");
            front.Type("i ");
            front.Type("kan");
            var commits = Commits(front.Event("CommitRequested", extra: leave, omitField: true));
            Assert.Equal(1, commits.GetArrayLength(), front.Trace);
            Assert.Equal("かん", commits[0].GetProperty("text").GetString(), front.Trace);
            Assert.Equal(JsonValueKind.Null, commits[0].GetProperty("removeBefore").ValueKind, front.Trace);
            Assert.True(front.Composition["t1"] is null, front.Trace);

            front.Type("nihongo ");
            Assert.Equal("日本語", Commits(front.Event("CommitRequested", extra: leave, omitField: true))[0].GetProperty("text").GetString(), front.Trace);
            Assert.Equal(isPrivate ? 0 : 1, phrases.Count, (isPrivate ? "Private の欄で始めた入力は学習しない\n" : "通常の欄では学習する\n") + front.Trace);
        }
    }

    [Test]
    public static void CandidateClicked_UsesTheLatestPrivateTrait()
    {
        foreach (var isPrivate in new[] { false, true })
        {
            var directory = Directory.CreateTempSubdirectory();
            try
            {
                var path = Path.Combine(directory.FullName, "phrases.txt");
                var phrases = new PhraseHistory(path);
                phrases.Remember("にほんごでにゅうりょく", "日本語で入力");
                LearningSaves.FlushAll();
                var originalPhrases = File.ReadAllText(path);
                var front = new Front
                {
                    Sends = null,
                    Services = new CompositionServices { Predictor = new Predictor(phrases, null, null) },
                };
                front.Settings.PredictiveCandidates = true;
                var index = Candidates(front.Type("nihongo")[^1]).IndexOf("日本語で入力");
                Assert.True(index >= 0, front.Trace);

                // 候補を出したあと、属性の変化をクリックの出来事で初めて知らせる。
                if (isPrivate) front.Traits.Add("Private");
                front.Event("CandidateClicked", extra: $",\"index\":{index}");
                Assert.Equal("日本語で入力", front.Document, front.Trace);
                LearningSaves.FlushAll();
                Assert.Equal(isPrivate, originalPhrases == File.ReadAllText(path), front.Trace);
            }
            finally
            {
                LearningSaves.FlushAll();
                directory.Delete(recursive: true);
            }
        }
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
        front.Settings.TypedKeys = Meltype.Config.TypedKeysStyle.Keys;
        var view = front.Type("kan")[^1].GetProperty("result").GetProperty("view");
        Assert.Equal("かん", view.GetProperty("text").GetString(), front.Trace);
        Assert.Equal("kan", view.GetProperty("typedKeys").GetString(), front.Trace);
        Assert.True(!view.TryGetProperty("committed", out _), "見せる文字は 1 つだけ\n" + front.Trace);
    }

    [Test]
    public static void List_IsOneAndSelectReadsTheLastOne()
    {
        // select の番号は前の応答の一覧で読む
        var front = new Front();
        front.Type("kyou");
        var list = front.Key(' ').GetProperty("result").GetProperty("view").GetProperty("candidates");
        Assert.True(list.ValueKind == JsonValueKind.Object && list.GetProperty("items").GetArrayLength() > 0 && !list.TryGetProperty("prediction", out _),
            "変換の候補の一覧\n" + front.Trace);
        var second = list.GetProperty("items")[1].GetString();
        var selected = front.Event("CandidateClicked", extra: ",\"index\":1");
        Assert.Equal(second, selected.GetProperty("result").GetProperty("view").GetProperty("text").GetString(), "見えていた一覧の 2 番目\n" + front.Trace);
    }

    [Test]
    public static void LockKeys_PassWithoutCommitting()
    {
        // CapsLock・NumLock・ScrollLock は文字を入れないので、打ちかけを確定せずにアプリに通す (フロントによって確定していた)
        foreach (var vk in new[] { 0x14, 0x90, 0x91 })
        {
            var front = new Front();
            front.Type("ka");
            var pressed = front.Key(vk);
            Assert.True(!Consumed(pressed) && front.Composition["t1"] == "か" && front.Document == "", $"0x{vk:X2}: 打ちかけのまま\n" + front.Trace);
            front.Key('\n');
            Assert.Equal("か", front.Document, front.Trace);
        }
        // 切り替えキーに登録した CapsLock (JIS の Shift+英数) は切り替えに使う
        var toggle = new Front();
        toggle.Settings.ToggleShortcuts = [new() { Key = 0x14 }];
        Assert.True(Consumed(toggle.Key(0x14)) && !toggle.Open, "切り替えキーの CapsLock で英数\n" + toggle.Trace);
    }

    [Test]
    public static void KanaKey_IsTheSameKeyOnEveryFront()
    {
        // カタカナ/ひらがな キーは Windows が 0xF2、IBus・fcitx5・Mac が 0x15 で送る
        foreach (var vk in new[] { VirtualKeys.OemCopy, VirtualKeys.Kana })
        {
            var front = new Front();
            front.Key(VirtualKeys.NonConvert);
            Assert.True(Consumed(front.Key(vk)) && front.Open, $"0x{vk:X2}: 日本語にする\n" + front.Trace);
            var toggle = new Front();
            toggle.Settings.ToggleShortcuts = [new() { Key = VirtualKeys.Kana }];
            toggle.Key(vk);
            Assert.True(!toggle.Open, $"0x{vk:X2}: 切り替えキーに登録したら英数\n" + toggle.Trace);
            toggle.Key(vk);
            Assert.True(toggle.Open, $"0x{vk:X2}: もう一度押して日本語\n" + toggle.Trace);
        }
    }

    [Test]
    public static void CtrlH_DeletesWhetherTheFrontSendsTheKeyOrItsCharacter()
    {
        // Meltype キーボードは仮想キーコード、Mac・IBus・fcitx5 はキーの文字 (h) を送る
        foreach (var (vk, ch) in new[] { (0x48, '\0'), (0, 'h') })
        {
            var front = new Front();
            front.Type("kana");
            Assert.True(Consumed(front.Key(vk, ch, ctrl: true)) && front.Composition["t1"] == "か" && front.Document == "", $"vk {vk:X2}: 1 音消す\n" + front.Trace);
        }
    }

    [Test]
    public static void OwnCommit_IsNotTrustedAfterTheCaretMoves()
    {
        // 確定した直後はキャレットの前より自分の確定を信じるが、キャレットが動いたら (別の入力欄・アプリに通したキー) 読めた文字を使う
        foreach (var moved in new[] { "", Entered })
        {
            var front = new Front();
            front.Settings.SpaceAroundEnglish = true;
            front.Settings.LiveConversion = false;
            front.Type("ka\n");
            front.Document = "";
            if (moved.Length > 0) front.Event("CaretMoved", extra: moved);
            // 英数で ka を入れてから日本語で「か」: 前の英単語との間に空白
            front.Key(0xF0);
            front.Type("ka");
            front.Key(0x15);
            front.Type("ka");
            front.Key(0x75);
            front.Key('\n');
            Assert.Equal("ka か", front.Document, front.Trace);
        }
    }

    [Test]
    public static void FieldMode_IsLearnedFromTheSwitchBeforeTyping()
    {
        // #1144: 入力欄に入ってまだ何も打たないうちに切り替えたモードを覚え、次にその入力欄に入ったらそのモードにする
        var front = new Front { Services = new() { FieldModes = new Meltype.Learning.FieldModes(null) }, App = "chat.exe" };
        Assert.True(Japanese(front.Event("CaretMoved", extra: Entered)), front.Trace);
        Assert.True(!Japanese(front.Key(0xF0)), "英数キーで英数に" + front.Trace);
        front.Type("hello ");
        // 打ち始めてからの切り替えは一時的なもの
        Assert.True(Japanese(front.Event("JapaneseModeSelected")), front.Trace);
        front.App = "notepad.exe";
        Assert.True(Japanese(front.Event("CaretMoved", extra: Entered)), "覚えていない入力欄はそのまま" + front.Trace);
        front.App = "chat.exe";
        Assert.True(!Japanese(front.Event("CaretMoved", extra: Entered)), "覚えたモードにする" + front.Trace);
        // 自動で切り替えた直後に逆へ直したら、覚え直す
        Assert.True(Japanese(front.Event("JapaneseModeSelected")), front.Trace);
        front.App = "notepad.exe";
        front.Event("CaretMoved", extra: Entered);
        front.App = "chat.exe";
        Assert.True(Japanese(front.Event("CaretMoved", extra: Entered)), "直したモードを覚え直す" + front.Trace);
    }

    [Test]
    public static void FieldMode_KeepsALimitedNumberOfFields()
    {
        // 覚える入力欄の数には上限があり、古いものから忘れる
        var modes = new Meltype.Learning.FieldModes(null);
        for (var i = 0; i < 1000; i++) modes.Remember($"app{i}.exe\t", false);
        Assert.True(modes.Count < 1000, modes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(false, modes.Get("app999.exe\t"), "新しいものは残る");
        Assert.Equal(null, modes.Get("app0.exe\t"), "古いものは忘れる");
    }

    [Test]
    public static void CapsLock_TypesCapitalsOnlyWhenTheSettingIsOn()
    {
        // #837: 日本語入力中も CapsLock で大文字にしたい人と、CapsLock を ON のまま日本語にしたい人 (#736) がいる
        foreach (var on in new[] { false, true })
        {
            var front = new Front();
            front.Settings.CapsLockTypesCapitals = on;
            foreach (var c in "KA") front.Key(c, c, capsLock: true);
            // CapsLock 中の Shift は小文字
            front.Key('N', 'n', shift: true, capsLock: true);
            front.Key('\n');
            Assert.Equal(on ? "KAn" : "かN", front.Document, front.Trace);
        }
    }

    [Test]
    public static void ThumbShift_HoldsTheThumbKeyUntilItIsReleased()
    {
        // ワークアラウンド (KeyReleased): 親指シフトの配列のときだけ、離したことも受け取る
        var front = new Front();
        Assert.True(!front.Event("StateRequested").GetProperty("state").GetProperty("capture").GetProperty("keyReleases").GetBoolean(), front.Trace);
        front.Settings.InputStyle = InputStyle.Kana;
        front.Settings.KanaLayout = KanaLayout.Nicola;
        JsonElement Press(int vk, char ch = '\0') =>
            front.Event("KeyPressed", extra: $",\"key\":{{\"vk\":{vk},\"ch\":{(int)ch},\"releaseFollows\":true}}");
        JsonElement Release(int vk) => front.Event("KeyReleased", extra: $",\"key\":{{\"vk\":{vk}}}");
        Assert.True(Consumed(Press(VirtualKeys.NonConvert)), front.Trace);
        Assert.True(!Consumed(Release(VirtualKeys.NonConvert)), "文頭の無変換の単独押しはアプリに渡せる\n" + front.Trace);
        Assert.True(Press(VirtualKeys.Convert).GetProperty("state").GetProperty("capture").GetProperty("keyReleases").GetBoolean(), front.Trace);
        // 右親指 (変換) を押したまま W: 右シフト面の「が」
        Press('W', 'w');
        Release('W');
        Assert.True(Consumed(Release(VirtualKeys.Convert)), "同時打鍵に使った親指キーはアプリへ渡さない\n" + front.Trace);
        Assert.Equal("が", front.Composition["t1"], "親指キーを離すまでシフト面\n" + front.Trace);
        // 離してから打った W はふつうの面の「か」
        Press('W', 'w');
        Release('W');
        Assert.Equal("がか", front.Composition["t1"], front.Trace);
    }

    private static string Physical(string type, int code) => type switch
    {
        "Evdev" => $"{{\"type\":\"Evdev\",\"code\":{code}}}",
        "Mac" => $"{{\"type\":\"Mac\",\"keyCode\":{code}}}",
        "Windows" => $"{{\"type\":\"Windows\",\"scan\":{code},\"extended\":false}}",
        _ => throw new ArgumentException(type),
    };

    /// <summary>文字を入力するキーを、仮想キーコードを付けずに物理キーと文字で送る (Linux・macOS のフロント)。</summary>
    private static JsonElement PhysicalKey(Front front, string physical, char ch, bool shift = false, bool releaseFollows = false) =>
        front.Event("KeyPressed", extra: $",\"key\":{{\"vk\":0,\"ch\":{(int)ch},\"shift\":{(shift ? "true" : "false")},\"releaseFollows\":{(releaseFollows ? "true" : "false")},\"physical\":{physical}}}");

    [Test]
    public static void KanaInput_ReadsTheKanaOfThePhysicalKey()
    {
        // #2434: 記号の文字からはキーが決まらない (「\」は ¥ キーとろキーのどちらでも出る)。Shift+数字の記号も
        foreach (var (os, t, at, semicolon, yen, ro, three) in new[]
        {
            ("Evdev", 20, 26, 39, 124, 89, 4),
            ("Mac", 0x11, 0x21, 0x29, 0x5D, 0x5E, 0x14),
            ("Windows", 0x14, 0x1A, 0x27, 0x7D, 0x73, 0x04),
        })
        {
            var front = new Front();
            front.Settings.InputStyle = InputStyle.Kana;
            PhysicalKey(front, Physical(os, t), 't');
            PhysicalKey(front, Physical(os, at), '@');
            PhysicalKey(front, Physical(os, semicolon), ';');
            PhysicalKey(front, Physical(os, yen), '\\');
            PhysicalKey(front, Physical(os, ro), '\\');
            PhysicalKey(front, Physical(os, three), '#', shift: true);
            Assert.Equal("がれーろぁ", front.Composition["t1"], $"{os}\n{front.Trace}");
        }
    }

    [Test]
    public static void PhysicalKey_KanaFollowsThePositionAndRomajiFollowsTheCharacter()
    {
        // Dvorak の配列: S の位置のキーで o が入る
        var kana = new Front();
        kana.Settings.InputStyle = InputStyle.Kana;
        PhysicalKey(kana, Physical("Evdev", 31), 'o');
        Assert.Equal("と", kana.Composition["t1"], kana.Trace);
        var romaji = new Front();
        PhysicalKey(romaji, Physical("Evdev", 47), 'k');
        PhysicalKey(romaji, Physical("Evdev", 31), 'o');
        Assert.Equal("こ", romaji.Composition["t1"], romaji.Trace);
        // 中指NICOLA: 離したキーも位置で読む。Dvorak の D の位置は e、J の位置は h。D を押したまま J を先に離すと D+J の「お」
        var middle = new Front();
        middle.Settings.InputStyle = InputStyle.Kana;
        middle.Settings.KanaLayout = KanaLayout.NakayubiNicola;
        PhysicalKey(middle, Physical("Evdev", 32), 'e', releaseFollows: true);
        PhysicalKey(middle, Physical("Evdev", 36), 'h', releaseFollows: true);
        middle.Event("KeyReleased", extra: $",\"key\":{{\"vk\":{(int)'H'},\"physical\":{Physical("Evdev", 36)}}}");
        middle.Event("KeyReleased", extra: $",\"key\":{{\"vk\":{(int)'E'},\"physical\":{Physical("Evdev", 32)}}}");
        Assert.Equal("お", middle.Composition["t1"], middle.Trace);
        // 英数状態で打った語を日本語に戻すときも、かなは位置で決める (Dvorak の k・a は QWERTY の v・a の位置)
        var direct = new Front { ClassifyDirect = (letters, _, _) => letters == "ka" ? Verdict.Japanese : Verdict.Undecided };
        direct.Settings.InputStyle = InputStyle.Kana;
        direct.Settings.DirectModeAutoDetect = true;
        direct.Event("AlphanumericModeSelected");
        PhysicalKey(direct, Physical("Evdev", 47), 'k');
        PhysicalKey(direct, Physical("Evdev", 30), 'a');
        Assert.Equal("ひち", direct.Composition["t1"], direct.Trace);
    }

    [Test]
    public static void Numpad_FollowsTheNumberKeysSetting()
    {
        // テンキーも上の段の数字と同じく、設定「変換中の数字キーで候補を選ぶ」に従う (OFF なのにテンキーだけ番号で選んでいた)
        var front = new Front();
        front.Settings.NumberKeysSelectCandidates = false;
        front.Type("kyouha");
        var converting = front.Key(' ');
        Assert.True(!converting.GetProperty("result").GetProperty("view").GetProperty("candidates").GetProperty("numbered").GetBoolean(),
            "数字キーで選べないので番号を出さない\n" + front.Trace);
        front.Key(0x61, '1');
        front.Key(0x60, '0');
        front.Key('\n');
        Assert.Equal("今日は10", front.Document, "今の候補で確定してから数字を入力する\n" + front.Trace);
    }

    [Test]
    public static void List_IsNumberedOnlyWhenNumberKeysSelect()
    {
        // 数字キーで選べるのは変換中の候補だけ。予測変換の一覧の数字は、そのまま打つ文字になる
        var front = new Front { Services = new CompositionServices { Predictor = new Predictor(null, null, null) } };
        front.Settings.PredictiveCandidates = true;
        front.Document = "I made ";
        static bool Numbered(JsonElement output) => output.GetProperty("result").GetProperty("view").GetProperty("candidates").GetProperty("numbered").GetBoolean();
        Assert.True(!Numbered(front.Type("decis")[^1]), "予測変換\n" + front.Trace);
        front = new Front();
        front.Type("kyou");
        Assert.True(Numbered(front.Key(' ')), "変換中\n" + front.Trace);
    }

    [Test]
    public static void Select_HighlightDoesNotCommitAPrediction()
    {
        // マウスでなぞった (CandidateHighlighted) だけでは予測変換の候補を確定しない。クリックしたら確定する
        var front = new Front { Services = new CompositionServices { Predictor = new Predictor(null, null, null) } };
        front.Settings.PredictiveCandidates = true;
        front.Document = "I made ";
        var list = front.Type("decis")[^1].GetProperty("result").GetProperty("view").GetProperty("candidates");
        Assert.True(list.ValueKind == JsonValueKind.Object && list.GetProperty("items").GetArrayLength() > 1, "予測変換の候補を出す\n" + front.Trace);
        Assert.True(Commits(front.Event("CandidateHighlighted", extra: ",\"index\":1")).GetArrayLength() == 0, "なぞっただけ\n" + front.Trace);
        Assert.Equal(list.GetProperty("items")[1].GetString(), Commits(front.Event("CandidateClicked", extra: ",\"index\":1"))[0].GetProperty("text").GetString(),
            "クリックした候補を確定する\n" + front.Trace);
    }

    [Test]
    public static void ModeSelected_SetsTheModeWhateverItWas()
    {
        // 入力メニューで選んだモードは、切り替えでなく、選んだモードにする (同じモードを選んでも変わらない)
        var front = new Front();
        front.Settings.ModeIndicator = ModeIndicatorTiming.OnSwitch;
        var direct = front.Event("AlphanumericModeSelected");
        Assert.True(!Japanese(direct) && Indicator(direct), "英数にして表示する\n" + front.Trace);
        Assert.True(!Japanese(front.Event("AlphanumericModeSelected")), "もう一度選んでも英数\n" + front.Trace);
        Assert.True(Japanese(front.Event("JapaneseModeSelected")), "日本語にする\n" + front.Trace);
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
        var shell = new Front { App = "gnome-shell" };
        Assert.True(!Indicator(shell.Event("CaretMoved", extra: Entered)), "GNOME Shell の画面では、入ったときは表示しない\n" + shell.Trace);
        Assert.True(Indicator(shell.Key(ToggleShortcut.Hankaku)), "切り替えたら表示する\n" + shell.Trace);
        var front = new Front();
        Assert.True(Indicator(front.Event("CaretMoved", extra: Entered)), "入力欄に入ったときも表示する\n" + front.Trace);
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
        Assert.True(!Commits(front.Key('\n'))[0].GetProperty("insertByPaste").GetBoolean(), "ふつうはキーで入れる\n" + front.Trace);
        front.Settings.AppRules.Add(new AppRule { Process = "paste.exe", Enabled = true, Commit = CommitMethod.Paste });
        front.App = "paste.exe";
        front.Type("ka");
        Assert.True(Commits(front.Key('\n'))[0].GetProperty("insertByPaste").GetBoolean(), "アプリ別設定で貼り付け\n" + front.Trace);
        front = new Front();
        front.Snippets.Set("sig", "--\nメルト");
        front.Key(0xBA, ';');
        front.Type("sig");
        Assert.True(Commits(front.Key(' '))[0].GetProperty("insertByPaste").GetBoolean(), "改行を含む定型文は貼り付け\n" + front.Trace);
    }

    [Test]
    public static void Capture_ListsControlKeysUsedWhileComposing()
    {
        var front = new Front();
        Assert.Equal(0, Capture(front.Event("StateRequested")).GetProperty("ctrlKeysWhileComposing").GetArrayLength(), "入力していなければ空\n" + front.Trace);
        var typing = front.Key('k');
        var keys = Capture(typing).GetProperty("ctrlKeysWhileComposing").EnumerateArray().Select(k => (k.GetProperty("vk").GetInt32(), k.GetProperty("modifiers").GetInt32())).ToList();
        Assert.True(keys.Contains((VirtualKeys.Return, 2)) && keys.Contains(('U', 2)) && keys.Contains(('H', 2)) && !keys.Contains(('J', 2)),
            "Ctrl+Enter・Ctrl+U・Ctrl+H は変換ボックスで使う\n" + front.Trace);
        front.Settings.ControlKeys = ControlKeyLayout.Mac;
        keys = Capture(front.Key('a')).GetProperty("ctrlKeysWhileComposing").EnumerateArray().Select(k => (k.GetProperty("vk").GetInt32(), k.GetProperty("modifiers").GetInt32())).ToList();
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
    public static void CodeApps_KeepCommandNamesLowercase()
    {
        // 固有名詞の自動の大文字小文字 (mysql → MySQL、jquery → jQuery) は、コードの行や文字列リテラルの中では効かせない
        foreach (var (app, before, typed) in new[]
        {
            ("Code.exe", "import ", "numpy"),
            ("Code.exe", "db = ", "mysql"),
            ("WindowsTerminal.exe", "$ ", "mysql"),
            ("Code.exe", "require('", "jquery"),
            ("Code.exe", "image = \"", "mysql"),
        })
        {
            var front = new Front { App = app, Document = before };
            front.Type(typed);
            if (front.Composition.GetValueOrDefault("t1") is not null) front.Key('\n');
            Assert.Equal(before + typed, front.Document, $"{app}: {before}{typed}\n" + front.Trace);
        }
        // コードのコメントの中は文章なので、通常どおり自動で直す
        var comment = new Front { App = "Code.exe", Document = "// " };
        comment.Type("iphone\n");
        Assert.Equal("// iPhone", comment.Document, comment.Trace);
    }

    [Test]
    public static void Snippets_ExpandFromTheTextBeforeTheCaret()
    {
        // 何も打っていないときの Space・Tab・Esc は受け取らないキーなので、定型文に使うときだけ capture に入れる (TSF・Meltype キーボードはほかを通す)
        static IEnumerable<int> Plain(JsonElement output) => output.GetProperty("state").GetProperty("capture").GetProperty("shortcuts").EnumerateArray()
            .Where(k => k.GetProperty("modifiers").GetInt32() == 0).Select(k => k.GetProperty("vk").GetInt32());
        var front = new Front();
        front.Snippets.Set("ty", "ありがとう");
        JsonElement typed = default;
        foreach (var c in ";ty")
        {
            Assert.True(!Plain(typed.ValueKind == JsonValueKind.Undefined ? front.Event("StateRequested") : typed).Contains(VirtualKeys.Tab), $"{c} の前は Tab を通す\n" + front.Trace);
            typed = front.Key(c == ';' ? 0xBA : c - 'a' + 'A', c);
            Assert.True(!Consumed(typed), $"{c} はそのままアプリへ\n" + front.Trace);
        }
        Assert.True(Plain(typed).Contains(VirtualKeys.Space) && Plain(typed).Contains(VirtualKeys.Tab), "登録した名前を打ったら Space・Tab を受け取る\n" + front.Trace);
        Assert.True(Plain(front.Key(' ')).Contains(VirtualKeys.Escape), "入れた直後は Esc を受け取る\n" + front.Trace);
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
        var closed = front.Event("CaretMoved", extra: ",\"caret\":{}");
        Assert.True(!Japanese(closed) && !Indicator(closed), "合わせるだけで表示はしない\n" + front.Trace);
        Assert.True(!Consumed(front.Key('a')), front.Trace);
        front.Open = true;
        Assert.True(Consumed(front.Key('k')), front.Trace);
    }

    [Test]
    public static void ClosedWhileComposing_CommitsOnce()
    {
        // 入力中にアプリが IME を閉じた: TSF の DLL は、開閉だけを先に知らせ (応答の確定は入れない)、入力欄を編集できるようになってから確定を頼む。
        // 状態だけを受け取る出来事では入力を続け、確定を頼まれたときに 1 回だけ確定する
        foreach (var notice in new[] { ("StateRequested", ""), ("CaretMoved", ",\"caret\":{}") })
        {
            var front = new Front();
            front.Type("ka");
            front.Open = false;
            var noticed = front.Event(notice.Item1, extra: notice.Item2);
            Assert.True(Commits(noticed).GetArrayLength() == 0 && front.Composition["t1"] == "か" && !Japanese(noticed), front.Trace);
            front.Event("CommitRequested");
            Assert.Equal("か", front.Document, front.Trace);
            front.Event("CommitRequested");
            Assert.True(!Consumed(front.Key('a')) && front.Document == "かa", "確定した後は英数\n" + front.Trace);
        }
    }

    [Test]
    public static void Capture_FollowsTheModeAndSettings()
    {
        static bool HasKey(JsonElement capture, int key, int mods) =>
            capture.GetProperty("shortcuts").EnumerateArray().Any(k => k.GetProperty("vk").GetInt32() == key && k.GetProperty("modifiers").GetInt32() == mods);
        var front = new Front();
        var japanese = Capture(front.Event("StateRequested"));
        Assert.True(japanese.GetProperty("printableAscii").GetBoolean() && japanese.GetProperty("printableAsciiExcept").GetString() == "", "日本語では文字を受け取る\n" + front.Trace);
        Assert.True(HasKey(japanese, VirtualKeys.Space, 1) && !HasKey(japanese, VirtualKeys.Space, 0), "Shift+Space は受け取り、半角の Space は通す\n" + front.Trace);
        front.Settings.SpaceWidth = SpaceWidth.FullWidth;
        Assert.True(HasKey(Capture(front.Event("StateRequested")), VirtualKeys.Space, 0), "全角スペースなら Space も受け取る\n" + front.Trace);
        front.Open = false;
        var english = Capture(front.Event("StateRequested"));
        Assert.True(!english.GetProperty("printableAscii").GetBoolean() && !HasKey(english, VirtualKeys.Space, 1), "既定の英数ではアプリへ通す\n" + front.Trace);
        Assert.True(HasKey(english, ToggleShortcut.Hankaku, 0), "英数でも切り替えキーは受け取る\n" + front.Trace);
        front.Type("kyouha");
        Assert.Equal("kyouha", front.Document, front.Trace);
        Assert.True(!front.Open && front.Composition["t1"] is null, "英数のまま確定先に入る\n" + front.Trace);
        front.Settings.DirectModeAutoDetect = true;
        english = Capture(front.Event("StateRequested"));
        Assert.True(english.GetProperty("printableAscii").GetBoolean(), "設定を ON にしたときだけ英数でも判定する\n" + front.Trace);
    }

    [Test]
    public static void Capture_TakesSpaceOnlyWhereItTypesJapanese()
    {
        // #1883: 英数とコードの行では、全角スペースにならない Shift+Space・Space をアプリに通す
        static bool HasSpace(Front front, int mods) =>
            Capture(front.Event("StateRequested")).GetProperty("shortcuts").EnumerateArray()
                .Any(k => k.GetProperty("vk").GetInt32() == VirtualKeys.Space && k.GetProperty("modifiers").GetInt32() == mods);
        static void Check(Front front, bool expected)
        {
            front.Settings.SpaceWidth = SpaceWidth.FullWidth;
            Assert.Equal(expected, HasSpace(front, (int)ShortcutModifiers.Shift), front.Trace);
            Assert.Equal(expected, HasSpace(front, 0), front.Trace);
        }
        Check(new Front(), true);
        Check(new Front { Open = false }, false);
        var code = new Front { Terminal = true };
        code.Type("ls ");
        Check(code, false);
        code.Key(ToggleShortcut.Hankaku);
        Check(code, true);
        code.Key(ToggleShortcut.Hankaku);
        Check(code, false);
        code.Document = "echo \"";
        Check(code, true);
        // コードの行で最初に打ったキーが Shift+Space でも、その応答から受け取らない (TSF はその応答を適用してから送り直す)
        var first = new Front { Terminal = true, Document = "ls " };
        Assert.True(!Consumed(first.Key(VirtualKeys.Space, ' ', shift: true)), first.Trace);
        Check(first, false);
        // 入力モード切り替えキーに割り当てた Shift+Space は、英数でもコードの行でも受け取る
        var toggle = new Front { Terminal = true };
        toggle.Type("ls ");
        foreach (var front in new[] { new Front { Open = false }, toggle })
        {
            front.Settings.ToggleShortcuts = [new() { Key = VirtualKeys.Space, Modifiers = ShortcutModifiers.Shift }];
            Assert.True(HasSpace(front, (int)ShortcutModifiers.Shift), "切り替えキーは受け取る\n" + front.Trace);
        }
    }

    [Test]
    public static void CharacterKeys_NeedNoVirtualKey()
    {
        // 文字を入力するキーは ch だけで送れる (仮想キーコードは本体が決める)
        var front = new Front();
        foreach (var c in "kyouha,")
            front.Send($"{{\"version\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"field\":{{\"before\":\"\",\"composing\":{(c == 'k' ? "false" : "true")}}},\"key\":{{\"ch\":{(int)c}}}}}");
        var view = front.Send($"{{\"version\":{ImeHost.Version},\"type\":\"StateRequested\",\"instance\":\"t1\"}}");
        var enter = front.Send($"{{\"version\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"field\":{{\"before\":\"\",\"composing\":true}},\"key\":{{\"vk\":13}}}}");
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
        var list = typing.GetProperty("result").GetProperty("view").GetProperty("candidates");
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
    public static void Terminal_WithoutTheTextBeforeTheCaret_DecidesLikeWithIt()
    {
        // Windows Terminal の TSF はキャレットの前を空で渡し、iTerm2 は日本語を出したあと読めないことがある。
        // 本体が通したキーと確定から組み立てた今の行で、読めるときと同じに決める
        string Run(string? sends)
        {
            var front = new Front { Terminal = true, Sends = sends };
            front.Type("echo\"kome\"kome\n");
            front.Type("echo\"kome");
            front.Key('\n');
            return front.Document + "|" + front.Composition.GetValueOrDefault("t1");
        }
        var readable = Run("Document");
        Assert.Equal("echo\"こめ\"kome\necho\"こめ|", readable, "読めるときは引用符の中だけ日本語");
        Assert.Equal(readable, Run(null), "読めない");
        Assert.Equal(readable, Run(""), "空で届く");
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
    public static void CodeLine_AltTapsSelectTheLanguage()
    {
        // 右 Alt の単独押しでコードの行を日本語にし、左 Alt で英数に戻す (設定「左 Alt で英数 / 右 Alt で日本語」)
        var front = new Front { Terminal = true, Document = "ls " };
        front.Settings.AltKeysSwitchKeyboard = true;
        JsonElement Tap(int vk)
        {
            front.Event("KeyPressed", extra: $",\"key\":{{\"vk\":{vk},\"alt\":true}}");
            return front.Event("KeyReleased", extra: $",\"key\":{{\"vk\":{vk}}}");
        }
        var japanese = Tap(VirtualKeys.RMenu);
        Assert.True(Consumed(japanese) && Japanese(japanese) && front.Open, "コードの行を日本語にする\n" + front.Trace);
        front.Type("kyou");
        Assert.Equal("きょう", front.Composition["t1"], front.Trace);
        front.Key('\n');
        var back = Tap(VirtualKeys.LMenu);
        Assert.True(Consumed(back) && !Japanese(back), "左 Alt で英数\n" + front.Trace);
        Assert.True(!Consumed(front.Key('k')), "コードの行は英数のまま\n" + front.Trace);
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
        var unknown = front.Send($"{{\"version\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t1\",\"imeOpen\":true,\"field\":{{\"traits\":[\"Terminal\"]}},\"key\":{{\"vk\":75,\"ch\":107}}}}");
        Assert.True(Active(unknown) && !Consumed(unknown), "キャレットの前を読めないコードの入力欄は、コードの行として英数\n" + front.Trace);
    }
}
