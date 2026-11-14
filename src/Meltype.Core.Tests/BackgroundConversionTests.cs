// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Diagnostics;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>変換エンジンを入力スレッドで待たず、結果が届いたら表示を作り直す仕組みのテスト。</summary>
internal static class BackgroundConversionTests
{
    /// <summary>delayMs たってから答える変換エンジン。release を渡すと、それが Set されるまで答えない。</summary>
    private static BackgroundCallbackConverter Slow(int delayMs = 0, ManualResetEventSlim? release = null) => new(
        (reading, _) =>
        {
            release?.Wait(10000);
            Thread.Sleep(delayMs);
            return [new ConversionClause(reading, reading == "にほんご" ? "日本語" : reading + "!")];
        },
        _ => []);

    private static MeltypeSession Session(IKanjiConverter converter)
    {
        (converter as IBackgroundConverter)?.Budget.Enable();
        return new(CompositionTests.Detector, converter, new CompositionServices(), () => new Settings { LiveConversion = true });
    }

    [Test]
    public static void Signal_DoesNotLoseANotificationBeforeWaiting()
    {
        var signal = new ConversionSignal();
        Assert.Equal(0L, signal.WaitAsync(-1, 5000, default).Result, "知らない世代なら、すぐ今の世代を返す");
        signal.Notify();
        var watch = Stopwatch.StartNew();
        Assert.Equal(1L, signal.WaitAsync(0, 5000, default).Result, "待つ前に進んでいたら、すぐ返す");
        Assert.True(watch.ElapsedMilliseconds < 2000, $"待たない ({watch.ElapsedMilliseconds}ms)");
    }

    [Test]
    public static void Signal_WakesAWaiter_AndTimesOut()
    {
        var signal = new ConversionSignal();
        var waiting = signal.WaitAsync(0, 5000, default);
        Assert.True(!waiting.IsCompleted, "進むまで待つ");
        signal.Notify();
        Assert.Equal(1L, waiting.Result, "進んだら返す");

        var watch = Stopwatch.StartNew();
        Assert.Equal(1L, signal.WaitAsync(1, 50, default).Result, "時間切れでも今の世代を返す");
        Assert.True(watch.ElapsedMilliseconds < 2000, $"時間切れで返す ({watch.ElapsedMilliseconds}ms)");
    }

    [Test]
    public static void Worker_ProcessesOnlyTheActiveAndLatestRequests()
    {
        var processed = new List<int>();
        using var started = new ManualResetEventSlim();
        using var first = new ManualResetEventSlim();
        using var worker = new LatestRequestWorker<int>(n =>
        {
            started.Set();
            first.Wait();
            lock (processed) processed.Add(n);
        });
        worker.Submit(1);
        Assert.True(started.Wait(5000), "1 は処理中");
        worker.Submit(2);
        worker.Submit(3);
        first.Set();
        Assert.True(worker.Wait(3, 5000), "最後の要求が終わる");
        lock (processed) Assert.Equal("1,3", string.Join(",", processed));
    }

    [Test]
    public static void Worker_WaitGivesUpAtTimeout_AndSkipsSettling()
    {
        using var release = new ManualResetEventSlim();
        using var worker = new LatestRequestWorker<int>(_ => release.Wait());
        worker.Submit(1);
        var watch = Stopwatch.StartNew();
        Assert.True(!worker.Wait(1, 50), "終わらなければ false");
        Assert.True(watch.ElapsedMilliseconds < 1000, $"待ちすぎない ({watch.ElapsedMilliseconds}ms)");
        release.Set();
        Assert.True(worker.Wait(1, 5000), "終われば true");

        using var settling = new LatestRequestWorker<int>(_ => { }, settleMs: 5000);
        settling.Submit(1);
        watch.Restart();
        Assert.True(settling.Wait(1, 3000), "Wait で待たれている要求は settle を待たない");
        Assert.True(watch.ElapsedMilliseconds < 3000, $"settle を待たない ({watch.ElapsedMilliseconds}ms)");
    }

    [Test]
    public static void Worker_SurvivesAnExceptionInProcess()
    {
        var calls = 0;
        using var worker = new LatestRequestWorker<int>(n =>
        {
            Interlocked.Increment(ref calls);
            if (n == 1) throw new InvalidOperationException("試し");
        });
        worker.Submit(1);
        Assert.True(worker.Wait(1, 5000), "例外でも終わる");
        worker.Submit(2);
        Assert.True(worker.Wait(2, 5000), "次の要求も処理する");
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Test]
    public static void Live_DoesNotWait_AndRefreshShowsTheResult()
    {
        using var release = new ManualResetEventSlim();
        using var converter = Slow(release: release);
        using var ready = new ManualResetEventSlim();
        converter.ResultReady += ready.Set;
        var session = Session(converter);

        var watch = Stopwatch.StartNew();
        var results = SessionFacadeTests.Type(session, "nihongo");
        Assert.True(watch.ElapsedMilliseconds < 2000, $"打鍵は答えを待たない ({watch.ElapsedMilliseconds}ms)");
        Assert.Equal("にほんご", results[^1].View?.Text);

        release.Set();
        Assert.True(ready.Wait(5000), "結果が届いたら知らせる");
        var refreshed = session.RefreshConversion();
        Assert.Equal("日本語", refreshed?.View?.Text);
        Assert.True(refreshed!.Consumed && refreshed.Commits.Count == 0, "再描画は確定しない");
    }

    [Test]
    public static void Refresh_RebuildsPredictionsFromTheArrivedClauses()
    {
        // 最後の文節 (とる) の予測は、文節が届くまで出せない (Prediction_FromLastClause の非同期の経路)
        using var release = new ManualResetEventSlim();
        using var ready = new ManualResetEventSlim();
        using var converter = new BackgroundCallbackConverter(
            (reading, _) =>
            {
                release.Wait(10000);
                return reading == "たんいをとる" ? [new ConversionClause("たんいを", "単位を"), new ConversionClause("とる", "取る")] : [new ConversionClause(reading, reading)];
            },
            _ => []);
        converter.Budget.Enable();
        var phrases = new PhraseHistory(null);
        phrases.Remember("とることができる", "取ることができる");
        var session = new MeltypeSession(CompositionTests.Detector, converter,
            new CompositionServices { Predictor = new Predictor(phrases, null, null) }, () => new Settings { LiveConversion = true, PredictiveCandidates = true });
        Assert.Equal(0, SessionFacadeTests.Type(session, "tanniwotoru")[^1].View?.Predictions?.Count ?? 0);

        release.Set();
        CompositionView? view = null;
        Assert.True(SpinWait.SpinUntil(() => (view = session.RefreshConversion()?.View)?.Predictions?.Count > 0, 5000), "文節が届いたら予測が出る");
        Assert.Equal("単位を取る", view?.Text);
        Assert.Equal("取ることができる", string.Join(",", view?.Predictions ?? []));

        // 予測を選んでいる間に結果が届いても、選んでいる候補はそのまま
        Assert.Equal(0, SessionFacadeTests.Type(session, "\t")[^1].View?.SelectedPrediction);
        Assert.Equal(0, session.RefreshConversion()?.View?.SelectedPrediction);
    }

    [Test]
    public static void Refresh_DoesNothingWithoutComposition()
    {
        using var converter = Slow();
        Assert.True(Session(converter).RefreshConversion() is null, "入力していなければ null");
    }

    /// <summary>答えが届くまで候補を開いて、その後に答えを届ける。</summary>
    private static (MeltypeSession Session, ManualResetEventSlim Release, ManualResetEventSlim Ready, BackgroundCallbackConverter Converter) OpenWithoutAnswer(string typed)
    {
        var release = new ManualResetEventSlim();
        var ready = new ManualResetEventSlim();
        var converter = new BackgroundCallbackConverter(
            (reading, _) =>
            {
                release.Wait(10000);
                return reading == "たんいをとる"
                    ? [new ConversionClause("たんいを", "単位を"), new ConversionClause("とる", "取る")]
                    : [new ConversionClause(reading, reading == "にほんご" ? "日本語" : reading + "!")];
            },
            _ => ["候補1", "候補2"]);
        converter.ResultReady += ready.Set;
        var session = Session(converter);
        SessionFacadeTests.Type(session, typed + " ");
        return (session, release, ready, converter);
    }

    private static CompositionView? RefreshUntil(MeltypeSession session, Func<CompositionView, bool> done)
    {
        CompositionView? view = null;
        SpinWait.SpinUntil(() => session.RefreshConversion()?.View is { } v && done(view = v), 5000);
        return view;
    }

    [Test]
    public static void Refresh_ReplacesAnUntouchedOpenConversion()
    {
        // 答えが間に合わないまま Space で開く (予算 400ms を待ってから、かなで開く)。3 文字以内の読みも同じ
        var (session, release, _, converter) = OpenWithoutAnswer("kyou");
        using (converter)
        {
            release.Set();
            var view = RefreshUntil(session, v => v.Text == "きょう!");
            Assert.Equal("きょう!", view?.Text);
            Assert.True(view?.Converting == true, "候補を開いたまま");
        }
    }

    [Test]
    public static void Refresh_SplitsAnUntouchedConversionIntoTheArrivedClauses()
    {
        var (session, release, _, converter) = OpenWithoutAnswer("tanniwotoru");
        using (converter)
        {
            release.Set();
            Assert.Equal("単位を,取る", string.Join(",", RefreshUntil(session, v => v.Clauses is { Count: 2 })?.Clauses ?? []));
        }
    }

    [Test]
    public static void Refresh_KeepsTheChosenCandidate_AndInsertsNewOnesAbove()
    {
        var (session, release, _, converter) = OpenWithoutAnswer("nihongo");
        using (converter)
        {
            // 候補を選び直す (↓)。ひらがな → カタカナ
            var moved = session.HandleKey(VirtualKeys.Down, null, false, false, false, false).View;
            var chosen = moved?.Text;
            Assert.True(chosen != "にほんご", $"選び直した: {chosen}");
            release.Set();
            var view = RefreshUntil(session, v => v.Candidates.Contains("日本語"));
            Assert.Equal(chosen, view?.Text, "選んでいる候補は変えない");
            Assert.Equal("日本語", view?.Candidates[0], "新しい候補は上に入る");
            Assert.Equal(view!.Candidates.ToList().IndexOf(chosen!), view.SelectedIndex);
        }
    }

    [Test]
    public static void Refresh_DoesNotUndoAResizedClause()
    {
        var (session, release, ready, converter) = OpenWithoutAnswer("tanniwotoru");
        using (converter)
        {
            var resized = session.HandleKey(VirtualKeys.Left, null, true, false, false, false).View;
            release.Set();
            Assert.True(ready.Wait(5000), "結果が届く");
            Thread.Sleep(100);
            Assert.True(session.RefreshConversion() is null, $"区切りを動かしたあとは置き換えない: {resized?.Text}");
        }
    }

    private sealed class ChangingAnswer : IKanjiConverter, IBackgroundConverter
    {
        public IReadOnlyList<ConversionClause> Clauses { get; set; } = [];

        public ConversionBudget Budget { get; } = new();

        public event Action? ResultReady { add { } remove { } }

        public string? Convert(string hiragana) => string.Concat(Clauses.Select(c => c.Text));

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => Clauses;
    }

    [Test]
    public static void Refresh_DoesNotReplaceTheClausesAfterMovingToAnotherClause()
    {
        var converter = new ChangingAnswer { Clauses = [new("たんいを", "単位を"), new("とる", "取る")] };
        var session = Session(converter);
        SessionFacadeTests.Type(session, "tanniwotoru ");
        session.HandleKey(VirtualKeys.Right, null, false, false, false, false);
        converter.Clauses = [new("たん", "短"), new("いを", "意を"), new("とる", "取る")];
        Assert.True(session.RefreshConversion() is null, "選んでいる文節がずれるので置き換えない");
    }

    [Test]
    public static void Space_WaitsWithinTheBudget()
    {
        using var converter = Slow(delayMs: 50);
        var session = Session(converter);
        SessionFacadeTests.Type(session, "nihongo");
        var watch = Stopwatch.StartNew();
        var opened = SessionFacadeTests.Type(session, " ")[^1].View;
        Assert.True(watch.ElapsedMilliseconds < 800, $"Meltype IME の応答期限より短い ({watch.ElapsedMilliseconds}ms)");
        Assert.Equal("日本語", opened?.Text);
    }

    [Test]
    public static void Space_GivesUpAtTheBudget()
    {
        using var release = new ManualResetEventSlim();
        using var converter = Slow(release: release);
        var session = Session(converter);
        SessionFacadeTests.Type(session, "nihongo");
        var watch = Stopwatch.StartNew();
        SessionFacadeTests.Type(session, " ");
        Assert.True(watch.ElapsedMilliseconds < 800, $"答えが来なくても応答期限より先に返す ({watch.ElapsedMilliseconds}ms)");
        release.Set();
    }

    [Test]
    public static void WithoutEnable_WaitsForTheAnswer()
    {
        // --repro・--self-test: 結果を受け取る経路が無いので、打鍵ごとに答えを待つ
        using var converter = Slow(delayMs: 50);
        var session = new MeltypeSession(CompositionTests.Detector, converter, new CompositionServices(), () => new Settings { LiveConversion = true });
        Assert.Equal("日本語", SessionFacadeTests.Type(session, "nihongo")[^1].View?.Text);
    }

    [Test]
    public static void MozcOnly_ShowsKanaUntilTheAnswer()
    {
        var profile = Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile);
        using var mozc = new MozcConverter(Environment.ProcessPath!, profile);
        using var ready = new ManualResetEventSlim();
        mozc.ResultReady += ready.Set;
        mozc.WarmUp();
        Assert.True(ready.Wait(10000), "起動したら知らせる");

        var hybrid = new HybridConverter(mozc);
        var session = Session(hybrid);
        Assert.Equal("きしゃがきた", SessionFacadeTests.Type(session, "kishagakita")[^1].View?.Text);
        Assert.True(SpinWait.SpinUntil(() => session.RefreshConversion()?.View?.Text == "記者が来た", 5000), "届いたら表示が更新される");
    }

    [Test]
    public static void Mozc_AnswersFromTheCacheWhileTheHelperIsBusy()
    {
        // ヘルパーの応答を待っている間に再描画しても、届いている答えが読めないとかなに戻って、表示が 漢字 → かな と揺れる
        var profile = Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile);
        using var mozc = new MozcConverter(Environment.ProcessPath!, profile);
        mozc.Budget.Enable();
        mozc.Budget.Start(10000);
        Assert.Equal("あ", mozc.ConvertClauses("あ")?[0].Text, "起動して答える");
        Assert.True(mozc.Candidates("あ").Count > 0, "候補も覚えた");

        mozc.Budget.Start(0);
        Assert.True(mozc.ConvertClauses("おそい") is null, "遅い読みをワーカーに頼む");
        Thread.Sleep(200);
        Assert.Equal("あ", mozc.ConvertClauses("あ")?[0].Text, "ヘルパーの応答を待っている間も、届いている答えを読める");
        Assert.True(mozc.Candidates("あ").Count > 0, "候補も読める");
    }

    [Test]
    public static void Mozc_AnswersFromTheWorker_AndReportsReadiness()
    {
        var profile = Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile);
        using var mozc = new MozcConverter(Environment.ProcessPath!, profile);
        var notified = 0;
        mozc.ResultReady += () => Interlocked.Increment(ref notified);
        mozc.Budget.Enable();

        // 起動の完了を知らせる
        mozc.WarmUp();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref notified) >= 1, 10000), "起動したら知らせる");

        var watch = Stopwatch.StartNew();
        Assert.True(mozc.ConvertClauses("あ") is null, "控えが無ければ待たない");
        Assert.True(watch.ElapsedMilliseconds < 500, $"応答を待たない ({watch.ElapsedMilliseconds}ms)");
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref notified) >= 2, 5000), "結果が届いたら知らせる");
        Assert.Equal("あ", mozc.ConvertClauses("あ")?[0].Text);

        // 失敗は知らせない (再描画が同じ要求を繰り返さない)
        var before = Volatile.Read(ref notified);
        Assert.True(mozc.ConvertClauses("おちる") is null, "落ちる読み");
        Thread.Sleep(500);
        Assert.Equal(before, Volatile.Read(ref notified));
    }
}
