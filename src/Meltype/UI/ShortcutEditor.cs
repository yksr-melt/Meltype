// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

using Meltype.Config;
using Meltype.Input;

namespace Meltype.UI;

/// <summary>登録済みの入力モード切り替えショートカットを表示し、追加・変更・削除する。</summary>
internal sealed class ShortcutEditor : UserControl
{
    private readonly MeltypeEngine _engine;
    private readonly Func<Settings> _settings;
    private readonly ListBox _list = new() { Dock = DockStyle.Top, IntegralHeight = false };
    private List<ToggleShortcut> _bindings = [];

    public ShortcutEditor(MeltypeEngine engine, Func<Settings> settings)
    {
        _engine = engine;
        _settings = settings;
        Dock = DockStyle.Top;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, WrapContents = true };
        foreach (var (label, action) in new (string, Action)[]
        {
            ("追加", () => Edit(-1)), ("変更", () => { if (_list.SelectedIndex >= 0) Edit(_list.SelectedIndex); }),
            ("削除", () => { if (_list.SelectedIndex >= 0) { _bindings.RemoveAt(_list.SelectedIndex); RefreshList(); } }),
            ("初期値に戻す", () => LoadBindings(ToggleShortcut.Defaults())),
        })
        {
            var button = new Button { Text = label, AutoSize = true };
            button.Click += (_, _) => action();
            buttons.Controls.Add(button);
        }
        Controls.Add(buttons);
        Controls.Add(_list);
        var resizing = false;
        void ResizeLayout()
        {
            if (resizing) return;
            resizing = true;
            try
            {
                _list.Height = (int)(_list.ItemHeight * 2.5) + SystemInformation.BorderSize.Height * 2;
                buttons.Width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
                buttons.Height = buttons.GetPreferredSize(new Size(buttons.Width, 0)).Height;
                Height = Padding.Vertical + _list.Height + buttons.Height;
            }
            finally { resizing = false; }
        }
        SizeChanged += (_, _) => ResizeLayout();
        FontChanged += (_, _) => ResizeLayout();
        buttons.Layout += (_, _) => ResizeLayout();
        ResizeLayout();
        _list.DoubleClick += (_, _) => Edit(_list.SelectedIndex);
    }

    public void LoadBindings(IEnumerable<ToggleShortcut> bindings)
    {
        _bindings = bindings.Select(b => b with { }).ToList();
        RefreshList();
    }

    public List<ToggleShortcut> CollectBindings() => _bindings.Select(b => b with { }).ToList();

    private void RefreshList()
    {
        var selected = _list.SelectedIndex;
        _list.Items.Clear();
        _list.Items.AddRange(_bindings.Select(Describe).Cast<object>().ToArray());
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Clamp(selected, 0, _list.Items.Count - 1);
    }

    internal static string Describe(ToggleShortcut b)
    {
        var names = new List<string>();
        foreach (var (flag, label) in new[] { (ShortcutModifiers.Control, "Ctrl"), (ShortcutModifiers.Alt, "Alt"), (ShortcutModifiers.Shift, "Shift"), (ShortcutModifiers.Win, "Win") })
            if ((b.Modifiers & flag) != 0) names.Add(label);
        names.Add(b.Key switch
        {
            ToggleShortcut.Hankaku => "半角/全角", ToggleShortcut.Grave => "` (バッククォート)",
            0x1C => "変換", 0x1D => "無変換", _ => ((Keys)b.Key).ToString(),
        });
        return string.Join(" + ", names);
    }

    internal static string? ValidationError(ToggleShortcut b, Settings settings, IEnumerable<ToggleShortcut> others)
    {
        return ToggleShortcutValidator.Validate(b, settings, others) switch
        {
            ShortcutValidationError.Invalid => "このキー操作は登録できません。Ctrl + 半角/全角 は Meltype の有効／無効に使用します。",
            ShortcutValidationError.Duplicate => "同じショートカットがすでに登録されています。",
            ShortcutValidationError.Conversion => "このショートカットは既存の変換操作に使用します。",
            ShortcutValidationError.FunctionKey => "F6〜F10 は既存のかな・英字変換に使用します。修飾キーとの組み合わせを選んでください。",
            ShortcutValidationError.ConvertKeys => "既存の「無変換で英数 / 変換で日本語」と重複します。先にその設定を OFF にしてください。",
            _ => null,
        };
    }

    private void Edit(int index)
    {
        using var dialog = new ShortcutCaptureForm(_engine, index >= 0 ? _bindings[index] : null);
        dialog.ValidateBinding = b => ValidationError(b, _settings(), _bindings.Where((_, i) => i != index));
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || dialog.Shortcut is not { } binding) return;
        if (index >= 0) _bindings[index] = binding; else _bindings.Add(binding);
        RefreshList();
    }
}
