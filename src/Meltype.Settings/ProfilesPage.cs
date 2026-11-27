// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Meltype.SettingsApp;

/// <summary>プロファイル (設定の値に名前を付けたもの) の一覧。切り替え・追加・名前の変更・削除・書き出し・読み込み。</summary>
internal static class ProfilesPage
{
    public static Control Build(SettingsStore store, Window owner)
    {
        // 一度もプロファイルを作っていない config.json は、Normalize で「標準」を足す
        var settings = store.Current.Clone().Normalize(reportShortcutExclusions: false);
        var page = new StackPanel { Spacing = 8, Classes = { "page" } };
        page.Children.Add(new TextBlock
        {
            Text = "設定の値をまとめて名前を付け、切り替えて使えます (例: 仕事用・趣味用)。切り替えると、ほかのページの設定はそのプロファイルの値になります。Meltype の有効 / 無効・ログ・更新はプロファイルによらず共通です。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.7,
        });
        foreach (var name in settings.ProfileNames)
        {
            var active = name == settings.ActiveProfile;
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            if (active) actions.Children.Add(new TextBlock { Text = "使用中", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            else actions.Children.Add(SettingEditors.Button("切り替える", () => store.Replace(s => s.SwitchProfile(name))));
            actions.Children.Add(SettingEditors.Button("名前を変える…", async () =>
            {
                if (await Dialogs.Prompt(owner, "名前を変える", $"「{name}」の新しい名前", name) is not { } next || next == name) return;
                if (!store.Replace(s => s.RenameProfile(name, next)))
                    await Dialogs.Inform(owner, "名前を変える", "名前が空か、同じ名前のプロファイルがあります。");
            }));
            if (settings.Profiles.Count > 1)
            {
                actions.Children.Add(SettingEditors.Button("削除…", async () =>
                {
                    if (await Dialogs.Ask(owner, "プロファイルの削除", $"プロファイル「{name}」を削除しますか？", ok: "削除"))
                        store.Replace(s => s.RemoveProfile(name));
                }));
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock { Text = name, FontWeight = active ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(actions, 1);
            row.Children.Add(actions);
            page.Children.Add(new Border { Classes = { "card" }, Child = row });
        }
        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                SettingEditors.Button("新しいプロファイル…", async () =>
                {
                    if (await Dialogs.Prompt(owner, "新しいプロファイル", "名前 (例: 仕事用)。使用中のプロファイルの値を写して作ります。") is not { } name) return;
                    if (!store.Replace(s => s.AddProfile(name)))
                        await Dialogs.Inform(owner, "新しいプロファイル", "名前が空か、同じ名前のプロファイルがあります。");
                }),
                SettingEditors.Button("使用中のものを書き出す…", () => Export(store, owner)),
                SettingEditors.Button("読み込む…", () => Import(store, owner)),
            },
        });
        return page;
    }

    // 渡す相手に見られてよいか確かめてもらう (アプリ別設定のプロセス名が入る)
    private static async void Export(SettingsStore store, Window owner)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "プロファイルを書き出す",
            SuggestedFileName = $"{store.Current.ActiveProfile}.meltype-profile.json",
            FileTypeChoices = [new FilePickerFileType("Meltype のプロファイル") { Patterns = ["*.json"] }],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(store.Current.ExportProfile());
    }

    private static async void Import(SettingsStore store, Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "プロファイルを読み込む",
            FileTypeFilter = [new FilePickerFileType("Meltype のプロファイル") { Patterns = ["*.json"] }],
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        // プロファイルは数 KB。大きすぎるファイルは読まない
        if (stream.Length > 1024 * 1024)
        {
            await Dialogs.Inform(owner, "プロファイルを読み込む", "ファイルが大きすぎます。Meltype で書き出したプロファイルを選んでください。");
            return;
        }
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        if (!store.Replace(s => s.ImportProfile(json)))
            await Dialogs.Inform(owner, "プロファイルを読み込む", "Meltype のプロファイルとして読めませんでした。");
    }
}
