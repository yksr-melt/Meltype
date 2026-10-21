// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 kikutadev

using System.Text.Json;

namespace Meltype.Tests;

internal static class MixedCorpusTests
{
    [Test]
    public static void ReportsRowsAndGroupScoresWithoutTreatingMismatchesAsTestFailures()
    {
        var input = Path.GetTempFileName();
        var output = Path.GetTempFileName();
        try
        {
            File.WriteAllText(input, """
                # diagnostic expectations, not the current engine's output
                japanese	kyouhaamedesu	きょうはあめです
                mixed_front	Excelwotsukau	Excelをつかう
                mixed_middle	kinouhaZoomdekaigi	きのうはZoomでかいぎ
                japanese	ashitahaamedesu	意図的に不一致
                """);
            Checks.MixedCorpus(input, output);
            using var json = JsonDocument.Parse(File.ReadAllText(output));
            var root = json.RootElement;
            Assert.Equal(4, root.GetProperty("total").GetInt32());
            Assert.Equal(3, root.GetProperty("groups").GetArrayLength());
            Assert.Equal(4, root.GetProperty("cases").GetArrayLength());
            Assert.True(root.GetProperty("pass").GetInt32() < 4, "不一致も記録し、例外にしない");
            Assert.True(root.GetProperty("cases").EnumerateArray().Any(c =>
                !c.GetProperty("ok").GetBoolean()), "差分が消えないこと");
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Test]
    public static void RejectsMalformedInputRatherThanSkippingRows()
    {
        var input = Path.GetTempFileName();
        var output = Path.GetTempFileName();
        try
        {
            File.WriteAllText(input, "mixed\tkeys-without-expected\n");
            var error = false;
            try { Checks.MixedCorpus(input, output); }
            catch (FormatException) { error = true; }
            Assert.True(error, "不正なTSVは黙って捨てずに報告する");
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }
}
