// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;
using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>JIS かな以外のかな配列 (月配列・花配列。issue #1415)。</summary>
internal static class KanaLayoutTests
{
    private static Keyboard Layout(KanaLayout layout) => new() { Kana = true, KanaLayout = layout };

    [Test]
    public static void KanaLayout_Tsuki_PrefixShift()
    {
        // 月配列: W = と。D・K は 2 段目、S・L は 3 段目の前置シフト。D の次の J = お、K の次の E = ね、L の次の E = じ
        var k = Layout(KanaLayout.Tsuki);
        k.TypeKanaKeys("wdjkele");
        Assert.Equal("とおねじ", k.Showing);
    }

    [Test]
    public static void KanaLayout_Tsuki_PrefixAppliesOnlyToNextKey()
    {
        // 前置シフトは次の 1 打鍵だけ (D J J → お う)
        var k = Layout(KanaLayout.Tsuki);
        k.TypeKanaKeys("djj");
        Assert.Equal("おう", k.Showing);
    }

    [Test]
    public static void KanaLayout_Hana_PrefixShift()
    {
        // 花配列: A = す、D の次の A = ぁ、K の次の C = あ
        var k = Layout(KanaLayout.Hana);
        k.TypeKanaKeys("adakc");
        Assert.Equal("すぁあ", k.Showing);
    }

    [Test]
    public static void KanaLayout_PrefixIsForgottenByOtherKeys()
    {
        // 前置シフトの後の BackSpace は、シフトのキーを打ち消す (W D BackSpace J → と う。「お」にしない・と も消さない)
        var k = Layout(KanaLayout.Tsuki);
        k.TypeKanaKeys("wd");
        k.Press(VirtualKeys.Back);
        k.TypeKanaKeys("j");
        Assert.Equal("とう", k.Showing);
        // Esc で取り消した後も、前のシフトは残らない
        var esc = Layout(KanaLayout.Tsuki);
        esc.TypeKanaKeys("wd");
        esc.Press(VirtualKeys.Escape);
        esc.TypeKanaKeys("j");
        Assert.Equal("う", esc.Showing);
    }

    [Test]
    public static void KanaLayout_Hana_DakutenKeyCombines()
    {
        // 花配列の L は濁点。直前の かな に付く (S = か + ゛ = が)
        var k = Layout(KanaLayout.Hana);
        k.TypeKanaKeys("sl");
        Assert.Equal("が", k.Showing);
    }

    [Test]
    public static void KanaLayout_JisIsUnchanged()
    {
        var k = Layout(KanaLayout.Jis);
        k.TypeKanaKeys("tu");
        Assert.Equal("かな", k.Showing);
    }

    [Test]
    public static void KanaLayout_IgnoredForRomajiInput()
    {
        // ローマ字入力のときは、かな配列の設定を使わない
        var k = new Keyboard { KanaLayout = KanaLayout.Tsuki };
        k.Type("kana");
        Assert.Equal("かな", k.Showing);
    }
}
