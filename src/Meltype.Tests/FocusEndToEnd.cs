// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TakeMoriXX

using System.Diagnostics;
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// 調査用: クリックの直後に打ち始めたときのフォーカスの判定を、実際のウィンドウで確かめる。
///   Meltype.Tests --focus-e2e
/// マウスを SendInput で動かしてクリックするので、数秒間このウィンドウが前面に出る (キーは送らない)。
/// キーを押したときの判定は MeltypeEngine と同じ順 (CanCaptureWaiting → RecheckAfterClick) で呼び、
/// 変換ボックスに入れるか (= キーを横取りするか) と、フックで止まった時間を測る。
/// </summary>
internal static class FocusEndToEnd
{
    // Enable only during targeted diagnosis; keep the default timing test unchanged.
    private static readonly bool DiagnosticsEnabled = Environment.GetEnvironmentVariable("MELTYPE_FOCUS_DIAGNOSTICS") == "1";
    private sealed record Result(bool Captured, bool CapturedBeforeFix, bool ByRecheck, long Waited, long TotalMs, int DelayMs = 0, string Focus = "",
        IntPtr TargetHwnd = default, IntPtr NativeFocusAtKey = default, IntPtr NativeFocusAfterCheck = default,
        IntPtr ForegroundAtKey = default, IntPtr ForegroundAfterCheck = default);

    public static int Run()
    {
        Application.EnableVisualStyles();
        var form = new Form { Text = "Meltype focus test", Width = 520, Height = 320, StartPosition = FormStartPosition.Manual, Location = new Point(200, 200), TopMost = true };
        var input = new TextBox { Left = 20, Top = 20, Width = 220 };
        var button = new Button { Left = 260, Top = 18, Width = 120, Text = "送信" };
        var password = new TextBox { Left = 20, Top = 70, Width = 220, UseSystemPasswordChar = true };
        // 調べるのが遅いアプリの代わり: フォーカスが来ると UI スレッドが 300ms 止まる (その間 UI Automation の問い合わせも返らない)
        var slow = new TextBox { Left = 20, Top = 120, Width = 220 };
        slow.Enter += (_, _) => Thread.Sleep(300);
        form.Controls.AddRange([input, button, password, slow]);
        // 入力欄の無いウィンドウ (ゲームの代わり)
        var game = new Form { Text = "Meltype focus test (no input)", Width = 300, Height = 200, StartPosition = FormStartPosition.Manual, Location = new Point(760, 200), TopMost = true };
        var gameButton = new Button { Left = 20, Top = 20, Width = 200, Height = 100, Text = "操作" };
        game.Controls.Add(gameButton);

        var failures = 0;
        form.Shown += (_, _) =>
        {
            game.Show();
            Input.ForegroundTracker.TypingAllowedWindows[form.Handle] = true;
            Input.ForegroundTracker.TypingAllowedWindows[game.Handle] = true;
            Task.Run(() =>
            {
                using var focus = new FocusInspector();
                try { failures = Scenarios(focus, form, input, button, password, slow, gameButton); }
                catch (Exception ex) { Console.WriteLine($"例外: {ex}"); failures++; }
                form.BeginInvoke(() => { game.Close(); form.Close(); });
            });
        };
        Application.Run(form);
        Console.WriteLine(failures == 0 ? "すべて期待どおり" : $"期待と違った: {failures} 件");
        return failures == 0 ? 0 : 1;
    }

    private static int Scenarios(FocusInspector focus, Form form, TextBox input, Button button, TextBox password, TextBox slow, Button gameButton)
    {
        var failures = 0;
        void Expect(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "OK " : "NG ")} {what}");
            if (!ok) failures++;
        }

        // 最初に入力欄を一度選んでおく (ふだん使っているウィンドウ = 入力欄を見たことのあるウィンドウ)
        Settle(focus, input);

        Console.WriteLine("1. ボタンから入力欄をクリックして、すぐ打つ (0・10・30 ms 後、各 10 回)");
        var results = new List<Result>();
        foreach (var delay in new[] { 0, 10, 30 })
        {
            for (var i = 0; i < 10; i++)
            {
                Settle(focus, button);
                results.Add(KeyAfterClick(focus, input, delay));
            }
        }
        Report(results);
        // 6 回流して 174/180 回 (直す前は 9/180 回)。取りこぼした 6 回は最初の 2 回の実行だけで、理由は分かっていない
        Expect(results.Count(r => r.Captured) >= 27, "30 回中 27 回以上、変換ボックスに入る");
        Expect(results.All(r => r.TotalMs <= 130), $"フックで止まった時間は最大 {results.Max(r => r.TotalMs)} ms (130 ms 以下)");

        Console.WriteLine("2. 続けて押した 2 打目は、調べ直さない");
        Settle(focus, button);
        KeyAfterClick(focus, input, 0);
        var second = focus.RecheckAfterClick(out var waited);
        Expect(!second && waited == -1, $"2 打目は待たない (waited={waited})");

        Console.WriteLine("3. 入力欄からボタンをクリックして、すぐ打つ (各 10 回)");
        results.Clear();
        foreach (var delay in new[] { 0, 10, 30 })
        {
            for (var i = 0; i < 10; i++)
            {
                Settle(focus, input);
                results.Add(KeyAfterClick(focus, button, delay));
            }
        }
        Report(results, "input-to-button");
        if (DiagnosticsEnabled)
            Expect(results.All(r => !r.Captured || r.NativeFocusAtKey == IntPtr.Zero || r.TargetHwnd == IntPtr.Zero ||
                r.NativeFocusAtKey != r.TargetHwnd),
                "ボタンにネイティブフォーカスが移った時点では、入力を横取りしない");
        Expect(results.All(r => !r.ByRecheck), "クリック直後の調べ直しで、ボタンを入力欄にしたことは無い");
        // 直す前からの動き: 調べ終わる前に打つと、クリックする前の判定 (入力欄) のまま変換ボックスに入れることがある (この変更では変わらない)
        Expect(Settled(focus, button) == false, "落ち着いた後、ボタンは入力欄ではない");

        Console.WriteLine("4. 入力欄からパスワード欄をクリックして、すぐ打つ (各 10 回)");
        results.Clear();
        foreach (var delay in new[] { 0, 10, 30 })
        {
            for (var i = 0; i < 10; i++)
            {
                Settle(focus, input);
                results.Add(KeyAfterClick(focus, password, delay));
            }
        }
        Report(results, "input-to-password");
        if (DiagnosticsEnabled)
            Expect(results.All(r => !r.Captured || r.NativeFocusAtKey == IntPtr.Zero || r.TargetHwnd == IntPtr.Zero ||
                r.NativeFocusAtKey != r.TargetHwnd),
                "パスワード欄にネイティブフォーカスが移った時点では、入力を横取りしない");
        Expect(results.All(r => !r.ByRecheck), "クリック直後の調べ直しで、パスワード欄を入力欄にしたことは無い");
        Expect(Settled(focus, password) == false, "落ち着いた後、パスワード欄は入力欄ではない");

        Console.WriteLine("5. 調べるのが遅い所 (フォーカスが来ると 300ms 止まる) をクリックして、すぐ打つ (5 回)");
        results.Clear();
        var seconds = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            Settle(focus, button);
            results.Add(KeyAfterClick(focus, slow, 0));
            var sw = Stopwatch.StartNew();
            _ = focus.CanCaptureWaiting() || focus.RecheckAfterClick(out _);
            seconds.Add(sw.ElapsedMilliseconds);
            Thread.Sleep(400);
        }
        Report(results);
        Expect(results.All(r => r.TotalMs <= 130), $"1 打目でフックが止まるのは最大 {results.Max(r => r.TotalMs)} ms (130 ms 以下)");
        Expect(seconds.All(ms => ms <= 100), $"2 打目は最大 {seconds.Max()} ms (クリック直後の待ちはしない。CanCaptureWaiting の 80 ms 以内)");

        Console.WriteLine("6. 入力欄の無いウィンドウ (ゲームの代わり) をクリックして、すぐ打つ (10 回)");
        results.Clear();
        Settle(focus, gameButton);
        for (var i = 0; i < 10; i++)
        {
            results.Add(KeyAfterClick(focus, gameButton, 0));
            Thread.Sleep(200);
        }
        Report(results);
        Expect(results.All(r => r.Waited == -1 && !r.Captured), "調べ直さず (待たず)、横取りもしない");
        Expect(results.All(r => r.TotalMs <= 5), $"フックで止まった時間は最大 {results.Max(r => r.TotalMs)} ms");
        return failures;
    }

    /// <summary>
    /// MeltypeEngine と同じ順にする: マウスのフックがボタンを押したのを受け取り (NoteClick・Invalidate)、アプリがクリックを処理し、
    /// keyDelayMs 後にキーを押した (CanCaptureWaiting → RecheckAfterClick)。
    /// </summary>
    private static Result KeyAfterClick(FocusInspector focus, Control target, int keyDelayMs)
    {
        var point = (Point)target.Invoke(() => target.PointToScreen(new Point(target.Width / 2, target.Height / 2)));
        // HWND の読み取りはクリック前に行う。診断による UI スレッド待ちを判定経路へ追加しない。
        var targetHwnd = DiagnosticsEnabled ? (IntPtr)target.Invoke(() => target.Handle) : IntPtr.Zero;
        Cursor.Position = point;
        focus.NoteClick();
        focus.Invalidate();
        Click();
        if (keyDelayMs > 0) Thread.Sleep(keyDelayMs);
        // UIA を直接呼ばずに Win32 のフォーカスだけを記録する。取得不能な場合は 0 として扱う。
        var atKey = DiagnosticsEnabled ? SnapshotNativeFocus() : default;
        var sw = Stopwatch.StartNew();
        var before = focus.CanCaptureWaiting();
        var waited = -1L;
        var byRecheck = !before && focus.RecheckAfterClick(out waited);
        if (before) focus.RecheckAfterClick(out _); // 使わなかったクリックを消しておく (次の回に残さない)
        var elapsed = sw.ElapsedMilliseconds;
        var afterCheck = DiagnosticsEnabled ? SnapshotNativeFocus() : default;
        return new Result(before || byRecheck, before, byRecheck, waited, elapsed, keyDelayMs, focus.LastInspected.Description,
            targetHwnd, atKey.Focus, afterCheck.Focus, atKey.Foreground, afterCheck.Foreground);
    }

    private static void Report(List<Result> results, string scenario = "")
    {
        Console.WriteLine($"     変換ボックスに入れた {results.Count(r => r.Captured)}/{results.Count} 回" +
            $" (直す前の判定だけなら {results.Count(r => r.CapturedBeforeFix)} 回、クリック直後の調べ直しで {results.Count(r => r.ByRecheck)} 回)、" +
            $"調べ直した {results.Count(r => r.Waited >= 0)} 回 (待った時間 最大 {results.Select(r => r.Waited).DefaultIfEmpty(0).Max()} ms)、" +
            $"フックで止まった時間 平均 {results.Average(r => r.TotalMs):0.0} ms・最大 {results.Max(r => r.TotalMs)} ms");
        foreach (var r in results.Where(r => !r.Captured && r.Waited >= 0).Take(10))
            Console.WriteLine($"       入れなかった: {r.DelayMs} ms 後のキー、待った {r.Waited} ms、判定 {r.Focus}");
        if (DiagnosticsEnabled && !string.IsNullOrEmpty(scenario))
        {
            // 入力を横取りすると判定した瞬間に、Win32 では既に目的のコントロールへ
            // フォーカスが移っていたかを区別する。HWND不明は一致とみなさない。
            foreach (var group in results.GroupBy(r => r.DelayMs).OrderBy(g => g.Key))
            {
                var captured = group.Where(r => r.Captured).ToArray();
                var confirmedAtKey = captured.Count(r => r.TargetHwnd != IntPtr.Zero && r.NativeFocusAtKey == r.TargetHwnd);
                var confirmedAfter = captured.Count(r => r.TargetHwnd != IntPtr.Zero && r.NativeFocusAfterCheck == r.TargetHwnd);
                var unknownAtKey = captured.Count(r => r.NativeFocusAtKey == IntPtr.Zero);
                Console.WriteLine($"     FOCUS_DIAG scenario={scenario} delay={group.Key}ms " +
                    $"captured={captured.Length}/{group.Count()} captured_with_target_focus_at_key={confirmedAtKey} " +
                    $"captured_with_target_focus_after_check={confirmedAfter} captured_with_unknown_focus_at_key={unknownAtKey}");
            }
            foreach (var r in results.Where(r => r.Captured))
                Console.WriteLine($"       FOCUS_TRACE scenario={scenario} delay={r.DelayMs}ms " +
                    $"target=0x{r.TargetHwnd.ToInt64():X} at_key=0x{r.NativeFocusAtKey.ToInt64():X} " +
                    $"after_check=0x{r.NativeFocusAfterCheck.ToInt64():X} " +
                    $"foreground_at_key=0x{r.ForegroundAtKey.ToInt64():X} " +
                    $"foreground_after_check=0x{r.ForegroundAfterCheck.ToInt64():X} " +
                    $"captured_before_recheck={r.CapturedBeforeFix} captured_by_recheck={r.ByRecheck} " +
                    $"last_published={r.Focus}");
        }
    }

    /// <summary>Win32 native focus only: no UIA calls, no input contents and no password values.</summary>
    private static (IntPtr Foreground, IntPtr Focus) SnapshotNativeFocus()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return (foreground, IntPtr.Zero);
        var thread = Native.GetWindowThreadProcessId(foreground, out _);
        var info = new Native.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        return thread != 0 && Native.GetGUIThreadInfo(thread, ref info)
            ? (foreground, info.hwndFocus)
            : (foreground, IntPtr.Zero);
    }

    /// <summary>target をクリックして、フォーカスを調べ終わるまで待つ。</summary>
    private static void Settle(FocusInspector focus, Control target) => Settled(focus, target);

    private static bool Settled(FocusInspector focus, Control target)
    {
        var point = (Point)target.Invoke(() => target.PointToScreen(new Point(target.Width / 2, target.Height / 2)));
        Cursor.Position = point;
        Click();
        Thread.Sleep(400);
        focus.Invalidate();
        Thread.Sleep(300);
        return focus.CanCapture;
    }

    private static void Click()
    {
        const uint LeftDown = 0x0002, LeftUp = 0x0004;
        var inputs = new[]
        {
            new Native.INPUT { type = 0, u = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = LeftDown } } },
            new Native.INPUT { type = 0, u = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = LeftUp } } },
        };
        Native.SendAll(inputs, "クリック");
    }
}
