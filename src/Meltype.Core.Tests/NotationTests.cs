// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;
using Meltype.Notation;

namespace Meltype.Tests;

/// <summary>見せる表記の規則 (<see cref="NotationRules"/>) の表。</summary>
internal static class NotationTests
{
    private static string? ProperNoun(string word) => word switch { "github" => "GitHub", "iphone" => "iPhone", _ => null };

    private static TextSpan T(string text) => new(SpanKind.Typed, text);

    private static TextSpan E(string text) => new(SpanKind.English, text);

    private static TextSpan J(string text) => new(SpanKind.Japanese, text);

    private static TextSpan P(string text) => new(SpanKind.Protected, text);

    private static ComposedText Text(params TextSpan[] spans) => ComposedText.Concat(spans.Select(s => ComposedText.Of(s.Kind, s.Text)));

    [Test]
    public static void ProperNouns_AreRecasedOnlyInTypedSpans()
    {
        foreach (var (typed, expected) in new (TextSpan[], TextSpan[])[]
                 {
                     ([T("github")], [E("GitHub")]),
                     ([T("hello")], [E("hello")]),
                     ([E("github")], [E("github")]), // F10・候補で小文字を選んだ
                     ([J("github"), P("github")], [J("github"), P("github")]),
                     ([J("kyouha"), T("github"), J("ni")], [J("kyouha"), E("GitHub"), J("ni")]),
                     ([T("github"), T("."), T("com")], [E("github"), E("."), E("com")]), // ドメイン
                     ([T("google"), T("."), T("github")], [E("google"), E("."), E("github")]),
                     ([T("github"), T("."), J("desu")], [E("github"), E("."), J("desu")]), // 打った文字で見る
                     ([T("github"), T(".")], [E("GitHub"), E(".")]), // 文末
                     ([T("github"), T("."), T(".")], [E("GitHub"), E("."), E(".")]),
                     ([T("."), T("github")], [E("."), E("GitHub")]),
                     ([T("github"), T(","), T("iphone")], [E("GitHub"), E(","), E("iPhone")]),
                 })
        {
            Assert.Equal(string.Join(" ", expected), string.Join(" ", NotationRules.Typed(typed, ProperNoun)));
        }
        Assert.Equal("GitHub", NotationRules.Word("github", ProperNoun));
        Assert.Equal("hello", NotationRules.Word("hello", ProperNoun));
    }

    [Test]
    public static void Stars_AreHalfWidth_WhenTypedTwice()
    {
        foreach (var (text, typed, expected) in new[]
                 {
                     ("＊あ＊", "*a*", "*あ*"), ("＊＊太字＊＊", "**futoji**", "**太字**"), ("＊", "*", "＊"), ("あ＊", "a*", "あ＊"), ("＊＊", "", "＊＊"),
                 })
        {
            Assert.Equal(expected, NotationRules.Show(ComposedText.Kana(text), typed).Text, text);
        }
    }

    [Test]
    public static void Spaces_FollowTheSettings()
    {
        foreach (var (text, before, after, english, numbers, expected) in new (string, string?, string?, bool, bool, string)[]
                 {
                     ("今日はGitHubに3回", null, null, false, false, "今日はGitHubに3回"),
                     ("今日はGitHubに3回", null, null, true, false, "今日は GitHub に3回"),
                     ("今日はGitHubに3回", null, null, false, true, "今日はGitHubに 3 回"),
                     ("今日はGitHubに3回", null, null, true, true, "今日は GitHub に 3 回"),
                     ("に", "GitHub", null, true, false, " に"),
                     ("GitHub", "今日は", "に", true, false, " GitHub "),
                     ("、", "GitHub", null, true, true, "、"),
                 })
        {
            var settings = new Settings { SpaceAroundEnglish = english, SpaceAroundNumbers = numbers };
            Assert.Equal(expected, NotationRules.Commit(ComposedText.Of(SpanKind.Japanese, text), before, after, settings).Text, $"{before}|{text}|{after}");
        }
    }

    [Test]
    public static void Spaces_KeepTheKindsOfSpans()
    {
        var spaced = NotationRules.Commit(Text(J("今日は"), E("GitHub")) + ComposedText.Kana("、"), null, null, new Settings { SpaceAroundEnglish = true });
        Assert.Equal("今日は GitHub，", NotationRules.Write(spaced, PunctuationStyle.FullWidthCommaPeriod));
    }

    [Test]
    public static void Punctuation_IsWrittenOnlyInTypedMarks_AndReadBack()
    {
        foreach (var (style, written) in new[]
                 {
                     (PunctuationStyle.Japanese, "モーニング娘。、。"), (PunctuationStyle.FullWidthCommaPeriod, "モーニング娘。，．"),
                     (PunctuationStyle.FullWidthCommaKuten, "モーニング娘。，。"), (PunctuationStyle.ToutenFullWidthPeriod, "モーニング娘。、．"),
                 })
        {
            Assert.Equal(written, NotationRules.Write(Text(J("モーニング娘。")) + ComposedText.Kana("、。"), style), style.ToString());
            Assert.Equal("モーニング娘。、。", NotationRules.Read(written, style), style.ToString());
        }
    }
}
