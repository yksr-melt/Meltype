// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Meltype.Engine;

/// <summary>
/// Mozc の変換ヘルパーとの 1 行ずつのやり取り (UTF-8。要求と応答の形は native/mozc/meltype_mozc_helper.cc の先頭)。
/// <see cref="MozcConverter"/> が 1 つずつ順に呼ぶ (同時には呼ばない)。
/// デスクトップは別プロセスのヘルパー (<see cref="MozcHelperProcess"/>)、Android はアプリのプロセスの Mozc を JNI のコールバックで呼ぶ (Meltype.Core.CApi)。
/// ヘルパーが動かない・止まった・答えないのは普通に起こるので、例外ではなく戻り値 (false・null) で返す。
/// </summary>
public interface IMozcHelper : IDisposable
{
    /// <summary>ヘルパーがあるか (動かせるかは使ってみるまで分からない)。</summary>
    bool IsInstalled { get; }

    /// <summary>動いていなければ動かす。動かせなければ false (理由はログに書く)。</summary>
    bool Start();

    /// <summary>1 行送って 1 行受け取る。動いていなければ動かしてから送る。動かせない・やり取りが切れた・応答が無ければ null。</summary>
    string? Send(string request);

    /// <summary>やり取りに失敗した。動いているヘルパーを止め、次の <see cref="Start"/> で動かし直す。</summary>
    void Reset();

    /// <summary>ヘルパーを起動した回数。起動し直したヘルパーは、前に送ったユーザー辞書を持っていない。</summary>
    int Launches { get; }

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

    /// <summary>
    /// ヘルパーのプロセスとのやり取りで、ヘルパーのせいで起きる例外: 起動できない (Win32Exception)・
    /// 終わったプロセスを触った (Process が InvalidOperationException を投げる)・パイプが切れた (IOException・ObjectDisposedException)。
    /// この型で捕まえるのは、Process とその標準入出力を呼ぶ所だけにする (InvalidOperationException はコードの誤りでも出るので、広い範囲を囲まない)。
    /// </summary>
    private static bool IsHelperGone(Exception ex) => ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or ObjectDisposedException;

    public bool IsInstalled => File.Exists(helperPath) && !_missingDependency;

    public int Launches { get; private set; }

    public bool Start() => Running() is not null;

    public string? Send(string request)
    {
        if (Running() is not { } process) return null;
        try
        {
            process.StandardInput.Write(request + "\n");
            process.StandardInput.Flush();
        }
        catch (Exception ex) when (IsHelperGone(ex))
        {
            // ヘルパーが終わっていて、パイプが切れていた
            Diagnostics.Log.Warn($"Mozc に送れませんでした: {ex.Message}");
            return null;
        }
        var line = ReadLine(process, TimeoutMs);
        if (line is null) Diagnostics.Log.Warn($"Mozc が応答しませんでした ({TimeoutMs}ms)。");
        return line;
    }

    public void Reset()
    {
        if (_process is not null) Stop(_process);
        _process = null;
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch (Exception ex) when (IsHelperGone(ex))
        {
            // 止めるあいだに終わった
        }
        process.Dispose();
    }

    public long WorkingSet
    {
        get
        {
            if (_process is not { HasExited: false } process) return 0;
            try
            {
                process.Refresh();
                return process.WorkingSet64;
            }
            catch (Exception ex) when (IsHelperGone(ex))
            {
                // 読むあいだに終わった
                return 0;
            }
        }
    }

    /// <summary>動いているヘルパー。動いていなければ起動して READY を待つ。起動できなければ null。</summary>
    private Process? Running()
    {
        if (_process is { HasExited: false } running) return running;
        _process?.Dispose();
        return _process = Launch();
    }

    private Process? Launch()
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
        if (profileDirectory is not null) info.ArgumentList.Add(profileDirectory);
        Process? process;
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
            if (profileDirectory is not null) Directory.CreateDirectory(profileDirectory);
            process = Process.Start(info);
        }
        catch (Exception ex) when (IsHelperGone(ex) || Config.DataFile.IsFileError(ex))
        {
            // 実行ファイルを動かせない (権限が無い・壊れている) か、学習データのフォルダを作れない
            Diagnostics.Log.Warn($"Mozc の変換ヘルパーを起動できませんでした: {ex.Message}");
            return null;
        }
        finally
        {
            if (OperatingSystem.IsWindows()) _ = SetErrorMode(previousMode);
        }
        if (process is null) return null;
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
            else Diagnostics.Log.Warn($"Mozc の変換ヘルパーが起動しませんでした: {ready ?? "応答なし"}");
            Stop(process);
            return null;
        }
        Diagnostics.Log.Info("Mozc の変換エンジンを起動しました。");
        Launches++;
        return process;
    }

    /// <summary>1 行読む。時間内に答えない・終わった・パイプが切れたら null。</summary>
    private static string? ReadLine(Process process, int timeoutMs)
    {
        var task = process.StandardOutput.ReadLineAsync();
        try
        {
            return task.Wait(timeoutMs) ? task.Result : null;
        }
        catch (AggregateException ex) when (ex.InnerException is { } inner && IsHelperGone(inner))
        {
            return null;
        }
    }

    /// <summary>終わらせる (Q)。</summary>
    public void Dispose()
    {
        if (_process is not { } process) return;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Write("Q\n");
                process.StandardInput.Flush();
                process.WaitForExit(1000);
            }
        }
        catch (Exception ex) when (IsHelperGone(ex))
        {
            // もう終わっていた・パイプが切れていた
        }
        Stop(process);
        _process = null;
    }
}
