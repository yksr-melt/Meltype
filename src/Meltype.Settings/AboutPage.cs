// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>版・ライセンスと、データフォルダー (設定・辞書・学習データの置き場所) を開くボタン。</summary>
internal static class AboutPage
{
    public static Control Build(Window owner)
    {
        var message = Pages.Message();
        var folder = new SelectableTextBlock { Text = AppPaths.DataDirectory, FontFamily = "monospace" };
        var open = SettingEditors.Button("データフォルダーを開く", async () =>
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            if (!await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(AppPaths.DataDirectory)))
                Pages.Show(message, "フォルダーを開けませんでした。上のパスをファイルマネージャーで開いてください。", error: true);
        });
        var source = SettingEditors.Button("ソースコード", async () => await owner.Launcher.LaunchUriAsync(new Uri(ProjectInfo.SourceUrl)));
        return Pages.Layout("Meltype について", "",
            Pages.Card(
                Pages.Heading($"Meltype {ProjectInfo.WithCommit(ProjectInfo.CoreVersion)}"),
                new SelectableTextBlock
                {
                    Text = "Copyright (C) 2026 雪代 / Yukishiro (@yksr_melt / @yksr-melt)\n\n" +
                        "このプログラムはフリーソフトウェアです。GNU General Public License (バージョン 3、またはそれ以降のバージョン) の条件で再配布・改変できます。" +
                        "このプログラムは無保証です。詳しくは GNU GPL を参照してください。\n" +
                        "同梱のライブラリ・辞書のライセンスは THIRD-PARTY-NOTICES を参照してください。",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                Pages.Row(source)),
            Pages.Card(
                Pages.Heading("データフォルダー"),
                Pages.Hint("設定 (config.json)・ユーザー辞書・学習データ・定型文・ログの置き場所です。"),
                folder,
                Pages.Row(open),
                message));
    }
}
