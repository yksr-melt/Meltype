// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>種類だけでは部品が決まらない項目の部品 (<see cref="SettingEditors"/> に項目名で登録する)。</summary>
internal static class CustomEditors
{
    /// <summary>OFF から ON にするときだけ確かめる。キャンセルなら OFF に戻す。</summary>
    public static Editor UpdatePrerelease(EditorContext context)
    {
        ToggleSwitch? toggle = null;
        toggle = SettingEditors.Switch((bool)context.Value!, async on =>
        {
            if (on && !await Dialogs.Ask(context.Owner, "プレリリース版の自動更新",
                    "プレリリース版は、正式版の前に試してもらうための版です。不具合や動作の不安定さが含まれることがあります。\n\n" +
                    "プレリリース版も自動で更新するようにしますか？ (あとから OFF に戻せます)", ok: "有効にする"))
            {
                toggle!.IsChecked = false;
                return;
            }
            context.Set(on);
        });
        return new Editor(toggle);
    }

    private const string DefaultFont = "既定";

    /// <summary>この PC のフォントから選ぶ。今は無いフォントを選んでいたら、選んだままにする。</summary>
    public static Editor Font(EditorContext context)
    {
        var current = (string?)context.Value ?? "";
        var names = FontManager.Current.SystemFonts.Select(f => f.Name).Order(StringComparer.CurrentCulture).ToList();
        if (current.Length > 0 && !names.Contains(current)) names.Insert(0, current);
        names.Insert(0, DefaultFont);
        var combo = new ComboBox { ItemsSource = names, SelectedItem = current.Length == 0 ? DefaultFont : current, MinWidth = 200, MaxDropDownHeight = 400 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string name) context.Set(name == DefaultFont ? "" : name);
        };
        return new Editor(combo);
    }

    /// <summary>
    /// #RRGGBB の色。横に今の色の見本を出す。空なら既定の色 (項目の説明に書いてある)。
    /// customTheme の色 (変換ボックスのカスタムの色) は「変換ボックスの色」がカスタムのときだけ使うので、色を決めたらカスタムにする。
    /// </summary>
    public static Editor Color(EditorContext context, bool customTheme = false)
    {
        var swatch = new Border { Width = 28, Height = 28, CornerRadius = new(4), BorderThickness = new(1), BorderBrush = Brushes.Gray };
        var warning = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        void Show(string text)
        {
            swatch.Background = Avalonia.Media.Color.TryParse(text, out var color) ? new SolidColorBrush(color) : null;
            // 読みにくい組み合わせは止めずに知らせる (保存するかは利用者が決める)
            warning.Text = customTheme ? context.Store.Current.ColorWarning() ?? "" : "";
            warning.IsVisible = warning.Text.Length > 0;
        }
        var box = SettingEditors.Text((string?)context.Value ?? "", text =>
        {
            if (customTheme)
            {
                // 「変換ボックスの色」も変わるので、ページを出し直す
                context.Edit(settings =>
                {
                    context.Item.Property.SetValue(settings, text.Trim());
                    settings.CompositionTheme = CompositionTheme.Custom;
                }, showAll: true);
                return;
            }
            context.Set(text.Trim());
            Show(text.Trim());
        });
        box.MinWidth = 120;
        box.PlaceholderText = "既定";
        Show(box.Text ?? "");
        return new Editor(new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, box } },
                warning,
            },
        });
    }
}
