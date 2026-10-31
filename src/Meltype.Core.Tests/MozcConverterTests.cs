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

    /// <summary>偽のヘルパー: 1 秒たってから READY を返す。まつ の変換・学習と、指定された保存では、テストが解除するまで待つ。</summary>
    public static int RunFakeHelper()
    {
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var profile = Environment.GetCommandLineArgs()[^1];
        void Pause()
        {
            File.WriteAllText(Path.Combine(profile, "busy"), "");
            SpinWait.SpinUntil(() => File.Exists(Path.Combine(profile, "release")), 10000);
        }
        Thread.Sleep(1000);
        output.WriteLine("READY");
        while (input.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            switch (fields[0])
            {
                case "C" when fields[2] == "おちる":
                    return 1;
                case "L" when fields[2].StartsWith("まつ" + UnitSeparator, StringComparison.Ordinal):
                case "S" when File.Exists(Path.Combine(profile, "slow-save")):
                    Pause();
                    output.WriteLine("OK");
                    break;
                case "C" when fields[2] == "まつ":
                    Pause();
                    output.WriteLine(fields[2] + UnitSeparator + fields[2]);
                    break;
                case "C" when fields[2] == "ゆっくり":
                    Thread.Sleep(75);
                    output.WriteLine(fields[2] + UnitSeparator + fields[2]);
                    break;
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

    private const char UnitSeparator = '\x1f';

    [Test]
    public static void Issue1209_SlowLearningDoesNotBlockInput() => CheckBusyHelper("L");

    [Test]
    public static void Issue1209_SlowSaveDoesNotBlockInput() => CheckBusyHelper("S");

    [Test]
    public static void Issue1209_OtherConversionDoesNotBlockInput() => CheckBusyHelper("C");

    private static void CheckBusyHelper(string operation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-mozc-busy-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(directory, FakeHelperProfile);
        try
        {
            using var mozc = new MozcConverter(Environment.ProcessPath!, profile);
            mozc.WarmUp();
            Assert.True(WaitConverted(mozc, "あ"), "起動した");
            var hybrid = new HybridConverter(() => Config.ConversionEngine.Hybrid, mozc, new CompositionTests.FakeConverter(), null);
            var keyboard = new CompositionTests.Keyboard(live: true, converter: hybrid, moreCandidates: hybrid.Candidates);
            mozc.TakeWaitStats();
            if (operation == "S") File.WriteAllText(Path.Combine(profile, "slow-save"), "");
            var busy = Task.Run(() =>
            {
                if (operation == "C") mozc.Convert("まつ");
                else if (operation == "L") mozc.Learn(null, [new ConversionClause("まつ", "待つ")]);
                else for (var i = 0; i < 5; i++) mozc.Learn(null, [new ConversionClause("にほんご", "日本語")]);
            });
            try
            {
                Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(profile, "busy")), 5000), "ヘルパーが処理中になった");
                var watch = Stopwatch.StartNew();
                Assert.Equal("日本語", hybrid.Convert("にほんご"), "処理中は別の変換エンジンで入力を続ける");
                Assert.Equal(0, mozc.Candidates("にほんご").Count, "処理中は候補も待たない");
                Assert.True(watch.ElapsedMilliseconds < 500, $"{operation} の待ちに入力を巻き込まない ({watch.ElapsedMilliseconds}ms)");
                Console.WriteLine($"Mozc の {operation} 処理中: 入力側 {watch.Elapsed.TotalMilliseconds:F1} ms");
                Assert.Equal(2L, mozc.TakeWaitStats().BusySkips, "変換と候補の見送りを数える");
                keyboard.Type("nihongo\n");
                Assert.Equal("日本語", keyboard.Host.Document, "ライブ変換から確定まで、打った文字を失わない");
            }
            finally
            {
                File.WriteAllText(Path.Combine(profile, "release"), "");
                Assert.True(busy.Wait(5000), "裏の処理を終えた");
            }
            Assert.True(WaitConverted(mozc, "にほんご"), "処理が終わったら Mozc を再び使える");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void Issue1209_CountsMozcWaitsWithoutLearningOrCacheHits()
    {
        using var mozc = new MozcConverter(Environment.ProcessPath!, Path.Combine(Path.GetTempPath(), FakeHelperProfile));
        mozc.WarmUp();
        Assert.True(WaitConverted(mozc, "あ"), "起動した");
        mozc.TakeWaitStats();
        Assert.Equal("ゆっくり", mozc.Convert("ゆっくり"));
        Assert.Equal("ゆっくり", mozc.Candidates("ゆっくり").Single(), "候補の控えを使う");
        mozc.Learn(null, [new ConversionClause("にほんご", "日本語")]);
        var (calls, ms, busy) = mozc.TakeWaitStats();
        Assert.Equal(1L, calls, "入力側の問い合わせだけを数える");
        Assert.True(ms >= 40, $"ヘルパーを待った時間: {ms}ms");
        Assert.Equal(0L, busy);
        Assert.Equal((0L, 0L, 0L), mozc.TakeWaitStats(), "取ったら 0 に戻す");
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
