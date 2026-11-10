// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.UI;

/// <summary>
/// 使い方 (初めて起動したときと、トレイの「使い方...」)。半角/全角を押さずに打てること、困ったときのキーを説明し、
/// 下の欄で実際に Meltype キーボードで打って試せる。
/// </summary>
internal sealed class WelcomeForm : Form
{
    private static readonly Color Accent = Color.FromArgb(0, 120, 212);

    public WelcomeForm()
    {
        Text = "Meltype の使い方";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 10F);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(640, area.Width - 40), Math.Min(720, area.Height - 60));
        MinimumSize = new Size(480, 480);
        BackColor = SystemColors.Window;

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(20, 16, 20, 8),
        };
        var width = ClientSize.Width - 60;
        Label Heading(string text) => new() { Text = text, AutoSize = true, Font = new Font("Yu Gothic UI", 13F, FontStyle.Bold), ForeColor = Accent, Margin = new Padding(0, 10, 0, 4) };
        Label Paragraph(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, 6) };

        body.Controls.Add(new Label { Text = "ようこそ Meltype へ", AutoSize = true, Font = new Font("Yu Gothic UI", 18F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) });
        body.Controls.Add(Paragraph("半角/全角 を押さずに、日本語と英語をそのまま打てます。ローマ字で打った部分は日本語に、英単語は英字のままになります。"));

        body.Controls.Add(Heading("基本"));
        body.Controls.Add(KeyTable(width,
        [
            ("kyouhagoogledekensaku", "今日はgoogleで検索 (英単語だけ英字になる)"),
            ("Space", "変換 (英単語で終わっていれば、確定して空白)"),
            ("Enter", "確定"),
            ("← →", "変換する区切り (文節) を選ぶ"),
            ("Ctrl + 半角/全角", "Meltype を一時停止 / 再開"),
        ]));

        body.Controls.Add(Heading("思いどおりにならないとき"));
        body.Controls.Add(KeyTable(width,
        [
            ("F10", "英字にする (次からその語は英字。覚えた語は「学習した語...」で見られます)"),
            ("F6 / F7", "ひらがな / カタカナにする (次からその語は日本語)"),
            ("Shift + Space", "英字になった語を、ローマ字として変換する (go → 語)"),
            ("Esc", "変換を取り消す・打った文字を消す"),
            ("Ctrl + F7", "選んでいる語をユーザー辞書に登録する (読みは自動で入ります)"),
        ]));
        body.Controls.Add(Paragraph("候補で少し止まると、その語の意味が出ます (設定で OFF にできます)。"));

        body.Controls.Add(Heading("困ったら"));
        var last = Paragraph("画面右下のタスクトレイの Meltype のアイコン (あ / A) を右クリックすると、設定・ユーザー辞書 (ほかの日本語入力の辞書の取り込みもここ)・学習した語・不具合の報告があります。この画面はトレイの「使い方...」でまた開けます。");
        // 自動スクロールの枠は下の余白 (Padding) を含めないので、最後の段落の下に余白を取って見切れないようにする
        last.Margin = new Padding(0, 0, 0, 24);
        body.Controls.Add(last);

        var close = new Button { Text = "はじめる", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(12, 2, 12, 2) };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = SystemColors.Control };
        buttons.Controls.Add(close);

        // 「試してみる」はスクロールする本文の外に置く。本文の中だと、打っている途中にスクロールしたとき、
        // 欄だけが動いて変換ボックスが元の位置に残っていた
        var trial = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(20, 0, 20, 8),
        };
        var hint = Paragraph("下の欄をクリックして打ってみてください (例: kyouhameetinggaarimasu、nihongotoenglish)。");
        var practice = new TextBox { Width = width, Height = 70, Multiline = true, Font = new Font("Yu Gothic UI", 12F), Margin = new Padding(0) };
        trial.Controls.Add(Heading("試してみる"));
        trial.Controls.Add(hint);
        trial.Controls.Add(practice);
        // 本文と違って横にスクロールしないので、窓の幅に合わせる (狭めたときに右が切れないように)
        ClientSizeChanged += (_, _) =>
        {
            var inner = Math.Max(100, ClientSize.Width - trial.Padding.Horizontal);
            practice.Width = inner;
            hint.MaximumSize = new Size(inner, 0);
        };

        Controls.Add(body);
        Controls.Add(trial);
        Controls.Add(buttons);
        AcceptButton = close;
        ActiveControl = close;
    }

    // この画面では Meltype キーボードで打てるようにする (「試してみる」の欄。ほかの Meltype の画面では横取りしない)
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

    /// <summary>「キー | 説明」の表。</summary>
    private static TableLayoutPanel KeyTable(int width, (string Key, string Description)[] rows)
    {
        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = width, Margin = new Padding(0, 0, 0, 6) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (key, description) in rows)
        {
            table.Controls.Add(new Label
            {
                Text = key,
                AutoSize = true,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                BackColor = Color.FromArgb(238, 242, 248),
                Padding = new Padding(6, 3, 6, 3),
                Margin = new Padding(0, 2, 12, 2),
            });
            table.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(width - 200, 0), Margin = new Padding(0, 5, 0, 2) });
        }
        return table;
    }
}
