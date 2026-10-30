// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;
using Meltype.Config;

namespace Meltype.Input;

/// <summary>前面アプリの情報 (アプリ別設定と、保留してよいかの判断に使う)。</summary>
public sealed record AppInfo(IntPtr Window, uint ProcessId, string ProcessName, bool? IsElevated, bool IsFullscreen, bool IsOwnProcess, bool LooksLikeGame = false)
{
    public static AppInfo None { get; } = new(IntPtr.Zero, 0, "", false, false, false);
}

/// <summary>打鍵を保留してよいかの判断。</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "打鍵を保留してよいかの結果を表す既存の公開名。セキュリティ権限の型ではない。")]
public readonly record struct CollectPermission(bool Allowed, string Reason)
{
    public static CollectPermission Allow { get; } = new(true, "");
    public static CollectPermission Deny(string reason) => new(false, reason);
}

/// <summary>
/// 前面ウィンドウごとにプロセス名・権限・全画面かどうかをキャッシュする。
/// フックのスレッドから呼ばれるので、取得は前面ウィンドウが変わったときの 1 回だけにする。
/// 全画面かどうかだけは、同じウィンドウのまま切り替わるゲームがあるので、少し間を空けて確かめ直す。
/// </summary>
internal sealed class ForegroundTracker
{
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;
    internal static readonly bool SelfElevated = QueryElevation(Process.GetCurrentProcess().Handle) ?? false;
    private readonly object _gate = new();
    private AppInfo _current = AppInfo.None;
    private long _checkedAt;
    private const long FullscreenRecheckMs = 500;

    /// <summary>
    /// Meltype 自身のウィンドウのうち、Meltype キーボードで入力してよいもの (ユーザー辞書・定型文・プロファイルの名前など、日本語を打つ画面)。
    /// ほかの自分のウィンドウ (設定画面など) では横取りしない。
    /// </summary>
    public static System.Collections.Concurrent.ConcurrentDictionary<IntPtr, bool> TypingAllowedWindows { get; } = new();

    /// <summary>入力してよい画面の中で、横取りしない欄 (英数のまま打つ定型文の名前・選ぶだけのドロップダウンなど)。</summary>
    public static System.Collections.Concurrent.ConcurrentDictionary<IntPtr, bool> DirectInputControls { get; } = new();

    /// <summary>
    /// Meltype 自身の画面 form でも、Meltype キーボードで日本語を打てるようにする (窓ができたら登録し、閉じたら外す)。
    /// direct の欄では横取りしない。
    /// </summary>
    public static void AllowTyping(Form form, params Control[] direct)
    {
        Track(form, TypingAllowedWindows);
        foreach (var control in direct) Track(control, DirectInputControls);
    }

    private static void Track(Control control, System.Collections.Concurrent.ConcurrentDictionary<IntPtr, bool> handles)
    {
        if (control.IsHandleCreated) handles[control.Handle] = true;
        control.HandleCreated += (_, _) => handles[control.Handle] = true;
        control.HandleDestroyed += (_, _) => handles.TryRemove(control.Handle, out _);
    }

    /// <summary>Meltype 自身のウィンドウで、入力してよい画面 (ユーザー辞書など) ではないもの。</summary>
    public static bool IsOwnWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        if (Native.GetWindowThreadProcessId(window, out var processId) == 0) return false;
        return processId == OwnProcessId && !TypingAllowedWindows.ContainsKey(window);
    }

    /// <summary>このアプリに SendInput の入力が届くか (管理者として動くアプリには、Meltype も管理者でなければ届かない: UIPI)。</summary>
    public static bool CanSendInputTo(AppInfo app) => app.IsElevated == false || SelfElevated;

    public AppInfo Current => CurrentFor(Native.GetForegroundWindow(), Environment.TickCount64);

    internal AppInfo CurrentFor(IntPtr window, long now)
    {
        AppInfo cached;
        long checkedAt;
        lock (_gate)
        {
            cached = _current;
            checkedAt = _checkedAt;
        }
        if (cached.Window != window) return Refresh(window, now);
        if (window == IntPtr.Zero || now - checkedAt < FullscreenRecheckMs) return cached;
        // 枠ありで起動して、同じウィンドウのまま全画面に切り替えるゲームがある
        var info = cached with { IsFullscreen = IsFullscreen(window) };
        lock (_gate)
        {
            if (ReferenceEquals(_current, cached))
            {
                _current = info;
                _checkedAt = now;
            }
        }
        return info;
    }

    public AppInfo Refresh(IntPtr window) => Refresh(window, Environment.TickCount64);

    private AppInfo Refresh(IntPtr window, long now)
    {
        var info = Describe(window);
        lock (_gate)
        {
            _current = info;
            _checkedAt = now;
        }
        return info;
    }

    /// <summary>このアプリで打鍵を保留してよいか。</summary>
    public CollectPermission Check(Settings settings)
    {
        if (!settings.Enabled) return CollectPermission.Deny("Meltype が無効");
        var app = Current;
        if (app.Window == IntPtr.Zero) return CollectPermission.Deny("前面ウィンドウがない");
        if (app.IsOwnProcess) return CollectPermission.Deny("Meltype 自身のウィンドウ");
        // 権限の高いアプリには SendInput が届かず (UIPI)、保留した入力を失うおそれがある。
        if (app.IsElevated != false && !SelfElevated) return CollectPermission.Deny($"{app.ProcessName} は管理者権限で動作中 (または権限を確認できない)");
        if (!settings.IsAppEnabled(app.ProcessName)) return CollectPermission.Deny($"{app.ProcessName} はアプリ別設定で OFF");
        if (settings.ExcludeFullscreen && app.IsFullscreen) return CollectPermission.Deny($"{app.ProcessName} は全画面表示");
        if (settings.IsGame(app.ProcessName, app.LooksLikeGame)) return CollectPermission.Deny($"{app.ProcessName} はゲーム");
        return CollectPermission.Allow;
    }

    private static AppInfo Describe(IntPtr window)
    {
        if (window == IntPtr.Zero) return AppInfo.None;
        if (Native.GetWindowThreadProcessId(window, out var processId) == 0) return AppInfo.None;
        if (processId == OwnProcessId) return new AppInfo(window, processId, "Meltype.exe", false, false, !TypingAllowedWindows.ContainsKey(window));

        var name = "";
        bool? elevated = null;
        var looksLikeGame = false;
        var process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process != IntPtr.Zero)
        {
            try
            {
                var buffer = new char[1024];
                var size = buffer.Length;
                if (Native.QueryFullProcessImageName(process, 0, buffer, ref size))
                {
                    var path = new string(buffer, 0, size);
                    name = Path.GetFileName(path);
                    looksLikeGame = LooksLikeGame(path);
                }
                elevated = QueryElevation(process);
            }
            finally
            {
                Native.CloseHandle(process);
            }
        }
        return new AppInfo(window, processId, name, elevated, IsFullscreen(window), false, looksLikeGame);
    }

    /// <summary>ゲームのストアのインストール先にある実行ファイルか (Steam・Epic・Riot・EA・Ubisoft・Battle.net・Xbox)。</summary>
    private static readonly string[] GameFolders =
    [
        @"\steamapps\common\", @"\Epic Games\", @"\Riot Games\", @"\EA Games\", @"\Electronic Arts\",
        @"\Ubisoft Game Launcher\games\", @"\Battle.net\", @"\XboxGames\", @"\GOG Galaxy\Games\", @"\Origin Games\",
        // 専用のランチャーから入れるゲーム (PSO2 NGS: …\PHANTASYSTARONLINE2_JP\pso2_bin\pso2.exe)
        @"\pso2_bin\",
        // Minecraft (Java 版): 公式ランチャーが入れた Java (…\.minecraft\runtime\…\javaw.exe) で動く (#1009)
        @"\.minecraft\runtime\", @"\Minecraft Launcher\runtime\", @"\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime\",
    ];

    /// <summary>そのプロセスの実行ファイルがゲームのフォルダーにあるか。調べられなければ false。</summary>
    public static bool LooksLikeGame(uint processId)
    {
        var process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return false;
        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return Native.QueryFullProcessImageName(process, 0, buffer, ref size) && LooksLikeGame(new string(buffer, 0, size));
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    public static bool LooksLikeGame(string path) =>
        GameFolders.Any(folder => path.Contains(folder, StringComparison.OrdinalIgnoreCase)) &&
        // ランチャー本体 (Steam のクライアントなど) は除く。ストアの画面の検索欄では Meltype を使える
        !Path.GetFileName(path).Equals("steam.exe", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains(@"\Launcher\", StringComparison.OrdinalIgnoreCase);

    private static bool? QueryElevation(IntPtr process)
    {
        if (!Native.OpenProcessToken(process, Native.TOKEN_QUERY, out var token)) return null;
        try
        {
            return Native.GetTokenInformation(token, Native.TokenElevation, out var elevation, sizeof(int), out _) ? elevation != 0 : null;
        }
        finally
        {
            Native.CloseHandle(token);
        }
    }

    private static bool IsFullscreen(IntPtr window)
    {
        if (window == Native.GetShellWindow() || window == Native.GetDesktopWindow()) return false;
        var className = new char[64];
        var length = Native.GetClassName(window, className, className.Length);
        if (length == 0 || new string(className, 0, length) is "Progman" or "WorkerW") return false;
        // タイトルバーのあるウィンドウは最大化されているだけ (タスクバー自動非表示だと画面全体を覆う)。
        const long WS_CAPTION = 0x00C00000;
        if ((Native.GetWindowLongPtr(window, Native.GWL_STYLE).ToInt64() & WS_CAPTION) == WS_CAPTION) return false;
        if (!Native.GetWindowRect(window, out var rect)) return false;
        var monitor = Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return false;
        return rect.Left <= info.rcMonitor.Left && rect.Top <= info.rcMonitor.Top &&
               rect.Right >= info.rcMonitor.Right && rect.Bottom >= info.rcMonitor.Bottom;
    }
}
