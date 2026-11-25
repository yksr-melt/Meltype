// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Ime;

namespace Meltype.CApi;

/// <summary>
/// Mac 版の IME (Swift, Input Method Kit)、Linux 版の IME (Python の IBus・C++ の fcitx5)、Android 版 (JNI) から呼ぶ C の関数。入力は meltype_ime_handle の 1 つで受ける。
/// NativeAOT で libMeltypeNative.dylib (mac/build.sh) / libMeltypeNative.so (native/build-core-linux.sh) にする。
///
/// 文字列はすべて UTF-8 の NUL 終端。こちらが返す文字列は meltype_free で解放する。
/// Input Method Kit はメインスレッドから呼ぶので、排他はしない。
/// 例外は C の側に投げられない (投げるとアプリごと落ちる) ので、どの関数も一番外ですべて捕まえ、中身 (場所) を Log.Error に書いて失敗の値を返す。
/// </summary>
public static unsafe class Exports
{
    /// <summary>
    /// 最初に 1 回呼ぶ。appDirectory は同梱の Mozc のヘルパー (mozc/meltype_mozc_helper) と Zenzai (zenzai/) を置いたフォルダー。
    /// Mozc のヘルパーがあれば 1、無ければ 0 (変換はかなのまま)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_init")]
    public static int Init(byte* appDirectory)
    {
        try
        {
            var directory = FromUtf8(appDirectory);
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("appDirectory が空です");
            return Runtime.StartConverter(directory) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"変換エンジンを用意できませんでした: {ex}");
            return 0;
        }
    }

    /// <summary>設定・辞書・学習データ (最初に使うときに作る)。</summary>
    private static MeltypeRuntime Runtime => s_runtime ??= MeltypeRuntime.OpenDefault();

    private static MeltypeRuntime? s_runtime;

    // meltype_set_conversion_ready を呼んだ (変換エンジンの答えを後から受け取れる) フロントか
    private static bool s_lateResults;

    /// <summary>
    /// Android 版の meltype_init。Mozc はアプリのプロセスで動くので、変換ヘルパーの要求 1 行 (native/mozc/meltype_mozc_helper.cc の先頭) を
    /// mozcRequest に渡し、答えの 1 行を受け取る。mozcRequest は変換のスレッドから呼ばれ、malloc した UTF-8 (失敗なら NULL) を返す。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_android_init")]
    public static int AndroidInit(delegate* unmanaged<byte*, byte*> mozcRequest)
    {
        try
        {
            if (mozcRequest == null) return 0;
            // llama-server は同梱していないので、Zenzai は使えない (変換は Mozc だけ)
            Runtime.StartConverter(AppContext.BaseDirectory, new CallbackMozcHelper(mozcRequest));
            return 1;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Android の Mozc 変換を登録できませんでした: {ex}");
            return 0;
        }
    }

    /// <summary>
    /// 変換エンジンを入力のスレッドで待たず、結果が届いたら notify を呼ぶ。meltype_init (Android は meltype_android_init) の後、meltype_ime_create の前に 1 回呼ぶ。
    /// notify はワーカーのスレッドで呼ばれるので、フロントはメインループに移してから、フォーカスのある入力欄に refresh の出来事を送る。
    /// 呼ばないフロント (古いフロント・--self-test) では、結果を反映する経路が無いので、答えが出るまで待つ。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_conversion_ready")]
    public static void SetConversionReady(delegate* unmanaged<void> notify)
    {
        try
        {
            s_lateResults = true;
            Runtime.ConversionReady += () => notify();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"変換の結果の通知を登録できませんでした: {ex}");
        }
    }

    /// <summary>
    /// 終了するときに 1 回呼ぶ。変換エンジンを止める (Zenzai の llama-server を含む変換エンジンを止める)。
    /// 呼ばずに終わると llama-server が残り、次に起動した Meltype が終わらせるまで動き続ける。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_shutdown")]
    public static void Shutdown()
    {
        // ログアウトでは、終了の通知と SIGTERM の両方から呼ばれることがある
        if (Interlocked.Exchange(ref s_shutDown, 1) == 1) return;
        try
        {
            s_runtime?.Dispose();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"変換エンジンを止められませんでした: {ex}");
        }
    }

    private static int s_shutDown;

    /// <summary>フロント 1 つ (IBus・fcitx5 のエンジン、Input Method Kit のコントローラー) の <see cref="ImeHost"/> を作る。meltype_ime_destroy で解放する。作れなければ NULL (meltype_ime_handle は NULL に何も返さない)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_ime_create")]
    public static IntPtr ImeCreate()
    {
        try
        {
            return GCHandle.ToIntPtr(GCHandle.Alloc(Runtime.CreateHost(new NativeEnvironment(), () => Runtime.Settings.Current, () => s_lateResults)));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"入力の本体を作れませんでした: {ex}");
            return IntPtr.Zero;
        }
    }

    /// <summary>出来事 (<see cref="ImeEvent"/> の JSON) を処理して、<see cref="ImeOutput"/> の JSON を返す。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_ime_handle")]
    public static byte* ImeHandle(IntPtr handle, byte* json)
    {
        try
        {
            if (handle == IntPtr.Zero || GCHandle.FromIntPtr(handle).Target is not ImeHost host) return null;
            return ToUtf8(host.Handle(FromUtf8(json) ?? ""));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"入力の処理で例外: {ex}");
            return null;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "meltype_ime_destroy")]
    public static void ImeDestroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero) GCHandle.FromIntPtr(handle).Free();
    }

    /// <summary>Mac・Linux の環境。設定は config.json、コードの入力欄はフロントが知らせるターミナルの印で決める。</summary>
    private sealed class NativeEnvironment : IImeEnvironment
    {
        public Config.InputMode? InputMethod => null;

        public SnippetStore Snippets => Runtime.Snippets;

        public string? ClipboardText => null;

        public string? OtherTipVersion => null;
    }

    /// <summary>データの保存場所 (設定・学習・ユーザー辞書)。meltype_free で解放する。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_data_directory")]
    public static byte* DataDirectory() => ToUtf8(Config.AppPaths.DataDirectory);

    /// <summary>Meltype.Core の版 (csproj の Version)。meltype_free で解放する。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_version")]
    public static byte* Version() => ToUtf8(Config.ProjectInfo.CoreVersion);

    /// <summary>
    /// 不具合報告を開く URL (OS・版・実行環境を入れたもの)。platform は "Mac" か "Linux"。meltype_free で解放する。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_report_url")]
    public static byte* ReportUrl(byte* platform)
    {
        try
        {
            var name = FromUtf8(platform) ?? "Mac";
            var settings = Runtime.Settings.Current;
            return ToUtf8(Config.ProjectInfo.GitHubReportUrl("1-bug.yml", $"{name} (プレビュー版)", Config.ProjectInfo.CoreVersion, Config.ProjectInfo.Environment(settings, $"{Config.ProjectInfo.WithCommit(Config.ProjectInfo.CoreVersion)} ({name}、プレビュー版)", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})")));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"報告の URL を作れませんでした: {ex}");
            return ToUtf8($"{Config.ProjectInfo.SourceUrl}/issues/new/choose");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "meltype_free")]
    public static void Free(byte* text) => NativeMemory.Free(text);

    /// <summary>Android: アプリのプロセスの Mozc。動かすのも止めるのもアプリなので、Start・Reset・Dispose では何もしない。</summary>
    private sealed class CallbackMozcHelper(delegate* unmanaged<byte*, byte*> request) : IMozcHelper
    {
        public bool IsInstalled => true;

        // アプリの Mozc はユーザー辞書をファイルに保存している
        public int Launches => 1;

        public long WorkingSet => 0;

        public bool Start() => true;

        public string? Send(string line)
        {
            var input = ToUtf8(line);
            try
            {
                return TakeUtf8(request(input));
            }
            finally
            {
                NativeMemory.Free(input);
            }
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private static string? TakeUtf8(byte* text)
    {
        if (text == null) return null;
        try { return Marshal.PtrToStringUTF8((IntPtr)text); }
        finally { NativeMemory.Free(text); }
    }

    // ---- UTF-8 の文字列 ----

    private static string? FromUtf8(byte* text) => text == null ? null : Marshal.PtrToStringUTF8((IntPtr)text);

    /// <summary>malloc した UTF-8 の文字列にする (呼び出し側が解放する)。</summary>
    private static byte* ToUtf8(string text)
    {
        var length = Encoding.UTF8.GetByteCount(text);
        var buffer = (byte*)NativeMemory.Alloc((nuint)length + 1);
        fixed (char* chars = text) Encoding.UTF8.GetBytes(chars, text.Length, buffer, length);
        buffer[length] = 0;
        return buffer;
    }
}
