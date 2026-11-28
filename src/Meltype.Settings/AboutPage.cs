// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>
/// 設定アプリを開いて最初に出すページ。上に Meltype の有効 / 一時停止と動作モード (分類「Meltype」の設定)、
/// 下に版・ライセンスと、データフォルダー (設定・辞書・学習データの置き場所) を開くボタン。
/// </summary>
internal static class AboutPage
{
    public const string Title = "Meltype";

    public static Control Build(Window owner, Control settings)
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
        Control[] windows = OperatingSystem.IsWindows() ? [.. WindowsCards.Home()] : [];
        Control[] bottom = OperatingSystem.IsWindows() ? [.. WindowsCards.Bottom()] : [];
        return Pages.Layout(Title, "",
        [
            settings,
            .. windows,
            Pages.Card(
                Pages.Heading($"Meltype {ProjectInfo.WithCommit(ProjectInfo.CoreVersion)}"),
                new SelectableTextBlock
                {
                    Text = "Copyright (C) 2026 雪代 / Yukishiro (@yksr_melt / @yksr-melt)\n\n" +
                        "このプログラムはフリーソフトウェアです。GNU General Public License (バージョン 3、またはそれ以降のバージョン) の条件で再配布・改変できます。" +
                        "このプログラムは無保証です。詳しくは GNU GPL を参照してください。\n" +
                        "GPL v3 の条件で使えない (非公開で利用したい) 場合は、メールでご相談ください: " + ProjectInfo.Contact + "\n\n" +
                        "変換エンジン Mozc は BSD-3-Clause ライセンスです。\n" +
                        "英訳の候補・英字で書く語の候補・よく使う語の読みの一覧は JMdict (Electronic Dictionary Research and Development Group) を元にしています (CC BY-SA 4.0)。\n" +
                        "候補の意味はウィクショナリー日本語版 (Wiktionary の執筆者) を元にしています (CC BY-SA 4.0)。\n" +
                        "よく使う英単語の一覧は SCOWL (Copyright 2000-2018 by Kevin Atkinson) を元にしています。\n" +
                        "絵文字の読みは Unicode CLDR を元にしています (Unicode License v3)。\n" +
                        "同梱のライブラリ・辞書のライセンスは THIRD-PARTY-NOTICES を参照してください。",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                Pages.Row(source)),
            Pages.Card(
                Pages.Heading("データフォルダー"),
                Pages.Hint("設定 (config.json)・ユーザー辞書・学習データ・定型文・ログの置き場所です。"),
                folder,
                Pages.Row(open),
                message),
            .. bottom,
        ]);
    }
}
