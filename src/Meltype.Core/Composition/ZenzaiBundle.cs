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

    /// <summary>同梱のモデルのファイル名 (native/zenzai/Get-Zenzai.ps1 が置く名前)。</summary>
    public const string ModelFileName = "zenz-v3.2-small-Q5_K_M.gguf";

    /// <summary>設定の llama-server の場所。空なら同梱のもの。</summary>
    public static string ServerPath(string configured, string? baseDirectory = null) =>
        Resolve(configured, ServerFileName, baseDirectory);

    /// <summary>設定のモデルの場所。空なら同梱のもの。</summary>
    public static string ModelPath(string configured, string? baseDirectory = null) =>
        Resolve(configured, ModelFileName, baseDirectory);

    private static string Resolve(string configured, string fileName, string? baseDirectory)
    {
        var path = configured?.Trim() ?? "";
        if (path.Length > 0) return path;
        return Path.Combine(baseDirectory ?? AppContext.BaseDirectory, FolderName, fileName);
    }
}
