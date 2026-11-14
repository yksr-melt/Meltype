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
                // 応答が遅い読み (ヘルパーの応答を待っている間も、キャッシュの答えを読めるかのテスト)
                case "C" when fields[2] == "おそい":
                    Thread.Sleep(1000);
                    output.WriteLine(fields[2] + '\x1f' + fields[2]);
                    break;
                // 2 つの文節に分けて、文節ごとの候補も返す (Zenzai の結果を Mozc の文節に当てはめるテスト: ZenzaiConverterTests)
                case "C" when fields[2] == "きしゃがきた":
                    output.WriteLine("きしゃが\x1f記者が\x1f汽車が\x1f帰社が" + '\x1e' + "きた\x1f来た\x1f着た\x1f北");
                    break;
                // 読みと表記が違う候補 (ず の読みの 気づく) がある (Zenzai の答えを Mozc の候補で確かめるテスト: ZenzaiConverterTests)
                case "C" when fields[2] == "きずくのがはやい":
                    output.WriteLine("きずくのが\x1f築くのが\x1f気づくのが" + '\x1e' + "はやい\x1f早い\x1f速い");
                    break;
                // 助詞だけの文節 (に) の候補に数字がある (Zenzai の 荷物を2階に運ぶ を当てはめると、に を 2 にしてしまう)
                case "C" when fields[2] == "にもつをにかいにはこぶ":
                    // (\x1f の後ろに 2 を続けて書くと \x1f2 という 1 文字になるので、区切りは別の文字として足す)
                    output.WriteLine("にもつを\x1f荷物を" + '\x1e' + "に\x1fに" + '\x1f' + "2" + '\x1f' + "二" + '\x1e' + "かいに\x1f階に\x1f回に" + '\x1e' + "はこぶ\x1f運ぶ");
                    break;
                // CompositionTests.FakeConverter と同じ答え (Zenzai を組み合わせるテスト: ZenzaiConverterTests)
                case "C" when fields[2] == "たんいをとる":
                    output.WriteLine("たんいを\x1f単位を" + '\x1e' + "とる\x1f取る");
                    break;
                case "C" when fields[2] == "たんい":
                    output.WriteLine("たんい\x1f単位");
                    break;
                case "C" when fields[2] == "きょう":
                    output.WriteLine("きょう\x1f今日");
                    break;
                case "C":
                    output.WriteLine(fields[2] + '\x1f' + fields[2]);
                    break;
                // 逆変換は 漢字 だけ読める
                case "R":
                    output.WriteLine(fields[1] == "漢字" ? "かんじ" : "");
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

    /// <summary>偽のヘルパーで動く Mozc (最初の変換は起動を待つ)。</summary>
    public static MozcConverter Fake() => new(Environment.ProcessPath!, Path.Combine(Path.GetTempPath(), FakeHelperProfile));

    [Test]
    public static void Mozc_StartsInBackground_AndDoesNotWaitForIt()
    {
        var profile = Path.Combine(Path.GetTempPath(), FakeHelperProfile);
        using var mozc = new MozcConverter(Environment.ProcessPath!, profile);
        mozc.Budget.Enable();

        // 起動しているあいだ (READY を待っているあいだ) の変換は、待たずに null を返す (呼び出し側はかなのまま見せる)
        mozc.WarmUp();
        var watch = Stopwatch.StartNew();
        Assert.True(mozc.ConvertClauses("あ") is null, "起動を待たない");
        Assert.True(watch.ElapsedMilliseconds < 500, $"起動を待たない ({watch.ElapsedMilliseconds}ms)");
        Assert.True(WaitConverted(mozc, "い"), "起動したら変換する");

        // ヘルパーが落ちたら、起動し直しを待たずに null を返し、起動し直したらまた変換する
        mozc.Budget.Start(5000);
        Assert.True(mozc.ConvertClauses("おちる") is null, "落ちた");
        mozc.Budget.Start(0);
        watch.Restart();
        Assert.True(mozc.ConvertClauses("う") is null, "起動し直しを待たない");
        Assert.True(watch.ElapsedMilliseconds < 500, $"起動し直しを待たない ({watch.ElapsedMilliseconds}ms)");
        Assert.True(WaitConverted(mozc, "え"), "起動し直したら変換する");
        Assert.True(mozc.IsAvailable, "起動し直せたので使える");
    }

    [Test]
    public static void Mozc_Reading_AsksTheHelper()
    {
        var profile = Path.Combine(Path.GetTempPath(), FakeHelperProfile);
        using var mozc = new MozcConverter(Environment.ProcessPath!, profile);
        Assert.Equal("かんじ", mozc.Reading("漢字"));
        Assert.True(mozc.Reading("読めない") is null, "逆変換できなければ null");
        Assert.True(mozc.IsAvailable, "逆変換できなくても使える");
        // かなだけの文字列はヘルパーに聞かずにひらがなにする
        var hybrid = new HybridConverter(mozc);
        Assert.Equal("めるたいぷ", hybrid.Reading("メルタイプ"));
        Assert.Equal("かんじ", hybrid.Reading("漢字"));
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
