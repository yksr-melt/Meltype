// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

using Meltype.Config;

namespace Meltype.UI;

/// <summary>既定の行コメントは ON/OFF のみ、独自の行コメントは表で編集する。値の検証は Core が行う。</summary>
internal sealed class CodeSingleCommentsEditor : TableLayoutPanel
{
    private readonly ToggleSwitch _slash = new() { Text = "//", AccessibleName = "既定の //" };
    private readonly ToggleSwitch _hash = new() { Text = "#", AccessibleName = "既定の #" };
    private readonly ToggleSwitch _hyphen = new() { Text = "--", AccessibleName = "既定の --" };
    private readonly ToggleSwitch _rem = new() { Text = "REM", AccessibleName = "既定の REM" };
    private readonly DataGridView _custom = new()
    {
        Dock = DockStyle.Fill, Height = 160, RowHeadersWidth = 24,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        AllowUserToAddRows = false, AllowUserToDeleteRows = true,
        AccessibleName = "独自の行コメント",
    };
    private readonly Label _error = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.Firebrick, AccessibleRole = AccessibleRole.Alert };

    public CodeSingleCommentsEditor()
    {
        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Add(new Label { Text = "コードのコメント内で日本語を判定します。この設定はプロファイルごとに保存されます。", AutoSize = true, Dock = DockStyle.Fill });
        Add(new Label { Text = "既定の開始記号 (ON/OFF のみ):", AutoSize = true, Dock = DockStyle.Fill });
        var builtIn = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        builtIn.Controls.AddRange([_slash, _hash, _hyphen, _rem]);
        Add(builtIn);
        Add(new Label
        {
            Text = "REM は大文字・小文字を区別しません。行頭 (字下げ可) で、直後に半角スペースが必要です。",
            AutoSize = true, Dock = DockStyle.Fill,
        });
        Add(new Label
        {
            Text = "独自の開始記号: 空白を含まない半角の英数字・記号 (印字可能 ASCII)。大文字・小文字を区別します。既定の記号と同じものや重複は登録できません。コード内で一致した位置から行末までをコメントとして扱います。",
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 4),
        });
        _custom.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "開始記号 (例: %)", FillWeight = 75, MaxInputLength = int.MaxValue, SortMode = DataGridViewColumnSortMode.NotSortable });
        _custom.Columns.Add(new ToggleSwitchColumn("有効", 25) { SortMode = DataGridViewColumnSortMode.NotSortable });
        _custom.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_custom.IsCurrentCellDirty && _custom.CurrentCell is ToggleSwitchCell)
                _custom.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        Add(_custom);
        var add = new Button { Text = "追加", AutoSize = true };
        add.Click += (_, _) =>
        {
            var row = _custom.Rows.Add("", true);
            _custom.CurrentCell = _custom.Rows[row].Cells[0];
            _custom.Focus();
            _custom.BeginEdit(true);
        };
        var remove = new Button { Text = "選んだ行を削除", AutoSize = true };
        remove.Click += (_, _) =>
        {
            _custom.CancelEdit();
            foreach (var row in _custom.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow).OfType<DataGridViewRow>().Distinct().ToList())
                _custom.Rows.Remove(row);
        };
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        actions.Controls.AddRange([add, remove]);
        Add(actions);
        Add(_error);
    }

    private void Add(Control control)
    {
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(control, 0, RowCount++);
    }

    public void LoadValue(CodeSingleCommentSettings value)
    {
        _slash.Checked = value.BuiltIn.SlashSlash;
        _hash.Checked = value.BuiltIn.Hash;
        _hyphen.Checked = value.BuiltIn.DoubleHyphen;
        _rem.Checked = value.BuiltIn.Rem;
        _custom.CancelEdit();
        _custom.Rows.Clear();
        foreach (var entry in value.Custom) _custom.Rows.Add(entry.Token, entry.Enabled);
        ShowError("");
    }

    public CodeSingleCommentSettings ReadValue()
    {
        // 編集中のテキストも収集する。空行は捨てず、Core の検証でエラーにする。
        if (!_custom.EndEdit()) throw new CodeSingleCommentValidationException("行コメントの編集中の値を確定できませんでした。");
        var value = new CodeSingleCommentSettings
        {
            BuiltIn = new BuiltInCodeSingleComments { SlashSlash = _slash.Checked, Hash = _hash.Checked, DoubleHyphen = _hyphen.Checked, Rem = _rem.Checked },
            Custom = _custom.Rows.Cast<DataGridViewRow>().Select(row => new CodeSingleComment
            {
                Token = row.Cells[0].Value as string ?? "", Enabled = row.Cells[1].Value is true,
            }).ToList(),
        };
        Settings.ValidateCodeSingleComments(value);
        ShowError("");
        return value;
    }

    public void ShowError(string message) => _error.Text = message;
}
