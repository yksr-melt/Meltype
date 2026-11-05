// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Diagnostics;
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>変換エンジンを入力スレッドで待たず、結果が届いたら表示を作り直す仕組みのテスト。</summary>
internal static class BackgroundConversionTests
{
    /// <summary>delayMs たってから答える変換エンジン。release を渡すと、それが Set されるまで答えない。</summary>
    private sealed class SlowConverter : IKanjiConverter, IBackgroundConverter, IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, string> _answers = [];
        private readonly LatestRequestWorker<Key> _worker;
        private readonly ManualResetEventSlim? _release;
        private readonly int _delayMs;

        public SlowConverter(int delayMs = 0, ManualResetEventSlim? release = null)
        {
            _delayMs = delayMs;
            _release = release;
            _worker = new LatestRequestWorker<Key>(Process);
        }

        public ConversionBudget Budget { get; } = new();
        public event Action? ResultReady;

        private readonly record struct Key(string Reading);

        private void Process(Key key)
        {
            _release?.Wait();
            Thread.Sleep(_delayMs);
            lock (_gate) _answers[key.Reading] = key.Reading switch
            {
                "にほんご" => "日本語",
                _ => key.Reading + "!",
            };
            ResultReady?.Invoke();
        }

        public string? Convert(string hiragana) => ConvertClauses(hiragana)?[0].Text;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            lock (_gate)
            {
                if (_answers.TryGetValue(hiragana, out var known)) return [new ConversionClause(hiragana, known)];
            }
            var key = new Key(hiragana);
            _worker.Submit(key);
            if (!_worker.Wait(key, Budget.RemainingMs)) return null;
            lock (_gate) return _answers.TryGetValue(hiragana, out var answer) ? [new ConversionClause(hiragana, answer)] : null;
        }

        public void Dispose() => _worker.Dispose();
    }

    private static MeltypeSession Session(IKanjiConverter converter, bool background = true) =>
        new(CompositionTests.Detector, converter, new CompositionOptions { BackgroundConversion = background, LiveConversion = () => true }, () => new Settings());

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
        using var converter = new SlowConverter(release: release);
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
    public static void Refresh_DoesNothingWithoutComposition()
    {
        using var converter = new SlowConverter();
        Assert.True(Session(converter).RefreshConversion() is null, "入力していなければ null");
    }

    [Test]
    public static void Refresh_KeepsTheCandidatesOfAnOpenConversion()
    {
        using var release = new ManualResetEventSlim();
        using var converter = new SlowConverter(release: release);
        var session = Session(converter);
        SessionFacadeTests.Type(session, "nihongo");
        // 答えが間に合わないまま Space で開く (予算 400ms を待ってから、かなで開く)
        var opened = SessionFacadeTests.Type(session, " ")[^1].View;
        Assert.True(opened?.Converting == true, "変換を開いた");
        Assert.Equal("にほんご", opened!.Text);

        using var ready = new ManualResetEventSlim();
        converter.ResultReady += ready.Set;
        release.Set();
        Assert.True(ready.Wait(5000), "結果が届く");
        Assert.True(session.RefreshConversion() is null, "開いた候補の表示は作り直さない");
    }

    [Test]
    public static void Space_WaitsWithinTheBudget()
    {
        using var converter = new SlowConverter(delayMs: 50);
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
        using var converter = new SlowConverter(release: release);
        var session = Session(converter);
        SessionFacadeTests.Type(session, "nihongo");
        var watch = Stopwatch.StartNew();
        SessionFacadeTests.Type(session, " ");
        Assert.True(watch.ElapsedMilliseconds < 800, $"答えが来なくても応答期限より先に返す ({watch.ElapsedMilliseconds}ms)");
        release.Set();
    }

    [Test]
    public static void WithoutTheOption_ConverterIsSynchronous()
    {
        using var converter = new SlowConverter(delayMs: 50);
        // --repro など: Budget を使わないので、変換エンジンが答えるまで待つ想定の呼び方は変わらない
        Assert.True(!converter.Budget.Limited, "Start を呼ぶまで制限なし");
        Session(converter, background: false);
        Assert.True(!converter.Budget.Limited, "オプションが無ければ Budget を使わない");
        Session(converter, background: true);
        Assert.True(converter.Budget.Limited, "オプションがあれば Budget を使う");
    }

    [Test]
    public static void Callback_IsCalledFromOneThreadAtATime()
    {
        // Mac の変換エンジン (MeltypeConverter) はスレッドセーフでない。文節と候補を重ねて呼ばない
        var running = 0;
        var overlapped = false;
        void Enter()
        {
            if (Interlocked.Increment(ref running) != 1) overlapped = true;
            Thread.Sleep(5);
            Interlocked.Decrement(ref running);
        }
        using var converter = new BackgroundCallbackConverter(
            (reading, _) => { Enter(); return [new ConversionClause(reading, reading + "!")]; },
            reading => { Enter(); return [reading + "?"]; });
        converter.Budget.Start(0);
        var notified = 0;
        converter.ResultReady += () => Interlocked.Increment(ref notified);
        for (var i = 0; i < 40; i++)
        {
            converter.ConvertClauses("よみ" + i);
            converter.Candidates("よみ" + i);
            Thread.Sleep(2);
        }
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref notified) > 0, 5000), "結果が届く");
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref running) == 0, 5000), "ワーカーが止まる");
        Assert.True(!overlapped, "同時に呼ばない");
    }

    [Test]
    public static void Callback_AnswersLater_AndWaitsOnlyWithinTheBudget()
    {
        using var release = new ManualResetEventSlim();
        using var ready = new ManualResetEventSlim();
        using var converter = new BackgroundCallbackConverter(
            (reading, _) => { release.Wait(); return [new ConversionClause(reading, "答え")]; },
            _ => ["候補"]);
        converter.ResultReady += ready.Set;
        converter.Budget.Start(0);

        var watch = Stopwatch.StartNew();
        Assert.True(converter.ConvertClauses("よみ") is null, "答えが無ければ待たない");
        Assert.True(watch.ElapsedMilliseconds < 1000, $"待たない ({watch.ElapsedMilliseconds}ms)");
        release.Set();
        Assert.True(ready.Wait(5000), "答えが届いたら知らせる");
        Assert.Equal("答え", converter.Convert("よみ"));

        // 候補は Budget の間だけ待てば、その場で受け取れる
        converter.Budget.Start(3000);
        Assert.Equal("候補", string.Join(",", converter.Candidates("よみ")));
    }

    [Test]
    public static void Callback_IsSynchronousWithoutTheBudget()
    {
        // --self-test など、meltype_set_conversion_ready を呼ばないフロントは今までどおり呼んだスレッドで答える
        var thread = -1;
        using var converter = new BackgroundCallbackConverter(
            (reading, _) => { thread = Environment.CurrentManagedThreadId; return [new ConversionClause(reading, "答え")]; },
            _ => []);
        Assert.Equal("答え", converter.Convert("よみ"));
        Assert.Equal(Environment.CurrentManagedThreadId, thread);
    }

    [Test]
    public static void Mozc_AnswersFromTheWorker_AndReportsReadiness()
    {
        var profile = Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile);
        using var mozc = new MozcConverter(Environment.ProcessPath!, profile);
        var notified = 0;
        mozc.ResultReady += () => Interlocked.Increment(ref notified);
        mozc.Budget.Start(0);

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
