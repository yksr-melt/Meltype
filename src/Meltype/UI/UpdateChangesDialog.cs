// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;
using Meltype.Composition;

namespace Meltype.UI;

/// <summary>
/// 更新の前に「あなたの打ち方で変わる語」を見せる (issue #285)。自分で英字 / かなに直した語を、今の版と新しい版で打ってみて、
/// 見え方が変わる語だけを出す。「今のまま覚えておく」なら、その語を今の見え方で学習に入れてから更新する。
/// </summary>
internal sealed class UpdateChangesDialog : Form
{
    /// <summary>
    /// 変わる語を調べて、あれば見せる。更新してよければ true (「あとで」なら false)。
    /// 調べられない (新しい版がこの仕組みを持たない) ・変わる語が無いときは、何も見せずに true。
    /// </summary>
    public static bool Confirm(LanguageMemory languages, string version, Config.Settings settings)
    {
        if (Updater.PreviewChanges(languages.Entries().Select(e => e.Word)) is not { Count: > 0 } changes) return true;
        using var dialog = new UpdateChangesDialog(changes, version, Diagnostics.ReportInfo.Environment(settings));
        switch (dialog.ShowDialog())
        {
            case DialogResult.Yes:
                foreach (var change in changes) languages.Remember(change.Word, english: change.NowEnglish, explicitChoice: true);
                Diagnostics.Log.Info($"更新で変わる {changes.Count} 語を、今の見え方で覚えました。");
                return true;
            case DialogResult.No:
                return true;
            default:
                return false;
        }
    }

    private UpdateChangesDialog(IReadOnlyList<WordChange> changes, string version, string environment)
    {
        Text = "Meltype の更新";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9.5F);
        Size = new Size(560, 460);
        MinimumSize = new Size(440, 340);
        TopMost = true;

        var intro = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Padding = new Padding(10, 10, 10, 6),
            Text = $"Meltype {version} に更新すると、あなたが自分で英字 / かなに直したことのある語のうち {changes.Count} 語の、自動の判定が変わります。\n"
                + "「今のまま覚えておく」を選ぶと、これらの語を今の見え方で覚えてから更新します (この PC の中で調べただけで、何も送っていません)。",
        };
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        list.Columns.Add("打った英字", 140);
        list.Columns.Add("今", 150);
        list.Columns.Add($"{version}", 150);
        foreach (var change in changes) list.Items.Add(new ListViewItem([change.Word, change.Now, change.Next]));
        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
        listPanel.Controls.Add(list);

        var keep = new Button { Text = "今のまま覚えて更新", AutoSize = true, DialogResult = DialogResult.Yes };
        var update = new Button { Text = "そのまま更新", AutoSize = true, DialogResult = DialogResult.No };
        var later = new Button { Text = "あとで", AutoSize = true, DialogResult = DialogResult.Cancel };
        var report = new Button { Text = "報告する...", AutoSize = true };
        report.Click += (_, _) => Report(changes, version, environment);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([later, update, keep, report]);

        Controls.Add(listPanel);
        Controls.Add(intro);
        Controls.Add(buttons);
        AcceptButton = keep;
        CancelButton = later;
    }

    /// <summary>変わる語をコピーして、「変換・判定の間違い」の報告の画面を開く (新しい版で何が崩れたかを、開発側が更新したその日に知れるように)。</summary>
    private static void Report(IReadOnlyList<WordChange> changes, string version, string environment)
    {
        var text = $"Meltype {AppInfo.Version} → {version} の更新で変わる語:\n" + string.Join("\n", changes.Select(c => $"- {c.Word}: {c.Now} → {c.Next}"));
        try
        {
            Clipboard.SetText(text);
            Process.Start(new ProcessStartInfo(AppInfo.GitHubReportUrl("2-misdetection.yml", environment)) { UseShellExecute = true });
            MessageBox.Show("変わる語をコピーしました。開いた報告の画面の「前後の文」などに貼り付けてください。", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"報告の画面を開けませんでした: {ex.Message}");
        }
    }
}
