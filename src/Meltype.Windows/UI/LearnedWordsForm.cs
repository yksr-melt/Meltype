// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using Meltype.Composition;

namespace Meltype.UI;

/// <summary>
/// 学習した語の一覧 (トレイの「学習した語...」)。間違えて覚えた語 (go を英語として覚えて にほんgo になる など) を、
/// 学習データ全体をリセットせずに 1 語ずつ忘れさせる。英字で打った語を英語・日本語のどちらで出すか (LanguageMemory) を並べる。
/// </summary>
internal sealed class LearnedWordsForm : Form
{
    private readonly LanguageMemory _languages;
    private readonly DataGridView _grid = FormControls.ReadOnlyGrid();
    private readonly TextBox _filter = new() { Width = 220, PlaceholderText = "絞り込み (語)" };

    public LearnedWordsForm(LanguageMemory languages)
    {
        _languages = languages;
        Text = "Meltype 学習した語";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(620, 540);
        MinimumSize = new Size(460, 360);
        Font = new Font("Yu Gothic UI", 9.5F);

        _grid.Columns.Add("word", "打った英字");
        _grid.Columns.Add("language", "出し方");
        _grid.Columns.Add("count", "回数");
        _grid.Columns.Add("state", "状態");
        _grid.Columns.Add("used", "最後に使った日");
        var note = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
            ForeColor = SystemColors.GrayText,
            Text = "英字で打った語を、自動の判定ではなく覚えた方 (英語 / 日本語) で出します。間違えて覚えた語は、選んで「忘れる」を押してください。",
            MaximumSize = new Size(580, 0),
        };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        top.Controls.Add(_filter);
        _filter.TextChanged += (_, _) => Reload();

        var forget = new Button { Text = "選んだ語を忘れる", AutoSize = true };
        forget.Click += (_, _) => ForgetSelected();
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([close, forget]);
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) ForgetSelected(); };

        Controls.Add(_grid);
        Controls.Add(note);
        Controls.Add(top);
        Controls.Add(buttons);
        CancelButton = close;
        Reload();
    }

    private void Reload()
    {
        var filter = _filter.Text.Trim();
        _grid.Rows.Clear();
        foreach (var entry in _languages.Entries().Where(e => filter.Length == 0 || e.Word.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var row = _grid.Rows.Add(entry.Word, (entry.English ? "英語 (英字のまま)" : "日本語 (かな)") + (entry.Context is { } context ? $"・{context}" : ""), entry.Count,
                entry.Active ? "有効" : "もう一度で覚える", entry.Used.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            _grid.Rows[row].Tag = entry.EntryKey;
        }
    }

    private void ForgetSelected()
    {
        var keys = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (string)r.Tag!).ToList();
        if (keys.Count == 0) return;
        _languages.Remove(keys);
        Diagnostics.Log.Info($"学習した語を {keys.Count} 個忘れました。");
        Reload();
    }
}
