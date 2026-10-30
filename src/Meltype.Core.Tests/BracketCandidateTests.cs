// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

internal static class BracketCandidateTests
{
    // 仕様の入力別一覧。片括弧も同じ側だけを同じ順序で出す。
    private static readonly (string Input, string Order)[] Cases =
    [
        ("「」", "「」 ｢｣ 『』 ［］ [] 【】 〔〕"),
        ("『』", "『』 「」 ［］ [] 【】 〔〕 ｢｣"),
        ("（）", "（） ()"),
        ("()", "() （）"),
        ("［］", "［］ [] 「」 『』 【】 〔〕 ｢｣"),
        ("[]", "[] ［］ 「」 『』 【】 〔〕 ｢｣"),
        ("｛｝", "｛｝ {}"),
        ("{}", "{} ｛｝"),
        ("【】", "【】 「」 『』 ［］ [] 〔〕 ｢｣"),
        ("〈〉", "〈〉 《》 ＜＞ <>"),
        ("《》", "《》 〈〉 ＜＞ <>"),
        ("〔〕", "〔〕 「」 『』 ［］ [] 【】 ｢｣"),
        ("｢｣", "｢｣ 「」 『』 ［］ [] 【】 〔〕"),
        ("＜＞", "＜＞ <> 〈〉 《》"),
        ("<>", "<> ＜＞ 〈〉 《》"),
    ];

    [Test]
    public static void Brackets_AllShapesKeepTheirFamilyAndOrder()
    {
        foreach (var (input, order) in Cases)
        {
            foreach (var side in new[] { -1, 0, 1 })
            {
                var typed = side < 0 ? input : input[side].ToString();
                var expected = order.Split(' ').Select(p => side < 0 ? p : p[side].ToString());
                var actual = BracketCandidates.For(typed);
                Assert.True(actual is not null, typed);
                Assert.Equal(string.Join(" ", expected), string.Join(" ", actual!.Candidates), typed);
                Assert.Equal(typed, actual.Candidates[0]);
            }
        }
    }

    [Test]
    public static void Brackets_WidthNotesFollowTheCandidateAfterReordering()
    {
        // 全候補の注記表は複製せず、幅の異なる入力で先頭が変わっても注記が追従することを確かめる。
        foreach (var (input, note) in new[] { ("「」", CandidateNote.FullWidth), ("｢｣", CandidateNote.HalfWidth) })
        {
            foreach (var side in new[] { -1, 0, 1 })
            {
                var typed = side < 0 ? input : input[side].ToString();
                var actual = BracketCandidates.For(typed)!;
                Assert.Equal(typed, actual.Candidates[0]);
                Assert.Equal(note, actual.Notes[0], "並べ替え後の先頭候補の幅");
                var unlabelled = side < 0 ? "『』" : "『』"[side].ToString();
                Assert.Equal(CandidateNote.None, actual.Notes[actual.Candidates.IndexOf(unlabelled)], "幅違いがない候補は注記なし");
            }
        }
    }

    [Test]
    public static void Brackets_OnlySingleOrEmptyMatchedPairsAreSpecial()
    {
        foreach (var input in new[] { "", "「文」", "(abc)", "文「」", "「 」", " 「」", "「」 ", "「)", "［」", ")(", "「」()", "「『』」", "((", "\"\"", "“”" })
            Assert.True(BracketCandidates.For(input) is null, input);
    }

    [Test]
    public static void Issue870_SpaceAndTabIncludeTheClosingBracket()
    {
        foreach (var tab in new[] { false, true })
        {
            foreach (var square in new[] { false, true })
            {
                foreach (var raw in new[] { "[", "]", "[]", "(", ")", "()", "{", "}", "{}", "<", ">", "<>" })
                {
                    var k = new CompositionTests.Keyboard(tabConversion: tab) { SquareBrackets = square };
                    k.Type(raw);
                    k.Press(tab ? VirtualKeys.Tab : VirtualKeys.Space);
                    var view = k.Host.View;
                    Assert.True(view?.Converting == true, $"{raw}, Tab={tab}, Square={square}");
                    var expected = raw.Replace("[", square ? "［" : "「").Replace("]", square ? "］" : "」");
                    Assert.Equal(expected, view!.Text, raw);
                    Assert.Equal(1, view.Clauses!.Count, raw);
                    Assert.Equal(0, view.SelectedIndex, raw);
                    Assert.Equal(string.Join(" ", BracketCandidates.For(expected)!.Candidates), string.Join(" ", view.Candidates), raw);
                }
            }
        }
    }

    [Test]
    public static void Brackets_ZInputUsesDisplayedBrackets()
    {
        // Raw の z 記号入力ではなく、表示上の片括弧・ペアから専用候補を作る。
        foreach (var (raw, expected) in new[] { ("z[", "『"), ("z]", "』"), ("z[z]", "『』") })
        {
            var k = new CompositionTests.Keyboard { SquareBrackets = true };
            k.Type(raw);
            k.Press(VirtualKeys.Space);
            var view = k.Host.View!;
            Assert.True(view.Converting, raw);
            Assert.Equal(expected, view.Text, raw);
            Assert.Equal(1, view.Clauses!.Count, raw);
            Assert.Equal(string.Join(" ", BracketCandidates.For(expected)!.Candidates), string.Join(" ", view.Candidates), raw);
        }
    }

    [Test]
    public static void Brackets_FullWidthSettingAndKanaInputAreRespected()
    {
        var full = new CompositionTests.Keyboard { FullWidthParentheses = true };
        full.Type("() ");
        Assert.Equal("（） ()", string.Join(" ", full.Host.View!.Candidates));
        var kana = new CompositionTests.Keyboard { Kana = true };
        kana.TypeKeys((0xDB, true), (0xDD, true));
        kana.Press(VirtualKeys.Space);
        Assert.Equal("「」", kana.Host.View!.Text);
        Assert.Equal(1, kana.Host.View.Clauses!.Count);
    }

    [Test]
    public static void Brackets_TabCyclesOnlyWithinItsFamily()
    {
        var k = new CompositionTests.Keyboard(tabConversion: true);
        k.Type("{}");
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Tab);
        Assert.Equal("｛｝", k.Host.View!.Text);
        k.Press(VirtualKeys.Tab);
        Assert.Equal("{}", k.Host.View!.Text);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Tab);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("｛｝", k.Host.View!.Text);
        k.Press(VirtualKeys.Return);
        Assert.Equal("｛｝", k.Host.Document);
        var disabled = new CompositionTests.Keyboard();
        disabled.Type("{}");
        disabled.Press(VirtualKeys.Tab);
        Assert.True(disabled.Host.View?.Converting != true, "Tab変換無効なら変換を始めない");
    }

    [Test]
    public static void Brackets_CannotResizeAndCancellationRestoresInput()
    {
        var k = new CompositionTests.Keyboard { SquareBrackets = true };
        k.Type("[] ");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal(1, k.Host.View!.Clauses!.Count);
        Assert.Equal("［］", k.Host.View.Text);
        k.Controller.SelectCandidate(2);
        Assert.Equal("「」", k.Host.View.Text);
        k.Press(VirtualKeys.Escape);
        Assert.True(!k.Host.View!.Converting, "Escで変換を取り消す");
        Assert.Equal("［］", k.Host.View.Text);
        Assert.Equal("", k.Host.Document);
    }

    [Test]
    public static void Brackets_ContinueAfterConversionRestoresSelectionWithoutReordering()
    {
        foreach (var full in new[] { false, true })
        {
            foreach (var raw in new[] { "[", "]", "[]", "(", ")", "()", "{", "}", "{}", "<", ">", "<>" })
            {
                var k = new CompositionTests.Keyboard
                { ContinueAfterConversion = true, SquareBrackets = full, FullWidthParentheses = full };
                k.Type(raw + " ");
                var candidates = k.Host.View!.Candidates.ToArray();
                var notes = k.Host.View.Notes!.ToArray();
                k.Controller.SelectCandidate(1);
                k.Type("a\b ");
                Assert.Equal(candidates[1], k.Host.View!.Text, raw);
                Assert.Equal(1, k.Host.View.SelectedIndex, raw);
                Assert.Equal(1, k.Host.View.Clauses!.Count, raw);
                Assert.Equal(string.Join(" ", candidates), string.Join(" ", k.Host.View.Candidates), raw);
                Assert.Equal(string.Join(" ", notes), string.Join(" ", k.Host.View.Notes!), raw);
                k.Press(VirtualKeys.Return);
                Assert.Equal(candidates[1], k.Host.Document, raw);

                // 選択の保持は同じ未確定入力の中だけ。次の入力の候補順・初期選択は変えない。
                k.Type(raw + " ");
                Assert.Equal(0, k.Host.View!.SelectedIndex, raw);
                Assert.Equal(string.Join(" ", candidates), string.Join(" ", k.Host.View.Candidates), raw);
                k.Press(VirtualKeys.Return);
            }
        }
    }

    [Test]
    public static void Brackets_ContinuedTypingShowsTheReadingLikeOtherConversions()
    {
        // 続けて打つと表示は入力に戻り、選んだ形は再変換のときだけ復元する (ContinueAfterConversion の既存仕様)。
        var k = new CompositionTests.Keyboard { ContinueAfterConversion = true };
        k.Type("[] ");
        k.Controller.SelectCandidate(5);
        Assert.Equal("【】", k.Host.View!.Text);
        k.Type("a");
        Assert.Equal("", k.Host.Document, "続けて入力しても未確定");
        var shown = k.Host.View!.Text;
        Assert.True(!shown.Contains('【'), shown);
        k.Press(VirtualKeys.Return);
        Assert.Equal(shown, k.Host.Document);
    }

    [Test]
    public static void Brackets_SessionContinuesTypingWithoutCommittingAndRestoresSelection()
    {
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { ContinueAfterConversion = () => true, LiveConversion = () => true }, () => new Settings());
        session.HandleKey(0xDB, '[', false, false, false, false);
        session.HandleKey(0xDD, ']', false, false, false, false);
        var original = session.HandleKey(VirtualKeys.Space, null, false, false, false, false).View!;
        for (var i = 0; i < 4; i++) session.HandleKey(VirtualKeys.Space, null, false, false, false, false);
        var selected = session.HandleKey(VirtualKeys.Space, null, false, false, false, false);
        Assert.Equal("【】", selected.View!.Text);
        var continued = session.HandleKey('A', 'a', false, false, false, false);
        Assert.Equal(0, continued.Commits.Count);
        Assert.True(!continued.View!.Text.Contains('【'), continued.View.Text);
        session.HandleKey(VirtualKeys.Back, null, false, false, false, false);
        var restored = session.HandleKey(VirtualKeys.Space, null, false, false, false, false).View!;
        Assert.Equal("【】", restored.Text);
        Assert.Equal(5, restored.SelectedIndex);
        Assert.Equal(string.Join(" ", original.Candidates), string.Join(" ", restored.Candidates));
        Assert.Equal("【】", session.CommitPending().Commits.Single().Text);
    }

    private sealed class CountingConverter : IKanjiConverter, ILearningConverter
    {
        public int Calls { get; private set; }
        public ManualResetEventSlim Learned { get; } = new();
        public string? Convert(string reading) { Calls++; return reading; }
        public IReadOnlyList<ConversionClause>? ConvertClauses(string reading, string? context = null)
        {
            Calls++;
            return [new(reading, reading)];
        }
        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses) => Learned.Set();
    }

    [Test]
    public static void Brackets_DoNotCallConvertersOrReadOrWriteLearning()
    {
        var history = new ConversionHistory(null);
        history.Remember("「", "【");
        history.Remember("」", "】");
        history.Remember("「」", "【】");
        var version = history.Version;
        var converter = new CountingConverter();
        var moreCalls = 0;
        foreach (var learning in new[] { false, true })
        {
            foreach (var raw in new[] { "[", "]", "[]", "()", "{}", "<>" })
            {
                var k = new CompositionTests.Keyboard(history: history, converter: converter,
                    moreCandidates: _ => { moreCalls++; return ["外部候補"]; })
                { Learning = learning };
                k.Type(raw + " ");
                Assert.Equal(0, k.Host.View!.SelectedIndex);
                k.Controller.SelectCandidate(1);
                k.Press(VirtualKeys.Return);
            }
        }
        Assert.Equal(version, history.Version, "選び直しても履歴を更新しない");
        Assert.Equal(0, converter.Calls, "括弧をエンジンに渡さない");
        Assert.Equal(0, moreCalls, "表示や候補送りでも外部候補を追加しない");
        Assert.True(!converter.Learned.Wait(100), "非同期の外部学習を送らない");
    }

    [Test]
    public static void Brackets_TextAndMismatchesStillUseTheEngine()
    {
        foreach (var input in new[] { "[ka]", "(ka)", "[)", "[][ ]", "[z[z]]" })
        {
            var converter = new CountingConverter();
            var k = new CompositionTests.Keyboard(converter: converter);
            k.Type(input + " ");
            Assert.True(converter.Calls > 0, input);
        }
        var direct = new CompositionTests.Keyboard(direct: true);
        direct.Type("[] ");
        Assert.True(direct.Host.View?.Converting != true, "直接入力を変換しない");
    }

    [Test]
    public static void Brackets_SessionJsonAndClicksKeepNotesOutOfCommits()
    {
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { TabConversion = () => true }, () => new Settings());
        session.HandleKey(0xDB, '[', false, false, false, false);
        session.HandleKey(0xDD, ']', false, false, false, false);
        var result = session.HandleKey(VirtualKeys.Tab, null, false, false, false, false);
        using var json = JsonDocument.Parse(result.ToJson());
        var view = json.RootElement.GetProperty("view");
        Assert.Equal(1, view.GetProperty("clauses").GetArrayLength());
        Assert.Equal(7, view.GetProperty("candidates").GetArrayLength());
        Assert.Equal("全角", view.GetProperty("notes")[0].GetString());
        Assert.Equal("半角", view.GetProperty("notes")[1].GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("notes")[2].ValueKind);
        session.SelectCandidate(1);
        var committed = session.CommitPending();
        Assert.Equal("｢｣", string.Concat(committed.Commits.Select(c => c.Text)));
    }

    [Test]
    public static void Brackets_CursorIntoBracketsKeepsTheExistingPairs()
    {
        foreach (var raw in new[] { "[]", "()", "{}", "<>" })
        {
            var expected = BracketCandidates.For(raw == "[]" ? "「」" : raw)!.Candidates;
            for (var i = 0; i < expected.Count; i++)
            {
                var k = new CompositionTests.Keyboard { CursorIntoBrackets = true };
                k.Type(raw + " ");
                k.Controller.SelectCandidate(i);
                k.Press(VirtualKeys.Return);
                Assert.Equal(expected[i], k.Host.Document);
                // 従来のカッコの組だけが対象。<>・＜＞・｢｣ は含めない。
                Assert.Equal(expected[i] is not ("<>" or "＜＞" or "｢｣"), k.Host.Events.Contains("down:25"), expected[i]);
            }
        }
        var unconverted = new CompositionTests.Keyboard { CursorIntoBrackets = true };
        unconverted.Type("<>\n");
        Assert.True(!unconverted.Host.Events.Contains("down:25"), "変換せずに確定した <> でもカーソルを戻さない");
        var single = new CompositionTests.Keyboard { CursorIntoBrackets = true };
        single.Type("[ \n");
        Assert.True(!single.Host.Events.Contains("down:25"), "片括弧ではカーソルを戻さない");
    }
}
