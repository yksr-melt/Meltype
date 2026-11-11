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
/// Mac 版の IME (Swift, Input Method Kit) と Linux 版の IME (Python の IBus・C++ の fcitx5)、Android 版 (JNI) から呼ぶ C の関数。入力は meltype_ime_handle の 1 つで受ける。
/// NativeAOT で libMeltypeNative.dylib (mac/build.sh) / libMeltypeNative.so (linux/build.sh・android/tools/build-meltype-native.sh) にする。
///
/// 文字列はすべて UTF-8 の NUL 終端。こちらが返す文字列は meltype_free で解放する。
/// Swift 側が登録する関数 (漢字変換・候補・英単語の判定) が返す文字列は malloc したもので、こちらで free する。
/// Input Method Kit はメインスレッドから呼ぶので、排他はしない。漢字変換・候補の関数はワーカーのスレッドから呼ぶ (BackgroundCallbackConverter)。
/// </summary>
public static unsafe class Exports
{
    /// <summary>(ひらがな, 文脈 or NULL) → 「読み\t変換結果」を改行でつないだ文字列 (文節ごと)。変換できなければ NULL。</summary>
    private static delegate* unmanaged<byte*, byte*, byte*> s_clauses;

    /// <summary>読み → 候補を改行でつないだ文字列。</summary>
    private static delegate* unmanaged<byte*, byte*> s_candidates;

    /// <summary>小文字の英単語 → 英語として正しい綴りなら 1。</summary>
    private static delegate* unmanaged<byte*, int> s_isWord;

    /// <summary>Swift 側の関数を登録する (最初に 1 回)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_init")]
    public static int Init(delegate* unmanaged<byte*, byte*, byte*> clauses, delegate* unmanaged<byte*, byte*> candidates, delegate* unmanaged<byte*, int> isWord)
    {
        s_clauses = clauses;
        s_candidates = candidates;
        s_isWord = isWord;
        LoadSettings();
        return 1;
    }

    /// <summary>起動したときに config.json を読む (古い版の設定は、ここで移行して保存し直す。読めない設定もここでログに書く)。</summary>
    private static void LoadSettings() => _ = Runtime;

    /// <summary>設定・辞書・学習データ。Swift 側のスペルチェッカーを登録した後 (meltype_init) に作る。</summary>
    private static MeltypeRuntime Runtime => s_runtime ??= MeltypeRuntime.OpenDefault(s_isWord == null ? null : new CallbackWordChecker());

    private static MeltypeRuntime? s_runtime;

    /// <summary>
    /// 漢字変換を Mozc の変換ヘルパー (meltype_mozc_helper) で行う (Linux 版)。meltype_init の代わりに最初に 1 回呼ぶ。
    /// helper はヘルパーの実行ファイルのパス、profile は Mozc の学習データの保存先 (NULL ならデータの保存場所の mozc)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_init_mozc")]
    public static int InitMozc(byte* helper, byte* profile)
    {
        try
        {
            LoadSettings();
            var path = FromUtf8(helper);
            if (string.IsNullOrEmpty(path)) return 0;
            s_mozc?.Dispose();
            s_mozc = new MozcConverter(path, FromUtf8(profile) ?? Path.Combine(Config.AppPaths.DataDirectory, "mozc"));
            s_converter = new HybridConverter(() => Config.ConversionEngine.Mozc, s_mozc, null, null);
            if (!s_mozc.IsInstalled) return 0;
            s_mozc.WarmUp();
            return 1;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mozc を使えませんでした: {ex}");
            return 0;
        }
    }

    /// <summary>Mozc で変換するとき (Linux 版)。null なら登録された関数で変換する (Mac 版)。</summary>
    private static MozcConverter? s_mozc;

    /// <summary>
    /// 変換エンジンを入力のスレッドで待たず、結果が届いたら notify を呼ぶ。meltype_init / meltype_init_mozc の後、meltype_ime_create の前に 1 回呼ぶ。
    /// notify はワーカーのスレッドで呼ばれるので、フロントはメインループに移してから、フォーカスのある入力欄に refresh の出来事を送る。
    /// 呼ばないフロント (古いフロント・--self-test) では、結果を反映する経路が無いので、答えが出るまで待つ。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_conversion_ready")]
    public static void SetConversionReady(delegate* unmanaged<void> notify)
    {
        try
        {
            s_converter.Budget.Enable();
            s_converter.ResultReady += () => notify();
            if (s_mozc is null) s_callback.WarmUp();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"変換の結果の通知を登録できませんでした: {ex}");
        }
    }

    /// <summary>登録された関数で変換するとき (Mac 版)。入力欄がいくつあっても 1 つ (登録された関数を 1 つのワーカーからだけ呼ぶ)。</summary>
    private static readonly BackgroundCallbackConverter s_callback = new(new CallbackConverter().ConvertClauses, MoreCandidates);

    /// <summary>入力欄が使う変換エンジン。Windows 版と同じ HybridConverter で、Mac 版は登録された関数だけ、Linux 版は Mozc だけを組み合わせる。</summary>
    private static HybridConverter s_converter = new(() => Config.ConversionEngine.System, null, s_callback, s_callback.Candidates);

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
            Runtime.CreateSession(s_converter, settings, s_converter.Candidates, autoSpacing: s_mozc is null, directModeDetection: true);

        public string? ReadingOf(string text) => null;

        public SnippetStore Snippets => Runtime.Snippets;

        public string? ClipboardText => null;
    }

    /// <summary>データの保存場所 (設定・学習・ユーザー辞書)。meltype_free で解放する。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_data_directory")]
    public static byte* DataDirectory() => ToUtf8(Config.AppPaths.DataDirectory);

    /// <summary>
    /// Zenzai の設定 (Mac 版。issue #1130)。「Zenzai を使う」が OFF なら NULL、ON なら設定のモデルの場所
    /// (空文字列なら Meltype.app に同梱のモデルを使う)。meltype_free で解放する。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_zenzai_model")]
    public static byte* ZenzaiModel()
    {
        try
        {
            var settings = Runtime.Settings.Current;
            return settings.UseZenzai ? ToUtf8(settings.ZenzaiModelPath.Trim()) : null;
        }
        catch (Exception ex)
        {
            // 読めないときは使わない (打鍵のたびに推論が走るので、分からないまま ON にしない)
            Diagnostics.Log.Error($"Zenzai の設定を読めませんでした。Zenzai を使わずに変換します: {ex}");
            return null;
        }
    }

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
            return ToUtf8(Config.ProjectInfo.ReportUrl($"{name} (プレビュー版)", Config.ProjectInfo.CoreVersion, Config.ProjectInfo.Environment(settings, $"{Config.ProjectInfo.CoreVersion} ({name}、プレビュー版)", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})")));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"報告の URL を作れませんでした: {ex}");
            return ToUtf8($"{Config.ProjectInfo.SourceUrl}/issues/new/choose");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "meltype_free")]
    public static void Free(byte* text) => NativeMemory.Free(text);

    private static string[] MoreCandidates(string reading)
    {
        if (s_candidates == null) return [];
        var text = TakeUtf8(s_candidates(ToUtf8(reading, out var buffer)));
        NativeMemory.Free(buffer);
        return text is null ? [] : text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class CallbackConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) =>
            ConvertClauses(hiragana) is { Count: > 0 } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            if (s_clauses == null) return null;
            var input = ToUtf8(hiragana, out var inputBuffer);
            var contextPointer = context is null ? null : ToUtf8(context, out var contextBuffer);
            try
            {
                var text = TakeUtf8(s_clauses(input, contextPointer));
                if (text is null) return null;
                var clauses = new List<ConversionClause>();
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var tab = line.IndexOf('\t');
                    if (tab <= 0) return null;
                    clauses.Add(new ConversionClause(line[..tab], line[(tab + 1)..]));
                }
                // 読みをつなげると元のひらがなになること (Meltype の文節の扱いの前提)。
                return clauses.Count > 0 && string.Concat(clauses.Select(c => c.Reading)) == hiragana ? clauses : null;
            }
            finally
            {
                NativeMemory.Free(inputBuffer);
                if (contextPointer != null) NativeMemory.Free(contextPointer);
            }
        }
    }

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

    /// <summary>Swift 側が malloc した文字列を読んで解放する。</summary>
    private static string? TakeUtf8(byte* text)
    {
        if (text == null) return null;
        try
        {
            return Marshal.PtrToStringUTF8((IntPtr)text);
        }
        finally
        {
            NativeMemory.Free(text);
        }
    }

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
