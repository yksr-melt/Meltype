// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.UI;

/// <summary>
/// ユーザー辞書の登録・削除。読みはローマ字で打ってもよい (kigoutou → きごうとう)。
/// 読みを打つと変換候補がプルダウンに出るので、そこから選ぶか、単語欄に直接打って登録する
/// (この画面では Meltype キーボードが使える)。
/// </summary>
internal sealed class UserDictionaryForm : Form
{
    private readonly CompositionService _service;
    private readonly TextBox _reading = new() { Width = 220 };
    private readonly Label _readingPreview = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 6, 0, 0) };
    private readonly ComboBox _word = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly DataGridView _grid = FormControls.ReadOnlyGrid();
    private readonly Label _message = FormControls.ErrorLabel();
    private readonly System.Windows.Forms.Timer _suggestTimer = new() { Interval = 300 };

    public UserDictionaryForm(CompositionService service)
    {
        _service = service;
        Text = "Meltype ユーザー辞書";
        StartPosition = FormStartPosition.CenterScreen;
        // 文字は画面の拡大率に合わせて大きくなるので、窓の大きさも拡大率に合わせる。
        Size = LogicalToDeviceUnits(new Size(560, 520));
        MinimumSize = new Size(460, 360);
        Font = new Font("Yu Gothic UI", 9.5F);

        var add = new Button { Text = "登録", AutoSize = true };
        add.Click += (_, _) => Register();
        var entry = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        entry.Controls.Add(new Label { Text = "読み (ローマ字・ひらがな)", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 0);
        entry.Controls.Add(_reading, 1, 0);
        entry.Controls.Add(_readingPreview, 2, 0);
        entry.Controls.Add(new Label { Text = "単語 (候補から選ぶか直接入力)", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }, 0, 1);
        entry.Controls.Add(_word, 1, 1);
        entry.Controls.Add(add, 2, 1);
        entry.Controls.Add(_message, 1, 2);
        entry.SetColumnSpan(_message, 2);

        var remove = new Button { Text = "選んだ語を削除", AutoSize = true };
        remove.Click += (_, _) => RemoveSelected();
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        // ほかの日本語入力 (Microsoft IME・Google 日本語入力) の辞書の取り込みと、Microsoft IME の形式での書き出し
        var import = new Button { Text = "取り込む...", AutoSize = true };
        import.Click += (_, _) => Import();
        var export = new Button { Text = "書き出す...", AutoSize = true };
        export.Click += (_, _) => Export();
        buttons.Controls.AddRange([close, remove, export, import]);

        _grid.Columns.Add("reading", "読み");
        _grid.Columns.Add("word", "単語");
        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        gridPanel.Controls.Add(_grid);

        Controls.Add(gridPanel);
        Controls.Add(entry);
        Controls.Add(buttons);
        AcceptButton = add;
        CancelButton = close;

        _reading.TextChanged += (_, _) =>
        {
            _readingPreview.Text = _reading.Text.Any(char.IsAsciiLetter) ? "→ " + _service.ToReading(_reading.Text) : "";
            _suggestTimer.Stop();
            _suggestTimer.Start();
        };
        _suggestTimer.Tick += (_, _) =>
        {
            _suggestTimer.Stop();
            Suggest();
        };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) RemoveSelected();
        };
        Reload();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ForegroundTracker.TypingAllowedWindows[Handle] = true;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ForegroundTracker.TypingAllowedWindows.TryRemove(Handle, out _);
        base.OnHandleDestroyed(e);
    }

    private void Suggest()
    {
        var reading = _service.ToReading(_reading.Text);
        var typed = _word.Text;
        _word.Items.Clear();
        if (reading.Length == 0) return;
        foreach (var word in _service.SuggestWords(reading)) _word.Items.Add(word);
        if (typed.Length == 0 && _word.Items.Count > 0) _word.SelectedIndex = 0;
    }

    private void Register()
    {
        var reading = _service.ToReading(_reading.Text);
        var error = _service.UserDictionary.Add(reading, _word.Text);
        if (error is not null)
        {
            _message.Text = error;
            return;
        }
        _message.Text = "";
        Diagnostics.Log.Info($"ユーザー辞書に登録しました: {Diagnostics.Log.Text(reading)} → {Diagnostics.Log.Text(_word.Text.Trim())}");
        _reading.Clear();
        _word.Text = "";
        _word.Items.Clear();
        Reload();
        _reading.Focus();
    }

    /// <summary>選んでいた語を入れて開く (Ctrl+F7)。読みは推測したもの (直して登録できる)。</summary>
    public void Prefill(string word, string reading)
    {
        _word.Text = word;
        _reading.Text = reading;
        _message.Text = reading.Length == 0 ? "読みを入力してください。" : "読みを確かめて「登録」を押してください。";
        _message.ForeColor = reading.Length == 0 ? Color.Firebrick : SystemColors.GrayText;
        Activate();
        _reading.Focus();
        _reading.SelectAll();
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "ユーザー辞書を取り込む",
            Filter = "辞書のテキストファイル (*.txt)|*.txt|macOS のユーザ辞書 (*.plist)|*.plist|すべてのファイル (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var result = UserDictionaryFile.Parse(File.ReadAllBytes(dialog.FileName));
            var added = _service.UserDictionary.AddRange(result.Words);
            Reload();
            Diagnostics.Log.Info($"ユーザー辞書を取り込みました: {added} 語 ({result.Encoding})");
            var skipped = result.Skipped > 0 ? $"\n読みがかなでない・短すぎるなどで飛ばした行: {result.Skipped}" : "";
            var duplicates = result.Words.Count - added;
            MessageBox.Show(this, $"{added} 語を登録しました。{(duplicates > 0 ? $"\n登録済みの語: {duplicates}" : "")}{skipped}", "ユーザー辞書の取り込み",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"取り込めませんでした。\n\n{ex.Message}", "ユーザー辞書の取り込み", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "ユーザー辞書を書き出す",
            Filter = "辞書のテキストファイル (*.txt)|*.txt",
            FileName = $"Meltype-ユーザー辞書-{DateTime.Now:yyyyMMdd}.txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, UserDictionaryFile.Export(_service.UserDictionary.Words));
            MessageBox.Show(this, $"{_service.UserDictionary.Count} 語を書き出しました。\nMicrosoft IME・Google 日本語入力・ATOK の辞書ツールで取り込めます。", "ユーザー辞書の書き出し",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"書き出せませんでした。\n\n{ex.Message}", "ユーザー辞書の書き出し", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveSelected()
    {
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            if (row.Tag is UserWord word) _service.UserDictionary.Remove(word);
        }
        Reload();
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        foreach (var word in _service.UserDictionary.Words.Reverse())
        {
            var index = _grid.Rows.Add(word.Reading, word.Word);
            _grid.Rows[index].Tag = word;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _suggestTimer.Dispose();
        base.Dispose(disposing);
    }
}
