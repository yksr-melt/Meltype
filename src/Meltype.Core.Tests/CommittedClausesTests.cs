// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;
using Meltype.Learning;

namespace Meltype.Tests;

/// <summary>確定する入力の文節ごとの記録 (<see cref="CompositionCommit.CommittedClauses"/>)。#1570 で入力全体の F6 を学習するのに使う。</summary>
internal static class CommittedClausesTests
{
    [Test]
    public static void F6DuringConversion_KeepsTheConvertedClauses()
    {
        var (state, env) = Composing("kyouha", liveConversion: false);
        Press(state, env, VirtualKeys.Space, ' ');
        Assert.Equal("きょうは|今日は|今日は", Joined(CompositionCommit.CommittedClauses(Scene(state), env)), "変換中は変換の文節");
        Press(state, env, VirtualKeys.F6);
        Assert.Equal(false, CompositionController.IsConverting(state), "F6 で変換をやめる (今までどおり)");
        Assert.Equal("きょうは|今日は|きょうは", Joined(CompositionCommit.CommittedClauses(Scene(state), env)), "F6 の前の変換の答えに、ひらがなの形を当てる");
        Press(state, env, VirtualKeys.F7);
        Assert.Equal("きょうは|今日は|キョウハ", Joined(CompositionCommit.CommittedClauses(Scene(state), env)), "F7 でもその前の変換のまま");
    }

    [Test]
    public static void F6OnLiveConversion_RebuildsTheAutoClauses()
    {
        var (state, env) = Composing("kyouha", liveConversion: true);
        Press(state, env, VirtualKeys.F6);
        Assert.Equal("きょうは|今日は|きょうは", Joined(CompositionCommit.CommittedClauses(Scene(state), env)), "変換していなければ、自動の表示のライブ変換の文節");
    }

    [Test]
    public static void F6OnLiveConversion_KeepsTheEngineAnswerBeforeLearning()
    {
        // ライブ変換の文字は学習で選び直した後 (暑い) だが、変換エンジンの答え (熱い) は元のまま残す
        var history = new ConversionHistory(null);
        history.Remember("あつい", "暑い");
        var (state, env) = Composing("atsui", liveConversion: true, new AtsuiConverter(), new CompositionServices { History = history });
        Press(state, env, VirtualKeys.F6);
        Assert.Equal("あつい|熱い|あつい", Joined(CompositionCommit.CommittedClauses(Scene(state), env)));
    }

    private sealed class AtsuiConverter : TestConverter
    {
        public override string? Convert(string hiragana, string? before) => hiragana == "あつい" ? "熱い" : null;
    }

    [Test]
    public static void ChangedInput_DoesNotUseTheLeftConversion()
    {
        var (state, env) = Composing("kyouha", liveConversion: false);
        Press(state, env, VirtualKeys.Space, ' ');
        Press(state, env, VirtualKeys.F6);
        Press(state, env, VirtualKeys.Back);
        Assert.Equal("きょう|-|きょう", Joined(CompositionCommit.CommittedClauses(Scene(state), env)), "入力が変わったら、抜けた変換は使わない");
    }

    [Test]
    public static void Commit_ForgetsTheLeftConversion()
    {
        // 確定したら抜けた変換を消す。消さないと、同じ綴りを打ち直して F7 を押した入力の記録に、前に確定した入力の文節が使われる
        var (state, env) = Composing("kyouha", liveConversion: false);
        Press(state, env, VirtualKeys.Space, ' ');
        Press(state, env, VirtualKeys.F6);
        Press(state, env, VirtualKeys.Return, '\r');
        foreach (var c in "kyouha") Press(state, env, char.ToUpperInvariant(c), c);
        Press(state, env, VirtualKeys.F7);
        Assert.Equal("きょうは|-|キョウハ", Joined(CompositionCommit.CommittedClauses(Scene(state), env)));
    }

    [Test]
    public static void WholeKanaOfOneClause_IsLearnedInThatForm()
    {
        // #1570: 入力全体を F6・F7 でかなにして確定したら、文節が 1 つなら候補からかなを選んだのと同じく覚える
        foreach (var (key, kana) in new[] { (VirtualKeys.F6, "いって"), (VirtualKeys.F7, "イッテ") })
        {
            var history = new ConversionHistory(null);
            var (state, env) = Composing("itte", liveConversion: true, new ItteConverter(), new CompositionServices { History = history });
            Press(state, env, key);
            Press(state, env, VirtualKeys.Return, '\r');
            Assert.Equal(kana, history.Get("いって"), kana);
        }
    }

    [Test]
    public static void WholeKanaAfterSpaceConversion_IsLearned()
    {
        // Space で変換して見せた文節を F6 にしたときも、ライブ変換の ON・OFF によらず覚える (変換を抜けた LeftConversion の文節)
        foreach (var live in new[] { true, false })
        {
            var history = new ConversionHistory(null);
            var (state, env) = Composing("itte", liveConversion: live, new ItteConverter(), new CompositionServices { History = history });
            Press(state, env, VirtualKeys.Space, ' ');
            Press(state, env, VirtualKeys.F6);
            Press(state, env, VirtualKeys.Return, '\r');
            Assert.Equal("いって", history.Get("いって"), live ? "ライブ変換 ON" : "ライブ変換 OFF");
        }
    }

    [Test]
    public static void WholeKanaOfSeveralClauses_IsNotLearned()
    {
        // 記憶して言ってくれる を F6 にすると、直したい 言って のほかに 記憶 もかなになる。どの文節を直したのか分からないので覚えない
        var history = new ConversionHistory(null);
        var (state, env) = Composing("kiokusiteittekureru", liveConversion: true, new ItteConverter(), new CompositionServices { History = history });
        Press(state, env, VirtualKeys.F6);
        Press(state, env, VirtualKeys.Return, '\r');
        Assert.Equal(0, history.Count);
    }

    [Test]
    public static void WholeKanaOfShownKana_IsNotLearned()
    {
        // ライブ変換が OFF なら、変換して見せた文節が無い。見えていたかなのまま F6・F7 で確定したのは、漢字を直したのではない
        foreach (var (typed, key) in new[] { ("itte", VirtualKeys.F6), ("itte", VirtualKeys.F7), ("kan,kan", VirtualKeys.F7) })
        {
            var history = new ConversionHistory(null);
            var (state, env) = Composing(typed, liveConversion: false, new ItteConverter(), new CompositionServices { History = history });
            Press(state, env, key);
            Press(state, env, VirtualKeys.Return, '\r');
            Assert.Equal(0, history.Count, typed);
        }
    }

    [Test]
    public static void WholeKanaOfUnshownOrKanaAnswer_IsNotLearned()
    {
        // ライブ変換が ON でも、見えていた文字に漢字が無い (変換エンジンがかなを返した です) なら、漢字を直したのではない。
        // kan・kantan は かn・かんたn と見えていて、確定するときだけ末尾の n が ん になる。見えていない かん・かんたん の答えからは覚えない
        foreach (var (typed, key) in new[] { ("desu", VirtualKeys.F7), ("kan", VirtualKeys.F6), ("kantan", VirtualKeys.F6) })
        {
            var history = new ConversionHistory(null);
            var (state, env) = Composing(typed, liveConversion: true, new ItteConverter(), new CompositionServices { History = history });
            Press(state, env, key);
            Press(state, env, VirtualKeys.Return, '\r');
            Assert.Equal(0, history.Count, typed);
        }
    }

    private sealed class ItteConverter : TestConverter
    {
        public override IReadOnlyList<Engine.ConversionClause>? ConvertClauses(string hiragana, string? before) => hiragana switch
        {
            "いって" => [new("いって", "言って")],
            "です" => [new("です", "です")],
            "かん" => [new("かん", "缶")],
            "かんたん" => [new("かんたん", "簡単")],
            "きおくしていってくれる" => [new("きおくして", "記憶して"), new("いってくれる", "言ってくれる")],
            _ => null,
        };
    }

    private static (CompositionState State, CompositionEnv Env) Composing(string typed, bool liveConversion, Engine.IKanjiConverter? converter = null, CompositionServices? services = null)
    {
        var state = new CompositionState();
        var env = new CompositionEnv(CompositionTests.Detector, converter ?? new CompositionTests.FakeConverter(), services ?? new CompositionServices(), new Settings { LiveConversion = liveConversion })
        {
            Context = () => TextContext.None,
            LiveCache = state.LiveCache,
        };
        foreach (var c in typed) Press(state, env, char.ToUpperInvariant(c), c);
        return (state, env);
    }

    private static long _now = 1000;

    private static void Press(CompositionState state, CompositionEnv env, int vk, char? ch = null) =>
        CompositionController.Press(state, env, new KeyPress(vk, vk, ch, false, false, false, false, _now += 100));

    private static CompositionScene.WithInput Scene(CompositionState state) => (CompositionScene.WithInput)state.Scene;

    private static string Joined(IReadOnlyList<CommittedClause> clauses) => string.Join(" / ", clauses.Select(c => $"{c.Reading}|{c.Engine ?? "-"}|{c.Text ?? "-"}"));
}
