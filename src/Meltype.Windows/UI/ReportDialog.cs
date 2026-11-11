// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;

namespace Meltype.UI;

/// <summary>
/// 不具合の報告・提案 (トレイの「不具合の報告・提案...」)。フォームに入れる実行環境と、貼り付けてもらう最近のログを見せる。
/// ログは中身を確かめて (見られて困る部分は消して) からコピーしてもらう。実行環境はフォームを開くときに自動で入る。
/// </summary>
internal sealed class ReportDialog : Form
{
    private readonly TextBox _log;
    private readonly string _environment;

    public ReportDialog(Config.Settings settings)
    {
        Text = "Meltype 不具合の報告・提案";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9.5F);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(720, area.Width - 40), Math.Min(620, area.Height - 60));
        MinimumSize = new Size(480, 420);
        _environment = Diagnostics.ReportInfo.Environment(settings);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10, 10, 10, 6),
            MaximumSize = new Size(Width - 40, 0),
            Text = (Config.ProjectInfo.HasReportForm
                    ? "「フォームで報告」を押すと、報告のフォームがブラウザーで開きます (改善の提案もここから送れます)。下の実行環境は自動でフォームに入ります。\n"
                    : "「GitHub で不具合を報告」を押すと、GitHub の不具合の報告の画面がブラウザーで開きます (改善の提案は下のリンクから)。下の実行環境と版は自動で入ります。\n")
                + "ログは「ログをコピー」を押して、フォームの「ログ」の欄に貼り付けてください。見られて困る部分があれば、ここで消してからコピーしてください。",
        };

        var environment = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = _environment.Replace("\n", "\r\n") };
        var environmentBox = new GroupBox { Dock = DockStyle.Top, Height = 150, Text = "実行環境 (自動でフォームに入ります)", Padding = new Padding(8) };
        environmentBox.Controls.Add(environment);

        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9F),
            Text = Diagnostics.ReportInfo.RecentLog().Replace("\n", "\r\n"),
        };
        var logBox = new GroupBox { Dock = DockStyle.Fill, Text = "最近のログ (編集できます)", Padding = new Padding(8) };
        logBox.Controls.Add(_log);

        var reportBox = new GroupBox { Dock = DockStyle.Bottom, Height = 150, Text = "報告する", Padding = new Padding(8) };

        var copy = new Button { Text = "ログをコピー", AutoSize = true };
        // フォームが無いときは GitHub の「不具合の報告」のひな形を、実行環境などを入れて開く (issue #877)
        var open = new Button { Text = Config.ProjectInfo.HasReportForm ? "フォームで報告 (おすすめ・アカウント不要)" : "GitHub で不具合を報告", AutoSize = true };
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        var copied = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 8, 0, 0) };
        copy.Click += (_, _) =>
        {
            try
            {
                if (_log.TextLength == 0) return;
                Clipboard.SetText(_log.Text);
                copied.Text = "コピーしました";
            }
            catch (Exception ex)
            {
                copied.Text = "コピーできませんでした";
                Diagnostics.Log.Warn($"ログをコピーできませんでした: {ex.Message}");
            }
        };
        open.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.ReportUrl(_environment)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"報告の画面を開けませんでした: {ex.Message}");
            }
        };

        // GitHub のアカウントがある人向け (返事や修正の通知が届く)。公開 (1.0.0) まではリポジトリが非公開なので出さない。
        if (AppInfo.IsPublicRelease) reportBox.Controls.Add(GitHubLinks());
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6), Margin = new Padding(0, 4, 0, 0) };
        buttons.Controls.AddRange(close, open, copy, copied);
        reportBox.Controls.Add(buttons);

        Controls.Add(logBox);
        Controls.Add(environmentBox);
        Controls.Add(intro);
        Controls.Add(reportBox);
        CancelButton = close;
    }

    /// <summary>「GitHub で報告: 不具合 / 変換・判定の間違い / 改善の提案」のリンク。</summary>
    private LinkLabel GitHubLinks()
    {
        const string prefix = "GitHub のアカウントがある人は GitHub で報告 (返事や修正の通知が届きます)\n報告リンク: ";
        (string Text, string Template)[] kinds = [("不具合", "1-bug.yml"), ("変換・判定の間違い", "2-misdetection.yml"), ("改善の提案", "4-idea.yml")];
        var link = new LinkLabel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 4, 10, 4), Text = prefix + string.Join(" / ", kinds.Select(k => k.Text)) };
        link.Links.Clear();
        var start = prefix.Length;
        foreach (var (text, template) in kinds)
        {
            link.Links.Add(start, text.Length, template);
            start += text.Length + 3;
        }
        link.LinkClicked += (_, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.GitHubReportUrl((string)e.Link!.LinkData!, _environment)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"GitHub を開けませんでした: {ex.Message}");
            }
        };
        return link;
    }
}
