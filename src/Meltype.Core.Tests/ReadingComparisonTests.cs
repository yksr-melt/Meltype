// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 souta

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// 日英の区切りを点数で決める β版 (<see cref="ReadingComparison"/>) のテスト。
/// 既定は OFF なので、テストの中だけで有効にして、OFF の結果を壊していないことも見る。
/// </summary>
internal static class ReadingComparisonTests
{
    /// <summary>環境変数 MELTYPE_READING=1 で β 版を強制しているか (そのときは「切ったとき」を作れない)。</summary>
    private static bool BetaForcedByEnvironment =>
        Environment.GetEnvironmentVariable("MELTYPE_READING") == "1";

    private static void WithBeta(bool enabled, Action action)
    {
        var previous = CompositionTests.Detector.UseReadingComparison;
        CompositionTests.Detector.UseReadingComparison = () => enabled;
        try
        {
            action();
        }
        finally
        {
            CompositionTests.Detector.UseReadingComparison = previous;
        }
    }

    [Test]
    public static void Beta_FixesWordsWhoseSplitCrossesAUnit()
    {
        // printenv は print が te の途中で終わるため、今までの決まりでは 日本語の読みに英字が残る。
        WithBeta(true, () =>
        {
            Assert.Equal("printenv", Committed("printenv"), "printenv が英字のまま");
            Assert.Equal("mainにpushする", Committed("mainnipushsuru"), "main に push する の区切り");
        });
    }

    [Test]
    public static void Beta_Off_KeepsTheExistingDecision()
    {
        // 環境変数で有効にしたままテストを流したときは、「切ったとき」を作れないので飛ばす
        if (BetaForcedByEnvironment) return;
        WithBeta(false, () =>
        {
            Assert.Equal("pりんてんv", Committed("printenv"), "β版を切れば今までどおり");
            Assert.Equal("まいんいpushする", Committed("mainnipushsuru"), "β版を切れば今までどおり");
        });
    }

    [Test]
    public static void ExplicitJapaneseLearning_IsNotOverridden()
    {
        // ユーザーが明示的に「日本語」と直した語 (printenv → ぷりんてん…) は、点数でも英語にしない。
        var memory = new LanguageMemory(null);
        memory.Remember("printenv", english: false, explicitChoice: true);
        var previous = CompositionTests.Detector.Memory;
        CompositionTests.Detector.Memory = memory;
        try
        {
            WithBeta(true, () =>
            {
                var typed = Committed("printenv");
                Assert.True(typed != "printenv", "明示的に日本語と覚えた語を英語にしない: " + typed);
                Assert.True(typed.Contains("りんてん", StringComparison.Ordinal), "日本語の読みのまま: " + typed);
            });
        }
        finally
        {
            CompositionTests.Detector.Memory = previous;
        }
    }

    [Test]
    public static void TogglingTheSetting_DoesNotReuseStaleSegments()
    {
        // 環境変数で有効にしたままテストを流したときは、OFF から始められないので飛ばす
        if (BetaForcedByEnvironment) return;
        // 同じ CompositionText のまま設定を切り替えたときに、前の区間の控えを返さない。
        var text = new CompositionText(CompositionTests.Detector);
        foreach (var c in "printenv") text.Append(c);
        var before = string.Join("|", text.Segments(final: true).Select(s => (s.IsEnglish ? "EN:" : "JA:") + s.Raw));
        // テストの前の値に必ず戻す (共有の Detector を他のテストと取り合わない)
        var previous = CompositionTests.Detector.UseReadingComparison;
        try
        {
            CompositionTests.Detector.UseReadingComparison = () => true;
            var after = string.Join("|", text.Segments(final: true).Select(s => (s.IsEnglish ? "EN:" : "JA:") + s.Raw));
            Assert.True(before != after, "設定を変えたら区間を計算し直す: " + before + " / " + after);
            Assert.Equal("EN:printenv", after);
        }
        finally
        {
            CompositionTests.Detector.UseReadingComparison = previous;
        }
    }

    /// <summary>打って Enter で確定した文字列。</summary>
    private static string Committed(string keys)
    {
        var keyboard = new CompositionTests.Keyboard();
        keyboard.Type(keys + "\n");
        return keyboard.Host.Document;
    }
}
