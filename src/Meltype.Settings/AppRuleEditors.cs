// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>
/// アプリ別設定 (<see cref="Settings.AppRules"/>) と独自の種類 (<see cref="Settings.AppKinds"/>) の表。1 行が 1 つの規則で、
/// 行を変えるたびに表全体を保存する。
/// </summary>
internal static class AppRuleEditors
{
    private const string SameAsGlobal = "全体と同じ";

    public static Editor Rules(EditorContext context)
    {
        var rules = context.Store.Current.AppRules.Select(r => r.Clone()).ToList();
        // 種類の選択肢: 一般 / コード / ゲーム と、独自の種類の名前
        var kinds = Enum.GetValues<AppProfile>().Select(p => SettingsCatalog.ChoiceName(typeof(AppProfile), p))
            .Concat(context.Store.Current.AppKinds.Select(k => k.Name)).Distinct().ToList();
        var commits = Enum.GetValues<CommitMethod>().ToList();
        var table = new Table(["プロセス名", "有効", "種類", Name(nameof(AppRule.Commit)), Name(nameof(AppRule.TextInput))]);
        void Save() => context.Set(rules.Where(r => r.Process.Trim().Length > 0).Select(r => r.Clone()).ToList());
        void AddRow(AppRule rule)
        {
            var kind = rule.Kind is { Length: > 0 } name && kinds.Contains(name) ? name : SettingsCatalog.ChoiceName(typeof(AppProfile), rule.Profile);
            var kindBox = new ComboBox { ItemsSource = kinds, SelectedItem = kind, HorizontalAlignment = HorizontalAlignment.Stretch };
            kindBox.SelectionChanged += (_, _) =>
            {
                if (kindBox.SelectedItem is not string selected) return;
                var builtIn = Enum.GetValues<AppProfile>().Cast<AppProfile?>().FirstOrDefault(p => SettingsCatalog.ChoiceName(typeof(AppProfile), p!.Value) == selected);
                rule.Profile = builtIn ?? AppProfile.General;
                rule.Kind = builtIn is null ? selected : null;
                Save();
            };
            var commit = new ComboBox
            {
                ItemsSource = commits.Select(c => SettingsCatalog.ChoiceName(typeof(CommitMethod), c)).ToList(),
                SelectedIndex = commits.IndexOf(rule.Commit),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            commit.SelectionChanged += (_, _) =>
            {
                rule.Commit = commits[Math.Max(0, commit.SelectedIndex)];
                Save();
            };
            var textInput = new CheckBox { IsChecked = rule.TextInput };
            textInput.IsCheckedChanged += (_, _) =>
            {
                rule.TextInput = textInput.IsChecked == true;
                Save();
            };
            table.AddRow(() =>
            {
                rules.Remove(rule);
                Save();
            },
                SettingEditors.Text(rule.Process, text =>
                {
                    rule.Process = text.Trim();
                    Save();
                }),
                SettingEditors.Switch(rule.Enabled, on =>
                {
                    rule.Enabled = on;
                    Save();
                }),
                kindBox, commit, textInput);
        }
        foreach (var rule in rules) AddRow(rule);
        var add = SettingEditors.Button("行を追加", () =>
        {
            var rule = new AppRule();
            rules.Add(rule);
            AddRow(rule);
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add } };
        // プロセス名を知らなくても足せるように、実行中のアプリから選べるようにする。
        // Mac (bundle ID) と Linux (プログラム名) は、窓を持つプロセスを .NET から見分けられないので、手で書く
        if (OperatingSystem.IsWindows())
        {
            var running = new Button { Content = "実行中のアプリから追加…" };
            running.Click += (_, _) =>
            {
                var menu = new MenuFlyout();
                foreach (var name in RunningApps())
                {
                    var item = new MenuItem { Header = name };
                    item.Click += (_, _) =>
                    {
                        if (rules.Any(r => string.Equals(r.Process, name, StringComparison.OrdinalIgnoreCase))) return;
                        var rule = new AppRule { Process = name };
                        rules.Add(rule);
                        AddRow(rule);
                        Save();
                    };
                    menu.Items.Add(item);
                }
                if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "(窓を開いているアプリがありません)", IsEnabled = false });
                menu.ShowAt(running);
            };
            actions.Children.Add(running);
        }
        return new Editor(new StackPanel { Spacing = 8, Children = { table, actions } }, Wide: true);
    }

    public static Editor Kinds(EditorContext context)
    {
        var kinds = context.Store.Current.AppKinds.Select(k => k.Clone()).ToList();
        var bases = Enum.GetValues<AppProfile>().ToList();
        string[] tristate = [SameAsGlobal, "ON", "OFF"];
        var table = new Table(["名前 (例: チャット)", "元にする種類", "自動判定", "ライブ変換"]);
        // 名前の重なる種類はアプリ別設定から見分けられないので、最初のものだけを残す
        void Save() => context.Set(kinds.Where(k => k.Name.Trim().Length > 0).GroupBy(k => k.Name).Select(g => g.First().Clone()).ToList());
        ComboBox Tristate(bool? value, Action<bool?> changed)
        {
            var combo = new ComboBox { ItemsSource = tristate, SelectedIndex = value is { } on ? (on ? 1 : 2) : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) =>
            {
                changed(combo.SelectedIndex switch { 1 => true, 2 => false, _ => null });
                Save();
            };
            return combo;
        }
        void AddRow(AppKind kind)
        {
            var baseBox = new ComboBox
            {
                ItemsSource = bases.Select(b => SettingsCatalog.ChoiceName(typeof(AppProfile), b)).ToList(),
                SelectedIndex = bases.IndexOf(kind.Base),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            baseBox.SelectionChanged += (_, _) =>
            {
                kind.Base = bases[Math.Max(0, baseBox.SelectedIndex)];
                Save();
            };
            table.AddRow(() =>
            {
                kinds.Remove(kind);
                Save();
            },
                SettingEditors.Text(kind.Name, text =>
                {
                    kind.Name = text.Trim();
                    Save();
                }),
                baseBox,
                Tristate(kind.AutoDetection, value => kind.AutoDetection = value),
                Tristate(kind.LiveConversion, value => kind.LiveConversion = value));
        }
        foreach (var kind in kinds) AddRow(kind);
        var add = SettingEditors.Button("行を追加", () =>
        {
            var kind = new AppKind();
            kinds.Add(kind);
            AddRow(kind);
        });
        return new Editor(new StackPanel { Spacing = 8, Children = { table, add } }, Wide: true);
    }

    private static string Name(string property) =>
        typeof(AppRule).GetProperty(property)?.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property;

    private static SortedSet<string> RunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && process.Id != Environment.ProcessId) names.Add(process.ProcessName + ".exe");
                }
                catch (InvalidOperationException)
                {
                    // 一覧を取ってから終わったプロセス
                }
            }
        }
        return names;
    }

    /// <summary>見出しの行と、行ごとの部品と削除のボタン。列の幅は見出しと部品で揃える。</summary>
    private sealed class Table : Grid
    {
        public Table(string[] headers)
        {
            ColumnSpacing = 8;
            RowSpacing = 4;
            ColumnDefinitions.Add(new ColumnDefinition(2, GridUnitType.Star) { MinWidth = 140 });
            for (var i = 1; i < headers.Length; i++) ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var i = 0; i < headers.Length; i++)
            {
                var header = new TextBlock { Text = headers[i], Opacity = 0.7, Margin = new Thickness(0, 0, 0, 2) };
                SetColumn(header, i);
                Children.Add(header);
            }
        }

        public void AddRow(Action removed, params Control[] cells)
        {
            var row = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var remove = new Button { Content = "削除" };
            Control[] all = [.. cells, remove];
            for (var i = 0; i < all.Length; i++)
            {
                all[i].VerticalAlignment = VerticalAlignment.Center;
                SetRow(all[i], row);
                SetColumn(all[i], i);
                Children.Add(all[i]);
            }
            remove.Click += (_, _) =>
            {
                // 消した行は高さ 0 にして残す (行の番号を詰めると、ほかの行の部品の位置を付け替えることになる)
                foreach (var cell in all) Children.Remove(cell);
                RowDefinitions[row].Height = new GridLength(0);
                removed();
            };
        }
    }
}
