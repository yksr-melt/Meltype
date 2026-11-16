// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Meltype.Composition;
using Meltype.Detection;
using Meltype.Ime;
using Meltype.Input;

namespace Meltype.Mac;

/// <summary>
/// Mac 版の IME (Swift, Input Method Kit) と Linux 版の IME (Python の IBus・C++ の fcitx5) から呼ぶ C の関数。入力は meltype_ime_handle の 1 つで受ける。
/// NativeAOT で libMeltypeNative.dylib (mac/build.sh) / libMeltypeNative.so (linux/build.sh) にする。
///
/// 文字列はすべて UTF-8 の NUL 終端。こちらが返す文字列は meltype_free で解放する。
/// Input Method Kit はメインスレッドから呼ぶので、排他はしない。
/// </summary>
public static unsafe class Exports
{
    /// <summary>小文字の英単語 → 英語として正しい綴りなら 1 (Mac のスペルチェッカー)。</summary>
    private static delegate* unmanaged<byte*, int> s_isWord;

    /// <summary>
    /// 最初に 1 回呼ぶ。appDirectory は同梱の Mozc のヘルパー (mozc/meltype_mozc_helper) と Zenzai (zenzai/) を置いたフォルダー。
    /// isWord は英単語の判定 (NULL なら同梱の英単語の一覧)。Mozc のヘルパーがあれば 1、無ければ 0 (変換はかなのまま)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_init")]
    public static int Init(byte* appDirectory, delegate* unmanaged<byte*, int> isWord)
    {
        try
        {
            s_isWord = isWord;
            var directory = FromUtf8(appDirectory);
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("appDirectory が空です");
            s_converter = Runtime.CreateConverter(directory);
            if (!s_converter.MozcInstalled) return 0;
            s_converter.WarmUp();
            return 1;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"変換エンジンを用意できませんでした: {ex}");
            return 0;
        }
    }

    /// <summary>設定・辞書・学習データ。Swift 側のスペルチェッカーを登録した後 (meltype_init) に作る。</summary>
    private static MeltypeRuntime Runtime => s_runtime ??= MeltypeRuntime.OpenDefault(s_isWord == null ? null : new CallbackWordChecker());

    private static MeltypeRuntime? s_runtime;

    /// <summary>入力欄が使う変換エンジン (meltype_init で作る)。</summary>
    private static HybridConverter Converter => s_converter ?? throw new InvalidOperationException("meltype_init を先に呼んでください");

    private static HybridConverter? s_converter;

    /// <summary>
    /// 変換エンジンを入力のスレッドで待たず、結果が届いたら notify を呼ぶ。meltype_init の後、meltype_ime_create の前に 1 回呼ぶ。
    /// notify はワーカーのスレッドで呼ばれるので、フロントはメインループに移してから、フォーカスのある入力欄に refresh の出来事を送る。
    /// 呼ばないフロント (古いフロント・--self-test) では、結果を反映する経路が無いので、答えが出るまで待つ。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_conversion_ready")]
    public static void SetConversionReady(delegate* unmanaged<void> notify)
    {
        try
        {
            Converter.Budget.Enable();
            Converter.ResultReady += () => notify();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"変換の結果の通知を登録できませんでした: {ex}");
        }
    }

    /// <summary>
    /// 終了するときに 1 回呼ぶ。変換エンジンを止める (Zenzai の llama-server を終わらせ、Mozc の学習を保存する)。
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

    /// <summary>フロント 1 つ (IBus・fcitx5 のエンジン、Input Method Kit のコントローラー) の <see cref="ImeHost"/> を作る。meltype_ime_destroy で解放する。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_ime_create")]
    public static IntPtr ImeCreate() => GCHandle.ToIntPtr(GCHandle.Alloc(new ImeHost(new NativeEnvironment(), () => Runtime.Settings.Current)));

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

        public MeltypeSession CreateSession(Func<Config.Settings> settings) =>
            Runtime.CreateSession(Converter, settings, Converter.Candidates, autoSpacing: OperatingSystem.IsMacOS(), directModeDetection: true);

        public SnippetStore Snippets => Runtime.Snippets;

        public string? ClipboardText => null;
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
            return ToUtf8(Config.ProjectInfo.ReportUrl($"{name} (プレビュー版)", Config.ProjectInfo.CoreVersion, Config.ProjectInfo.Environment(settings, $"{Config.ProjectInfo.WithCommit(Config.ProjectInfo.CoreVersion)} ({name}、プレビュー版)", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})")));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"報告の URL を作れませんでした: {ex}");
            return ToUtf8($"{Config.ProjectInfo.SourceUrl}/issues/new/choose");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "meltype_free")]
    public static void Free(byte* text) => NativeMemory.Free(text);

    private sealed class CallbackWordChecker : IWordChecker
    {
        public bool IsAvailable => s_isWord != null;

        public bool IsWord(string lower)
        {
            if (s_isWord == null || lower.Length < 2) return false;
            var pointer = ToUtf8(lower, out var buffer);
            try
            {
                return s_isWord(pointer) != 0;
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }
    }

    // ---- UTF-8 の文字列 ----

    private static string? FromUtf8(byte* text) => text == null ? null : Marshal.PtrToStringUTF8((IntPtr)text);

    /// <summary>malloc した UTF-8 の文字列にする (呼び出し側が解放する)。</summary>
    private static byte* ToUtf8(string text) => ToUtf8(text, out _);

    private static byte* ToUtf8(string text, out byte* buffer)
    {
        var length = Encoding.UTF8.GetByteCount(text);
        buffer = (byte*)NativeMemory.Alloc((nuint)length + 1);
        fixed (char* chars = text) Encoding.UTF8.GetBytes(chars, text.Length, buffer, length);
        buffer[length] = 0;
        return buffer;
    }
}
