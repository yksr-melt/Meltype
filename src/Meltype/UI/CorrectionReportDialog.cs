// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;
using Meltype.Composition;

namespace Meltype.UI;

/// <summary>
/// 自分で直した誤判定を、1 件の報告にする (トレイの「直した誤判定を報告...」: issue #284)。
/// 最近直したもの (F6〜F10・変換の候補で英字 / かなを選び直したもの) を一覧にし、選んだものの中身を全部見せる。
/// 個人的な文章は似た例に書き換えてから、「変換・判定の間違い」のひな形に入れた GitHub の画面を開く。自動では何も送らない。
/// </summary>
internal sealed class CorrectionReportDialog : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _typed = new() { Dock = DockStyle.Fill };
    private readonly TextBox _shown = new() { Dock = DockStyle.Fill };
    private readonly TextBox _expected = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _key = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _before = new() { Dock = DockStyle.Fill };
    private readonly string _environment;
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 8, 0, 0) };

    public CorrectionReportDialog(CorrectionLog log, Config.Settings settings)
    {
        Text = "Meltype 直した誤判定を報告";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9.5F);
        Size = new Size(640, 520);
        MinimumSize = new Size(480, 420);
        _environment = Diagnostics.ReportInfo.Environment(settings);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            Padding = new Padding(10, 10, 10, 6),
            Text = "最近、自分で直した判定です (Meltype を終了すると消えます。どこにも送っていません)。\n"
                + "選んで中身を確かめ、見られて困る文は似た例に書き換えてから「GitHub で報告」を押すと、「変換・判定の間違い」の報告の画面が開きます。",
        };

        _key.Items.AddRange(["Space (変換)", "Enter (確定)", "打っている途中 (まだ確定していない)", "句読点・記号", "そのほか"]);
        var fields = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(10, 6, 10, 6) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(string label, Control control)
        {
            fields.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
            fields.Controls.Add(control);
        }
        Add("打ったもの (キーをそのまま)", _typed);
        Add("出たもの", _shown);
        Add("期待した結果", _expected);
        Add("最後に押したキー", _key);
        Add("前後の文 (分かれば)", _before);

        var open = new Button { Text = "GitHub で報告", AutoSize = true };
        var copy = new Button { Text = "コピー", AutoSize = true };
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        open.Click += (_, _) => Open();
        copy.Click += (_, _) => Copy();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([close, open, copy, _status]);

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
        listPanel.Controls.Add(_list);
        Controls.Add(listPanel);
        Controls.Add(intro);
        Controls.Add(fields);
        Controls.Add(buttons);
        CancelButton = close;

        foreach (var correction in log.Recent()) _list.Items.Add(correction);
        _list.Format += (_, e) =>
        {
            if (e.ListItem is Correction c) e.Value = $"{c.Time:HH:mm}  {c.Typed}  : {c.Shown} → {c.Corrected}";
        };
        _list.SelectedIndexChanged += (_, _) => ShowCorrection(_list.SelectedItem as Correction);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else _status.Text = "まだ直した判定がありません";
        open.Enabled = copy.Enabled = _list.Items.Count > 0;
    }

    private void ShowCorrection(Correction? correction)
    {
        if (correction is null) return;
        _typed.Text = correction.Typed;
        _shown.Text = correction.Shown;
        _expected.Text = correction.Corrected;
        _key.SelectedItem = correction.LastKey;
        _before.Text = correction.Before;
        _status.Text = "";
    }

    /// <summary>画面で書き換えた中身。</summary>
    private Correction Current() =>
        new(_typed.Text.Trim(), _shown.Text, _expected.Text, _key.SelectedItem as string ?? "そのほか", _before.Text, DateTime.Now);

    private void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppInfo.MisdetectionReportUrl(Current(), _environment)) { UseShellExecute = true });
            _status.Text = "報告の画面を開きました";
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"報告の画面を開けませんでした: {ex.Message}");
        }
    }

    private void Copy()
    {
        try
        {
            Clipboard.SetText(Current().ToReportText());
            _status.Text = "コピーしました";
        }
        catch (Exception ex)
        {
            _status.Text = "コピーできませんでした";
            Diagnostics.Log.Warn($"直した誤判定をコピーできませんでした: {ex.Message}");
        }
    }
}
