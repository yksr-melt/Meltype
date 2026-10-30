// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>入力に関する報告のうち、共有Coreだけで検査できる回帰条件を固定する。</summary>
/// <remarks>
/// Keyboardはテスト用ホストを使う。OSのキー配送・実IME・実アプリへの入力は保証しない。
/// ケースの背景は各テストに記し、操作と期待値はテスト本体を正本とする。
/// </remarks>
internal static class InputRegressionTests
{
    [Test]
    public static void EnterCommit_AllowsTheNextInput()
    {
        // Issue #255: 確定後に 2 回目の入力が止まる報告。変換ボックスでは次の入力も確定できる。
        var k = new CompositionTests.Keyboard();
        k.Type("aiueo\n");
        Assert.True(k.Showing is null, "1回目の確定後は未確定文字がない");
        k.Type("aiueo");
        Assert.Equal("あいうえお", k.Showing, "2回目も通常の入力になる");
        k.Press(VirtualKeys.Return);
        Assert.Equal("あいうえおあいうえお", k.Host.Document);
        Assert.Equal(2, k.Host.Output.Count, "各入力を1回ずつ確定する");
        Assert.True(k.Showing is null, "2回目も未確定文字を残さない");
    }

    [Test]
    public static void LongComposition_CommitsAndAllowsTheNextInput()
    {
        // Issue #221: 100 文字を超える入力の報告。長い未確定文字も保持し、確定後に続けて入力できる。
        var k = new CompositionTests.Keyboard();
        var input = string.Concat(Enumerable.Repeat("aiueo", 30));
        var expected = string.Concat(Enumerable.Repeat("あいうえお", 30));
        k.Type(input);
        Assert.Equal(expected, k.Showing, "150文字を未確定のまま保持する");
        k.Press(VirtualKeys.Return);
        Assert.Equal(expected, k.Host.Document);
        Assert.True(k.Showing is null, "長い入力を確定する");
        k.Type("aiueo\n");
        Assert.Equal(expected + "あいうえお", k.Host.Document, "確定後も入力できる");
    }

    [Test]
    public static void UserDictionary_AllArrowReadingsRoundTrip()
    {
        // Issue #54: zh・zj・zk・zl に矢印を登録する。ユーザー辞書を保存して読み直しても候補に出る。
        var path = Path.Combine(Path.GetTempPath(), "meltype-ci-arrows-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var pairs = new[] { ("zh", "←"), ("zj", "↓"), ("zk", "↑"), ("zl", "→") };
            // 利用者の辞書と組込辞書から隔離し、保存・再読込みした4件だけで候補を検査する。
            var dictionary = new UserDictionary(path, builtIn: false);
            foreach (var (reading, word) in pairs)
                Assert.True(dictionary.Add(reading, word) is null, "登録: " + reading);
            var restored = new UserDictionary(path, builtIn: false);
            Assert.Equal(4, restored.Count, "組込辞書を含めず4件を再読込する");
            foreach (var (reading, word) in pairs)
                Assert.Equal(word, restored.Lookup(reading).Single(), reading + "の保存内容を読み直す");

            // z系は組込ショートカットでも同じ矢印になる。独自のASCII読みを追加し、
            // 読込みだけでなく辞書候補の利用が壊れた場合も検出する。
            const string controlReading = "qx";
            const string controlWord = "辞書だけの矢印←";
            Assert.True(restored.Add(controlReading, controlWord) is null, "対照用の読みを登録する");
            restored = new UserDictionary(path, builtIn: false);
            Assert.Equal(controlWord, restored.Lookup(controlReading).Single());
            foreach (var live in new[] { false, true })
            foreach (var (reading, word) in pairs.Append((controlReading, controlWord)))
            {
                var k = new CompositionTests.Keyboard(live: live, userDictionary: restored);
                k.Type(reading + " ");
                Assert.Equal(word, k.Host.View!.Clauses![0], reading + "の候補");
                k.Press(VirtualKeys.Return);
                Assert.Equal(word, k.Host.Document, reading + "の確定");
            }
        }
        finally { File.Delete(path); }
    }

    [Test]
    public static void SnippetAfterCaretMove_UsesTheNewLineContext()
    {
        // Issue #463 / PR #586: キャレットを移動したら、移動先の行頭でスニペットを展開する。単語の途中では展開しない。
        var store = new SnippetStore(null);
        store.Set("srch", "検索する");
        var trigger = new SnippetTrigger();
        trigger.Append("previous text");
        // 実際のキャレット移動の通知は扱わず、移動通知後にCoreへ渡される状態を作る。
        trigger.Lose();
        Assert.True(trigger.PassesThrough(';', ""), "移動先が行頭ならトリガーを受け取る");
        trigger.Append(";srch");
        var expansion = trigger.Expand(store);
        Assert.Equal<int?>(5, expansion?.Delete, "展開時の削除数");
        Assert.Equal("検索する", expansion?.Text, "展開文字列");
        Assert.Equal(";srch", expansion?.Typed, "展開前の入力");
        trigger.Lose();
        Assert.True(!trigger.PassesThrough(';', "text"), "移動先が単語の途中なら展開しない");
    }
}
