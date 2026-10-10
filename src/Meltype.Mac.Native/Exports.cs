// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Meltype.Composition;
using Meltype.Detection;

namespace Meltype.Mac;

/// <summary>
/// Mac 版の IME (Swift, Input Method Kit) と Linux 版の IME (Python, IBus) から呼ぶ C の関数。
/// NativeAOT で libMeltypeNative.dylib (mac/build.sh) / libMeltypeNative.so (linux/build.sh) にする。
///
/// 文字列はすべて UTF-8 の NUL 終端。こちらが返す文字列は meltype_free で解放する。
/// Swift 側が登録する関数 (漢字変換・候補・英単語の判定) が返す文字列は malloc したもので、こちらで free する。
/// Input Method Kit はメインスレッドから呼ぶので、排他はしない。
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
        return 1;
    }

    /// <summary>
    /// 漢字変換を Mozc の変換ヘルパー (meltype_mozc_helper) で行う (Linux 版)。meltype_init の代わりに最初に 1 回呼ぶ。
    /// helper はヘルパーの実行ファイルのパス、profile は Mozc の学習データの保存先 (NULL ならデータの保存場所の mozc)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_init_mozc")]
    public static int InitMozc(byte* helper, byte* profile)
    {
        try
        {
            var path = FromUtf8(helper);
            if (string.IsNullOrEmpty(path)) return 0;
            s_mozc?.Dispose();
            s_mozc = new MozcConverter(path, FromUtf8(profile) ?? Path.Combine(Config.AppPaths.DataDirectory, "mozc"));
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

    /// <summary>入力欄 (Input Method Kit のクライアント) ごとの入力の本体を作る。失敗したら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_create")]
    public static IntPtr Create()
    {
        try
        {
            var session = s_mozc is { } mozc
                ? MeltypeSession.CreateDefault(mozc, mozc.Candidates, s_isWord == null ? null : new CallbackWordChecker())
                : MeltypeSession.CreateDefault(new CallbackConverter(), MoreCandidates, s_isWord == null ? null : new CallbackWordChecker(), autoSpacing: true);
            return GCHandle.ToIntPtr(GCHandle.Alloc(session));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 初期化できませんでした: {ex}");
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "meltype_destroy")]
    public static void Destroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero) GCHandle.FromIntPtr(handle).Free();
    }

    /// <summary>
    /// キーを 1 つ処理して、結果を JSON で返す (<see cref="SessionResult.ToJson"/>)。
    /// vk は Windows の仮想キーコード、ch は入力する Unicode スカラー (無ければ 0)、
    /// modifiers は Shift = 1, Control = 2, Option = 4, Command = 8。before / after はキャレットの前後の文字列 (NULL 可)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_handle_key")]
    public static byte* HandleKey(IntPtr handle, int vk, int ch, int modifiers, byte* before, byte* after)
    {
        return Run(handle, session =>
        {
            // 文字を伴わない・扱えない入力。不正な Unicode (負数・単独サロゲート・上限超過) は
            // 文字列を作らず、状態も変えずにアプリへそのまま渡す。
            if (IsInvalidScalar(ch)) return session.PassThrough();
            // 結合文字は、未確定表示に取り込まず直前の内容を確定して元のイベントを通す。
            // U+0300..036F の Latin アクセントのみ ASCII 英字の原文を優先する。
            // 濁点・異体字セレクターでは、日本語の表示や明示的な候補をそのまま確定する。
            if (Rune.GetUnicodeCategory(new Rune(ch)) is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                return session.CommitCombiningForPassThrough(preserveLatinRaw: ch is >= 0x0300 and <= 0x036F);
            // 補助面の有効なスカラー (U+10000..U+10FFFF) は char (UTF-16 1 コードユニット) へ切り詰められない。
            // 未確定の内容をここで確定してから、元の OS イベントを 1 回だけアプリへ通す (Consumed = false)。
            if (ch > 0xFFFF) return session.CommitForPassThrough();
            return session.HandleKey(vk, ch > 0 ? (char)ch : null,
                (modifiers & 1) != 0, (modifiers & 2) != 0, (modifiers & 4) != 0, (modifiers & 8) != 0, FromUtf8(before), FromUtf8(after));
        });
    }

    /// <summary>
    /// 変換・無変換・ひらがな/カタカナ キー (vk 0x1C / 0x1D / 0x15)。設定「入力中のキーの役割を分ける」が ON で入力中なら、
    /// 変換ボックスで処理して結果を返す (issue #199)。そうでなければ NULL (今までどおり、英数 / 日本語の切り替えに使う)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_role_key")]
    public static byte* RoleKey(IntPtr handle, int vk)
    {
        try
        {
            if (handle == IntPtr.Zero || GCHandle.FromIntPtr(handle).Target is not MeltypeSession session) return null;
            return session.HandleRoleKey(vk) is { } result ? ToUtf8(result.ToJson()) : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"入力の処理で例外: {ex}");
            return null;
        }
    }

    /// <summary>不正な文字の引数か。0 は「文字を伴わないキー」なので不正ではない。</summary>
    private static bool IsInvalidScalar(int ch) =>
        ch < 0 || ch > 0x10FFFF || (ch >= 0xD800 && ch <= 0xDFFF);

    /// <summary>未確定の内容を確定する (フォーカスが外れたときなど)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_commit")]
    public static byte* Commit(IntPtr handle) => Run(handle, session => session.CommitPending());

    /// <summary>候補ウィンドウで候補を選んだ。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_select_candidate")]
    public static byte* SelectCandidate(IntPtr handle, int index) => Run(handle, session => session.SelectCandidate(index));

    /// <summary>英数 (直接入力) にするか (1) 日本語にするか (0)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_direct")]
    public static void SetDirect(IntPtr handle, int direct)
    {
        if (handle != IntPtr.Zero && GCHandle.FromIntPtr(handle).Target is MeltypeSession session) session.Direct = direct != 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "meltype_set_code_input")]
    public static void SetCodeInput(IntPtr handle, int enabled)
    {
        if (handle != IntPtr.Zero && GCHandle.FromIntPtr(handle).Target is MeltypeSession session) session.CodeInput = enabled != 0;
    }

    /// <summary>入力欄が確定済みの文字の削除に対応しているか (1) いないか (0)。対応していなければ、確定し直しをしない。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_can_delete")]
    public static void SetCanDelete(IntPtr handle, int canDelete)
    {
        if (handle != IntPtr.Zero && GCHandle.FromIntPtr(handle).Target is MeltypeSession session) session.CanDeleteSurrounding = canDelete != 0;
    }

    /// <summary>データの保存場所 (設定・学習・ユーザー辞書)。meltype_free で解放する。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_data_directory")]
    public static byte* DataDirectory() => ToUtf8(Config.AppPaths.DataDirectory);

    /// <summary>
    /// 不具合報告を開く URL (OS・版・実行環境を入れたもの)。platform は "Mac" か "Linux"。meltype_free で解放する。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_report_url")]
    public static byte* ReportUrl(byte* platform)
    {
        try
        {
            var name = FromUtf8(platform) ?? "Mac";
            var settings = Config.Settings.Load(Config.AppPaths.ConfigFile);
            return ToUtf8(Config.ProjectInfo.ReportUrl($"{name} (プレビュー版)", Config.ProjectInfo.CoreVersion, Config.ProjectInfo.Environment(settings, name)));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"報告の URL を作れませんでした: {ex}");
            return ToUtf8($"{Config.ProjectInfo.SourceUrl}/issues/new/choose");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "meltype_free")]
    public static void Free(byte* text) => NativeMemory.Free(text);

    private static byte* Run(IntPtr handle, Func<MeltypeSession, SessionResult> action)
    {
        try
        {
            if (handle == IntPtr.Zero || GCHandle.FromIntPtr(handle).Target is not MeltypeSession session) return null;
            return ToUtf8(action(session).ToJson());
        }
        catch (Exception ex)
        {
            // 例外を Swift 側へ投げると IME ごと落ちるので、ここで止めてキーはアプリに渡す (NULL)。
            Diagnostics.Log.Error($"Mac: 入力の処理で例外: {ex}");
            return null;
        }
    }

    private static IReadOnlyList<string> MoreCandidates(string reading)
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
