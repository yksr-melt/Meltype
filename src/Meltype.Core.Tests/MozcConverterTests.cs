// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 asutarisucu

using System.Diagnostics;
using System.Text;
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>Mozc の変換ヘルパーの起動・起動し直し (本物の Mozc の代わりに、このテストの実行ファイルを偽のヘルパーとして動かす)。</summary>
internal static class MozcConverterTests
{
    /// <summary>偽のヘルパーの目印 (学習データの保存先のフォルダー名。ヘルパーにはこれが 1 つ目の引数で渡る)。</summary>
    public const string FakeHelperProfile = "meltype-fake-mozc-helper";

    /// <summary>偽のヘルパー: 1 秒たってから READY を返し、読みをそのまま候補にして返す。読みが おちる なら落ちる。</summary>
    public static int RunFakeHelper()
    {
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        Thread.Sleep(1000);
        output.WriteLine("READY");
        while (input.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            switch (fields[0])
            {
                case "C" when fields[2] == "おちる":
                    return 1;
                // 2 つの文節に分けて、文節ごとの候補も返す (Zenzai の結果を Mozc の文節に当てはめるテスト: ZenzaiConverterTests)
                case "C" when fields[2] == "きしゃがきた":
                    output.WriteLine("きしゃが\x1f記者が\x1f汽車が\x1f帰社が" + '\x1e' + "きた\x1f来た\x1f着た\x1f北");
                    break;
                // 助詞だけの文節 (に) の候補に数字がある (Zenzai の 荷物を2階に運ぶ を当てはめると、に を 2 にしてしまう)
                case "C" when fields[2] == "にもつをにかいにはこぶ":
                    // (\x1f の後ろに 2 を続けて書くと \x1f2 という 1 文字になるので、区切りは別の文字として足す)
                    output.WriteLine("にもつを\x1f荷物を" + '\x1e' + "に\x1fに" + '\x1f' + "2" + '\x1f' + "二" + '\x1e' + "かいに\x1f階に\x1f回に" + '\x1e' + "はこぶ\x1f運ぶ");
                    break;
                case "C":
                    output.WriteLine(fields[2] + '\x1f' + fields[2]);
                    break;
                case "Q":
                    return 0;
                default:
                    output.WriteLine("OK");
                    break;
            }
        }
        return 0;
    }

    [Test]
    public static void Mozc_StartsInBackground_AndDoesNotWaitForIt()
    {
        var profile = Path.Combine(Path.GetTempPath(), FakeHelperProfile);
        using var mozc = new MozcConverter(Environment.ProcessPath!, profile);

        // 起動しているあいだ (READY を待っているあいだ) の変換は、待たずに null を返す (呼び出し側が Microsoft IME で変換する)
        mozc.WarmUp();
        var watch = Stopwatch.StartNew();
        Assert.True(mozc.ConvertClauses("あ") is null, "起動を待たない");
        Assert.True(watch.ElapsedMilliseconds < 500, $"起動を待たない ({watch.ElapsedMilliseconds}ms)");
        Assert.True(WaitConverted(mozc, "い"), "起動したら変換する");

        // ヘルパーが落ちたら、起動し直しを待たずに null を返し、起動し直したらまた変換する
        Assert.True(mozc.ConvertClauses("おちる") is null, "落ちた");
        watch.Restart();
        Assert.True(mozc.ConvertClauses("う") is null, "起動し直しを待たない");
        Assert.True(watch.ElapsedMilliseconds < 500, $"起動し直しを待たない ({watch.ElapsedMilliseconds}ms)");
        Assert.True(WaitConverted(mozc, "え"), "起動し直したら変換する");
        Assert.True(mozc.IsAvailable, "起動し直せたので使える");
    }

    private static bool WaitConverted(MozcConverter mozc, string reading)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 10000)
        {
            if (mozc.ConvertClauses(reading) is { Count: 1 } clauses && clauses[0].Text == reading) return true;
            Thread.Sleep(50);
        }
        return false;
    }
}
