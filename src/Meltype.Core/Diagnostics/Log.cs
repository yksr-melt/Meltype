// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Collections.Concurrent;
using System.Text;

namespace Meltype.Diagnostics;

public enum LogLevel { Info, Decision, Warn, Error }

public readonly record struct LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public override string ToString() => $"{Time:HH:mm:ss.fff} [{Level}] {Message}";
}

/// <summary>
/// キーボードフックのスレッドからも呼ばれるので、呼び出し側では I/O をしない。
/// ファイル出力はバックグラウンドのタイマーでまとめて書く。
/// </summary>
public static class Log
{
    private const int Capacity = 1000;
    private static readonly object Gate = new();
    private static readonly LogEntry[] Ring = new LogEntry[Capacity];
    private static readonly ConcurrentQueue<LogEntry> FileQueue = new();
    private static int _next;
    private static int _count;
    private static long _version;
    private static string? _filePath;
    private static System.Threading.Timer? _fileTimer;

    // 最初の DateTime.Now はタイムゾーンを読み込むので重い (Linux で約 160ms)。入力中に初めてログを書くときに待たせない
    private static readonly bool TimeZoneRequested = ThreadPool.QueueUserWorkItem(_ => _ = DateTime.Now);

    public static long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// 入力した文字 (確定した文字列・打った英字・読み・直した語) をログに残すか (設定「ログに入力した文字を残す」、既定は残さない)。
    /// 残さないときは文字数だけを残す。判定の理由に出る、判定した語の先頭の数文字はこれに関係なく残る。
    /// </summary>
    public static bool RecordText { get; set; }

    /// <summary>入力した文字をログに出すときに使う。RecordText が false なら「(n 文字)」にする。</summary>
    public static string Text(string text) => RecordText ? $"「{text}」" : $"({text.Length} 文字)";

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Decision(string message) => Write(LogLevel.Decision, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (Gate)
        {
            Ring[_next] = entry;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
        Interlocked.Increment(ref _version);
        if (Volatile.Read(ref _filePath) is not null) FileQueue.Enqueue(entry);
    }

    public static LogEntry[] Snapshot()
    {
        lock (Gate)
        {
            var result = new LogEntry[_count];
            var start = (_next - _count + Capacity) % Capacity;
            for (var i = 0; i < _count; i++) result[i] = Ring[(start + i) % Capacity];
            return result;
        }
    }

    /// <summary>null を渡すとファイル出力を止める。</summary>
    public static void SetFileOutput(string? path)
    {
        if (path is null)
        {
            // 書き出す先を消す前に、まだ書いていない分を書く (先に消すと捨てられてしまう)。
            FlushFile();
            Volatile.Write(ref _filePath, null);
            _fileTimer?.Dispose();
            _fileTimer = null;
            return;
        }
        Volatile.Write(ref _filePath, path);
        _fileTimer ??= new System.Threading.Timer(_ => FlushFile(), null, 1000, 1000);
    }

    public static void FlushFile()
    {
        var path = Volatile.Read(ref _filePath);
        if (FileQueue.IsEmpty) return;
        var builder = new StringBuilder();
        while (FileQueue.TryDequeue(out var entry)) builder.AppendLine(entry.ToString());
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024) File.Move(path, path + ".old", overwrite: true);
            File.AppendAllText(path, builder.ToString());
        }
        catch
        {
            // ログの失敗で入力処理を止めない。
        }
    }
}
