// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

namespace Meltype.Config;

/// <summary>
/// 学習データ・定型文のファイルを、ほかのプロセス (設定アプリ) が書き換えたかを、更新時刻で見分ける。
/// 持ち主は、読んだ・書いた直後に <see cref="Synced"/> を呼び、<see cref="Changed"/> なら保存せずに読み直す
/// (設定アプリで消した学習を、入力している Meltype の保存で戻さないため。まだ保存していない学習 (最長 <see cref="LearningSaves.Delay"/>) は捨てる)。
/// 書いてから Synced を呼ぶまでの間にほかのプロセスが書いたときは見分けられない (間は短いので、まず重ならない)。
/// </summary>
internal sealed class FileStamp(string? path)
{
    private DateTime? _known = Read(path);

    // 更新時刻を読めないときは、変わっていないとみなす (一時的に読めないだけで、学習を捨てない)
    public bool Changed => Read(path) is { } time && time != _known;

    public void Synced() => _known = Read(path);

    /// <summary>Synced の後で読めなかったときに呼ぶ (次の <see cref="Changed"/> で読み直す)。</summary>
    public void Forget() => _known = null;

    // 無いファイルは 1601 年になるので、消されたことも書き換えとして見分けられる
    private static DateTime? Read(string? path)
    {
        if (path is null) return null;
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
