// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Automation;
using Avalonia.Media;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>設定の項目を、1 項目 1 枚のカードで縦に並べる (名前と説明が左、部品が右。表は名前の下に幅いっぱい)。</summary>
internal static class SettingsPage
{
    public static Control Build(IEnumerable<(string Category, IReadOnlyList<SettingItem> Items)> groups, SettingsStore store, Window owner, bool headers)
    {
        var page = new StackPanel { Spacing = 8, Margin = new Thickness(24, 16) };
        // 値はトレイで選んだプロファイルにも入るので、プロファイルを使い分けている人には、どれを変えているかを出す
        if (store.Current.Profiles.Count > 1)
            page.Children.Add(new TextBlock { Text = $"プロファイル「{store.Current.ActiveProfile}」の値を変えています。", Opacity = 0.7 });
        foreach (var (category, items) in groups)
        {
            if (headers) page.Children.Add(new TextBlock { Text = category, Classes = { "pageHeader" }, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var item in items) page.Children.Add(Card(item, SettingEditors.Create(new EditorContext(item, store, owner))));
        }
        return page;
    }

    private static Border Card(SettingItem item, Editor editor)
    {
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (item.Description.Length > 0)
            text.Children.Add(new SelectableTextBlock { Text = item.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 });
        // スイッチ・プルダウンには見出しの文字が無いので、読み上げには項目名で読ませる
        AutomationProperties.SetName(editor.Control, item.Title);
        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Children.Add(text);
        if (editor.Wide)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(editor.Control, 1);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(editor.Control, 1);
            editor.Control.VerticalAlignment = VerticalAlignment.Center;
        }
        grid.Children.Add(editor.Control);
        return new Border { Classes = { "card" }, Child = grid };
    }

    /// <summary>words をすべて (大文字小文字を区別せず) 名前・説明・設定のキーに含む項目。</summary>
    public static bool Matches(SettingItem item, string[] words) =>
        words.All(word => item.Title.Contains(word, StringComparison.CurrentCultureIgnoreCase) ||
            item.Description.Contains(word, StringComparison.CurrentCultureIgnoreCase) ||
            item.Property.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
}
