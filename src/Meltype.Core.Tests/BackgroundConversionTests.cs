// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Diagnostics;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Engine;
using Meltype.Input;
using Meltype.Ime;
using Meltype.Learning;

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

    private static TestSession Session(IKanjiConverter converter) => new(() => new Settings { LiveConversion = true }, converter, lateResults: true);

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
        // 打っている途中の読み (には・にほん) の結果も届くので、にほんご の結果が届くまで再描画する
        SessionResult? refreshed = null;
        Assert.True(SpinWait.SpinUntil(() => (refreshed = session.RefreshConversion())?.View?.Text == "日本語", 5000), $"再描画で結果を見せる ({refreshed?.View?.Text})");
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
        var phrases = new PhraseHistory(null);
        phrases.Remember("とることができる", "取ることができる");
        var session = new TestSession(() => new Settings { LiveConversion = true, PredictiveCandidates = true }, converter,
            new CompositionServices { Predictor = new Predictor(phrases, null, null) }, lateResults: true);
        Assert.Equal(0, SessionFacadeTests.Type(session, "tanniwotoru")[^1].View?.Predictions?.Count ?? 0);

        release.Set();
        CompositionView? view = null;
        Assert.True(SpinWait.SpinUntil(() => (view = session.RefreshConversion()?.View)?.Predictions?.Count > 0, 5000), "文節が届いたら予測が出る");
        Assert.Equal("単位を取る", view?.Text);
        Assert.Equal("取ることができる", string.Join(",", view?.Predictions ?? []));

        // 予測を選んでいる間に結果が届いても、選んでいる候補はそのまま
        var tab = SessionFacadeTests.Type(session, "\t")[^1];
        Assert.Equal(0, tab.View?.SelectedPrediction);
        Assert.Equal(0, (session.RefreshConversion() ?? tab).View?.SelectedPrediction);
    }

    [Test]
    public static void Refresh_DoesNothingWithoutComposition()
    {
        using var converter = Slow();
        Assert.True(Session(converter).RefreshConversion() is null, "入力していなければ null");
    }

    /// <summary>答えが届くまで候補を開いて、その後に答えを届ける。</summary>
    private static (TestSession Session, ManualResetEventSlim Release, ManualResetEventSlim Ready, BackgroundCallbackConverter Converter) OpenWithoutAnswer(string typed)
    {
        var release = new ManualResetEventSlim();
        var ready = new ManualResetEventSlim();
        var converter = new BackgroundCallbackConverter(
            (reading, _) =>
            {
                release.Wait(10000);
                return reading switch
                {
                    "たんいをとる" => [new ConversionClause("たんいを", "単位を"), new ConversionClause("とる", "取る")],
                    "にほんごをかく" => [new ConversionClause("にほんごを", "日本語を"), new ConversionClause("かく", "書く")],
                    _ => [new ConversionClause(reading, reading == "にほんご" ? "日本語" : reading + "!")],
                };
            },
            _ => ["候補1", "候補2"]);
        converter.ResultReady += ready.Set;
        var session = Session(converter);
        SessionFacadeTests.Type(session, typed + " ");
        return (session, release, ready, converter);
    }

    private static CompositionView? RefreshUntil(TestSession session, Func<CompositionView, bool> done)
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
        var (session, release, _, converter) = OpenWithoutAnswer("nihongowokaku");
        using (converter)
        {
            release.Set();
            var view = RefreshUntil(session, v => v.Clauses is { Count: 2 });
            Assert.Equal("日本語を,書く", string.Join(",", view?.Clauses ?? []));
            Assert.Equal(0, view?.SelectedClause, "文節が増えても先頭を選ぶ");
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
            // 1 回の作り直しの途中で答えが届くと、日本語 が にほんご の下に入った一覧が一度出る。答えがすべて届いた後の一覧を待つ
            var view = RefreshUntil(session, v => v.Candidates is ["日本語", ..]);
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

    private sealed class ChangingAnswer : TestConverter
    {
        public IReadOnlyList<ConversionClause> Clauses { get; set; } = [];

        public override string? Convert(string hiragana, string? before) => string.Concat(Clauses.Select(c => c.Text));

        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) => Clauses;
    }

    [Test]
    public static void Refresh_AfterEnterOrFocusCommitDoesNotReviveTheFinishedInput()
    {
        foreach (var focusCommit in new[] { false, true })
        {
            var converter = new ChangingAnswer { Clauses = [new("にほんご", "日本語")] };
            var session = Session(converter);
            SessionFacadeTests.Type(session, "nihongo ");
            var committed = focusCommit ? session.CommitPending() : session.HandleKey(VirtualKeys.Return, null, false, false, false, false);
            Assert.Equal("日本語", string.Concat(committed.Commits.Select(c => c.Text)), $"確定: {focusCommit}");

            converter.Clauses = [new("にほんご", "二本語")];
            var arrived = session.Send(new ImeEvent.ConversionFinished());
            Assert.True(arrived.View is null && arrived.Commits.Count == 0, "確定後の答えで未確定入力を復活させず、二重に確定しない");

            converter.Clauses = [new("きょう", "今日")];
            var next = SessionFacadeTests.Type(session, "kyou ")[^1].View;
            Assert.Equal("今日", next?.Text, "次の入力は、新しい読みの変換になる");
            Assert.True(next?.Candidates.Contains("二本語") == false, "前の読みの候補を持ち越さない");
        }
    }

    [Test]
    public static void Refresh_AfterEscapeOrDeletingAllTextDoesNotReviveCancelledInput()
    {
        foreach (var deleteAll in new[] { false, true })
        {
            var converter = new ChangingAnswer { Clauses = [new("にほんご", "日本語")] };
            var session = Session(converter);
            SessionFacadeTests.Type(session, "nihongo");
            if (deleteAll)
            {
                for (var i = 0; i < "nihongo".Length; i++) session.HandleKey(VirtualKeys.Back, null, false, false, false, false);
            }
            else session.HandleKey(VirtualKeys.Escape, null, false, false, false, false);

            converter.Clauses = [new("にほんご", "二本語")];
            var arrived = session.Send(new ImeEvent.ConversionFinished());
            Assert.True(arrived.View is null && arrived.Commits.Count == 0, $"取り消した入力を後から表示・確定しない: {deleteAll}");
            Assert.True(!session.IsComposing, "入力は終わっている");
        }
    }

    [Test]
    public static void Refresh_DoesNotReplaceTheClausesAfterMovingToAnotherClause()
    {
        foreach (var startWithRight in new[] { false, true })
        {
            var converter = new ChangingAnswer { Clauses = [new("たんいを", "単位を"), new("とる", "取る")] };
            var session = Session(converter);
            SessionFacadeTests.Type(session, "tanniwotoru");
            if (!startWithRight) session.HandleKey(VirtualKeys.Space, null, false, false, false, false);
            var view = session.HandleKey(startWithRight ? VirtualKeys.Right : VirtualKeys.Left, null, false, false, false, false).View;
            Assert.Equal(0, view?.SelectedClause, "先頭の文節を選ぶ");
            converter.Clauses = [new("たん", "短"), new("いを", "意を"), new("とる", "取る")];
            Assert.True(session.RefreshConversion() is null, "選んでいる文節がずれるので置き換えない");
        }
    }

    [Test]
    public static void Refresh_DoesNotUndoSplittingTheWholeSentence()
    {
        // 英日の区切りの案を候補にした文全体の文節を ← で分けたら、届いた答えで文全体の文節に戻さない (issue #1737)
        var converter = new ChangingAnswer { Clauses = [new("と", "と")] };
        var session = Session(converter);
        SessionFacadeTests.Type(session, "botto ");
        var split = session.HandleKey(VirtualKeys.Left, null, false, false, false, false).View;
        Assert.Equal("bot,と", string.Join(",", split?.Clauses ?? []));
        converter.Clauses = [new("と", "戸")];
        Assert.True(session.RefreshConversion() is null, "分けた文節を置き換えない");
    }

    [Test]
    public static void Refresh_DoesNotReplaceTheWholeSentenceAfterChoosingAnAlternative()
    {
        // 文全体の文節で案を選び直したあとに届いた答えで作り直すと、選んだ文が新しい案に無くなり、その案の文節に分けられなくなる (issue #1737)
        var converter = new ChangingAnswer { Clauses = [new("と", "と")] };
        var session = Session(converter);
        SessionFacadeTests.Type(session, "botto ");
        var chosen = SessionFacadeTests.Type(session, " ")[^1].View;
        Assert.True(chosen?.Clauses is [var text] && text != "botと", string.Join(",", chosen?.Clauses ?? []));
        converter.Clauses = [new("と", "戸")];
        Assert.True(session.RefreshConversion() is null, "選び直した文全体の文節を置き換えない");
    }

    [Test]
    public static void Refresh_ReturningToTheFirstClauseAllowsNewSegmentation()
    {
        foreach (var key in new[] { VirtualKeys.Space, VirtualKeys.Convert, VirtualKeys.Right, VirtualKeys.Down })
        {
            var converter = new ChangingAnswer { Clauses = [new("にほんごを", "日本語を"), new("かく", "書く")] };
            var session = Session(converter);
            SessionFacadeTests.Type(session, "nihongowokaku");
            var opened = session.HandleKey(key, null, false, false, false, false).View;
            Assert.Equal("日本語を,書く", string.Join(",", opened?.Clauses ?? []), $"変換開始: {key}");
            Assert.Equal(0, opened?.SelectedClause, $"先頭の文節を選ぶ: {key}");
            if (key == VirtualKeys.Space)
            {
                session.HandleKey(VirtualKeys.Right, null, false, false, false, false);
                session.HandleKey(VirtualKeys.Left, null, false, false, false, false);
            }
            converter.Clauses = [new("にほん", "日本"), new("ごを", "語を"), new("かく", "書く")];
            var view = session.RefreshConversion()?.View;
            Assert.Equal("日本,語を,書く", string.Join(",", view?.Clauses ?? []), $"新しい区切り: {key}");
            Assert.Equal(0, view?.SelectedClause, $"新しい先頭の文節: {key}");
        }
    }

    [Test]
    public static void Refresh_SelectsTheLastClauseWhenTheSettingIsOn()
    {
        var converter = new ChangingAnswer { Clauses = [new("にほんごを", "日本語を"), new("かく", "書く")] };
        var session = new TestSession(() => new Settings { LiveConversion = true, ConversionStartsAtLastClause = true }, converter);
        SessionFacadeTests.Type(session, "nihongowokaku");
        var opened = session.HandleKey(VirtualKeys.Space, null, false, false, false, false).View;
        Assert.Equal(1, opened?.SelectedClause, "最後の文節を選ぶ");
        converter.Clauses = [new("にほん", "日本"), new("ごを", "語を"), new("かく", "書く")];
        var view = session.RefreshConversion()?.View;
        Assert.Equal("日本,語を,書く", string.Join(",", view?.Clauses ?? []), "最後の文節を選んだままなら区切りを置き換える");
        Assert.Equal(2, view?.SelectedClause, "新しい最後の文節");
    }

    [Test]
    public static void Refresh_DoesNotReplaceTheClausesAfterChoosingACandidate()
    {
        foreach (var key in new[] { VirtualKeys.Space, VirtualKeys.Up })
        {
            var converter = new ChangingAnswer { Clauses = [new("にほんごを", "日本語を"), new("かく", "書く")] };
            var session = Session(converter);
            SessionFacadeTests.Type(session, "nihongowokaku");
            var view = session.HandleKey(key, null, false, false, false, false).View;
            if (key == VirtualKeys.Space) view = session.HandleKey(VirtualKeys.Down, null, false, false, false, false).View;
            Assert.Equal(0, view?.SelectedClause, "先頭の文節を選ぶ");
            Assert.True(view?.Clauses is [var first, "書く"] && first != "日本語を", "先頭の文節の候補を選び直す");
            converter.Clauses = [new("にほん", "日本"), new("ごを", "語を"), new("かく", "書く")];
            Assert.True(session.RefreshConversion() is null, "選び直した候補を残す");
        }
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
        var session = new TestSession(() => new Settings { LiveConversion = true }, converter);
        Assert.Equal("日本語", SessionFacadeTests.Type(session, "nihongo")[^1].View?.Text);
    }

    [Test]
    public static void MozcOnly_ShowsKanaUntilTheAnswer()
    {
        var profile = Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile);
        using var mozc = new MozcConverter(new MozcHelperProcess(Environment.ProcessPath!, profile));
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
        using var mozc = new MozcConverter(new MozcHelperProcess(Environment.ProcessPath!, profile));
        var deadline = Environment.TickCount64 + 10000;
        Assert.Equal("あ", Ask(mozc, "あ", deadline)?[0].Text, "起動して答える");
        Assert.True(mozc.Candidates("あ", deadline).Count > 0, "候補も覚えた");

        Assert.True(Ask(mozc, "おそい", 0) is null, "遅い読みをワーカーに頼む");
        Thread.Sleep(200);
        Assert.Equal("あ", Ask(mozc, "あ", 0)?[0].Text, "ヘルパーの応答を待っている間も、届いている答えを読める");
        Assert.True(mozc.Candidates("あ", 0).Count > 0, "候補も読める");
    }

    [Test]
    public static void Mozc_AnswersFromTheWorker_AndReportsReadiness()
    {
        var profile = Path.Combine(Path.GetTempPath(), MozcConverterTests.FakeHelperProfile);
        using var mozc = new MozcConverter(new MozcHelperProcess(Environment.ProcessPath!, profile));
        var notified = 0;
        mozc.ResultReady += () => Interlocked.Increment(ref notified);

        // 起動の完了を知らせる
        mozc.WarmUp();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref notified) >= 1, 10000), "起動したら知らせる");

        var watch = Stopwatch.StartNew();
        Assert.True(Ask(mozc, "あ", 0) is null, "控えが無ければ待たない");
        Assert.True(watch.ElapsedMilliseconds < 500, $"応答を待たない ({watch.ElapsedMilliseconds}ms)");
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref notified) >= 2, 5000), "結果が届いたら知らせる");
        Assert.Equal("あ", Ask(mozc, "あ", 0)?[0].Text);

        // 失敗は知らせない (再描画が同じ要求を繰り返さない)
        var before = Volatile.Read(ref notified);
        Assert.True(Ask(mozc, "おちる", 0) is null, "落ちる読み");
        Thread.Sleep(500);
        Assert.Equal(before, Volatile.Read(ref notified));
    }

    /// <summary>deadline (Environment.TickCount64) まで答えを待つ変換。0 なら待たない。</summary>
    private static IReadOnlyList<ConversionClause>? Ask(MozcConverter mozc, string reading, long deadline) => mozc.Convert(new ConversionRequest(reading, null) { Deadline = deadline });
}
