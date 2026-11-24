// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.SettingsApp;

/// <summary>入力モード切り替えキー (<see cref="Settings.ToggleShortcuts"/>) の一覧。追加は、キーと修飾キーを選ぶ。</summary>
internal static class ShortcutEditors
{
    public static Editor ToggleShortcuts(EditorContext context)
    {
        var list = new StackPanel { Spacing = 4 };
        void Show()
        {
            list.Children.Clear();
            var shortcuts = context.Store.Current.EffectiveToggleShortcuts.ToList();
            foreach (var shortcut in shortcuts)
            {
                var remove = SettingEditors.Button("削除", () =>
                {
                    context.Set(shortcuts.Where(s => s != shortcut).ToList());
                    Show();
                });
                list.Children.Add(new DockPanel
                {
                    LastChildFill = true,
                    Children = { Dock(remove, Avalonia.Controls.Dock.Right), new TextBlock { Text = shortcut.Describe(), VerticalAlignment = VerticalAlignment.Center } },
                });
            }
            if (shortcuts.Count == 0) list.Children.Add(new TextBlock { Text = "(なし)", Opacity = 0.7 });
        }
        Show();
        var add = SettingEditors.Button("追加…", async () =>
        {
            var shortcuts = context.Store.Current.EffectiveToggleShortcuts.ToList();
            if (await Choose(context.Owner, shortcut => ValidationError(shortcut, context.Store.Current, shortcuts)) is not { } added) return;
            context.Set(shortcuts.Append(added).ToList());
            Show();
        });
        var reset = SettingEditors.Button("初期値に戻す", () =>
        {
            context.Set(ToggleShortcut.Defaults());
            Show();
        });
        return new Editor(new StackPanel
        {
            Spacing = 8,
            MinWidth = 280,
            Children = { list, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, reset } } },
        });
    }

    private static Control Dock(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    private static string? ValidationError(ToggleShortcut shortcut, Settings settings, IEnumerable<ToggleShortcut> others) =>
        ToggleShortcutValidator.Validate(shortcut, settings, others) switch
        {
            ShortcutValidationError.Invalid => "このキー操作は登録できません。Ctrl + 半角/全角 は Meltype の有効／無効に使用します。",
            ShortcutValidationError.UnmodifiedKey => "修飾キーなし、または Shift のみで登録できるのは半角/全角・CapsLock・カタカナ/ひらがな・変換・無変換です。Ctrl・Alt・Win との組み合わせを選んでください。",
            ShortcutValidationError.Duplicate => "同じショートカットがすでに登録されています。",
            ShortcutValidationError.ControlKeyBinding => "既存の「Control キーの割り当て」と競合します。先にその割り当てを変更してください。",
            ShortcutValidationError.Conversion => "このショートカットは既存の変換操作に使用します。",
            ShortcutValidationError.FunctionKey => "F6〜F10 は既存のかな・英字変換に使用します。修飾キーとの組み合わせを選んでください。",
            ShortcutValidationError.ShiftSpaceHalfWidth => "設定「スペースの幅」が「半角」のときは、Shift + Space は切り替えに使えません。先にその設定を変えてください。",
            _ => null,
        };

    /// <summary>
    /// キーと修飾キーを選ぶダイアログ。登録できる組を選んで「登録」したら返す。
    /// 押したキーを取り込む形にしない: 入力している Meltype (Windows のキーボードフック・各 OS の IME) が、切り替えキーを設定アプリより先に取る。
    /// </summary>
    private static async Task<ToggleShortcut?> Choose(Window owner, Func<ToggleShortcut, string?> validate)
    {
        var keys = ToggleShortcut.SelectableKeys;
        var key = new ComboBox { ItemsSource = keys.Select(ToggleShortcut.KeyName).ToList(), SelectedIndex = 0, MinWidth = 180, MaxDropDownHeight = 400 };
        (ShortcutModifiers Flag, CheckBox Box)[] modifiers =
        [
            (ShortcutModifiers.Control, new CheckBox { Content = "Ctrl" }),
            (ShortcutModifiers.Alt, new CheckBox { Content = "Alt" }),
            (ShortcutModifiers.Shift, new CheckBox { Content = "Shift" }),
            (ShortcutModifiers.Win, new CheckBox { Content = OperatingSystem.IsMacOS() ? "Command" : "Win" }),
        ];
        var error = new TextBlock { Foreground = Avalonia.Media.Brushes.OrangeRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
        var dialog = new Window
        {
            Title = "入力モード切り替えキーの登録",
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 520,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "登録", IsDefault = true, Classes = { "accent" } };
        ok.Click += (_, _) =>
        {
            var shortcut = new ToggleShortcut
            {
                Key = keys[Math.Max(0, key.SelectedIndex)],
                Modifiers = modifiers.Where(m => m.Box.IsChecked == true).Aggregate(ShortcutModifiers.None, (all, m) => all | m.Flag),
            };
            error.Text = validate(shortcut) ?? "";
            error.IsVisible = error.Text.Length > 0;
            if (!error.IsVisible) dialog.Close(shortcut);
        };
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        cancel.Click += (_, _) => dialog.Close(null);
        var modifierRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var (_, box) in modifiers) modifierRow.Children.Add(box);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                modifierRow,
                key,
                new TextBlock
                {
                    Text = "日本語／英数の入力モードを切り替えます。\nAlt＋バッククォートと Alt＋半角／全角は別のキー操作として登録します。\nOS や他のアプリで使うキー操作を登録すると、元の操作が使えなくなったり、入力モードの切り替えと同時に動いたりする場合があります。",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Opacity = 0.7,
                },
                error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        return await dialog.ShowDialog<ToggleShortcut?>(owner);
    }
}
