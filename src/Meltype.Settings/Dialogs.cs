// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Meltype.SettingsApp;

/// <summary>確認・お知らせのダイアログ (Avalonia には MessageBox が無い)。</summary>
internal static class Dialogs
{
    /// <summary>message を出し、ok を押したら true。cancel が null なら ok だけを出す (お知らせ)。</summary>
    public static async Task<bool> Ask(Window owner, string title, string message, string ok = "OK", string? cancel = "キャンセル")
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 560,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var accept = new Button { Content = ok, IsDefault = cancel is null, Classes = { "accent" } };
        accept.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(accept);
        if (cancel is not null)
        {
            // 取り返しのつかない操作が多いので、Enter で選ばれるのはキャンセルにする
            var reject = new Button { Content = cancel, IsDefault = true, IsCancel = true };
            reject.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(reject);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children = { new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
        };
        return await dialog.ShowDialog<bool>(owner);
    }

    public static Task Inform(Window owner, string title, string message) => Ask(owner, title, message, cancel: null);

    /// <summary>1 行の文字を聞く。キャンセルなら null。</summary>
    public static async Task<string?> Prompt(Window owner, string title, string message, string initial = "")
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 360,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var box = new TextBox { Text = initial };
        var ok = new Button { Content = "OK", IsDefault = true, Classes = { "accent" } };
        ok.Click += (_, _) => dialog.Close(box.Text ?? "");
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                box,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        dialog.Opened += (_, _) => box.Focus();
        return await dialog.ShowDialog<string?>(owner);
    }
}
