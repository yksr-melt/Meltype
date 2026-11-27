// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// A-PROP-01..04: 保護範囲の境界が常に正しい (非重複・サロゲートを割らない) ことと、
/// 編集して作った状態が、同じ原文を最初から打ち直した状態と一致すること (incremental == reference)。
/// </summary>
internal static class ProtectionPropertyTests
{
    private static readonly CompositionEnv Env = TestSupport.Env(CompositionTests.Detector);

    private static readonly string[] Alphabet =
    [
        "@", "k", "u", "r", "a", "i", "d", "o", "h", "t", "p", "s", ":", "/", ".", "-", "_", "+",
        "#", "?", "=", "&", "%", "\\", "C", "U", "1", "9", "~", "n",
    ];

    [Test]
    public static void Protection_SpansAreWellFormed()
    {
        var random = new Random(20261005);
        for (var sequence = 0; sequence < 2000; sequence++)
        {
            CompositionTextState text = new CompositionTextState();
            var steps = random.Next(1, 24);
            for (var step = 0; step < steps; step++)
            {
                if (random.Next(0, 4) == 0 && CompositionText.Raw(CompositionText.Read(text, Env)).Length > 0) text = CompositionText.RemoveLast(text, Env);
                else text = CompositionText.Append(text, Alphabet[random.Next(Alphabet.Length)][0]);

                var raw = CompositionText.Raw(CompositionText.Read(text, Env));
                AssertWellFormed(raw, CompositionText.ProtectedSpans(text));
                if (CompositionText.IsProtectedRaw(CompositionText.Read(text, Env)))
                    Assert.Equal(raw, CompositionText.Display(CompositionText.Read(text, Env), Env, final: true), "保護範囲は原文のまま表示する");
            }
        }
    }

    private static void AssertWellFormed(string raw, IReadOnlyList<ProtectedSpan> spans)
    {
        var previousEnd = 0;
        foreach (var span in spans)
        {
            Assert.True(span.Start >= 0 && span.Length > 0 && span.End <= raw.Length, "保護区間が原文の範囲内");
            Assert.True(previousEnd <= span.Start, $"保護区間は順序どおりで重複しない: {raw}");
            // 開始・終了の両境界が UTF-16 サロゲートペアの途中でないこと。
            foreach (var boundary in new[] { span.Start, span.End })
            {
                Assert.True(boundary == 0 || boundary == raw.Length ||
                    !(char.IsHighSurrogate(raw[boundary - 1]) && char.IsLowSurrogate(raw[boundary])), "サロゲートを割らない");
            }
            previousEnd = span.End;
        }
    }

    [Test]
    public static void Protection_ScannerBoundariesIncludeUnicodeAndAdjacentTokens()
    {
        foreach (var raw in new[]
                 {
                     "@a@b", "a@b@c", "/a@b", "@a.b@c", "@user taro@example.com", "https://a/b@c",
                     "😊@user𠮷 taro@example.com", "e\u0301 @user", "@user😊/tmp/main.ts", "𠮷https://example.com😊",
                 })
        {
            AssertWellFormed(raw, ProtectedSpanScanner.Scan(raw));
        }
    }

    [Test]
    public static void Protection_IncrementalEqualsReference()
    {
        var random = new Random(20261005);
        for (var sequence = 0; sequence < 2000; sequence++)
        {
            // 編集 (打つ・保護範囲内を消す) で作った状態
            CompositionTextState incremental = new CompositionTextState();
            var steps = random.Next(1, 20);
            for (var step = 0; step < steps; step++)
            {
                if (random.Next(0, 4) == 0 && CompositionText.Raw(CompositionText.Read(incremental, Env)).Length > 0) incremental = CompositionText.RemoveLast(incremental, Env);
                else incremental = CompositionText.Append(incremental, Alphabet[random.Next(Alphabet.Length)][0]);
            }
            var raw = CompositionText.Raw(CompositionText.Read(incremental, Env));
            // 原文から一意に決まるのは保護範囲だけ (通常の日本語は打鍵の履歴で状態が変わるため対象外)。
            if (raw.Length == 0 || !CompositionText.IsProtectedRaw(CompositionText.Read(incremental, Env))) continue;

            // 参照: 同じ原文を最初から 1 文字ずつ打ち直した状態
            CompositionTextState reference = new CompositionTextState();
            foreach (var c in raw) reference = CompositionText.Append(reference, c);

            Assert.Equal(CompositionText.Raw(CompositionText.Read(reference, Env)), CompositionText.Raw(CompositionText.Read(incremental, Env)), "原文が一致する");
            Assert.Equal(CompositionText.Display(CompositionText.Read(reference, Env), Env, final: true), CompositionText.Display(CompositionText.Read(incremental, Env), Env, final: true), "確定表示が一致する");
            Assert.True(CompositionText.ProtectedSpans(incremental).SequenceEqual(CompositionText.ProtectedSpans(reference)), "保護区間が一致する");
        }
    }

    [Test]
    public static void Protection_NormalJapaneseBackspaceKeepsOneMora()
    {
        // 普通の日本語は、保護範囲内の Backspace と違って 1 音ずつ消える。
        CompositionTextState text = new CompositionTextState();
        foreach (var c in "kyou") text = CompositionText.Append(text, c);
        var before = CompositionText.Display(CompositionText.Read(text, Env), Env, final: false);
        text = CompositionText.RemoveLast(text, Env);
        var after = CompositionText.Display(CompositionText.Read(text, Env), Env, final: false);
        Assert.True(before != after, "1 音消える");
        Assert.True(after.Length < before.Length, "短くなる");
    }
}
