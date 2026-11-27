// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Meltype.SettingsApp;

/// <summary>
/// 設定以外のページ (左の一覧で、設定の分類の後ろに並ぶ)。
/// どのページも、開くたび・操作するたびにデータフォルダーのファイルを Meltype.Core で読み直す (入力している Meltype も同じファイルを書くので、覚えておかない)。
/// </summary>
internal static class Pages
{
    /// <param name="newWord">ユーザー辞書の登録欄に入れておく語 (MainWindow の --add-word)。</param>
    public static IEnumerable<Page> All(Window owner, Func<Dictionary.UserWord?> newWord) =>
    [
        new(UserDictionaryPage.Title, () => UserDictionaryPage.Build(owner, newWord())),
        new("定型文", () => SnippetsPage.Build(owner)),
        new("学習データ", () => LearningPage.Build(owner)),
        new("バックアップ", () => BackupPage.Build(owner)),
        new("Meltype について", () => AboutPage.Build(owner)),
    ];

    /// <summary>ページの見出しと説明を上に置き、続けて parts を縦に並べる。</summary>
    public static StackPanel Layout(string title, string description, params Control[] parts)
    {
        var page = new StackPanel { Spacing = 12, Classes = { "page" } };
        page.Children.Add(new TextBlock { Text = title, Classes = { "pageHeader" } });
        if (description.Length > 0) page.Children.Add(Hint(description));
        foreach (var part in parts) page.Children.Add(part);
        return page;
    }

    public static Border Card(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 8 };
        foreach (var child in children) stack.Children.Add(child);
        return new Border { Classes = { "card" }, Child = stack };
    }

    public static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };

    public static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };

    /// <summary>エラー・結果の 1 行 (空なら隠す)。</summary>
    public static TextBlock Message() => new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };

    public static void Show(TextBlock message, string text, bool error = false)
    {
        message.Text = text;
        if (error) message.Foreground = Brushes.OrangeRed;
        else message.ClearValue(TextBlock.ForegroundProperty);
        message.IsVisible = text.Length > 0;
    }

    public static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children)
        {
            child.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(child);
        }
        return row;
    }

    /// <summary>ファイルを選んで中身を読む。選ばなければ null。</summary>
    public static async Task<byte[]?> OpenFile(Window owner, string title, string type, string[] patterns)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = [new FilePickerFileType(type) { Patterns = patterns }, FilePickerFileTypes.All],
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    /// <summary>保存先を選んで content を書く。選ばなければ false。</summary>
    public static async Task<bool> SaveFile(Window owner, string title, string type, string pattern, string suggestedName, byte[] content)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType(type) { Patterns = [pattern] }],
        });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(content);
        return true;
    }
}
