// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>通常版とポータブル版でデータの保存先を切り替える。</summary>
internal static class AppPaths
{
    private const string PortableMarker = "Meltype.portable";

    /// <summary>app フォルダーにポータブル版のマーカーがあれば有効。</summary>
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    /// <summary>
    /// 設定・学習データ・辞書の保存場所。MELTYPE_DATA_DIR は開発時の上書きに使う。
    /// ポータブル版は app の隣、通常版は LocalAppData を使う。
    /// </summary>
    public static string DataDirectory { get; } = ResolveDataDirectory(
        IsPortable,
        Environment.GetEnvironmentVariable("MELTYPE_DATA_DIR"),
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string ResolveDataDirectory(bool isPortable, string? overrideDirectory, string appBaseDirectory, string localAppDataDirectory)
    {
        if (overrideDirectory is { Length: > 0 })
            return overrideDirectory;
        if (isPortable)
            return Path.Combine(appBaseDirectory, "data");
        return Path.Combine(localAppDataDirectory, "Meltype");
    }

    /// <summary>旧名 (AutoIME) のときの保存場所。</summary>
    private static string OldDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoIME");

    /// <summary>旧名 (AutoIME) の設定・学習データ・ユーザー辞書があれば、新しい保存場所に移す。</summary>
    public static void MigrateFromOldName()
    {
        if (IsPortable) return;
        try
        {
            if (Directory.Exists(DataDirectory) || !Directory.Exists(OldDataDirectory)) return;
            Directory.Move(OldDataDirectory, DataDirectory);
            var oldLog = Path.Combine(DataDirectory, "autoime.log");
            if (File.Exists(oldLog)) File.Move(oldLog, LogFile, overwrite: true);
            Diagnostics.Log.Info($"旧名 (AutoIME) の設定と学習データを {DataDirectory} に移しました。");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"旧名 (AutoIME) の設定と学習データを移せませんでした: {ex.Message}");
        }
    }

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");
    public static string ConversionHistoryFile => Path.Combine(DataDirectory, "conversions.json");
    public static string TranslationHistoryFile => Path.Combine(DataDirectory, "translations.json");
    public static string LanguageMemoryFile => Path.Combine(DataDirectory, "languages.json");
    public static string PhraseHistoryFile => Path.Combine(DataDirectory, "phrases.txt");
    public static string UserDictionaryFile => Path.Combine(DataDirectory, "userdict.txt");
    public static string SnippetsFile => Path.Combine(DataDirectory, "snippets.json");
    public static string KeyCountFile => Path.Combine(DataDirectory, "keycount.json");
    public static string LogFile => Path.Combine(DataDirectory, "meltype.log");
    /// <summary>落ちたときの例外 (ファイルへのログが OFF でも書く)。</summary>
    public static string CrashLogFile => Path.Combine(DataDirectory, "crash.log");
    /// <summary>ユーザー辞書 (japanese.txt / english.txt) を置くと組み込み辞書に追加される。</summary>
    public static string UserDictionaryDirectory => Path.Combine(DataDirectory, "dictionaries");
}
