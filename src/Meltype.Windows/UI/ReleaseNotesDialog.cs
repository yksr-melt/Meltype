// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;

namespace Meltype.UI;

/// <summary>
/// リリースノートを見せる (issue #1207)。更新したあとの初めての起動で一度だけ出し、設定アプリの「更新」→「リリースノート」からも開ける。
/// 中身は配布物に同梱したその版のリリースノート (docs/release-notes/&lt;版&gt;.md。GitHub のリリースにも同じ本文を載せる)。
/// 無ければ (開発中のビルド など) リリースのページへ案内する。読めなくても Meltype の動きには関係しない。
/// </summary>
internal sealed class ReleaseNotesDialog : Form
{
    private static ReleaseNotesDialog? _open;

    /// <summary>その版のリリースノートを出す (もう出ていれば前に出す)。updated なら「更新しました」と書く。</summary>
    public static void ShowFor(string version, bool updated, MeltypeEngine engine)
    {
        try
        {
            if (_open is { IsDisposed: false })
            {
                _open.Activate();
                return;
            }
            _open = new ReleaseNotesDialog(version, updated, Bundled(version), engine.Settings.ShowReleaseNotesAfterUpdate, showAfterUpdate =>
            {
                // チェックは画面の操作から直接呼ばれるので、設定の保存や反映で例外が出ても画面を壊さない
                try
                {
                    if (engine.Settings.ShowReleaseNotesAfterUpdate == showAfterUpdate) return;
                    engine.EditSettings(settings => settings.ShowReleaseNotesAfterUpdate = showAfterUpdate);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log.Warn($"リリースノートの自動表示の設定を変えられませんでした: {ex.Message}");
                }
            });
            _open.Show();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"リリースノートを出せませんでした: {ex.Message}");
        }
    }

    internal ReleaseNotesDialog(string version, bool updated, string? notes, bool showAfterUpdate = true, Action<bool>? setShowAfterUpdate = null)
    {
        Text = $"Meltype {version} のリリースノート";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9.5F);
        Size = new Size(LogicalToDeviceUnits(600), LogicalToDeviceUnits(500));
        MinimumSize = new Size(400, 300);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(560), 0),
            Padding = new Padding(10, 10, 10, 6),
            Text = (updated ? $"Meltype を {version} に更新しました。" : $"Meltype {version} です。")
                + (notes is null ? "\nこの版のリリースノートは、GitHub のリリースのページで見られます。" : "\n新しい機能・直した不具合など、この版で変わったことです。"),
        };
        var text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BackColor = SystemColors.Window,
            DetectUrls = false,
        };
        var textPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0), Visible = notes is not null };
        textPanel.Controls.Add(text);

        // 次に更新したときから自動で出さない (設定の「更新後にリリースノートを表示する」と同じ)
        var dontShow = new CheckBox { Text = "今後表示しない", AutoSize = true, Checked = !showAfterUpdate };
        dontShow.CheckedChanged += (_, _) => setShowAfterUpdate?.Invoke(!dontShow.Checked);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10, 0, 10, 0) };
        footer.Controls.Add(dontShow);

        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.OK };
        close.Click += (_, _) => Close();
        var page = new Button { Text = "GitHub で見る", AutoSize = true };
        page.Click += (_, _) => Open(Config.ProjectInfo.ReleaseUrl(version));
        var all = new Button { Text = "過去のリリースノート...", AutoSize = true };
        all.Click += (_, _) => Open(Config.ProjectInfo.ReleasesUrl);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([close, page, all]);

        Controls.Add(textPanel);
        Controls.Add(intro);
        Controls.Add(footer);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;
        // WinForms の TextBox は、選択範囲をコードで指定していないと最初のフォーカスで中身を全選択する (TextBox.OnGotFocus)。
        // 本文は RichTextBox に替えたので全選択は起きないが、開いたときは「閉じる」にフォーカスを置く。
        ActiveControl = close;
        Shown += (_, _) =>
        {
            // 書式は RichTextBox のハンドルができてからでないと付かないので、表示するときに描く
            if (notes is not null && text.TextLength == 0) Render(text, notes);
            text.Select(0, 0);
            close.Focus();
        };
        if (notes is null) Height = intro.PreferredHeight + footer.PreferredSize.Height + buttons.PreferredSize.Height + LogicalToDeviceUnits(60);
    }

    /// <summary>見出しは大きく太く、強調は太字、インラインコードは等幅で出す。リンクは文字だけ出す。</summary>
    private static void Render(RichTextBox text, string markdown)
    {
        var baseFont = text.Font;
        var first = true;
        foreach (var line in Config.ReleaseNotesMarkdown.Parse(markdown))
        {
            if (!first) text.AppendText("\n");
            first = false;
            var heading = line.Kind == Config.ReleaseNotesMarkdown.LineKind.Heading;
            var size = heading
                ? line.HeadingLevel switch { 1 => 15F, 2 => 12F, 3 => 10.5F, _ => baseFont.Size }
                : baseFont.Size;
            if (line.Kind == Config.ReleaseNotesMarkdown.LineKind.Bullet)
            {
                text.SelectionFont = baseFont;
                text.AppendText(new string(' ', line.Indent) + "・");
            }
            foreach (var span in line.Spans)
            {
                using var font = new Font(span.Code ? "Consolas" : baseFont.FontFamily.Name, size,
                    span.Bold || heading ? FontStyle.Bold : FontStyle.Regular);
                text.SelectionFont = font;
                text.AppendText(span.Text);
            }
        }
        text.Select(0, 0);
    }

    /// <summary>配布物に同梱したリリースノート (無い・読めなければ null)。</summary>
    private static string? Bundled(string version)
    {
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "release-notes", $"{version}.md");
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"リリースノートを読めませんでした: {ex.Message}");
            return null;
        }
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"リリースのページを開けませんでした: {ex.Message}");
        }
    }
}
