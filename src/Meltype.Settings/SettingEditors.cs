// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Avalonia.Controls;
using Avalonia.Layout;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>項目の部品と、項目名の下に幅いっぱいで出すか (表など、右の列では狭いもの)。</summary>
internal sealed record Editor(Control Control, bool Wide = false);

/// <summary>部品を作るときに渡すもの。部品は作ったときの値を出し、変えたら Store.Edit で保存する (ほかのプロセスが書いたら、ページごと作り直す)。</summary>
internal sealed record EditorContext(SettingItem Item, SettingsStore Store, Window Owner)
{
    public object? Value => Item.Property.GetValue(Store.Current);

    /// <summary>部品を作ったときのプロファイル (<see cref="SettingsStore.Edit"/>)。</summary>
    private string Profile { get; } = Store.Current.ActiveProfile;

    public void Edit(Action<Settings> change, bool showAll = false) => Store.Edit(change, Profile, showAll);

    public void Set(object? value) => Edit(settings => Item.Property.SetValue(settings, value));
}

/// <summary>
/// 項目の種類に合わせた部品を作る: ON/OFF → スイッチ、列挙型 → 選択肢の名前 (Description) のプルダウン、数値・文字列 → 入力欄。
/// 種類だけでは決まらない項目 (色・フォント・キーの割り当て・表) は <see cref="Custom"/> に項目名で登録する。
/// </summary>
internal static class SettingEditors
{
    private static readonly Dictionary<string, Func<EditorContext, Editor>> Custom = new()
    {
        [nameof(Settings.UpdatePrerelease)] = CustomEditors.UpdatePrerelease,
        [nameof(Settings.CompositionFont)] = CustomEditors.Font,
        [nameof(Settings.ModeIndicatorJapaneseColor)] = context => CustomEditors.Color(context),
        [nameof(Settings.ModeIndicatorDirectColor)] = context => CustomEditors.Color(context),
        [nameof(Settings.ModeIndicatorTextColor)] = context => CustomEditors.Color(context),
        [nameof(Settings.CompositionCustomBackColor)] = context => CustomEditors.Color(context, customTheme: true),
        [nameof(Settings.CompositionCustomTextColor)] = context => CustomEditors.Color(context, customTheme: true),
        [nameof(Settings.AppRules)] = AppRuleEditors.Rules,
        [nameof(Settings.AppKinds)] = AppRuleEditors.Kinds,
        [nameof(Settings.ToggleShortcuts)] = ShortcutEditors.ToggleShortcuts,
    };

    public static Editor Create(EditorContext context)
    {
        if (Custom.TryGetValue(context.Item.Property.Name, out var custom)) return custom(context);
        var type = context.Item.Property.PropertyType;
        if (type == typeof(bool)) return new Editor(Switch((bool)context.Value!, value => context.Set(value)));
        if (type.IsEnum) return new Editor(Choices(type, context.Value!, value => context.Set(value)));
        if (type == typeof(int)) return new Editor(Number((int)context.Value!, value => context.Set((int)value)));
        if (type == typeof(double)) return new Editor(Number((decimal)(double)context.Value!, value => context.Set((double)value), integer: false));
        if (type == typeof(string)) return new Editor(Text((string?)context.Value ?? "", value => context.Set(value)));
        throw new NotSupportedException($"設定 {context.Item.Property.Name} ({type}) の部品がありません。SettingEditors.Custom に登録してください。");
    }

    public static ToggleSwitch Switch(bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null, VerticalAlignment = VerticalAlignment.Center };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return toggle;
    }

    public static ComboBox Choices(Type enumType, object value, Action<object> changed)
    {
        // Enum.GetValues(Type) は NativeAOT で使えない (配列の型を実行時に作る)
        var values = Enum.GetValuesAsUnderlyingType(enumType).Cast<object>().Select(v => Enum.ToObject(enumType, v)).ToList();
        var combo = new ComboBox
        {
            ItemsSource = values.Select(v => SettingsCatalog.ChoiceName(enumType, v)).ToList(),
            SelectedIndex = values.IndexOf(value),
            MinWidth = 200,
            VerticalAlignment = VerticalAlignment.Center,
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) changed(values[combo.SelectedIndex]);
        };
        return combo;
    }

    private static NumericUpDown Number(decimal value, Action<decimal> changed, bool integer = true)
    {
        var number = new NumericUpDown
        {
            Value = value,
            Increment = integer ? 1 : 0.1m,
            FormatString = integer ? "0" : "0.##",
            MinWidth = 140,
            VerticalAlignment = VerticalAlignment.Center,
        };
        number.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } next) changed(next);
        };
        return number;
    }

    /// <summary>文字列の入力欄。打つたびに保存せず、Enter・フォーカスが外れたときに保存する (入力する Meltype が、打ちかけの値を読まないように)。</summary>
    public static TextBox Text(string value, Action<string> changed)
    {
        var box = new TextBox { Text = value, MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        var saved = value;
        void Commit()
        {
            var text = box.Text ?? "";
            if (text == saved) return;
            saved = text;
            changed(text);
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) Commit();
        };
        return box;
    }

    public static Button Button(string text, Action clicked)
    {
        var button = new Button { Content = text };
        button.Click += (_, e) =>
        {
            e.Handled = true;
            clicked();
        };
        return button;
    }
}
