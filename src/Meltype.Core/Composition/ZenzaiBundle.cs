// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 配布物に同梱した Zenzai (llama-server と zenz のモデル) の場所 (issue #1120)。
/// 設定の場所が空なら、アプリのフォルダーの <c>zenzai\</c> にあるものを使う。
/// 自分で用意したものを使いたい人は、設定に場所を書けば同梱のものより優先する。
/// </summary>
public static class ZenzaiBundle
{
    /// <summary>同梱のフォルダーの名前 (アプリのフォルダーの中)。</summary>
    public const string FolderName = "zenzai";

    /// <summary>同梱の llama-server のファイル名。</summary>
    public static string ServerFileName => OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";


    /// <summary>設定の llama-server の場所。空なら同梱のもの。</summary>
    public static string ServerPath(string configured, string? baseDirectory = null) =>
        Resolve(configured, ServerFileName, baseDirectory);

    /// <summary>
    /// 設定のモデルの場所。空なら同梱の zenzai\ にある .gguf (名前は見ない: 版とファイル名は native/zenzai/zenzai.env だけに書く)。
    /// 同梱のモデルが無ければ、zenzai\ の中の存在しないファイルの場所 (ZenzaiConverter が「見つからない」と答える)。
    /// </summary>
    public static string ModelPath(string configured, string? baseDirectory = null)
    {
        var path = configured?.Trim() ?? "";
        if (path.Length > 0) return path;
        var folder = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, FolderName);
        try
        {
            if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.gguf").Order(StringComparer.Ordinal).FirstOrDefault() is { } bundled) return bundled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return Path.Combine(folder, "model.gguf");
    }

    private static string Resolve(string configured, string fileName, string? baseDirectory)
    {
        var path = configured?.Trim() ?? "";
        if (path.Length > 0) return path;
        return Path.Combine(baseDirectory ?? AppContext.BaseDirectory, FolderName, fileName);
    }
}
