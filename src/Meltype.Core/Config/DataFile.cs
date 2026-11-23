// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>設定・学習データのファイルの読み書きで共通の手順。</summary>
internal static class DataFile
{
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
        catch (Exception ex)
        {
            try { File.Copy(path, path + ".broken", overwrite: true); }
            catch (Exception copyEx) { Diagnostics.Log.Warn($"{Path.GetFileName(path)} を .broken に残せませんでした: {copyEx.Message}"); }
            Diagnostics.Log.Warn($"{what}を読めませんでした: {ex.Message}");
        }
    }
}
