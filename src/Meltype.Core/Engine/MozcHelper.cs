// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Meltype.Engine;

/// <summary>
/// Mozc の変換ヘルパーとの 1 行ずつのやり取り (UTF-8。要求と応答の形は native/mozc/meltype_mozc_helper.cc の先頭)。
/// <see cref="MozcConverter"/> が 1 つずつ順に呼ぶ (同時には呼ばない)。
/// デスクトップは別プロセスのヘルパー (<see cref="MozcHelperProcess"/>)、Android はアプリのプロセスの Mozc を JNI のコールバックで呼ぶ (Meltype.Core.CApi)。
/// </summary>
public interface IMozcHelper : IDisposable
{
    /// <summary>ヘルパーがあるか (動かせるかは使ってみるまで分からない)。</summary>
    bool IsInstalled { get; }

    /// <summary>動いていなければ動かす。動かせなければ例外。</summary>
    void Start();

    /// <summary>1 行送って 1 行受け取る。動いていなければ動かしてから送る。応答が無ければ null。</summary>
    string? Send(string request);

    /// <summary>やり取りに失敗した。動いているヘルパーを止め、次の <see cref="Start"/> で動かし直す。</summary>
    void Reset();

    /// <summary>ヘルパーが使っているメモリ (計測用。分からなければ 0)。</summary>
    long WorkingSet { get; }
}

/// <summary>同梱の変換ヘルパー (meltype_mozc_helper) を別のプロセスで動かし、標準入出力でやり取りする。</summary>
/// <param name="helperPath">meltype_mozc_helper の実行ファイル。</param>
/// <param name="profileDirectory">Mozc の学習データの保存先 (null なら Mozc の既定)。</param>
internal sealed class MozcHelperProcess(string helperPath, string? profileDirectory) : IMozcHelper
{
    private const int TimeoutMs = 1500;
    private const int StartTimeoutMs = 10000;

    private Process? _process;

    // 要る DLL が無くて起動できない (Visual C++ のランタイムが無いなど)。起動し直しても同じなので、もう試さない
    private volatile bool _missingDependency;

    // Windows: 子プロセスを起動するあいだ、DLL が見つからないときのエラーの画面を出さない (子プロセスはこの設定を引き継ぐ)。
    // 子プロセスが引き継ぐのはプロセス全体の設定だけなので、SetThreadErrorMode では効かない。起動したらすぐ戻す
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();

    // STATUS_DLL_NOT_FOUND / STATUS_ENTRYPOINT_NOT_FOUND / STATUS_INVALID_IMAGE_FORMAT (起動のときに DLL を読み込めなかった)
    private static bool IsLoaderFailure(int exitCode) => exitCode is unchecked((int)0xC0000135) or unchecked((int)0xC0000139) or unchecked((int)0xC000007B);

    public bool IsInstalled => File.Exists(helperPath) && !_missingDependency;

    public void Start() => Running();

    public string? Send(string request)
    {
        var process = Running();
        process.StandardInput.Write(request + "\n");
        process.StandardInput.Flush();
        var line = ReadLine(process, TimeoutMs);
        if (line is null) Diagnostics.Log.Warn($"Mozc が応答しませんでした ({TimeoutMs}ms)。");
        return line;
    }

    public void Reset()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill();
        }
        catch
        {
        }
        _process?.Dispose();
        _process = null;
    }

    public long WorkingSet
    {
        get
        {
            if (_process is not { HasExited: false } process) return 0;
            process.Refresh();
            return process.WorkingSet64;
        }
    }

    /// <summary>動いているヘルパー。動いていなければ起動して READY を待つ。</summary>
    private Process Running()
    {
        if (_process is { HasExited: false } running) return running;
        _process?.Dispose();
        return _process = Launch();
    }

    private Process Launch()
    {
        var info = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(helperPath) ?? "",
        };
        if (profileDirectory is not null)
        {
            Directory.CreateDirectory(profileDirectory);
            info.ArgumentList.Add(profileDirectory);
        }
        Process process;
        // 今の設定に足す (GetErrorMode で読む。SetErrorMode(0) で読むと、一瞬プロセス全体の設定が 0 になる)
        var previousMode = 0u;
        if (OperatingSystem.IsWindows())
        {
            previousMode = GetErrorMode();
            // 戻り値は以前のモード (エラーコードではない)。復元には直前に読んだモードを使う。
            _ = SetErrorMode(previousMode | SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);
        }
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("起動できませんでした");
        }
        finally
        {
            if (OperatingSystem.IsWindows()) _ = SetErrorMode(previousMode);
        }
        // Mozc のログ (標準エラー) は読み捨てる (溜まるとヘルパーが止まる)。
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        var ready = ReadLine(process, StartTimeoutMs);
        if (ready != "READY")
        {
            if (process.WaitForExit(1000) && IsLoaderFailure(process.ExitCode))
            {
                _missingDependency = true;
                Diagnostics.Log.Warn($"Mozc の変換ヘルパーを起動できません (DLL を読み込めない: 0x{process.ExitCode:X8}。同梱の Visual C++ のランタイムが無いか壊れている)。漢字に変換できません。");
            }
            try { process.Kill(); } catch { }
            process.Dispose();
            throw new InvalidOperationException($"起動に失敗しました: {ready ?? "応答なし"}");
        }
        Diagnostics.Log.Info("Mozc の変換エンジンを起動しました。");
        return process;
    }

    private static string? ReadLine(Process process, int timeoutMs)
    {
        var task = process.StandardOutput.ReadLineAsync();
        return task.Wait(timeoutMs) ? task.Result : null;
    }

    /// <summary>保存して終わらせる (Q)。</summary>
    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false } process)
            {
                process.StandardInput.Write("Q\n");
                process.StandardInput.Flush();
                if (!process.WaitForExit(1000)) process.Kill();
            }
        }
        catch
        {
        }
        _process?.Dispose();
        _process = null;
    }
}
