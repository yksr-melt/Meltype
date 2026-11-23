// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using System.Xml;

namespace Meltype.Config;

/// <summary>
/// 設定・学習データのファイルの読み書きで共通の手順。
/// 読み書きで捕まえる例外は <see cref="IsFileError"/> と <see cref="IsBrokenContent"/> だけにする。
/// それ以外の例外はコードの誤りなので、ログに書いて握りつぶさずに気付けるようにする。
/// </summary>
internal static class DataFile
{
    /// <summary>利用者の環境のせいで読み書きできない (無い・権限が無い・ほかのアプリが使っている・ディスクがいっぱい)。</summary>
    public static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException;

    /// <summary>ファイルの中身が壊れている・形式が違う (手で書き換えた・書きかけで止まった)。</summary>
    public static bool IsBrokenContent(Exception ex) => ex is JsonException or InvalidDataException or XmlException or FormatException or System.Text.DecoderFallbackException;

    /// <summary>
    /// 利用者のフォルダのテキストファイル (辞書など) を読む。無ければ null。読めなければログに出して null。
    /// what は「ユーザーの候補辞書」のように、ログに出す名前。
    /// </summary>
    public static string? ReadUserText(string? directory, string fileName, string what)
    {
        if (directory is null) return null;
        var path = Path.Combine(directory, fileName);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Diagnostics.Log.Warn($"{what}を読めませんでした: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// write に一時ファイル (.tmp) のパスを渡して書かせ、書き終えたら path に置き換える (書きかけのファイルを残さない)。
    /// フォルダが無ければ作る。
    /// </summary>
    public static void Write(string path, Action<string> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        write(temp);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// path があれば中身を load に渡す。読めなかったら (壊れていたら) .broken に残してログに出す。
    /// what は「定型文を読めませんでした」のように、ログに出す名前。
    /// </summary>
    public static void Load(string? path, string what, Action<string> load)
    {
        if (path is null || !File.Exists(path)) return;
        try
        {
            load(File.ReadAllText(path));
        }
        catch (Exception ex) when (IsFileError(ex) || IsBrokenContent(ex))
        {
            try { File.Copy(path, path + ".broken", overwrite: true); }
            catch (Exception copyEx) when (IsFileError(copyEx)) { Diagnostics.Log.Warn($"{Path.GetFileName(path)} を .broken に残せませんでした: {copyEx.Message}"); }
            Diagnostics.Log.Warn($"{what}を読めませんでした: {ex.Message}");
        }
    }
}
