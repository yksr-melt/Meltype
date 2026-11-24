// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using System.Xml;

namespace Meltype.Config;

/// <summary>
/// 設定・学習データのファイルの読み書きで共通の手順。
/// 書くとき (中身は Meltype が作る) に捕まえる例外は <see cref="IsFileError"/> だけにする。それ以外はコードの誤りなので投げる。
/// 読むとき (中身は利用者が書き換えられる) は、中身から出る例外を型で「壊れている」と「コードの誤り」に分けられない
/// (手で書いた値の型が違うと、解析が InvalidOperationException などを投げる)。読み込みはすべて捕まえて既定のまま続け、
/// 想定外の型の例外は <see cref="ReportUnreadable"/> が中身 (場所) ごと Log.Error に書いて、気付けるようにする。
/// </summary>
internal static class DataFile
{
    /// <summary>利用者の環境のせいで読み書きできない (無い・権限が無い・ほかのアプリが使っている・ディスクがいっぱい)。</summary>
    public static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException;

    /// <summary>ファイルの中身が壊れている・形式が違う (手で書き換えた・書きかけで止まった)。</summary>
    public static bool IsBrokenContent(Exception ex) => ex is JsonException or InvalidDataException or XmlException or FormatException or System.Text.DecoderFallbackException;

    /// <summary>
    /// 利用者が書き換えられるファイルを読めなかったことをログに書く。ファイルの問題・壊れた中身は要点だけ、
    /// それ以外の型 (読み込みのコードの誤りかもしれない) は中身ごと Log.Error に書く。
    /// what は「ユーザーの候補辞書」のように、ログに出す名前。
    /// </summary>
    public static void ReportUnreadable(string what, Exception ex)
    {
        if (IsFileError(ex) || IsBrokenContent(ex)) Diagnostics.Log.Warn($"{what}を読めませんでした: {ex.Message}");
        else Diagnostics.Log.Error($"{what}を読めませんでした: {ex}");
    }

    /// <summary>
    /// 利用者のフォルダのテキストファイル (辞書など) があれば、中身を parse に渡す。読めない・解析できなければログに出して飛ばす。
    /// what は「ユーザーの候補辞書」のように、ログに出す名前。
    /// </summary>
    public static void LoadUserText(string? directory, string fileName, string what, Action<string> parse)
    {
        if (directory is null) return;
        var path = Path.Combine(directory, fileName);
        try
        {
            if (File.Exists(path)) parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            ReportUnreadable(what, ex);
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
    /// path があれば中身を load に渡す。中身が壊れていたら .broken に残してログに出す。
    /// ファイルを読めなかった (ほかのプロセスが開いている・一時的に読めない) ときは、壊れてはいないので .broken に残さず、false を返す。
    /// 呼び出し側は、前の中身のまま続け、読めなかったファイルを上書きしない (一時的に読めないだけで中身が消える: issue #2272)。
    /// stamp を渡すと、読めなかったときに忘れさせる (次の <see cref="FileStamp.Changed"/> で読み直す)。
    /// what は「定型文を読めませんでした」のように、ログに出す名前。
    /// </summary>
    public static bool Load(string? path, string what, Action<string> load, FileStamp? stamp = null)
    {
        if (path is null || !File.Exists(path)) return true;
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Diagnostics.Log.Warn($"{what}を読めませんでした: {ex.Message}");
            stamp?.Forget();
            return false;
        }
        try
        {
            load(content);
        }
        catch (Exception ex)
        {
            try { File.Copy(path, path + ".broken", overwrite: true); }
            catch (Exception copyEx) when (IsFileError(copyEx)) { Diagnostics.Log.Warn($"{Path.GetFileName(path)} を .broken に残せませんでした: {copyEx.Message}"); }
            ReportUnreadable(what, ex);
        }
        return true;
    }
}
