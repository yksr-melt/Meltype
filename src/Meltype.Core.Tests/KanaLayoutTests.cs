// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;
using static Meltype.Tests.CompositionTests;

namespace Meltype.Tests;

/// <summary>JIS かな以外のかな配列と SKY 配列 (issue #1415)。</summary>
internal static class KanaLayoutTests
{
    private const int NonConvert = 0x1D, Convert = 0x1C;

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
    public static void KanaLayout_EmptyKeysTypeNothing()
    {
        // 配列の表で空いているキーは何も入れない (月配列の 1 と ] で、JIS かなの ぬ む を入れない)
        var k = Layout(KanaLayout.Tsuki);
        k.TypeKanaKeys("w1");
        k.Press(0xDD); // ]
        Assert.Equal("と", k.Showing);
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
    [Test]
    public static void KanaLayout_Nicola_ThumbHeldBeforeKey()
    {
        // NICOLA: 変換 (右親指) を押したまま W = が。無変換 (左親指) を押したまま S = あ
        var k = Layout(KanaLayout.Nicola);
        k.TypeKanaKeys("w");
        k.Key(Convert);
        k.TypeKanaKeys("w");
        k.Key(Convert, up: true);
        k.Key(NonConvert);
        k.TypeKanaKeys("s");
        k.Key(NonConvert, up: true);
        Assert.Equal("かがあ", k.Showing);
    }

    [Test]
    public static void KanaLayout_Nicola_ThumbRightAfterKeyIsChord()
    {
        // 文字キーを押している間に押した親指キーも同時打鍵 (か → が に打ち直す)
        var k = Layout(KanaLayout.Nicola);
        k.TypeKanaKeys("k");
        k.Key(0x57);
        k.Press(Convert);
        k.Key(0x57, up: true);
        Assert.Equal("きが", k.Showing);
    }

    [Test]
    public static void KanaLayout_Nicola_LateThumbTapStillConverts()
    {
        // 時間を空けて親指キーだけを押して離すと、ふつうの変換 (打ち直さない)
        var k = Layout(KanaLayout.Nicola);
        k.TypeKanaKeys("kw");
        k.Wait(500);
        k.Press(Convert);
        Assert.True(k.Host.View is { Converting: true }, "変換キーとして変換を始める");
        Assert.True(!k.Host.View!.Clauses!.Any(c => c.Contains('が')), "か を が にしない");
    }

    [Test]
    public static void KanaLayout_Nicola_ThumbAfterReleaseIsNotChord()
    {
        // 文字キーを離してから押した親指キーは、すぐでも同時打鍵にしない (か のまま。親指キーは押したまま次の文字に効く)
        var k = Layout(KanaLayout.Nicola);
        k.TypeKanaKeys("w");
        k.Key(Convert);
        k.TypeKanaKeys("i");
        k.Key(Convert, up: true);
        Assert.Equal("かる", k.Showing);
    }

    [Test]
    public static void KanaLayout_Nicola_SpaceStillConverts()
    {
        var k = Layout(KanaLayout.Nicola);
        k.TypeKanaKeys("ki");
        k.Press(0x20);
        Assert.True(k.Host.View is { Converting: true }, "Space はふつうの変換");
    }

    [Test]
    public static void KanaLayout_NakayubiNicola_MiddleFingerShift()
    {
        // 中指NICOLA: K を押したまま A (左手) = を (左シフト)、D を押したまま J (右手) = お (右シフト)
        var k = Layout(KanaLayout.NakayubiNicola);
        k.Key(0x4B);
        k.TypeKanaKeys("a");
        k.Key(0x4B, up: true);
        k.Key(0x44);
        k.TypeKanaKeys("j");
        k.Key(0x44, up: true);
        Assert.Equal("をお", k.Showing);
    }

    [Test]
    public static void KanaLayout_NakayubiNicola_SameHandIsNotShift()
    {
        // K を押したままでも、右手のキーはシフトしない (き の)。離してから打った左手のキーもシフトしない
        var k = Layout(KanaLayout.NakayubiNicola);
        k.Key(0x4B);
        k.TypeKanaKeys("l");
        k.Key(0x4B, up: true);
        k.TypeKanaKeys("ka");
        Assert.Equal("きいきう", k.Showing);
    }

    [Test]
    public static void KanaLayout_Tron_ThumbShift()
    {
        // TRON: Q = ら、右親指 + Q = び、左親指 + F = よ
        var k = Layout(KanaLayout.Tron);
        k.TypeKanaKeys("q");
        k.Key(Convert);
        k.TypeKanaKeys("q");
        k.Key(Convert, up: true);
        k.Key(NonConvert);
        k.TypeKanaKeys("f");
        k.Key(NonConvert, up: true);
        Assert.Equal("らびよ", k.Showing);
    }

    [Test]
    public static void KanaLayout_DakutenKeyCombines()
    {
        // TRON の左親指 + H は濁点。直前の かな に付く (か + ゛ = が)
        var k = Layout(KanaLayout.Tron);
        k.TypeKanaKeys("d");
        k.Key(NonConvert);
        k.TypeKanaKeys("h");
        k.Key(NonConvert, up: true);
        Assert.Equal("が", k.Showing);
    }

    [Test]
    public static void SkyLayout_ConsonantVowelKeys()
    {
        // SKY: F J = か、A J = な、S L = ち、F G K I = きょう (K = o、I = ou)
        var k = new Keyboard { RomajiLayout = RomajiLayout.Sky };
        k.Type("fjajsl");
        Assert.Equal("かなち", k.Showing);
        var j = new Keyboard { RomajiLayout = RomajiLayout.Sky };
        j.Type("fgi");
        Assert.Equal("きょう", j.Showing);
    }

    [Test]
    public static void SkyLayout_NasalKeysBeforeVowel()
    {
        // UN (N のキー) の後に母音のキーが来ても う + な にしない (ほんあ ではなく うんあ)
        var k = new Keyboard { RomajiLayout = RomajiLayout.Sky };
        k.Type("nj");
        Assert.Equal("うんあ", k.Showing);
    }

    [Test]
    public static void SkyLayout_WordsLookingEnglishStayJapanese()
    {
        // 打ったキーの英字 (h j → u a) は英単語として見ない
        var k = new Keyboard { RomajiLayout = RomajiLayout.Sky };
        k.Type("sh");
        Assert.Equal("つ", k.Showing);
    }
    [Test]
    public static void KanaLayout_Nicola_ThumbAutoRepeatKeepsChord()
    {
        // 親指キーを押し続けたときのリピートで、文字に使ったことを忘れない (離しても変換しない)
        var k = Layout(KanaLayout.Nicola);
        k.Key(NonConvert);
        k.TypeKanaKeys("w");
        k.Key(NonConvert);
        k.Key(NonConvert);
        k.Key(NonConvert, up: true);
        Assert.Equal("え", k.Showing);
        Assert.True(k.Host.View?.Candidates is not { Count: > 1 }, "変換していない");
    }

    [Test]
    public static void KanaLayout_Nicola_RightThumbAtStartIsShift()
    {
        // 文頭の 右親指 (変換) + W は、右シフト面の「が」 (再変換にしない)
        var k = Layout(KanaLayout.Nicola);
        k.Key(Convert);
        k.TypeKanaKeys("w");
        k.Key(Convert, up: true);
        Assert.Equal("が", k.Showing);
    }

    [Test]
    public static void KanaLayout_NakayubiNicola_RolloverIsNotShift()
    {
        // D を押したまま K を押しても、D を先に離したら、速く打って重なっただけ (て き のまま)
        var k = Layout(KanaLayout.NakayubiNicola);
        k.Key(0x44);
        k.Key(0x4B);
        k.Key(0x44, up: true);
        k.Key(0x4B, up: true);
        Assert.Equal("てき", k.Showing);
    }
    [Test]
    public static void KanaLayout_Nicola_ThumbTapAtStartKeepsOriginalAndDoesNotStick()
    {
        // 文頭で 無変換 だけを押して離したら、次の S はシフト面にしない (し。「あ」にしない)
        var k = Layout(KanaLayout.Nicola);
        k.Key(NonConvert);
        k.Key(NonConvert, up: true);
        k.TypeKanaKeys("s");
        Assert.Equal("し", k.Showing);
        Assert.True(k.Host.Events.Contains("passed:1D") || k.Host.Events.Any(e => e.Contains("1D")), "無変換はアプリに渡す: " + string.Join(",", k.Host.Events));
    }

    [Test]
    public static void KanaLayout_Nicola_ThumbDuringConversionIsShift()
    {
        // 変換中に 変換 を押したまま W は、右シフト面の「が」 (次の候補にしない)
        var k = Layout(KanaLayout.Nicola);
        k.TypeKanaKeys("kw");
        k.Press(VirtualKeys.Space);
        k.Key(Convert);
        k.TypeKanaKeys("w");
        k.Key(Convert, up: true);
        Assert.True(k.Showing?.EndsWith('が') == true, k.Showing ?? "");
    }

    [Test]
    public static void KanaLayout_Nicola_RightThumbTapAtStartDoesNotStick()
    {
        // 文頭で変換キーだけを押して離した後は、次の W が通常面の「か」になる。
        var k = Layout(KanaLayout.Nicola);
        k.Press(Convert);
        k.TypeKanaKeys("w");
        Assert.Equal("か", k.Showing);
    }

    [Test]
    public static void KanaLayout_Nicola_ThumbTapDuringConversionKeepsOriginalAction()
    {
        // 変換中の変換キー単独タップは候補送り。次の文字はシフトされない。
        var k = Layout(KanaLayout.Nicola);
        k.TypeKanaKeys("kw");
        k.Press(VirtualKeys.Space);
        Assert.True(k.Host.View is { Converting: true }, "Space で変換中になる");
        k.Press(Convert);
        Assert.True(k.Host.View is { Converting: true }, "単独タップでは変換を確定しない");
        k.TypeKanaKeys("w");
        Assert.True(k.Showing?.EndsWith('か') == true, k.Showing ?? "");
    }
}
