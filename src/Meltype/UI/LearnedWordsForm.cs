// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using Meltype.Composition;

namespace Meltype.UI;

/// <summary>
/// 学習した語の一覧 (トレイの「学習した語...」)。間違えて覚えた語 (go を英語として覚えて にほんgo になる など) を、
/// 学習データ全体をリセットせずに 1 語ずつ忘れさせる。
///   英語 / 日本語: 英字で打った語を英語・日本語のどちらで出すか (LanguageMemory)
///   変換: 読みに対して選び直した変換 (ConversionHistory: はし → 箸)
/// </summary>
internal sealed class LearnedWordsForm : Form
{
    private readonly LanguageMemory _languages;
    private readonly ConversionHistory _conversions;
    private readonly DataGridView _languageGrid = FormControls.ReadOnlyGrid();
    private readonly DataGridView _conversionGrid = FormControls.ReadOnlyGrid();
    private readonly TextBox _filter = new() { Width = 220, PlaceholderText = "絞り込み (語・読み)" };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    public LearnedWordsForm(LanguageMemory languages, ConversionHistory conversions)
    {
        _languages = languages;
        _conversions = conversions;
        Text = "Meltype 学習した語";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(620, 540);
        MinimumSize = new Size(460, 360);
        Font = new Font("Yu Gothic UI", 9.5F);

        _languageGrid.Columns.Add("word", "打った英字");
        _languageGrid.Columns.Add("language", "出し方");
        _languageGrid.Columns.Add("count", "回数");
        _languageGrid.Columns.Add("state", "状態");
        _languageGrid.Columns.Add("used", "最後に使った日");
        _conversionGrid.Columns.Add("reading", "読み");
        _conversionGrid.Columns.Add("text", "選んだ語");
        _conversionGrid.Columns.Add("used", "最後に使った日");

        var languagePage = new TabPage("英語 / 日本語");
        languagePage.Controls.Add(_languageGrid);
        languagePage.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
            ForeColor = SystemColors.GrayText,
            Text = "英字で打った語を、自動の判定ではなく覚えた方 (英語 / 日本語) で出します。間違えて覚えた語は、選んで「忘れる」を押してください。",
            MaximumSize = new Size(580, 0),
        });
        var conversionPage = new TabPage("変換");
        conversionPage.Controls.Add(_conversionGrid);
        conversionPage.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
            ForeColor = SystemColors.GrayText,
            Text = "変換で選び直した語は、次から最初の候補になります (はし → 箸)。",
            MaximumSize = new Size(580, 0),
        });
        _tabs.TabPages.Add(languagePage);
        _tabs.TabPages.Add(conversionPage);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        top.Controls.Add(_filter);
        _filter.TextChanged += (_, _) => Reload();

        var forget = new Button { Text = "選んだ語を忘れる", AutoSize = true };
        forget.Click += (_, _) => ForgetSelected();
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([close, forget]);
        _languageGrid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) ForgetSelected(); };
        _conversionGrid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) ForgetSelected(); };

        Controls.Add(_tabs);
        Controls.Add(top);
        Controls.Add(buttons);
        CancelButton = close;
        Reload();
    }

    private void Reload()
    {
        var filter = _filter.Text.Trim();
        _languageGrid.Rows.Clear();
        foreach (var entry in _languages.Entries().Where(e => filter.Length == 0 || e.Word.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var row = _languageGrid.Rows.Add(entry.Word, (entry.English ? "英語 (英字のまま)" : "日本語 (かな)") + (entry.Context is { } context ? $"・{context}" : ""), entry.Count,
                entry.Active ? "有効" : "もう一度で覚える", entry.Used.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            _languageGrid.Rows[row].Tag = entry.EntryKey;
        }
        _conversionGrid.Rows.Clear();
        foreach (var (reading, text, used) in _conversions.Entries().Where(e => filter.Length == 0 || e.Reading.Contains(filter) || e.Text.Contains(filter)))
        {
            var row = _conversionGrid.Rows.Add(reading, text, used.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            _conversionGrid.Rows[row].Tag = reading;
        }
        _tabs.TabPages[0].Text = $"英語 / 日本語 ({_languageGrid.Rows.Count})";
        _tabs.TabPages[1].Text = $"変換 ({_conversionGrid.Rows.Count})";
    }

    private void ForgetSelected()
    {
        var grid = _tabs.SelectedIndex == 0 ? _languageGrid : _conversionGrid;
        var keys = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (string)r.Tag!).ToList();
        if (keys.Count == 0) return;
        if (_tabs.SelectedIndex == 0) _languages.Remove(keys);
        else foreach (var reading in keys) _conversions.Forget(reading);
        Diagnostics.Log.Info($"学習した語を {keys.Count} 個忘れました。");
        Reload();
    }
}
