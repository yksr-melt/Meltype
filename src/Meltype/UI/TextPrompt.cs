// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.UI;

/// <summary>名前などを 1 行入力してもらう小さな画面 (プロファイルの名前など)。</summary>
internal static class TextPrompt
{
    /// <summary>入力された文字列 (前後の空白は除く)。キャンセルなら null。</summary>
    public static string? Ask(IWin32Window owner, string title, string message, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = new Font("Yu Gothic UI", 9.5F),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        // プロファイルの名前などは日本語で付けることがあるので、Meltype キーボードで打てるようにする
        Input.ForegroundTracker.AllowTyping(form);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        var label = new Label { Text = message, AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(0, 0, 0, 6) };
        var input = new TextBox { Text = initial, Width = 360, Margin = new Padding(0, 0, 0, 10) };
        var ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "キャンセル", Width = 90, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        buttons.Controls.AddRange([cancel, ok]);
        layout.Controls.Add(label);
        layout.Controls.Add(input);
        layout.Controls.Add(buttons);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => input.SelectAll();
        return form.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
    }
}
