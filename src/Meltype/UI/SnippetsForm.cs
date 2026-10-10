// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.UI;

/// <summary>
/// 定型文の登録・直す・消す (トレイの「定型文...」: issue #290)。;名前 + Space / Tab で、登録した文 (複数行でもよい) に置き換える。
/// 一覧で選ぶと下の欄に出るので、直して「登録」すれば上書きする。書き出し / 取り込みで、別の PC に移したりチームで配ったりできる。
/// </summary>
internal sealed class SnippetsForm : Form
{
    private readonly SnippetStore _store;
    private readonly Func<string> _mark;
    private readonly TextBox _name = new() { Width = 220 };
    private readonly TextBox _text = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 110 };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
    };
    private readonly Label _message = new() { AutoSize = true, ForeColor = Color.Firebrick, Padding = new Padding(0, 6, 0, 0) };

    public SnippetsForm(SnippetStore store, Func<string> mark)
    {
        _store = store;
        _mark = mark;
        Text = "Meltype 定型文";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(620, 580);
        MinimumSize = new Size(480, 420);
        Font = new Font("Yu Gothic UI", 9.5F);

        var add = new Button { Text = "登録", AutoSize = true };
        add.Click += (_, _) => Register();
        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(0, 0, 0, 4),
            Text = $"行の先頭か空白の直後に {MarkText()}名前 と打って Space / Tab を押すと、登録した文に置き換えます。直後に Esc で元に戻します。",
        };
        var entry = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entry.Controls.Add(hint, 0, 0);
        entry.SetColumnSpan(hint, 3);
        entry.Controls.Add(new Label { Text = "名前 (半角の英数字・記号)", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 1);
        entry.Controls.Add(_name, 1, 1);
        entry.Controls.Add(add, 2, 1);
        entry.Controls.Add(new Label { Text = "文 (複数行でもよい)", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 2);
        entry.Controls.Add(_text, 1, 2);
        entry.SetColumnSpan(_text, 2);
        entry.Controls.Add(_message, 1, 3);
        entry.SetColumnSpan(_message, 2);

        var remove = new Button { Text = "選んだ定型文を削除", AutoSize = true };
        remove.Click += (_, _) => RemoveSelected();
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        var import = new Button { Text = "取り込む...", AutoSize = true };
        import.Click += (_, _) => Import();
        var export = new Button { Text = "書き出す...", AutoSize = true };
        export.Click += (_, _) => Export();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([close, remove, export, import]);

        _grid.Columns.Add("name", "名前");
        _grid.Columns.Add("text", "文");
        _grid.Columns[0].FillWeight = 25;
        _grid.SelectionChanged += (_, _) =>
        {
            if (_grid.SelectedRows is [{ Tag: (string name, string text) }])
            {
                _name.Text = name;
                _text.Text = text.ReplaceLineEndings("\r\n");
            }
        };
        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        gridPanel.Controls.Add(_grid);

        Controls.Add(gridPanel);
        Controls.Add(entry);
        Controls.Add(buttons);
        CancelButton = close;
        Reload();
    }

    private string MarkText() => _mark() is { Length: > 0 } mark ? mark : ";";

    private void Register()
    {
        var name = _name.Text.Trim();
        if (name.StartsWith(MarkText(), StringComparison.Ordinal)) name = name[MarkText().Length..];
        if (!SnippetStore.IsValidName(name))
        {
            _message.Text = "名前は、空白と ; を使わない半角の英数字・記号にしてください (例: sig、ty、pr-template)。";
            return;
        }
        if (_text.Text.Length == 0)
        {
            _message.Text = "文を入れてください。";
            return;
        }
        var replaced = _store.Get(name) is not null;
        _store.Set(name, _text.Text);
        _message.Text = "";
        Diagnostics.Log.Info($"定型文を{(replaced ? "直しました" : "登録しました")} ({name.Length} 文字の名前)。");
        Reload();
        _name.Clear();
        _text.Clear();
        _name.Focus();
    }

    private void RemoveSelected()
    {
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            if (row.Tag is (string name, string _)) _store.Remove(name);
        }
        Reload();
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog { Title = "定型文を取り込む", Filter = "定型文 (*.json)|*.json|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var count = _store.Import(File.ReadAllText(dialog.FileName));
            Reload();
            MessageBox.Show(this, $"{count} 件取り込みました (同じ名前は取り込んだ方にしました)。", "定型文の取り込み", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"取り込めませんでした。\n\n{ex.Message}", "定型文の取り込み", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog { Title = "定型文を書き出す", Filter = "定型文 (*.json)|*.json", FileName = $"Meltype-定型文-{DateTime.Now:yyyyMMdd}.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, _store.Export());
            MessageBox.Show(this, $"{_store.Count} 件書き出しました。", "定型文の書き出し", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"書き出せませんでした。\n\n{ex.Message}", "定型文の書き出し", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        foreach (var (name, text) in _store.Entries())
        {
            var index = _grid.Rows.Add(MarkText() + name, text.Replace("\n", " ⏎ "));
            _grid.Rows[index].Tag = (name, text);
        }
    }
}
