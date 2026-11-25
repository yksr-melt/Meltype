// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia.Controls;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>設定・学習データ・ユーザー辞書・定型文のバックアップ (<see cref="Backup"/>) を書き出す・戻す。</summary>
internal static class BackupPage
{
    private const string Type = "Meltype のバックアップ";
    private const string Pattern = "*.meltype-backup";

    public static Control Build(Window owner)
    {
        var message = Pages.Message();
        var create = SettingEditors.Button("バックアップを書き出す…", async () =>
        {
            try
            {
                var (content, files) = Backup.Create(AppPaths.DataDirectory, ProjectInfo.CoreVersion);
                if (await Pages.SaveFile(owner, "Meltype のバックアップを書き出す", Type, Pattern, $"Meltype-{DateTime.Now:yyyyMMdd}.meltype-backup", content))
                    Pages.Show(message, $"設定・学習データ・ユーザー辞書を書き出しました ({files} 個のファイル)。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Pages.Show(message, $"書き出せませんでした: {ex.Message}", error: true);
            }
        });
        var restore = SettingEditors.Button("バックアップから戻す…", async () =>
        {
            if (await Pages.OpenFile(owner, "Meltype のバックアップから戻す", Type, [Pattern]) is not { } content) return;
            try
            {
                var backup = Backup.Read(content);
                if (!await Dialogs.Ask(owner, "Meltype のバックアップ",
                        $"{backup.Created} に作ったバックアップ (Meltype {backup.App}、{backup.Files.Count} 個のファイル) で、今の設定・学習データ・ユーザー辞書を置き換えます。よろしいですか？\n\n今のファイルは .before-restore という名前で残します。",
                        ok: "置き換える")) return;
                var restored = backup.Restore(AppPaths.DataDirectory);
                // 設定・ユーザー辞書・学習データ・定型文は、入力している Meltype が書き換えに気づいて読み直す。
                // dictionaries/ の辞書・ローマ字の表は起動したときにだけ読む
                Pages.Show(message, $"{restored} 個のファイルを戻しました。dictionaries フォルダーの辞書は、Meltype を起動し直すと使われます。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Pages.Show(message, $"戻せませんでした: {ex.Message}", error: true);
            }
        });
        return Pages.Layout("バックアップ", "設定・学習データ・ユーザー辞書・定型文を 1 つのファイルに書き出します。別の PC の Meltype で「バックアップから戻す」を選ぶと戻せます。",
            Pages.Row(create, restore),
            message);
    }
}
